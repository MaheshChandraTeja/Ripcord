#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management; // Requires package: System.Management (Windows-only)
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Common;
using Ripcord.Engine.Journal;

namespace Ripcord.Engine.Vss
{
    /// <summary>
    /// Deletes (purges) shadow copies by ID or in bulk for a volume. Requires administrative privileges.
    /// </summary>
    public static class ShadowPurger
    {
        /// <summary>
        /// Delete a single shadow copy by ID (best-effort).
        /// </summary>
        public static Result DeleteById(Guid id, bool dryRun = false, ILogger? logger = null, JobJournal? journal = null)
        {
            try
            {
                if (dryRun)
                {
                    logger?.LogInformation("DRY-RUN: would delete shadow copy {Id}", id);
                    return Result.Ok();
                }

                using var obj = new ManagementObject(@"\\.\ROOT\CIMV2:Win32_ShadowCopy.ID='" + id + "'");
                var res = obj.InvokeMethod("Delete", null);
                int code = Convert.ToInt32(res, CultureInfo.InvariantCulture);
                if (code == 0)
                {
                    journal?.WriteAsync(new Journal.JournalRecord(DateTimeOffset.Now, Journal.JournalLevel.Info, "VssDelete", id.ToString(), Message: "Deleted")).GetAwaiter().GetResult();
                    return Result.Ok();
                }

                string msg = $"VSS Delete returned 0x{code:X}";
                logger?.LogWarning(msg);
                journal?.WriteAsync(new Journal.JournalRecord(DateTimeOffset.Now, Journal.JournalLevel.Warn, "VssDeleteFailed", id.ToString(), Message: msg)).GetAwaiter().GetResult();
                return Result.Fail(msg);
            }
            catch (UnauthorizedAccessException ex)
            {
                string msg = "Access denied. Run elevated to purge shadow copies.";
                logger?.LogError(ex, msg);
                return Result.Fail(msg, ex);
            }
            catch (ManagementException ex)
            {
                logger?.LogError(ex, "WMI error deleting VSS shadow {Id}", id);
                return Result.Fail(ex.Message, ex);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error deleting VSS shadow {Id}", id);
                return Result.Fail(ex.Message, ex);
            }
        }

        /// <summary>
        /// Deletes shadow copies for the given <paramref name="volumeRoot"/>. When <paramref name="olderThan"/> is set,
        /// only snapshots older than that age are removed. When <paramref name="includeClientAccessible"/> is false,
        /// "Previous Versions" (client-accessible) copies are left intact.
        /// </summary>
        public static async Task<Result<int>> PurgeVolumeAsync(
            string volumeRoot,
            TimeSpan? olderThan = null,
            bool includeClientAccessible = true,
            bool dryRun = false,
            ILogger? logger = null,
            JobJournal? journal = null,
            CancellationToken ct = default)
        {
            try
            {
                var snapshots = ShadowEnumerator.EnumerateByVolume(volumeRoot, logger);
                DateTime? cutoffUtc = olderThan.HasValue ? DateTime.UtcNow - olderThan.Value : null;

                var targets = snapshots.Where(s =>
                    (includeClientAccessible || !s.ClientAccessible) &&
                    (!cutoffUtc.HasValue || (s.InstallDateUtc.HasValue && s.InstallDateUtc.Value < cutoffUtc.Value)))
                    .ToList();

                int deleted = 0;
                foreach (var snap in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    if (dryRun)
                    {
                        logger?.LogInformation("DRY-RUN: would delete snapshot {Snapshot}", snap);
                        continue;
                    }

                    var r = DeleteById(snap.Id, false, logger, journal);
                    if (r.Success) deleted++;
                }

                if (dryRun)
                {
                    logger?.LogInformation("DRY-RUN: {Count} snapshots would be deleted for {Volume}.", targets.Count, volumeRoot);
                }

                await (journal?.WriteAsync(new Journal.JournalRecord(
                    DateTimeOffset.Now, Journal.JournalLevel.Info, "VssPurge",
                    volumeRoot, Message: $"Deleted={deleted} (Eligible={targets.Count})", Success: true)) ?? ValueTask.CompletedTask);

                return Result<int>.Ok(deleted);
            }
            catch (OperationCanceledException)
            {
                return Result<int>.Fail("Canceled");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "VSS purge failed for volume {Volume}", volumeRoot);
                return Result<int>.Fail(ex.Message, ex);
            }
        }
    }
}
