// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Hardware {

    // Reads the ACPI thermal zones Windows exposes.
    //
    // Every laptop has these. They are the temperatures the operating system's
    // own thermal management acts on — the ones behind Windows deciding to
    // throttle or to spin something up — and they come from the same firmware
    // tables the Embedded Controller registers do, without needing to know
    // which register a particular board put them in.
    //
    // That makes them the one temperature source that needs no per-model
    // knowledge at all, which is exactly what a machine with no recognised
    // register map has to fall back to. On a machine that does have one they
    // are a second opinion, and on this application's own hardware a zone
    // reports a figure no other sensor here was showing.
    //
    // The reading is in tenths of a kelvin, which is an ACPI convention and
    // not a mistake.
    public static class AcpiThermal {

        private const string Namespace = "root\\wmi";
        private const string ClassName = "MSAcpi_ThermalZoneTemperature";

        // One zone as the firmware names it
        public sealed class Zone {
            public string Name;     // e.g. "TZ01", from the ACPI instance path
            public int Celsius;
        }

        private static readonly object Lock = new object();
        private static Zone[] Cache = new Zone[0];
        private static int CacheStamp;
        private static bool Queried;
        private static bool Present;

        // As for the published HP sensors: this is a WMI enumeration, not a
        // register read, so it is held rather than repeated
        private const int CacheMs = 4000;

        // A machine with no zones is not going to grow any
        private static int EmptyRuns;
        private static bool GivenUp;
        private const int GiveUpAfter = 3;

        // Plausible bounds. A zone reporting outside these is reporting
        // something that is not a temperature, and the thermal guard acts on
        // the hottest reading it is given.
        private const int Min = 5;
        private const int Max = 125;

        public static bool IsAvailable {
            get {
                if(!Queried)
                    Read();
                return Present;
            }
        }

        // The zones, re-read when the held set has gone stale
        public static Zone[] Read(bool force = false) {

            lock(Lock) {

                if(GivenUp && !force)
                    return Cache;

                int now = Environment.TickCount;

                if(!force && Queried
                    && unchecked(now - CacheStamp) < CacheMs)
                    return Cache;

                Zone[] fresh = Enumerate();

                if(fresh.Length > 0 || !Present)
                    Cache = fresh;

                if(fresh.Length > 0) {
                    Present = true;
                    EmptyRuns = 0;
                } else if(!Present && ++EmptyRuns >= GiveUpAfter) {
                    GivenUp = true;
                }

                Queried = true;
                CacheStamp = now;

                return Cache;

            }

        }

        // The hottest zone, or 0 when there are none
        public static int GetMaxTemperature() {

            int max = 0;
            foreach(Zone zone in Read())
                if(zone.Celsius > max)
                    max = zone.Celsius;

            return max;

        }

        private static Zone[] Enumerate() {

            List<Zone> zones = new List<Zone>(2);

            try {
                using(WmiInfo wmi = new WmiInfo()) {

                    if(!wmi.IsInitialized)
                        return zones.ToArray();

                    foreach(Dictionary<string, object> row
                        in wmi.EnumerateValues(ClassName, Namespace)) {

                        object raw;
                        if(!row.TryGetValue("CurrentTemperature", out raw) || raw == null)
                            continue;

                        int tenthsKelvin;
                        try {
                            tenthsKelvin = Convert.ToInt32(raw,
                                System.Globalization.CultureInfo.InvariantCulture);
                        } catch {
                            continue;
                        }

                        // Tenths of a kelvin to whole degrees Celsius, rounded
                        int celsius = (int) Math.Round(tenthsKelvin / 10.0 - 273.15);

                        if(celsius < Min || celsius > Max)
                            continue;

                        object instance;
                        row.TryGetValue("InstanceName", out instance);

                        zones.Add(new Zone {
                            Name = ShortName(instance == null ? "" : instance.ToString()),
                            Celsius = celsius
                        });

                    }

                }
            } catch { }

            return zones.ToArray();

        }

        // "ACPI\ThermalZone\TZ01_0" is the whole path to a zone, and none of
        // it but the zone's own name means anything to a reader
        private static string ShortName(string instance) {

            if(string.IsNullOrEmpty(instance))
                return "TZ";

            int slash = instance.LastIndexOf('\\');
            string name = slash >= 0 ? instance.Substring(slash + 1) : instance;

            // The trailing "_0" is the enumerator's index, not part of the name
            int underscore = name.LastIndexOf('_');
            if(underscore > 0)
                name = name.Substring(0, underscore);

            return name.Length > 0 ? name : "TZ";

        }

        // Resets the cache, for the self-tests
        internal static void Reset() {
            lock(Lock) {
                Cache = new Zone[0];
                Queried = false;
                Present = false;
                CacheStamp = 0;
                EmptyRuns = 0;
                GivenUp = false;
            }
        }

    }

}
