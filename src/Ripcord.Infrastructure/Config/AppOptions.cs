using System;

namespace Ripcord.Infrastructure.Config
{
    /// <summary>
    /// Root options section: "Ripcord".
    /// Bind via: builder.Configuration.GetSection("Ripcord").Get<AppOptions>()
    /// </summary>
    public sealed class AppOptions
    {
        public LoggingOptions Logging { get; set; } = new();
        public JournalOptions Journal { get; set; } = new();
    }

    public sealed class LoggingOptions
    {
        /// <summary>Minimum level: Verbose/Debug/Information/Warning/Error/Fatal.</summary>
        public string Level { get; set; } = "Information";

        /// <summary>Directory for rolling log files.</summary>
        public string Directory { get; set; } =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Logs");

        /// <summary>Base filename (may include rolling placeholder such as ripcord-.log for date rolling).</summary>
        public string FileName { get; set; } = "ripcord-.log";

        /// <summary>Per-file size limit in MB (rolls when exceeded).</summary>
        public int FileSizeMB { get; set; } = 32;

        /// <summary>How many files to retain.</summary>
        public int RetainedFiles { get; set; } = 10;

        /// <summary>Write Serilog JSON lines instead of text template.</summary>
        public bool Json { get; set; } = false;

        /// <summary>Also write to Debug sink (useful during development).</summary>
        public bool DebugSink { get; set; } = true;
    }

    public sealed class JournalOptions
    {
        /// <summary>Directory for the structured JSONL engine journal.</summary>
        public string Directory { get; set; } =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ripcord", "Logs");

        /// <summary>Max size per journal file in bytes.</summary>
        public long MaxBytesPerFile { get; set; } = 16L * 1024 * 1024;

        /// <summary>How many journal files to retain.</summary>
        public int MaxFiles { get; set; } = 8;
    }
}
