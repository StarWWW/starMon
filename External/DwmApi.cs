// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Desktop Window Manager (dwmapi.dll) resources
    // Used to opt a window's non-client area (title bar) into dark mode
    public static class DwmApi {

        public const string DllName = "dwmapi.dll";

        // Immersive dark-mode window attribute
        // Value 20 applies from Windows 10 build 18985 (20H1) onwards,
        // while value 19 was used on earlier 1809/1903/1909 builds
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE              = 20;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1  = 19;

        // Rounded corners, Windows 11 and later. The window draws its own
        // title bar, and a square-cornered window beside the rounded ones the
        // rest of the system draws looks like a rendering fault.
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_DEFAULT    = 0;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND      = 2;
        public const int DWMWCP_ROUNDSMALL = 3;

        // The material behind the window, Windows 11 22H2 and later. Mica
        // tints the desktop wallpaper through the window, which is what makes
        // an application look like part of the system rather than a rectangle
        // sitting on it. Unsupported releases return a failure code, which is
        // the whole reason the caller has to check rather than assume.
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMSBT_AUTO           = 0;
        public const int DWMSBT_NONE           = 1;
        public const int DWMSBT_MAINWINDOW     = 2;  // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        public const int DWMSBT_TABBEDWINDOW   = 4;

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, PreserveSig = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    }

}
