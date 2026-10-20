// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Configuration Manager (cfgmgr32.dll) resources
    // Used to read a device's power state from the Plug and Play manager,
    // which answers from what it already knows and does not touch the device
    public class CfgMgr32 {

#region Configuration Manager Data
        public const int CR_SUCCESS = 0;

        // Filters for the device instance list
        public const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x00000001;
        public const uint CM_GETIDLIST_FILTER_PRESENT    = 0x00000100;

        public const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

        // DEVICE_POWER_STATE
        public const int PowerDeviceD0 = 1;
        public const int PowerDeviceD3 = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY {
            public Guid fmtid;
            public uint pid;
        }

        // DEVPKEY_Device_ClassGuid
        public static readonly DEVPROPKEY DEVPKEY_Device_ClassGuid = new DEVPROPKEY {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 10 };

        // DEVPKEY_Device_PowerData: a CM_POWER_DATA, whose second field is the
        // power state the device was most recently put in
        public static readonly DEVPROPKEY DEVPKEY_Device_PowerData = new DEVPROPKEY {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 32 };

        // GUID_DEVCLASS_DISPLAY
        public static readonly Guid GUID_DEVCLASS_DISPLAY =
            new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
#endregion

#region Configuration Manager Imports
        public const string DllName = "cfgmgr32.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, uint length, uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key,
            out uint type, byte[] buffer, ref uint size, uint flags);
#endregion

    }

}
