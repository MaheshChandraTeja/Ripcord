#nullable enable
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ripcord.App.Telemetry
{
    /// <summary>
    /// Lightweight, resilient, local metrics sink (JSONL) with batching + daily rolling files.
    /// No external deps; safe to call from hot paths. Intended for opt-in, local diagnostics.
    /// </summary>
    public sealed class LocalMetricsStore : IAsyncDisposable
    {
        private readonly ILogger<LocalMetricsStore>? _log;
        private readonly ConcurrentQueue<Metric> _queue = new();
        private readonly object _flushGate = new();
        private readonly Timer _timer;
        private long _queued;
        private readonly int _batchSize;
        private readonly string _root;
        private readonly int _retentionDays;
        private DateTime _lastPrune = DateTime.MinValue;

        public LocalMetricsStore(ILogger<LocalMetricsStore>? log = null, string? root = null, int batchSize = 64, int retentionDays = 14)
        {
            _log = log;
            _batchSize = Math.Max(8, batchSize);
            _retentionDays = Math.Clamp(retentionDays, 1, 90);

            _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Telemetry");
            Directory.CreateDirectory(_root);

            // Background cadence flush
            _timer = new Timer(async _ => await SafeFlushAsync().ConfigureAwait(false), null, dueTime: TimeSpan.FromSeconds(2), period: TimeSpan.FromSeconds(2));
        }

        // ------------- Public API -------------

        public static LocalMetricsStore CreateDefault(ILoggerFactory? lf = null)
            => new(lf?.CreateLogger<LocalMetricsStore>());

        public void Counter(string name, long delta = 1, IDictionary<string, string>? tags = null)
            => Enqueue(Metric.Counter(name, delta, tags));

        public void Gauge(string name, double value, IDictionary<string, string>? tags = null)
            => Enqueue(Metric.Gauge(name, value, tags));

        public void Event(string name, IDictionary<string, string>? fields = null)
            => Enqueue(Metric.Event(name, fields));

        public IDisposable Time(string name, IDictionary<string, string>? tags = null)
            => new Scope(name, tags, this);

        public async Task FlushAsync() => await FlushInternalAsync().ConfigureAwait(false);

        // ------------- Internals -------------

        private void Enqueue(Metric m)
        {
            _queue.Enqueue(m);
            Interlocked.Increment(ref _queued);
            // Backpressure-ish: if we cross batch size, opportunistically flush
            if (Interlocked.Read(ref _queued) >= _batchSize)
                _ = SafeFlushAsync();
        }

        private async Task SafeFlushAsync()
        {
            try { await FlushInternalAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log?.LogDebug(ex, "LocalMetricsStore flush failed."); }
        }

        private async Task FlushInternalAsync()
        {
            // Fast path: nothing to do
            if (_queue.IsEmpty) { MaybePrune(); return; }

            List<Metric> drained = new(_batchSize * 2);
            while (drained.Count < _batchSize && _queue.TryDequeue(out var m))
                drained.Add(m);
            if (drained.Count == 0) { MaybePrune(); return; }

            Interlocked.Add(ref _queued, -drained.Count);

            // Write JSONL
            var filePath = Path.Combine(_root, $"metrics-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            // Serialize outside lock to reduce contention
            var sb = new StringBuilder(capacity: drained.Count * 128);
            foreach (var m in drained)
                sb.AppendLine(JsonSerializer.Serialize(m, JsonOpts));
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            lock (_flushGate)
            {
                using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true);
                fs.Write(bytes, 0, bytes.Length);
            }

            MaybePrune();

            // If queue still large, schedule another flush quickly
            if (Interlocked.Read(ref _queued) >= _batchSize)
                _ = Task.Run(SafeFlushAsync);
            await Task.CompletedTask;
        }

        private void MaybePrune()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPrune) < TimeSpan.FromMinutes(30)) return;
            _lastPrune = now;

            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "metrics-*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if (name.Length >= 16 && DateTime.TryParseExact(name.AsSpan(8, 8), "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var day))
                    {
                        if (day.Date < now.Date.AddDays(-_retentionDays))
                            TryDelete(file);
                    }
                }
            }
            catch (Exception ex) { _log?.LogDebug(ex, "Metrics prune failed."); }
        }

        private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public async ValueTask DisposeAsync()
        {
            _timer.Dispose();
            await SafeFlushAsync().ConfigureAwait(false);
        }

        // ------------- Types -------------

        private sealed class Scope : IDisposable
        {
            private readonly string _name;
            private readonly IDictionary<string, string>? _tags;
            private readonly LocalMetricsStore _owner;
            private readonly long _start = Stopwatch.GetTimestamp();
            private bool _done;

            public Scope(string name, IDictionary<string, string>? tags, LocalMetricsStore owner)
            { _name = name; _tags = tags; _owner = owner; }

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                var elapsed = (double)(Stopwatch.GetTimestamp() - _start) / Stopwatch.Frequency;
                _owner.Enqueue(Metric.Timing(_name, elapsed, _tags)); // seconds
            }
        }

        private sealed record Metric(
            string Type,            // counter|gauge|timing|event
            string Name,
            DateTime TimestampUtc,
            string Machine,
            string User,
            double? Value = null,
            long? Count = null,
            IDictionary<string, string>? Tags = null,
            IDictionary<string, string>? Fields = null)
        {
            public static Metric Counter(string name, long delta, IDictionary<string, string>? tags) =>
                new("counter", name, DateTime.UtcNow, Environment.MachineName, Environment.UserName, Count: delta, Tags: tags);

            public static Metric Gauge(string name, double value, IDictionary<string, string>? tags) =>
                new("gauge", name, DateTime.UtcNow, Environment.MachineName, Environment.UserName, Value: value, Tags: tags);

            public static Metric Timing(string name, double seconds, IDictionary<string, string>? tags) =>
                new("timing", name, DateTime.UtcNow, Environment.MachineName, Environment.UserName, Value: seconds, Tags: tags);

            public static Metric Event(string name, IDictionary<string, string>? fields) =>
                new("event", name, DateTime.UtcNow, Environment.MachineName, Environment.UserName, Fields: fields);
        }
    }
}
