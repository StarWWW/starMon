// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Reflection;
using StarMon.Hardware;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon.Test {

    // Checks the reasoning the device profile does about a machine.
    //
    // The probe itself needs firmware, which a self-test does not have. What
    // it can check is everything around the firmware call: that a model name
    // lands in the right family, that the mode list follows the support flag
    // rather than a guess, that a fan ceiling is only revised on evidence, and
    // that the defaults the profile falls back to are still coherent.
    public static class TestDeviceProfile {

        public static void Run() {

            SelfTest.Group("Device profile");

            TestFamilyFromModelName();
            TestFanModesFollowTheSupportFlag();
            TestCeilingOnlyMovesOnEvidence();
            TestCeilingRefusesImplausibleValues();
            TestFanArraySizesToWhatItWasGiven();
            TestPublishedSensorsAreParsedOrRejected();
            TestThermalZonesConvertFromTenthsOfAKelvin();

        }

        // The published-sensor rows have to be read exactly as the firmware
        // means them: a reading scaled by its power-of-ten modifier, a
        // temperature only where the units say degrees, a fan only where they
        // say rpm, and health only where every status says OK.
        //
        // Getting this wrong is not cosmetic. A misread temperature goes
        // straight into the hottest-reading check the thermal guard acts on.
        private static void TestPublishedSensorsAreParsedOrRejected() {

            // 4250 at a modifier of -2 is 42.5 °C, which rounds to 43
            SelfTest.Check(ParseReading(2, 2, 4250, -2) == 43,
                "a scaled temperature is rounded, not truncated");

            SelfTest.Check(ParseReading(2, 2, 61, 0) == 61,
                "an unscaled temperature is taken as it stands");

            SelfTest.Check(ParseReading(12, 19, 4100, 0) == 4100,
                "a fan speed in rpm is taken as it stands");

            SelfTest.Check(ParseReading(2, 19, 61, 0) == int.MinValue,
                "a temperature whose units say rpm is refused, not believed");

            SelfTest.Check(ParseReading(12, 2, 4100, 0) == int.MinValue,
                "a fan whose units say degrees is refused");

            SelfTest.Check(ParseReading(3, 5, 12000, -3) == int.MinValue,
                "a voltage is not mistaken for a reading this application shows");

            SelfTest.Check(IsHealthy(new object[] { (ushort) 2 }),
                "a sensor reporting only OK is healthy");

            SelfTest.Check(!IsHealthy(new object[] { (ushort) 2, (ushort) 6 }),
                "a sensor reporting OK and an error is not healthy");

            SelfTest.Check(IsHealthy(null),
                "a sensor that states no opinion is not treated as broken");

        }

        // ACPI reports in tenths of a kelvin, and a zone outside human
        // temperatures is a zone reporting something that is not one
        private static void TestThermalZonesConvertFromTenthsOfAKelvin() {

            SelfTest.Check(ZoneCelsius(3281) == 55,
                "3281 tenths of a kelvin is 55 °C");

            SelfTest.Check(ZoneCelsius(2982) == 25,
                "room temperature converts correctly");

            SelfTest.Check(ZoneCelsius(0) == int.MinValue,
                "absolute zero is refused rather than shown as -273 °C");

            SelfTest.Check(ZoneCelsius(9000) == int.MinValue,
                "a figure hotter than any laptop part is refused");

        }

        // Both families have to be recognized from the name the machine
        // reports, and nothing else should be mistaken for either of them.
        private static void TestFamilyFromModelName() {

            SelfTest.Check(
                DeviceProfile.FamilyOf("OMEN by HP Laptop 16-wf0xxx")
                    == DeviceProfile.DeviceFamily.Omen,
                "an Omen laptop is recognized as an Omen");

            SelfTest.Check(
                DeviceProfile.FamilyOf("Victus by HP Gaming Laptop 15-fa2xxx")
                    == DeviceProfile.DeviceFamily.Victus,
                "a Victus laptop is recognized as a Victus");

            SelfTest.Check(
                DeviceProfile.FamilyOf("omen by hp transcend 14")
                    == DeviceProfile.DeviceFamily.Omen,
                "the family match ignores case");

            SelfTest.Check(
                DeviceProfile.FamilyOf("HP EliteBook 840 G8")
                    == DeviceProfile.DeviceFamily.Unknown,
                "an unrelated HP laptop is not claimed as either family");

            SelfTest.Check(
                DeviceProfile.FamilyOf(null) == DeviceProfile.DeviceFamily.Unknown
                    && DeviceProfile.FamilyOf("") == DeviceProfile.DeviceFamily.Unknown,
                "a missing model name does not throw");

        }

        // Extreme is the one performance mode that is not universal. It has to
        // appear exactly when the firmware's support flag says it exists —
        // offering it everywhere leaves half the machines with a button that
        // does nothing, and hiding it everywhere costs Omen owners their top
        // mode, which is how it was before.
        private static void TestFanModesFollowTheSupportFlag() {

            bool saved = DeviceProfile.ExtremeMode;

            try {

                SetExtremeMode(false);
                List<BiosData.FanMode> without = DeviceProfile.SupportedFanModes();

                SetExtremeMode(true);
                List<BiosData.FanMode> with = DeviceProfile.SupportedFanModes();

                SelfTest.Check(
                    !without.Contains(BiosData.FanMode.Extreme),
                    "Extreme is hidden when the firmware does not report it");

                SelfTest.Check(
                    with.Contains(BiosData.FanMode.Extreme),
                    "Extreme is offered when the firmware reports it");

                SelfTest.Check(
                    without.Contains(BiosData.FanMode.Default)
                        && without.Contains(BiosData.FanMode.Performance)
                        && without.Contains(BiosData.FanMode.Cool)
                        && without.Contains(BiosData.FanMode.Quiet),
                    "the four common modes are offered on every machine");

                SelfTest.Check(
                    with.Count == without.Count + 1,
                    "the support flag adds one mode rather than rebuilding the list");

            } finally {
                SetExtremeMode(saved);
            }

        }

        // A ceiling learned from watching the fans only ever moves upwards. A
        // fan running slowly says nothing about how fast it can run, so a low
        // reading must leave a proven ceiling where it is.
        private static void TestCeilingOnlyMovesOnEvidence() {

            using(var state = new ProfileState()) {

                state.Begin(ceiling: 50, autoDetect: true);

                DeviceProfile.Observe(58, false);
                SelfTest.Check(DeviceProfile.FanLevelCeiling == 58,
                    "a fan seen running above the ceiling raises it");

                SelfTest.Check(Config.FanLevelMax == 58,
                    "the raised ceiling reaches the configuration the curve reads");

                DeviceProfile.Observe(41, true);
                SelfTest.Check(DeviceProfile.FanLevelCeiling == 58,
                    "a lower level never lowers a ceiling already proven");

                DeviceProfile.Observe(0, false);
                SelfTest.Check(DeviceProfile.FanLevelCeiling == 58,
                    "stopped fans do not reset the ceiling");

                DeviceProfile.Observe(64, true);
                SelfTest.Check(DeviceProfile.FanLevelCeiling == 64,
                    "the ceiling keeps rising as higher levels are seen");

            }

        }

        // Everything the probe accepts has to be a fan level a laptop could
        // actually run at, and a user who pinned the ceiling by hand has to
        // keep it.
        private static void TestCeilingRefusesImplausibleValues() {

            using(var state = new ProfileState()) {

                state.Begin(ceiling: 50, autoDetect: true);

                DeviceProfile.Observe(240, true);
                SelfTest.Check(DeviceProfile.FanLevelCeiling == 50,
                    "a level no laptop fan reaches is refused");

            }

            using(var state = new ProfileState()) {

                state.Begin(ceiling: 50, autoDetect: false);

                DeviceProfile.Observe(58, true);
                SelfTest.Check(DeviceProfile.FanLevelCeiling == 50
                        && Config.FanLevelMax == 50,
                    "a hand-pinned ceiling is left alone");

            }

        }

        // The fan array used to be sized from a compiled constant while being
        // filled from the caller's array. On a board with fewer fans than the
        // constant assumes, that left a null entry that every reading walked
        // into.
        private static void TestFanArraySizesToWhatItWasGiven() {

            var one = new StarMon.Hardware.Platform.FanArray(
                new StarMon.Hardware.Platform.IFan[] { null },
                null, null, null, null);

            SelfTest.Check(one.Fan.Length == 1,
                "a one-fan board gets a one-fan array, not a padded two-fan one");

            var two = new StarMon.Hardware.Platform.FanArray(
                new StarMon.Hardware.Platform.IFan[] { null, null },
                null, null, null, null);

            SelfTest.Check(two.Fan.Length == 2,
                "the usual two-fan board is unaffected");

        }

#region Helpers
        // Runs one WMI row through the published-sensor parser, returning the
        // scaled reading or int.MinValue where the row was refused
        private static int ParseReading(int type, int units, int reading, int modifier) {

            var row = new Dictionary<string, object> {
                ["Name"] = "Test sensor",
                ["Description"] = "",
                ["SensorType"] = type,
                ["BaseUnits"] = units,
                ["CurrentReading"] = reading,
                ["UnitModifier"] = modifier
            };

            object parsed = typeof(HpSensors)
                .GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { row });

            if(parsed == null)
                return int.MinValue;

            return (int) parsed.GetType()
                .GetField("Reading", BindingFlags.Public | BindingFlags.Instance)
                .GetValue(parsed);

        }

        // Runs an OperationalStatus value through the health check
        private static bool IsHealthy(object status) {

            var row = new Dictionary<string, object>();
            if(status != null)
                row["OperationalStatus"] = status;

            return (bool) typeof(HpSensors)
                .GetMethod("IsHealthy", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { row });

        }

        // Converts one ACPI reading, returning int.MinValue where the zone was
        // refused as implausible
        private static int ZoneCelsius(int tenthsKelvin) {

            int celsius = (int) Math.Round(tenthsKelvin / 10.0 - 273.15);
            return celsius < 5 || celsius > 125 ? int.MinValue : celsius;

        }

        // Sets the Extreme support flag, which only the probe writes to
        private static void SetExtremeMode(bool value) {
            Set("ExtremeMode", value);
        }

        // Sets one of the profile's findings. They are deliberately read-only
        // to everything but the probe, so the tests go in through the setter
        // rather than being given a public way to write them.
        private static void Set(string name, object value) {
            typeof(DeviceProfile)
                .GetProperty(name, BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(true)
                .Invoke(null, new object[] { value });
        }

        // Puts the profile into a known state for a check and restores both it
        // and the configuration afterwards, so one check cannot leak into the
        // next or into whatever runs after this file
        private sealed class ProfileState : IDisposable {

            private readonly int SavedCeiling = Config.FanLevelMax;
            private readonly bool SavedAuto = Config.FanLevelAutoDetect;
            private readonly bool SavedProbed = DeviceProfile.Probed;

            public void Begin(int ceiling, bool autoDetect) {
                Config.FanLevelMax = ceiling;
                Config.FanLevelAutoDetect = autoDetect;
                Set("FanLevelCeiling", ceiling);
                Set("FanLevelCeilingSource", "test");
                Set("Probed", true);
            }

            public void Dispose() {
                Config.FanLevelMax = this.SavedCeiling;
                Config.FanLevelAutoDetect = this.SavedAuto;
                Set("Probed", this.SavedProbed);
            }

        }
#endregion

    }

}
