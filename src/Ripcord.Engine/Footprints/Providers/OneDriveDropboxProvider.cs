#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Footprints.Providers
{
    /// <summary>
    /// Finds common OneDrive/Dropbox caches and logs that may contain filenames and activity traces.
    /// </summary>
    public static class OneDriveDropboxProvider
    {
        public static IEnumerable<FootprintScanner.Entry> Scan(ILogger? logger = null)
        {
            var entries = new List<FootprintScanner.Entry>();

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // OneDrive logs and temp
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(local, "Microsoft", "OneDrive", "logs"), "*.*"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(local, "Microsoft", "OneDrive", "setup", "logs"), "*.*"), logger);

            // OneDrive sync root (if present) – look for temp files (~$ and *.tmp) one level deep
            var oneDriveRoot = Path.Combine(home, "OneDrive");
            Collect(entries, FootprintScanner.Category.TempUser, OneLevel(oneDriveRoot, "~$*"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, OneLevel(oneDriveRoot, "*.tmp"), logger);

            // Dropbox cache (.dropbox.cache is auto-cleaned, but can persist)
            var dropboxRoot = Path.Combine(home, "Dropbox");
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(dropboxRoot, ".dropbox.cache"), "*.*"), logger);

            // Dropbox logs
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(roaming, "Dropbox", "logs"), "*.*"), logger);

            return entries;
        }

        // helpers
        private static IEnumerable<string> OneLevel(string root, string pattern)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;

            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); } catch { }
            foreach (var f in files) yield return f;

            IEnumerable<string> subs = Array.Empty<string>();
            try { subs = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly); } catch { }
            foreach (var d in subs)
            {
                IEnumerable<string> files2 = Array.Empty<string>();
                try { files2 = Directory.EnumerateFiles(d, pattern, SearchOption.TopDirectoryOnly); } catch { }
                foreach (var f in files2) yield return f;
            }
        }

        private static IEnumerable<string> All(string root, string pattern)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories); } catch { }
            foreach (var f in files) yield return f;
        }

        private static void Collect(List<FootprintScanner.Entry> output, FootprintScanner.Category cat, IEnumerable<string> files, ILogger? logger)
        {
            foreach (var p in files)
            {
                try
                {
                    var fi = new FileInfo(p);
                    if (!fi.Exists) continue;
                    output.Add(new FootprintScanner.Entry(cat, fi.FullName, fi.Length, fi.LastWriteTimeUtc));
                }
                catch (Exception ex) { logger?.LogDebug(ex, "OneDriveDropboxProvider: failed to stat {Path}", p); }
            }
        }
    }
}
