// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml;
using StarMon.External;
using StarMon.Library;
using StarMon.Hardware.Platform;

namespace StarMon.Hardware {

    // Provides a consolidated battery status reading
    public static class Battery {

        // The last percentage that could actually be true, kept so an
        // implausible reading can be recognised and held back.
        //
        // This is a guard against a false zero, which on this hardware was not
        // a cosmetic bug: a momentarily desynced Embedded Controller could
        // poison the ACPI fuel gauge Windows reads, and Windows acts on a
        // "critical" charge by shutting the machine down — on a battery that
        // was in fact full. The desync itself is fixed at the EC layer; this
        // is a second line so a single bad sample never even reaches the eye.
        private static int LastGoodPercent = -1;
        private static int RejectedRunning;

        // The charge/discharge rate is a WMI query, which is not free; it
        // moves slowly enough that refreshing it every few seconds is
        // indistinguishable from every tick and costs a fraction as much.
        private static double CachedRateWatts = double.NaN;
        private static int RateCallsSinceQuery = int.MaxValue;
        private const int RateEvery = 5;

        // Past this many rejected samples in a row, the low reading is
        // believed: a battery genuinely can drain, and refusing to ever
        // accept a drop would be its own kind of lie.
        private const int RejectRunLimit = 8;

        // Snapshot of the current battery state
        public struct Info {
            public bool Present;        // Whether a battery is installed
            public bool OnAc;           // Whether AC power is connected
            public bool Charging;       // Whether the battery is charging
            public int Percent;         // Charge level 0-100, or -1 if unknown
            public int MinutesLeft;     // Estimated minutes remaining, or -1
            public double RateWatts;    // +charging / -discharging watts, NaN if unknown
            public int HealthPercent;   // Full vs designed capacity %, or -1
            public int CycleCount;      // Charge cycles, or -1 if unknown
            public int DesignmWh;       // Designed capacity [mWh], or 0 if unknown
            public int FullmWh;         // Full-charge capacity [mWh], or 0 if unknown
        }

        // These values do not change during a session, so query them just once
        // (-2 = not yet queried, -1 = unavailable, otherwise the value);
        // a failed query is retried a few times before being given up on,
        // so a transient WMI hiccup at startup does not lose them for good
        private const int QueryAttemptsMax = 3;
        private static int CachedHealth = -2;
        private static int CachedCycle = -2;
        private static int CachedDesignmWh; // 0 until determined
        private static int CachedFullmWh;   // 0 until determined
        private static int HealthAttempts;
        private static int CycleAttempts;

        // Returns the current battery state
        public static Info Get() {

            Info info = new Info {
                Percent = -1,
                MinutesLeft = -1,
                HealthPercent = -1,
                CycleCount = -1,
                RateWatts = double.NaN
            };

            // Basic state from the power-status API
            try {
                if(Kernel32.GetSystemPowerStatus(out Kernel32.SYSTEM_POWER_STATUS sps)) {
                    info.OnAc = sps.ACLineStatus == 1;
                    info.Charging = (sps.BatteryFlag & 8) != 0;
                    info.Present = (sps.BatteryFlag & 128) == 0 && sps.BatteryLifePercent != 255;
                    if(sps.BatteryLifePercent <= 100)
                        info.Percent = sps.BatteryLifePercent;
                    if(sps.BatteryLifeTime >= 0)
                        info.MinutesLeft = sps.BatteryLifeTime / 60;
                }
            } catch { }

            info.Percent = Sanitise(info.Percent, info.OnAc, info.Charging);

            // Health and capacities, queried once and cached
            if(CachedHealth == -2) {
                int health = QueryHealth();
                if(health > 0 || ++HealthAttempts >= QueryAttemptsMax)
                    CachedHealth = health;
                info.HealthPercent = health;
            } else
                info.HealthPercent = CachedHealth;
            info.DesignmWh = CachedDesignmWh;
            info.FullmWh = CachedFullmWh;

            // Charge cycle count, queried once and cached
            if(CachedCycle == -2) {
                int cycle = QueryCycleCount();
                if(cycle > 0 || ++CycleAttempts >= QueryAttemptsMax)
                    CachedCycle = cycle;
                info.CycleCount = cycle;
            } else
                info.CycleCount = CachedCycle;

            // Instantaneous charge / discharge power, refreshed every few
            // calls rather than every one
            if(RateCallsSinceQuery >= RateEvery) {
                CachedRateWatts = QueryRateWatts();
                RateCallsSinceQuery = 0;
            } else {
                RateCallsSinceQuery++;
            }
            info.RateWatts = CachedRateWatts;

            return info;
        }

        // Rejects a physically impossible charge reading, holding the last
        // good one in its place.
        //
        // A battery cannot fall from a healthy charge to nothing in a single
        // second, and it certainly cannot read empty while it is plugged in
        // and charging. When a sample says one of those things it is not the
        // battery talking — it is a poisoned fuel gauge — so the last
        // believable value stands until enough samples agree to overrule it.
        // Left visible in the code as its own method because the failure it
        // guards against is severe (an unplanned shutdown) and rare enough to
        // be forgotten.
        public static int Sanitise(int percent, bool onAc, bool charging) {

            if(percent < 0)
                return LastGoodPercent >= 0 ? LastGoodPercent : percent;

            // Empty while plugged in, or a cliff-drop of more than half the
            // charge in one tick, is not something a real cell does
            bool impossible =
                (percent <= 2 && (onAc || charging) && LastGoodPercent > 20)
                || (LastGoodPercent >= 0 && LastGoodPercent - percent > 50);

            if(impossible && RejectedRunning < RejectRunLimit) {
                RejectedRunning++;
                Logger.Warning("Battery",
                    "Ignoring an impossible charge reading of " + percent
                    + " % (holding " + LastGoodPercent + " %)");
                return LastGoodPercent;
            }

            RejectedRunning = 0;
            LastGoodPercent = percent;
            return percent;

        }

        // Reads the battery charge cycle count (root\wmi BatteryCycleCount)
        private static int QueryCycleCount() {
            try {
                using(WmiInfo wmi = new WmiInfo()) {
                    foreach(Dictionary<string, string> p in wmi.EnumerateInstances("BatteryCycleCount", "root\\wmi")) {
                        double c = ParseDouble(p, "CycleCount");
                        if(c > 0)
                            return (int)Math.Round(c);
                    }
                }
            } catch { }
            return -1;
        }

        // Reads battery health as full-charged divided by designed capacity
        private static int QueryHealth() {

            double design = 0, full = 0;

            try {
                using(WmiInfo wmi = new WmiInfo()) {
                    design = FirstValue(wmi, "BatteryStaticData", "DesignedCapacity");
                    full = FirstValue(wmi, "BatteryFullChargedCapacity", "FullChargedCapacity");
                }
            } catch { }

            // The design capacity is the half that goes missing.
            //
            // On this Victus — and it is not unusual — the ACPI driver refuses
            // BatteryStaticData outright ("Generic failure") and
            // Win32_Battery.DesignCapacity is left empty by the firmware, so
            // there is no WMI source for it at all and the health figure could
            // never be worked out. The full-charge capacity and the cycle
            // count both come back fine, which is what makes the gap look like
            // a bug in the panel rather than an absent reading.
            //
            // Windows itself knows the number: powercfg reads it from the
            // ACPI battery information object and will report it without
            // elevation. Asking costs a process, so it is asked once.
            if(design <= 0)
                design = DesignFromReport();

            if(design > 0) CachedDesignmWh = (int) Math.Round(design);
            if(full > 0) CachedFullmWh = (int) Math.Round(full);

            if(design > 0 && full > 0) {
                int health = (int) Math.Round(full / design * 100);
                if(health > 100) health = 100;
                return health > 0 ? health : -1;
            }

            return -1;

        }

        // Whether the battery report has already been asked for. Once per
        // session whatever the answer: a machine that does not give the design
        // capacity will not start giving it, and spawning a process on every
        // retry to be told so again is not a bargain.
        private static bool ReportAsked;
        private static double ReportDesignmWh;

        // The design capacity, out of Windows' own battery report.
        //
        // The XML form rather than the HTML one: the same numbers without a
        // page of markup around them. The report is namespaced, so the element
        // is found by name rather than by path.
        private static double DesignFromReport() {

            if(ReportAsked)
                return ReportDesignmWh;

            ReportAsked = true;

            string path = null;

            try {

                path = Path.Combine(Path.GetTempPath(),
                    "StarMon-battery-" + Process.GetCurrentProcess().Id + ".xml");

                ProcessStartInfo start = new ProcessStartInfo("powercfg",
                    "/batteryreport /output \"" + path + "\" /xml") {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using(Process report = Process.Start(start)) {

                    // Bounded: this runs on the poller's thread, and a hung
                    // powercfg must not take the readings down with it
                    if(report == null || !report.WaitForExit(ReportTimeoutMs)) {
                        Logger.Debug("Battery", "The battery report timed out");
                        return 0;
                    }

                }

                if(!File.Exists(path))
                    return 0;

                XmlDocument document = new XmlDocument();
                document.Load(path);

                foreach(XmlNode node in document.GetElementsByTagName("DesignCapacity")) {

                    double capacity;
                    if(double.TryParse(node.InnerText, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out capacity) && capacity > 0) {

                        ReportDesignmWh = capacity;
                        Logger.Info("Battery", "Design capacity from the battery report",
                            capacity + " mWh");
                        return capacity;

                    }

                }

            } catch(Exception e) {

                Logger.Debug("Battery", "The battery report could not be read", e.Message);

            } finally {

                try {
                    if(path != null && File.Exists(path))
                        File.Delete(path);
                } catch { }

            }

            return ReportDesignmWh;

        }

        // Long enough for a slow disk, short enough not to stall a reading
        private const int ReportTimeoutMs = 8000;

        // Reads the instantaneous charge (+) or discharge (-) power, in watts
        private static double QueryRateWatts() {
            try {
                using(WmiInfo wmi = new WmiInfo()) {
                    foreach(Dictionary<string, string> p in wmi.EnumerateInstances("BatteryStatus", "root\\wmi")) {
                        double charge = ParseDouble(p, "ChargeRate");       // mW
                        double discharge = ParseDouble(p, "DischargeRate"); // mW
                        if(charge > 0)
                            return charge / 1000.0;
                        if(discharge > 0)
                            return -discharge / 1000.0;
                        return 0;
                    }
                }
            } catch { }
            return double.NaN;
        }

        // Returns the first positive numeric value of a property across instances
        private static double FirstValue(WmiInfo wmi, string className, string property) {
            foreach(Dictionary<string, string> p in wmi.EnumerateInstances(className, "root\\wmi")) {
                double v = ParseDouble(p, property);
                if(v > 0)
                    return v;
            }
            return 0;
        }

        // Parses a numeric property from an instance's property dictionary
        private static double ParseDouble(Dictionary<string, string> props, string key) {
            if(props.TryGetValue(key, out string s)
                && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                return value;
            return 0;
        }

    }

}
