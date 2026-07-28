// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Windows User Interface API (user32.dll) Resources
    // Used for user interface settings and system restart
    public class User32 {

#region Windows User Interface API Data
        // Change display settings
        public const int CDS_TEST           = 0x02;
        public const int CDS_UPDATEREGISTRY = 0x01;

        public const int DISP_CHANGE_FAILED    = -1;
        public const int ENUM_CURRENT_SETTINGS = -1;

        // Exit Windows flags
        public const int EWX_FORCE  = 0x00000004;
        public const int EWX_REBOOT = 0x00000002;

        // Handle identifying all windows for message broadcast purposes
        public const int HWND_BROADCAST = 0xFFFF;

        // Handle values used for setting window position
        public static readonly IntPtr HWND_BOTTOM    = new IntPtr( 1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_TOP       = new IntPtr( 0);
        public static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);

        // System commands
        public const int SC_MONITORPOWER = 0xF170;

        // GetSysColor indices
        public const int COLOR_BTNFACE  = 15; // The face colour of a button (SystemColors.Control)
        public const int COLOR_GRAYTEXT = 17; // Disabled text (SystemColors.GrayText)

        // GetSystemMetrics indices
        public const int SM_CXSCREEN = 0; // Primary display width, in pixels
        public const int SM_CYSCREEN = 1; // Primary display height, in pixels

        // Shutdown reason flags
        public const uint SHTDN_REASON_FLAG_PLANNED   = 0x80000000;
        public const uint SHTDN_REASON_MAJOR_HARDWARE = 0x00010000;
        public const uint SHTDN_REASON_MINOR_RECONFIG = 0x00000004;

        // Show window flags
        public const int SW_HIDE       = 0x00;
        public const int SW_MINIMIZE   = 0x06;
        public const int SW_SHOWNORMAL = 0x01;
        public const int SW_RESTORE    = 0x09;

        // Extended window styles
        public const int WS_EX_TOPMOST = 0x00000008;

        // Set window position flags
        public const uint SWP_NOSIZE     = 0x0001;
        public const uint SWP_NOMOVE     = 0x0002;
        public const uint SWP_NOZORDER   = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        // Key codes
        public const int VK_ENTER = 0x0D;

        // Window messages
        public const int WM_CHAR                    = 0x0102;
        public const int WM_SYSCOMMAND              = 0x0112;
        // Sent by the shell as a notification-icon callback under version 4,
        // where it means "the user asked for this icon's context menu"
        public const int WM_CONTEXTMENU             = 0x007B;
        public const int WM_CTLCOLORSTATIC          = 0x0138;
        public const int WM_DPICHANGED              = 0x02E0;
        public const int WM_HOTKEY                  = 0x0312;
        public const int WM_INITDIALOG              = 0x0110;
        public const int WM_USER                    = 0x0400;

        // Global hotkey modifier flags (RegisterHotKey)
        public const uint MOD_ALT      = 0x0001;
        public const uint MOD_CONTROL  = 0x0002;
        public const uint MOD_SHIFT    = 0x0004;
        public const uint MOD_WIN      = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // Device mode
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVMODE {

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public short dmOrientation;
            public short dmPaperSize;
            public short dmPaperLength;
            public short dmPaperWidth;
            public short dmScale;
            public short dmCopies;
            public short dmDefaultSource;
            public short dmPrintQuality;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;

        }

        // Monitor power
        public enum MONITORPOWER {
            POWERING_ON = -1,
            LOW_POWER   =  1,
            STANDBY     =  2

        }

        // Last input event timestamp (GetLastInputInfo)
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO {
            public uint cbSize;
            public uint dwTime;
        }
#endregion

#region Windows User Interface API Imports
        public const string DllName = "user32.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int ExitWindowsEx(int uFlags, uint dwReason);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int GetSysColor(int nIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool PostMessage(IntPtr hWnd, [MarshalAs(UnmanagedType.U4)] uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint RegisterWindowMessage(string message);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int SendMessage(IntPtr hWnd, [MarshalAs(UnmanagedType.U4)] uint msg, IntPtr wParam, IntPtr lParam);

        [DllImportAttribute(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImportAttribute(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImportAttribute(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool SetWindowText(IntPtr hWnd, string text);

        [DllImportAttribute(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImportAttribute(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
#endregion

    }

}
