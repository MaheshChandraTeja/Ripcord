#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ripcord.Receipts.Model;

namespace Ripcord.Receipts.Export
{
    /// <summary>
    /// Writes canonical JSON for the receipt and a companion attestation JSON.
    /// </summary>
    public static class ReceiptJsonExporter
    {
        public sealed record Paths(string ReceiptPath, string? AttestationPath);

        public static async Task<Paths> ExportAsync(Receipt receipt, Attestation? attestation, string outputDirectory, string? baseFileName = null, CancellationToken ct = default)
        {
            Directory.CreateDirectory(outputDirectory);
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            string baseName = baseFileName ?? $"receipt_{stamp}_{receipt.JobId:N}";

            string receiptPath = Path.Combine(outputDirectory, baseName + ".receipt.json");
            await File.WriteAllBytesAsync(receiptPath, receipt.ToCanonicalJsonUtf8(), ct).ConfigureAwait(false);

            string? attPath = null;
            if (attestation is not null)
            {
                attPath = Path.Combine(outputDirectory, baseName + ".attestation.json");
                var json = JsonSerializer.SerializeToUtf8Bytes(attestation, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllBytesAsync(attPath, json, ct).ConfigureAwait(false);
            }

            return new Paths(receiptPath, attPath);
        }
    }
}
