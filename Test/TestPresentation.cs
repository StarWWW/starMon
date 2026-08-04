// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Globalization;
using System.Windows;
using StarMon.AppService;
using StarMon.Hardware;
using StarMon.Library;
using StarMon.Ui.ViewModels;
using StarMon.Ui.Views;

namespace StarMon.Test {

    // The layer between a reading and what the user sees.
    //
    // Twelve thousand lines of this application are interface, and until now
    // three assertions touched any of it. Most of that is markup and drawn
    // controls, which cannot be checked without a window — but a useful part
    // is not: the value converters are pure functions of their input, and so
    // are the poller's label and name helpers. They are also where a wrong
    // answer is least likely to be noticed, because the result is a colour or
    // a piece of text that looks like a decision somebody made.
    [TestSuite(Order = 85)]
    public static class TestPresentation {

        public static void Run() {

            SelfTest.Group("Presentation: visibility");
            TestBoolVisibility();
            TestEmptyVisibility();

            SelfTest.Group("Presentation: colour selection");
            TestHealthBrushIsAlwaysABrush();
            TestLogLevelBrushIsAlwaysABrush();

            SelfTest.Group("Presentation: names and labels");
            TestPartNamesAreTidied();
            TestSensorLabels();
            TestThrottleDescriptions();

            SelfTest.Group("Presentation: durations and numbers");
            TestUptimeFormatting();
            TestNumberFormatting();

        }

        // The one converter with options, and the one most likely to be given
        // a value that is not a bool: a binding to a nullable, or to a
        // property that has not been set yet, arrives here as null.
        private static void TestBoolVisibility() {

            BoolVisibilityConverter converter = new BoolVisibilityConverter();

            SelfTest.Equal(Visibility.Visible, Convert(converter, true, null),
                "true shows the element");
            SelfTest.Equal(Visibility.Collapsed, Convert(converter, false, null),
                "false reclaims the space");

            SelfTest.Equal(Visibility.Hidden, Convert(converter, false, "Hidden"),
                "the Hidden option keeps the space");
            SelfTest.Equal(Visibility.Visible, Convert(converter, true, "Hidden"),
                "and still shows a true value");

            SelfTest.Equal(Visibility.Collapsed, Convert(converter, true, "Inverse"),
                "the Inverse option flips the sense");
            SelfTest.Equal(Visibility.Visible, Convert(converter, false, "Inverse"),
                "so a false value is what shows");

            SelfTest.Equal(Visibility.Hidden, Convert(converter, true, "Inverse,Hidden"),
                "both options together");

            // A binding that has not resolved yet hands over null, and a
            // converter that threw on it would take the window down
            SelfTest.Equal(Visibility.Collapsed, Convert(converter, null, null),
                "a value that is not a bool reads as false");
            SelfTest.Equal(Visibility.Collapsed, Convert(converter, "true", null),
                "including the string spelling of one");

            SelfTest.Equal(true, converter.ConvertBack(Visibility.Visible,
                    typeof(bool), null, CultureInfo.InvariantCulture),
                "Visible converts back to true");
            SelfTest.Equal(false, converter.ConvertBack(Visibility.Collapsed,
                    typeof(bool), null, CultureInfo.InvariantCulture),
                "Collapsed converts back to false");

        }

        private static void TestEmptyVisibility() {

            EmptyVisibilityConverter converter = new EmptyVisibilityConverter();

            SelfTest.Equal(Visibility.Collapsed, Convert(converter, "", null),
                "an empty supporting line reserves no row");
            SelfTest.Equal(Visibility.Collapsed, Convert(converter, null, null),
                "and neither does a missing one");
            SelfTest.Equal(Visibility.Visible, Convert(converter, "42 C", null),
                "a line with something to say is shown");

            // A single space is text, and the row it occupies is a blank one.
            // Recorded rather than asserted either way: it is a judgement
            // about what the interface should do, not a defect.
            SelfTest.Equal(Visibility.Visible, Convert(converter, " ", null),
                "a space counts as text, so the row is kept");

        }

        // Neither of the brush converters may hand back null. A null brush in
        // a binding is not a missing colour, it is a control that draws
        // nothing — and both of these run before the theme dictionary exists
        // when a surface is rendered outside the application.
        private static void TestHealthBrushIsAlwaysABrush() {

            HealthBrushConverter converter = new HealthBrushConverter();

            foreach(Health health in Enum.GetValues(typeof(Health)))
                SelfTest.Check(Convert(converter, health, null) != null,
                    "a brush is produced for " + health);

            SelfTest.Check(Convert(converter, null, null) != null,
                "and for a value that is not a health at all");

            SelfTest.Check(Convert(converter, Health.Neutral, "Muted") != null,
                "and for the quiet weight");

        }

        private static void TestLogLevelBrushIsAlwaysABrush() {

            LogLevelBrushConverter converter = new LogLevelBrushConverter();

            foreach(LogLevel level in Enum.GetValues(typeof(LogLevel)))
                SelfTest.Check(Convert(converter, level, null) != null,
                    "a brush is produced for log level " + level);

            SelfTest.Check(Convert(converter, "Error", null) != null,
                "and for a value that is not a level");

        }

        // What WMI reports is not what fits in a card
        private static void TestPartNamesAreTidied() {

            SelfTest.Equal("Intel Core 5 210H",
                Poller.Tidy("Intel(R) Core(TM) 5 210H"),
                "the trademark marks come off a processor name");

            SelfTest.Equal("NVIDIA GeForce RTX 5050",
                Poller.Tidy("NVIDIA GeForce RTX 5050 Laptop GPU"),
                "and the redundant suffix off a graphics one");

            SelfTest.Equal("AMD Ryzen 7 7840HS",
                Poller.Tidy("AMD Ryzen 7 7840HS  CPU"),
                "a removal that leaves a double space collapses it");

            SelfTest.Equal("", Poller.Tidy(null),
                "a name that is not there produces no text");
            SelfTest.Equal("", Poller.Tidy(""),
                "and neither does an empty one");

            SelfTest.Equal("Something Unremarkable",
                Poller.Tidy("  Something Unremarkable  "),
                "surrounding space is trimmed");

        }

        // The register labels come from the board's ACPI tables and mean
        // nothing to a reader. The mapping is what makes the sensors page
        // legible, and getting it wrong mislabels a temperature rather than
        // failing.
        private static void TestSensorLabels() {

            // The four named probes resolve to something other than their
            // register label. Which words those are is the locale's business,
            // so what is asserted is that a translation happened at all.
            string[] named = { "RTMP", "TMP1", "CPUT", "GPTM", "BIOS" };

            foreach(string register in named)
                SelfTest.Check(Poller.SensorLabel(register) != register,
                    register + " is given a name rather than shown as a register");

            // The spare probes keep their firmware numbering, so that two
            // machines can be compared by it
            SelfTest.Check(Poller.SensorLabel("TNT4").EndsWith("4", StringComparison.Ordinal),
                "a spare probe keeps the number the firmware gave it");

            SelfTest.Check(Poller.SensorLabel("TNT2").EndsWith("2", StringComparison.Ordinal),
                "each of them its own");

            // A register this build has no name for is shown as itself, which
            // is at least honest. A board carrying a probe nobody here has
            // seen lands in exactly this branch.
            SelfTest.Equal("ZZZ9", Poller.SensorLabel("ZZZ9"),
                "an unrecognised register is shown as itself");

            SelfTest.Equal(null, Poller.SensorLabel(null),
                "and a missing one produces nothing rather than throwing");

        }

        private static void TestThrottleDescriptions() {

            var none = Hardware.Cpu.CpuTemperature.ThrottleFlags.None;
            var thermal = Hardware.Cpu.CpuTemperature.ThrottleFlags.Thermal;
            var power = Hardware.Cpu.CpuTemperature.ThrottleFlags.PowerLimit;

            string forNone = Poller.Describe(none);
            string forThermal = Poller.Describe(thermal);
            string forPower = Poller.Describe(power);
            string forBoth = Poller.Describe(thermal | power);

            SelfTest.Check(forNone != forThermal
                    && forNone != forPower
                    && forThermal != forPower
                    && forBoth != forThermal
                    && forBoth != forPower,
                "each throttle state reads differently from the others");

            SelfTest.Check(!string.IsNullOrEmpty(forNone)
                    && !string.IsNullOrEmpty(forBoth),
                "and none of them is blank");

        }

        private static void TestUptimeFormatting() {

            SelfTest.Equal("5m", SystemMetrics.FormatUptime(TimeSpan.FromMinutes(5)),
                "under an hour reads in minutes");

            SelfTest.Equal("2h 30m", SystemMetrics.FormatUptime(
                    TimeSpan.FromMinutes(150)),
                "under a day reads in hours and minutes");

            SelfTest.Equal("3d 4h", SystemMetrics.FormatUptime(
                    new TimeSpan(3, 4, 30, 0)),
                "over a day reads in days and hours");

            SelfTest.Equal("0m", SystemMetrics.FormatUptime(TimeSpan.Zero),
                "a machine that has just started reads as nought minutes");

            // The boundaries, where an off-by-one shows up as a jump
            SelfTest.Equal("1h 0m", SystemMetrics.FormatUptime(
                    TimeSpan.FromMinutes(60)),
                "exactly an hour crosses into the hour form");

            SelfTest.Equal("1d 0h", SystemMetrics.FormatUptime(
                    TimeSpan.FromHours(24)),
                "exactly a day crosses into the day form");

            SelfTest.Equal("59m", SystemMetrics.FormatUptime(
                    TimeSpan.FromMinutes(59)),
                "and a minute short of an hour does not");

        }

        // The formatting half of Conv. The parsing half has had tests since
        // the beginning; this direction is what the command line prints and
        // what the configuration writer emits, and it had none.
        private static void TestNumberFormatting() {

            SelfTest.Equal("42", Conv.GetString(42, 0, 10),
                "a decimal number with no padding");

            SelfTest.Equal("0042", Conv.GetString(42, 4, 10),
                "and padded to a width");

            // Lower case, and deliberately so: the byte-array overload just
            // above it in Conv.cs forces ToLowerInvariant, so the two agree
            // and the command line prints one style throughout.
            SelfTest.Equal("2a", Conv.GetString(42, 0, 16),
                "hexadecimal is lower case, as the byte-array overload also produces");

            SelfTest.Equal("002a", Conv.GetString(42, 4, 16),
                "and pads the same way");

            SelfTest.Equal(Conv.GetString(new byte[] { 0xAB }),
                Conv.GetString(0xAB, 2, 16),
                "the two ways of writing a byte as hexadecimal agree");

            SelfTest.Equal("101010", Conv.GetString(42, 0, 2),
                "binary");

            SelfTest.Equal("00101010", Conv.GetString(42, 8, 2),
                "and a byte's worth of it");

            // Every one of these has to survive the round trip the
            // configuration file puts it through, on any machine's locale
            byte parsed;
            SelfTest.Check(Conv.GetByte("0x" + Conv.GetString(200, 2, 16), out parsed)
                    && parsed == 200,
                "a byte written as hexadecimal parses back to itself");

            ushort word;
            SelfTest.Check(Conv.GetWord(Conv.GetString(4095, 0, 10), out word)
                    && word == 4095,
                "a word written as decimal parses back to itself");

        }

#region Helpers
        private static object Convert(System.Windows.Data.IValueConverter converter,
            object value, object parameter) {

            return converter.Convert(value, typeof(object), parameter,
                CultureInfo.InvariantCulture);

        }
#endregion

    }

}
