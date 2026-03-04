#nullable enable
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Shred;
using Ripcord.Receipts.Model;
using Ripcord.Receipts.Export;

namespace Ripcord.CLI.Commands
{
    internal static class Shred
    {
        public static Command Build()
        {
            var cmd = new Command("shred", "Securely shred a file or directory");

            var path = new Argument<string>("path", "File or directory to shred");
            var recurse = new Option<bool>("--recurse", "Recurse into subdirectories when path is a directory");
            var dryRun = new Option<bool>("--dry-run", "Plan only; do not write or delete");
            var deleteAfter = new Option<bool>("--delete-after", () => true, "Delete the file/directory after overwrite");
            var profile = new Option<string>("--profile", () => "default", "Profile id to use (currently 'default')");
            var broker = new Option<bool>("--broker", "Send the job to the elevated Broker over named pipes");
            var receiptDir = new Option<string?>("--receipt-dir", "Output directory for receipts (default ProgramData\\Ripcord\\Receipts)");
            var json = new Option<bool>("--json", "Emit a one-line JSON summary to stdout");
            cmd.AddArgument(path);
            cmd.AddOption(recurse); cmd.AddOption(dryRun); cmd.AddOption(deleteAfter);
            cmd.AddOption(profile); cmd.AddOption(broker); cmd.AddOption(receiptDir); cmd.AddOption(json);

            cmd.SetHandler(async (InvocationContext ictx) =>
            {
                var ct = ictx.GetCancellationToken();
                var host = ictx.GetHost();
                var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("shred");

                var argPath = ictx.ParseResult.GetValueForArgument(path)!;
                var optRecurse = ictx.ParseResult.GetValueForOption(recurse);
                var optDry = ictx.ParseResult.GetValueForOption(dryRun);
                var optDel = ictx.ParseResult.GetValueForOption(deleteAfter);
                var optBroker = ictx.ParseResult.GetValueForOption(broker);
                var optReceiptDir = ictx.ParseResult.GetValueForOption(receiptDir);
                var optJson = ictx.ParseResult.GetValueForOption(json);

                if (optBroker && OperatingSystem.IsWindows())
                {
                    // Try broker path first
                    var payload = new { path = argPath, recurse = optRecurse, dryRun = optDry, deleteAfter = optDel, profile = "default" };
                    var res = await IpcClient.TryCallAsync("shred", payload, ct);
                    if (res.Ok)
                    {
                        if (optJson) ictx.Console.WriteLine(res.PayloadJson ?? "{}");
                        else ictx.Console.WriteLine("Shred submitted via Broker: " + (res.PayloadJson ?? "{}"));
                        return;
                    }
                    ictx.Console.Error.WriteLine("Broker call failed, falling back to direct engine: " + res.Error);
                }

                // Direct Engine path
                var engine = new ShredEngine(log);
                var req = new ShredJobRequest(argPath, Profiles.Default(), optDry, optDel, optRecurse);

                var observed = new List<(string path, long size, bool deleted)>();
                engine.Progress += (_, p) =>
                {
                    if (p.Percent.HasValue && p.Percent % 10 is 0)
                        ictx.Console.WriteLine($"{p.Percent:0}% {p.Status} {p.CurrentFile}");
                    if (!string.IsNullOrWhiteSpace(p.CurrentFile) && p.BytesTotal.HasValue)
                        observed.Add((p.CurrentFile!, p.BytesTotal.Value, string.Equals(p.Status, "Deleted", StringComparison.OrdinalIgnoreCase)));
                };

                var started = DateTimeOffset.UtcNow;
                var result = await engine.RunAsync(req, ct);
                var completed = DateTimeOffset.UtcNow;

                if (!result.Success)
                {
                    ictx.Console.Error.WriteLine("Shred failed: " + result.Error);
                    ictx.ExitCode = 2;
                    return;
                }

                // Emit a receipt (unsigned)
                string outDir = optReceiptDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Receipts");
                Directory.CreateDirectory(outDir);

                var items = observed
                    .GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Last())
                    .Select(o => new Receipt.ReceiptItem(
                        Path: o.path, SizeBytes: Math.Max(0, o.size), Deleted: o.deleted,
                        Passes: req.Profile.Passes.Count, AdsBefore: -1, AdsAfter: -1, VerificationOk: null))
                    .ToList();

                var summary = result.Value!;
                var receipt = Receipt.Create(
                    jobId: Guid.NewGuid(),
                    startedUtc: started,
                    completedUtc: completed,
                    machine: Environment.MachineName,
                    user: Environment.UserName,
                    profileId: req.Profile.Id,
                    profileName: req.Profile.DisplayName,
                    dryRun: req.DryRun,
                    items: items,
                    stats: new Receipt.ReceiptStats(summary.FilesProcessed, summary.FilesFailed, (int)summary.FilesDeleted, summary.BytesOverwritten));

                var att = Attestation.FromReceipt(receipt);
                var paths = await ReceiptJsonExporter.ExportAsync(receipt, att, outDir);

                if (optJson)
                {
                    ictx.Console.WriteLine($$"""{"ok":true,"receipt":"{{paths.ReceiptPath.Replace("\\","\\\\")}}"}""");
                }
                else
                {
                    ictx.Console.WriteLine($"OK. Receipt: {paths.ReceiptPath}");
                }
            });

            return cmd;
        }

        // --- minimal broker client (JSON line-based) ---
        private static class NamedPipeClient
        {
            public sealed record Reply(bool Ok, string? PayloadJson, string? Error);

            public static async Task<Reply> TryCallAsync(string verb, object payload, CancellationToken ct)
            {
                try
                {
                    if (!OperatingSystem.IsWindows()) return new(false, null, "Windows-only");
                    var pipe = GetPipeNameForCurrentUser();
                    using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    await client.ConnectAsync(cts.Token).ConfigureAwait(false);

                    using var writer = new StreamWriter(client, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                    using var reader = new StreamReader(client, new System.Text.UTF8Encoding(false));

                    string id = Guid.NewGuid().ToString("N");
                    var line = System.Text.Json.JsonSerializer.Serialize(new { id, verb, payload });
                    await writer.WriteLineAsync(line).ConfigureAwait(false);

                    var resp = await reader.ReadLineAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                    if (resp is null) return new(false, null, "no response");
                    using var doc = System.Text.Json.JsonDocument.Parse(resp);
                    var root = doc.RootElement;
                    bool ok = root.GetProperty("ok").GetBoolean();
                    string? err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                    string? payloadJson = root.TryGetProperty("payload", out var p) ? p.GetRawText() : null;
                    return new(ok, payloadJson, err);
                }
                catch (Exception ex) { return new(false, null, ex.Message); }
            }

            private static string GetPipeNameForCurrentUser()
            {
                var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "S-1-0-0";
                var compact = sid.Replace("-", "", StringComparison.OrdinalIgnoreCase);
                return $"ripcord-broker-{compact}";
            }
        }
    }
}
