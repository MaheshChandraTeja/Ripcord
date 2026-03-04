#nullable enable
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Vss;

namespace Ripcord.CLI.Commands
{
    internal static class ShadowPurge
    {
        public static Command Build()
        {
            var cmd = new Command("shadowpurge", "Enumerate or purge Volume Shadow Copies (VSS)");

            var volume = new Option<string?>("--volume", description: "Volume root (e.g., C:\\). Defaults to system drive.");
            var older = new Option<int>("--older-than-days", () => 0, "Delete snapshots older than N days (0 for any age)");
            var includeClient = new Option<bool>("--include-client", "Include client-accessible (Previous Versions) snapshots");
            var dryRun = new Option<bool>("--dry-run", () => true, "Dry run (list what would be deleted)");
            var broker = new Option<bool>("--broker", "Use elevated Broker if available");

            var list = new Option<bool>("--list", "List snapshots (no deletion)");
            cmd.AddOption(list);
            cmd.AddOption(volume);
            cmd.AddOption(older);
            cmd.AddOption(includeClient);
            cmd.AddOption(dryRun);
            cmd.AddOption(broker);

            cmd.SetHandler(async (InvocationContext ictx) =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    ictx.Console.Error.WriteLine("VSS is Windows-only.");
                    ictx.ExitCode = 3;
                    return;
                }

                var ct = ictx.GetCancellationToken();
                var log = ictx.GetHost().Services.GetRequiredService<ILoggerFactory>().CreateLogger("shadowpurge");

                var vol = ictx.ParseResult.GetValueForOption(volume) ?? (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
                var doList = ictx.ParseResult.GetValueForOption(list);
                var days = Math.Max(0, ictx.ParseResult.GetValueForOption(older));
                var include = ictx.ParseResult.GetValueForOption(includeClient);
                var dry = ictx.ParseResult.GetValueForOption(dryRun);
                var useBroker = ictx.ParseResult.GetValueForOption(broker);

                if (doList)
                {
                    var snaps = ShadowEnumerator.EnumerateByVolume(vol, log);
                    foreach (var s in snaps)
                        ictx.Console.WriteLine($"{s.Id} {s.VolumeName} {s.InstallDateUtc:u} client={s.ClientAccessible} device={s.DeviceObject}");
                    ictx.Console.WriteLine($"{snaps.Count} snapshot(s).");
                    return;
                }

                if (useBroker)
                {
                    var payload = new { volume = vol, olderThanDays = days, includeClientAccessible = include, dryRun = dry };
                    var res = await IpcClient.TryCallAsync("vss.purge", payload, ct);
                    if (res.Ok)
                    {
                        ictx.Console.WriteLine("Broker purge result: " + (res.PayloadJson ?? "{}"));
                        return;
                    }
                    ictx.Console.Error.WriteLine("Broker call failed, falling back: " + res.Error);
                }

                var r = await ShadowPurger.PurgeVolumeAsync(vol,
                    olderThan: days > 0 ? TimeSpan.FromDays(days) : null,
                    includeClientAccessible: include,
                    dryRun: dry,
                    logger: log,
                    journal: null,
                    ct: ct);

                if (!r.Success)
                {
                    ictx.Console.Error.WriteLine("Purge failed: " + r.Error);
                    ictx.ExitCode = 2;
                    return;
                }

                ictx.Console.WriteLine($"Deleted {r.Value} snapshot(s). DryRun={dry}.");
            });

            return cmd;
        }
    }
}
