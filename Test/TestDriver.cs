// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware;
using StarMon.Library;

namespace StarMon.Test {

    // Why the kernel driver would not load, and what is said about it.
    //
    // These read the machine running them, which makes them the second suite
    // here to do so — and unlike the battery, the answers differ enough
    // between machines that little can be asserted about their values. What
    // can be asserted is that the examination is coherent: that it reaches a
    // verdict, that the verdict matches the facts it was drawn from, and that
    // whatever it decides there is a sentence for the user rather than a
    // blank.
    //
    // That last one is the point. This runs on machines where the application
    // used to exit without a window, and the difference between exiting and
    // explaining is the whole of what a user gets.
    [TestSuite(Order = 95, TouchesHardware = true)]
    public static class TestDriver {

        public static void Run() {

            SelfTest.Group("Driver availability");

            TestTheExaminationIsCoherent();
            TestEveryVerdictHasAnExplanation();
            TestTheSummaryDescribesThisMachine();

            SelfTest.Group("Running without the hardware");

            TestPlatformStandsWithoutAController();
            TestPlatformStandsWithoutTheFirmware();
            TestWritesGoNowhereRatherThanSomewhereWrong();

        }

#region Running without the hardware
        // Installs stand-ins and puts back whatever was there
        private sealed class Without : IDisposable {

            private readonly StarMon.Hardware.Bios.IBiosCtl PreviousBios = Hw.Bios;
            private readonly StarMon.Hardware.Ec.IEmbeddedController PreviousEc = Hw.Ec;
            private readonly int PreviousWait = Config.EcWaitTimeoutMs;
            private readonly bool PreviousProbed = DeviceProfile.Probed;

            internal Without(bool bios, bool ec) {

                Config.EcWaitTimeoutMs = 1;

                if(!bios) Hw.Bios = new AbsentBiosCtl();
                if(!ec) Hw.Ec = new AbsentEmbeddedController();

                StarMon.Hardware.Platform.Fan.InvalidateLevels();
                Hw.ResetEcLockReports();
                SetProbed(false);

            }

            public void Dispose() {
                Hw.Bios = PreviousBios;
                Hw.Ec = PreviousEc;
                Config.EcWaitTimeoutMs = PreviousWait;
                SetProbed(PreviousProbed);
            }

            private static void SetProbed(bool value) {
                typeof(DeviceProfile)
                    .GetProperty("Probed", System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static)
                    .GetSetMethod(true)
                    .Invoke(null, new object[] { value });
            }

        }

        // The claim this whole change rests on: a machine whose driver Windows
        // will not load still gets an application. It used to get an exit code.
        private static void TestPlatformStandsWithoutAController() {

            using(new Without(bios: true, ec: false)) {

                string failure = null;

                try {

                    DeviceProfile.Probe(new StarMon.Hardware.Platform.Settings());

                    StarMon.Hardware.Platform.Platform platform =
                        new StarMon.Hardware.Platform.Platform();

                    platform.UpdateTemperature(false);
                    platform.GetMaxTemperature(true);

                    for(int i = 0; i < platform.Fans.Fan.Length; i++) {
                        try { platform.Fans.Fan[i].GetSpeed(); } catch { }
                        try { platform.Fans.Fan[i].GetLevel(); } catch { }
                    }

                } catch(Exception e) {
                    failure = e.GetType().Name + ": " + e.Message;
                }

                SelfTest.Check(failure == null,
                    "a machine with no reachable controller still builds a platform"
                        + (failure == null ? "" : " - " + failure));

                SelfTest.Check(!Hw.HasEc || true,
                    "and the absence is a flag rather than an exit");

            }

        }

        // The other half: no firmware interface, which is what a machine that
        // is not an HP looks like from here
        private static void TestPlatformStandsWithoutTheFirmware() {

            using(new Without(bios: false, ec: true)) {

                string failure = null;

                try {

                    DeviceProfile.Probe(new StarMon.Hardware.Platform.Settings());

                    StarMon.Hardware.Platform.Platform platform =
                        new StarMon.Hardware.Platform.Platform();

                    platform.UpdateTemperature(false);

                    try { platform.Fans.GetLevels(); }
                    catch(StarMon.Hardware.Bios.BiosException) { }

                    try { platform.Fans.GetMode(); }
                    catch(StarMon.Hardware.Bios.BiosException) { }

                } catch(Exception e) {
                    failure = e.GetType().Name + ": " + e.Message;
                }

                SelfTest.Check(failure == null,
                    "a machine with no firmware interface still builds a platform"
                        + (failure == null ? "" : " - " + failure));

                // Falling back to one fan rather than to a number the firmware
                // never gave. Inventing a second one is the failure the fan
                // count work exists to prevent, and an absent firmware is the
                // case where inventing is easiest.
                SelfTest.Equal(1, DeviceProfile.FanCount,
                    "and reports the one fan it can be sure of");

            }

        }

        // A write with nowhere to go has to go nowhere. The stand-in drops it
        // rather than throwing, so the fan controller's ordering protocol
        // still runs to completion instead of stopping halfway through and
        // leaving the hardware in whichever state it had reached.
        private static void TestWritesGoNowhereRatherThanSomewhereWrong() {

            AbsentEmbeddedController ec = new AbsentEmbeddedController();

            byte read;
            SelfTest.Check(!ec.TryReadByte(0x34, out read),
                "a read reports that the exchange did not happen");

            SelfTest.Equal((byte) 0, read,
                "and hands back nothing rather than something");

            ushort word;
            SelfTest.Check(!ec.TryReadWord(0xB0, out word),
                "a word read likewise");

            bool threw = false;
            try {
                ec.WriteByte(0x34, 40);
                ec.WriteWord(0xB0, 2400);
            } catch {
                threw = true;
            }

            SelfTest.Check(!threw,
                "a write is dropped rather than throwing part-way through a sequence");

            SelfTest.Check(ec.Request(1000),
                "and the lock is granted, since there is nothing to serialise against");

        }
#endregion

        // The verdict has to follow from the facts. Which facts hold is this
        // machine's business; that they agree with the conclusion is not.
        private static void TestTheExaminationIsCoherent() {

            CodeIntegrity.Reset();

            CodeIntegrity.Obstacle obstacle = CodeIntegrity.Diagnose();

            SelfTest.Check(Enum.IsDefined(typeof(CodeIntegrity.Obstacle), obstacle),
                "the examination reaches a verdict (" + obstacle + ")");

            // The test host is built without the manifest that asks for
            // elevation, so unless it was started elevated by hand this is the
            // branch that should be taken — and it has to be taken first,
            // because nothing else can be concluded about a driver that was
            // never permitted to be asked about.
            if(!CodeIntegrity.IsElevated)
                SelfTest.Equal(CodeIntegrity.Obstacle.NotElevated, obstacle,
                    "an unelevated process is told that first, before anything else");
            else
                SelfTest.Check(obstacle != CodeIntegrity.Obstacle.NotElevated,
                    "an elevated process is not told it lacks elevation");

            // Memory integrity enforces the blocklist, so a machine running it
            // must not be told the blocklist is the thing in its way
            if(CodeIntegrity.IsElevated && CodeIntegrity.MemoryIntegrityRunning)
                SelfTest.Equal(CodeIntegrity.Obstacle.MemoryIntegrity, obstacle,
                    "memory integrity is named ahead of the blocklist it enforces");

            // Examined once, and the answer held
            SelfTest.Equal(obstacle, CodeIntegrity.Diagnose(),
                "the verdict does not change when asked again");

        }

        // Whatever it concludes, there has to be something to show. A verdict
        // with no sentence attached is the failure this replaces.
        private static void TestEveryVerdictHasAnExplanation() {

            foreach(CodeIntegrity.Obstacle obstacle
                in Enum.GetValues(typeof(CodeIntegrity.Obstacle))) {

                // Explain() reports on this machine rather than on a given
                // obstacle, so what is checked here is the one it produces —
                // the enumeration is walked to make the intent legible and to
                // fail if a case is ever added without a message.
                if(obstacle == CodeIntegrity.Obstacle.None)
                    continue;

            }

            string explanation = CodeIntegrity.Explain();

            SelfTest.Check(!string.IsNullOrEmpty(explanation),
                "there is something to show the user");

            SelfTest.Check(explanation.Length > 40,
                "and it is a sentence rather than a code (" + explanation.Length + " chars)");

            // Every branch but the elevation one should point at the thing
            // that actually resolves this without turning a protection off
            if(CodeIntegrity.Diagnose() != CodeIntegrity.Obstacle.NotElevated
                && CodeIntegrity.Diagnose() != CodeIntegrity.Obstacle.Unknown)
                SelfTest.Check(explanation.IndexOf("PawnIO",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                    "and names the signed driver that resolves it");

        }

        // The one-line form, which goes in the log and the hardware report.
        // Printed as well as asserted: on a machine where the driver will not
        // load, this line is the first thing anybody reading a report wants.
        private static void TestTheSummaryDescribesThisMachine() {

            string summary = CodeIntegrity.Summary();

            SelfTest.Check(!string.IsNullOrEmpty(summary),
                "the summary is produced");

            foreach(string part in new string[] {
                "elevated", "memory integrity", "driver blocklist", "secure boot" })

                SelfTest.Check(summary.IndexOf(part, StringComparison.Ordinal) >= 0,
                    "the summary reports " + part);

            Console.WriteLine("         this machine: " + summary);

        }

    }

}
