#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Ripcord.Engine.Journal;
using Ripcord.Engine.Shred;
using Ripcord.Engine.Validation;
using Xunit;

namespace Engine.Tests
{
    public sealed class ShredEngineTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "RipcordTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static FileInfo NewTempFile(string dir, long bytes, byte fill)
        {
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".bin");
            Span<byte> buf = stackalloc byte[8192];
            buf.Fill(fill);
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.SequentialScan);
            long remaining = bytes;
            while (remaining > 0)
            {
                int toWrite = (int)Math.Min(buf.Length, remaining);
                fs.Write(buf[..toWrite]);
                remaining -= toWrite;
            }
            fs.Flush(true);
            return new FileInfo(path);
        }

        [Fact(DisplayName = "VerificationProbe accepts constant-pattern file (ZeroFill)")]
        public async Task VerificationProbe_ConstantPattern_Ok()
        {
            string dir = NewTempDir();
            try
            {
                var file = NewTempFile(dir, bytes: 1_000_000, fill: 0x00);
                var lastPass = new OverwritePass("Zeros", OverwritePatternType.Constant, 0x00);

                bool ok = await VerificationProbe.SamplePatternAsync(file, lastPass, fraction: 0.02, minSamples: 8, bytesPerSample: 2048);
                Assert.True(ok);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact(DisplayName = "VerificationProbe skips validation for Random pass")]
        public async Task VerificationProbe_RandomPass_Skips()
        {
            string dir = NewTempDir();
            try
            {
                // Write random-looking data (not guaranteed, but adequate here)
                var file = NewTempFile(dir, bytes: 256_000, fill: 0xAA); // content doesn't matter for Random
                var lastPass = new OverwritePass("Random", OverwritePatternType.Random);

                bool ok = await VerificationProbe.SamplePatternAsync(file, lastPass, fraction: 0.05, minSamples: 4, bytesPerSample: 1024);
                Assert.True(ok); // Random pass -> probe should return true (skip)
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact(DisplayName = "JobJournal rolls files by size and retains at most N files")]
        public async Task JobJournal_Rolling_Works()
        {
            string dir = NewTempDir();
            await using var journal = new JobJournal(directory: dir, maxBytesPerFile: 1024, maxFiles: 2);

            // Write a bunch of lines to force multiple files & rolling
            for (int i = 0; i < 200; i++)
            {
                var rec = new JournalRecord(
                    Timestamp: DateTimeOffset.Now,
                    Level: JournalLevel.Info,
                    Event: "TestEvent",
                    TargetPath: $"C:\\dummy\\file{i}.bin",
                    ProfileId: "zero",
                    Message: new string('X', 200) // Ensure we exceed 1KB per few lines
                );
                await journal.WriteAsync(rec);
            }

            // Dispose to flush and close
            await journal.DisposeAsync();

            // Verify max 2 files retained
            var files = new DirectoryInfo(dir).GetFiles("engine-*.jsonl");
            Assert.True(files.Length <= 2, $"Expected <= 2 log files, found {files.Length}");
        }

        [Fact(DisplayName = "RenameNoise changes name and preserves extension")]
        public async Task RenameNoise_Basics()
        {
            string dir = NewTempDir();
            try
            {
                string ext = ".dat";
                var file = NewTempFile(dir, bytes: 8_192, fill: 0x5A);
                string origPathWithExt = Path.ChangeExtension(file.FullName, ext);
                File.Move(file.FullName, origPathWithExt);
                file = new FileInfo(origPathWithExt);

                string origName = file.Name;
                await RenameNoise.ApplyAsync(file, iterations: 5, logger: NullLogger.Instance);
                // After ApplyAsync, 'file' variable points to old FileInfo; find the newest file with same extension
                var after = new DirectoryInfo(dir).EnumerateFiles("*" + ext).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                Assert.NotNull(after);
                Assert.True(after!.Exists);
                Assert.EndsWith(ext, after.Name, StringComparison.OrdinalIgnoreCase);
                Assert.NotEqual(origName, after.Name);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact(DisplayName = "MftSlackCleaner runs best-effort without throwing and cleans workspace")]
        public async Task MftSlackCleaner_Runs()
        {
            string dir = NewTempDir();
            var cleaner = new MftSlackCleaner(NullLogger.Instance);
            await cleaner.CleanAsync(new DirectoryInfo(dir), batches: 1, filesPerBatch: 64);

            // Work directory ('.ripcord_mft_fill') should be gone (best-effort)
            string work = Path.Combine(dir, ".ripcord_mft_fill");
            Assert.False(Directory.Exists(work));
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
