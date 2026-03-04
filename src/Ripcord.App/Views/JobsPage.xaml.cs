using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ripcord.App.ViewModels;

namespace Ripcord.App.Views
{
    public sealed partial class JobsPage : Page
    {
        public JobsPage()
        {
            this.InitializeComponent();
        }

        private async void OnAddFile(object sender, RoutedEventArgs e)
        {
            if (DataContext is JobQueueViewModel vm && !string.IsNullOrWhiteSpace(PathBox.Text))
                await ((System.Windows.Input.ICommand)vm.AddFileCommand).ExecuteAsync(PathBox.Text);
        }

        private async void OnAddFolder(object sender, RoutedEventArgs e)
        {
            if (DataContext is JobQueueViewModel vm && !string.IsNullOrWhiteSpace(PathBox.Text))
                await ((System.Windows.Input.ICommand)vm.AddFolderCommand).ExecuteAsync(PathBox.Text);
        }
    }

    internal static class CommandExtensions
    {
        public static System.Threading.Tasks.Task ExecuteAsync(this System.Windows.Input.ICommand cmd, object? param)
        {
            if (cmd is null) return System.Threading.Tasks.Task.CompletedTask;
            cmd.Execute(param);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
