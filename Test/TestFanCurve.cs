// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Test {

    // Exercises the fan curve: how a percentage on the curve becomes a fan
    // level, and how the running program steps between levels.
    public static class TestFanCurve {

        public static void Run() {

            SelfTest.Group("Fan curve: level mapping");
            TestCeiling();
            TestDefaultRamp();

            SelfTest.Group("Fan curve: level stepping");
            TestLookUp();
            TestStepsUpImmediately();
            TestHoldsBeforeSteppingDown();
            TestStepsDownOnceClear();
            TestLargeDropCrossesSeveralThresholds();

        }

        // The percentage-to-level conversion the curve editor performs. Kept
        // in step with GuiFormFanCurve deliberately: the property under test
        // is that 100 % lands exactly on the configured ceiling, whatever it
        // is set to, rather than on some separately hard-coded maximum.
        private static byte PctToLevel(int pct, int max) {
            int lv = (int) Math.Round(pct / 100.0 * max);
            return (byte) (lv < 0 ? 0 : lv > max ? max : lv);
        }

        // A full-speed point must map onto the ceiling exactly. Writing a
        // level above what the hardware accepts is silently clamped or
        // ignored by the firmware, so a curve built against an invented
        // maximum never actually reaches full speed.
        private static void TestCeiling() {

            int max = Config.FanLevelMax;

            SelfTest.Check(max > 0 && max <= 255,
                "the configured fan ceiling is a usable byte value");

            SelfTest.Equal((byte) max, PctToLevel(100, max),
                "100 % maps exactly onto the configured ceiling");
            SelfTest.Equal((byte) 0, PctToLevel(0, max),
                "0 % maps onto level zero");

            // Nothing on the curve may ever exceed the ceiling
            for(int pct = 0; pct <= 100; pct++)
                if(PctToLevel(pct, max) > max) {
                    SelfTest.Check(false,
                        "level for " + pct + " % exceeds the ceiling");
                    return;
                }

            SelfTest.Check(true, "no percentage maps above the ceiling");

        }

        // The stock ramp, checked at the stock ceiling. These are the levels
        // the machine was actually tuned against, so a change to either the
        // ramp or the ceiling should be a deliberate one.
        private static void TestDefaultRamp() {

            int[] pct = { 36, 46, 57, 70, 86, 100 };
            byte[] expected = { 20, 26, 32, 39, 48, 56 };

            for(int i = 0; i < pct.Length; i++)
                SelfTest.Equal(expected[i], PctToLevel(pct[i], 56),
                    "default ramp point " + pct[i] + " % maps to level "
                        + expected[i] + " at a ceiling of 56");

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
