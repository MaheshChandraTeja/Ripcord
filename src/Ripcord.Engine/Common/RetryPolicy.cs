#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Common
{
    /// <summary>
    /// Simple exponential backoff with jitter for transient operations.
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>
        /// Executes <paramref name="operation"/> with retries on exceptions for which <paramref name="isTransient"/> returns true.
        /// </summary>
        public static async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<Exception, bool> isTransient,
            int maxRetries = 5,
            TimeSpan? baseDelay = null,
            double jitterRatio = 0.25,
            ILogger? logger = null,
            CancellationToken ct = default)
        {
            if (operation is null) throw new ArgumentNullException(nameof(operation));
            if (isTransient is null) throw new ArgumentNullException(nameof(isTransient));
            if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));
            if (jitterRatio < 0 || jitterRatio > 1) throw new ArgumentOutOfRangeException(nameof(jitterRatio));

            var delay = baseDelay ?? TimeSpan.FromMilliseconds(200);
            int attempt = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await operation(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (isTransient(ex) && attempt < maxRetries)
                {
                    attempt++;
                    var next = ComputeDelay(delay, attempt, jitterRatio);
                    logger?.LogDebug(ex, "Retryable error (attempt {Attempt}/{Max}). Delaying {Delay}.", attempt, maxRetries, next);
                    await Task.Delay(next, ct).ConfigureAwait(false);
                    continue;
                }
            }
        }

        /// <summary>
        /// Non-generic helper that returns true when the action eventually succeeds.
        /// </summary>
        public static Task<bool> ExecuteAsync(
            Func<CancellationToken, Task> operation,
            Func<Exception, bool> isTransient,
            int maxRetries = 5,
            TimeSpan? baseDelay = null,
            double jitterRatio = 0.25,
            ILogger? logger = null,
            CancellationToken ct = default)
        {
            return ExecuteAsync<bool>(async token =>
            {
                await operation(token).ConfigureAwait(false);
                return true;
            }, isTransient, maxRetries, baseDelay, jitterRatio, logger, ct);
        }

        private static TimeSpan ComputeDelay(TimeSpan baseDelay, int attempt, double jitterRatio)
        {
            // Exponential backoff: base * 2^(attempt-1), capped at 10s
            double mult = Math.Pow(2, Math.Max(0, attempt - 1));
            double millis = Math.Min(10_000, baseDelay.TotalMilliseconds * mult);

            // Jitter: +/- jitterRatio
            double jitter = 1.0 + ((Random.Shared.NextDouble() * 2 - 1) * jitterRatio);
            millis = Math.Max(0, millis * jitter);

            return TimeSpan.FromMilliseconds(millis);
        }
    }
}
