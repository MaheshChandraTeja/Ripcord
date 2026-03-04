#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ripcord.Engine.Diagnostics
{
    /// <summary>
    /// Global, opt-in fault injection for chaos and E2E tests. Engine code may call
    /// <see cref="Hit"/> at strategic points (e.g. "Shred.BeforeDelete", "FileChunker.AfterWrite").
    /// When a matching rule is active, <see cref="Hit"/> throws or cancels to simulate faults
    /// like power loss, IO errors, etc.
    ///
    /// Usage in engine code:
    ///   FaultInjector.Hit("Shred.Progress",
    ///       new FaultContext(currentFile, bytesProcessed, bytesTotal, passIndex), ct);
    /// </summary>
    public static class FaultInjector
    {
        private static readonly ConcurrentDictionary<string, Rule> _rules = new(StringComparer.OrdinalIgnoreCase);

        public static void Reset() => _rules.Clear();

        public static void Enable(string point,
                                  FaultMode mode = FaultMode.Throw,
                                  int maxHits = 1,
                                  double? percentThreshold = null,
                                  long? bytesThreshold = null,
                                  string? filePathContains = null)
        {
            _rules[point] = new Rule(mode, maxHits, percentThreshold, bytesThreshold, filePathContains);
        }

        public static void Disable(string point) => _rules.TryRemove(point, out _);

        /// <summary>Called by engine at injection points. Throws on trigger.</summary>
        public static void Hit(string point, FaultContext? ctx = null, CancellationToken ct = default)
        {
            if (!_rules.TryGetValue(point, out var rule)) return;

            if (!rule.ShouldFire(ctx)) return;

            switch (rule.Mode)
            {
                case FaultMode.Throw:
                    throw new InjectedFaultException(point, ctx);
                case FaultMode.Cancel:
                    // Simulate abrupt cancellation (e.g., power loss). If token provided, throw OCE with it.
                    if (ct.CanBeCanceled) throw new OperationCanceledException(ct);
                    throw new OperationCanceledException("Injected cancellation");
                case FaultMode.FailFast:
                    // For extreme chaos runs; avoid in unit tests
                    Environment.FailFast($"Injected FailFast at {point} ({ctx})");
                    break;
            }
        }

        // ------------- types -------------

        public enum FaultMode { Throw, Cancel, FailFast }

        public sealed class InjectedFaultException : Exception
        {
            public string Point { get; }
            public FaultContext? Context { get; }
            public InjectedFaultException(string point, FaultContext? ctx)
                : base($"Injected fault at '{point}' ({ctx})")
            {
                Point = point; Context = ctx;
            }
        }

        public sealed class FaultContext
        {
            public string? FilePath { get; }
            public long? BytesProcessed { get; }
            public long? BytesTotal { get; }
            public int? PassIndex { get; }

            public FaultContext(string? filePath = null, long? bytesProcessed = null, long? bytesTotal = null, int? passIndex = null)
            {
                FilePath = filePath; BytesProcessed = bytesProcessed; BytesTotal = bytesTotal; PassIndex = passIndex;
            }

            public double? Percent => (BytesProcessed.HasValue && BytesTotal.HasValue && BytesTotal.Value > 0)
                ? (100.0 * BytesProcessed.Value / BytesTotal.Value)
                : null;

            public override string ToString()
                => $"file={Path.GetFileName(FilePath) ?? "?"}, bytes={BytesProcessed}/{BytesTotal}, pass={PassIndex}, pct={Percent:0.##}%";
        }

        private sealed class Rule
        {
            private int _hits;
            public FaultMode Mode { get; }
            public int MaxHits { get; }
            public double? PercentThreshold { get; }
            public long? BytesThreshold { get; }
            public string? FileContains { get; }

            public Rule(FaultMode mode, int maxHits, double? percentThreshold, long? bytesThreshold, string? fileContains)
            {
                Mode = mode;
                MaxHits = Math.Max(1, maxHits);
                PercentThreshold = percentThreshold;
                BytesThreshold = bytesThreshold;
                FileContains = fileContains;
            }

            public bool ShouldFire(FaultContext? ctx)
            {
                // Max hits check
                if (Interlocked.CompareExchange(ref _hits, 0, 0) >= MaxHits) return false;

                // Thresholds
                if (PercentThreshold.HasValue)
                {
                    var pct = ctx?.Percent;
                    if (!pct.HasValue || pct.Value < PercentThreshold.Value) return false;
                }

                if (BytesThreshold.HasValue)
                {
                    var bp = ctx?.BytesProcessed;
                    if (!bp.HasValue || bp.Value < BytesThreshold.Value) return false;
                }

                if (!string.IsNullOrWhiteSpace(FileContains) && (ctx?.FilePath is null || ctx.FilePath.IndexOf(FileContains, StringComparison.OrdinalIgnoreCase) < 0))
                    return false;

                // We will fire; increment hits
                Interlocked.Increment(ref _hits);
                return true;
            }
        }
    }
}
