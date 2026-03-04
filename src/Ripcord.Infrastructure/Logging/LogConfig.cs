using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ripcord.Infrastructure.Config;

namespace Ripcord.Infrastructure.Logging
{
    /// <summary>
    /// Extension methods to wire logging/DI quickly.
    /// </summary>
    public static class LogConfig
    {
        /// <summary>
        /// Binds <see cref="AppOptions"/> from configuration section "Ripcord",
        /// then configures Serilog as the logging backend for the host.
        /// </summary>
        public static IHostBuilder UseRipcordLogging(this IHostBuilder builder)
        {
            builder.ConfigureServices((ctx, services) =>
            {
                // Bind options once; make them injectable
                var opts = new AppOptions();
                ctx.Configuration.GetSection("Ripcord").Bind(opts);
                services.AddSingleton(opts);
                services.AddSingleton(opts.Logging);
                services.AddSingleton(opts.Journal);

                // Build Serilog-backed ILoggerFactory
                var factory = SerilogBootstrap.BuildFactory(opts.Logging, ctx.HostingEnvironment.EnvironmentName);
                services.AddSingleton<ILoggerFactory>(factory);
                services.AddLogging(); // enables ILogger<T> injection
            });

            return builder;
        }
    }
}
