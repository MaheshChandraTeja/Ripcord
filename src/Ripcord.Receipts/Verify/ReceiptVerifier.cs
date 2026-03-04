#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using Ripcord.Receipts.Merkle;
using Ripcord.Receipts.Model;

namespace Ripcord.Receipts.Verify
{
    /// <summary>
    /// Validates structural integrity, computes Merkle root over items, and checks attestation (hash & signature).
    /// </summary>
    public static class ReceiptVerifier
    {
        public sealed record Report(
            bool StructureOk,
            bool HashOk,
            bool SignatureOk,
            string ReceiptHashHex,
            string ItemsMerkleRootHex,
            string[] Issues);

        public static Report Verify(Receipt receipt, Attestation? attestation)
        {
            var issues = new System.Collections.Generic.List<string>();

            // 1) Shape & values
            bool structureOk = true;
            try { receipt.Validate(); }
            catch (Exception ex) { issues.Add("Validation: " + ex.Message); structureOk = false; }

            // 2) Merkle over items (canonical item JSON)
            var mb = new MerkleBuilder();
            foreach (var it in receipt.Items)
            {
                // Canonicalize just the item to keep root stable across schemas
                var elem = JsonSerializer.SerializeToElement(it, new JsonSerializerOptions { WriteIndented = false });
                using var ms = new System.IO.MemoryStream();
                using var w = new Utf8JsonWriter(ms);
                elem.WriteTo(w);
                w.Flush();
                mb.AddLeaf(ms.ToArray());
            }
            var root = mb.Build().ToArray();
            string merkleHex = Convert.ToHexString(root).ToLowerInvariant();

            // 3) Hash & signature
            string receiptHash = receipt.ComputeHashHex();
            bool hashOk = true, sigOk = true;
            if (attestation is not null)
            {
                if (!string.Equals(attestation.ReceiptHashHex, receiptHash, StringComparison.OrdinalIgnoreCase))
                {
                    hashOk = false;
                    issues.Add("Attestation hash does not match the receipt.");
                }

                sigOk = attestation.Verify(receipt, out var reason);
                if (!sigOk && reason is not null) issues.Add("Signature: " + reason);
            }

            return new Report(structureOk, hashOk, sigOk, receiptHash, merkleHex, issues.ToArray());
        }
    }
}
