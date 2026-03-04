#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Common;
using Ripcord.Engine.IO;
using Ripcord.Engine.Journal;

namespace Ripcord.Engine.SSD
{
    /// <summary>
    /// Executes a <see cref="TrimPlan"/> by creating wipe files to occupy free space
    /// and, when possible, issuing FILE_LEVEL_TRIM on those files before deleting them.
    /// </summary>
    public sealed class TrimExecutor
    {
        private readonly ILogger? _logger;
        private readonly JobJournal _journal;

        public TrimExecutor(ILogger? logger = null, JobJournal? journal = null)
        {
            _logger = logger;
            _journal = journal ?? new JobJournal();
        }

        public event EventHandler<FreeWipeProgress>? Progress;

        public sealed record FreeWipeProgress(
            string VolumeRoot,
            long BytesTarget,
            long BytesWritten,
            int FilesCreated,
            string? CurrentFile,
            double Percent
        );

        public sealed record FreeWipeResult(
            string VolumeRoot,
            long BytesWritten,
            int FilesCreated,
            bool TrimAttempted
        );

        /// <summary>
        /// Run the free-space wipe on <paramref name="plan"/>. This is best-effort and requires no elevation.
        /// </summary>
        public async Task<Result<FreeWipeResult>> RunAsync(TrimPlan plan, CancellationToken ct = default)
        {
            try
            {
                if (plan.TargetBytesToFill <= 0)
                {
                    _logger?.LogInformation("TrimExecutor: nothing to do; target is 0 bytes.");
                    return Result<FreeWipeResult>.Ok(new FreeWipeResult(plan.VolumeRoot, 0, 0, false));
                }

                string workDir = Path.Combine(plan.VolumeRoot, ".ripcord_freewipe");
                Directory.CreateDirectory(workDir);

                long written = 0;
                int files = 0;
                bool trimAttempted = false;

                // Choose buffer size based on media hints
                var caps = VolumeCapabilities.Detect(plan.VolumeRoot);
                int bufSize = plan.ChunkBytes;
                var options = FileOptions.SequentialScan | FileOptions.WriteThrough;

                for (int i = 0; i < plan.MaxFiles && written < plan.TargetBytesToFill; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = $"wipe-{i:D4}.bin";
                    string path = Path.Combine(workDir, name);

                    long toWrite = Math.Min(plan.TargetBytesToFill - written, Math.Max(caps.ClusterSizeBytes * 64, bufSize * 128L)); // write in reasonable segments
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufSize, options);

                    // Fill with zeros (constant) – we don't need random; goal is allocation.
                    await FileChunker.WriteAsync(
                        fs,
                        toWrite,
                        bufSize,
                        dest =>
                        {
                            dest.Span.Clear();
                            return dest.Length;
                        },
                        onAdvanced: adv =>
                        {
                            long now = written + adv;
                            Progress?.Invoke(this, new FreeWipeProgress(plan.VolumeRoot, plan.TargetBytesToFill, now, files, path, now / (double)plan.TargetBytesToFill * 100.0));
                        },
                        ct).ConfigureAwait(false);

                    files++;
                    written += toWrite;

                    // Best-effort: tell storage that the file's clusters can be discarded now
                    try
                    {
                        if (NativeTrim.RcFileLevelTrim(path))
                        {
                            trimAttempted = true;
                            _logger?.LogDebug("FILE_LEVEL_TRIM issued for {File}", path);
                        }
                    }
                    catch (DllNotFoundException) { /* native optional */ }
                    catch (EntryPointNotFoundException) { /* optional */ }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "FILE_LEVEL_TRIM failed for {File}", path);
                    }

                    // Delete immediately to release space
                    try { File.Delete(path); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed deleting wipe file {File}", path); }
                }

                // Clean up directory
                try { Directory.Delete(workDir, recursive: true); } catch { }

                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Info, "FreeSpaceWipe", plan.VolumeRoot, Message: $"Wrote {written:n0} bytes in {files} files; TrimAttempted={trimAttempted}", Success: true));

                return Result<FreeWipeResult>.Ok(new FreeWipeResult(plan.VolumeRoot, written, files, trimAttempted));
            }
            catch (OperationCanceledException)
            {
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Warn, "FreeSpaceWipeCanceled", plan.VolumeRoot));
                return Result<FreeWipeResult>.Fail("Canceled");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Free-space wipe failed on {Root}", plan.VolumeRoot);
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Error, "FreeSpaceWipeFailed", plan.VolumeRoot, Message: ex.Message, Exception: ex.ToString()));
                return Result<FreeWipeResult>.Fail(ex.Message, ex);
            }
        }

        private static class NativeTrim
        {
            // Provided by Ripcord.Engine.Native (native_vss_and_trim.cpp)
            [DllImport("Ripcord.Engine.Native.dll", EntryPoint = "RcFileLevelTrimW", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern bool RcFileLevelTrimW(string path);

            public static bool RcFileLevelTrim(string path) => RcFileLevelTrimW(path);
        }
    }
}
