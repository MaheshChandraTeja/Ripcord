#nullable enable
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Ripcord.CLI.Commands
{
    /// <summary>Shared JSON line IPC client for Broker.</summary>
    internal static class IpcClient
    {
        public sealed record Reply(bool Ok, string? PayloadJson, string? Error);

        public static async Task<Reply> TryCallAsync(string verb, object payload, CancellationToken ct)
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return new(false, null, "Windows-only");

                var pipe = GetPipeNameForCurrentUser();
                using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(cts.Token).ConfigureAwait(false);

                using var writer = new StreamWriter(client, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                using var reader = new StreamReader(client, new System.Text.UTF8Encoding(false));

                string id = Guid.NewGuid().ToString("N");
                var line = JsonSerializer.Serialize(new { id, verb, payload });
                await writer.WriteLineAsync(line).ConfigureAwait(false);

                var resp = await reader.ReadLineAsync().WaitAsync(cts.Token).ConfigureAwait(false);
                if (resp is null) return new(false, null, "no response");

                using var doc = JsonDocument.Parse(resp);
                var root = doc.RootElement;
                bool ok = root.GetProperty("ok").GetBoolean();
                string? err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                string? payloadJson = root.TryGetProperty("payload", out var p) ? p.GetRawText() : null;
                return new(ok, payloadJson, err);
            }
            catch (Exception ex)
            {
                return new(false, null, ex.Message);
            }
        }

        private static string GetPipeNameForCurrentUser()
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "S-1-0-0";
            var compact = sid.Replace("-", "", StringComparison.OrdinalIgnoreCase);
            return $"ripcord-broker-{compact}";
        }
    }
}
