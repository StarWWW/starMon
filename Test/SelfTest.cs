// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using StarMon.Library;

namespace StarMon.Test {

    // Marks a class as a group of tests for the runner to find.
    //
    // The runner used to hold a hand-written list of TestXxx.Run() calls. A
    // suite added to the project but left out of that list did not fail, did
    // not warn, and did not run — it simply was not there, and the count at
    // the end looked as healthy as ever. Discovery removes that failure mode:
    // a class carrying this attribute runs because it exists.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class TestSuiteAttribute : Attribute {

        // Runs in ascending order; ties break on the class name so a run is
        // reproducible. Order matters here only because some suites leave
        // global configuration altered and restore it themselves — see
        // TestDeviceProfile.ProfileState.
        public int Order { get; set; }

        // Set where a suite reads the machine it is running on. Those go last,
        // after everything pure has had its say, so that a run on hardware
        // that answers strangely still reports every other result first.
        public bool TouchesHardware { get; set; }

    }

    // A dependency-free test runner, invoked with "StarMon -SelfTest".
    //
    // The usual answer here would be a separate xunit project, but the build
    // this repository is set up for cannot restore packages, so a test project
    // would be one nobody could actually run. These tests ship inside the
    // application instead: no framework, no restore, and they exercise the
    // logic that has historically regressed (the EC wait protocol, the fan
    // level ceiling, the fan curve stepping, and the locale key parity).
    //
    // Everything here is pure with one stated exception: no test touches the
    // Embedded Controller, the BIOS, or the kernel driver. Hardware behaviour
    // is stood in for by fakes. TestBattery reads the real battery, read-only,
    // and declares itself with TouchesHardware.
    public static class SelfTest {

        private static int Passed;
        private static int Failed;

        // Checks that could not run here rather than checks that passed.
        //
        // These used to be recorded with Check(true, "... skipped"), which
        // counted them among the passes: a run where the source-scanning
        // checks found no repository to scan reported the same clean total as
        // a run where they scanned it and agreed. A skip is not a pass, and
        // the summary now says so.
        private static int Skipped;
        private static readonly List<string> Skips = new List<string>();

        // Expectations the code does not meet yet.
        //
        // A device matrix written against machines nobody here owns turns up
        // behaviour that is wrong and not yet fixed. Deleting those checks
        // loses the finding; leaving them failing makes every run red and
        // teaches everyone to ignore it. So they are recorded as gaps, listed
        // at the end, and counted apart from both passes and failures.
        //
        // The important half: a gap that starts holding is a *failure*. Work
        // that lands has to retire its own marker, or the suite goes on
        // excusing something that no longer needs excusing - and the next
        // person reads the gap list as a description of the code and is wrong.
        private static int Gaps;
        private static readonly List<string> GapList = new List<string>();

        // Failures are printed as they happen and again at the end. A long run
        // scrolls the first occurrence off the screen, and the summary is the
        // part anyone actually reads.
        private static readonly List<string> Failures = new List<string>();

        // The group a check belongs to, for that summary
        private static string Section = "";

        // Runs every test and returns a process exit code (0 when all passed).
        //
        // "filter" restricts the run to suites and groups whose name contains
        // it, case-insensitively. Passing null or an empty string runs
        // everything.
        public static int Run(string filter = null) {

            Passed = 0;
            Failed = 0;
            Skipped = 0;
            Gaps = 0;
            Failures.Clear();
            Skips.Clear();
            GapList.Clear();
            Section = "";

            Console.WriteLine("StarMon self-test");
            if(!string.IsNullOrEmpty(filter))
                Console.WriteLine("Filter: " + filter);
            Console.WriteLine(new string('=', 60));

            var suites = Discover();

            if(suites.Count == 0) {
                Console.WriteLine();
                Console.WriteLine("No test suites were found. This is a defect in the runner,");
                Console.WriteLine("not a clean run: something has to be there.");
                return (int) Config.ExitStatus.ErrorSelfTest;
            }

            Stopwatch total = Stopwatch.StartNew();
            int ran = 0;

            foreach(var suite in suites) {

                if(!Matches(suite, filter))
                    continue;

                ran++;

                // Each suite is isolated. One suite throwing used to abort the
                // whole chain, so a failure in the first file meant the last
                // seven never ran — and the exit code said only "1", the same
                // as a single failed check.
                try {

                    suite.Invoke(null, null);

                } catch(TargetInvocationException e) {

                    ReportThrow(suite, e.InnerException ?? e);

                } catch(Exception e) {

                    ReportThrow(suite, e);

                }

            }

            total.Stop();

            Console.WriteLine();
            Console.WriteLine(new string('=', 60));

            if(Skips.Count > 0) {
                Console.WriteLine("Skipped:");
                foreach(string skip in Skips)
                    Console.WriteLine("  " + skip);
                Console.WriteLine();
            }

            if(GapList.Count > 0) {
                Console.WriteLine("Known gaps - expectations the code does not meet yet:");
                foreach(string gap in GapList)
                    Console.WriteLine("  " + gap);
                Console.WriteLine();
            }

            if(Failures.Count > 0) {
                Console.WriteLine("Failures:");
                foreach(string failure in Failures)
                    Console.WriteLine("  " + failure);
                Console.WriteLine();
            }

            Console.WriteLine("{0} passed, {1} failed, {2} skipped, {3} known gap{4}, "
                    + "{5} suite{6} in {7} ms",
                Passed, Failed, Skipped, Gaps, Gaps == 1 ? "" : "s",
                ran, ran == 1 ? "" : "s", total.ElapsedMilliseconds);

            if(ran == 0 && !string.IsNullOrEmpty(filter)) {
                Console.WriteLine();
                Console.WriteLine("Nothing matched '" + filter + "'. Available suites:");
                foreach(var suite in suites)
                    Console.WriteLine("  " + NameOf(suite));

                // A filter that selects nothing is a mistyped filter, and
                // reporting success for it is how a typo turns into a build
                // that tests nothing while still going green.
                return (int) Config.ExitStatus.ErrorSelfTest;
            }

            return Failed == 0
                ? (int) Config.ExitStatus.NoError
                : (int) Config.ExitStatus.ErrorSelfTest;

        }

#region Discovery
        // Every [TestSuite] class in this assembly, in the order to run them
        private static List<MethodInfo> Discover() {

            var found = new List<Tuple<TestSuiteAttribute, MethodInfo>>();

            foreach(Type type in Assembly.GetExecutingAssembly().GetTypes()) {

                var attribute = (TestSuiteAttribute)
                    Attribute.GetCustomAttribute(type, typeof(TestSuiteAttribute));

                if(attribute == null)
                    continue;

                MethodInfo run = type.GetMethod("Run",
                    BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);

                // A suite that declares itself and then offers nothing to call
                // is a mistake worth surfacing rather than skipping quietly.
                if(run == null) {
                    Failed++;
                    Failures.Add(type.Name + ": marked [TestSuite] but has no public static Run()");
                    continue;
                }

                found.Add(Tuple.Create(attribute, run));

            }

            return found
                .OrderBy(f => f.Item1.TouchesHardware)
                .ThenBy(f => f.Item1.Order)
                .ThenBy(f => f.Item2.DeclaringType.Name, StringComparer.Ordinal)
                .Select(f => f.Item2)
                .ToList();

        }

        // Whether a suite is selected by the filter. Matched against the class
        // name so that "-SelfTest service" reaches TestService; group names
        // are not known until the suite runs, so they cannot be matched here.
        private static bool Matches(MethodInfo suite, string filter) {

            return string.IsNullOrEmpty(filter)
                || NameOf(suite).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        }

        private static string NameOf(MethodInfo suite) {
            return suite.DeclaringType.Name;
        }

        private static void ReportThrow(MethodInfo suite, Exception e) {

            Failed++;
            Failures.Add(NameOf(suite) + " threw " + e.GetType().Name + ": " + e.Message);

            Console.WriteLine();
            Console.WriteLine("   THREW " + NameOf(suite));
            Console.WriteLine(e.ToString());

        }
#endregion

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
                Fail(description);
            }
        }

        // Records a check comparing two values, reporting both when they differ
        internal static void Equal(object expected, object actual, string description) {
            if(Equals(expected, actual)) {
                Passed++;
            } else {
                Fail(description);
                Console.WriteLine("         expected: " + Describe(expected));
                Console.WriteLine("         actual:   " + Describe(actual));
            }
        }

        // Records an expectation the code is known not to meet yet.
        //
        // Not a pass and not a failure while it holds false. When it starts
        // holding true, that is a failure: the work has landed and the marker
        // has to go, or the gap list stops describing the code.
        internal static void Gap(bool condition, string description) {

            if(condition) {

                Failed++;
                string message = description
                    + " - this now holds; replace Gap with Check";
                Failures.Add((Section.Length > 0 ? Section + ": " : "") + message);
                Console.WriteLine("   FIXED " + message);

            } else {

                Gaps++;
                GapList.Add((Section.Length > 0 ? Section + ": " : "") + description);
                Console.WriteLine("   GAP   " + description);

            }

        }

        // Records a check that could not run in this environment. Counted
        // apart from the passes, and listed at the end, so that a run which
        // quietly checked less than usual says so.
        internal static void Skip(string description) {
            Skipped++;
            Skips.Add((Section.Length > 0 ? Section + ": " : "") + description);
            Console.WriteLine("   SKIP  " + description);
        }

        private static void Fail(string description) {
            Failed++;
            Failures.Add((Section.Length > 0 ? Section + ": " : "") + description);
            Console.WriteLine("   FAIL  " + description);
        }

        private static string Describe(object value) {
            return value == null ? "(null)" : value.ToString();
        }
#endregion

    }

}
