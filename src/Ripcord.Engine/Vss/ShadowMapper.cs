#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Vss
{
    /// <summary>
    /// Maps a live file path to potential paths inside one or more shadow copies of the same volume.
    /// </summary>
    public static class ShadowMapper
    {
        /// <summary>
        /// Given a target path (e.g. "C:\data\file.bin") and known snapshots for C:, produce
        /// candidate snapshot paths like "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy42\data\file.bin".
        /// When <paramref name="checkExists"/> is true, only returns candidates that exist.
        /// </summary>
        public static IReadOnlyList<string> MapToSnapshotPaths(
            string targetPath,
            IEnumerable<ShadowEnumerator.ShadowInfo> snapshots,
            bool checkExists = true)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));
            var full = Path.GetFullPath(targetPath);
            var root = Path.GetPathRoot(full) ?? throw new ArgumentException("Path must be rooted", nameof(targetPath));
            if (!root.EndsWith("\\", StringComparison.Ordinal)) root += "\\";

            string relative = full.Substring(root.Length).TrimStart('\\');

            var matches = new List<string>();
            foreach (var s in snapshots.Where(s => string.Equals(s.VolumeName, root, StringComparison.OrdinalIgnoreCase)))
            {
                var candidate = Path.Combine(s.DeviceRootWithSlash, relative);
                if (!checkExists || File.Exists(candidate) || Directory.Exists(candidate))
                    matches.Add(candidate);
            }
            return matches;
        }

        /// <summary>
        /// Convenience: enumerate snapshots for the volume and return the newest existing mapped path, if any.
        /// </summary>
        public static bool TryGetNewestExistingPath(string targetPath, out string? snapshotPath, ILogger? logger = null)
        {
            snapshotPath = null;
            try
            {
                var snaps = ShadowEnumerator.EnumerateByVolume(targetPath, logger);
                var list = MapToSnapshotPaths(targetPath, snaps, checkExists: true);
                snapshotPath = list.FirstOrDefault();
                return snapshotPath != null;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Shadow mapping failed for {Path}", targetPath);
                return false;
            }
        }
    }
}
