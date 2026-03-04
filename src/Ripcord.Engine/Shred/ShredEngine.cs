using Microsoft.Extensions.Logging;
using Ripcord.Engine.Common;
using Ripcord.Engine.IO;
using Ripcord.Engine.Journal;
using Ripcord.Engine.Validation;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ripcord.Engine.Shred
{
    /// <summary>
    /// Core shred engine that operates on files/directories using an <see cref="IShredProfile"/>.
    /// Emits progress events and writes a structured journal.
    /// </summary>
    public sealed class ShredEngine
    {
        private readonly ILogger? _logger;
        private readonly JobJournal _journal;

        public ShredEngine(ILogger? logger = null, JobJournal? journal = null)
        {
            _logger = logger;
            _journal = journal ?? new JobJournal();
        }

        public event EventHandler<ShredJobProgress>? Progress;

        public async Task<Result<ShredJobResult>> RunAsync(ShredJobRequest request, CancellationToken ct = default)
        {
            try
            {
                Guard.NotNull(request, nameof(request));
                Guard.NotNull(request.Profile, nameof(request.Profile));
                Guard.NotNullOrWhiteSpace(request.TargetPath, nameof(request.TargetPath));

                var jobId = Guid.NewGuid();
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Info, "JobStart", request.TargetPath, request.Profile.Id));

                var fi = new FileInfo(request.TargetPath);
                var di = new DirectoryInfo(request.TargetPath);

                if (!fi.Exists && !di.Exists)
                    return Result<ShredJobResult>.Fail($"Target not found: {request.TargetPath}");

                int filesProcessed = 0, filesFailed = 0;
                long bytesOverwritten = 0, filesDeleted = 0;

                if (fi.Exists)
                {
                    var r = await ProcessFileAsync(jobId, fi, request, ct);
                    if (r.Success)
                    {
                        filesProcessed++;
                        bytesOverwritten += r.Value!.overwritten;
                        filesDeleted += r.Value!.deleted ? 1 : 0;
                    }
                    else
                    {
                        filesFailed++;
                    }
                }
                else
                {
                    var enumeration = request.Recurse
                        ? di.EnumerateFiles("*", SearchOption.AllDirectories)
                        : di.EnumerateFiles("*", SearchOption.TopDirectoryOnly);

                    foreach (var f in enumeration)
                    {
                        ct.ThrowIfCancellationRequested();
                        var r = await ProcessFileAsync(jobId, f, request, ct);
                        if (r.Success)
                        {
                            filesProcessed++;
                            bytesOverwritten += r.Value!.overwritten;
                            filesDeleted += r.Value!.deleted ? 1 : 0;
                        }
                        else
                        {
                            filesFailed++;
                        }
                    }
                }

                var result = new ShredJobResult(jobId, filesProcessed, filesFailed, bytesOverwritten, filesDeleted);
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Info, "JobCompleted", request.TargetPath, request.Profile.Id, Success: filesFailed == 0));
                return Result<ShredJobResult>.Ok(result);
            }
            catch (OperationCanceledException)
            {
                return Result<ShredJobResult>.Fail("Canceled");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ShredEngine failed for {Path}", request.TargetPath);
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Error, "JobFailed", request.TargetPath, request.Profile.Id, Message: ex.Message, Exception: ex.ToString()));
                return Result<ShredJobResult>.Fail(ex.Message, ex);
            }
        }

        private async Task<Result<(long overwritten, bool deleted)>> ProcessFileAsync(Guid jobId, FileInfo file, ShredJobRequest request, CancellationToken ct)
        {
            try
            {
                file.Refresh();
                if (!file.Exists) return Result<(long, bool)>.Fail($"Missing: {file.FullName}");

                long total = file.Length;
                long overwritten = 0;

                Progress?.Invoke(this, new ShredJobProgress(jobId, file.FullName, null, 0, total, 0, "Starting"));
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Info, "FileStart", file.FullName, request.Profile.Id, BytesProcessed: 0, AdsCountBefore: SafeCountAds(file)));

                // Rename noise
                if (request.Profile.ApplyRenameNoise && !request.DryRun)
                {
                    await RenameNoise.ApplyAsync(file, request.Profile.RenameIterations, _logger, ct);
                }

                // Overwrite passes
                if (!request.DryRun && total > 0)
                {
                    var media = MediaDetector.Detect(file.FullName);
                    var rng = Random.Shared;
                    var fsOptions = FileOptions.SequentialScan | (media.RecommendWriteThrough ? FileOptions.WriteThrough : 0);

                    for (int p = 0; p < request.Profile.Passes.Count; p++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var pass = request.Profile.Passes[p];

                        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None, media.RecommendedBufferBytes, fsOptions);
                        fs.Position = 0;
                        long passWritten = 0;

                        await FileChunker.WriteAsync(fs, total, media.RecommendedBufferBytes, dest =>
                        {
                            passWritten += dest.Length;
                            return OverwriteStrategy.Fill(pass, dest.Span, rng);
                        },
                        onAdvanced: advanced =>
                        {
                            overwritten = advanced;
                            double percent = (advanced / (double)total) * 100.0;
                            Progress?.Invoke(this, new ShredJobProgress(jobId, file.FullName, p, advanced, total, percent, $"Pass {p + 1}/{request.Profile.Passes.Count}"));
                        }, ct);

                        await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Info, "FilePassCompleted", file.FullName, request.Profile.Id, PassIndex: p, BytesProcessed: passWritten));
                    }
                }

                // ADS cleanup (best-effort)
                if (request.Profile.WipeAlternateDataStreams && !request.DryRun)
                {
                    try { AdsEnumerator.DeleteAll(file.FullName, _logger); }
                    catch (Exception ex) { _logger?.LogWarning(ex, "ADS cleanup failed on {File}", file.FullName); }
                }

                // Verification sample
                if (request.Profile.Passes.Count > 0)
                {
                    var last = request.Profile.Passes[^1];
                    bool ok = await VerificationProbe.SamplePatternAsync(file, last, fraction: 0.01, minSamples: 4, bytesPerSample: 4096, logger: _logger, ct: ct);
                    if (!ok) _logger?.LogWarning("Verification sample mismatch on {File}", file.FullName);
                }

                // Delete the file
                bool deleted = false;
                if (request.DeleteAfter && !request.DryRun)
                {
                    try
                    {
                        File.SetAttributes(file.FullName, FileAttributes.Normal);
                        file.Delete();
                        deleted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Deletion failed for {File}", file.FullName);
                        // Treat as failure: file remains
                        await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Warn, "FileDeleteFailed", file.FullName, request.Profile.Id, Exception: ex.ToString(), Message: ex.Message));
                    }
                }

                // Optional MFT slack pressure once per containing directory (cheap small run)
                if (request.Profile.WipeMftSlack && !request.DryRun)
                {
                    try
                    {
                        var cleaner = new MftSlackCleaner(_logger);
                        await cleaner.CleanAsync(file.Directory ?? new DirectoryInfo(Path.GetDirectoryName(file.FullName)!), batches: 1, filesPerBatch: 512, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "MFT slack cleaner issue near {Dir}", file.DirectoryName);
                    }
                }

                Progress?.Invoke(this, new ShredJobProgress(jobId, file.FullName, request.Profile.Passes.Count - 1, total, total, 100, deleted ? "Deleted" : "Processed"));
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Info, "FileCompleted", file.FullName, request.Profile.Id, BytesProcessed: total, AdsCountAfter: SafeCountAds(file), Success: true));

                return Result<(long overwritten, bool deleted)>.Ok((overwritten, deleted));
            }
            catch (OperationCanceledException)
            {
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Warn, "FileCanceled", file.FullName, request.Profile.Id));
                return Result<(long, bool)>.Fail("Canceled");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed processing file {File}", file.FullName);
                await _journal.WriteAsync(new JournalRecord(DateTimeOffset.Now, JournalLevel.Error, "FileFailed", file.FullName, request.Profile.Id, Message: ex.Message, Exception: ex.ToString()));
                return Result<(long, bool)>.Fail(ex.Message, ex);
            }
        }

        private static int SafeCountAds(FileInfo file)
        {
            try { return Validation.VerificationProbe.CountAds(file.FullName); }
            catch { return -1; }
        }
    }
}
