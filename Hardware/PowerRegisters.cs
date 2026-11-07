// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using StarMon.Driver;

namespace StarMon.Hardware.Cpu {

    // The processor's power-limit registers, read and decoded. Read-only.
    //
    // Shared by -Msr on the command line and the window's Advanced page. Both
    // answer the question a package held at a fixed wattage raises: which
    // budget is it being held to? ThrottleStop shows the package limit and its
    // memory-mapped copy; the platform (PSys) limit, which the firmware may
    // program and which the processor reports as PL1 just the same, it does
    // not.
    public static class PowerRegisters {

        // Intel's architectural and model-specific power registers
        public const uint MsrVrCurrentConfig   = 0x601;
        public const uint MsrRaplPowerUnit     = 0x606;
        public const uint MsrPkgPowerLimit     = 0x610;
        public const uint MsrPkgEnergyStatus   = 0x611;
        public const uint MsrConfigTdpNominal  = 0x648;
        public const uint MsrConfigTdpLevel1   = 0x649;
        public const uint MsrConfigTdpLevel2   = 0x64A;
        public const uint MsrConfigTdpControl  = 0x64B;
        public const uint MsrTurboActivation   = 0x64C;
        public const uint MsrCoreLimitReasons  = 0x64F;
        public const uint MsrPlatformLimit     = 0x65C;
        public const uint MsrFlexRatio         = 0x194;
        public const uint MsrTemperatureTarget = 0x1A2;

        // The units the power registers count in, from MSR 0x606
        public struct RaplUnits {
            public double Watts;    // One count of a power field [W]
            public double Joules;   // One count of the energy counter [J]
            public double Seconds;  // The base of a time window [s]
        }

        public static RaplUnits DecodeUnits(ulong raw) {
            return new RaplUnits {
                Watts   = 1.0 / (1UL << (int) (raw & 0xF)),
                Joules  = 1.0 / (1UL << (int) ((raw >> 8) & 0x1F)),
                Seconds = 1.0 / (1UL << (int) ((raw >> 16) & 0xF))
            };
        }

        // One half of a power-limit register: a 15-bit power, its enable and
        // clamp bits, and a time window stored as 2^Y × (1 + Z/4) units
        public struct LimitHalf {
            public double Watts;
            public bool Enabled;
            public bool Clamped;
            public double WindowSeconds;
        }

        public static LimitHalf DecodeLimitHalf(uint half, RaplUnits units) {
            int y = (int) ((half >> 17) & 0x1F);
            int z = (int) ((half >> 22) & 0x3);
            return new LimitHalf {
                Watts = (half & 0x7FFF) * units.Watts,
                Enabled = (half & 0x8000) != 0,
                Clamped = (half & 0x10000) != 0,
                WindowSeconds = Math.Pow(2, y) * (1 + z / 4.0) * units.Seconds
            };
        }

        // The status bits of MSR 0x64F, by bit. The log bits sit sixteen above
        // each. "PL1" and "PL2" cover both the package and the platform limit:
        // the register does not say which of the two was reached.
        private static readonly KeyValuePair<int, string>[] LimitReasonBits = {
            new KeyValuePair<int, string>(0,  "PROCHOT"),
            new KeyValuePair<int, string>(1,  "Thermal"),
            new KeyValuePair<int, string>(4,  "Residency"),
            new KeyValuePair<int, string>(5,  "Average thermal"),
            new KeyValuePair<int, string>(6,  "VR thermal alert"),
            new KeyValuePair<int, string>(7,  "VR current (TDC)"),
            new KeyValuePair<int, string>(8,  "Other (EDP)"),
            new KeyValuePair<int, string>(10, "PL1 (package/platform)"),
            new KeyValuePair<int, string>(11, "PL2 (package/platform)"),
            new KeyValuePair<int, string>(12, "Max turbo"),
            new KeyValuePair<int, string>(13, "Turbo attenuation"),
        };

        // Names the reasons set in a 16-bit field of MSR 0x64F
        public static string DescribeLimitReasons(uint bits) {
            var names = new List<string>();
            foreach(KeyValuePair<int, string> reason in LimitReasonBits)
                if((bits & (1u << reason.Key)) != 0)
                    names.Add(reason.Value);
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }

        // Reads a register into one 64-bit value, or reports that it could not
        public static bool TryRead(uint index, out ulong value) {
            value = 0;
            if(!LowLevel.ReadMsr(index, out uint eax, out uint edx))
                return false;
            value = ((ulong) edx << 32) | eax;
            return true;
        }

        // A power-limit register, both halves and its lock
        public struct LimitRegister {
            public bool Readable;
            public bool Locked;
            public LimitHalf Pl1;
            public LimitHalf Pl2;
        }

        public static LimitRegister DecodeLimitRegister(ulong raw, RaplUnits units) {
            return new LimitRegister {
                Readable = true,
                Locked = (raw >> 63) != 0,
                Pl1 = DecodeLimitHalf((uint) raw, units),
                Pl2 = DecodeLimitHalf((uint) (raw >> 32), units)
            };
        }

        // Everything the window shows, read at one moment
        public struct Snapshot {
            public bool Readable;           // Whether the registers could be read at all
            public RaplUnits Units;
            public LimitRegister Package;
            public LimitRegister Platform;
            public double PackageWatts;     // Since the previous snapshot, or -1
            public bool HasReasons;
            public uint ReasonsNow;
            public int ConfigTdpLevel;      // -1 when unreadable
            public int OverclockingLocked;  // 1 set, 0 clear, -1 unreadable
        }

        // Keeps the energy counter between snapshots, so each one can say what
        // the package drew since the last
        public sealed class Sampler {

            private bool HasLast;
            private uint LastEnergy;
            private long LastTicks;

            public Snapshot Read() {

                Snapshot s = new Snapshot { PackageWatts = -1, ConfigTdpLevel = -1, OverclockingLocked = -1 };

                if(!LowLevel.HasMsr || !TryRead(MsrRaplPowerUnit, out ulong unitRaw))
                    return s;

                s.Readable = true;
                s.Units = DecodeUnits(unitRaw);

                if(TryRead(MsrPkgPowerLimit, out ulong pkg))
                    s.Package = DecodeLimitRegister(pkg, s.Units);

                if(TryRead(MsrPlatformLimit, out ulong psys))
                    s.Platform = DecodeLimitRegister(psys, s.Units);

                if(TryRead(MsrCoreLimitReasons, out ulong reasons)) {
                    s.HasReasons = true;
                    s.ReasonsNow = (uint) reasons & 0xFFFF;
                }

                if(TryRead(MsrConfigTdpControl, out ulong ctdp))
                    s.ConfigTdpLevel = (int) (ctdp & 0x3);

                if(TryRead(MsrFlexRatio, out ulong flex))
                    s.OverclockingLocked = (int) ((flex >> 20) & 1);

                if(LowLevel.ReadMsr(MsrPkgEnergyStatus, out uint energy, out _)) {

                    long ticks = Stopwatch.GetTimestamp();

                    if(this.HasLast) {
                        double elapsed = (ticks - this.LastTicks) / (double) Stopwatch.Frequency;
                        if(elapsed > 0.05 && elapsed < 90)
                            s.PackageWatts = unchecked(energy - this.LastEnergy) * s.Units.Joules / elapsed;
                    }

                    this.HasLast = true;
                    this.LastEnergy = energy;
                    this.LastTicks = ticks;

                }

                return s;

            }

        }

    }

}
