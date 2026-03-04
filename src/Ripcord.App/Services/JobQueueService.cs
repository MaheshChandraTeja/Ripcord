#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Engine.Shred;

namespace Ripcord.App.Services
{
    public sealed class JobQueueService : IAsyncDisposable
    {
        private readonly ILogger<JobQueueService> _log;
        private readonly ShredEngine _engine;
        private readonly ReceiptEmitter _receipts;

        private readonly Channel<ShredJobRequest> _queue = Channel.CreateUnbounded<ShredJobRequest>();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public event EventHandler<ShredJobProgress>? Progress;

        public JobQueueService(ILogger<JobQueueService> log, ShredEngine engine, ReceiptEmitter receipts)
        {
            _log = log;
            _engine = engine;
            _receipts = receipts;
            _engine.Progress += (_, p) => Progress?.Invoke(this, p);
            _pump = Task.Run(RunAsync);
        }

        public async Task EnqueueAsync(ShredJobRequest request)
        {
            await _queue.Writer.WriteAsync(request, _cts.Token).ConfigureAwait(false);
        }

        private async Task RunAsync()
        {
            try
            {
                while (await _queue.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (_queue.Reader.TryRead(out var req))
                    {
                        var started = DateTimeOffset.UtcNow;
                        var jobId = Guid.NewGuid();

                        var observed = new ConcurrentDictionary<string, long>();
                        bool deletedFlagFor(string f) => _lastStatus.TryGetValue(f, out var s) && string.Equals(s, "Deleted", StringComparison.OrdinalIgnoreCase);
                        _lastStatus.Clear();

                        void onProgress(object? s, ShredJobProgress p)
                        {
                            if (!string.IsNullOrWhiteSpace(p.CurrentFile))
                            {
                                var path = p.CurrentFile!;
                                if (p.BytesTotal.HasValue) observed[path] = p.BytesTotal.Value;
                                if (!string.IsNullOrWhiteSpace(p.Status))
                                    _lastStatus[path] = p.Status!;
                            }
                        }

                        _engine.Progress += onProgress;
                        ShredJobResult? summary = null;

                        try
                        {
                            var result = await _engine.RunAsync(req, _cts.Token).ConfigureAwait(false);
                            if (result.Success)
                            {
                                summary = result.Value!;
                            }
                            else
                            {
                                _log.LogWarning("Shred job failed: {Error}", result.Error);
                            }
                        }
                        finally
                        {
                            _engine.Progress -= onProgress;
                        }

                        var completed = DateTimeOffset.UtcNow;
                        if (summary is not null)
                        {
                            var items = new List<(string path, long size, bool deleted)>();
                            foreach (var kv in observed)
                                items.Add((kv.Key, kv.Value, deletedFlagFor(kv.Key)));

                            await _receipts.EmitAsync(jobId, req, summary!, items, started, completed).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex) { _log.LogError(ex, "JobQueue pump crashed."); }
        }

        private readonly ConcurrentDictionary<string, string> _lastStatus = new(StringComparer.OrdinalIgnoreCase);

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _pump.ConfigureAwait(false); } catch { }
            _cts.Dispose();
        }
    }
}
