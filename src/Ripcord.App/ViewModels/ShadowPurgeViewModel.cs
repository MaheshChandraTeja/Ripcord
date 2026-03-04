#nullable enable
using Microsoft.UI.Dispatching;
using Ripcord.Engine.Vss;
using Ripcord.Engine.Journal;
using Ripcord.Engine.Common;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Ripcord.App.ViewModels
{
    public sealed class ShadowPurgeViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ShadowItem> Snapshots { get; } = new();

        private string _volumeRoot = @"C:\";
        public string VolumeRoot { get => _volumeRoot; set { if (Set(ref _volumeRoot, value)) _ = RefreshAsync(); } }

        private bool _includeClientAccessible = true;
        public bool IncludeClientAccessible { get => _includeClientAccessible; set => Set(ref _includeClientAccessible, value); }

        private int _olderThanDays = 0;
        public int OlderThanDays { get => _olderThanDays; set => Set(ref _olderThanDays, value); }

        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set => Set(ref _dryRun, value); }

        public string Summary => $"{Snapshots.Count} snapshot(s) listed";
        public string Footer => DryRun ? "Dry run is ON (no deletions will be performed)." : "Dry run is OFF (deletions are permanent).";

        public bool CanPurge => Snapshots.Any();

        public ICommand RefreshCommand { get; }
        public ICommand PurgeCommand { get; }
        public ICommand DeleteOneCommand { get; }

        public ShadowPurgeViewModel()
        {
            RefreshCommand = new Relay(async _ => await RefreshAsync());
            PurgeCommand = new Relay(async _ => await PurgeAsync(), _ => CanPurge);
            DeleteOneCommand = new Relay(async o =>
            {
                if (o is ShadowItem item)
                    await DeleteSingleAsync(item);
            });
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            await Task.Run(() =>
            {
                var list = ShadowEnumerator.EnumerateByVolume(VolumeRoot);
                AppDispatch(() =>
                {
                    Snapshots.Clear();
                    foreach (var s in list)
                        Snapshots.Add(ShadowItem.From(s));
                    Raise(nameof(Summary));
                    Raise(nameof(CanPurge));
                });
            });
        }

        private async Task PurgeAsync()
        {
            var age = OlderThanDays > 0 ? TimeSpan.FromDays(OlderThanDays) : (TimeSpan?)null;
            var result = await ShadowPurger.PurgeVolumeAsync(VolumeRoot, olderThan: age, includeClientAccessible: IncludeClientAccessible, dryRun: DryRun);
            // Refresh view regardless
            await RefreshAsync();
        }

        private async Task DeleteSingleAsync(ShadowItem item)
        {
            // Use managed WMI purger (integrates with JobJournal) — single-shot
            var r = await Task.Run(() => ShadowPurger.DeleteById(item.Id, DryRun));
            await RefreshAsync();
        }

        // ------------- helpers -------------

        private static void AppDispatch(Action action)
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq is null || dq.HasThreadAccess) action();
            else dq.TryEnqueue(action);
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!Equals(field, value))
            {
                field = value;
                Raise(name!);
                return true;
            }
            return false;
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public event PropertyChangedEventHandler? PropertyChanged;

        public sealed class ShadowItem : INotifyPropertyChanged
        {
            public Guid Id { get; init; }
            public string VolumeName { get; init; } = "";
            public string DeviceObject { get; init; } = "";
            public DateTime? CreatedUtc { get; init; }
            public bool Persistent { get; init; }
            public bool ClientAccessible { get; init; }
            public uint State { get; init; }

            public string CreatedDisplay => CreatedUtc?.ToLocalTime().ToString("g") ?? "—";

            private bool _isSelected;
            public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; Raise(nameof(IsSelected)); } } }

            public static ShadowItem From(ShadowEnumerator.ShadowInfo s) => new()
            {
                Id = s.Id,
                VolumeName = s.VolumeName,
                DeviceObject = s.DeviceObject,
                CreatedUtc = s.InstallDateUtc,
                Persistent = s.Persistent,
                ClientAccessible = s.ClientAccessible,
                State = s.State
            };

            public event PropertyChangedEventHandler? PropertyChanged;
            private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private sealed class Relay : ICommand
        {
            private readonly Func<object?, Task> _executeAsync;
            private readonly Predicate<object?>? _can;
            public Relay(Func<object?, Task> executeAsync, Predicate<object?>? can = null) { _executeAsync = executeAsync; _can = can; }
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
            public async void Execute(object? parameter) => await _executeAsync(parameter);
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
