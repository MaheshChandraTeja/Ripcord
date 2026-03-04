#nullable enable
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.SSD;

namespace Ripcord.CLI.Commands
{
    internal static class FreeSpace
    {
        public static Command Build()
        {
            var cmd = new Command("freespace", "Wipe free space on a volume (best-effort, issues TRIM when possible)");

            var path = new Argument<string>("path", () => @"C:\", "Any path on the target volume (e.g., C:\\)");
            var reserveMb = new Option<int>("--reserve-mb", () => 1024, "Keep at least this much space free (MB)");
            cmd.AddArgument(path);
            cmd.AddOption(reserveMb);

            cmd.SetHandler(async (InvocationContext ictx) =>
            {
                var ct = ictx.GetCancellationToken();
                var log = ictx.GetHost().Services.GetRequiredService<ILoggerFactory>().CreateLogger("freespace");

                var argPath = ictx.ParseResult.GetValueForArgument(path)!;
                var reserve = Math.Max(0, ictx.ParseResult.GetValueForOption(reserveMb)) * 1024L * 1024L;

                var wiper = new FreeSpaceWiper(log);
                wiper.Progress += (_, p) =>
                {
                    if (p.Percent >= 0)
                        ictx.Console.WriteLine($"{p.Percent:0.0}% {p.BytesWritten:n0}/{p.BytesTarget:n0} bytes");
                };

                var res = await wiper.WipeAsync(argPath, reserve, ct);
                if (!res.Success)
                {
                    ictx.Console.Error.WriteLine("Free-space wipe failed: " + res.Error);
                    ictx.ExitCode = 2;
                    return;
                }

                var r = res.Value!;
                ictx.Console.WriteLine($"OK. Wrote {r.BytesWritten:n0} bytes across {r.FilesCreated} files. TRIM attempted: {r.TrimAttempted}.");
            });

            return cmd;
        }
    }
}
