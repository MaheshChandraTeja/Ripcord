#nullable enable
using System;
using System.IO;
using System.Linq;
using Ripcord.Engine.Footprints;
using Xunit;

namespace Engine.Tests
{
    public sealed class FootprintScannerTests
    {
        [Fact]
        public void Scanner_FindsTempFileInUserTemp()
        {
            // Create a temp file in the user's temp dir; scanner should include it in TempUser category.
            string tempDir = Path.GetTempPath();
            string path = Path.Combine(tempDir, "ripcord_test_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(path, "hello");

            try
            {
                var report = FootprintScanner.Scan(includeSystemWide: false);
                var found = report.Entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
                Assert.True(found, $"Expected scanner to include temp file: {path}");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
