// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Globalization;
using System.Text;
using StarMon.Hardware;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.AppCli {

    public static partial class CliOp {

        // Writes down everything this machine says about itself.
        //
        // The register map compiled into this application comes from one
        // board's ACPI tables, and the only way that ever gets any wider is if
        // somebody with a different board can produce a description of it
        // without reading the source or owning a debugger. That is what this
        // is for: one command, one file, nothing to interpret by hand.
        //
        // Read-only throughout. It asks the firmware questions and reads
        // Embedded Controller registers; it writes nothing to either. A report
        // that changed the machine it was describing would be worse than no
        // report, and this runs on machines that are already misbehaving.
        public static void Probe(string[] args) {

            string path = args != null && args.Length > 1
                && !args[1].StartsWith("-", StringComparison.Ordinal)
                ? args[1] : null;

            // Neither failure stops the report. A machine where one of these
            // will not initialise is precisely the machine somebody is trying
            // to describe, and a report that refuses to be produced there
            // describes nothing.
            Hw.BiosInit();
            Hw.EcInit();

            Platform platform = null;

            try {
                DeviceProfile.Probe(new Settings());
            } catch(Exception e) {
                Logger.Error("Device", "Hardware profiling failed", e.Message);
            }

            try {

                platform = new Platform();
                DeviceProfile.Attach(platform);

                // Without this the capability report has no platform to probe
                // against, and silently omits the entire HP BIOS and Embedded
                // Controller block — the keyboard backlight, the colour zones,
                // the MUX, the graphics power, the fan-level path, the fan
                // table. Which is to say: everything this application is for,
                // missing from the report written to describe it.
                //
                // Found by running -Probe on real hardware and reading what
                // came out. The interface calls this on startup, so nothing
                // that used the report from a running window could see it.
                FeatureSupport.Initialize(platform);

            } catch(Exception e) {
                Logger.Error("Device", "Platform initialisation failed", e.Message);
            }

            string report = Compose(platform);

            if(path == null) {

                Console.WriteLine(report);

            } else {

                try {

                    System.IO.File.WriteAllText(path, report, new UTF8Encoding(false));
                    Console.WriteLine(path);

                } catch(Exception e) {

                    App.Error("ErrProbeWrite|" + e.Message);

                }

            }

        }

        // The report itself, as Markdown so it can be pasted into an issue.
        //
        // Internal so the tests can compose one against each board in the
        // device matrix. That is not a formality: this exists to be run on
        // machines that are already misbehaving, so a composer that throws
        // when a call is refused would fail exactly when it is needed.
        internal static string Compose(Platform platform) {

            StringBuilder sb = new StringBuilder(8192);

            sb.AppendLine("# StarMon hardware report");
            sb.AppendLine();
            sb.AppendLine("Version " + Config.AppVersion
                + " · produced by `StarMon.exe -Probe`");
            sb.AppendLine();
            sb.AppendLine("Read-only: this asked the firmware and the Embedded "
                + "Controller for values and changed neither.");
            sb.AppendLine();

            sb.AppendLine("| | |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| Firmware interface | "
                + (Hw.HasBios ? "available" : "**not available**") + " |");
            sb.AppendLine("| Embedded Controller | "
                + (Hw.HasEc ? "available" : "**not reachable**") + " |");
            sb.AppendLine("| Kernel driver | " + StarMon.Driver.LowLevel.Describe()
                + " |");
            sb.AppendLine("| Code integrity | " + CodeIntegrity.Summary() + " |");
            sb.AppendLine();

            if(!Hw.HasEc) {
                sb.AppendLine("> " + CodeIntegrity.Explain().Replace("\n\n", " "));
                sb.AppendLine();
            }


            // Everything the application already knows how to say about a
            // machine, rather than a second description of the same thing
            sb.AppendLine("```");
            try {
                sb.AppendLine(Capabilities.Report(platform));
            } catch(Exception e) {
                sb.AppendLine("The capability report failed: " + e.Message);
            }
            sb.AppendLine("```");
            sb.AppendLine();

            AppendRegisterMap(sb);
            AppendNamedRegisters(sb);
            AppendFanTable(sb);
            AppendFans(sb, platform);

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("If something on this machine does not work, this file "
                + "is what makes it diagnosable by somebody who does not have one.");

            return sb.ToString();

        }

        // All 256 registers, as a table.
        //
        // The raw dump matters more than the named readings below it: the names
        // are this build's guesses at what a register means, and on a board
        // whose firmware lays them out differently the guesses are exactly what
        // is wrong. The numbers are not a guess.
        private static void AppendRegisterMap(StringBuilder sb) {

            sb.AppendLine("## Embedded Controller registers");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("     _0 _1 _2 _3 _4 _5 _6 _7 _8 _9 _a _b _c _d _e _f");

            for(int high = 0; high <= 0xF0; high += 0x10) {

                sb.Append(Conv.GetString((uint) (high >> 4), 1, 16)).Append("_  ");

                for(int low = 0; low <= 0xF; low++) {

                    byte value;

                    // A register the board does not implement is written as
                    // "--" rather than as the nought a blind read hands back.
                    // The difference is the whole point of the dump.
                    sb.Append(Hw.EcTryGetByte((byte) (high | low), out value)
                        ? Conv.GetString(value, 2, 16) : "--");

                    sb.Append(low == 0xF ? "" : " ");

                }

                sb.AppendLine();

            }

            sb.AppendLine("```");
            sb.AppendLine();

        }

        // The registers this build has names for, with what they hold here
        private static void AppendNamedRegisters(StringBuilder sb) {

            sb.AppendLine("## Named registers");
            sb.AppendLine();
            sb.AppendLine("Names are this build's reading of one board's ACPI "
                + "tables. A value that makes no sense beside its name is the "
                + "interesting part of this report.");
            sb.AppendLine();
            sb.AppendLine("| Register | Address | Value | Decimal |");
            sb.AppendLine("|---|---|---|---|");

            foreach(EmbeddedControllerData.Register register
                in Enum.GetValues(typeof(EmbeddedControllerData.Register))) {

                byte address = (byte) register;
                byte value;

                bool answered = Hw.EcTryGetByte(address, out value);

                sb.AppendLine("| " + register
                    + " | 0x" + Conv.GetString(address, 2, 16)
                    + " | " + (answered ? "0x" + Conv.GetString(value, 2, 16) : "no answer")
                    + " | " + (answered ? value.ToString(CultureInfo.InvariantCulture) : "-")
                    + " |");

            }

            sb.AppendLine();

        }

        // The firmware's own fan curve, as it answered.
        //
        // The profile above reports the ceiling this was reduced to and, now,
        // which branch it took to get there. That was not enough: a report
        // reading "the fan table topped out at 24, which is not a fan level"
        // says the answer was rejected without showing the answer, and whether
        // rejecting it was right is the whole question. This is the evidence.
        private static void AppendFanTable(StringBuilder sb) {

            sb.AppendLine("## The firmware's fan table");
            sb.AppendLine();

            BiosData.FanTable table;

            try {
                table = Hw.BiosGetStruct<BiosData.FanTable>(Hw.Bios.GetFanTable);
            } catch(Exception e) {
                sb.AppendLine("The firmware declined to describe its fan table: "
                    + e.Message);
                sb.AppendLine();
                return;
            }

            if(table.Level == null || table.Level.Length == 0) {
                sb.AppendLine("The firmware returned no rows.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("Reported for " + table.FanCount + " fan(s), "
                + table.Level.Length + " row(s). Levels are the units the fan "
                + "setpoint registers take; on the boards seen so far a level "
                + "is roughly a hundredth of the resulting speed in rpm.");
            sb.AppendLine();
            sb.AppendLine("| Row | Temperature | Fan 1 level | Fan 2 level |");
            sb.AppendLine("|---|---|---|---|");

            int top = 0;

            for(int i = 0; i < table.Level.Length; i++) {

                BiosData.FanLevel row = table.Level[i];

                if(row.Fan1Level > top) top = row.Fan1Level;
                if(row.Fan2Level > top) top = row.Fan2Level;

                sb.AppendLine("| " + i
                    + " | " + row.Temperature + " C"
                    + " | " + row.Fan1Level
                    + " | " + row.Fan2Level + " |");

            }

            sb.AppendLine();
            sb.AppendLine("Highest level in the table: **" + top + "**. "
                + "The ceiling in use is " + DeviceProfile.FanLevelCeiling
                + " — see the device profile above for which of the two won "
                + "and why.");
            sb.AppendLine();

        }

        // What the fan array was built as, and what it reads
        private static void AppendFans(StringBuilder sb, Platform platform) {

            sb.AppendLine("## Fans");
            sb.AppendLine();

            if(platform == null || platform.Fans == null || platform.Fans.Fan == null) {
                sb.AppendLine("The fan array could not be built on this machine.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("Built as " + platform.Fans.Fan.Length
                + " fan(s), from the firmware's own count.");
            sb.AppendLine();
            sb.AppendLine("| Index | Type | Level | Rate % | Speed rpm |");
            sb.AppendLine("|---|---|---|---|---|");

            for(int i = 0; i < platform.Fans.Fan.Length; i++) {

                IFan fan = platform.Fans.Fan[i];

                sb.AppendLine("| " + i
                    + " | " + Ask(() => fan.GetFanType().ToString())
                    + " | " + Ask(() => fan.GetLevel().ToString(CultureInfo.InvariantCulture))
                    + " | " + Ask(() => fan.GetRate().ToString(CultureInfo.InvariantCulture))
                    + " | " + Ask(() => fan.GetSpeed().ToString(CultureInfo.InvariantCulture))
                    + " |");

            }

            sb.AppendLine();

        }

        // A reading, or why there is not one. A report that stops at the first
        // refusal describes less than a report that records it.
        private static string Ask(Func<string> read) {
            try {
                return read();
            } catch(Exception e) {
                return "(" + e.GetType().Name + ")";
            }
        }

    }

}
