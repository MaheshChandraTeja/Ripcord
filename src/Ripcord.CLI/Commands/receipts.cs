#nullable enable
using System.CommandLine;
using System.CommandLine.Invocation;
using Ripcord.Receipts.Export;
using Ripcord.Receipts.Model;
using Ripcord.Receipts.Verify;

namespace Ripcord.CLI.Commands
{
    internal static class Receipts
    {
        public static Command Build()
        {
            var cmd = new Command("receipts", "List, verify, and export receipts");

            var dir = new Option<string?>("--dir", "Directory containing *.receipt.json (default ProgramData\\Ripcord\\Receipts)");
            var verify = new Option<bool>("--verify", "Verify each receipt against its attestation (if present)");
            var export = new Option<bool>("--export", "Export each receipt to HTML/PDF (see reports/templates)");
            var outDir = new Option<string?>("--out", "Output directory for exports (default: <dir>\\exports)");
            cmd.AddOption(dir); cmd.AddOption(verify); cmd.AddOption(export); cmd.AddOption(outDir);

            cmd.SetHandler(async (InvocationContext ictx) =>
            {
                var ct = ictx.GetCancellationToken();
                string root = ictx.ParseResult.GetValueForOption(dir)
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Receipts");
                string outRoot = ictx.ParseResult.GetValueForOption(outDir) ?? Path.Combine(root, "exports");
                bool doVerify = ictx.ParseResult.GetValueForOption(verify);
                bool doExport = ictx.ParseResult.GetValueForOption(export);

                Directory.CreateDirectory(root);
                var receipts = Directory.EnumerateFiles(root, "*.receipt.json", SearchOption.TopDirectoryOnly).OrderBy(f => f).ToArray();
                if (receipts.Length == 0)
                {
                    ictx.Console.WriteLine("No receipts found in " + root);
                    return;
                }

                foreach (var rp in receipts)
                {
                    Attestation? att = null;
                    var ap = Path.ChangeExtension(rp, ".attestation.json");
                    var r = System.Text.Json.JsonSerializer.Deserialize<Receipt>(await File.ReadAllBytesAsync(rp, ct))!;
                    if (File.Exists(ap))
                        att = System.Text.Json.JsonSerializer.Deserialize<Attestation>(await File.ReadAllBytesAsync(ap, ct))!;

                    string status = "—";
                    if (doVerify)
                    {
                        var report = ReceiptVerifier.Verify(r, att);
                        status = (report.StructureOk && report.HashOk && report.SignatureOk) ? "Verified" : "FAILED: " + string.Join("; ", report.Issues);
                    }

                    string? exported = null;
                    if (doExport)
                    {
                        Directory.CreateDirectory(outRoot);
                        var exeDir = AppContext.BaseDirectory;
                        var brand = Path.Combine(exeDir, "reports", "templates", "receipt.branding.json");
                        var tpl = Path.Combine(exeDir, "reports", "templates", "receipt.pdf.template.json");
                        exported = await ReceiptPdfExporter.ExportAsync(r, att, brand, tpl, outRoot, Path.GetFileNameWithoutExtension(rp), ct);
                    }

                    ictx.Console.WriteLine($"{Path.GetFileName(rp)} | hash={r.ComputeHashHex()[..12]}… | {(doVerify ? status : "skip-verify")} | {(doExport ? ("export=" + exported) : "skip-export")}");
                }
            });

            return cmd;
        }
    }
}
