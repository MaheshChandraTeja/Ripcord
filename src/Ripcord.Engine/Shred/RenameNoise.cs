
// =============================
// File: src/Ripcord.Engine/Shred/RenameNoise.cs
// =============================
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Shred
{
    /// <summary>
    /// Performs multiple random renames on a target file to reduce recoverable filename history
    /// in directory entries and MFT attribute logs.
    /// </summary>
    public static class RenameNoise
    {
        private static readonly char[] SafeChars =
            ("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_ .").ToCharArray();

        public static async Task ApplyAsync(FileInfo file, int iterations, ILogger? logger = null, CancellationToken ct = default)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (!file.Exists) throw new FileNotFoundException(file.FullName);
            iterations = Math.Clamp(iterations, 0, 64);
            if (iterations == 0) return;

            string dir = file.Directory!.FullName;
            string ext = file.Extension;

            for (int i = 0; i < iterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                string noiseName = GenerateName(12) + ext; // retain extension shape to avoid surprises
                string dest = Path.Combine(dir, noiseName);
                try
                {
                    File.Move(file.FullName, dest, overwrite: true);
                    logger?.LogDebug("Rename noise {Index}/{Total}: {From} -> {To}", i + 1, iterations, file.Name, Path.GetFileName(dest));
                    file = new FileInfo(dest);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Rename noise failed at {Index} for {File}", i + 1, file.FullName);
                    break; // stop on first hard failure
                }

                // Add a tiny jitter so file system updates get distinct timestamps.
                await Task.Delay(10, ct);
            }
        }

        private static string GenerateName(int length)
        {
            var rnd = Random.Shared;
            Span<char> buf = stackalloc char[length];
            for (int i = 0; i < buf.Length; i++) buf[i] = SafeChars[rnd.Next(SafeChars.Length)];
            // avoid leading dot or space to keep visibility
            if (buf[0] == '.' || buf[0] == ' ') buf[0] = 'X';
            return new string(buf);
        }
    }
}
