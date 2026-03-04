#nullable enable
using System;
using System.Collections.Generic;
using Ripcord.Engine.Vss;
using Xunit;

namespace Engine.Tests
{
    public sealed class ShadowMapperTests
    {
        [Fact]
        public void MapToSnapshotPaths_BuildsCandidates()
        {
            var target = @"C:\folder\sub\file.txt";
            var snaps = new List<ShadowEnumerator.ShadowInfo>
            {
                new ShadowEnumerator.ShadowInfo(
                    Id: Guid.NewGuid(),
                    VolumeName: @"C:\",
                    DeviceObject: @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy10",
                    InstallDateUtc: DateTime.UtcNow,
                    Persistent: true,
                    ClientAccessible: true,
                    State: 12)
            };

            var mapped = ShadowMapper.MapToSnapshotPaths(target, snaps, checkExists: false);
            Assert.Single(mapped);
            Assert.Equal(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy10\folder\sub\file.txt", mapped[0]);
        }

        [Fact]
        public void TryGetNewestExistingPath_NoThrowWhenNotPresent()
        {
            // Should not throw even if there are no snapshots. It will return false/null.
            bool ok = ShadowMapper.TryGetNewestExistingPath(@"C:\this\is\unlikely\to\exist.file", out var snapPath);
            Assert.False(ok);
            Assert.Null(snapPath);
        }
    }
}
