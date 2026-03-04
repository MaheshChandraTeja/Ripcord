using System;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Ripcord.Infrastructure.Config;

namespace Ripcord.Infrastructure.Logging
{
    /// <summary>
    /// Creates a Serilog logger based on <see cref="LoggingOptions"/> and environment.
    /// </summary>
    public static class SerilogBootstrap
    {
        public static LoggerConfiguration CreateConfiguration(LoggingOptions opts, string environmentName)
        {
            Directory.CreateDirectory(opts.Directory);
            var path = Path.Combine(opts.Directory, opts.FileName);

            var minLevel = ParseLevel(opts.Level);
            var cfg = new LoggerConfiguration()
                .MinimumLevel.Is(minLevel)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Env", environmentName)
                .Enrich.WithProperty("App", "Ripcord");

            // File sink
            if (opts.Json)
            {
                cfg = cfg.WriteTo.File(new RenderedCompactJsonFormatter(),
                                       path,
                                       rollingInterval: RollingInterval.Day,
                                       retainedFileCountLimit: opts.RetainedFiles,
                                       fileSizeLimitBytes: opts.FileSizeMB * 1024L * 1024L,
                                       rollOnFileSizeLimit: true,
                                       shared: true);
            }
            else
            {
                const string template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext:l} {Message:lj} {Properties:j}{NewLine}{Exception}";
                cfg = cfg.WriteTo.File(path,
                                       outputTemplate: template,
                                       rollingInterval: RollingInterval.Day,
                                       retainedFileCountLimit: opts.RetainedFiles,
                                       fileSizeLimitBytes: opts.FileSizeMB * 1024L * 1024L,
                                       rollOnFileSizeLimit: true,
                                       shared: true);
            }

            if (opts.DebugSink)
                cfg = cfg.WriteTo.Debug();

            return cfg;
        }

        public static ILoggerFactory BuildFactory(LoggingOptions opts, string environmentName)
        {
            var logger = CreateConfiguration(opts, environmentName).CreateLogger();
            return new Serilog.Extensions.Logging.SerilogLoggerFactory(logger, dispose: true);
        }

        private static LogEventLevel ParseLevel(string level) =>
            level?.ToLowerInvariant() switch
            {
                "verbose"      => LogEventLevel.Verbose,
                "debug"        => LogEventLevel.Debug,
                "information"  => LogEventLevel.Information,
                "warning"      => LogEventLevel.Warning,
                "error"        => LogEventLevel.Error,
                "fatal"        => LogEventLevel.Fatal,
                _              => LogEventLevel.Information
            };
    }
}
