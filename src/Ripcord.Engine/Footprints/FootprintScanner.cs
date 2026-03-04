#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Footprints
{
    /// <summary>
    /// Scans for common forensic "footprints" and residue locations (temp files, jump lists, caches, dumps).
    /// Produces a list of entries so the caller can decide to shred / purge.
    /// </summary>
    public static class FootprintScanner
    {
        public enum Category
        {
            TempUser,
            TempSystem,
            RecycleBin,
            ThumbCache,
            RecentJumpLists,
            Prefetch,
            CrashDumps,
            MiniDumps,
            WindowsLogs
        }

        public sealed record Entry(Category Category, string Path, long Size, DateTime? LastWriteUtc);

        public sealed record Report(IReadOnlyList<Entry> Entries)
        {
            public long TotalBytes => Entries.Sum(e => e.Size);
            public long BytesBy(Category cat) => Entries.Where(e => e.Category == cat).Sum(e => e.Size);
            public IEnumerable<IGrouping<Category, Entry>> GroupByCategory() => Entries.GroupBy(e => e.Category);
        }

        /// <summary>
        /// Performs a best-effort scan. Set <paramref name="includeSystemWide"/> to look at system-wide locations (requires admin for some items).
        /// </summary>
        public static Report Scan(bool includeSystemWide = true, ILogger? logger = null)
        {
            var entries = new List<Entry>();
            try
            {
                // Current user temp
                SafeCollect(entries, Category.TempUser, GetTempFiles(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), logger);

                // System temp
                if (includeSystemWide)
                {
                    SafeCollect(entries, Category.TempSystem, EnumerateFiles(@"C:\Windows\Temp", "*", SearchOption.AllDirectories), logger);
                }

                // Thumb caches
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
                SafeCollect(entries, Category.ThumbCache, EnumerateFiles(explorer, "thumbcache_*.db", SearchOption.TopDirectoryOnly), logger);

                // Recent Jump Lists (Automatic & Custom Destinations)
                var recent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");
                SafeCollect(entries, Category.RecentJumpLists, EnumerateFiles(Path.Combine(recent, "AutomaticDestinations"), "*.automaticDestinations-ms", SearchOption.TopDirectoryOnly), logger);
                SafeCollect(entries, Category.RecentJumpLists, EnumerateFiles(Path.Combine(recent, "CustomDestinations"), "*.customDestinations-ms", SearchOption.TopDirectoryOnly), logger);

                // Prefetch
                if (includeSystemWide)
                {
                    SafeCollect(entries, Category.Prefetch, EnumerateFiles(@"C:\Windows\Prefetch", "*.pf", SearchOption.TopDirectoryOnly), logger);
                }

                // Crash dumps
                SafeCollect(entries, Category.CrashDumps, EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"), "*.dmp", SearchOption.AllDirectories), logger);
                if (includeSystemWide)
                {
                    SafeCollect(entries, Category.MiniDumps, EnumerateFiles(@"C:\Windows\Minidump", "*.dmp", SearchOption.AllDirectories), logger);
                }

                // Windows logs (selected dirs)
                if (includeSystemWide)
                {
                    SafeCollect(entries, Category.WindowsLogs, EnumerateFiles(@"C:\Windows\Logs", "*.*", SearchOption.TopDirectoryOnly), logger);
                    SafeCollect(entries, Category.WindowsLogs, EnumerateFiles(@"C:\Windows\System32\LogFiles", "*.*", SearchOption.AllDirectories), logger);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Footprint scan encountered an issue.");
            }

            return new Report(entries);
        }

        // -------- helpers --------

        private static IEnumerable<string> GetTempFiles(string localAppData)
        {
            // %TEMP% can be redirected; use framework API
            string userTemp = Path.GetTempPath();
            foreach (var p in EnumerateFiles(userTemp, "*", SearchOption.TopDirectoryOnly)) yield return p;

            // Also scan AppData\Local\Temp if distinct
            string another = Path.Combine(localAppData, "Temp");
            if (!string.Equals(Path.GetFullPath(userTemp).TrimEnd('\\'), Path.GetFullPath(another).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var p in EnumerateFiles(another, "*", SearchOption.TopDirectoryOnly)) yield return p;
            }
        }

        private static void SafeCollect(List<Entry> output, Category cat, IEnumerable<string> files, ILogger? logger)
        {
            foreach (var path in files)
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists) continue;
                    output.Add(new Entry(cat, fi.FullName, fi.Length, fi.LastWriteTimeUtc));
                }
                catch (Exception ex) { logger?.LogDebug(ex, "Failed to stat {Path}", path); }
            }
        }

        private static IEnumerable<string> EnumerateFiles(string root, string pattern, SearchOption opt)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;

            Stack<string> dirs = new();
            dirs.Push(root);

            while (dirs.Count > 0)
            {
                string current = dirs.Pop();
                IEnumerable<string> files = Array.Empty<string>();
                try { files = Directory.EnumerateFiles(current, pattern, SearchOption.TopDirectoryOnly); }
                catch { /* skip */ }

                foreach (var f in files) yield return f;

                if (opt == SearchOption.AllDirectories)
                {
                    IEnumerable<string> subs = Array.Empty<string>();
                    try { subs = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly); }
                    catch { /* skip */ }

                    foreach (var d in subs) dirs.Push(d);
                }
            }
        }
    }
}
