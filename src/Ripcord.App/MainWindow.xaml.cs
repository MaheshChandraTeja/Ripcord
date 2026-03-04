using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ripcord.App.Views;

namespace Ripcord.App
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            // Default page
            ContentFrame.Navigate(typeof(JobsPage));
            Nav.SelectedItem = Nav.MenuItems[0];
        }

        private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem nvi)
            {
                switch ((nvi.Tag as string) ?? "")
                {
                    case "jobs": ContentFrame.Navigate(typeof(JobsPage)); break;
                    case "free": ContentFrame.Navigate(typeof(FreeSpacePage)); break;
                    case "vss": ContentFrame.Navigate(typeof(ShadowPurgePage)); break;
                    case "receipts": ContentFrame.Navigate(typeof(ReceiptsPage)); break;
                    case "settings": ContentFrame.Navigate(typeof(Ripcord.App.Views.SettingsPage)); break;
                }
            }
        }
    }
}
