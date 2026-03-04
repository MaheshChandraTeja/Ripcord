#nullable enable
using System;
using System.IO;
using System.Security.Principal;

namespace Ripcord.Broker.Security
{
    /// <summary>
    /// Authorization & constraints for the broker. Defaults are safe: only current user may connect,
    /// allowed verbs limited, and paths must be local absolute.
    /// </summary>
    public sealed class BrokerPolicy
    {
        private readonly BrokerPolicyOptions _opts;

        public BrokerPolicy(BrokerPolicyOptions opts) => _opts = opts ?? new BrokerPolicyOptions();

        public string GetPipeNameForCurrentUser()
        {
            using var id = WindowsIdentity.GetCurrent();
            var sid = id.User?.Value?.Replace("-", "", StringComparison.OrdinalIgnoreCase) ?? "unknown";
            return $"{_opts.PipeBaseName}-{sid}";
        }

        public bool AuthorizeClient(string clientUser)
        {
            if (string.IsNullOrWhiteSpace(clientUser)) return false;

            // Require same logon user (e.g., "DOMAIN\User") unless policy allows cross-user
            var current = WindowsIdentity.GetCurrent().Name;
            if (string.Equals(current, clientUser, StringComparison.OrdinalIgnoreCase)) return true;

            return _opts.AllowDifferentUserClients;
        }

        public void AuthorizeVerb(string verb)
        {
            if (Array.IndexOf(_opts.AllowedVerbs, verb) < 0)
                throw new UnauthorizedAccessException($"Verb not allowed: {verb}");
        }

        public void ValidateLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("Path must be absolute.", nameof(path));

            // Disallow UNC/network paths by default
            if (!_opts.AllowUncPaths && path.StartsWith(@"\\", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("UNC paths are not allowed.");
        }

        public TimeSpan OperationTimeout => TimeSpan.FromSeconds(Math.Clamp(_opts.OperationTimeoutSeconds, 10, 3600));
    }

    public sealed class BrokerPolicyOptions
    {
        public string PipeBaseName { get; set; } = "ripcord-broker";
        public string[] AllowedVerbs { get; set; } = new[] { "ping", "shred", "vss.enumerate", "vss.purge", "vss.delete" };
        public bool AllowDifferentUserClients { get; set; } = false;
        public bool AllowUncPaths { get; set; } = false;
        public int OperationTimeoutSeconds { get; set; } = 900;
    }
}
