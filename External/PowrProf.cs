// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Microsoft Power Profile Helper DLL (powrprof.dll) Resources
    // Used for receiving suspend and resume power event notifications
    public class PowrProf {

#region Microsoft Power Profile Helper DLL Data
        // Notification type constant
        public const uint DEVICE_NOTIFY_CALLBACK    = 0x0002;

        // Power management event constants
        public const uint PBT_APMRESUMEAUTOMATIC    = 0x0012;  // Resume from low-power state
        public const uint PBT_APMSUSPEND            = 0x0004;  // Suspend initiated

        // Callback routine delegate
        public delegate uint DeviceNotifyCallbackRoutine(IntPtr Context, uint Type, IntPtr Setting);

        // Device notification subscription
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS {
            public DeviceNotifyCallbackRoutine Callback;
            public IntPtr Context;
        }
#endregion

#region Microsoft Power Profile Helper DLL Imports
        public const string DllName = "powrprof.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerRegisterSuspendResumeNotification(
            uint Flags,
            ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS Recipient,
            ref IntPtr RegistrationHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerUnregisterSuspendResumeNotification(
            IntPtr RegistrationHandle);

        // Active power scheme query (used to show the current power plan name)
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerReadFriendlyName(
            IntPtr RootPowerKey, IntPtr SchemeGuid, IntPtr SubGroupOfPowerSettingsGuid,
            IntPtr PowerSettingGuid, byte[] Buffer, ref uint BufferSize);

        // The effective power-mode "overlay" the modern Windows power slider sets
        // (the classic active scheme stays Balanced while the overlay changes)
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

        // Switches the power-mode overlay, the same thing the Windows power
        // slider / quick-settings "Power mode" selector does
        // (Guid.Empty selects the default "Balanced" overlay)
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerSetActiveOverlayScheme(Guid OverlaySchemeGuid);

        // Individual power-setting value access within a power scheme,
        // separately for AC (plugged in) and DC (on battery) operation
        // (used for the processor performance boost mode setting)
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid, out uint AcValueIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerReadDCValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid, out uint DcValueIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerWriteACValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid, uint AcValueIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid,
            ref Guid PowerSettingGuid, uint DcValueIndex);

        // Re-activates a power scheme, which makes any value changes effective
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);
#endregion

    }

}
