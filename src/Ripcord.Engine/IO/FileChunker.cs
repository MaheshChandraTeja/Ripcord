using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ripcord.Engine.IO
{
    /// <summary>
    /// File chunk write helpers with progress callbacks. Allocates a single reusable buffer.
    /// </summary>
    public static class FileChunker
    {
        /// <summary>
        /// Writes <paramref name="length"/> bytes to <paramref name="fs"/> using a reusable buffer.
        /// For each iteration, <paramref name="fill"/> is invoked to populate the next slice.
        /// </summary>
        public static async Task WriteAsync(
            FileStream fs,
            long length,
            int bufferSize,
            Func<Memory<byte>, int> fill,
            Action<long>? onAdvanced = null,
            CancellationToken ct = default)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));

            byte[] buffer = new byte[bufferSize];
            long written = 0;

            while (written < length)
            {
                ct.ThrowIfCancellationRequested();
                int toWrite = (int)Math.Min(buffer.Length, length - written);
                int filled = fill(buffer.AsMemory(0, toWrite));
                if (filled <= 0) throw new IOException("No data filled for write pass.");

                await fs.WriteAsync(buffer.AsMemory(0, filled), ct).ConfigureAwait(false);
                written += filled;
                onAdvanced?.Invoke(written);
            }

            await fs.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}
