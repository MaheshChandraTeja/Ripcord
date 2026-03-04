#nullable enable
using System;

namespace Ripcord.Engine.SSD
{
    /// <summary>
    /// Computes a feasible plan for free-space wiping on a volume.
    /// </summary>
    public sealed record TrimPlan(
        string VolumeRoot,
        long TargetBytesToFill,
        long ReserveBytes,
        int MaxFiles,
        int ChunkBytes
    )
    {
        public long BytesPerFile => (long)ChunkBytes * 512; // Fudge factor: group of chunks/file
        public int EstimatedFiles => (int)Math.Max(1, Math.Min(MaxFiles, TargetBytesToFill / Math.Max(1, BytesPerFile)));
    }

    public static class TrimPlanner
    {
        /// <summary>
        /// Build a plan to fill (and then discard) the majority of free space while keeping <paramref name="reserveBytes"/> available.
        /// </summary>
        /// <param name="caps">Volume capabilities.</param>
        /// <param name="reserveBytes">Bytes to keep free.</param>
        /// <param name="preferredChunkBytes">Buffer/chunk size for streaming writes.</param>
        /// <param name="maxFiles">Upper bound on number of wipe files.</param>
        public static TrimPlan Build(VolumeCapabilities caps, long reserveBytes, int preferredChunkBytes = 1024 * 1024, int maxFiles = 256)
        {
            if (reserveBytes < 0) reserveBytes = 0;
            if (preferredChunkBytes < caps.ClusterSizeBytes) preferredChunkBytes = (int)caps.ClusterSizeBytes;
            if (preferredChunkBytes > 8 * 1024 * 1024) preferredChunkBytes = 8 * 1024 * 1024;

            long free = Math.Max(0, caps.FreeBytes - reserveBytes);
            // Don't attempt to fill absurdly small free space
            if (free < caps.ClusterSizeBytes * 128) free = 0;

            maxFiles = Math.Clamp(maxFiles, 1, 4096);

            return new TrimPlan(
                VolumeRoot: caps.VolumeRoot,
                TargetBytesToFill: free,
                ReserveBytes: reserveBytes,
                MaxFiles: maxFiles,
                ChunkBytes: preferredChunkBytes
            );
        }
    }
}
