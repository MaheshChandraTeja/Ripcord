#nullable enable
using System.Text;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Diagnostics;
using Ripcord.Engine.Shred;
using Xunit;

namespace E2E.Tests
{
    /// <summary>
    /// Simulates a mid-run interruption and verifies a second run completes and removes the target.
    /// We use cancellation via progress callback to emulate a power-loss event safely.
    /// </summary>
    public sealed class PowerLossResumeTests
    {
        [Fact]
        public async Task CancelMidRun_ThenResume_DeletesFile()
        {
            // Make a small file so this test is fast & deterministic on CI
            string dir = Directory.CreateTempSubdirectory("ripcord_plr_").FullName;
            string path = Path.Combine(dir, "victim.bin");
            await File.WriteAllBytesAsync(path, new byte[5 * 1024 * 1024]); // 5 MB

            var logger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning)).CreateLogger<ShredEngine>();
            var engine = new ShredEngine(logger);

            // First run: cancel after ~10% progress
            using var cts1 = new CancellationTokenSource();
            var cancelArmed = 0;
            engine.Progress += (_, p) =>
            {
                if (p.CurrentFile != null && string.Equals(p.CurrentFile, path, StringComparison.OrdinalIgnoreCase))
                {
                    // Trip once at >10% to simulate abrupt halt
                    if ((p.Percent ?? 0) >= 10 && Interlocked.Exchange(ref cancelArmed, 1) == 0)
                        cts1.Cancel();
                }
            };

            var req = new ShredJobRequest(path, Profiles.Default(), DryRun: false, DeleteAfter: true, Recurse: false);
            try
            {
                var result = await engine.RunAsync(req, cts1.Token);
                // Either an OCE bubbled or the engine returned an error object; both ok.
            }
            catch (OperationCanceledException) { /* expected */ }

            Assert.True(File.Exists(path)); // likely still present because delete step may not have executed

            // Second run: complete cleanly
            var result2 = await engine.RunAsync(req, CancellationToken.None);
            Assert.True(result2.Success, result2.Error ?? "Run failed on resume");

            // File should be gone
            Assert.False(File.Exists(path));

            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
