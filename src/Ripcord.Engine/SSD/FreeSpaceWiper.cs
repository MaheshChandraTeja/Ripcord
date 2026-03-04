#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Common;

namespace Ripcord.Engine.SSD
{
    /// <summary>
    /// High-level façade for free-space wiping on a target volume.
    /// </summary>
    public sealed class FreeSpaceWiper
    {
        private readonly ILogger? _logger;
        public FreeSpaceWiper(ILogger? logger = null) { _logger = logger; }

        public event EventHandler<TrimExecutor.FreeWipeProgress>? Progress;

        public async Task<Result<TrimExecutor.FreeWipeResult>> WipeAsync(string anyPathOnVolume, long reserveBytes, CancellationToken ct = default)
        {
            var caps = VolumeCapabilities.Detect(anyPathOnVolume);
            var plan = TrimPlanner.Build(caps, reserveBytes, preferredChunkBytes: 1 * 1024 * 1024, maxFiles: 512);

            var exec = new TrimExecutor(_logger);
            if (Progress is not null)
            {
                exec.Progress += (s, e) => Progress?.Invoke(this, e);
            }

            return await exec.RunAsync(plan, ct).ConfigureAwait(false);
        }
    }
}
