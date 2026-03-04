#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Broker.IPC;
using Ripcord.Broker.Security;
using Ripcord.Engine.Vss;

namespace Ripcord.Broker.Services
{
    /// <summary>
    /// Enumerate and purge Volume Shadow Copies with elevated permissions.
    /// </summary>
    public sealed class BrokerVssService
    {
        private readonly ILogger<BrokerVssService> _log;
        private readonly BrokerPolicy _policy;

        public BrokerVssService(ILogger<BrokerVssService> log, BrokerPolicy policy)
        {
            _log = log;
            _policy = policy;
        }

        public Task<IpcResult> HandleEnumerateAsync(NamedPipeServer.RequestContext ctx, JsonElement payload, CancellationToken ct)
        {
            _policy.AuthorizeVerb("vss.enumerate");

            var root = payload.TryGetProperty("volume", out var v) ? v.GetString() : @"C:\";
            if (string.IsNullOrWhiteSpace(root)) root = @"C:\";
            _policy.ValidateLocalPath(root);

            var snaps = ShadowEnumerator.EnumerateByVolume(root, _log)
                .Select(s => new
                {
                    id = s.Id,
                    volume = s.VolumeName,
                    device = s.DeviceObject,
                    createdUtc = s.InstallDateUtc,
                    clientAccessible = s.ClientAccessible,
                    persistent = s.Persistent,
                    state = s.State
                })
                .ToArray();

            return Task.FromResult(IpcResult.Ok(new { snapshots = snaps }));
        }

        public async Task<IpcResult> HandlePurgeAsync(NamedPipeServer.RequestContext ctx, JsonElement payload, CancellationToken ct)
        {
            _policy.AuthorizeVerb("vss.purge");

            string root = payload.TryGetProperty("volume", out var v) ? v.GetString() ?? @"C:\" : @"C:\";
            _policy.ValidateLocalPath(root);

            int days = payload.TryGetProperty("olderThanDays", out var d) ? Math.Max(0, d.GetInt32()) : 0;
            bool includeClient = payload.TryGetProperty("includeClientAccessible", out var ica) && ica.GetBoolean();
            bool dryRun = payload.TryGetProperty("dryRun", out var dr) && dr.GetBoolean();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_policy.OperationTimeout);

            var res = await ShadowPurger.PurgeVolumeAsync(root,
                olderThan: days > 0 ? TimeSpan.FromDays(days) : null,
                includeClientAccessible: includeClient,
                dryRun: dryRun,
                logger: _log,
                journal: null,
                ct: cts.Token).ConfigureAwait(false);

            if (!res.Success) return IpcResult.Fail(res.Error ?? "Purge failed");
            return IpcResult.Ok(new { deleted = res.Value, dryRun });
        }

        public Task<IpcResult> HandleDeleteAsync(NamedPipeServer.RequestContext ctx, JsonElement payload, CancellationToken ct)
        {
            _policy.AuthorizeVerb("vss.delete");

            if (!payload.TryGetProperty("id", out var idEl)) return Task.FromResult(IpcResult.Fail("Missing id"));
            if (!Guid.TryParse(idEl.GetString(), out var id)) return Task.FromResult(IpcResult.Fail("Invalid id"));

            bool dryRun = payload.TryGetProperty("dryRun", out var dr) && dr.GetBoolean();

            var res = ShadowPurger.DeleteById(id, dryRun, _log, null);
            if (!res.Success) return Task.FromResult(IpcResult.Fail(res.Error ?? "Delete failed"));
            return Task.FromResult(IpcResult.Ok(new { deleted = true, id }));
        }
    }
}
