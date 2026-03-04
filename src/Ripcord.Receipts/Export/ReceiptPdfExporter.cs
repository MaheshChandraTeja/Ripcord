#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ripcord.Receipts.Model;

namespace Ripcord.Receipts.Export
{
    /// <summary>
    /// PDF exporter with an optional QuestPDF implementation (compile with USE_QUESTPDF).
    /// Falls back to an HTML export if PDF support is not present.
    /// </summary>
    public static class ReceiptPdfExporter
    {
        public static async Task<string> ExportAsync(
            Receipt receipt,
            Attestation? attestation,
            string brandingJsonPath,
            string templateJsonPath,
            string outputDirectory,
            string? baseFileName = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(outputDirectory);
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            string baseName = baseFileName ?? $"receipt_{stamp}_{receipt.JobId:N}";

#if USE_QUESTPDF
            var outPath = Path.Combine(outputDirectory, baseName + ".pdf");
            await RenderWithQuestPdfAsync(receipt, attestation, brandingJsonPath, templateJsonPath, outPath, ct).ConfigureAwait(false);
            return outPath;
#else
            // Fallback: render a clean HTML summary next to the JSON. Many orgs are fine printing to PDF in CI.
            var outPath = Path.Combine(outputDirectory, baseName + ".html");
            await RenderHtmlAsync(receipt, attestation, brandingJsonPath, templateJsonPath, outPath, ct).ConfigureAwait(false);
            return outPath;
#endif
        }

#if USE_QUESTPDF
        private static Task RenderWithQuestPdfAsync(Receipt r, Attestation? a, string brandPath, string tplPath, string outPdf, CancellationToken ct)
        {
            // Example implementation outline (requires QuestPDF package):
            // var brand = JsonSerializer.Deserialize<Branding>(File.ReadAllBytes(brandPath))!;
            // var tpl = JsonSerializer.Deserialize<Template>(File.ReadAllBytes(tplPath))!;
            // QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            // Document.Create(container => { /* layout using Fluent API */ })
            //         .GeneratePdf(outPdf);
            throw new NotImplementedException("Build with USE_QUESTPDF and add a QuestPDF-based renderer.");
        }
#endif

        private static async Task RenderHtmlAsync(Receipt r, Attestation? a, string brandPath, string tplPath, string outHtml, CancellationToken ct)
        {
            using var sBrand = File.OpenRead(brandPath);
            using var sTpl = File.OpenRead(tplPath);
            var brandJson = await JsonDocument.ParseAsync(sBrand, cancellationToken: ct).ConfigureAwait(false);
            var tplJson = await JsonDocument.ParseAsync(sTpl, cancellationToken: ct).ConfigureAwait(false);

            string title = brandJson.RootElement.GetProperty("title").GetString() ?? "Ripcord Receipt";
            string org = brandJson.RootElement.GetProperty("organization").GetString() ?? "Organization";
            string color = brandJson.RootElement.GetProperty("accentColor").GetString() ?? "#0078D4";

            var sb = new StringBuilder();
            sb.Append("""
            <!doctype html><html><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
            body{font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;margin:24px;}
            h1{color:
            """).Append(color).Append("}").Append("""
            table{border-collapse:collapse;width:100%;margin-top:12px}
            th,td{border:1px solid #ddd;padding:8px}
            th{background:#f3f3f3;text-align:left}
            small{color:#666}
            </style></head><body>
            """);

            sb.Append("<h1>").Append(title).Append("</h1>");
            sb.Append("<p><b>Organization:</b> ").Append(org).Append("</p>");
            sb.Append("<p><b>Job ID:</b> ").Append(r.JobId).Append("<br/>")
              .Append("<b>Created:</b> ").Append(r.CreatedUtc.ToLocalTime().ToString("u")).Append("<br/>")
              .Append("<b>Profile:</b> ").Append(System.Net.WebUtility.HtmlEncode(r.ProfileName)).Append("</p>");

            sb.Append("<h3>Summary</h3><ul>");
            sb.Append("<li>Files processed: ").Append(r.Stats.FilesProcessed).Append("</li>");
            sb.Append("<li>Files deleted: ").Append(r.Stats.FilesDeleted).Append("</li>");
            sb.Append("<li>Bytes overwritten: ").Append(r.Stats.BytesOverwritten.ToString("n0")).Append("</li>");
            if (r.FreeSpace is not null)
            {
                sb.Append("<li>Free-space wipe: ").Append(r.FreeSpace.BytesWritten.ToString("n0"))
                  .Append(" bytes, files: ").Append(r.FreeSpace.FilesCreated)
                  .Append(", TRIM attempted: ").Append(r.FreeSpace.TrimAttempted).Append("</li>");
            }
            sb.Append("</ul>");

            sb.Append("<h3>Items</h3><table><thead><tr>")
              .Append("<th>Path</th><th>Size</th><th>Passes</th><th>Deleted</th><th>ADS (before/after)</th><th>Verify</th>")
              .Append("</tr></thead><tbody>");

            foreach (var it in r.Items)
            {
                sb.Append("<tr><td>")
                  .Append(System.Net.WebUtility.HtmlEncode(it.Path))
                  .Append("</td><td>")
                  .Append(it.SizeBytes.ToString("n0"))
                  .Append("</td><td>")
                  .Append(it.Passes)
                  .Append("</td><td>")
                  .Append(it.Deleted ? "yes" : "no")
                  .Append("</td><td>")
                  .Append(it.AdsBefore).Append(" / ").Append(it.AdsAfter)
                  .Append("</td><td>")
                  .Append(it.VerificationOk.HasValue ? (it.VerificationOk.Value ? "ok" : "fail") : "—")
                  .Append("</td></tr>");
            }
            sb.Append("</tbody></table>");

            var hash = r.ComputeHashHex();
            sb.Append("<p><b>Receipt SHA-256:</b> <code>").Append(hash).Append("</code></p>");

            if (a is not null)
            {
                sb.Append("<h3>Attestation</h3><p>")
                  .Append("<b>Signer:</b> ").Append(System.Net.WebUtility.HtmlEncode(a.SignerSubject ?? "—"))
                  .Append("<br/><b>Thumbprint:</b> ").Append(a.SignerThumbprint ?? "—")
                  .Append("<br/><b>Algorithm:</b> ").Append(a.SignatureAlgorithm ?? "—")
                  .Append("</p>");
            }

            sb.Append("<p><small>Generated by Ripcord</small></p></body></html>");

            await File.WriteAllTextAsync(outHtml, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
        }
    }
}
