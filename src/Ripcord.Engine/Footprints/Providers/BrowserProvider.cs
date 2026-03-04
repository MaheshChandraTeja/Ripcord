#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Footprints.Providers
{
    /// <summary>
    /// Finds browser caches and databases for Chrome/Edge/Firefox user profiles.
    /// (Read-only detection; caller decides whether to shred.)
    /// </summary>
    public static class BrowserProvider
    {
        public static IEnumerable<FootprintScanner.Entry> Scan(ILogger? logger = null)
        {
            var entries = new List<FootprintScanner.Entry>();

            var local   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Chrome
            var chrome = Path.Combine(local, "Google", "Chrome", "User Data");
            CollectProfile(entries, chrome, logger);

            // Edge
            var edge = Path.Combine(local, "Microsoft", "Edge", "User Data");
            CollectProfile(entries, edge, logger);

            // Firefox
            var fxProfilesRoot = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(fxProfilesRoot))
            {
                IEnumerable<string> profiles = Array.Empty<string>();
                try { profiles = Directory.EnumerateDirectories(fxProfilesRoot, "*", SearchOption.TopDirectoryOnly); } catch { }

                foreach (var prof in profiles)
                {
                    Collect(entries, FootprintScanner.Category.TempUser, Files(prof, "places.sqlite"), logger);
                    Collect(entries, FootprintScanner.Category.TempUser, Files(prof, "favicons.sqlite"), logger);
                    Collect(entries, FootprintScanner.Category.TempUser, Files(prof, "cookies.sqlite"), logger);
                    Collect(entries, FootprintScanner.Category.TempUser, Files(Path.Combine(prof, "cache2", "entries"), "*"), logger);
                }
            }

            return entries;
        }

        private static void CollectProfile(List<FootprintScanner.Entry> output, string userDataRoot, ILogger? logger)
        {
            if (!Directory.Exists(userDataRoot)) return;

            IEnumerable<string> profiles = Array.Empty<string>();
            try { profiles = Directory.EnumerateDirectories(userDataRoot, "*", SearchOption.TopDirectoryOnly); } catch { }

            foreach (var prof in profiles)
            {
                // SQLite DBs
                Collect(output, FootprintScanner.Category.TempUser, Files(prof, "History"), logger);
                Collect(output, FootprintScanner.Category.TempUser, Files(prof, "Cookies"), logger);
                Collect(output, FootprintScanner.Category.TempUser, Files(prof, "Login Data"), logger);
                Collect(output, FootprintScanner.Category.TempUser, Files(prof, "Web Data"), logger);
                Collect(output, FootprintScanner.Category.TempUser, Files(prof, "Favicons"), logger);
                Collect(output, FootprintScanner.Category.TempUser, Files(Path.Combine(prof, "Network"), "Cookies"), logger);

                // Caches
                Collect(output, FootprintScanner.Category.TempUser, All(Path.Combine(prof, "Cache"), "*"), logger);
                Collect(output, FootprintScanner.Category.TempUser, All(Path.Combine(prof, "Network", "Cache"), "*"), logger);
                Collect(output, FootprintScanner.Category.TempUser, All(Path.Combine(prof, "Media Cache"), "*"), logger);
                Collect(output, FootprintScanner.Category.TempUser, All(Path.Combine(prof, "Code Cache"), "*"), logger);
            }
        }

        // helpers
        private static IEnumerable<string> Files(string root, string pattern)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); } catch { }
            foreach (var f in files) yield return f;
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
                catch (Exception ex) { logger?.LogDebug(ex, "BrowserProvider: failed to stat {Path}", p); }
            }
        }
    }
}
