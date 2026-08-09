// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware;
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
            TestStuckAuxiliaryProbeDoesNotDriveTheFans();
            TestAHundredDegreesIsARealTemperature();
            TestAFrozenProbeStopsCountingTowardsTheMaximum();

            SelfTest.Group("Device matrix: leaving the machine as it was found");
            TestExitHandsTheFansBack();

            SelfTest.Group("Device matrix: writes that do not take");
            TestRefusedFanWriteIsNotRetriedForever();
            TestIgnoredFanWriteIsNoticed();
            TestAWriteThatTookIsNotComplainedAbout();
            TestTheTwoFalseAlarmsRealHardwareProduced();

            SelfTest.Group("Device matrix: the controller lock");
            TestLockHeldElsewhereDoesNotCrash();

            SelfTest.Group("Device matrix: every scenario stays standing");
            TestEveryScenarioSurvivesConstruction();
            TestEveryScenarioCanBeReported();

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
            private readonly bool PreviousProbed;

            private readonly System.Collections.Generic.Dictionary<string,
                Config.TemperatureSensorData> PreviousSensors;

            internal readonly DeviceScenario Scenario;

            internal Installed(DeviceScenario scenario) {

                Scenario = scenario;

                PreviousBios = Hw.Bios;
                PreviousEc = Hw.Ec;
                PreviousCeiling = Config.FanLevelMax;
                PreviousAutoDetect = Config.FanLevelAutoDetect;
                PreviousWaitTimeout = Config.EcWaitTimeoutMs;
                PreviousProbed = DeviceProfile.Probed;
                PreviousSensors = Config.TemperatureSensor;

                // The scenarios describe boards, not the machine running the
                // tests. A configuration file beside the test host replaces
                // the sensor set wholesale, so without this a user who had
                // opted a probe back into the fan decision would see these
                // fail — reporting a defect in the application when what
                // changed was their own preference.
                Config.TemperatureSensor =
                    new System.Collections.Generic.Dictionary<string,
                        Config.TemperatureSensorData>(Config.TemperatureSensorShipped);

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

                // The firmware returns every fan's level in one call, so Fan
                // holds the answer briefly and shares it between fans. That
                // window is shorter than anything reads it at on a real
                // machine and longer than the gap between two scenarios here,
                // so without this one board would be handed the previous
                // board's levels.
                Fan.InvalidateLevels();

                // Likewise the record of what the last write asked for, and
                // the once-per-episode latch that goes with it
                Fan.ResetLevelVerification();

                // The once-per-episode latch on the lock-failure report is
                // process-wide, so a scenario that expects to see one has to
                // start from none
                Hw.ResetEcLockReports();

                // The probe is what the platform is built from now, so a
                // scenario has to be probed the way a real machine is. Its
                // once-only guard is cleared first, since it has already run
                // for whichever scenario came before.
                SetProfile("Probed", false);
                DeviceProfile.Probe(new Settings());

            }

            public void Dispose() {
                Hw.Bios = PreviousBios;
                Hw.Ec = PreviousEc;
                Config.FanLevelMax = PreviousCeiling;
                Config.FanLevelAutoDetect = PreviousAutoDetect;
                Config.EcWaitTimeoutMs = PreviousWaitTimeout;
                SetProfile("Probed", PreviousProbed);
                Config.TemperatureSensor = PreviousSensors;
                DeviceProfile.Attach(null);
            }

            // The findings are deliberately read-only to everything but the
            // probe, so the guard is reset through the private setter rather
            // than by opening one up
            private static void SetProfile(string name, object value) {
                typeof(DeviceProfile)
                    .GetProperty(name, System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static)
                    .GetSetMethod(true)
                    .Invoke(null, new object[] { value });
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

                SelfTest.Equal(1, platform.Fans.Fan.Length,
                    "a single-fan board builds one fan, not two");

            }

            DeviceScenario three = DeviceCatalogue.ThreeFanBoard();

            using(new Installed(three)) {

                Platform platform = new Platform();

                SelfTest.Equal((byte) 3, Hw.Bios.GetFanCount(),
                    "the firmware reports three fans on a three-fan board");

                SelfTest.Equal(3, platform.Fans.Fan.Length,
                    "a three-fan board builds three fans");

                // The third fan has a tachometer register in the table and no
                // setpoint one. It is driven through the firmware's own
                // fan-level call, which takes an index, so it is a fan with
                // fewer registers rather than a fan that is not there.
                SelfTest.Equal(30, platform.Fans.Fan[0].GetLevel(),
                    "each fan reads its own index out of the firmware's array");
                SelfTest.Equal(35, platform.Fans.Fan[1].GetLevel(),
                    "the second one its own");
                SelfTest.Equal(40, platform.Fans.Fan[2].GetLevel(),
                    "and the third, which has no setpoint register, its own too");

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

                SelfTest.Check(one.Bios.CallCount("GetFanSpeed") == 1
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

                // Every sensor, which is what the poller asks for: a probe
                // kept for display and left out of the fan decision still has
                // to be read, and still has to be stood down when the board
                // does not carry it
                for(int i = 0; i < 40; i++)
                    platform.UpdateTemperature(false);

                int dormant = 0;
                foreach(int index in backed)
                    if(platform.TemperatureDormant[index])
                        dormant++;

                // Five of the six registers the fake backs are the ones this
                // scenario removes; the sixth is present and has to stay awake
                SelfTest.Equal(5, dormant,
                    "every probe the board does not carry is stood down, and only those ("
                        + dormant + " of " + backed.Count + " after 40 updates)");

                // The cost the mechanism exists to avoid, measured
                board.Ec.ResetCounts();

                for(int i = 0; i < 10; i++)
                    platform.UpdateTemperature(false);

                SelfTest.Check(board.Ec.ReadCount(Register.TNT2) < 10,
                    "and stops being polled every update ("
                        + board.Ec.ReadCount(Register.TNT2) + " reads in 10 updates)");

                SelfTest.Check(board.Ec.ReadCount(Register.TMP1) == 10,
                    "while the probe that answers is still read every update ("
                        + board.Ec.ReadCount(Register.TMP1) + ")");

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
                    platform.UpdateTemperature(false);

                int invented = 0;
                foreach(int index in backed)
                    if(platform.Temperature[index].GetValue() != 0)
                        invented++;

                SelfTest.Equal(0, invented,
                    "a controller that never answers yields no temperature");

            }

        }

        // The most reported failure there is, from inside.
        //
        // An auxiliary probe reading 84 C on an idle machine is not a
        // temperature of anything, but it is a believable one — so the
        // plausibility ceiling passes it, and the dormancy mechanism has
        // nothing to stand down because the register answers perfectly well.
        // The only thing that keeps it away from the fans is not being asked.
        private static void TestStuckAuxiliaryProbeDoesNotDriveTheFans() {

            DeviceScenario board = DeviceCatalogue.StuckAuxiliarySensor();

            using(new Installed(board)) {

                Platform platform = new Platform();

                for(int i = 0; i < 5; i++)
                    platform.UpdateTemperature(false);

                // It is read, and it is there to be shown
                int shown = -1;
                for(int i = 0; i < platform.Temperature.Length; i++)
                    if(platform.Temperature[i].GetName() == "TNT2")
                        shown = platform.Temperature[i].GetValue();

                SelfTest.Equal(84, shown,
                    "the auxiliary probe is still read and still shown");

                // And it is not among the readings the hottest-of check walks
                bool used = false;
                for(int i = 0; i < platform.Temperature.Length; i++)
                    if(platform.TemperatureUse[i]
                        && platform.Temperature[i].GetName() == "TNT2")
                        used = true;

                SelfTest.Check(!used,
                    "and is kept out of the reading the fan curve acts on");

                // The named probes on this board are all cooler than the stuck
                // one, so a maximum that has picked it up is visible as such.
                // Asserted against the controller-backed sensors only: the
                // platform maximum also folds in the real machine's published
                // sensors and ACPI zones.
                int highest = 0;
                foreach(int index in ControllerBacked(platform))
                    if(platform.TemperatureUse[index]
                        && platform.Temperature[index].GetValue() > highest)
                        highest = platform.Temperature[index].GetValue();

                SelfTest.Check(highest > 0 && highest < 84,
                    "so the hottest used reading is a real one (" + highest + " C)");

            }

        }

        // The believability ceiling has to admit temperatures the hardware
        // actually reaches. A reading above it is discarded, not clamped, so
        // the component keeps reporting the last cooler figure it saw — and
        // the thermal guard, whose whole job is to notice a machine getting
        // too hot, reads that stale number instead.
        private static void TestAHundredDegreesIsARealTemperature() {

            DeviceScenario board = DeviceCatalogue.RunningPastAHundred();

            using(new Installed(board)) {

                Platform platform = new Platform();

                for(int i = 0; i < 3; i++)
                    platform.UpdateTemperature(false);

                // Asserted on the chipset probe rather than the processor one:
                // CPUT is replaced by the processor's own MSR reading wherever
                // that is available, which on the machine running these tests
                // it is, so it would be measuring the wrong path.
                int hot = -1;
                for(int i = 0; i < platform.Temperature.Length; i++)
                    if(platform.Temperature[i].GetName() == "RTMP")
                        hot = platform.Temperature[i].GetValue();

                SelfTest.Equal(103, hot,
                    "a register reading of 103 C is kept rather than discarded");

                // Whichever probe the board reports it through, the hottest
                // reading has to be able to exceed a hundred at all
                SelfTest.Check(Config.MaxBelievableTemperature >= 105,
                    "the ceiling admits a full-load mobile processor ("
                        + Config.MaxBelievableTemperature + " C)");

                // And it still rejects a register that is not a temperature
                SelfTest.Check(Config.MaxBelievableTemperature < 255,
                    "while still rejecting a register holding something else");

            }

        }

        // Quitting has to put the machine back.
        //
        // Nothing on any exit path did. Quitting with the fans switched off
        // left them off; quitting while thermal protection had them pinned at
        // maximum left them there. The controller's own failsafe countdown was
        // the only recovery, and it governs the manual level rather than the
        // off switch or the maximum toggle - and the panic path zeroes it
        // deliberately.
        private static void TestExitHandsTheFansBack() {

            DeviceScenario board = DeviceCatalogue.Reference();

            using(new Installed(board)) {

                Platform platform = new Platform();

                // Leave the machine in the state that used to survive a quit
                platform.Fans.SetOff(true);
                platform.Fans.SetMax(true);
                platform.Fans.SetManual(true);

                board.Bios.WriteLog.Clear();
                board.Ec.ResetCounts();

                StarMon.AppService.FanControl.ReleaseToFirmware(platform.Fans);

                SelfTest.Check(!platform.Fans.GetOff(),
                    "the fans are switched back on");

                SelfTest.Check(!platform.Fans.GetMax(),
                    "the maximum override is lifted");

                SelfTest.Check(!platform.Fans.GetManual(),
                    "and manual control is handed back");

            }

            // A board that refuses one of the three still gets the other two.
            // Stopping at the first refusal would leave the rest of the state
            // exactly as it was, which is the outcome being avoided.
            DeviceScenario awkward = DeviceCatalogue.Reference();
            awkward.Bios.Refuse("SetMaxFan");

            using(new Installed(awkward)) {

                Platform platform = new Platform();

                platform.Fans.SetOff(true);
                platform.Fans.SetManual(true);

                string failure = null;
                try {
                    StarMon.AppService.FanControl.ReleaseToFirmware(platform.Fans);
                } catch(Exception e) {
                    failure = e.GetType().Name;
                }

                SelfTest.Check(failure == null,
                    "a refused step does not abandon the rest"
                        + (failure == null ? "" : " - " + failure));

                SelfTest.Check(!platform.Fans.GetOff() && !platform.Fans.GetManual(),
                    "and the steps either side of it still happen");

            }

        }

        // A probe that reads hot and then stops answering must not go on being
        // the machine's hottest point for ever.
        //
        // From a real session log: the chipset probe on this board reads low
        // forties and intermittently returns 144, which the plausibility
        // ceiling refuses. Once it settles there every update is refused, and
        // the sensor keeps whatever it last managed — indistinguishable from a
        // working one, for the life of the process.
        //
        // Standing a sensor down used to mean only "ask less often"; its last
        // reading still counted. So a probe frozen at ninety pinned the fans
        // at maximum with nothing anywhere saying which sensor was doing it,
        // which is the complaint this application's upstream gets most often.
        private static void TestAFrozenProbeStopsCountingTowardsTheMaximum() {

            DeviceScenario board = DeviceCatalogue.ProbeFreezesHot();

            using(new Installed(board)) {

                Platform platform = new Platform();

                // It reads, and it is hot
                platform.UpdateTemperature(false);

                SelfTest.Check(HottestOfControllerBacked(platform) >= 92,
                    "the probe is read while it is answering");

                // Now it starts returning what the real board returns: 0x90,
                // which is 144, which the plausibility ceiling refuses. Every
                // update from here on is refused and the component keeps the
                // last figure it managed.
                //
                // Modelled this way rather than by removing the register: an
                // absent register answers a blind read with nought, and the
                // component settles at nought, which is excluded for a
                // different reason. The case that matters is a register that
                // answers with something that is not a temperature.
                board.Ec.Set(Register.RTMP, 144);

                for(int i = 0; i < 40; i++)
                    platform.UpdateTemperature(false);

                int frozen = -1;
                for(int i = 0; i < platform.Temperature.Length; i++)
                    if(platform.Temperature[i].GetName() == "RTMP") {
                        frozen = platform.Temperature[i].GetValue();
                        SelfTest.Check(platform.TemperatureDormant[i],
                            "a sensor that stopped answering is stood down"
                                + " even though it still holds a reading");
                    }

                SelfTest.Equal(92, frozen,
                    "it does still hold the reading it froze at");

                SelfTest.Check(HottestOfControllerBacked(platform) < 92,
                    "but that reading no longer counts as the machine's hottest point ("
                        + HottestOfControllerBacked(platform) + " C)");

            }

        }

        // The hottest of the sensors this fake actually backs, ignoring the
        // real machine's published sensors and thermal zones
        private static int HottestOfControllerBacked(Platform platform) {

            int hottest = 0;

            for(int i = 0; i < platform.Temperature.Length; i++) {

                if(!platform.TemperatureUse[i] || platform.TemperatureDormant[i])
                    continue;

                if(platform.Temperature[i].GetValue() > hottest)
                    hottest = platform.Temperature[i].GetValue();

            }

            return hottest;

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
        // does nothing, and until now nothing anywhere could tell the
        // difference. This is what "the software completely ignores commands"
        // looks like from inside the software.
        //
        // It is noticed by comparing the next reading against what was asked
        // for, which is the reading the application already takes every
        // second — so no extra access to the hardware, and no asking a
        // firmware what its fans are doing in the same instant it was told to
        // change them.
        private static void TestIgnoredFanWriteIsNoticed() {

            DeviceScenario board = DeviceCatalogue.FanLevelWriteIgnored();

            using(new Installed(board)) {

                int previousDelay = Config.FanLevelVerifyDelayMs;

                try {

                    // The settle window is what stops a slow firmware being
                    // accused; here there is nothing to wait for, and a test
                    // that slept two seconds to prove a comparison would be
                    // paying for the wrong thing
                    Config.FanLevelVerifyDelayMs = 0;
                    Fan.ResetLevelVerification();

                    Platform platform = new Platform();

                    try { platform.Fans.SetLevels(new byte[] { 30, 30 }); } catch { }

                    byte[] readBack = null;
                    try { readBack = platform.Fans.GetLevels(); } catch { }

                    bool took = readBack != null && readBack.Length > 0
                        && readBack[0] == 30;

                    SelfTest.Check(!took,
                        "the board did not take the level, as the scenario declares");

                    // The readings the application takes anyway, which is where
                    // the comparison happens. Two of them, because one on its
                    // own cannot tell a board that refused from fans that are
                    // still spinning up — this board's answer does not move,
                    // and that is what identifies it.
                    platform.Fans.Fan[0].GetLevel();
                    Fan.InvalidateLevels();
                    platform.Fans.Fan[0].GetLevel();

                    SelfTest.Check(Fan.LevelWriteMismatches > 0,
                        "and the readings after it noticed that it had not ("
                            + Fan.LevelWriteMismatches + " mismatch)");

                    // Once per episode. A fan curve rewrites its level
                    // whenever the temperature crosses a threshold, and a
                    // board that ignores one write ignores every one of them.
                    int afterFirst = Fan.LevelWriteMismatches;

                    for(int i = 0; i < 5; i++) {
                        try { platform.Fans.SetLevels(new byte[] { 30, 30 }); } catch { }
                        platform.Fans.Fan[0].GetLevel();
                        Fan.InvalidateLevels();
                        platform.Fans.Fan[0].GetLevel();
                    }

                    SelfTest.Check(Fan.LevelWriteMismatches > afterFirst,
                        "every ignored write is counted");

                    // The settle window exists for the firmware that has not
                    // acted yet, and this is the board that never will. With
                    // the window open, even it must not be accused: what
                    // separates "has not answered" from "will not answer" is
                    // only how long has passed.
                    Config.FanLevelVerifyDelayMs = 60000;
                    Fan.ResetLevelVerification();

                    try { platform.Fans.SetLevels(new byte[] { 44, 44 }); } catch { }

                    for(int i = 0; i < 5; i++) {
                        platform.Fans.Fan[0].GetLevel();
                        Fan.InvalidateLevels();
                    }

                    SelfTest.Equal(0, Fan.LevelWriteMismatches,
                        "a board that has not had time to answer is not accused");

                } finally {
                    Config.FanLevelVerifyDelayMs = previousDelay;
                }

            }

        }

        // The two false alarms this check produced on real hardware, kept as
        // the cases they are.
        //
        // Both came out of an eighteen-minute run on the machine this was
        // written for, and both were the check being wrong rather than the
        // board. The comment above the original said a check that fires on
        // hardware doing as it is told is worse than no check at all; the
        // fake board in the matrix answers instantly and exactly, so the test
        // agreed with the code and neither noticed.
        private static void TestTheTwoFalseAlarmsRealHardwareProduced() {

            int ceiling = 56;

            // "asked for 255/255, the firmware reports 50/50".
            //
            // Nobody asked for level 255. It is the sentinel that clears a
            // custom level and hands the speeds back to the firmware, which is
            // what switching to Automatic does — so there is no level for the
            // board to have taken and nothing to compare against.
            SelfTest.Check(!Fan.DidNotTake(
                    new byte[] { 255, 255 }, new byte[] { 50, 50 },
                    new byte[] { 50, 50 }, ceiling),
                "clearing the custom level is not a write that failed");

            // "asked for 56/56, the firmware reports 40/38".
            //
            // The Maximum button, four seconds in, with the fans still on
            // their way up. The level register reports where they are, not
            // where they were sent.
            SelfTest.Check(!Fan.DidNotTake(
                    new byte[] { 56, 56 }, new byte[] { 40, 38 },
                    new byte[] { 32, 30 }, ceiling),
                "fans still spinning up are not a board refusing the level");

            SelfTest.Check(!Fan.DidNotTake(
                    new byte[] { 56, 56 }, new byte[] { 48, 46 },
                    new byte[] { 40, 38 }, ceiling),
                "nor are they one sample later");

            SelfTest.Check(!Fan.DidNotTake(
                    new byte[] { 56, 56 }, new byte[] { 56, 56 },
                    new byte[] { 48, 46 }, ceiling),
                "and arriving is agreement");

            // A request above the ceiling answered by the ceiling is the
            // firmware doing its job
            SelfTest.Check(!Fan.DidNotTake(
                    new byte[] { 70, 70 }, new byte[] { 56, 56 },
                    new byte[] { 56, 56 }, ceiling),
                "a level clamped to the ceiling is not a refusal");

            // With no previous reading nothing can be concluded, which is the
            // state the very first sample after a write is always in
            SelfTest.Check(!Fan.DidNotTake(
                    new byte[] { 56, 56 }, new byte[] { 40, 38 }, null, ceiling),
                "one sample on its own concludes nothing");

            // And the case that is real: the fans have stopped moving, and
            // they are nowhere near what was asked for
            SelfTest.Check(Fan.DidNotTake(
                    new byte[] { 56, 56 }, new byte[] { 30, 30 },
                    new byte[] { 30, 30 }, ceiling),
                "a level that has settled somewhere else is a write that "
                    + "did not take");

            SelfTest.Check(Fan.DidNotTake(
                    new byte[] { 30, 30 }, new byte[] { 0, 0 },
                    new byte[] { 0, 0 }, ceiling),
                "and so is one the board answered with nothing at all");

        }

        // And the other half, which matters more: a check that fires on a
        // board doing exactly what it was told is worse than no check at all,
        // because the log then says every machine is broken.
        private static void TestAWriteThatTookIsNotComplainedAbout() {

            DeviceScenario board = DeviceCatalogue.Reference();

            using(new Installed(board)) {

                int previousDelay = Config.FanLevelVerifyDelayMs;

                try {

                    Config.FanLevelVerifyDelayMs = 0;
                    Fan.ResetLevelVerification();

                    Platform platform = new Platform();

                    byte[] asked = new byte[] { 30, 30 };
                    try { platform.Fans.SetLevels(asked); } catch { }

                    byte[] readBack = null;
                    try { readBack = platform.Fans.GetLevels(); } catch { }

                    SelfTest.Check(readBack != null && readBack.Length >= 2
                        && readBack[0] == 30 && readBack[1] == 30,
                        "a board that takes the level reports it back");

                    for(int i = 0; i < 5; i++)
                        platform.Fans.Fan[0].GetLevel();

                    SelfTest.Equal(0, Fan.LevelWriteMismatches,
                        "and nothing is complained about");

                } finally {
                    Config.FanLevelVerifyDelayMs = previousDelay;
                }

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

                // A refused lock used to be answered with App.Error on every
                // access, which in the interface is a modal dialog: one per
                // sensor, per tick, for as long as the other application held
                // it. The hardware report reads 256 registers, so it produced
                // 256 of them. It is told once per episode now, and logged
                // every time.
                //
                // Asserted by counting what reaches the user rather than what
                // the controller saw: the lock is genuinely asked for once per
                // access, and that part is correct.
                SelfTest.Check(board.Ec.LockRefusals > 1,
                    "the lock is still asked for on every access ("
                        + board.Ec.LockRefusals + " for one update)");

                SelfTest.Equal(1, Hw.EcLockReports,
                    "and the user is told once rather than once per register");

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

        // -Probe has to produce a report on a machine that is misbehaving,
        // because that is the only kind of machine anybody runs it on. A
        // composer that stops at the first refused call describes least
        // exactly where description matters most.
        private static void TestEveryScenarioCanBeReported() {

            foreach(DeviceScenario scenario in DeviceCatalogue.All()) {

                using(new Installed(scenario)) {

                    string report = null;
                    string failure = null;

                    try {

                        Platform platform = new Platform();
                        report = StarMon.AppCli.CliOp.Compose(platform);

                    } catch(Exception e) {
                        failure = e.GetType().Name + ": " + e.Message;
                    }

                    SelfTest.Check(failure == null && report != null,
                        scenario + " can be reported"
                            + (failure == null ? "" : " - " + failure));

                    if(report == null)
                        continue;

                    // The parts that make the report worth having. A register
                    // dump is the one thing in it that is not this build's
                    // interpretation of the machine.
                    SelfTest.Check(report.IndexOf("Embedded Controller registers",
                            StringComparison.Ordinal) >= 0
                        && report.IndexOf("Named registers", StringComparison.Ordinal) >= 0
                        && report.IndexOf("## Fans", StringComparison.Ordinal) >= 0,
                        scenario + " carries the register dump and the fan table");

                }

            }

            // A board that answers nothing still has to produce a report, and
            // the registers it does not carry have to read as absent rather
            // than as nought - a dump full of zeroes and a dump of a board
            // whose registers are elsewhere look identical otherwise.
            DeviceScenario silent = DeviceCatalogue.SilentController();

            using(new Installed(silent)) {

                string report = StarMon.AppCli.CliOp.Compose(new Platform());

                SelfTest.Check(report.IndexOf("--", StringComparison.Ordinal) >= 0,
                    "a register the board does not answer for is written as absent");

                SelfTest.Check(report.IndexOf("no answer", StringComparison.Ordinal) >= 0,
                    "and named registers say so rather than reporting nought");

            }

        }

    }

}
