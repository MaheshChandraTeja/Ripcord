#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Footprints.Providers
{
    /// <summary>
    /// Finds Microsoft Office residue: lock files (~$), AutoRecover / backup files, and document cache.
    /// </summary>
    public static class OfficeProvider
    {
        public static IEnumerable<FootprintScanner.Entry> Scan(ILogger? logger = null)
        {
            var entries = new List<FootprintScanner.Entry>();

            var userDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop  = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // we'll scan Downloads below safely
            var roaming  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local    = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 1) Lock/owner files (~$*) in common user folders (top-level + one level deep to be safe)
            Collect(entries, FootprintScanner.Category.TempUser, OneLevel(userDocs, "~$*"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, OneLevel(desktop,  "~$*"), logger);
            var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Collect(entries, FootprintScanner.Category.TempUser, OneLevel(dl, "~$*"), logger);

            // 2) AutoRecover & backup
            var wordAuto = Path.Combine(roaming, "Microsoft", "Word");
            Collect(entries, FootprintScanner.Category.TempUser, Files(wordAuto, "*.asd"), logger); // Word AutoRecover
            Collect(entries, FootprintScanner.Category.TempUser, Files(wordAuto, "*.wbk"), logger); // Word backups

            var pptAuto = Path.Combine(roaming, "Microsoft", "PowerPoint");
            Collect(entries, FootprintScanner.Category.TempUser, Files(pptAuto, "*.ppt*.tmp"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, Files(pptAuto, "*.ppt*.autosu*"), logger); // loose catch

            var excelAuto = Path.Combine(roaming, "Microsoft", "Excel");
            Collect(entries, FootprintScanner.Category.TempUser, Files(excelAuto, "*.xar"), logger); // older autosave ext
            Collect(entries, FootprintScanner.Category.TempUser, Files(excelAuto, "*.xls*~*"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, Files(excelAuto, "~$*.xls*"), logger);

            // 3) Office Document Cache (ODC) — OfficeFileCache (deprecated but still present)
            var odc = Path.Combine(local, "Microsoft", "Office", "16.0", "OfficeFileCache");
            Collect(entries, FootprintScanner.Category.TempUser, All(odc, "*.fsd"), logger);
            Collect(entries, FootprintScanner.Category.TempUser, All(odc, "*.dat"), logger);

            return entries;
        }

        // ------- helpers -------

        private static IEnumerable<string> Files(string root, string pattern)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); } catch { }
            foreach (var f in files) yield return f;
        }

        private static IEnumerable<string> OneLevel(string root, string pattern)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;

            // top-level
            foreach (var f in Files(root, pattern)) yield return f;

            // one directory deep
            IEnumerable<string> subs = Array.Empty<string>();
            try { subs = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly); } catch { }
            foreach (var d in subs)
            {
                foreach (var f in Files(d, pattern)) yield return f;
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
                catch (Exception ex) { logger?.LogDebug(ex, "OfficeProvider: failed to stat {Path}", p); }
            }
        }
    }
}
