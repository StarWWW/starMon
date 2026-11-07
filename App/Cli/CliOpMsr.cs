// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using StarMon.Driver;
using StarMon.Hardware.Cpu;
using static StarMon.Hardware.Cpu.PowerRegisters;
using StarMon.Library;

namespace StarMon.AppCli {

    // Reads the processor's power-limit registers and says what they hold.
    //
    // Read-only, and nothing but: there is no assignment syntax here, and the
    // limit-reason log bits are left as they are rather than cleared, which
    // would be a write. It exists to answer one question a stress test raises
    // and ThrottleStop cannot - which budget a package held at a fixed wattage
    // is actually being held to. ThrottleStop shows the package limit and its
    // memory-mapped copy; the platform limit, which the firmware may program
    // and which the processor reports as PL1 just the same, it does not.
    public static partial class CliOp {

        // The most a watch is allowed to run for, so a mistyped number
        // cannot leave it going for days [s]
        private const int MsrWatchMaxSeconds = 3600;

        // Prints one power-limit register, both halves and its lock
        private static void PrintLimitRegister(string name, uint index, RaplUnits units) {

            Console.Write("  " + name + " (0x" + index.ToString("X3") + "): ");

            if(!LowLevel.ReadMsr(index, out uint eax, out uint edx)) {
                Cli.PrintColor((ConsoleColor) Cli.Color.Error, "not readable" + Environment.NewLine);
                return;
            }

            Console.WriteLine("0x" + edx.ToString("X8") + eax.ToString("X8")
                + ((edx & 0x80000000) != 0 ? "  [locked until reset]" : ""));

            PrintLimitHalf("PL1", DecodeLimitHalf(eax, units));
            PrintLimitHalf("PL2", DecodeLimitHalf(edx, units));

        }

        private static void PrintLimitHalf(string name, LimitHalf half) {
            Console.WriteLine("    " + name + ": " + half.Watts.ToString("0.###") + " W, "
                + (half.Enabled ? "enabled" : "disabled")
                + (half.Clamped ? ", clamped" : "")
                + ", window " + half.WindowSeconds.ToString("0.###") + " s");
        }

        // Reads a register into one 64-bit value, or reports that it could not
        private static bool TryMsr(uint index, out ulong value) {
            value = 0;
            if(!LowLevel.ReadMsr(index, out uint eax, out uint edx))
                return false;
            value = ((ulong) edx << 32) | eax;
            return true;
        }

        private static void PrintRaw(string name, uint index, Func<ulong, string> describe) {
            Console.Write("  " + name + " (0x" + index.ToString("X3") + "): ");
            if(!TryMsr(index, out ulong value)) {
                Cli.PrintColor((ConsoleColor) Cli.Color.Error, "not readable" + Environment.NewLine);
                return;
            }
            Console.WriteLine("0x" + value.ToString("X16") + "  " + describe(value));
        }

        // -Msr [seconds]: prints the registers, then, given a number of
        // seconds, the package power and the reasons the processor is being
        // held back once a second for that long
        internal static void Msr(string seconds) {

            try { LowLevel.Open(false); } catch { }

            if(!LowLevel.HasMsr) {
                Cli.PrintError("The processor registers are unreachable: " + LowLevel.Describe());
                return;
            }

            if(!TryMsr(MsrRaplPowerUnit, out ulong unitRaw)) {
                Cli.PrintError("MSR 0x606 could not be read: this is not an Intel processor "
                    + "with RAPL, or the driver refused it.");
                return;
            }

            RaplUnits units = DecodeUnits(unitRaw);
            Console.WriteLine("  Units (0x606): power " + units.Watts + " W, energy "
                + units.Joules.ToString("0.########") + " J, time " + units.Seconds + " s");

            PrintLimitRegister("Package power limit", MsrPkgPowerLimit, units);
            PrintLimitRegister("Platform (PSys) power limit", MsrPlatformLimit, units);

            PrintRaw("PL4 / VR current config", MsrVrCurrentConfig,
                v => "PL4 field " + (v & 0x1FFF) + ((v & 0x80000000) != 0 ? ", locked" : ""));

            PrintRaw("Config TDP nominal", MsrConfigTdpNominal,
                v => "ratio " + (v & 0xFF));
            PrintRaw("Config TDP level 1", MsrConfigTdpLevel1,
                v => "TDP " + ((v & 0x7FFF) * units.Watts).ToString("0.###") + " W, ratio " + ((v >> 16) & 0xFF));
            PrintRaw("Config TDP level 2", MsrConfigTdpLevel2,
                v => "TDP " + ((v & 0x7FFF) * units.Watts).ToString("0.###") + " W, ratio " + ((v >> 16) & 0xFF));
            PrintRaw("Config TDP control", MsrConfigTdpControl,
                v => "level " + (v & 0x3) + ((v & 0x80000000) != 0 ? ", locked" : ""));
            PrintRaw("Turbo activation ratio", MsrTurboActivation,
                v => "ratio " + (v & 0xFF) + ((v & 0x80000000) != 0 ? ", locked" : ""));
            PrintRaw("Flex ratio", MsrFlexRatio,
                v => "overclocking lock " + (((v >> 20) & 1) != 0 ? "set" : "clear"));
            PrintRaw("Temperature target", MsrTemperatureTarget,
                v => "TjMax " + ((v >> 16) & 0xFF) + " °C, offset " + ((v >> 24) & 0x3F) + " °C");
            PrintRaw("Core limit reasons", MsrCoreLimitReasons,
                v => "now: " + DescribeLimitReasons((uint) v & 0xFFFF)
                    + "; since cleared: " + DescribeLimitReasons((uint) (v >> 16) & 0xFFFF));

            if(string.IsNullOrEmpty(seconds))
                return;

            if(!int.TryParse(seconds, out int duration) || duration < 1) {
                Cli.PrintError("The watch length must be a whole number of seconds: " + seconds);
                return;
            }

            MsrWatch(Math.Min(duration, MsrWatchMaxSeconds), units);

        }

        // Once a second: package power from the energy counter, the two PL1
        // values as they stand, and what is holding the cores back
        private static void MsrWatch(int duration, RaplUnits units) {

            Console.CancelKeyPress += (sender, eventArgs) => {
                IsStop = true;
                eventArgs.Cancel = true;
            };

            Console.WriteLine();
            Console.WriteLine("  time   package    PL1 pkg   PL1 psys   held back by");

            if(!LowLevel.ReadMsr(MsrPkgEnergyStatus, out uint lastEnergy, out _)) {
                Cli.PrintError("MSR 0x611 could not be read");
                return;
            }

            Stopwatch clock = Stopwatch.StartNew();
            long lastTicks = clock.ElapsedTicks;

            for(int second = 1; second <= duration && !IsStop; second++) {

                Thread.Sleep(1000);

                if(!LowLevel.ReadMsr(MsrPkgEnergyStatus, out uint energy, out _))
                    break;

                long ticks = clock.ElapsedTicks;
                double elapsed = (ticks - lastTicks) / (double) Stopwatch.Frequency;

                // A 32-bit counter, so the subtraction wraps the way it should
                double watts = unchecked(energy - lastEnergy) * units.Joules / elapsed;
                lastEnergy = energy;
                lastTicks = ticks;

                string pkg = TryMsr(MsrPkgPowerLimit, out ulong pl) ? PlText((uint) pl, units) : "?";
                string psys = TryMsr(MsrPlatformLimit, out ulong pp) ? PlText((uint) pp, units) : "?";
                string reasons = TryMsr(MsrCoreLimitReasons, out ulong lr)
                    ? DescribeLimitReasons((uint) lr & 0xFFFF) : "?";

                var line = new StringBuilder();
                line.Append(("  " + second + " s").PadRight(9));
                line.Append((watts.ToString("0.0") + " W").PadLeft(8));
                line.Append(pkg.PadLeft(11));
                line.Append(psys.PadLeft(11));
                line.Append("   " + reasons);
                Console.WriteLine(line.ToString());

            }

        }

        // A PL1 as it is being enforced: its wattage, or "off" when disabled
        private static string PlText(uint half, RaplUnits units) {
            LimitHalf limit = DecodeLimitHalf(half, units);
            return limit.Enabled ? limit.Watts.ToString("0.#") + " W" : "off";
        }

    }

}
