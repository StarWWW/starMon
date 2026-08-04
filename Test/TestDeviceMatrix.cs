// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Hardware.Platform;
using StarMon.Library;
using StarMon.Test.Devices;
using Register = StarMon.Hardware.Ec.EmbeddedControllerData.Register;

namespace StarMon.Test {

    // Runs the shipping code against machines nobody here owns.
    //
    // This application is written for a family of laptops and developed on one
    // of them. The register map is one board's — Hardware/EcData.cs says so —
    // the fan ceiling was one board's, the fan count still is. None of that
    // can be found by testing on the machine it was written for, because on
    // that machine it is all correct. That is exactly why it survived.
    //
    // So the machines come to the code instead. Each scenario in
    // Test/Devices/DeviceCatalogue.cs is a board differing from this one in a
    // single stated way, drawn from a report where that difference broke
    // something, and each is run through the real Platform and the real fan
    // and sensor handling.
    //
    // Expectations that do not hold yet are recorded with SelfTest.Gap rather
    // than deleted or left red. Each one fails the moment it starts holding,
    // so the work that fixes it also retires its own marker.
    [TestSuite(Order = 75)]
    public static class TestDeviceMatrix {

        public static void Run() {

            SelfTest.Group("Device matrix: the board is what the firmware says");
            TestFanCountFollowsTheFirmware();
            TestAbsentFanIsStillAskedAbout();

            SelfTest.Group("Device matrix: a register the board does not carry");
            TestAbsentRegisterEventuallyReadsBlind();
            TestAbsentProbesNeverGoDormant();

            SelfTest.Group("Device matrix: readings that are not temperatures");
            TestSilentControllerYieldsNoReading();

            SelfTest.Group("Device matrix: writes that do not take");
            TestRefusedFanWriteIsNotRetriedForever();
            TestIgnoredFanWriteIsIndistinguishable();

            SelfTest.Group("Device matrix: the controller lock");
            TestLockHeldElsewhereDoesNotCrash();

            SelfTest.Group("Device matrix: every scenario stays standing");
            TestEveryScenarioSurvivesConstruction();

        }

#region Harness
        // Installs a scenario's hardware and restores whatever was there.
        //
        // Hw.Bios and Hw.Ec are assignable static fields, which is what makes
        // this possible at all. What is not usable from a test is Hw.BiosInit
        // and Hw.EcInit: both call App.Exit on failure, and a test that trips
        // that takes the whole run with it. Nothing here calls them.
        private sealed class Installed : IDisposable {

            private readonly IBiosCtl PreviousBios;
            private readonly IEmbeddedController PreviousEc;
            private readonly int PreviousCeiling;
            private readonly bool PreviousAutoDetect;
            private readonly int PreviousWaitTimeout;

            internal readonly DeviceScenario Scenario;

            internal Installed(DeviceScenario scenario) {

                Scenario = scenario;

                PreviousBios = Hw.Bios;
                PreviousEc = Hw.Ec;
                PreviousCeiling = Config.FanLevelMax;
                PreviousAutoDetect = Config.FanLevelAutoDetect;
                PreviousWaitTimeout = Config.EcWaitTimeoutMs;

                // A register the board does not carry costs a full wait before
                // the read gives up, and these scenarios are largely made of
                // such registers: at the shipping 20 ms this suite spends the
                // better part of a minute waiting on purpose.
                //
                // The wait duration is not what is under test here — the
                // wait-and-retry protocol has its own suite, which does time
                // it — so it is shortened. Everything that depends on the
                // *number* of failures rather than their length, which is what
                // the bypass and the dormancy counter both do, is unaffected.
                Config.EcWaitTimeoutMs = 1;

                scenario.Ec.Initialize();
                scenario.Bios.Initialize();

                Hw.Bios = scenario.Bios;
                Hw.Ec = scenario.Ec;

            }

            public void Dispose() {
                Hw.Bios = PreviousBios;
                Hw.Ec = PreviousEc;
                Config.FanLevelMax = PreviousCeiling;
                Config.FanLevelAutoDetect = PreviousAutoDetect;
                Config.EcWaitTimeoutMs = PreviousWaitTimeout;
            }

        }

        // The sensors backed by the fake controller.
        //
        // Not every entry in the array is one: on a machine with a readable
        // processor sensor or an NVIDIA card, Platform substitutes the MSR and
        // NVAPI components for the two named registers. Those read the real
        // machine, so a scenario cannot say anything about them — and an
        // assertion that included them would pass or fail depending on which
        // laptop ran the tests.
        private static List<int> ControllerBacked(Platform platform) {

            List<int> indices = new List<int>();

            for(int i = 0; i < platform.Temperature.Length; i++)
                if(platform.Temperature[i] is EcComponent)
                    indices.Add(i);

            return indices;

        }
#endregion

        // The firmware is asked how many fans the board has. Whether the
        // answer is used is the question.
        private static void TestFanCountFollowsTheFirmware() {

            DeviceScenario one = DeviceCatalogue.SingleFanBoard();

            using(new Installed(one)) {

                Platform platform = new Platform();

                SelfTest.Equal((byte) 1, Hw.Bios.GetFanCount(),
                    "the firmware reports one fan on a single-fan board");

                SelfTest.Gap(platform.Fans.Fan.Length == 1,
                    "a single-fan board builds one fan, not two");

            }

            DeviceScenario three = DeviceCatalogue.ThreeFanBoard();

            using(new Installed(three)) {

                Platform platform = new Platform();

                SelfTest.Equal((byte) 3, Hw.Bios.GetFanCount(),
                    "the firmware reports three fans on a three-fan board");

                SelfTest.Gap(platform.Fans.Fan.Length == 3,
                    "a three-fan board builds three fans");

            }

        }

        // A fan that is not there costs a round trip per tick to not be there.
        //
        // The speed reading prefers the firmware's tachometer call and falls
        // back to the register, so the cost shows up as a BIOS call rather
        // than as an Embedded Controller exchange — but it is still one
        // question per tick about a fan the firmware has already said does
        // not exist.
        private static void TestAbsentFanIsStillAskedAbout() {

            DeviceScenario one = DeviceCatalogue.SingleFanBoard();

            using(new Installed(one)) {

                Platform platform = new Platform();
                one.Bios.ResetCounts();
                one.Ec.ResetCounts();

                foreach(IFan fan in platform.Fans.Fan) {
                    try { fan.GetSpeed(); } catch { }
                    try { fan.GetRate(); } catch { }
                }

                SelfTest.Check(one.Bios.CallCount("GetFanSpeed") > 0,
                    "the fan that exists is asked about");

                SelfTest.Gap(one.Bios.CallCount("GetFanSpeed") == 1
                        && one.Ec.ReadCount(Register.XGS2) == 0,
                    "the fan that does not exist is not asked about ("
                        + one.Bios.CallCount("GetFanSpeed") + " tachometer calls, "
                        + one.Ec.ReadCount(Register.XGS2) + " reads of its rate register)");

            }

        }

        // The bypass that defeats the mechanism built to handle this exact
        // board.
        //
        // WaitRead attempts an honest wait, and once that has failed more than
        // EcFailLimit times for a register it lets the read go out blind. That
        // is deliberate and right for a controller that never raises the flag
        // while still holding an answer. On a register the board does not
        // implement there is no answer: the blind read returns nought, and the
        // caller is told the exchange succeeded.
        private static void TestAbsentRegisterEventuallyReadsBlind() {

            DeviceScenario board = DeviceCatalogue.MissingAuxiliarySensors();

            using(new Installed(board)) {

                int firstSuccess = -1;

                for(int attempt = 1; attempt <= Config.EcFailLimit + 10; attempt++) {

                    byte value;
                    if(Hw.EcTryGetByte((byte) Register.TNT2, out value)) {
                        firstSuccess = attempt;
                        SelfTest.Equal((byte) 0, value,
                            "the value the blind read hands back is nought");
                        break;
                    }

                }

                SelfTest.Check(firstSuccess > 0,
                    "a register the board does not carry eventually reports success "
                        + "(attempt " + firstSuccess + ")");

                SelfTest.Check(firstSuccess > Config.EcFailLimit / Config.EcRetryLimit,
                    "and only after the honest wait has failed its allowance");

            }

        }

        // What the bypass costs, one level up.
        //
        // Platform stands a sensor down after DormantAfter consecutive
        // fruitless updates, so that a register the board does not carry stops
        // costing an exchange a second. But a blind read reports success, and
        // success resets the counter — and EcFailLimit is 15 while
        // DormantAfter is 30, so the counter is reset at half the distance it
        // needs to travel, every time, forever.
        //
        // The mechanism written for boards with absent registers therefore
        // never engages on a board with absent registers.
        private static void TestAbsentProbesNeverGoDormant() {

            DeviceScenario board = DeviceCatalogue.MissingAuxiliarySensors();

            using(new Installed(board)) {

                Platform platform = new Platform();
                List<int> backed = ControllerBacked(platform);

                SelfTest.Check(backed.Count > 0,
                    "the scenario has sensors backed by the fake controller ("
                        + backed.Count + ")");

                for(int i = 0; i < 40; i++)
                    platform.UpdateTemperature(true);

                int dormant = 0;
                foreach(int index in backed)
                    if(platform.TemperatureDormant[index])
                        dormant++;

                SelfTest.Gap(dormant > 0,
                    "a probe the board does not carry is stood down (" + dormant
                        + " of " + backed.Count + " after 40 updates)");

                // The cost the mechanism exists to avoid, measured
                board.Ec.ResetCounts();

                for(int i = 0; i < 10; i++)
                    platform.UpdateTemperature(true);

                SelfTest.Gap(board.Ec.ReadCount(Register.TNT2) < 10,
                    "and stops being polled every update ("
                        + board.Ec.ReadCount(Register.TNT2) + " reads in 10 updates)");

            }

        }

        // A controller that answers nothing must produce no reading at all.
        //
        // Asserted against the components the fake actually backs. The
        // platform's own maximum folds in the firmware's published sensors and
        // the ACPI thermal zones as well, and both of those read the real
        // machine running the tests — so including them would make this assert
        // something about this laptop rather than about the scenario.
        private static void TestSilentControllerYieldsNoReading() {

            DeviceScenario board = DeviceCatalogue.SilentController();

            using(new Installed(board)) {

                Platform platform = new Platform();
                List<int> backed = ControllerBacked(platform);

                for(int i = 0; i < 40; i++)
                    platform.UpdateTemperature(true);

                int invented = 0;
                foreach(int index in backed)
                    if(platform.Temperature[index].GetValue() != 0)
                        invented++;

                SelfTest.Equal(0, invented,
                    "a controller that never answers yields no temperature");

            }

        }

        // A refused write has to be visible. It arriving as a return code that
        // looks like a value is what had the application repeating a call the
        // firmware had already declined.
        private static void TestRefusedFanWriteIsNotRetriedForever() {

            DeviceScenario board = DeviceCatalogue.FanLevelWriteRefused();

            using(new Installed(board)) {

                Platform platform = new Platform();
                board.Bios.ResetCounts();

                bool surfaced = false;
                try {
                    platform.Fans.SetLevels(new byte[] { 30, 30 });
                } catch(BiosException) {
                    surfaced = true;
                } catch { }

                SelfTest.Check(surfaced || board.Bios.CallCount("SetFanLevel") <= 2,
                    "a refused fan level write surfaces rather than being repeated ("
                        + board.Bios.CallCount("SetFanLevel") + " attempts)");

            }

        }

        // The quietest failure of the lot: the call succeeds, the firmware
        // does nothing, and nothing anywhere can tell the difference. This is
        // what "the software completely ignores commands" looks like from
        // inside the software.
        private static void TestIgnoredFanWriteIsIndistinguishable() {

            DeviceScenario board = DeviceCatalogue.FanLevelWriteIgnored();

            using(new Installed(board)) {

                Platform platform = new Platform();

                try { platform.Fans.SetLevels(new byte[] { 30, 30 }); } catch { }

                byte[] readBack = null;
                try { readBack = platform.Fans.GetLevels(); } catch { }

                bool took = readBack != null && readBack.Length > 0 && readBack[0] == 30;

                SelfTest.Check(!took,
                    "the board did not take the level, as the scenario declares");

                SelfTest.Gap(false,
                    "a fan level write that does not take is detected by reading it back "
                        + "- nothing checks this today");

            }

        }

        // Another application holding the lock is ordinary: OmenMon,
        // LibreHardwareMonitor and the manufacturer's own tray application all
        // take the same named mutex.
        private static void TestLockHeldElsewhereDoesNotCrash() {

            DeviceScenario board = DeviceCatalogue.EcLockHeldElsewhere();

            using(new Installed(board)) {

                Platform platform = new Platform();

                bool survived = true;
                try {
                    platform.UpdateTemperature(true);
                } catch(Exception) {
                    survived = false;
                }

                SelfTest.Check(survived,
                    "a held lock produces no exception out of the sensor path");

                SelfTest.Check(board.Ec.LockRefusals > 0,
                    "and the lock was genuinely refused ("
                        + board.Ec.LockRefusals + " times)");

                // Hw.EcExec answers a refused lock with App.Error, which in
                // the interface is a modal dialog rather than a line in the
                // log - one per register, per tick, for as long as the other
                // application holds it. The console host used by these tests
                // prints instead, which is why this run is not a wall of
                // dialogs; a user's would be.
                SelfTest.Gap(board.Ec.LockRefusals <= 1,
                    "a held lock is reported once, not once per register per tick ("
                        + board.Ec.LockRefusals + " reports for one update)");

            }

        }

        // The blunt one: every scenario in the catalogue has to get as far as
        // a constructed platform and a temperature reading without throwing.
        // A board this application cannot even start against is a board whose
        // owner sees nothing at all.
        private static void TestEveryScenarioSurvivesConstruction() {

            foreach(DeviceScenario scenario in DeviceCatalogue.All()) {

                using(new Installed(scenario)) {

                    string failure = null;

                    try {

                        Platform platform = new Platform();

                        platform.UpdateTemperature(true);

                        try { platform.Fans.GetLevels(); } catch(BiosException) { }
                        try { platform.Fans.GetMode(); } catch(BiosException) { }
                        try { platform.Fans.GetMax(); } catch(BiosException) { }

                    } catch(Exception e) {
                        failure = e.GetType().Name + ": " + e.Message;
                    }

                    SelfTest.Check(failure == null,
                        scenario + " reaches a working platform"
                            + (failure == null ? "" : " - " + failure));

                }

            }

        }

    }

}
