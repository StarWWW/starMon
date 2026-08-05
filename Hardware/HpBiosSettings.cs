// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Hardware {

    // Reads the BIOS setup menu, as HP publishes it.
    //
    // `root\HP\InstrumentedBIOS` carries the whole of setup as WMI objects:
    // every enumerated choice, every string, every integer, with its current
    // value. Reading them needs no password — only writing does, which this
    // class does not do.
    //
    // What it is for here is the questions the gaming interface answers badly
    // or not at all. The keyboard is the clearest case: `GetKbdType()` returns
    // a four-value enumeration that says nothing about whether the deck is ISO
    // or ANSI, while setup holds "Keyboard Type = US5 (Europe KB)" and
    // "Keyboard Layout = Full" — the firmware describing the physical board it
    // was built with. That beats inferring it from the typing layout, which is
    // a preference and not a shape.
    //
    // Every HP notebook publishes this, gaming line or not.
    public static class HpBiosSettings {

        private const string Namespace = "root\\HP\\InstrumentedBIOS";

        // The classes worth reading. Enumerations hold the choices, strings
        // the identity and the free-text values. Integers and ordered lists
        // are setup's own housekeeping and nothing here needs them.
        private static readonly string[] Classes = {
            "HPBIOS_BIOSEnumeration",
            "HPBIOS_BIOSString"
        };

        private static readonly object Lock = new object();
        private static Dictionary<string, string> Values;

        // Whether setup could be read at all
        public static bool IsAvailable {
            get { return All().Count > 0; }
        }

        // Every setting, by name. Read once: setup does not change while the
        // machine is running, short of a reboot into it.
        public static Dictionary<string, string> All() {

            lock(Lock) {

                if(Values != null)
                    return Values;

                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                try {
                    using(WmiInfo wmi = new WmiInfo()) {

                        if(!wmi.IsInitialized)
                            return Values;

                        foreach(string className in Classes)
                            foreach(Dictionary<string, object> row
                                in wmi.EnumerateValues(className, Namespace)) {

                                object name, value;
                                if(!row.TryGetValue("Name", out name) || name == null)
                                    continue;

                                string key = name.ToString().Trim();

                                // Setup publishes a good many nameless
                                // placeholder rows — 60-odd of the 93 on the
                                // machine this was written against
                                if(key.Length == 0)
                                    continue;

                                if(!row.TryGetValue("CurrentValue", out value) || value == null)
                                    row.TryGetValue("Value", out value);

                                Values[key] = value == null ? "" : value.ToString().Trim();

                            }

                    }
                } catch { }

                return Values;

            }

        }

        // One setting's value, or an empty string
        public static string Get(string name) {
            string value;
            return All().TryGetValue(name, out value) ? value : "";
        }

        // Whether a setting's value contains a word, case-insensitively
        private static bool Says(string name, string word) {
            string value = Get(name);
            return value.Length > 0
                && value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Whether the deck carries a numeric pad, as the firmware describes
        // the board it was built with. Null when setup does not say, which is
        // the caller's cue to fall back to the keyboard-type enumeration.
        public static bool? HasNumericPad {
            get {
                // Observed: "Full" on a 15-inch Victus. HP's other values for
                // this are the ten-key-less ones.
                if(Says("Keyboard Layout", "Full"))
                    return true;
                if(Says("Keyboard Layout", "TKL")
                    || Says("Keyboard Layout", "Ten Key")
                    || Says("Keyboard Layout", "TenKey"))
                    return false;
                return null;
            }
        }

        // Whether the deck is an ISO body — tall Enter, narrow left Shift with
        // the extra key beside it — rather than ANSI. Null when setup does not
        // say. Observed: "US5 (Europe KB)" on a European machine.
        public static bool? IsIsoKeyboard {
            get { return ClassifyBody(Get("Keyboard Type")); }
        }

        // Whether a keyboard-type string describes an ISO body.
        //
        // Separated out because it is a string classification and nothing
        // else, and because getting it wrong draws the machine's own keyboard
        // with the wrong Enter key.
        //
        // The country code used to be looked for as a bare substring, so any
        // value containing the letters "us" anywhere was read as a US layout
        // and therefore ANSI: "Russia", "Belarus", "Austria", "Prussia". The
        // Europe and International tests run first and mask most of that, but
        // a value naming a country without also naming its region does not
        // reach them. It is matched as a whole token now — HP's codes look
        // like "US", "US5 (Europe KB)", "UK", "Japan" — so letters inside a
        // longer word do not count.
        internal static bool? ClassifyBody(string type) {

            if(string.IsNullOrEmpty(type))
                return null;

            if(type.IndexOf("Europe", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("International", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("ISO", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // A plain US or Japanese code is an ANSI body
            if(HasToken(type, "US") || HasToken(type, "Japan"))
                return false;

            return null;

        }

        // Whether a word appears in a string as its own token rather than as
        // letters inside a longer one. HP's codes carry a trailing digit
        // often enough ("US5") that it is treated as part of the token.
        private static bool HasToken(string text, string word) {

            int at = 0;

            while((at = text.IndexOf(word, at, StringComparison.OrdinalIgnoreCase)) >= 0) {

                bool leftClear = at == 0 || !char.IsLetter(text[at - 1]);

                int after = at + word.Length;

                // Skip a numeric suffix, so "US5" is the token "US"
                while(after < text.Length && char.IsDigit(text[after]))
                    after++;

                bool rightClear = after >= text.Length || !char.IsLetter(text[after]);

                if(leftClear && rightClear)
                    return true;

                at += word.Length;

            }

            return false;

        }

        // Whether setup has the fans permanently spinning. Worth knowing: it
        // is why a machine's fans never reach zero however the application is
        // set, and someone chasing that would otherwise chase it here.
        public static bool? FanAlwaysOn {
            get {
                if(Says("Fan Always On", "Enable")) return true;
                if(Says("Fan Always On", "Disable")) return false;
                return null;
            }
        }

        // The machine's own names for itself, which are cleaner than the
        // baseboard strings: "HP Victus" and "8DCF" rather than a product
        // code that has to be pattern-matched
        public static string SystemFamily { get { return Get("System Family"); } }
        public static string SystemBoardId { get { return Get("System Board ID"); } }
        public static string ProductName { get { return Get("Product Name"); } }

        // Resets the cache, for the self-tests
        internal static void Reset() {
            lock(Lock) { Values = null; }
        }

    }

}
