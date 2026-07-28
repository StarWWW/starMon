// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.External;
using StarMon.Library;

namespace StarMon.Hardware {

    // Provides processor performance boost mode control
    public static class CpuBoost {

        // Processor power settings subgroup and the boost mode setting
        private static Guid SubProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static Guid PerfBoostMode = new Guid("be337238-0d82-4146-a960-4f3749d470c7");

        // Boost mode values (as defined by Windows; 3-6 are Efficient/
        // Guaranteed variants that are treated as their base mode here)
        public const int Disabled = 0;
        public const int Enabled = 1;
        public const int Aggressive = 2;

        // Returns the boost mode of the active scheme for the current power
        // source (AC when plugged in, DC on battery), or -1 when unavailable
        public static int Get() {
            IntPtr guidPtr = IntPtr.Zero;
            try {
                if(PowrProf.PowerGetActiveScheme(IntPtr.Zero, out guidPtr) != 0 || guidPtr == IntPtr.Zero)
                    return -1;

                Guid scheme = (Guid) System.Runtime.InteropServices.Marshal.PtrToStructure(guidPtr, typeof(Guid));

                bool onAc = true;
                try {
                    if(Kernel32.GetSystemPowerStatus(out Kernel32.SYSTEM_POWER_STATUS sps))
                        onAc = sps.ACLineStatus == 1;
                } catch { }

                uint value;
                uint status = onAc
                    ? PowrProf.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref PerfBoostMode, out value)
                    : PowrProf.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref PerfBoostMode, out value);

                return status == 0 ? (int) value : -1;
            } catch {
                return -1;
            } finally {
                if(guidPtr != IntPtr.Zero)
                    Kernel32.LocalFree(guidPtr);
            }
        }

        // Sets the boost mode for both AC and DC operation of the active
        // scheme and re-activates it so the change takes effect immediately
        public static bool Set(int mode) {
            if(mode < 0)
                return false;

            IntPtr guidPtr = IntPtr.Zero;
            try {
                if(PowrProf.PowerGetActiveScheme(IntPtr.Zero, out guidPtr) != 0 || guidPtr == IntPtr.Zero)
                    return false;

                Guid scheme = (Guid) System.Runtime.InteropServices.Marshal.PtrToStructure(guidPtr, typeof(Guid));

                bool ok =
                    PowrProf.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref PerfBoostMode, (uint) mode) == 0
                    & PowrProf.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref SubProcessor, ref PerfBoostMode, (uint) mode) == 0;

                // The new values only apply once the scheme is re-activated
                ok &= PowrProf.PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;

                if(ok)
                    Logger.Info("CpuBoost", "Processor boost mode set to " + mode);
                else
                    Logger.Warning("CpuBoost", "Setting the processor boost mode failed", "mode " + mode);

                return ok;
            } catch(Exception e) {
                Logger.Error("CpuBoost", "Setting the processor boost mode failed", e.Message);
                return false;
            } finally {
                if(guidPtr != IntPtr.Zero)
                    Kernel32.LocalFree(guidPtr);
            }
        }

    }

}
