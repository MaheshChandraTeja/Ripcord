#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ripcord.App.Services
{
    /// <summary>
    /// Minimal JSON-over-named-pipes client for talking to Ripcord.Broker.
    /// Handles connect, optional auto-start (with optional elevation), and simple verb calls.
    /// </summary>
    public sealed class BrokerClient : IAsyncDisposable
    {
        private readonly ILogger<BrokerClient> _log;
        private readonly TimeSpan _connectTimeout;
        private NamedPipeClientStream? _pipe;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        public bool AutoStartBroker { get; set; } = true;
        public bool ElevateBroker { get; set; } = true;

        /// <summary>Optional: set a specific path to the broker EXE. If null, several defaults are probed.</summary>
        public string? BrokerExePath { get; set; }

        public BrokerClient(ILogger<BrokerClient> log, TimeSpan? connectTimeout = null)
        {
            _log = log;
            _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        }

        // ------------- public high-level calls -------------

        public Task<IpcReply<JsonElement>> PingAsync(CancellationToken ct = default)
            => CallAsync<JsonElement>("ping", new { }, ct);

        public Task<IpcReply<JsonElement>> ShredAsync(string path, bool recurse, bool dryRun, bool deleteAfter, string profile = "default", CancellationToken ct = default)
            => CallAsync<JsonElement>("shred", new { path, recurse, dryRun, deleteAfter, profile }, ct);

        public Task<IpcReply<JsonElement>> VssEnumerateAsync(string volumeRoot, CancellationToken ct = default)
            => CallAsync<JsonElement>("vss.enumerate", new { volume = volumeRoot }, ct);

        public Task<IpcReply<JsonElement>> VssPurgeAsync(string volumeRoot, int olderThanDays, bool includeClientAccessible, bool dryRun, CancellationToken ct = default)
            => CallAsync<JsonElement>("vss.purge", new { volume = volumeRoot, olderThanDays, includeClientAccessible, dryRun }, ct);

        public Task<IpcReply<JsonElement>> VssDeleteAsync(Guid id, bool dryRun, CancellationToken ct = default)
            => CallAsync<JsonElement>("vss.delete", new { id, dryRun }, ct);

        // ------------- core JSON-RPC over pipe -------------

        public async Task<IpcReply<T>> CallAsync<T>(string verb, object payload, CancellationToken ct = default)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            string id = Guid.NewGuid().ToString("N");
            var line = JsonSerializer.Serialize(new { id, verb, payload });

            await _writer!.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);

            string? resp = await _reader!.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
            if (resp is null) return IpcReply<T>.Fail("broker-disconnected");

            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            string? err = root.TryGetProperty("error", out var e) ? e.GetString() : null;

            if (!ok) return IpcReply<T>.Fail(err ?? "unknown-error");

            if (!root.TryGetProperty("payload", out var payloadEl))
                return IpcReply<T>.Ok(default!);

            // Convert payload to requested type
            T result;
            if (typeof(T) == typeof(JsonElement))
            {
                // Unwrap as-is
                object boxed = payloadEl.Clone();
                result = (T)boxed;
            }
            else
            {
                result = payloadEl.Deserialize<T>()!;
            }
            return IpcReply<T>.Ok(result);
        }

        // ------------- connection lifecycle -------------

        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (_pipe is { IsConnected: true }) return;

            var name = GetPipeNameForCurrentUser();
            _pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_connectTimeout);
                await _pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (TimeoutException) when (AutoStartBroker)
            {
                _log.LogInformation("Broker not running; attempting to start it...");
                await StartBrokerAsync(ct).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                await _pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (AutoStartBroker)
            {
                _log.LogDebug(ex, "Initial connect failed; trying to start broker.");
                await StartBrokerAsync(ct).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                await _pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            }

            _reader = new StreamReader(_pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true) { AutoFlush = true };
        }

        private async Task StartBrokerAsync(CancellationToken ct)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Broker is Windows-only.");

            string? exe = ResolveBrokerPath();
            if (exe is null || !File.Exists(exe))
                throw new FileNotFoundException("Could not locate Ripcord.Broker executable.", exe ?? "<null>");

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = ElevateBroker ? "runas" : "", // triggers UAC when true
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc is null) throw new InvalidOperationException("Failed to start broker process.");
            }
            catch (Win32Exception win32) when (win32.NativeErrorCode == 1223 /*ERROR_CANCELLED*/ )
            {
                _log.LogWarning("User canceled UAC prompt. Continuing without broker.");
                throw;
            }

            // Give it a moment to bind the pipe before we try to connect
            await Task.Delay(600, ct).ConfigureAwait(false);
        }

        private string? ResolveBrokerPath()
        {
            if (!string.IsNullOrWhiteSpace(BrokerExePath)) return BrokerExePath;

            // 1) Next to the app (installed scenario)
            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "Ripcord.Broker.exe");
            if (File.Exists(candidate)) return candidate;

            // 2) Sibling folder (dev/debug)
            candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "Ripcord.Broker", "Ripcord.Broker.exe"));
            if (File.Exists(candidate)) return candidate;

            // 3) dotnet exe in dev (fallback)
            candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "Ripcord.Broker", "bin", "Debug", "net8.0-windows10.0.19041.0", "Ripcord.Broker.exe"));
            if (File.Exists(candidate)) return candidate;

            // Not found
            return null;
        }

        private static string GetPipeNameForCurrentUser()
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "S-1-0-0";
            var compact = sid.Replace("-", "", StringComparison.OrdinalIgnoreCase);
            return $"ripcord-broker-{compact}";
        }

        public async ValueTask DisposeAsync()
        {
            try { if (_writer is not null) await _writer.FlushAsync().ConfigureAwait(false); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }
        }

        // ------------ helper types ------------
        public sealed record IpcReply<T>(bool Ok, T? Payload, string? Error)
        {
            public static IpcReply<T> Ok(T? payload = default) => new(true, payload, null);
            public static IpcReply<T> Fail(string error) => new(false, default, error);
        }
    }
}
