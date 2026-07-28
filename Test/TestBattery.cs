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
    public static class TestBattery {

        public static void Run() {

            SelfTest.Group("Battery");

            TestPlausibleReadingsPassStraightThrough();
            TestAFalseZeroOnMainsIsRejected();
            TestASuddenCollapseIsRejected();
            TestRejectionGivesUpEventually();
            TestDesignCapacityIsFoundSomehow();

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
                SelfTest.Check(true, "no battery fitted, so capacity is not expected");
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
