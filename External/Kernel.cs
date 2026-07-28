// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StarMon.External {

    // Windows NT Kernel (kernel32.dll) resources
    // Used for console manipulation
    // and hardware operations with a kernel driver
    public class Kernel32 {

#region Windows NT Kernel Data
        // Console manipulation

        public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        // File operations

        public enum MoveFileFlags : uint {

            MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004

        }

        // Hardware operations with a kernel driver

        public const int ERROR_SERVICE_ALREADY_RUNNING = unchecked((int) 0x80070420);
        public const int ERROR_SERVICE_EXISTS          = unchecked((int) 0x80070431);

        public const uint OLS_TYPE = 40000;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct IOControlCode {
            public uint Code { get; }

            public IOControlCode(uint deviceType, uint function, Access access) : this(deviceType, function, Method.Buffered, access) { }

            public IOControlCode(uint deviceType, uint function, Method method, Access access) {
                Code = (deviceType << 16) | ((uint) access << 14) | (function << 2) | (uint) method;
            }

            public enum Method : uint {
                Buffered  = 0,
                InDirect  = 1,
                OutDirect = 2,
                Neither   = 3
            }

            public enum Access : uint {
                Any   = 0,
                Read  = 1,
                Write = 2
            }
        }

        public static readonly IOControlCode IOCTL_OLS_GET_REFCOUNT = new(OLS_TYPE, 0x801, Kernel32.IOControlCode.Access.Any);
        public static readonly IOControlCode IOCTL_OLS_READ_MSR = new(OLS_TYPE, 0x821, Kernel32.IOControlCode.Access.Any);
        public static readonly IOControlCode IOCTL_OLS_WRITE_MSR = new(OLS_TYPE, 0x822, Kernel32.IOControlCode.Access.Any);
        public static readonly IOControlCode IOCTL_OLS_READ_IO_PORT_BYTE = new(OLS_TYPE, 0x833, Kernel32.IOControlCode.Access.Read);
        public static readonly IOControlCode IOCTL_OLS_WRITE_IO_PORT_BYTE = new(OLS_TYPE, 0x836, Kernel32.IOControlCode.Access.Write);
        public static readonly IOControlCode IOCTL_OLS_READ_PCI_CONFIG = new(OLS_TYPE, 0x851, Kernel32.IOControlCode.Access.Read);
        public static readonly IOControlCode IOCTL_OLS_WRITE_PCI_CONFIG = new(OLS_TYPE, 0x852, Kernel32.IOControlCode.Access.Write);
        public static readonly IOControlCode IOCTL_OLS_READ_MEMORY = new(OLS_TYPE, 0x841, Kernel32.IOControlCode.Access.Read);
#endregion

#region Windows NT Kernel Imports
        public const string DllName = "kernel32.dll";

        // Console manipulation

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool AllocConsole();

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool FreeConsole();

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        public static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetConsoleWindow();

        // File operations

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        // Hardware operations with a kernel driver

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr CreateFile (
            string lpFileName,
            uint dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            FileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool DeviceIoControl (
            SafeFileHandle device,
            IOControlCode ioControlCode,
            [MarshalAs(UnmanagedType.AsAny)] [In] object inBuffer,
            uint inBufferSize,
            [MarshalAs(UnmanagedType.AsAny)] [Out] object outBuffer,
            uint nOutBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        // Power status

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS {
            public byte ACLineStatus;        // 0 = offline, 1 = online, 255 = unknown
            public byte BatteryFlag;         // Bit flags (1 high, 2 low, 4 critical, 8 charging, 128 no battery)
            public byte BatteryLifePercent;  // 0-100, or 255 if unknown
            public byte SystemStatusFlag;    // Battery saver status
            public int BatteryLifeTime;      // Seconds of battery life remaining, or -1 if unknown
            public int BatteryFullLifeTime;  // Seconds of full battery life, or -1 if unknown
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        // System-wide CPU load and memory usage

        // System CPU times (each is a FILETIME, 8 bytes, marshalled as a long).
        // Kernel time includes idle time, so busy = (kernel + user) - idle.
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX {
            public uint dwLength;
            public uint dwMemoryLoad;        // Used physical memory [%]
            public ulong ullTotalPhys;       // Total physical memory [bytes]
            public ulong ullAvailPhys;       // Available physical memory [bytes]
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // Milliseconds since the system was started (used for uptime)
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern ulong GetTickCount64();

        // Processor topology (used to read one MSR per physical core).
        // The buffer is parsed manually as fixed-size 32-byte records (x64) to
        // avoid marshalling the variable union inside the native structure.
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint returnedLength);

        // Thread processor affinity
        // (used to pin per-core MSR reads such as APERF/MPERF to a single core)

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetCurrentThread();

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

        // Raw device I/O (used for querying storage device temperature via SMART)

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[] lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool CloseHandle(IntPtr hObject);

        // Frees memory the power-profile API allocated (e.g. the active scheme GUID)
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr LocalFree(IntPtr hMem);
#endregion

    }

}
