using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ripcord.Infrastructure.Logging;
using Ripcord.Infrastructure.Config;

namespace Ripcord.CLI
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var root = new RootCommand("Ripcord command-line interface");

            // Subcommands (implemented in separate files)
            root.AddCommand(Commands.Shred.Build());
            root.AddCommand(Commands.FreeSpace.Build());
            root.AddCommand(Commands.ShadowPurge.Build());
            root.AddCommand(Commands.Receipts.Build());

            // Global options (verbosity etc.) – logging handled by Infrastructure
            root.SetHandler(ctx => { ctx.Console.WriteLine("Use a subcommand. Try: ripcord shred --help"); });

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.Sources.Clear();
                    var exeDir = AppContext.BaseDirectory;
                    cfg.SetBasePath(exeDir);
                    cfg.AddJsonFile(Path.Combine(exeDir, "config", "appsettings.json"), optional: true, reloadOnChange: false)
                       .AddJsonFile(Path.Combine(exeDir, "config", $"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json"), optional: true, reloadOnChange: false)
                       .AddEnvironmentVariables(prefix: "RIPCORD_");
                })
                .UseRipcordLogging()
                .ConfigureServices((ctx, services) =>
                {
                    services.AddSingleton<AppOptions>(sp => sp.GetRequiredService<AppOptions>()); // bound by UseRipcordLogging
                });

            return await root.InvokeAsync(args, builder);
        }
    }
}
