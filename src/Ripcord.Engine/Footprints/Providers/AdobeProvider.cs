#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Footprints.Providers
{
    /// <summary>
    /// Finds Adobe product residues: Acrobat autosaves/caches, Photoshop temp/autorecover, Lightroom caches.
    /// </summary>
    public static class AdobeProvider
    {
        public static IEnumerable<FootprintScanner.Entry> Scan(ILogger? logger = null)
        {
            var entries = new List<FootprintScanner.Entry>();

            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

            // Acrobat
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(roaming, "Adobe", "Acrobat"), "*.tmp"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(roaming, "Adobe", "Acrobat", "DC", "AutoSave"), "*.*"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(local,   "Adobe", "Acrobat", "Cache"), "*.*"), logger);

            // Photoshop
            Collect(entries, FootprintScanner.Category.TempUser, Files(Path.Combine(local, "Temp"), "Photoshop Temp*"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(roaming, "Adobe"), "AutoRecover*"), logger);

            // Lightroom
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(pictures, "Lightroom"), "*.lrcat.lock"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, All(Path.Combine(roaming, "Adobe", "Lightroom", "Caches"), "*.*"), logger);

            return entries;
        }

        // helpers shared pattern
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
                catch (Exception ex) { logger?.LogDebug(ex, "AdobeProvider: failed to stat {Path}", p); }
            }
        }
    }
}
