using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Ripcord.Infrastructure.Logging;
using Ripcord.Infrastructure.Config;
using Ripcord.Engine.Shred;
using Ripcord.Engine.Journal;
using Ripcord.App.Services;
using Ripcord.Receipts.Export;

namespace Ripcord.App
{
    public partial class App : Application
    {
        public static IHost Host { get; private set; } = default!;
        public static IServiceProvider Services => Host.Services;

        public App()
        {
            InitializeComponent();

            // Build Host (config + logging + DI)
            var exeDir = AppContext.BaseDirectory;
            var configDir = Path.Combine(exeDir, "config");

            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.Sources.Clear();
                    cfg.SetBasePath(exeDir);
                    cfg.AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true, reloadOnChange: true)
                       .AddJsonFile(Path.Combine(configDir, $"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
                       .AddEnvironmentVariables();
                })
                .UseRipcordLogging() // binds AppOptions and configures Serilog-backed ILoggerFactory
                .ConfigureServices((ctx, services) =>
                {
                    var opts = services.BuildServiceProvider().GetRequiredService<AppOptions>();

                    // Engine services
                    services.AddSingleton<JobJournal>();
                    services.AddSingleton<ShredEngine>();

                    // App services
                    services.AddSingleton<ReceiptStore>();
                    services.AddSingleton<ReceiptEmitter>(); // bridges Engine -> Receipts
                    services.AddSingleton<JobQueueService>();

                    // Exporters (stateless)
                    services.AddSingleton<ReceiptJsonExporter>();
                })
                .Build();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var window = new MainWindow();
            window.Activate();
        }
    }
}
