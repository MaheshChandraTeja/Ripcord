#nullable enable
using System;
using Ripcord.Engine.SSD;
using Xunit;

namespace Engine.Tests
{
    public sealed class TrimPlannerTests
    {
        [Fact]
        public void Build_RespectsReserve_And_Limits()
        {
            // Fake capabilities snapshot
            var caps = new VolumeCapabilities(
                VolumeRoot: @"C:\",
                FileSystem: "NTFS",
                ClusterSizeBytes: 4096,
                TotalBytes: 500L * 1024 * 1024 * 1024,
                FreeBytes: 120L * 1024 * 1024 * 1024,
                SupportsSparse: true,
                TrimEnabled: true,
                IncursSeekPenalty: false);

            long reserve = 20L * 1024 * 1024 * 1024; // 20 GiB
            var plan = TrimPlanner.Build(caps, reserve, preferredChunkBytes: 1024 * 1024, maxFiles: 300);

            Assert.Equal(@"C:\", plan.VolumeRoot);
            Assert.True(plan.TargetBytesToFill <= caps.FreeBytes - reserve + 4096); // small rounding
            Assert.InRange(plan.MaxFiles, 1, 300);
            Assert.InRange(plan.ChunkBytes, (int)caps.ClusterSizeBytes, 8 * 1024 * 1024);
        }

        [Fact]
        public void Build_SmallFreeSpace_NoWork()
        {
            var caps = new VolumeCapabilities(
                VolumeRoot: @"D:\",
                FileSystem: "NTFS",
                ClusterSizeBytes: 4096,
                TotalBytes: 10L * 1024 * 1024 * 1024,
                FreeBytes: 1L * 1024 * 1024, // 1 MiB
                SupportsSparse: true,
                TrimEnabled: true,
                IncursSeekPenalty: false);

            var plan = TrimPlanner.Build(caps, reserveBytes: 0, preferredChunkBytes: 1 * 1024 * 1024);
            Assert.Equal(0, plan.TargetBytesToFill);
        }
    }
}
