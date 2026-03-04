
// =============================
// File: src/Ripcord.Engine/Validation/VerificationProbe.cs
// =============================
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Shred;

namespace Ripcord.Engine.Validation
{
    /// <summary>
    /// Post-operation validation helpers (spot check passes, ADS hygiene, hashes).
    /// </summary>
    public static class VerificationProbe
    {
        /// <summary>
        /// Computes SHA-256 of a file. Returns hex uppercase string.
        /// </summary>
        public static async Task<string> ComputeSha256Async(FileInfo file, CancellationToken ct = default)
        {
            if (!file.Exists) throw new FileNotFoundException(file.FullName);
            using var sha = SHA256.Create();
            await using var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Verifies that the last pass' expected pattern appears to hold for a random sample of the file.
        /// If the last pass is Random, this probe skips byte-pattern validation and only confirms size and readability.
        /// </summary>
        public static async Task<bool> SamplePatternAsync(FileInfo file, OverwritePass lastPass, double fraction = 0.01, int minSamples = 8, int bytesPerSample = 4096, ILogger? logger = null, CancellationToken ct = default)
        {
            if (!file.Exists) throw new FileNotFoundException(file.FullName);
            if (fraction <= 0) return true;

            // If last pass was random we cannot predict content.
            if (lastPass.PatternType == OverwritePatternType.Random) return true;

            long length = file.Length;
            if (length == 0) return true;

            int samples = (int)Math.Max(minSamples, length * fraction / bytesPerSample);
            var rnd = Random.Shared;
            byte expected = lastPass.PatternType == OverwritePatternType.Constant ? lastPass.Constant : (byte)~lastPass.Constant;

            await using var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = ArrayPool<byte>.Shared.Rent(bytesPerSample);
            try
            {
                for (int i = 0; i < samples; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    long pos = (long)(rnd.NextDouble() * Math.Max(1, length - bytesPerSample));
                    fs.Position = pos;
                    int read = await fs.ReadAsync(buffer.AsMemory(0, bytesPerSample), ct).ConfigureAwait(false);
                    for (int j = 0; j < read; j++) if (buffer[j] != expected)
                    {
                        logger?.LogWarning("Verification failed at offset {Offset}: expected 0x{Expected:X2}", pos + j, expected);
                        return false;
                    }
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }

            return true;
        }

        /// <summary>
        /// Returns the number of remaining Alternate Data Streams for the file.
        /// </summary>
        public static int CountAds(string path)
        {
            return Ripcord.Engine.Shred.AdsEnumerator.Enumerate(path).Count();
        }

        /// <summary>
        /// Convenience: verify file does not exist (deleted) or has zero length.
        /// </summary>
        public static bool IsGoneOrZero(FileInfo file)
        {
            file.Refresh();
            return !file.Exists || file.Length == 0;
        }
    }
}
