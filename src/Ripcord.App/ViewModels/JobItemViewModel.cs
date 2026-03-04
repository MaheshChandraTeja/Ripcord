#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Ripcord.App.Services;
using Ripcord.Engine.Shred;

namespace Ripcord.App.ViewModels
{
    public sealed class JobQueueViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<JobItemViewModel> Items { get; } = new();

        private readonly JobQueueService _svc;

        public ICommand AddFileCommand { get; }
        public ICommand AddFolderCommand { get; }

        public JobQueueViewModel()
        {
            _svc = App.Services.GetRequiredService<JobQueueService>();
            _svc.Progress += (_, p) =>
            {
                if (p.CurrentFile is null) return;
                var vm = GetOrAdd(p.CurrentFile);
                vm.BytesProcessed = p.BytesProcessed ?? vm.BytesProcessed;
                vm.BytesTotal = p.BytesTotal ?? vm.BytesTotal;
                vm.Status = p.Status ?? vm.Status;
                vm.Percent = p.Percent ?? vm.Percent;
            };

            AddFileCommand = new Relay(async o =>
            {
                if (o is string path && File.Exists(path))
                {
                    var req = new ShredJobRequest(path, Profiles.Default(), DryRun: false, DeleteAfter: true, Recurse: false);
                    await _svc.EnqueueAsync(req);
                }
            });

            AddFolderCommand = new Relay(async o =>
            {
                if (o is string path && Directory.Exists(path))
                {
                    var req = new ShredJobRequest(path, Profiles.Default(), DryRun: false, DeleteAfter: true, Recurse: true);
                    await _svc.EnqueueAsync(req);
                }
            });
        }

        private JobItemViewModel GetOrAdd(string path)
        {
            foreach (var i in Items)
                if (string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))
                    return i;

            var vm = new JobItemViewModel { Path = path };
            Items.Add(vm);
            return vm;
        }

        // boilerplate
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private sealed class Relay : ICommand
        {
            private readonly Func<object?, Task> _exec;
            public Relay(Func<object?, Task> exec) { _exec = exec; }
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => true;
            public async void Execute(object? parameter) => await _exec(parameter);
        }
    }
}
