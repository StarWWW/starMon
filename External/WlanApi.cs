// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Provides Wi-Fi connection signal and link-rate information
    public static class WlanApi {

        private const string DllName = "wlanapi.dll";

        private const int WlanIntfOpcodeCurrentConnection = 7;
        private const int WlanInterfaceStateConnected = 1;

        // Size of one WLAN_INTERFACE_INFO record:
        // Guid (16) + strInterfaceDescription WCHAR[256] (512) + state (4)
        private const int InterfaceInfoSize = 16 + 512 + 4;

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceGuid, int opcode,
            IntPtr reserved, out uint dataSize, out IntPtr data, IntPtr opcodeValueType);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern void WlanFreeMemory(IntPtr memory);

        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES {
            public DOT11_SSID dot11Ssid;
            public uint dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public uint dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality; // 0-100
            public uint ulRxRate;          // kbps
            public uint ulTxRate;          // kbps
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_CONNECTION_ATTRIBUTES {
            public uint isState;
            public uint wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            // Trailing security attributes intentionally omitted; reading only the
            // leading portion of the larger native buffer is safe.
        }

        // Compatibility overload without the network name
        public static bool GetSignal(out int signalPercent, out int rxMbps, out int txMbps) {
            return GetSignal(out signalPercent, out rxMbps, out txMbps, out string _);
        }

        // Reads the signal quality (0-100), receive / transmit rates (Mbps) and
        // network name (SSID) of the first connected wireless interface.
        // Returns false if unavailable.
        public static bool GetSignal(out int signalPercent, out int rxMbps, out int txMbps, out string ssid) {
            signalPercent = -1; rxMbps = -1; txMbps = -1; ssid = "";

            IntPtr handle = IntPtr.Zero;
            IntPtr list = IntPtr.Zero;
            try {
                if(WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0 || handle == IntPtr.Zero)
                    return false;

                if(WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero)
                    return false;

                // WLAN_INTERFACE_INFO_LIST: dwNumberOfItems (4), dwIndex (4), then records
                int count = Marshal.ReadInt32(list, 0);
                if(count <= 0 || count > 64)
                    return false;

                for(int i = 0; i < count; i++) {
                    int recordOffset = 8 + i * InterfaceInfoSize;

                    byte[] guidBytes = new byte[16];
                    Marshal.Copy(IntPtr.Add(list, recordOffset), guidBytes, 0, 16);
                    Guid guid = new Guid(guidBytes);

                    int state = Marshal.ReadInt32(list, recordOffset + 16 + 512);
                    if(state != WlanInterfaceStateConnected)
                        continue;

                    if(WlanQueryInterface(handle, ref guid, WlanIntfOpcodeCurrentConnection,
                            IntPtr.Zero, out uint _, out IntPtr data, IntPtr.Zero) != 0
                        || data == IntPtr.Zero)
                        continue;

                    try {
                        WLAN_CONNECTION_ATTRIBUTES attr =
                            Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(data);

                        int sig = (int)attr.wlanAssociationAttributes.wlanSignalQuality;
                        if(sig < 0 || sig > 100)
                            continue;

                        signalPercent = sig;
                        rxMbps = (int)(attr.wlanAssociationAttributes.ulRxRate / 1000);
                        txMbps = (int)(attr.wlanAssociationAttributes.ulTxRate / 1000);

                        // The network name, preferring the raw SSID bytes over
                        // the profile name (they usually match)
                        try {
                            uint len = attr.wlanAssociationAttributes.dot11Ssid.uSSIDLength;
                            if(len > 0 && len <= 32)
                                ssid = System.Text.Encoding.UTF8.GetString(
                                    attr.wlanAssociationAttributes.dot11Ssid.ucSSID, 0, (int)len);
                            else if(!string.IsNullOrEmpty(attr.strProfileName))
                                ssid = attr.strProfileName;
                        } catch { }
                        return true;
                    } finally {
                        WlanFreeMemory(data);
                    }
                }

                return false;
            } catch {
                return false;
            } finally {
                if(list != IntPtr.Zero)
                    WlanFreeMemory(list);
                if(handle != IntPtr.Zero)
                    WlanCloseHandle(handle, IntPtr.Zero);
            }
        }

    }

}
