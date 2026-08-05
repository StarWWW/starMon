// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Hardware {

    // Whether this is a machine this application should be driving at all.
    //
    // Everything else here is careful about *how* it writes to the hardware.
    // This is about whether to write to it in the first place, and it exists
    // because of a specific report: somebody installed this application's
    // upstream onto an Omen desktop by mistake, and the fan curve was left
    // permanently wrong — through a BIOS reset and a Windows reinstall.
    //
    // The registers this application writes to are one laptop family's. On a
    // desktop board the same addresses belong to something else, and the
    // firmware accepts the write. There is no error to report and no way to
    // notice afterwards, which is exactly what makes it worth refusing before
    // the first write rather than coping with after it.
    //
    // The gate refuses on positive evidence and allows on the absence of it. A
    // machine whose WMI will not answer is not thereby a desktop, and refusing
    // to start because a query failed would be a worse failure than the one
    // being prevented.
    public static class Identity {

        public enum Verdict {

            // An HP portable: drive it
            Supported,

            // Positively identified as something else: do not write to it
            Unsupported,

            // Could not be established. Allowed, and said out loud.
            Unknown

        }

        private static readonly object Lock = new object();
        private static bool Examined;
        private static Verdict Found;
        private static string Reason;

        // SMBIOS chassis types that are portable machines. From the System
        // Enclosure table: this application is for laptops, and every one of
        // these is one.
        private static readonly HashSet<int> Portable = new HashSet<int> {
            8,   // Portable
            9,   // Laptop
            10,  // Notebook
            11,  // Hand Held
            12,  // Docking Station
            14,  // Sub Notebook
            30,  // Tablet
            31,  // Convertible
            32   // Detachable
        };

        // Chassis types that are definitely not. Listed rather than inferred
        // as "everything not portable", because the table has entries that
        // mean neither — and an unrecognised number should land in Unknown and
        // be allowed, not be refused by a rule nobody checked.
        private static readonly HashSet<int> Stationary = new HashSet<int> {
            3,   // Desktop
            4,   // Low Profile Desktop
            5,   // Pizza Box
            6,   // Mini Tower
            7,   // Tower
            13,  // All in One
            15,  // Space-saving
            16,  // Lunch Box
            17,  // Main System Chassis
            23,  // Rack Mount Chassis
            24,  // Sealed-case PC
            35,  // Mini PC
            36   // Stick PC
        };

        // Boards known to be the wrong machine, by baseboard product.
        //
        // The chassis check catches these already on a machine whose firmware
        // fills the table in honestly. This is for the ones that do not, and
        // it is how a board found to be harmful gets excluded without waiting
        // for a release: every entry should name where it came from.
        private static readonly Dictionary<string, string> Denied =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {

                // OmenMon#73: "I installed OmenMon on a desktop by mistake.
                // Now my fan are incontrollable [...] I reseted bios and
                // windows and the fan curve is still messed up"
                ["89EB"] = "an Omen desktop board, reported as permanently "
                    + "damaged by fan writes intended for a laptop"

            };

        // The verdict, established once
        public static Verdict Examine() {

            lock(Lock) {

                if(Examined)
                    return Found;

                Examined = true;
                Found = Verdict.Unknown;
                Reason = "";

                string manufacturer = null;
                string board = null;
                List<int> chassis = new List<int>();

                try {
                    using(WmiInfo wmi = new WmiInfo()) {

                        foreach(Dictionary<string, string> row
                            in wmi.EnumerateInstances("Win32_BaseBoard")) {

                            string value;
                            if(row.TryGetValue("Manufacturer", out value))
                                manufacturer = value;
                            if(row.TryGetValue("Product", out value))
                                board = value;

                        }

                    }
                } catch { }

                chassis.AddRange(ReadChassisTypes());

                Found = Decide(manufacturer, board, chassis, out Reason);

                Logger.Info("Identity", "Hardware gate",
                    Found + (Reason.Length > 0 ? ": " + Reason : ""));

                return Found;

            }

        }

        // The chassis types this machine declares.
        //
        // Read through CimSession rather than through WmiInfo, because the
        // property is an array of uint16 and WmiInfo renders every value with
        // ToString() — so a real machine's chassis arrived as the text
        // "System.UInt16[]", parsed as nothing, and the check silently never
        // ran on any machine. It failed open, which is the right direction to
        // fail in, but a gate that always abstains is not a gate.
        private static List<int> ReadChassisTypes() {

            List<int> types = new List<int>(1);

            try {

                using(Microsoft.Management.Infrastructure.CimSession session
                    = Microsoft.Management.Infrastructure.CimSession.Create(null))

                    foreach(Microsoft.Management.Infrastructure.CimInstance instance
                        in session.EnumerateInstances(
                            "root\\cimv2", "Win32_SystemEnclosure"))

                        using(instance) {

                            Microsoft.Management.Infrastructure.CimProperty property =
                                instance.CimInstanceProperties["ChassisTypes"];

                            Array values = property == null ? null : property.Value as Array;
                            if(values == null)
                                continue;

                            foreach(object value in values)
                                try {
                                    types.Add(Convert.ToInt32(value,
                                        System.Globalization.CultureInfo.InvariantCulture));
                                } catch { }

                        }

            } catch { }

            return types;

        }

        // The decision itself, taken apart from the reading of it so it can be
        // exercised against machines nobody here owns.
        internal static Verdict Decide(string manufacturer, string board,
            IEnumerable<int> chassisTypes, out string reason) {

            reason = "";

            // A named board known to be harmful loses before anything else is
            // considered, including a chassis table that says it is portable
            if(!string.IsNullOrEmpty(board)) {

                string why;
                if(Denied.TryGetValue(board.Trim(), out why)) {
                    reason = "board " + board.Trim() + " is " + why;
                    return Verdict.Unsupported;
                }

            }

            bool sawChassis = false;

            if(chassisTypes != null)
                foreach(int type in chassisTypes) {

                    sawChassis = true;

                    if(Stationary.Contains(type)) {
                        reason = "the chassis reports itself as type " + type
                            + ", which is not a portable machine";
                        return Verdict.Unsupported;
                    }

                }

            // Not HP is positive evidence: this application talks to HP's own
            // firmware interface and writes to registers laid out by HP's
            // firmware. On anything else those addresses belong to something
            // this application knows nothing about.
            if(!string.IsNullOrEmpty(manufacturer)
                && !IsHp(manufacturer)) {

                reason = "the baseboard manufacturer is \"" + manufacturer.Trim()
                    + "\" rather than HP";
                return Verdict.Unsupported;
            }

            if(string.IsNullOrEmpty(manufacturer)) {
                reason = "the baseboard manufacturer could not be read";
                return Verdict.Unknown;
            }

            if(!sawChassis) {
                reason = "the chassis type could not be read";
                return Verdict.Unknown;
            }

            bool portable = false;
            foreach(int type in chassisTypes)
                if(Portable.Contains(type))
                    portable = true;

            if(!portable) {
                reason = "the chassis type is not one this application recognises";
                return Verdict.Unknown;
            }

            return Verdict.Supported;

        }

        private static bool IsHp(string manufacturer) {

            string name = manufacturer.Trim();

            return name.IndexOf("HP", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hewlett", StringComparison.OrdinalIgnoreCase) >= 0;

        }

        // Why the machine was refused, for the message shown before exiting
        public static string Explain() {

            Examine();

            return "StarMon is for HP Omen and Victus laptops, and this machine "
                + "does not appear to be one — " + Reason + ".\n\n"
                + "It has not written anything to the hardware and will not. The "
                + "registers it drives belong to one laptop family; on another "
                + "machine the same addresses are something else, and the "
                + "firmware accepts the write without reporting an error.\n\n"
                + "If this is wrong, setting HardwareGateOverride to true in "
                + "StarMon.xml starts it anyway. Read the note beside that "
                + "setting first.";

        }

        public static string Summary() {
            Examine();
            return Found + (Reason.Length > 0 ? " (" + Reason + ")" : "");
        }

        // Whether the application should run here, honouring the override
        public static bool MayRun() {

            if(Examine() != Verdict.Unsupported)
                return true;

            if(Config.HardwareGateOverride) {
                Logger.Warning("Identity", "Hardware gate overridden",
                    "the user has asked for this machine to be driven anyway");
                return true;
            }

            return false;

        }

        internal static void Reset() {
            lock(Lock) {
                Examined = false;
                Found = Verdict.Unknown;
                Reason = "";
            }
        }

    }

}
