#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Ripcord.App.Services;
using Ripcord.Receipts.Export;
using Ripcord.Receipts.Model;
using Ripcord.Receipts.Verify;

namespace Ripcord.App.ViewModels
{
    public sealed class ReceiptsViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ReceiptItem> Items { get; } = new();

        private string _rootFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Receipts");
        public string RootFolder { get => _rootFolder; set { if (Set(ref _rootFolder, value)) _ = RefreshAsync(); } }

        private ReceiptItem? _selected;
        public ReceiptItem? Selected { get => _selected; set { if (Set(ref _selected, value)) Raise(nameof(CanExport)); Raise(nameof(CanVerify)); } }

        public bool CanExport => Selected is not null;
        public bool CanVerify => Selected is not null;

        public string Footer => $"{Items.Count} receipt(s) in {RootFolder}";

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand VerifyCommand { get; }

        private readonly ReceiptStore _store = new();

        public ReceiptsViewModel()
        {
            RefreshCommand = new Relay(async _ => await RefreshAsync());
            ExportCommand  = new Relay(async _ => await ExportAsync(), _ => CanExport);
            VerifyCommand  = new Relay(async _ => await VerifyAsync(), _ => CanVerify);
            _ = RefreshAsync();
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            Directory.CreateDirectory(RootFolder);
            Items.Clear();
            foreach (var rec in await _store.ListAsync(RootFolder))
                Items.Add(rec);
            Raise(nameof(Footer));
        }

        private async System.Threading.Tasks.Task ExportAsync()
        {
            if (Selected is null) return;

            string reportsDir = Path.Combine(RootFolder, "exports");
            string templates = Path.Combine(AppContext.BaseDirectory, "reports", "templates");
            Directory.CreateDirectory(reportsDir);

            var receipt = await _store.LoadReceiptAsync(Selected.ReceiptPath);
            var att = Selected.AttestationPath is not null ? await _store.LoadAttestationAsync(Selected.AttestationPath) : null;

            var pdf = await ReceiptPdfExporter.ExportAsync(receipt, att,
                brandingJsonPath: Path.Combine(templates, "receipt.branding.json"),
                templateJsonPath: Path.Combine(templates, "receipt.pdf.template.json"),
                outputDirectory: reportsDir);

            Selected!.ExportedPath = pdf;
            Raise(nameof(Selected));
        }

        private async System.Threading.Tasks.Task VerifyAsync()
        {
            if (Selected is null) return;
            var receipt = await _store.LoadReceiptAsync(Selected.ReceiptPath);
            var att = Selected.AttestationPath is not null ? await _store.LoadAttestationAsync(Selected.AttestationPath) : null;

            var rep = ReceiptVerifier.Verify(receipt, att);
            Selected.Verified = rep.StructureOk && rep.HashOk && rep.SignatureOk;
            Selected.VerificationDetails = string.Join("; ", rep.Issues);
            Raise(nameof(Selected));
        }

        // ---- types & helpers ----
        public sealed class ReceiptItem : INotifyPropertyChanged
        {
            public string Title => FileName;
            public string Subtitle => Created?.ToLocalTime().ToString("u") ?? "—";
            public string Profile { get; init; } = "—";
            public string HashShort { get; init; } = "—";
            public bool? Verified { get; set; }
            public string VerifiedDisplay => Verified is null ? "—" : (Verified.Value ? "Verified" : "FAILED");
            public string FileName { get; init; } = "";
            public DateTimeOffset? Created { get; init; }
            public string ReceiptPath { get; init; } = "";
            public string? AttestationPath { get; init; }
            public string? ExportedPath { get; set; }
            public string? VerificationDetails { get; set; }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        private sealed class Relay : ICommand
        {
            private readonly Func<object?, System.Threading.Tasks.Task> _exec;
            private readonly Predicate<object?>? _can;
            public Relay(Func<object?, System.Threading.Tasks.Task> exec, Predicate<object?>? can = null) { _exec = exec; _can = can; }
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
            public async void Execute(object? parameter) => await _exec(parameter);
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!Equals(field, value)) { field = value; Raise(name!); return true; }
            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
