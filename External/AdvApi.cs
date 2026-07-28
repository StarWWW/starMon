// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Advanced Windows NT Base API (advapi32.dll) resources
    // Used for kernel driver installation (service management)
    // and privilege token management
    public class AdvApi32 {

#region Advanced Windows NT Base API Data
        public const int SE_PRIVILEGE_ENABLED = 0x00000002;

        public const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
        public const int TOKEN_QUERY             = 0x00000008;

        [Flags]
        public enum SC_MANAGER_ACCESS_MASK : uint {
            SC_MANAGER_CONNECT        = 0x00001,
            SC_MANAGER_CREATE_SERVICE = 0x00002,
            SC_MANAGER_ALL_ACCESS     = 0xF003F
        }

        [Flags]
        public enum SERVICE_ACCESS_MASK : uint {
            SERVICE_ALL_ACCESS           = 0xF01FF
        }

        public enum SERVICE_CONTROL : uint {
            SERVICE_CONTROL_STOP = 1
        }

        public enum SERVICE_ERROR : uint {
            SERVICE_ERROR_NORMAL = 1
        }

        public enum SERVICE_START : uint {
            SERVICE_DEMAND_START = 3
        }

        public enum SERVICE_STATE : uint {
            SERVICE_STOPPED = 0x00000001
        }

        public enum SERVICE_TYPE : uint {
            SERVICE_KERNEL_DRIVER       = 0x00000001,
        }

        // A 64-bit identifier unique locally until restart 
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct LUID {
            public int LowPart;
            public int HighPart;
        }

        // Locally-unique identifier and its attributes
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct LUID_AND_ATTRIBUTES {
            public LUID Luid;
            public int Attributes;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SERVICE_STATUS {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TOKEN_PRIVILEGES {
            public int PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }
#endregion

#region Advanced Windows NT Base API Imports
        public const string DllName = "advapi32.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int AdjustTokenPrivileges(
            IntPtr TokenHandle,
            int DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            int BufferLength,
            ref TOKEN_PRIVILEGES PreviousState,
            ref int ReturnLength);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ControlService(IntPtr hService, SERVICE_CONTROL dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr CreateService(
            IntPtr hSCManager,
            string lpServiceName,
            string lpDisplayName,
            SERVICE_ACCESS_MASK dwDesiredAccess,
            SERVICE_TYPE dwServiceType,
            SERVICE_START dwStartType,
            SERVICE_ERROR dwErrorControl,
            string lpBinaryPathName,
            string lpLoadOrderGroup,
            string lpdwTagId,
            string lpDependencies,
            string lpServiceStartName,
            string lpPassword);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteService(IntPtr hService);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int LookupPrivilegeValue(string lpSystemName, string lpName, ref LUID lpLuid);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, ref IntPtr TokenHandle);

        [DllImport(DllName, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, SC_MANAGER_ACCESS_MASK dwDesiredAccess);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, SERVICE_ACCESS_MASK dwDesiredAccess);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS dwServiceStatus);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, string[] lpServiceArgVectors);
#endregion

    }

}
