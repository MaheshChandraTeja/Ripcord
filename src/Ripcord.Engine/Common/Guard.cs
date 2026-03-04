using System;
using System.Diagnostics;
using System.IO;

namespace Ripcord.Engine.Common
{
    /// <summary>Lightweight guard helpers for argument validation.</summary>
    public static class Guard
    {
        [DebuggerStepThrough]
        public static T NotNull<T>(T value, string name) where T : class
            => value ?? throw new ArgumentNullException(name);

        [DebuggerStepThrough]
        public static string NotNullOrWhiteSpace(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Cannot be null/empty/whitespace", name) : value;

        [DebuggerStepThrough]
        public static int InRange(int value, int minInclusive, int maxInclusive, string name)
            => (value < minInclusive || value > maxInclusive) ? throw new ArgumentOutOfRangeException(name) : value;

        [DebuggerStepThrough]
        public static long NonNegative(long value, string name)
            => value < 0 ? throw new ArgumentOutOfRangeException(name) : value;

        [DebuggerStepThrough]
        public static FileInfo FileExists(FileInfo fi, string name)
            => (fi is null || !fi.Exists) ? throw new FileNotFoundException($"File not found: {fi?.FullName}", name) : fi;

        [DebuggerStepThrough]
        public static DirectoryInfo DirectoryExists(DirectoryInfo di, string name)
            => (di is null || !di.Exists) ? throw new DirectoryNotFoundException(di?.FullName ?? name) : di;
    }
}
