using Microsoft.UI.Dispatching;
using Ripcord.App.Services;
using Ripcord.Engine.Shred;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Ripcord.App.ViewModels
{
    public sealed class JobQueueViewModel : INotifyPropertyChanged
    {
        private readonly JobQueueService _service;
        private readonly DispatcherQueue _dispatcher;

        public ObservableCollection<JobItemViewModel> Jobs { get; } = new();

        public ReadOnlyObservableCollection<IShredProfile> Profiles { get; }
        private IShredProfile _selectedProfile;
        public IShredProfile SelectedProfile { get => _selectedProfile; set => Set(ref _selectedProfile, value); }

        private string _pathInput = string.Empty;
        public string PathInput { get => _pathInput; set => Set(ref _pathInput, value); }

        private bool _dryRun = true;
        public bool DryRun { get => _dryRun; set { if (Set(ref _dryRun, value)) _service.DryRun = value; } }

        public string FooterStatus => $"{Jobs.Count} jobs in list";

        public bool CanStart => Jobs.Any(j => j.Status is "Queued" or "Paused");
        public bool CanClearCompleted => Jobs.Any(j => j.Status is "Completed" or "Failed" or "Canceled");

        public ICommand AddPathCommand { get; }
        public ICommand StartQueueCommand { get; }
        public ICommand ClearCompletedCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public JobQueueViewModel(JobQueueService service, DispatcherQueue dispatcher)
        {
            _service = service;
            _dispatcher = dispatcher;

            var all = BuiltInProfiles.GetAll();
            Profiles = new ReadOnlyObservableCollection<IShredProfile>(new ObservableCollection<IShredProfile>(all));
            _selectedProfile = all.FirstOrDefault() ?? throw new InvalidOperationException("No profiles available");

            _service.JobAdded += OnJobAdded;
            _service.JobUpdated += OnJobUpdated;

            AddPathCommand = new RelayCommand(async _ =>
            {
                if (string.IsNullOrWhiteSpace(PathInput)) return;
                var id = await _service.EnqueueAsync(PathInput, SelectedProfile);
                PathInput = string.Empty;
            });

            StartQueueCommand = new RelayCommand(_ => _service.EnsureRunning(), _ => true);

            ClearCompletedCommand = new RelayCommand(_ =>
            {
                for (int i = Jobs.Count - 1; i >= 0; i--)
                {
                    if (Jobs[i].Status is "Completed" or "Failed" or "Canceled")
                        Jobs.RemoveAt(i);
                }
                Raise(nameof(FooterStatus));
                Raise(nameof(CanStart));
                Raise(nameof(CanClearCompleted));
            });
        }

        private void OnJobAdded(object? sender, JobQueueService.JobAddedEventArgs e)
        {
            _dispatcher.TryEnqueue(() =>
            {
                var vm = new JobItemViewModel(_service, e.Id, e.Path, e.Profile);
                Jobs.Add(vm);
                Raise(nameof(FooterStatus));
                Raise(nameof(CanStart));
                Raise(nameof(CanClearCompleted));
            });
        }

        private void OnJobUpdated(object? sender, JobQueueService.JobUpdate e)
        {
            _dispatcher.TryEnqueue(() =>
            {
                var vm = Jobs.FirstOrDefault(j => j.Id == e.Id);
                if (vm != null)
                {
                    vm.UpdateFrom(e);
                    Raise(nameof(CanStart));
                    Raise(nameof(CanClearCompleted));
                }
            });
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

        // Simple RelayCommand
        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;
            public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
            { _execute = execute; _canExecute = canExecute; }
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
