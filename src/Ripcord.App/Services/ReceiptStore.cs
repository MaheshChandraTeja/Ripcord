#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ripcord.App.ViewModels;
using Ripcord.Receipts.Model;

namespace Ripcord.App.Services
{
    /// <summary>
    /// Simple file-system backed store for receipts and attestations.
    /// </summary>
    public sealed class ReceiptStore
    {
        public async Task<IReadOnlyList<ReceiptsViewModel.ReceiptItem>> ListAsync(string root)
        {
            var list = new List<ReceiptsViewModel.ReceiptItem>();
            if (!Directory.Exists(root)) return list;

            var receipts = Directory.EnumerateFiles(root, "*.receipt.json", SearchOption.TopDirectoryOnly).OrderByDescending(f => f);
            foreach (var rp in receipts)
            {
                try
                {
                    var receipt = await LoadReceiptAsync(rp);
                    string? att = FindAttestationSibling(rp);
                    list.Add(new ReceiptsViewModel.ReceiptItem
                    {
                        FileName = Path.GetFileName(rp),
                        Created = receipt.CreatedUtc,
                        Profile = receipt.ProfileName,
                        HashShort = receipt.ComputeHashHex().Substring(0, 12) + "…",
                        ReceiptPath = rp,
                        AttestationPath = att
                    });
                }
                catch { /* ignore corrupt entries */ }
            }
            return list;
        }

        public Task<Receipt> LoadReceiptAsync(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return Task.FromResult(JsonSerializer.Deserialize<Receipt>(bytes)!);
        }

        public Task<Attestation> LoadAttestationAsync(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return Task.FromResult(JsonSerializer.Deserialize<Attestation>(bytes)!);
        }

        private static string? FindAttestationSibling(string receiptPath)
        {
            string att = Path.ChangeExtension(receiptPath, ".attestation.json");
            return File.Exists(att) ? att : null;
        }
    }
}
