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
using StarMon.Ui.Windows;

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
            TestTheGraphicsCardIsNamedWhoeverMadeIt();

            SelfTest.Group("Presentation: durations and numbers");
            TestUptimeFormatting();
            TestNumberFormatting();

            SelfTest.Group("Presentation: fitting the screen");
            TestWindowFitsTheDisplaysThatBrokeIt();

            SelfTest.Group("Presentation: following the machine");
            TestTheSelectorSeedsItselfBeforeAnyClick();

        }

        // A selector with no choice made yet has to follow the machine.
        //
        // The System page's power mode read as blank on every machine, always,
        // until the user clicked something. "Never set" was stood in for by
        // int.MinValue, and the guard subtracted it from Environment.TickCount
        // — which is positive for the first twenty-five days of uptime, so the
        // subtraction overflowed and came out negative every time. The settle
        // window never opened. The poller reads the power mode every tick
        // specifically to keep that row fresh, and nothing consumed it.
        private static void TestTheSelectorSeedsItselfBeforeAnyClick() {

            const int Settle = 2500;

            // Nothing chosen yet: the machine's answer is the only one there is
            SelfTest.Check(WindowController.ShouldFollowHardware(
                    false, 0, 1000, Settle),
                "before any click, the reading from the machine is shown");

            // This is the case that was broken: an ordinary uptime, no click
            SelfTest.Check(WindowController.ShouldFollowHardware(
                    false, int.MinValue, 60000, Settle),
                "and still is at an ordinary tick count");

            // Just clicked: the user's choice outranks the reading already in
            // flight, which still carries the old mode
            SelfTest.Check(!WindowController.ShouldFollowHardware(
                    true, 100000, 100500, Settle),
                "just after a click, the choice made here wins");

            SelfTest.Check(!WindowController.ShouldFollowHardware(
                    true, 100000, 100000 + Settle, Settle),
                "and on the boundary itself");

            // Settled: back to following the machine, so a change made from
            // the battery flyout shows here
            SelfTest.Check(WindowController.ShouldFollowHardware(
                    true, 100000, 100000 + Settle + 1, Settle),
                "once settled, the machine is followed again");

            // The tick counter wraps every twenty-five days. Unchecked
            // subtraction is what makes that a non-event, and it has to stay
            // that way.
            SelfTest.Check(WindowController.ShouldFollowHardware(
                    true, int.MaxValue - 1000, unchecked(int.MaxValue + 4000), Settle),
                "and a click either side of the tick counter wrapping still settles");

        }

        // The window opens at a size that fits the desktop it opens on.
        //
        // It used to be pinned to exactly 1000x760 in both directions and set
        // to CanMinimize, so there was no recovery when that did not fit. Those
        // are device-independent units, so the physical size follows the
        // display scale — and at 150 %, an ordinary accessibility setting, the
        // window wanted 1140 points of height on a desktop with about 1040 to
        // give. A hundred points of the interface were below the bottom of the
        // screen and unreachable.
        //
        // The figures below are work areas: the desktop minus the taskbar, in
        // the same units the window is sized in, which is what
        // SystemParameters.WorkArea reports.
        private static void TestWindowFitsTheDisplaysThatBrokeIt() {

            const double MinWidth = 640;
            const double MinHeight = 480;

            // 1920x1080 at 100 %: the machine this was designed on
            Size roomy = MainWindow.FitTo(1920, 1040, MinWidth, MinHeight);
            SelfTest.Equal(1000.0, roomy.Width,
                "a full-size desktop opens at the design width");
            SelfTest.Equal(760.0, roomy.Height,
                "and the design height");

            // 1920x1080 at 150 %: 1280x693 points of room
            Size scaled = MainWindow.FitTo(1280, 693, MinWidth, MinHeight);
            SelfTest.Check(scaled.Height <= 693,
                "at 150 % the window fits the height available ("
                    + scaled.Height + " of 693)");
            SelfTest.Check(scaled.Width <= 1280,
                "and the width");

            // 1366x768, still shipped on entry-level machines in this family
            Size small = MainWindow.FitTo(1366, 728, MinWidth, MinHeight);
            SelfTest.Check(small.Height <= 728,
                "a 1366x768 panel fits too (" + small.Height + " of 728)");
            SelfTest.Equal(1000.0, small.Width,
                "with the width unaffected, since there is room for it");

            // Never past the design size, however much room there is
            Size huge = MainWindow.FitTo(3840, 2000, MinWidth, MinHeight);
            SelfTest.Equal(1000.0, huge.Width,
                "a large desktop does not stretch the window past its design size");
            SelfTest.Equal(760.0, huge.Height,
                "in either direction");

            // And never below the minimum the window declares: WPF would
            // resize it back up, and it would overflow again
            Size cramped = MainWindow.FitTo(400, 300, MinWidth, MinHeight);
            SelfTest.Equal(MinWidth, cramped.Width,
                "a desktop smaller than the minimum yields the minimum");
            SelfTest.Equal(MinHeight, cramped.Height,
                "in both directions");

            // A work area that could not be read is left alone rather than
            // guessed at
            SelfTest.Check(MainWindow.FitTo(0, 0, MinWidth, MinHeight).IsEmpty,
                "an unreadable work area yields no answer rather than a wrong one");

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
        // Which display adapter is the graphics card worth naming.
        //
        // The picker used to be one line: the first name containing "NVIDIA".
        // A Victus with Radeon graphics is a machine this application drives,
        // shows a temperature for, and left the card unnamed on — and the
        // adapter lists below are what real machines report, none of them the
        // machine this was written on.
        private static void TestTheGraphicsCardIsNamedWhoeverMadeIt() {

            // The machine this was developed on, exactly as -Probe reported
            // it. The integrated adapter is called "Intel(R) Graphics" —
            // not UHD, not Iris, just that — which is why the rule for these
            // is "Intel and not Arc" rather than a list of product names.
            SelfTest.Equal("NVIDIA GeForce RTX 5050 Laptop GPU",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "NVIDIA GeForce RTX 5050 Laptop GPU",
                    "Intel(R) Graphics" }),
                "the discrete NVIDIA card is preferred over the integrated one");

            // The same machine with the card removed from the list: the
            // integrated adapter must still be recognised as one, or it gets
            // named as though it were the discrete card
            SelfTest.Equal("",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Intel(R) Graphics" }),
                "and a bare \"Intel(R) Graphics\" is known to be integrated");

            SelfTest.Equal("NVIDIA GeForce RTX 5050 Laptop GPU",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Intel(R) UHD Graphics",
                    "NVIDIA GeForce RTX 5050 Laptop GPU" }),
                "the older Intel naming is handled the same way");

            // Order must not decide it
            SelfTest.Equal("NVIDIA GeForce RTX 4060 Laptop GPU",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "NVIDIA GeForce RTX 4060 Laptop GPU",
                    "Intel(R) Iris(R) Xe Graphics" }),
                "whichever way round the machine lists them");

            // A Victus with AMD graphics: the integrated Radeon of the
            // processor and the discrete Radeon RX card, which share a word
            SelfTest.Equal("AMD Radeon RX 7600S",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "AMD Radeon(TM) Graphics",
                    "AMD Radeon RX 7600S" }),
                "a discrete Radeon is told apart from the processor's own");

            SelfTest.Equal("Intel(R) Arc(TM) A730M Graphics",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Intel(R) UHD Graphics",
                    "Intel(R) Arc(TM) A730M Graphics" }),
                "and so is an Arc card from the integrated Intel graphics");

            // A machine with no discrete card at all. Naming the integrated
            // one would be honest, but the block this fills sits beside the
            // discrete card's readings — so nothing is better than a name for
            // something the readings are not about.
            SelfTest.Equal("",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Intel(R) UHD Graphics" }),
                "a machine with only integrated graphics names nothing");

            // The software adapters, which are what makes "the first one that
            // is not Intel" the wrong rule
            SelfTest.Equal("NVIDIA GeForce RTX 3050 Laptop GPU",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Microsoft Basic Display Adapter",
                    "Parsec Virtual Display Adapter",
                    "Intel(R) UHD Graphics",
                    "NVIDIA GeForce RTX 3050 Laptop GPU" }),
                "a remote-desktop adapter is not mistaken for the card");

            // Two unrecognised adapters is a coin toss, and an unnamed card is
            // better than a wrongly named one
            SelfTest.Equal("",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Some Unknown Accelerator", "Another Unknown Accelerator" }),
                "two names it cannot tell apart produce none");

            // One unrecognised adapter, on the other hand, is the answer: an
            // unfamiliar card is exactly the machine a report comes from
            SelfTest.Equal("Moore Threads MTT S80",
                StarMon.AppService.Poller.PickGraphicsName(new string[] {
                    "Intel(R) UHD Graphics", "Moore Threads MTT S80" }),
                "a single unfamiliar card is named rather than discarded");

            SelfTest.Equal("",
                StarMon.AppService.Poller.PickGraphicsName(null),
                "a machine that lists nothing produces nothing");

            SelfTest.Equal("",
                StarMon.AppService.Poller.PickGraphicsName(new string[0]),
                "and so does one whose list is empty");

        }

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
