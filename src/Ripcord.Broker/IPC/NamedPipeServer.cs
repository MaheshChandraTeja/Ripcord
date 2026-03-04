#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ripcord.Broker.Security;

namespace Ripcord.Broker.IPC
{
    /// <summary>
    /// Minimal JSON-over-named-pipes server with per-request verb dispatch.
    /// Frames are newline-delimited UTF-8 JSON messages:
    ///  Request:  {"id":"guid","verb":"shred","payload":{...}}
    ///  Response: {"id":"guid","ok":true,"payload":{...}} or {"id":"guid","ok":false,"error":"..."}
    /// </summary>
    public sealed class NamedPipeServer
    {
        private readonly ILogger<NamedPipeServer> _log;
        private readonly ConcurrentDictionary<string, Handler> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public NamedPipeServer(ILogger<NamedPipeServer> log) => _log = log;

        public delegate Task<IpcResult> Handler(RequestContext ctx, JsonElement payload, CancellationToken ct);

        public record Request(string id, string verb, JsonElement payload);
        public record Response(string id, bool ok, JsonElement? payload, string? error);

        public record RequestContext(string ClientUser, int? ClientPid);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = false,
            WriteIndented = false
        };

        public void Register(string verb, Handler handler)
        {
            if (!_handlers.TryAdd(verb, handler))
                throw new InvalidOperationException($"Verb already registered: {verb}");
        }

        public async Task RunAsync(BrokerPolicy policy, CancellationToken ct)
        {
            string pipeName = policy.GetPipeNameForCurrentUser();
            var ps = BuildPipeSecurity(policy);

            int instance = 0;
            while (!ct.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                    inBufferSize: 64 * 1024,
                    outBufferSize: 64 * 1024,
                    ps);

                _ = AcceptLoopAsync(server, policy, ++instance, ct);
                // throttle instance creation a little
                await Task.Delay(50, ct);
            }
        }

        private async Task AcceptLoopAsync(NamedPipeServerStream server, BrokerPolicy policy, int instance, CancellationToken ct)
        {
            try
            {
                _log.LogDebug("Waiting for client... (instance {Instance})", instance);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // Identify client
                string clientUser = "<unknown>";
                try { clientUser = server.GetImpersonationUserName(); } catch { }
                int? pid = TryGetClientPid(server);

                _log.LogInformation("Client connected: {User} pid={Pid} (inst {Instance})", clientUser, pid, instance);

                if (!policy.AuthorizeClient(clientUser))
                {
                    _log.LogWarning("Client not authorized: {User}", clientUser);
                    await SendRawAsync(server, new { id = "", ok = false, error = "unauthorized" }, ct);
                    server.Dispose();
                    return;
                }

                await ProcessClientAsync(server, new RequestContext(clientUser, pid), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (IOException ex)
            {
                _log.LogDebug(ex, "Pipe IO ended (inst {Instance})", instance);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Pipe instance {Instance} crashed", instance);
            }
            finally
            {
                try { server.Dispose(); } catch { }
            }
        }

        private async Task ProcessClientAsync(NamedPipeServerStream pipe, RequestContext ctx, CancellationToken ct)
        {
            var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true) { AutoFlush = true };

            char[] buf = ArrayPool<char>.Shared.Rent(64 * 1024);
            try
            {
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    string? line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
                    if (line is null) break;
                    if (line.Length == 0) continue;

                    IpcResult result;
                    Request req;

                    try
                    {
                        req = JsonSerializer.Deserialize<Request>(line, JsonOpts) ?? throw new InvalidDataException("Invalid message.");
                        if (!_handlers.TryGetValue(req.verb, out var handler))
                            throw new InvalidOperationException("Unknown verb: " + req.verb);

                        result = await handler(ctx, req.payload, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = IpcResult.Fail(ex.Message);
                    }

                    await writer.WriteLineAsync(result.ToJson(req.id));
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buf);
                try { await writer.FlushAsync(); } catch { }
            }
        }

        private static async Task SendRawAsync(NamedPipeServerStream pipe, object obj, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await pipe.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
        }

        private static int? TryGetClientPid(NamedPipeServerStream s)
        {
            try
            {
                // .NET doesn't expose client PID directly; if needed, use native GetNamedPipeClientProcessId via P/Invoke later.
                return null;
            }
            catch { return null; }
        }

        private static PipeSecurity BuildPipeSecurity(BrokerPolicy policy)
        {
            var ps = new PipeSecurity();

            // Current user
            var current = WindowsIdentity.GetCurrent().User!;
            var userRule = new PipeAccessRule(current, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, System.Security.AccessControl.AccessControlType.Allow);
            ps.AddAccessRule(userRule);

            // Administrators
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            ps.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));

            // Optional: deny Everyone explicitly to prevent accidental access
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            ps.AddAccessRule(new PipeAccessRule(everyone, PipeAccessRights.ReadWrite, AccessControlType.Deny));

            return ps;
        }
    }

    public sealed record IpcResult(bool Ok, JsonElement? Payload, string? Error)
    {
        public static IpcResult Ok(object? payload = null)
        {
            return new IpcResult(true, payload is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(payload), null);
        }
        public static IpcResult Fail(string error) => new(false, null, error);

        public string ToJson(string id)
            => JsonSerializer.Serialize(new { id, ok = Ok, payload = Payload, error = Error });
    }
}
