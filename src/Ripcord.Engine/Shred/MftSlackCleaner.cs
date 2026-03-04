
// =============================
// File: src/Ripcord.Engine/Shred/MftSlackCleaner.cs
// =============================
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Shred
{
    /// <summary>
    /// Best-effort mitigation for NTFS MFT slack by allocating and cycling many tiny files
    /// on the same volume to pressure MFT record reuse. This does not perform raw disk I/O
    /// and thus is safe for user-mode apps, but its effectiveness can vary across systems.
    /// </summary>
    public sealed class MftSlackCleaner
    {
        private readonly ILogger? _logger;

        public MftSlackCleaner(ILogger? logger = null) => _logger = logger;

        /// <summary>
        /// Executes a best-effort MFT slack pressure cycle under <paramref name="baseDirectory"/>.
        /// Creates batches of tiny files with unique names, fsyncs metadata, then deletes them.
        /// The goal is to cause NTFS to allocate and reuse MFT entries, minimizing residual slack.
        /// </summary>
        public async Task CleanAsync(DirectoryInfo baseDirectory, int batches = 8, int filesPerBatch = 4096, CancellationToken ct = default)
        {
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            baseDirectory.Refresh();
            if (!baseDirectory.Exists)
                throw new DirectoryNotFoundException(baseDirectory.FullName);

            string workRoot = Path.Combine(baseDirectory.FullName, ".ripcord_mft_fill");
            DirectoryInfo workDir = new(workRoot);
            if (!workDir.Exists) workDir.Create();
            try
            {
                _logger?.LogInformation("MFT slack cleaner starting in {Dir}. Batches={Batches} Files/Batch={FilesPerBatch}", workDir.FullName, batches, filesPerBatch);

                for (int b = 0; b < batches; b++)
                {
                    ct.ThrowIfCancellationRequested();
                    _logger?.LogDebug("MFT batch {Batch}/{Total}", b + 1, batches);

                    // Create files
                    for (int i = 0; i < filesPerBatch; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var name = $"{Guid.NewGuid():N}-{b:X2}-{i:X4}.tmp";
                        string p = Path.Combine(workDir.FullName, name);
                        using var fs = new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough | FileOptions.SequentialScan);
                        // Write 1 byte to ensure data stream exists (forces $DATA attribute).
                        fs.WriteByte(0);
                        fs.Flush(true); // ensure metadata flush
                    }

                    // Force a short delay to let NTFS settle
                    await Task.Delay(100, ct);

                    // Delete all files in this batch
                    foreach (var f in workDir.EnumerateFiles("*.tmp", SearchOption.TopDirectoryOnly))
                    {
                        try { f.Delete(); } catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed deleting temp file {File}", f.FullName);
                        }
                    }
                }

                _logger?.LogInformation("MFT slack cleaner completed in {Dir}", workDir.FullName);
            }
            finally
            {
                try { if (workDir.Exists) workDir.Delete(recursive: true); } catch { /* best-effort */ }
            }
        }
    }
}
