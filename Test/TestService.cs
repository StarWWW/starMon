// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.AppService;

namespace StarMon.Test {

    // Exercises the application-policy layer: the tick scheduler, the thermal
    // guard and the keyboard backlight modes.
    //
    // All of this used to live inside the tray context, welded to a WinForms
    // timer, balloon tips and live hardware, which is why none of it had ever
    // been tested. It is also the part most worth testing: the thermal guard
    // decides when to force the fans to maximum, and the failure modes are a
    // machine that oscillates at the threshold or one that quietly stops
    // protecting itself.
    [TestSuite(Order = 80)]
    public static class TestService {

        public static void Run() {

            SelfTest.Group("Tick scheduler");
            TestTickerFiresOnItsInterval();
            TestTickerSlotNotAskedStaysReady();
            TestTickerFollowsAChangedInterval();

            SelfTest.Group("Hardware work off the interface thread");
            TestWorkRunsSomewhereElse();
            TestABeatArrivingTooSoonIsDroppedRatherThanQueued();
            TestAFailingBeatDoesNotJamTheNextOne();
            TestTheWayOutWaitsForTheBeatInFlight();

            SelfTest.Group("Thermal guard");
            TestGuardEngagesAndReleasesWithHysteresis();
            TestGuardPanicsOnceWhenStillClimbing();
            TestGuardIgnoresAMissingReading();
            TestGuardReleasesQuietlyWhenSwitchedOff();
            TestThrottleNotificationIsRateLimited();
            TestManualFansNeedAPlausibleReading();
            TestManualFansAreKeptWhenAlreadyAtTheCeiling();

            SelfTest.Group("Fan control");
            TestConstantResolvesToTheRightShape();
            TestTheHardwareStateIsIdentified();

            SelfTest.Group("Fan curve as a program");
            TestCurveSurvivesBeingSavedAndRead();
            TestCurveCoversTemperaturesBelowTheFirstColumn();
            TestCurveReadsTheStepInForce();
            TestCurveClampsToTheCeiling();

            SelfTest.Group("Keyboard backlight");
            TestTemperatureColourSweep();
            TestHueCircleIsContinuous();
            TestIdleWatchSwitchesOffAndBackOn();
            TestIdleWatchStopsAskingWhenAlreadyOff();
            TestIdleWatchRestoresWhenSwitchedOffWhileDark();
            TestBreathingReturnsToItsStart();
            TestColourCycleCompletesALap();

        }

#region Hardware work off the interface thread
        // Waits for a condition rather than sleeping a guessed interval: a
        // fixed sleep is either longer than the tests need or shorter than a
        // loaded build runner takes, and usually both on different machines
        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000) {

            System.Diagnostics.Stopwatch clock =
                System.Diagnostics.Stopwatch.StartNew();

            while(clock.ElapsedMilliseconds < timeoutMs) {
                if(condition())
                    return true;
                System.Threading.Thread.Sleep(5);
            }

            return condition();

        }

        // The point of the whole thing: the work does not run on the thread
        // that asked for it
        private static void TestWorkRunsSomewhereElse() {

            Maintainer maintainer = new Maintainer();

            int caller = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int worker = caller;
            bool ran = false;

            SelfTest.Check(maintainer.Request(delegate {
                worker = System.Threading.Thread.CurrentThread.ManagedThreadId;
                ran = true;
            }), "a beat is taken up");

            SelfTest.Check(WaitFor(() => ran), "and the work runs");

            SelfTest.Check(worker != caller,
                "on a thread other than the one that scheduled it");

            SelfTest.Check(WaitFor(() => !maintainer.IsBusy),
                "and the worker is free again afterwards");

        }

        // A beat arriving while the previous one is still working is dropped.
        //
        // Queueing them is what turns a slow machine into one that never
        // catches up: the work is a live response to a live machine, so a
        // backlog of it has nothing in it anybody wants — the fan level from
        // four seconds ago is not worth writing now.
        private static void TestABeatArrivingTooSoonIsDroppedRatherThanQueued() {

            Maintainer maintainer = new Maintainer();

            System.Threading.ManualResetEvent hold =
                new System.Threading.ManualResetEvent(false);

            int started = 0;

            maintainer.Request(delegate {
                System.Threading.Interlocked.Increment(ref started);
                hold.WaitOne(5000);
            });

            SelfTest.Check(WaitFor(() => maintainer.IsBusy || started > 0),
                "the first beat is under way");

            SelfTest.Check(!maintainer.Request(delegate {
                    System.Threading.Interlocked.Increment(ref started);
                }),
                "a beat arriving while it runs is refused");

            SelfTest.Equal(1, maintainer.Dropped,
                "and counted, since a machine dropping them steadily is one "
                    + "whose hardware is slower than the heartbeat");

            hold.Set();

            SelfTest.Check(WaitFor(() => !maintainer.IsBusy),
                "the first beat finishes");

            SelfTest.Equal(1, started,
                "and the refused one never ran, rather than running late");

            SelfTest.Check(maintainer.Request(delegate { }),
                "the next beat after that is taken up as usual");

            WaitFor(() => !maintainer.IsBusy);

        }

        // An exception in one beat must not leave the worker looking busy
        // forever, which would stop every beat after it silently
        private static void TestAFailingBeatDoesNotJamTheNextOne() {

            Maintainer maintainer = new Maintainer();

            bool threw = false;

            try {
                maintainer.Request(delegate {
                    throw new InvalidOperationException("the controller said no");
                });
            } catch {
                threw = true;
            }

            SelfTest.Check(!threw,
                "a beat that throws does not throw at whoever scheduled it");

            SelfTest.Check(WaitFor(() => !maintainer.IsBusy),
                "and the worker is released rather than left claimed");

            bool ran = false;
            SelfTest.Check(maintainer.Request(delegate { ran = true; }),
                "the beat after a failure is taken up");

            SelfTest.Check(WaitFor(() => ran), "and runs");

        }

        // Shutdown hands the fans back to the firmware. A beat still running
        // while that happens re-asserts what is being cleared — one more level
        // from a fan program, or the sticky fan mode put back — and the
        // machine is left in the state the handback exists to prevent.
        private static void TestTheWayOutWaitsForTheBeatInFlight() {

            Maintainer maintainer = new Maintainer();

            System.Threading.ManualResetEvent hold =
                new System.Threading.ManualResetEvent(false);

            bool finished = false;

            maintainer.Request(delegate {
                hold.WaitOne(5000);
                finished = true;
            });

            SelfTest.Check(WaitFor(() => maintainer.IsBusy),
                "a beat is under way");

            // Released on another thread, so the wait below is a real wait
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                System.Threading.Thread.Sleep(120);
                hold.Set();
            });

            SelfTest.Check(maintainer.Drain(),
                "the way out waits for it rather than giving up");

            SelfTest.Check(finished,
                "and it had genuinely finished before the wait returned");

            SelfTest.Check(maintainer.IsClosed,
                "the worker is closed");

            bool after = false;
            SelfTest.Check(!maintainer.Request(delegate { after = true; }),
                "nothing further is taken up");

            SelfTest.Check(!WaitFor(() => after, 200),
                "and nothing further runs");

            // A worker that has stopped answering must not hold the process
            // open. Drain reports the failure rather than waiting for it.
            Maintainer stuck = new Maintainer();
            System.Threading.ManualResetEvent never =
                new System.Threading.ManualResetEvent(false);

            stuck.Request(delegate { never.WaitOne(3000); });
            WaitFor(() => stuck.IsBusy);

            System.Diagnostics.Stopwatch clock =
                System.Diagnostics.Stopwatch.StartNew();
            bool drained = stuck.Drain(200);
            clock.Stop();

            SelfTest.Check(!drained,
                "a beat that will not finish is reported rather than waited on");

            SelfTest.Check(clock.ElapsedMilliseconds < 1500,
                "and the wait is bounded (" + clock.ElapsedMilliseconds + " ms)");

            never.Set();

        }
#endregion

#region Tick scheduler
        // The work happens on the tick the counter is at zero, and then not
        // again until the interval has run
        private static void TestTickerFiresOnItsInterval() {

            Ticker ticker = new Ticker(5);
            int fired = 0;

            for(int i = 0; i < 20; i++) {
                ticker.Rewind();
                if(ticker.Due())
                    fired++;
            }

            SelfTest.Equal(4, fired, "a slot of interval 5 fires 4 times in 20 ticks");

        }

        // A slot that is rewound but never asked keeps its counter at zero, so
        // it fires immediately the first time it is asked. This is what makes
        // the history recording start on the very tick the window is hidden
        // rather than up to an interval later, and it is the reason the rewind
        // is a separate pass.
        private static void TestTickerSlotNotAskedStaysReady() {

            Ticker ticker = new Ticker(30);

            // Sixty ticks during which this slot is rewound but not asked
            for(int i = 0; i < 60; i++)
                ticker.Rewind();

            SelfTest.Check(ticker.Due(),
                "a slot that was never asked fires the moment it is");

        }

        // The menu lets the user change the monitoring cadence while the
        // application is running, so the interval is not something a slot may
        // capture once and keep
        private static void TestTickerFollowsAChangedInterval() {

            Ticker ticker = new Ticker(30);

            ticker.Rewind();
            ticker.Due();               // fires, counter now 1

            for(int i = 0; i < 4; i++) {
                ticker.Rewind();
                SelfTest.Check(!ticker.Due(), "the slot is quiet inside its interval");
            }

            // Shortened to less than the counter has already reached
            ticker.Interval = 3;
            ticker.Rewind();

            SelfTest.Check(ticker.Due(),
                "shortening the interval below the elapsed count fires at once");

        }
#endregion

#region Thermal guard
        // Engaging at the high mark and releasing at the low one, with nothing
        // happening in the gap between them: a single threshold would make the
        // fans oscillate every time the temperature wandered across it
        private static void TestGuardEngagesAndReleasesWithHysteresis() {

            ThermalGuard guard = new ThermalGuard();

            SelfTest.Equal(ThermalAction.None, guard.Step(true, 80, 90, 75),
                "below the high threshold, nothing happens");

            SelfTest.Equal(ThermalAction.Engage, guard.Step(true, 90, 90, 75),
                "reaching the high threshold engages the guard");

            SelfTest.Equal(ThermalAction.None, guard.Step(true, 80, 90, 75),
                "back inside the gap, the guard holds rather than releasing");

            SelfTest.Equal(ThermalAction.None, guard.Step(true, 76, 90, 75),
                "one degree above the low threshold still holds");

            SelfTest.Equal(ThermalAction.Release, guard.Step(true, 75, 90, 75),
                "reaching the low threshold releases the guard");

            SelfTest.Check(!guard.IsActive, "the guard reports itself released");

        }

        // Once the overrides have been dropped, dropping them again on every
        // following tick would be pointless hardware traffic
        private static void TestGuardPanicsOnceWhenStillClimbing() {

            ThermalGuard guard = new ThermalGuard();

            guard.Step(true, 90, 90, 75);

            SelfTest.Equal(ThermalAction.None, guard.Step(true, 93, 90, 75),
                "three degrees over is not yet a panic");

            SelfTest.Equal(ThermalAction.Panic, guard.Step(true, 94, 90, 75),
                "four degrees over the high threshold panics");

            SelfTest.Equal(ThermalAction.None, guard.Step(true, 96, 90, 75),
                "the panic is not repeated while it still applies");

            // Coming back down and going up again is a new episode
            guard.Step(true, 70, 90, 75);
            guard.Step(true, 90, 90, 75);

            SelfTest.Equal(ThermalAction.Panic, guard.Step(true, 95, 90, 75),
                "a fresh episode can panic again");

        }

        // Zero means the sensors gave nothing back. Treating that as cold
        // would release the guard exactly when it is least safe to.
        private static void TestGuardIgnoresAMissingReading() {

            ThermalGuard guard = new ThermalGuard();
            guard.Step(true, 95, 90, 75);

            SelfTest.Equal(ThermalAction.None, guard.Step(true, 0, 90, 75),
                "a zero reading decides nothing");

            SelfTest.Check(guard.IsActive,
                "a zero reading does not release an engaged guard");

        }

        // The ordinary release only runs while the toggle is still on, so
        // switching protection off mid-episode has to release separately —
        // otherwise the fans stay pinned at maximum with nothing holding them
        private static void TestGuardReleasesQuietlyWhenSwitchedOff() {

            ThermalGuard guard = new ThermalGuard();
            guard.Step(true, 95, 90, 75);

            SelfTest.Equal(ThermalAction.ReleaseQuiet, guard.Step(false, 95, 90, 75),
                "switching protection off releases the override");

            SelfTest.Equal(ThermalAction.None, guard.Step(false, 95, 90, 75),
                "and does not keep releasing it");

        }

        // The throttle status bit toggles rapidly at the thermal limit
        private static void TestThrottleNotificationIsRateLimited() {

            ThermalGuard guard = new ThermalGuard();

            SelfTest.Check(guard.ShouldNotifyThrottle(1000),
                "the first throttle notification is shown");

            SelfTest.Check(!guard.ShouldNotifyThrottle(1000 + 299000),
                "another one a second short of the interval is suppressed");

            SelfTest.Check(guard.ShouldNotifyThrottle(1000 + 300000),
                "one at the full interval is shown again");

        }

        // Letting the Embedded Controller's countdown lapse hands fan control
        // back to its own failsafe, so re-extending it needs a good reason
        private static void TestManualFansNeedAPlausibleReading() {

            ThermalGuard guard = new ThermalGuard();

            SelfTest.Check(guard.SafeToKeepManualFans(60, 90),
                "a cool reading is safe to keep manual fans on");

            SelfTest.Check(!guard.SafeToKeepManualFans(0, 90),
                "no reading is not safe");

            SelfTest.Check(!guard.SafeToKeepManualFans(90, 90),
                "a reading at the protection threshold is not safe");

            guard.Step(true, 95, 90, 75);

            SelfTest.Check(!guard.SafeToKeepManualFans(60, 90),
                "an engaged guard is never safe, whatever the reading");

        }

        // Fans already at the ceiling are not something to take away from a
        // machine that is running hot. The rule above was applied to every
        // manual speed including the top one, so a laptop at 95 °C with the
        // fans pinned flat out had the pinning lapse — the one direction that
        // cannot be argued for on safety grounds, and one that fires routinely
        // because 95 °C is an ordinary load temperature on this hardware.
        private static void TestManualFansAreKeptWhenAlreadyAtTheCeiling() {

            ThermalGuard guard = new ThermalGuard();

            SelfTest.Check(guard.SafeToKeepManualFans(95, 90, 55, 55, 56),
                "fans at the ceiling are kept even above the threshold");

            SelfTest.Check(!guard.SafeToKeepManualFans(95, 90, 20, 20, 56),
                "a low manual speed above the threshold still lapses");

            SelfTest.Check(!guard.SafeToKeepManualFans(95, 90, 55, 20, 56),
                "one fan at the ceiling is not enough");

            SelfTest.Check(guard.SafeToKeepManualFans(60, 90, 20, 20, 56),
                "below the threshold the level does not matter");

            SelfTest.Check(!guard.SafeToKeepManualFans(95, 90, 55, 55, 0),
                "with no known ceiling there is nothing to compare against");

            guard.Step(true, 95, 90, 75);

            SelfTest.Check(!guard.SafeToKeepManualFans(95, 90, 55, 55, 56),
                "an engaged guard still overrides the exemption");

        }
#endregion

#region Fan curve as a program

        // The editor's columns, as the interface uses them
        private static readonly int[] Columns = { 40, 50, 60, 70, 80, 90 };

        private const int Ceiling = 56;

        private static StarMon.Hardware.Platform.FanProgramData Save(int[] percent) {
            return FanCurve.ToProgram("Test", Columns, percent, Ceiling,
                StarMon.Hardware.Bios.BiosData.FanMode.Performance,
                StarMon.Hardware.Bios.BiosData.GpuPowerLevel.Minimum);
        }

        // A curve drawn, saved and read back has to be the curve that was
        // drawn. This is the failure nobody would notice: not a crash, but a
        // machine cooling itself a few points away from the picture on screen.
        //
        // The tolerance is a point, and it is real rather than slack: the
        // levels are whole numbers out of a ceiling of 56, so a percentage
        // that does not land on one of those 57 steps cannot come back
        // unchanged. What matters is that it lands on the nearest one.
        private static void TestCurveSurvivesBeingSavedAndRead() {

            int[] drawn = { 20, 30, 52, 74, 92, 100 };
            int[] read = FanCurve.ReadCurve(Save(drawn), Columns, Ceiling);

            for(int i = 0; i < drawn.Length; i++)
                SelfTest.Check(Math.Abs(read[i] - drawn[i]) <= 1,
                    "column " + Columns[i] + " reads back as it was drawn ("
                        + drawn[i] + " -> " + read[i] + ")");

        }

        // A program with no step below its first column leaves a cold machine
        // with no level at all to follow. The zero-degree key is what prevents
        // that, and it is invisible in the editor, so it is checked here.
        private static void TestCurveCoversTemperaturesBelowTheFirstColumn() {

            StarMon.Hardware.Platform.FanProgramData program =
                Save(new[] { 20, 30, 52, 74, 92, 100 });

            SelfTest.Check(program.Level.ContainsKey(0),
                "a saved curve carries a step at zero degrees");

            SelfTest.Equal(FanCurve.ToLevel(20, Ceiling), program.Level[0][0],
                "and that step holds the level of the lowest column");

            SelfTest.Equal(program.Level[0][0], program.Level[0][1],
                "both fans get the same level, as the firmware drives them");

        }

        // A program written elsewhere - by hand in the configuration file, or
        // by an older version - need not use the editor's columns at all. Each
        // column has to show the step actually in force at that temperature,
        // which is the last one at or below it.
        private static void TestCurveReadsTheStepInForce() {

            System.Collections.Generic.SortedDictionary<byte, byte[]> steps =
                new System.Collections.Generic.SortedDictionary<byte, byte[]>();

            steps[0] = new byte[] { 0, 0 };
            steps[45] = new byte[] { 14, 14 };   // 25 % of 56
            steps[75] = new byte[] { 42, 42 };   // 75 % of 56

            StarMon.Hardware.Platform.FanProgramData program =
                new StarMon.Hardware.Platform.FanProgramData("Odd",
                    StarMon.Hardware.Bios.BiosData.FanMode.Default,
                    StarMon.Hardware.Bios.BiosData.GpuPowerLevel.Minimum, steps);

            int[] read = FanCurve.ReadCurve(program, Columns, Ceiling);

            SelfTest.Equal(0, read[0],
                "a column below every step reads as the step at zero");
            SelfTest.Equal(25, read[1],
                "a column above one step and below the next reads as that step");
            SelfTest.Equal(25, read[2],
                "and so does the next column, until a further step is reached");
            SelfTest.Equal(25, read[3],
                "a step at 75 does not reach the column at 70");
            SelfTest.Equal(75, read[4],
                "the column at 80 reads the step at 75");
            SelfTest.Equal(75, read[5],
                "and the highest column holds it");

        }

        // A percentage cannot ask for a level the firmware would reject, and a
        // level read back against a ceiling of zero cannot divide by it
        private static void TestCurveClampsToTheCeiling() {

            SelfTest.Equal((byte) Ceiling, FanCurve.ToLevel(100, Ceiling),
                "a hundred per cent is the ceiling exactly");
            SelfTest.Equal((byte) Ceiling, FanCurve.ToLevel(140, Ceiling),
                "and more than a hundred is still the ceiling");
            SelfTest.Equal((byte) 0, FanCurve.ToLevel(-20, Ceiling),
                "a negative percentage is zero, not a wrapped byte");

            SelfTest.Equal(0, FanCurve.ToPercent(30, 0),
                "a ceiling of zero reads back as zero rather than dividing by it");
            SelfTest.Equal(100, FanCurve.ToPercent((byte) Ceiling, Ceiling),
                "a level at the ceiling is a hundred per cent");

        }

#endregion

#region Fan control
        // The firmware will not take every combination of levels: it insists
        // at least one fan keeps turning, so both at zero has to be expressed
        // as the off state rather than as levels of zero — which it accepts
        // and then ignores. Both at the ceiling is the same story the other
        // way up.
        private static void TestConstantResolvesToTheRightShape() {

            SelfTest.Equal(ConstantAction.SwitchOff,
                FanControl.ResolveConstant(0, 0, 56),
                "both fans at zero is the off state, not two levels of zero");

            SelfTest.Equal(ConstantAction.SetLevels,
                FanControl.ResolveConstant(0, 20, 56),
                "one fan at zero and one turning is an ordinary pair of levels");

            SelfTest.Equal(ConstantAction.SwitchToMaximum,
                FanControl.ResolveConstant(56, 56, 56),
                "both fans at the ceiling is the maximum state");

            SelfTest.Equal(ConstantAction.SetLevels,
                FanControl.ResolveConstant(56, 55, 56),
                "one short of the ceiling is not the maximum state");

            SelfTest.Equal(ConstantAction.SetLevels,
                FanControl.ResolveConstant(24, 31, 56),
                "anything in between is an ordinary pair of levels");

            // The ceiling is a property of the machine, so a curve or a
            // slider built against a different one must still resolve
            SelfTest.Equal(ConstantAction.SwitchToMaximum,
                FanControl.ResolveConstant(44, 44, 44),
                "the ceiling is whatever the hardware says it is");

        }
        // Reading the setting back out of the hardware.
        //
        // This is the other half of applying one, and it has gone wrong twice
        // in ways that did not look like wrong answers: the selector moved
        // where the user put it and then jumped back a second later, which
        // reads as the application refusing the request.
        //
        // First cause: levels alone were used, and fans held at zero look
        // like fans left to the firmware. Second cause: nonzero levels alone
        // were used, and the firmware's own spinning fans look like levels a
        // user set — so Automatic could never hold. The Embedded Controller's
        // manual toggle is the bit that records whose levels they are.
        private static void TestTheHardwareStateIsIdentified() {

            SelfTest.Equal(FanRequest.Automatic,
                FanControl.Identify(false, false, false, false, 0, 0, 56),
                "no levels, no override and no manual bit is the firmware deciding");

            SelfTest.Equal(FanRequest.Automatic,
                FanControl.Identify(false, false, false, false, 24, 31, 56),
                "the firmware's own spinning fans are still automatic — "
                    + "this is the reading that used to snap the selector back");

            SelfTest.Equal(FanRequest.Constant,
                FanControl.Identify(false, false, true, false, 0, 0, 56),
                "switched off is a constant setting of zero, not automatic");

            SelfTest.Equal(FanRequest.Constant,
                FanControl.Identify(false, false, false, true, 24, 31, 56),
                "levels with the manual bit set are the user's held levels");

            SelfTest.Equal(FanRequest.Maximum,
                FanControl.Identify(false, true, false, false, 24, 31, 56),
                "the firmware's own maximum state is maximum, whatever the levels");

            SelfTest.Equal(FanRequest.Maximum,
                FanControl.Identify(false, false, false, true, 56, 56, 56),
                "manual levels at the ceiling are maximum, which is how it is applied");

            SelfTest.Equal(FanRequest.Automatic,
                FanControl.Identify(false, false, false, false, 56, 56, 56),
                "the firmware flat out under load is not the user's maximum");

            SelfTest.Equal(FanRequest.Program,
                FanControl.Identify(true, false, false, true, 24, 31, 56),
                "a running program outranks whatever the levels say");

            SelfTest.Equal(FanRequest.Program,
                FanControl.Identify(true, true, false, true, 56, 56, 56),
                "and outranks the maximum override too");

            // A machine that reported no ceiling would otherwise make every
            // manual reading look like maximum, since anything is >= zero
            SelfTest.Equal(FanRequest.Constant,
                FanControl.Identify(false, false, false, true, 0, 0, 0),
                "an unknown ceiling does not turn held levels into maximum");

        }
#endregion

#region Keyboard backlight
        // Green when cool, full yellow in the middle, red when hot. The
        // midpoint is the one worth pinning: an obvious implementation fades
        // one channel out as the other fades in and gives a dim olive there.
        private static void TestTemperatureColourSweep() {

            SelfTest.Equal(0x00FF00, BacklightColor.FromTemperature(20),
                "well below the cool mark is pure green");

            SelfTest.Equal(0x00FF00, BacklightColor.FromTemperature(40),
                "at the cool mark is pure green");

            SelfTest.Equal(0xFF0000, BacklightColor.FromTemperature(85),
                "at the hot mark is pure red");

            SelfTest.Equal(0xFF0000, BacklightColor.FromTemperature(100),
                "above the hot mark stays pure red");

            int mid = BacklightColor.FromTemperature(
                (byte) ((BacklightColor.CoolC + BacklightColor.HotC) / 2));

            SelfTest.Check(((mid >> 16) & 0xFF) > 240 && ((mid >> 8) & 0xFF) > 240,
                "the midpoint is full yellow rather than a dim olive");

            SelfTest.Check(BacklightColor.FromTemperature(60) != 0
                && (BacklightColor.FromTemperature(60) & 0xFF) == 0,
                "the sweep never puts blue in");

        }

        // Every 60° step lands on a primary or secondary, and no step is
        // allowed to produce black, which is what an off-by-one in the sector
        // arithmetic looks like
        private static void TestHueCircleIsContinuous() {

            SelfTest.Equal(0xFF0000, BacklightColor.FromHue(0f), "0° is red");
            SelfTest.Equal(0xFFFF00, BacklightColor.FromHue(60f), "60° is yellow");
            SelfTest.Equal(0x00FF00, BacklightColor.FromHue(120f), "120° is green");
            SelfTest.Equal(0x00FFFF, BacklightColor.FromHue(180f), "180° is cyan");
            SelfTest.Equal(0x0000FF, BacklightColor.FromHue(240f), "240° is blue");
            SelfTest.Equal(0xFF00FF, BacklightColor.FromHue(300f), "300° is magenta");

            bool everyStepLit = true;
            for(float h = 0f; h < 360f; h += 3f)
                if(BacklightColor.FromHue(h) == 0)
                    everyStepLit = false;

            SelfTest.Check(everyStepLit, "no step around the circle comes out black");

        }

        // The ordinary case: idle long enough, backlight is on, switch it off;
        // user comes back, switch it on again
        private static void TestIdleWatchSwitchesOffAndBackOn() {

            IdleWatch watch = new IdleWatch();

            SelfTest.Equal(IdleAction.None, watch.Step(60000, 5),
                "one minute idle with a five-minute threshold does nothing");

            SelfTest.Equal(IdleAction.Query, watch.Step(300000, 5),
                "reaching the threshold asks what the backlight is doing");

            SelfTest.Check(watch.Resolve(true),
                "a lit backlight is switched off");

            SelfTest.Check(watch.IsEngaged,
                "the watch remembers that it is the reason it is off");

            SelfTest.Equal(IdleAction.None, watch.Step(400000, 5),
                "staying idle keeps it off without further action");

            SelfTest.Equal(IdleAction.TurnOn, watch.Step(0, 5),
                "input brings the backlight back");

            SelfTest.Check(!watch.IsEngaged, "and the watch lets go");

        }

        // A backlight the user switched off by hand must be left alone — and
        // not re-queried on every one of the following ticks to be told so
        private static void TestIdleWatchStopsAskingWhenAlreadyOff() {

            IdleWatch watch = new IdleWatch();

            SelfTest.Equal(IdleAction.Query, watch.Step(300000, 5),
                "the threshold is reached and the backlight is queried");

            SelfTest.Check(!watch.Resolve(false),
                "an already-dark backlight is not switched off again");

            SelfTest.Check(!watch.IsEngaged,
                "and the watch does not claim responsibility for it");

            SelfTest.Equal(IdleAction.None, watch.Step(400000, 5),
                "the question is not asked again while the user stays away");

            SelfTest.Equal(IdleAction.None, watch.Step(0, 5),
                "input alone does not turn on a backlight the watch never turned off");

            SelfTest.Equal(IdleAction.Query, watch.Step(300000, 5),
                "but after the user has been and gone, it asks afresh");

        }

        // Switching the feature off while the backlight is dark must not
        // strand it that way
        private static void TestIdleWatchRestoresWhenSwitchedOffWhileDark() {

            IdleWatch watch = new IdleWatch();
            watch.Step(300000, 5);
            watch.Resolve(true);

            SelfTest.Equal(IdleAction.TurnOn, watch.Step(400000, 0),
                "disabling the idle timer restores the backlight");

        }

        // Eight ticks is one full breath, so the phase has to come back to
        // where it started rather than drifting
        private static void TestBreathingReturnsToItsStart() {

            BacklightEffect effect = new BacklightEffect { BaseColor = 0xFFFFFF };

            int first = effect.Step(BacklightEffect.Breathe);

            int dimmest = first, brightest = first;
            for(int i = 0; i < 7; i++) {
                int c = effect.Step(BacklightEffect.Breathe);
                if((c & 0xFF) < (dimmest & 0xFF)) dimmest = c;
                if((c & 0xFF) > (brightest & 0xFF)) brightest = c;
            }

            SelfTest.Equal(0f, effect.Phase,
                "eight ticks is one full breath");

            SelfTest.Equal(first, effect.Step(BacklightEffect.Breathe),
                "the ninth tick repeats the first");

            SelfTest.Check((brightest & 0xFF) == 0xFF,
                "the breath reaches full brightness");

            SelfTest.Check((dimmest & 0xFF) >= 0x3F && (dimmest & 0xFF) <= 0x41,
                "and bottoms out at a quarter rather than going dark");

        }

        // Three degrees a tick around a 360° circle: 120 ticks a lap
        private static void TestColourCycleCompletesALap() {

            BacklightEffect effect = new BacklightEffect();

            int first = effect.Step(BacklightEffect.Cycle);

            for(int i = 0; i < 119; i++)
                effect.Step(BacklightEffect.Cycle);

            SelfTest.Equal(first, effect.Step(BacklightEffect.Cycle),
                "the cycle comes back round after 120 ticks");

        }
#endregion

    }

}
