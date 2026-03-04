#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Ripcord.Engine.Shred;
using Xunit;
using Xunit.Sdk;

namespace Engine.Tests
{
    public sealed class AdsEnumeratorTests
    {
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "RipcordADSTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact(DisplayName = "Enumerate returns empty on files without ADS (Windows only)")]
        public void Enumerate_NoAds_Empty()
        {
            if (!IsWindows) throw new SkipException("ADS is a Windows/NTFS feature.");

            string dir = NewTempDir();
            try
            {
                string path = Path.Combine(dir, "plain.txt");
                File.WriteAllText(path, "hello");

                var streams = AdsEnumerator.Enumerate(path).ToList();
                Assert.True(streams.Count == 0, $"Expected 0 ADS streams, got {streams.Count}");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact(DisplayName = "Create, enumerate and delete ADS (Windows + NTFS only)")]
        public void CreateEnumerateDelete_Ads_Works()
        {
            if (!IsWindows) throw new SkipException("ADS is a Windows/NTFS feature.");

            string dir = NewTempDir();
            try
            {
                string baseFile = Path.Combine(dir, "withads.bin");
                File.WriteAllBytes(baseFile, new byte[32]);

                string adsPath = baseFile + ":rcstream1";
                try
                {
                    File.WriteAllText(adsPath, "secret-data"); // will throw on non-NTFS
                }
                catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException)
                {
                    // Likely not NTFS or ADS blocked. Skip gracefully.
                    throw new SkipException($"Unable to create ADS on this volume: {ex.Message}");
                }

                // Enumerate should see rcstream1::$DATA
                var streams = AdsEnumerator.Enumerate(baseFile).ToList();
                Assert.True(streams.Any(s => s.Name.StartsWith(":rcstream1", StringComparison.OrdinalIgnoreCase)),
                            $"Expected to find ':rcstream1', got: {string.Join(", ", streams.Select(s => s.Name))}");

                // Delete and verify gone
                AdsEnumerator.DeleteAll(baseFile, NullLogger.Instance);
                var after = AdsEnumerator.Enumerate(baseFile).ToList();
                Assert.Empty(after);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
