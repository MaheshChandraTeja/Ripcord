#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Broker.IPC;
using Ripcord.Broker.Security;
using Ripcord.Engine.Shred;

namespace Ripcord.Broker.Services
{
    /// <summary>
    /// Handles shred requests out-of-process (often elevated). Thin adapter over ShredEngine.
    /// </summary>
    public sealed class BrokerShredService
    {
        private readonly ILogger<BrokerShredService> _log;
        private readonly BrokerPolicy _policy;

        public BrokerShredService(ILogger<BrokerShredService> log, BrokerPolicy policy)
        {
            _log = log;
            _policy = policy;
        }

        public async Task<IpcResult> HandleShredAsync(NamedPipeServer.RequestContext ctx, JsonElement payload, CancellationToken ct)
        {
            _policy.AuthorizeVerb("shred");

            // DTO: { "path":"...", "recurse":true, "dryRun":false, "deleteAfter":true, "profile":"default" }
            var req = JsonSerializer.Deserialize<ShredDto>(payload.GetRawText()) ?? throw new ArgumentException("Invalid payload.");
            _policy.ValidateLocalPath(req.path);

            var profile = Profiles.Default(); // hook up real profile lookup here if needed
            var job = new ShredJobRequest(req.path, profile, req.dryRun, req.deleteAfter, req.recurse);

            var engine = new ShredEngine(_log); // optionally inject; created local to isolate per-call logs
            ShredJobResult? endResult = null;
            var tcs = new TaskCompletionSource();

            engine.Progress += (_, p) =>
            {
                // For future: you can stream progress via pipe if the protocol supports it.
                // Here we just log a few milestones to keep broker quiet.
                if (p.Percent.HasValue && (p.Percent % 25) == 0) _log.LogInformation("Shred {File} {Pct}%", p.CurrentFile, p.Percent);
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_policy.OperationTimeout);

            var result = await engine.RunAsync(job, cts.Token).ConfigureAwait(false);
            if (!result.Success)
                return IpcResult.Fail(result.Error ?? "Shred failed");

            endResult = result.Value!;
            return IpcResult.Ok(new
            {
                ok = true,
                filesProcessed = endResult.FilesProcessed,
                filesFailed = endResult.FilesFailed,
                filesDeleted = endResult.FilesDeleted,
                bytesOverwritten = endResult.BytesOverwritten
            });
        }

        private sealed class ShredDto
        {
            public string path { get; set; } = "";
            public bool recurse { get; set; } = false;
            public bool dryRun { get; set; } = false;
            public bool deleteAfter { get; set; } = true;
            public string? profile { get; set; } = "default";
        }
    }
}
