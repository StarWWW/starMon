// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using Microsoft.Win32;
using StarMon.Driver;
using StarMon.External;
using StarMon.Library;

namespace StarMon.Hardware.Cpu {

    // Provides live CPU power and clock readings
    public static class CpuMetrics {

        private enum Vendor { Unknown, Intel, Amd }

        // Detected processor characteristics
        private static Vendor DetectedVendor;
        private static int BaseClockMhz;
        private static bool Detected;

        // RAPL energy sampling state
        private static bool HasEnergy;
        private static uint LastEnergyRaw;
        private static long LastEnergyTicks;
        private static double EnergyUnitJoules;

        // APERF / MPERF sampling state
        private static bool HasPerf;
        private static ulong LastAperf;
        private static ulong LastMperf;

        // Per-core APERF / MPERF sampling state
        private static bool HasPerfCore;
        private static ulong[] LastAperfCore;
        private static ulong[] LastMperfCore;
        private static bool[] LastValidCore;

        // Intel RAPL registers
        private const uint MSR_RAPL_POWER_UNIT   = 0x606;
        private const uint MSR_PKG_ENERGY_STATUS = 0x611;
        private const uint MSR_PKG_POWER_LIMIT   = 0x610;

        // AMD RAPL registers
        private const uint MSR_AMD_RAPL_PWR_UNIT   = 0xC0010299;
        private const uint MSR_AMD_PKG_ENERGY_STAT = 0xC001029B;

        // Performance frequency counters
        private const uint MSR_IA32_MPERF = 0x0E7;
        private const uint MSR_IA32_APERF = 0x0E8;

        // Reject deltas that span more than this (e.g. the system was asleep) [ms].
        // Comfortably above the slow background recording interval so that those
        // longer-spaced samples are still accepted while sleep gaps are rejected.
        private const long MaxDeltaMs = 90000;

        // Whether either metric can be read on this processor
        public static bool IsAvailable {
            get { EnsureDetected(); return DetectedVendor != Vendor.Unknown; }
        }

        // Clears the delta baselines; call when the window becomes visible again
        // so that a long, stale interval is not used for the next sample
        public static void Reset() {
            HasEnergy = false;
            HasPerf = false;
            HasPerfCore = false;
        }

        // Detects the CPU vendor and base clock once from the registry
        private static void EnsureDetected() {
            if(Detected)
                return;
            Detected = true;
            DetectedVendor = Vendor.Unknown;

            try {
                using(RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) {

                    if(key == null)
                        return;

                    string id = key.GetValue("VendorIdentifier") as string ?? "";
                    if(id.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                        DetectedVendor = Vendor.Intel;
                    else if(id.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0
                         || id.IndexOf("Authentic", StringComparison.OrdinalIgnoreCase) >= 0)
                        DetectedVendor = Vendor.Amd;

                    object mhz = key.GetValue("~MHz");
                    if(mhz != null)
                        int.TryParse(mhz.ToString(), out BaseClockMhz);
                }
            } catch {
                DetectedVendor = Vendor.Unknown;
            } finally {

                // A single detection entry, so the log always tells why the
                // CPU power and clock metrics are (or are not) being shown
                Logger.Info("CpuMetrics", "CPU detected: " + DetectedVendor
                    + ", base clock " + BaseClockMhz + " MHz"
                    + ", MSR driver " + (Ring0.IsOpen ? "open" : "not open"));
            }
        }

        // Reads the package power limits the processor is currently held to:
        // PL1, the sustained budget, and PL2, the short burst budget — the
        // figures a "45 W processor" is named after. Intel only; returns
        // false where the registers cannot be read.
        public static bool GetPowerLimits(out double pl1Watts, out double pl2Watts) {

            pl1Watts = -1; pl2Watts = -1;

            EnsureDetected();
            if(!Ring0.IsOpen || DetectedVendor != Vendor.Intel)
                return false;

            try {

                if(!Ring0.ReadMsr(MSR_RAPL_POWER_UNIT, out uint unitEax, out _))
                    return false;

                // Bits 3:0 carry the power unit as a divider exponent
                double unit = 1.0 / (1u << (int)(unitEax & 0xF));

                if(!Ring0.ReadMsr(MSR_PKG_POWER_LIMIT, out uint eax, out uint edx))
                    return false;

                // PL1 lives in bits 14:0 of the low word, PL2 in the same
                // bits of the high word — but bit 15 of each is the enable
                // flag, and the value field keeps whatever was last written
                // whether or not the limit is being enforced. A profile that
                // lifts the sustained cap leaves a perfectly plausible number
                // sitting in a register nothing is honouring, and reporting
                // that as "the budget" is worse than reporting nothing.
                pl1Watts = (eax & 0x8000) != 0 ? (eax & 0x7FFF) * unit : -1;
                pl2Watts = (edx & 0x8000) != 0 ? (edx & 0x7FFF) * unit : -1;

                if(pl1Watts >= 1000) pl1Watts = -1;
                if(pl2Watts >= 1000) pl2Watts = -1;

                // The caller treats a non-positive figure as "unavailable" and
                // hides the row, so one limit being disabled still lets the
                // other be shown
                return pl1Watts > 0 || pl2Watts > 0;

            } catch {
                return false;
            }

        }

        // Returns the average CPU package power since the previous call, in watts,
        // or -1 if unavailable or this is the first (baseline) sample
        public static double GetPowerWatts() {
            EnsureDetected();
            if(!Ring0.IsOpen || DetectedVendor == Vendor.Unknown)
                return -1;

            uint unitMsr = DetectedVendor == Vendor.Intel ? MSR_RAPL_POWER_UNIT : MSR_AMD_RAPL_PWR_UNIT;
            uint energyMsr = DetectedVendor == Vendor.Intel ? MSR_PKG_ENERGY_STATUS : MSR_AMD_PKG_ENERGY_STAT;

            try {
                // The energy unit is fixed for the lifetime of the system
                if(EnergyUnitJoules <= 0) {
                    if(!Ring0.ReadMsr(unitMsr, out uint unitEax, out _))
                        return -1;
                    int esu = (int)((unitEax >> 8) & 0x1F);
                    EnergyUnitJoules = 1.0 / (1u << esu);
                }

                if(!Ring0.ReadMsr(energyMsr, out uint eax, out _))
                    return -1;

                uint raw = eax;
                long now = Stopwatch.GetTimestamp();

                if(!HasEnergy) {
                    LastEnergyRaw = raw;
                    LastEnergyTicks = now;
                    HasEnergy = true;
                    return -1;
                }

                double seconds = (double)(now - LastEnergyTicks) / Stopwatch.Frequency;
                uint deltaRaw = unchecked(raw - LastEnergyRaw); // 32-bit wrap-safe
                LastEnergyRaw = raw;
                LastEnergyTicks = now;

                if(seconds <= 0 || seconds * 1000 > MaxDeltaMs)
                    return -1;

                double watts = (deltaRaw * EnergyUnitJoules) / seconds;
                return (watts >= 0 && watts < 1000) ? watts : -1;

            } catch {
                return -1;
            }
        }

        // Returns the average effective CPU clock since the previous call, in MHz,
        // or -1 if unavailable or this is the first (baseline) sample
        public static int GetClockMhz() {
            EnsureDetected();
            if(!Ring0.IsOpen || DetectedVendor == Vendor.Unknown || BaseClockMhz <= 0)
                return -1;

            // APERF and MPERF are per-logical-processor, so pin to a single core
            // for the duration of the read to keep the deltas consistent
            System.Threading.Thread.BeginThreadAffinity();
            UIntPtr previous = Kernel32.SetThreadAffinityMask(
                Kernel32.GetCurrentThread(), (UIntPtr)1);
            try {
                if(!Ring0.ReadMsr(MSR_IA32_APERF, out uint aLo, out uint aHi))
                    return -1;
                if(!Ring0.ReadMsr(MSR_IA32_MPERF, out uint mLo, out uint mHi))
                    return -1;

                ulong aperf = ((ulong)aHi << 32) | aLo;
                ulong mperf = ((ulong)mHi << 32) | mLo;

                if(!HasPerf) {
                    LastAperf = aperf;
                    LastMperf = mperf;
                    HasPerf = true;
                    return -1;
                }

                ulong deltaA = unchecked(aperf - LastAperf);
                ulong deltaM = unchecked(mperf - LastMperf);
                LastAperf = aperf;
                LastMperf = mperf;

                if(deltaM == 0)
                    return -1;

                int mhz = (int)Math.Round(BaseClockMhz * ((double)deltaA / deltaM));
                return (mhz > 0 && mhz < 12000) ? mhz : -1;

            } catch {
                return -1;
            } finally {
                if(previous != UIntPtr.Zero)
                    Kernel32.SetThreadAffinityMask(Kernel32.GetCurrentThread(), previous);
                System.Threading.Thread.EndThreadAffinity();
            }
        }

        // Returns the effective clock of every physical core, in MHz, since the
        // previous call (pins to one logical processor of each core in turn).
        // Returns null when unavailable; the first call after a Reset() only
        // establishes the baselines and reports -1 for each core.
        public static int[] GetPerCoreClocks() {
            EnsureDetected();
            if(!Ring0.IsOpen || DetectedVendor == Vendor.Unknown || BaseClockMhz <= 0)
                return null;

            ulong[] masks = Topology.GetPhysicalCoreMasks();
            if(masks == null || masks.Length == 0)
                return null;

            int n = masks.Length;
            if(LastAperfCore == null || LastAperfCore.Length != n) {
                LastAperfCore = new ulong[n];
                LastMperfCore = new ulong[n];
                LastValidCore = new bool[n];
                HasPerfCore = false;
            }

            ulong[] aperf = new ulong[n];
            ulong[] mperf = new ulong[n];
            bool[] ok = new bool[n];

            System.Threading.Thread.BeginThreadAffinity();
            IntPtr thread = Kernel32.GetCurrentThread();
            UIntPtr previous = Kernel32.SetThreadAffinityMask(thread, (UIntPtr)masks[0]);
            try {
                for(int i = 0; i < n; i++) {
                    Kernel32.SetThreadAffinityMask(thread, (UIntPtr)masks[i]);
                    if(Ring0.ReadMsr(MSR_IA32_APERF, out uint aLo, out uint aHi)
                        && Ring0.ReadMsr(MSR_IA32_MPERF, out uint mLo, out uint mHi)) {
                        aperf[i] = ((ulong)aHi << 32) | aLo;
                        mperf[i] = ((ulong)mHi << 32) | mLo;
                        ok[i] = true;
                    }
                }
            } catch {
                return null;
            } finally {
                if(previous != UIntPtr.Zero)
                    Kernel32.SetThreadAffinityMask(thread, previous);
                System.Threading.Thread.EndThreadAffinity();
            }

            int[] result = new int[n];

            if(!HasPerfCore) {
                for(int i = 0; i < n; i++) {
                    LastAperfCore[i] = aperf[i];
                    LastMperfCore[i] = mperf[i];
                    LastValidCore[i] = ok[i];
                    result[i] = -1;
                }
                HasPerfCore = true;
                return result;
            }

            for(int i = 0; i < n; i++) {

                // A failed read leaves the previous baseline in place: the
                // counters are monotonic, so the next successful read still
                // yields a valid (just longer-window) delta
                if(!ok[i]) {
                    result[i] = -1;
                    continue;
                }

                // No baseline yet for this core: establish one now
                if(!LastValidCore[i]) {
                    LastAperfCore[i] = aperf[i];
                    LastMperfCore[i] = mperf[i];
                    LastValidCore[i] = true;
                    result[i] = -1;
                    continue;
                }

                ulong deltaA = unchecked(aperf[i] - LastAperfCore[i]);
                ulong deltaM = unchecked(mperf[i] - LastMperfCore[i]);
                LastAperfCore[i] = aperf[i];
                LastMperfCore[i] = mperf[i];

                int mhz = deltaM == 0 ? -1
                    : (int)Math.Round(BaseClockMhz * ((double)deltaA / deltaM));
                result[i] = (mhz > 0 && mhz < 12000) ? mhz : -1;
            }

            return result;
        }

    }

}
