// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Windows Shell API (shell32.dll) Resources
    //
    // Used for the notification-area icon. WinForms wrapped this in NotifyIcon;
    // WPF has no equivalent, so the interop is here and Ui/Shell/TrayIcon.cs
    // builds the wrapper on top of it.
    public class Shell32 {

#region Windows Shell API Data
        // Notification icon messages (NIM_*)
        public enum NotifyIconMessage : uint {
            Add        = 0x00000000,
            Modify     = 0x00000001,
            Delete     = 0x00000002,
            SetFocus   = 0x00000003,
            SetVersion = 0x00000004
        }

        // Which NOTIFYICONDATA fields are valid (NIF_*)
        [Flags]
        public enum NotifyIconFlags : uint {
            Message  = 0x00000001,
            Icon     = 0x00000002,
            Tip      = 0x00000004,
            State    = 0x00000008,
            Info     = 0x00000010,
            Guid     = 0x00000020,
            Realtime = 0x00000040,
            ShowTip  = 0x00000080
        }

        // Balloon icon (NIIF_*)
        public enum NotifyIconInfoFlags : uint {
            None      = 0x00000000,
            Info      = 0x00000001,
            Warning   = 0x00000002,
            Error     = 0x00000003,
            User      = 0x00000004,
            NoSound   = 0x00000010,
            LargeIcon = 0x00000020
        }

        // Shell version to behave as. Version 4 gives the modern callback
        // protocol: the shell sends WM_CONTEXTMENU and NIN_SELECT rather than
        // raw mouse messages, and reports the anchor point in wParam, which is
        // what lets a menu be placed correctly without guessing from the
        // cursor. It also lifts the tooltip out of the old 64-character limit
        // that the WinForms build had to defeat by reflection.
        public const uint NOTIFYICON_VERSION_4 = 4;

        // Notification-area callback messages the shell sends back
        public const int NIN_SELECT           = 0x0400;  // WM_USER + 0
        public const int NIN_KEYSELECT        = 0x0401;
        public const int NIN_BALLOONSHOW      = 0x0402;
        public const int NIN_BALLOONHIDE      = 0x0403;
        public const int NIN_BALLOONTIMEOUT   = 0x0404;
        public const int NIN_BALLOONUSERCLICK = 0x0405;
        public const int NIN_POPUPOPEN        = 0x0406;
        public const int NIN_POPUPCLOSE       = 0x0407;

        // The tip and info strings are fixed-size inline buffers, so the
        // structure has to be laid out with ByValTStr rather than pointers
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public NotifyIconFlags uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            // A union of uTimeout and uVersion; NIM_SETVERSION reads it as the
            // version, everything else as a timeout
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public NotifyIconInfoFlags dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
#endregion

#region Windows Shell API Imports
        public const string DllName = "shell32.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool Shell_NotifyIcon(NotifyIconMessage message, ref NOTIFYICONDATA data);
#endregion

    }

}
