#nullable enable
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Shred;
using Xunit;

namespace E2E.Tests
{
    /// <summary>
    /// Ensures the engine behaves reasonably when files are exclusively locked.
    /// First attempt should not delete; after releasing the lock, a retry should succeed.
    /// </summary>
    public sealed class LockedFileMatrixTests
    {
        [Fact]
        public async Task LockedFile_RetryAfterRelease_Succeeds()
        {
            string dir = Directory.CreateTempSubdirectory("ripcord_lock_").FullName;
            string path = Path.Combine(dir, "locked.txt");
            await File.WriteAllTextAsync(path, "hello");

            // Lock file exclusively
            using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var logger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning)).CreateLogger<ShredEngine>();
            var engine = new ShredEngine(logger);
            var req = new ShredJobRequest(path, Profiles.Default(), DryRun: false, DeleteAfter: true, Recurse: false);

            var r1 = await engine.RunAsync(req, CancellationToken.None);
            // Depending on implementation, this may be Success=false or partial. Either way the file must still exist.
            Assert.True(File.Exists(path));

            // Release lock
            lockStream.Dispose();

            var r2 = await engine.RunAsync(req, CancellationToken.None);
            Assert.True(r2.Success, r2.Error ?? "Second attempt failed");
            Assert.False(File.Exists(path));

            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
