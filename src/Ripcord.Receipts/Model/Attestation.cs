#nullable enable
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;

namespace Ripcord.Receipts.Model
{
    /// <summary>
    /// Cryptographic attestation for a <see cref="Receipt"/>. 
    /// Hashes use SHA-256 over the receipt's canonical JSON. Optional X.509 signature.
    /// </summary>
    public sealed record Attestation(
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("receiptHashAlg")] string ReceiptHashAlgorithm,
        [property: JsonPropertyName("receiptHashHex")] string ReceiptHashHex,
        [property: JsonPropertyName("createdUtc")] DateTimeOffset CreatedUtc,
        [property: JsonPropertyName("signerSubject")] string? SignerSubject = null,
        [property: JsonPropertyName("signerThumbprint")] string? SignerThumbprint = null,
        [property: JsonPropertyName("signatureAlg")] string? SignatureAlgorithm = null,
        [property: JsonPropertyName("signatureB64")] string? SignatureBase64 = null,
        [property: JsonPropertyName("certificateB64")] string? CertificateRawBase64 = null
    )
    {
        public static Attestation FromReceipt(Receipt receipt)
        {
            var hashHex = receipt.ComputeHashHex();
            return new Attestation(
                SchemaVersion: "1.0",
                ReceiptHashAlgorithm: "SHA-256",
                ReceiptHashHex: hashHex,
                CreatedUtc: DateTimeOffset.UtcNow
            );
        }

        /// <summary>
        /// Returns a new attestation with a signature produced by <paramref name="cert"/> (RSA).
        /// The signature is over the receipt <em>canonical JSON bytes</em>.
        /// </summary>
        public Attestation Sign(Receipt receipt, X509Certificate2 cert)
        {
            if (!cert.HasPrivateKey) throw new InvalidOperationException("Certificate has no private key.");

            var canonical = receipt.ToCanonicalJsonUtf8();
            using var rsa = cert.GetRSAPrivateKey() ?? throw new NotSupportedException("Only RSA certificates are supported.");
            var sig = rsa.SignData(canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return this with
            {
                SignerSubject = cert.Subject,
                SignerThumbprint = cert.Thumbprint?.ToUpperInvariant(),
                SignatureAlgorithm = "RSA-SHA256-PKCS1",
                SignatureBase64 = Convert.ToBase64String(sig),
                CertificateRawBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Cert))
            };
        }

        /// <summary>Verifies both the receipt hash and the signature (if present).</summary>
        public bool Verify(Receipt receipt, out string? reason)
        {
            // Hash check
            var expected = receipt.ComputeHashHex();
            if (!string.Equals(expected, ReceiptHashHex, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Receipt hash mismatch.";
                return false;
            }

            // Signature optional
            if (string.IsNullOrWhiteSpace(SignatureBase64))
            {
                reason = null;
                return true;
            }

            try
            {
                byte[] sig = Convert.FromBase64String(SignatureBase64);
                byte[] canonical = receipt.ToCanonicalJsonUtf8();

                X509Certificate2? cert = null;
                if (!string.IsNullOrWhiteSpace(CertificateRawBase64))
                {
                    cert = new X509Certificate2(Convert.FromBase64String(CertificateRawBase64));
                }

                using var rsa = (cert?.GetRSAPublicKey()) ?? throw new InvalidOperationException("No certificate/public key available to verify signature.");
                bool ok = rsa.VerifyData(canonical, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                reason = ok ? null : "Signature verification failed.";
                return ok;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}
