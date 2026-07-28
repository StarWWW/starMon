// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Hardware {

    // Reads the sensors HP's firmware publishes through WMI.
    //
    // This is a different interface from the one the rest of this application
    // uses. The gaming line (Omen, Victus) answers `hpqBIntM` in root\wmi,
    // which is where the fan levels, the performance modes and the keyboard
    // colour table live. Every other HP notebook — Pavilion, Envy, Spectre,
    // ProBook, EliteBook, ZBook — publishes `root\HP\InstrumentedBIOS`
    // instead, and so, as it happens, do the gaming machines.
    //
    // What it gives is honest and worth having on both: named temperature
    // probes and real tachometer readings in rpm, each with the firmware's own
    // opinion of whether the part behind it is healthy. On a machine with no
    // Embedded Controller register map to read, it is the only sensor source
    // there is; on one with a map, it is a second opinion and a source of fan
    // speeds that are measured rather than inferred.
    //
    // The schema is HP's Client Management Interface, the same one the Linux
    // hp-wmi-sensors driver reads, so the constants below are not guesses.
    public static class HpSensors {

        // Where the classes live and what they are called
        private const string Namespace = "root\\HP\\InstrumentedBIOS";
        private const string ClassNumeric = "HPBIOS_BIOSNumericSensor";
        private const string ClassState = "HPBIOS_BIOSStateSensor";

        // SensorType, from the CIM sensor model HP follows
        private const int TypeTemperature = 2;
        private const int TypeVoltage = 3;
        private const int TypeCurrent = 4;
        private const int TypeAirFlow = 12;  // what a fan is filed under

        // BaseUnits, likewise
        private const int UnitsDegreesC = 2;
        private const int UnitsRpm = 19;

        // OperationalStatus: 2 is the only value that means "fine"
        private const int StatusOk = 2;

        // What a sensor measures, as far as this application cares
        public enum Kind { Other, Temperature, Fan, Voltage, Current }

        // One sensor as the firmware describes it
        public sealed class Sensor {
            public string Name;         // e.g. "CPU Thermal", "CPU Fan Speed"
            public string Description;  // the firmware's own longer text
            public Kind Type;
            public int Reading;         // °C for a temperature, rpm for a fan
            public bool Healthy;        // OperationalStatus said OK
        }

        // Guards the cache below, which the poller and the interface share
        private static readonly object Lock = new object();

        // The last successful read, and when it was taken
        private static Sensor[] Cache = new Sensor[0];
        private static int CacheStamp;
        private static bool Queried;
        private static bool Present;

        // How many times in a row the enumeration has come back empty, and
        // the point at which this machine is taken to publish nothing.
        //
        // The classes exist on every HP notebook; whether anything is
        // registered against them does not. The gaming line, as it turns out,
        // declares them and populates neither — so on those machines an
        // unguarded cache would re-enumerate a WMI class every few seconds,
        // for ever, to be told nothing again. Three tries is enough to tell an
        // empty machine from a momentarily busy WMI service.
        private static int EmptyRuns;
        private static bool GivenUp;
        private const int GiveUpAfter = 3;

        // How long a reading stands before another enumeration is worth it.
        //
        // Enumerating a WMI class is not a register read: it crosses into the
        // WMI service, which builds an object per instance. It costs tens of
        // milliseconds, which is nothing once a few seconds and a great deal
        // once a second. The poller only asks on its slow tick anyway; this is
        // the belt to that pair of braces.
        private const int CacheMs = 4000;

        // Whether this machine publishes the interface at all. Answered by the
        // first read, and remembered — a machine does not grow the namespace
        // while the application is running.
        public static bool IsAvailable {
            get {
                if(!Queried)
                    Read();
                return Present;
            }
        }

        // The sensors, re-read when the cached set has gone stale
        public static Sensor[] Read(bool force = false) {

            lock(Lock) {

                if(GivenUp && !force)
                    return Cache;

                int now = Environment.TickCount;

                if(!force && Queried
                    && unchecked(now - CacheStamp) < CacheMs)
                    return Cache;

                Sensor[] fresh = Enumerate();

                // A machine that answered once and then failed is a machine
                // whose WMI service is momentarily busy, not one that has lost
                // its sensors. Keep what was there rather than reporting the
                // parts have vanished.
                if(fresh.Length > 0 || !Present)
                    Cache = fresh;

                if(fresh.Length > 0) {
                    Present = true;
                    EmptyRuns = 0;
                } else if(!Present && ++EmptyRuns >= GiveUpAfter) {
                    GivenUp = true;
                    Logger.Info("HpSensors", "This machine publishes none",
                        "no further attempts will be made");
                }

                Queried = true;
                CacheStamp = now;

                return Cache;

            }

        }

        // The hottest temperature the firmware reports, or 0 if it reports none
        public static int GetMaxTemperature() {

            int max = 0;
            foreach(Sensor sensor in Read())
                if(sensor.Type == Kind.Temperature
                    && sensor.Reading > max
                    && sensor.Reading < 130)
                    max = sensor.Reading;

            return max;

        }

        // Measured fan speeds in rpm, in the order the firmware lists them
        public static int[] GetFanRpm() {

            List<int> speeds = new List<int>(2);
            foreach(Sensor sensor in Read())
                if(sensor.Type == Kind.Fan && sensor.Reading >= 0)
                    speeds.Add(sensor.Reading);

            return speeds.ToArray();

        }

        // Anything the firmware has flagged as not healthy, by name
        public static List<string> GetFaults() {

            List<string> faults = new List<string>();
            foreach(Sensor sensor in Read())
                if(!sensor.Healthy)
                    faults.Add(sensor.Name);

            return faults;

        }

        // Performs the actual enumeration
        private static Sensor[] Enumerate() {

            List<Sensor> sensors = new List<Sensor>(8);

            try {
                using(WmiInfo wmi = new WmiInfo()) {

                    if(!wmi.IsInitialized)
                        return sensors.ToArray();

                    foreach(Dictionary<string, object> row
                        in wmi.EnumerateValues(ClassNumeric, Namespace)) {

                        Sensor sensor = Parse(row);
                        if(sensor != null)
                            sensors.Add(sensor);

                    }

                }
            } catch { }

            return sensors.ToArray();

        }

        // Turns one WMI row into a sensor, or null if it is not one this
        // application can make sense of
        private static Sensor Parse(Dictionary<string, object> row) {

            int type = Int(row, "SensorType", -1);
            int units = Int(row, "BaseUnits", -1);
            int reading = Int(row, "CurrentReading", int.MinValue);

            if(reading == int.MinValue)
                return null;

            Kind kind =
                type == TypeTemperature ? Kind.Temperature
                : type == TypeAirFlow ? Kind.Fan
                : type == TypeVoltage ? Kind.Voltage
                : type == TypeCurrent ? Kind.Current
                : Kind.Other;

            // A sensor whose units contradict its type is one this application
            // has misread, and a misread temperature is worse than none: the
            // thermal guard acts on the hottest reading it is given.
            if(kind == Kind.Temperature && units != UnitsDegreesC)
                return null;
            if(kind == Kind.Fan && units != UnitsRpm)
                return null;
            if(kind == Kind.Other || kind == Kind.Voltage || kind == Kind.Current)
                return null;

            return new Sensor {
                Name = Text(row, "Name"),
                Description = Text(row, "Description"),
                Type = kind,
                Reading = Scale(reading, Int(row, "UnitModifier", 0)),
                Healthy = IsHealthy(row)
            };

        }

        // Applies the firmware's power-of-ten exponent, so a reading given as
        // 4250 with a modifier of -2 comes back as the 42 °C it means
        private static int Scale(int reading, int modifier) {

            // Guard the exponent as well as apply it: a firmware that reports
            // something absurd must not turn into a multiply that overflows
            if(modifier > 6) modifier = 6;
            if(modifier < -6) modifier = -6;

            while(modifier > 0) { reading *= 10; modifier--; }

            while(modifier < 0) {
                // Rounded rather than truncated, so 4250 with -2 is 43 and not
                // 42 — the same rounding the reference driver does
                reading = (reading + (reading < 0 ? -5 : 5)) / 10;
                modifier++;
            }

            return reading;

        }

        // OperationalStatus is an array, and a sensor is healthy only when it
        // says OK and nothing else
        private static bool IsHealthy(Dictionary<string, object> row) {

            object value;
            if(!row.TryGetValue("OperationalStatus", out value) || value == null)
                return true;  // said nothing, so claims nothing is wrong

            Array statuses = value as Array;
            if(statuses == null)
                return ToInt(value, StatusOk) == StatusOk;

            if(statuses.Length == 0)
                return true;

            foreach(object status in statuses)
                if(ToInt(status, StatusOk) != StatusOk)
                    return false;

            return true;

        }

#region Reading values out of a WMI row
        private static string Text(Dictionary<string, object> row, string key) {
            object value;
            return row.TryGetValue(key, out value) && value != null
                ? value.ToString().Trim() : "";
        }

        private static int Int(Dictionary<string, object> row, string key, int fallback) {
            object value;
            return row.TryGetValue(key, out value) && value != null
                ? ToInt(value, fallback) : fallback;
        }

        // WMI hands these back as whichever integer width the MOF declared,
        // and boxed, so the only safe route is through IConvertible
        private static int ToInt(object value, int fallback) {
            try {
                return Convert.ToInt32(value,
                    System.Globalization.CultureInfo.InvariantCulture);
            } catch {
                return fallback;
            }
        }
#endregion

        // Resets the cache, for the self-tests
        internal static void Reset() {
            lock(Lock) {
                Cache = new Sensor[0];
                Queried = false;
                Present = false;
                CacheStamp = 0;
                EmptyRuns = 0;
                GivenUp = false;
            }
        }

    }

}
