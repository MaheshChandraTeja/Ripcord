// =============================
// File: src/Ripcord.Engine/Shred/AdsEnumerator.cs
// =============================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Ripcord.Engine.Shred
{
    /// <summary>
    /// Utilities to enumerate and remove NTFS Alternate Data Streams (ADS).
    /// </summary>
    public static class AdsEnumerator
    {
        private const int ERROR_HANDLE_EOF = 38;

        public readonly struct AlternateStreamInfo
        {
            public string Name { get; }
            public long Size { get; }
            public AlternateStreamInfo(string name, long size) { Name = name; Size = size; }
            public override string ToString() => $"{Name} ({Size} bytes)";
        }

        /// <summary>
        /// Enumerates all alternate streams (excluding the default ::$DATA) for a given file.
        /// </summary>
        public static IEnumerable<AlternateStreamInfo> Enumerate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            var ext = NormalizeExtended(path);

            WIN32_FIND_STREAM_DATA data;
            IntPtr handle = FindFirstStreamW(ext, STREAM_INFO_LEVELS.FindStreamInfoStandard, out data, 0);
            if (handle == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_HANDLE_EOF) yield break; // no streams
                throw new Win32Exception(err, $"FindFirstStreamW failed for '{path}'");
            }
            try
            {
                do
                {
                    var name = data.cStreamName;
                    if (!string.IsNullOrWhiteSpace(name) && !name.Equals("::$DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new AlternateStreamInfo(name, data.StreamSize);
                    }
                }
                while (FindNextStreamW(handle, out data));

                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_HANDLE_EOF && err != 0)
                    throw new Win32Exception(err, $"FindNextStreamW failed for '{path}'");
            }
            finally
            {
                FindClose(handle);
            }
        }

        /// <summary>
        /// Deletes all ADS streams for the specified file. Logs progress and errors.
        /// </summary>
        public static void DeleteAll(string path, ILogger? logger = null)
        {
            foreach (var s in Enumerate(path))
            {
                var full = path + s.Name; // name already starts with ":" and includes :$DATA
                var ext = NormalizeExtended(full);
                if (!DeleteFileW(ext))
                {
                    int err = Marshal.GetLastWin32Error();
                    logger?.LogWarning("Failed to delete ADS {Stream} on {Path}: {Error}", s.Name, path, new Win32Exception(err).Message);
                }
                else
                {
                    logger?.LogInformation("Deleted ADS {Stream} on {Path} ({Size} bytes)", s.Name, path, s.Size);
                }
            }
        }

        private static string NormalizeExtended(string p)
        {
            if (p.StartsWith("\\\\?\\")) return p;
            if (p.StartsWith("\\\\")) return "\\\\?\\UNC\\" + p.TrimStart('\\');
            return "\\\\?\\" + p;
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private enum STREAM_INFO_LEVELS
        {
            FindStreamInfoStandard = 0
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_STREAM_DATA
        {
            public long StreamSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
            public string cStreamName;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindFirstStreamW(string lpFileName, STREAM_INFO_LEVELS InfoLevel, out WIN32_FIND_STREAM_DATA lpFindStreamData, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextStreamW(IntPtr hFindStream, out WIN32_FIND_STREAM_DATA lpFindStreamData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFileW(string lpFileName);
    }
}
