#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Ripcord.App.Services;

namespace Ripcord.App.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ILogger<SettingsPage> _log;
        private readonly BrokerClient _broker;

        private string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ripcord", "user.settings.json");

        public SettingsPage()
        {
            this.InitializeComponent();
            _log = App.Services.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsPage>();
            _broker = new BrokerClient(App.Services.GetRequiredService<ILoggerFactory>().CreateLogger<BrokerClient>());

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            LoadSettings();
        }

        private async void OnPingBroker(object sender, RoutedEventArgs e)
        {
            try
            {
                _broker.AutoStartBroker = AutoStartBrokerToggle.IsOn;
                _broker.ElevateBroker = true; // ping is safe; elevation prompt may appear if broker not running

                var reply = await _broker.PingAsync();
                BrokerStatusText.Text = reply.Ok ? $"OK — {reply.Payload?.GetProperty("user").GetString()}" : ("Error: " + reply.Error);
            }
            catch (Exception ex)
            {
                BrokerStatusText.Text = "Error: " + ex.Message;
            }
        }

        private async void OnBrowseReceipts(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WindowNative.GetWindowHandle(Window.Current ?? (Application.Current as App) as object);
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) ReceiptsFolderBox.Text = folder.Path;
        }

        private void OnOpenReceipts(object sender, RoutedEventArgs e)
        {
            var path = ReceiptsFolderBox.Text;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var obj = new
            {
                UseBroker = UseBrokerToggle.IsOn,
                AutoStartBroker = AutoStartBrokerToggle.IsOn,
                ReceiptsFolder = ReceiptsFolderBox.Text
            };

            var json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);

            // Nothing else to do; the UI reads this file on startup as needed.
            var tb = new TeachingTip { Title = "Saved", Subtitle = "Your settings were saved.", IsOpen = true };
        }

        private void LoadSettings()
        {
            // Defaults
            UseBrokerToggle.IsOn = true;
            AutoStartBrokerToggle.IsOn = true;
            ReceiptsFolderBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Receipts");

            try
            {
                if (!File.Exists(_settingsPath)) return;
                var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(_settingsPath)).RootElement;

                if (doc.TryGetProperty("UseBroker", out var ub)) UseBrokerToggle.IsOn = ub.GetBoolean();
                if (doc.TryGetProperty("AutoStartBroker", out var asb)) AutoStartBrokerToggle.IsOn = asb.GetBoolean();
                if (doc.TryGetProperty("ReceiptsFolder", out var rf)) ReceiptsFolderBox.Text = rf.GetString() ?? ReceiptsFolderBox.Text;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to read user settings.");
            }
        }
    }
}
