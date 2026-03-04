using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ripcord.App.ViewModels;

namespace Ripcord.App.Views
{
    public sealed partial class ReceiptsPage : Page
    {
        public ReceiptsPage()
        {
            this.InitializeComponent();
        }

        private async void OnBrowse(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WindowNative.GetWindowHandle((Application.Current as App)!.GetType()
                .GetProperty("m_window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .GetValue(Application.Current) as Window ?? this.XamlRoot.Content as Window);
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null && DataContext is ReceiptsViewModel vm)
            {
                vm.RootFolder = folder.Path;
            }
        }
    }
}
