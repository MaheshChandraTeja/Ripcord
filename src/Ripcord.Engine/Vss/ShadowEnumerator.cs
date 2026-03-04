#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management; // Requires package: System.Management (Windows-only)
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Vss
{
    /// <summary>
    /// Enumerates Volume Shadow Copies via WMI (Win32_ShadowCopy).
    /// </summary>
    public static class ShadowEnumerator
    {
        /// <summary>Snapshot information from Win32_ShadowCopy.</summary>
        public sealed record ShadowInfo(
            Guid Id,
            string VolumeName,       // e.g. "C:\"
            string DeviceObject,     // e.g. "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy42"
            DateTime? InstallDateUtc,
            bool Persistent,
            bool ClientAccessible,
            uint State
        )
        {
            public string DeviceRootWithSlash => DeviceObject.EndsWith("\\", StringComparison.Ordinal) ? DeviceObject : DeviceObject + "\\";
            public override string ToString()
                => $"{Id} Vol={VolumeName} Device={DeviceObject} ClientAccessible={ClientAccessible} Persistent={Persistent} State={State} Created={InstallDateUtc:u}";
        }

        /// <summary>
        /// Enumerate all snapshots on the machine.
        /// </summary>
        public static IReadOnlyList<ShadowInfo> EnumerateAll(ILogger? logger = null)
        {
            var list = new List<ShadowInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(@"ROOT\CIMV2", "SELECT * FROM Win32_ShadowCopy");
                foreach (ManagementObject o in searcher.Get())
                {
                    try
                    {
                        var id = Guid.Parse((string)o["ID"]);
                        string volume = ((string?)o["VolumeName"])?.Trim() ?? string.Empty;
                        string device = ((string?)o["DeviceObject"])?.Trim() ?? string.Empty;
                        bool persistent = (bool)(o["Persistent"] ?? false);
                        bool client = (bool)(o["ClientAccessible"] ?? false);
                        uint state = (uint)(o["State"] ?? 0U);

                        DateTime? installUtc = null;
                        if (o["InstallDate"] is string wmiDate && !string.IsNullOrWhiteSpace(wmiDate))
                        {
                            // WMI datetime format yyyymmddHHMMSS.mmmmmmsUUU
                            installUtc = ManagementDateTimeConverter.ToDateTime(wmiDate).ToUniversalTime();
                        }

                        if (!string.IsNullOrWhiteSpace(device) && !string.IsNullOrWhiteSpace(volume))
                        {
                            // Normalize volume to a root with backslash, e.g. "C:\"
                            volume = Path.GetPathRoot(volume) ?? volume;
                            if (!volume.EndsWith("\\", StringComparison.Ordinal)) volume += "\\";
                            list.Add(new ShadowInfo(id, volume, device, installUtc, persistent, client, state));
                        }
                    }
                    catch (Exception ex) { logger?.LogDebug(ex, "Failed to parse a Win32_ShadowCopy row."); }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "WMI query for Win32_ShadowCopy failed.");
            }

            // Newest first to favor recent points when mapping
            return list
                .OrderByDescending(s => s.InstallDateUtc ?? DateTime.MinValue)
                .ThenByDescending(s => s.Persistent)
                .ToList();
        }

        /// <summary>
        /// Enumerate snapshots for a specific volume root (e.g. "C:\").
        /// </summary>
        public static IReadOnlyList<ShadowInfo> EnumerateByVolume(string volumeRoot, ILogger? logger = null)
        {
            string root = Path.GetPathRoot(volumeRoot) ?? volumeRoot;
            if (!root.EndsWith("\\", StringComparison.Ordinal)) root += "\\";
            return EnumerateAll(logger).Where(s => string.Equals(s.VolumeName, root, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
