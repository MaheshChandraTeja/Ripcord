using System;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ripcord.Broker.IPC;
using Ripcord.Broker.Security;
using Ripcord.Broker.Services;
using Ripcord.Infrastructure.Config;
using Ripcord.Infrastructure.Logging;

namespace Ripcord.Broker
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Ensure we're running on Windows and (ideally) elevated
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Ripcord.Broker requires Windows.");
                return 1;
            }

            bool isAdmin = IsAdministrator();
            if (!isAdmin)
            {
                Console.Error.WriteLine("Warning: Broker is not elevated; some operations (VSS purge, locked files) may fail.");
            }

            var exeDir = AppContext.BaseDirectory;
            var configDir = Path.Combine(exeDir, "config");

            using var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.Sources.Clear();
                    cfg.SetBasePath(exeDir);
                    cfg.AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true, reloadOnChange: true)
                       .AddJsonFile(Path.Combine(configDir, $"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
                       .AddEnvironmentVariables(prefix: "RIPCORD_");
                })
                .UseRipcordLogging()
                .ConfigureServices((ctx, services) =>
                {
                    // Options
                    var appOpts = new AppOptions();
                    ctx.Configuration.GetSection("Ripcord").Bind(appOpts);
                    services.AddSingleton(appOpts);

                    var polOpts = new BrokerPolicyOptions();
                    ctx.Configuration.GetSection("Ripcord:BrokerPolicy").Bind(polOpts);
                    services.AddSingleton(polOpts);
                    services.AddSingleton<BrokerPolicy>();

                    // Services
                    services.AddSingleton<BrokerShredService>();
                    services.AddSingleton<BrokerVssService>();

                    // IPC
                    services.AddSingleton<NamedPipeServer>();
                })
                .Build();

            var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Broker");
            var pipe = host.Services.GetRequiredService<NamedPipeServer>();
            var policy = host.Services.GetRequiredService<BrokerPolicy>();
            var shred = host.Services.GetRequiredService<BrokerShredService>();
            var vss = host.Services.GetRequiredService<BrokerVssService>();

            // Register verbs
            pipe.Register("ping", async (ctx, payload, ct) =>
            {
                return IpcResult.Ok(new { server = "ripcord-broker", user = WindowsIdentity.GetCurrent().Name, admin = isAdmin });
            });

            pipe.Register("shred", (ctx, payload, ct) => shred.HandleShredAsync(ctx, payload, ct));
            pipe.Register("vss.enumerate", (ctx, payload, ct) => vss.HandleEnumerateAsync(ctx, payload, ct));
            pipe.Register("vss.purge", (ctx, payload, ct) => vss.HandlePurgeAsync(ctx, payload, ct));
            pipe.Register("vss.delete", (ctx, payload, ct) => vss.HandleDeleteAsync(ctx, payload, ct));

            // Start
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            log.LogInformation("Ripcord.Broker starting. Elevated={Elevated}", isAdmin);
            try
            {
                await pipe.RunAsync(policy, cts.Token);
                return 0;
            }
            catch (OperationCanceledException)
            {
                log.LogInformation("Broker shutting down (cancel).");
                return 0;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Broker crashed.");
                return 2;
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
