// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.AppService;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Test {

    // Exercises the fan curve: how a percentage on the curve becomes a fan
    // level, and how the running program steps between levels.
    [TestSuite(Order = 30)]
    public static class TestFanCurve {

        public static void Run() {

            SelfTest.Group("Fan curve: level mapping");
            TestCeiling();
            TestDefaultRamp();
            TestOutOfRangePercentages();

            SelfTest.Group("Fan curve: level stepping");
            TestLookUp();
            TestStepsUpImmediately();
            TestHoldsBeforeSteppingDown();
            TestStepsDownOnceClear();
            TestLargeDropCrossesSeveralThresholds();

        }

        // Fan ceilings to check the mapping against.
        //
        // The ceiling is the one number that genuinely differs from board to
        // board — it is read from the machine's own fan table — so the
        // property worth testing is that the mapping holds at any of them,
        // not that it holds at this laptop's. 56 is the value this codebase
        // grew up on and is kept for that reason; the rest bracket what other
        // boards report, up to the 120 the profile probe will accept.
        private static readonly int[] Ceilings = { 25, 39, 44, 56, 68, 90, 120 };

        // A full-speed point must map onto the ceiling exactly. Writing a
        // level above what the hardware accepts is silently clamped or
        // ignored by the firmware, so a curve built against an invented
        // maximum never actually reaches full speed.
        //
        // This used to read Config.FanLevelMax and test whatever that
        // happened to be. Two problems with that: the value is global mutable
        // state that earlier suites write, so the test silently checked
        // whatever the previous file left behind — and it proved the mapping
        // only for one ceiling, which is the opposite of what needs proving.
        private static void TestCeiling() {

            // Still worth asserting, but as a statement about the configured
            // value rather than as the input to the mapping.
            SelfTest.Check(Config.FanLevelMax > 0 && Config.FanLevelMax <= 255,
                "the configured fan ceiling is a usable byte value");

            foreach(int max in Ceilings) {

                SelfTest.Equal((byte) max, FanCurve.ToLevel(100, max),
                    "100 % maps exactly onto a ceiling of " + max);
                SelfTest.Equal((byte) 0, FanCurve.ToLevel(0, max),
                    "0 % maps onto level zero at a ceiling of " + max);

                // Nothing on the curve may ever exceed the ceiling
                bool over = false;
                for(int pct = 0; pct <= 100 && !over; pct++)
                    if(FanCurve.ToLevel(pct, max) > max)
                        over = true;

                SelfTest.Check(!over,
                    "no percentage maps above a ceiling of " + max);

                // The mapping has to be monotonic, or a curve drawn as rising
                // would cool less at a higher temperature somewhere along it
                bool descends = false;
                for(int pct = 1; pct <= 100 && !descends; pct++)
                    if(FanCurve.ToLevel(pct, max) < FanCurve.ToLevel(pct - 1, max))
                        descends = true;

                SelfTest.Check(!descends,
                    "the mapping never descends at a ceiling of " + max);

            }

        }

        // The stock ramp, checked at the stock ceiling. These are the levels
        // the machine was actually tuned against, so a change to either the
        // ramp or the ceiling should be a deliberate one.
        //
        // Calls the production converter. It used to call a copy of it kept
        // in this file, so these points asserted that the copy still agreed
        // with itself — the shipping conversion could have changed underneath
        // without a single check going red.
        private static void TestDefaultRamp() {

            int[] pct = { 36, 46, 57, 70, 86, 100 };
            byte[] expected = { 20, 26, 32, 39, 48, 56 };

            for(int i = 0; i < pct.Length; i++)
                SelfTest.Equal(expected[i], FanCurve.ToLevel(pct[i], 56),
                    "default ramp point " + pct[i] + " % maps to level "
                        + expected[i] + " at a ceiling of 56");

        }

        // A percentage out of range must not produce a level out of range.
        // The editor cannot draw one, but a hand-edited configuration file
        // can hold one, and that path reaches the same converter.
        private static void TestOutOfRangePercentages() {

            foreach(int max in Ceilings) {

                SelfTest.Equal((byte) max, FanCurve.ToLevel(150, max),
                    "a percentage above 100 clamps to the ceiling of " + max);
                SelfTest.Equal((byte) 0, FanCurve.ToLevel(-40, max),
                    "a negative percentage clamps to zero at a ceiling of " + max);

            }

            // A board that reports no usable ceiling must not produce a level
            // at all, rather than dividing by it
            SelfTest.Equal((byte) 0, FanCurve.ToLevel(100, 0),
                "a ceiling of zero yields level zero");
            SelfTest.Equal(0, FanCurve.ToPercent(40, 0),
                "a ceiling of zero yields nought per cent");

        }

        // The thresholds of a typical curve
        private static List<byte> Levels() {
            return new List<byte> { 0, 40, 50, 60, 70, 80, 90 };
        }

        private static void TestLookUp() {

            List<byte> levels = Levels();

            SelfTest.Equal((byte) 0, FanProgram.LookUpLevel(levels, 20),
                "a temperature below the first threshold takes the lowest level");
            SelfTest.Equal((byte) 60, FanProgram.LookUpLevel(levels, 60),
                "a temperature exactly on a threshold takes that threshold");
            SelfTest.Equal((byte) 60, FanProgram.LookUpLevel(levels, 69),
                "a temperature between thresholds takes the one below it");
            SelfTest.Equal((byte) 90, FanProgram.LookUpLevel(levels, 120),
                "a temperature above the last threshold takes the highest level");

        }

        // Cooling that is asked for is delivered at once: hysteresis must
        // never delay the fans on the way up.
        private static void TestStepsUpImmediately() {

            List<byte> levels = Levels();
            byte held;

            FanProgram.StepLevel(levels, FanProgram.HeldNone, 45, 3, out held);
            SelfTest.Equal((byte) 40, held,
                "the first reading picks its level with nothing held");

            byte level = FanProgram.StepLevel(levels, 40, 71, 3, out held);
            SelfTest.Equal((byte) 70, level,
                "a rise past a threshold steps up straight away");
            SelfTest.Equal((byte) 70, held,
                "the new level becomes the held one");

        }

        // The failure this prevents: a temperature sitting on a boundary
        // flipping the level on every single update, which the user hears as
        // the fans surging up and down every few seconds.
        private static void TestHoldsBeforeSteppingDown() {

            List<byte> levels = Levels();
            byte held;

            // Held at 70, temperature drops just below the threshold
            byte level = FanProgram.StepLevel(levels, 70, 69, 3, out held);

            SelfTest.Equal((byte) 70, level,
                "a drop of one degree below the threshold holds the level");
            SelfTest.Equal((byte) 70, held,
                "the held level is unchanged");

            // Still inside the margin
            level = FanProgram.StepLevel(levels, 70, 68, 3, out held);
            SelfTest.Equal((byte) 70, level,
                "a drop still inside the margin holds the level");

        }

        private static void TestStepsDownOnceClear() {

            List<byte> levels = Levels();
            byte held;

            byte level = FanProgram.StepLevel(levels, 70, 66, 3, out held);

            SelfTest.Equal((byte) 60, level,
                "a drop clear of the margin steps down");
            SelfTest.Equal((byte) 60, held,
                "the lower level becomes the held one");

        }

        // A sharp fall should settle at the level the temperature actually
        // warrants, not creep down one threshold per update.
        private static void TestLargeDropCrossesSeveralThresholds() {

            List<byte> levels = Levels();
            byte held;

            byte level = FanProgram.StepLevel(levels, 90, 42, 3, out held);

            SelfTest.Equal((byte) 0, level,
                "a fall from 90 to 42 with a 3 degree margin settles at the "
                    + "level for 39, not one threshold down");

            // With no margin the behaviour is the plain curve look-up
            level = FanProgram.StepLevel(levels, 90, 42, 0, out held);
            SelfTest.Equal((byte) 40, level,
                "a zero margin follows the curve exactly in both directions");

        }

    }

}
