using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ripcord.Engine.SSD;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ripcord.App.Views
{
    public sealed partial class FreeSpacePage : Page
    {
        private CancellationTokenSource? _cts;

        public FreeSpacePage()
        {
            this.InitializeComponent();
        }

        private void Analyze_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var caps = VolumeCapabilities.Detect(PathBox.Text);
                CapsText.Text = $"Volume: {caps.VolumeRoot} | FS: {caps.FileSystem} | Cluster: {caps.ClusterSizeBytes} B | " +
                                $"Total: {caps.TotalBytes:n0} | Free: {caps.FreeBytes:n0} | Sparse: {caps.SupportsSparse} | Trim: {caps.TrimEnabled} | " +
                                $"SeekPenalty(HDD?): {caps.IncursSeekPenalty?.ToString() ?? "Unknown"}";
                StartBtn.IsEnabled = caps.FreeBytes > 0;
            }
            catch (Exception ex)
            {
                CapsText.Text = "Analyze failed: " + ex.Message;
                StartBtn.IsEnabled = false;
            }
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            StartBtn.IsEnabled = false;
            CancelBtn.IsEnabled = true;
            Progress.Value = 0;
            ProgressText.Text = "Starting...";

            _cts = new CancellationTokenSource();
            try
            {
                var wiper = new FreeSpaceWiper();
                wiper.Progress += (s, ev) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        Progress.Value = ev.Percent;
                        ProgressText.Text = $"{ev.Percent:0.0}% — {ev.BytesWritten:n0}/{ev.BytesTarget:n0} bytes written ({ev.FilesCreated} files)";
                    });
                };

                long reserveBytes = (long)(ReserveBox.Value * 1024 * 1024);
                var result = await wiper.WipeAsync(PathBox.Text, reserveBytes, _cts.Token);

                if (result.Success)
                {
                    Progress.Value = 100;
                    ProgressText.Text = $"Done. Wrote {result.Value!.BytesWritten:n0} bytes across {result.Value.FilesCreated} files. TRIM attempted: {result.Value.TrimAttempted}.";
                }
                else
                {
                    ProgressText.Text = "Failed: " + result.Error;
                }
            }
            catch (OperationCanceledException)
            {
                ProgressText.Text = "Canceled.";
            }
            catch (Exception ex)
            {
                ProgressText.Text = "Error: " + ex.Message;
            }
            finally
            {
                CancelBtn.IsEnabled = false;
                StartBtn.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }
    }
}
