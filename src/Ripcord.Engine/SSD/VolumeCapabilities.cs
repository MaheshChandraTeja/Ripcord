#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ripcord.Engine.SSD
{
    /// <summary>
    /// Best-effort capability probe for a volume containing a specified path.
    /// Detects TRIM support, sparse-file support, cluster size, and capacity numbers.
    /// </summary>
    public sealed record VolumeCapabilities(
        string VolumeRoot,
        string FileSystem,
        uint ClusterSizeBytes,
        long TotalBytes,
        long FreeBytes,
        bool SupportsSparse,
        bool TrimEnabled,
        bool? IncursSeekPenalty // true = HDD; false = SSD; null = unknown
    )
    {
        public static VolumeCapabilities Detect(string anyPathOnVolume)
        {
            if (string.IsNullOrWhiteSpace(anyPathOnVolume))
                throw new ArgumentNullException(nameof(anyPathOnVolume));

            var full = Path.GetFullPath(anyPathOnVolume);
            var root = Path.GetPathRoot(full) ?? "C:\\";

            // Filesystem + cluster size
            Span<char> fsName = stackalloc char[64];
            if (!GetVolumeInformationW(root, null, 0, out _, out uint secPerCluster, out uint bytesPerSec, out _, fsName, (uint)fsName.Length))
            {
                ThrowWin32("GetVolumeInformationW", root);
            }
            string fs = new string(fsName[..fsName.IndexOf('\0') >= 0 ? fsName[..fsName.IndexOf('\0')] : fsName]);

            // Capacity
            if (!GetDiskFreeSpaceExW(root, out long free, out long total, out _))
            {
                ThrowWin32("GetDiskFreeSpaceExW", root);
            }

            var clusterSize = secPerCluster * bytesPerSec;

            // Sparse support → try FSCTL_SET_SPARSE against a temp file
            bool supportsSparse = TrySparseTest(root, clusterSize);

            // Trim support (device-level)
            bool trimEnabled = TryQueryTrimEnabled(root);

            // Seek penalty → SSD vs HDD hint
            bool? seekPenalty = TryQuerySeekPenalty(root);

            return new VolumeCapabilities(
                VolumeRoot: root,
                FileSystem: fs,
                ClusterSizeBytes: clusterSize,
                TotalBytes: total,
                FreeBytes: free,
                SupportsSparse: supportsSparse,
                TrimEnabled: trimEnabled,
                IncursSeekPenalty: seekPenalty
            );
        }

        // ------------ helpers ------------

        private static bool TrySparseTest(string root, uint clusterSize)
        {
            try
            {
                string testPath = Path.Combine(root, ".ripcord_sparse_test_" + Guid.NewGuid().ToString("N"));
                using var fs = new FileStream(testPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, (int)Math.Min(clusterSize, 65536), FileOptions.None);
                int tmp = 0;
                bool ok = DeviceIoControl(fs.SafeFileHandle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, ref tmp, IntPtr.Zero);
                fs.Close(); try { File.Delete(testPath); } catch { }
                return ok;
            }
            catch { return false; }
        }

        private static bool TryQueryTrimEnabled(string root)
        {
            try
            {
                // Open volume handle: \\.\C:
                string vol = @"\\.\" + root.TrimEnd('\\').TrimEnd(':') + ":";
                using var h = CreateFileW(vol, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                if (h.IsInvalid) return false;

                STORAGE_PROPERTY_QUERY q = new()
                {
                    PropertyId = STORAGE_PROPERTY_ID.StorageDeviceTrimProperty,
                    QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery
                };
                DEVICE_TRIM_DESCRIPTOR desc = default;
                int br = 0;
                bool ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, ref q, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), ref desc, Marshal.SizeOf<DEVICE_TRIM_DESCRIPTOR>(), ref br, IntPtr.Zero);
                return ok && desc.TrimEnabled;
            }
            catch { return false; }
        }

        private static bool? TryQuerySeekPenalty(string root)
        {
            try
            {
                string vol = @"\\.\" + root.TrimEnd('\\').TrimEnd(':') + ":";
                using var h = CreateFileW(vol, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                if (h.IsInvalid) return null;

                STORAGE_PROPERTY_QUERY q = new()
                {
                    PropertyId = STORAGE_PROPERTY_ID.StorageDeviceSeekPenaltyProperty,
                    QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery
                };
                DEVICE_SEEK_PENALTY_DESCRIPTOR desc = default;
                int br = 0;
                bool ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, ref q, Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(), ref desc, Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), ref br, IntPtr.Zero);
                return ok ? desc.IncursSeekPenalty : null;
            }
            catch { return null; }
        }

        private static void ThrowWin32(string api, string context)
        {
            int err = Marshal.GetLastWin32Error();
            throw new IOException($"{api} failed for '{context}': {new System.ComponentModel.Win32Exception(err).Message}", new System.ComponentModel.Win32Exception(err));
        }

        // --------- P/Invoke ---------
        private const uint FSCTL_SET_SPARSE = 0x900C4;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        private enum STORAGE_PROPERTY_ID
        {
            StorageDeviceProperty = 0,
            StorageDeviceSeekPenaltyProperty = 7,
            StorageDeviceTrimProperty = 8
        }

        private enum STORAGE_QUERY_TYPE
        {
            PropertyStandardQuery = 0,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public STORAGE_PROPERTY_ID PropertyId;
            public STORAGE_QUERY_TYPE QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public byte[]? AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.Bool)] public bool IncursSeekPenalty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_TRIM_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.Bool)] public bool TrimEnabled;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetVolumeInformationW(
            string lpRootPathName,
            char[]? lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            char[] lpFileSystemNameBuffer,
            uint nFileSystemNameSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetDiskFreeSpaceExW(string lpDirectoryName, out long lpFreeBytesAvailable, out long lpTotalNumberOfBytes, out long lpTotalNumberOfFreeBytes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr sec, FileMode disposition, int flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
            ref STORAGE_PROPERTY_QUERY lpInBuffer, int nInBufferSize,
            ref DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, int nOutBufferSize,
            ref int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
            ref STORAGE_PROPERTY_QUERY lpInBuffer, int nInBufferSize,
            ref DEVICE_TRIM_DESCRIPTOR lpOutBuffer, int nOutBufferSize,
            ref int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hFile, uint dwIoControlCode,
            IntPtr lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize,
            ref int lpBytesReturned, IntPtr lpOverlapped);
    }

    internal static partial class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        uint nFileSystemNameSize
    );
}
}
