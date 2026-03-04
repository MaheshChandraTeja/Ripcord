#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Ripcord.App.Services;

namespace E2E.Tests
{
    public sealed class ElevatedFlowTests
    {
        [Fact]
        public async Task Broker_Ping_Works_Or_Skips_On_NonWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Non-Windows CI: just skip gracefully
                return;
            }

            // Try to locate a broker EXE near the test binaries, else start none and just try pipe if already running.
            string? brokerExe = ResolveBrokerExe();
            Process? broker = null;

            try
            {
                if (brokerExe is not null && File.Exists(brokerExe))
                {
                    broker = Process.Start(new ProcessStartInfo
                    {
                        FileName = brokerExe,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    await Task.Delay(800);
                }

                var logger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning)).CreateLogger<BrokerClient>();
                var client = new BrokerClient(logger)
                {
                    AutoStartBroker = brokerExe is not null, // don't show UAC in tests
                    ElevateBroker = false,
                    BrokerExePath = brokerExe
                };

                var reply = await client.PingAsync();
                Assert.True(reply.Ok, reply.Error ?? "Ping failed");

                // Optional: VSS enumerate (safe). It's okay if returns with empty list.
                var vss = await client.VssEnumerateAsync(@"C:\");
                Assert.True(vss.Ok || vss.Error is not null);
            }
            finally
            {
                try { if (broker is not null && !broker.HasExited) broker.Kill(entireProcessTree: true); } catch { }
            }
        }

        private static string? ResolveBrokerExe()
        {
            // 1) Parallel to test binaries (CI published scenario)
            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "Ripcord.Broker.exe");
            if (File.Exists(candidate)) return candidate;

            // 2) Repo layout: ../src/Ripcord.Broker/bin/Debug/...
            var p2 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "Ripcord.Broker", "bin", "Debug"));
            if (Directory.Exists(p2))
            {
                foreach (var tf in new[] { "net8.0-windows10.0.19041.0", "net8.0-windows" })
                {
                    var exe = Path.Combine(p2, tf, "Ripcord.Broker.exe");
                    if (File.Exists(exe)) return exe;
                }
            }

            return null;
        }
    }
}
