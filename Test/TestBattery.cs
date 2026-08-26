// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware;

namespace StarMon.Test {

    // Exercises the battery reading.
    //
    // Two things worth testing here and both were untested. Sanitise guards
    // against a false zero, which on this hardware was not cosmetic: a
    // momentarily desynced Embedded Controller reported the charge as empty
    // and Windows carried out its critical-battery action, which is to say it
    // shut the machine down. And the design-capacity fallback exists because
    // on this machine WMI will not give that number at all, so the health
    // figure depends entirely on the fallback working.
    //
    // The fallback test does touch the machine, but only by reading: it runs
    // Windows' own battery report, which needs no elevation and changes
    // nothing. It is skipped rather than failed where there is no battery.
    // Reads the real battery through Windows' own report. No elevation, no
    // writes, and it skips itself where there is no battery - but it is the
    // only way to prove the design-capacity fallback works, and on this
    // hardware the health figure has nothing else to stand on. Declared as
    // touching hardware so it runs after everything pure has reported.
    [TestSuite(Order = 90, TouchesHardware = true)]
    public static class TestBattery {

        public static void Run() {

            SelfTest.Group("Battery");

            TestPlausibleReadingsPassStraightThrough();
            TestAFalseZeroOnMainsIsRejected();
            TestASuddenCollapseIsRejected();
            TestRejectionGivesUpEventually();
            TestDesignCapacityIsFoundSomehow();

            SelfTest.Group("Battery: the flag Windows shuts down on");

            TestAFalseCriticalFlagIsRecognised();
            TestARealCriticalFlagIsLeftAlone();

        }

        // The failure this exists to witness.
        //
        // Windows does not read this application's opinion of the battery. It
        // reads the ACPI fuel gauge itself, and when that gauge says critical
        // it does what the power plan says — which by default is to shut the
        // machine down at once, no warning, no undo. On this hardware the
        // gauge can be poisoned, and a laptop that switches itself off at a
        // full battery is otherwise a mystery: the event log records that
        // Windows was told the charge was critical, and not that it was false.
        //
        // Nothing here can prevent it. What it can do is leave a line saying
        // the flag was a lie, in the log, the second before the machine goes.
        private static void TestAFalseCriticalFlagIsRecognised() {

            // Critical while plugged in. Not a state a battery can be in.
            SelfTest.Check(Battery.IsFalseCritical(0x04, 100, true),
                "critical at a full charge on mains is recognised as false");

            SelfTest.Check(Battery.IsFalseCritical(0x04, 87, false),
                "and so is critical at 87 % on battery");

            // Charging and critical at once, which is the exact combination
            // seen when the fuel gauge is poisoned
            SelfTest.Check(Battery.IsFalseCritical(0x0C, 100, true),
                "charging and critical at the same time is false");

        }

        // And the other half, which matters more: a laptop that really is
        // about to die must be allowed to say so. A guard that suppressed a
        // true critical flag would turn a warning into lost work.
        private static void TestARealCriticalFlagIsLeftAlone() {

            SelfTest.Check(!Battery.IsFalseCritical(0x04, 3, false),
                "a genuine critical charge on battery is believed");

            SelfTest.Check(!Battery.IsFalseCritical(0x04, 0, false),
                "and so is an empty one");

            SelfTest.Check(!Battery.IsFalseCritical(0x01, 100, true),
                "a high charge is not a critical flag at all");

            SelfTest.Check(!Battery.IsFalseCritical(0x02, 30, false),
                "nor is a low one");

            // A machine with no battery reports the critical bit alongside
            // the no-battery bit on some firmware, and a desktop is not a
            // laptop about to lie about its charge
            SelfTest.Check(!Battery.IsFalseCritical(0x84, 100, true),
                "a machine with no battery is not accused of anything");

            SelfTest.Check(!Battery.IsFalseCritical(0xFF, 100, true),
                "and neither is one whose state could not be determined");

        }

        // Sanitise holds the last good percentage; every case has to start
        // from a known one, and the run counter has to start at zero
        private static void Seed(int percent) {
            Battery.Sanitise(percent, true, false);
        }

        private static void TestPlausibleReadingsPassStraightThrough() {

            Seed(80);

            SelfTest.Equal(79, Battery.Sanitise(79, true, false),
                "an ordinary reading is passed through unchanged");
            SelfTest.Equal(45, Battery.Sanitise(45, false, false),
                "so is a large but believable fall between readings");
            SelfTest.Equal(0, Battery.Sanitise(0, false, false),
                "and a genuine zero on battery is not second-guessed");

        }

        // The failure that shut the machine down: the controller reports empty
        // while the machine is plugged in and was nearly full a second ago
        private static void TestAFalseZeroOnMainsIsRejected() {

            Seed(96);

            SelfTest.Equal(96, Battery.Sanitise(0, true, false),
                "an empty reading on mains is rejected in favour of the last good one");
            SelfTest.Equal(96, Battery.Sanitise(1, true, true),
                "and so is a near-empty one while charging");

            // Startup case: LastGoodPercent is unseeded (-1)
            Battery.ResetLastGoodPercent();
            SelfTest.Equal(0, Battery.Sanitise(0, true, false),
                "an empty reading at startup on mains returns 0 but does not poison LastGoodPercent");
            SelfTest.Equal(96, Battery.Sanitise(96, true, false),
                "a subsequent good reading is accepted");

        }

        // A drop no battery can physically make between two readings a second
        // apart is a bad reading, not a fast discharge
        private static void TestASuddenCollapseIsRejected() {

            Seed(90);

            SelfTest.Equal(90, Battery.Sanitise(20, false, false),
                "a seventy-point fall in one reading is rejected");

            Seed(90);

            SelfTest.Equal(75, Battery.Sanitise(75, false, false),
                "a fifteen-point fall is believed");

        }

        // The guard cannot hold out for ever: a battery really can end up
        // empty, and refusing to say so is its own failure
        private static void TestRejectionGivesUpEventually() {

            Seed(96);

            int last = 96;
            for(int i = 0; i < 20; i++)
                last = Battery.Sanitise(0, true, false);

            SelfTest.Equal(0, last,
                "a zero that persists is eventually believed rather than held back for ever");

        }

        // The design capacity has to come from somewhere, and on this machine
        // WMI is not that somewhere
        private static void TestDesignCapacityIsFoundSomehow() {

            Battery.Info info = Battery.Get();

            if(!info.Present) {
                SelfTest.Skip("no battery fitted, so capacity cannot be checked here");
                return;
            }

            SelfTest.Check(info.FullmWh > 0,
                "the full-charge capacity is read (" + info.FullmWh + " mWh)");

            SelfTest.Check(info.DesignmWh > 0,
                "the design capacity is found, by WMI or by the battery report ("
                    + info.DesignmWh + " mWh)");

            SelfTest.Check(info.HealthPercent > 0 && info.HealthPercent <= 100,
                "so the health figure can be worked out (" + info.HealthPercent + " %)");

            SelfTest.Check(info.DesignmWh >= info.FullmWh,
                "and a battery cannot hold more than it was designed to");

        }

    }

}
