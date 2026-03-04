using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ripcord.Engine.IO
{
    /// <summary>
    /// Best-effort media detection for a given path/volume (rotational vs. solid-state, removable, network).
    /// Used to pick sane defaults for buffer sizes and write flags.
    /// </summary>
    public static class MediaDetector
    {
        public sealed record MediaInfo(
            string VolumeRoot,
            DriveType DriveType,
            bool? IsRotational,
            bool IsRemovable,
            int RecommendedBufferBytes,
            bool RecommendWriteThrough);

        /// <summary>
        /// Detects properties of the volume that contains <paramref name="anyPathOnVolume"/>.
        /// </summary>
        public static MediaInfo Detect(string anyPathOnVolume)
        {
            if (string.IsNullOrWhiteSpace(anyPathOnVolume))
                throw new ArgumentNullException(nameof(anyPathOnVolume));

            string root = Path.GetPathRoot(Path.GetFullPath(anyPathOnVolume)) ?? "C:\\";
            var di = new DriveInfo(root);

            bool removable = di.DriveType == DriveType.Removable;
            bool network = di.DriveType == DriveType.Network;
            bool? rotational = TryDetectRotationalWindows(root);

            // Conservative buffer guidance:
            int buf = network ? 128 * 1024
                    : rotational == true ? 1 * 1024 * 1024
                    : 512 * 1024;

            bool writeThrough = true; // default to safer behavior; app may allow toggling

            return new MediaInfo(root, di.DriveType, rotational, removable, buf, writeThrough);
        }

        /// <summary>
        /// Windows-specific: use Storage Seek Penalty property to infer rotational media.
        /// Returns null if not determinable.
        /// </summary>
        private static bool? TryDetectRotationalWindows(string root)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            try
            {
                // Open the volume (e.g., "\\.\C:")
                string vol = @"\\.\" + (root.TrimEnd('\\').TrimEnd(':')) + ":";
                using var handle = CreateFile(vol, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                if (handle.IsInvalid) return null;

                STORAGE_PROPERTY_QUERY q = new()
                {
                    PropertyId = STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty,
                    QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery
                };

                var desc = new DEVICE_SEEK_PENALTY_DESCRIPTOR();
                int bytesReturned = 0;
                bool ok = DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, ref q, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                                          ref desc, Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), ref bytesReturned, IntPtr.Zero);
                if (!ok) return null;

                // If SeekPenalty is true => rotational (HDD). If false => no seek penalty (SSD).
                return desc.IncursSeekPenalty;
            }
            catch { return null; }
        }

        // ====== P/Invoke (Windows only paths will execute this) ======
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, FileShare dwShareMode,
                                                        IntPtr lpSecurityAttributes, FileMode dwCreationDisposition,
                                                        int dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
                                                   ref STORAGE_PROPERTY_QUERY lpInBuffer, int nInBufferSize,
                                                   ref DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, int nOutBufferSize,
                                                   ref int lpBytesReturned, IntPtr lpOverlapped);

        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        private enum STORAGE_PROPERTY_ID
        {
            StorageDeviceSeekPenaltyProperty = 7
        }

        private enum STORAGE_QUERY_TYPE
        {
            PropertyStandardQuery = 0
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public STORAGE_PROPERTY_ID PropertyId;
            public STORAGE_QUERY_TYPE QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.Bool)]
            public bool IncursSeekPenalty;
        }
    }
}
