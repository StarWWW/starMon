// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Text;

namespace StarMon.Test {

    // A dependency-free test runner, invoked with "StarMon -SelfTest".
    //
    // The usual answer here would be a separate xunit project, but the build
    // this repository is set up for cannot restore packages, so a test project
    // would be one nobody could actually run. These tests ship inside the
    // application instead: no framework, no restore, and they exercise the
    // logic that has historically regressed (the EC wait protocol, the fan
    // level ceiling, the fan curve stepping, and the locale key parity).
    //
    // Everything here is pure: no test touches the Embedded Controller, the
    // BIOS, or the kernel driver. Hardware behaviour is stood in for by fakes.
    public static class SelfTest {

        private static int Passed;
        private static int Failed;
        private static string Section = "";

        // Runs every test and returns a process exit code (0 when all passed)
        public static int Run() {

            Passed = 0;
            Failed = 0;

            Console.WriteLine("StarMon self-test");
            Console.WriteLine(new string('=', 60));

            try {
                TestConv.Run();
                TestLocale.Run();
                TestFanCurve.Run();
                TestEmbeddedController.Run();
                TestGraph.Run();
                TestConfig.Run();
                TestDeviceProfile.Run();
                TestService.Run();

                // Reads the real battery through Windows' own report. No
                // elevation, no writes, and it skips itself where there is no
                // battery — but it is the only way to prove the design-capacity
                // fallback works, and on this hardware the health figure has
                // nothing else to stand on.
                TestBattery.Run();
            } catch(Exception e) {
                Console.WriteLine();
                Console.WriteLine("A test threw an unexpected exception:");
                Console.WriteLine(e.ToString());
                Failed++;
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("{0} passed, {1} failed", Passed, Failed);

            return Failed == 0 ? 0 : 1;

        }

#region Assertions
        // Starts a named group of checks
        internal static void Group(string name) {
            Section = name;
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        // Records a check, printing only failures in full
        internal static void Check(bool condition, string description) {
            if(condition) {
                Passed++;
            } else {
                Failed++;
                Console.WriteLine("   FAIL  " + description);
            }
        }

        // Records a check comparing two values, reporting both when they differ
        internal static void Equal(object expected, object actual, string description) {
            bool ok = Equals(expected, actual);
            if(ok) {
                Passed++;
            } else {
                Failed++;
                Console.WriteLine("   FAIL  " + description);
                Console.WriteLine("         expected: " + Describe(expected));
                Console.WriteLine("         actual:   " + Describe(actual));
            }
        }

        private static string Describe(object value) {
            return value == null ? "(null)" : value.ToString();
        }
#endregion

    }

}
