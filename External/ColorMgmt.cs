// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;

namespace StarMon.External {

    // Microsoft Color Matching System DLL (mscms.dll) Resources
    // Used for reapplying the color profile (also part of nVidia Advanced Optimus fix)
    public class ColorMgmt {

#region Microsoft Color Matching System DLL Imports
        public const string DllName = "mscms.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool WcsSetCalibrationManagementState(bool bIsEnabled);
#endregion

    }

}
