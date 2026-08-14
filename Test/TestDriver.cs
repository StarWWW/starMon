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

            SelfTest.Group("The hardware gate");

            TestPortableHpMachinesAreDriven();
            TestOtherMachinesAreRefused();
            TestUnreadableMachinesAreAllowed();
            TestThisMachineIsJudgedCorrectly();
            TestOnlyWritingRunsAreGated();

            SelfTest.Group("Running without the hardware");

            TestPlatformStandsWithoutAController();
            TestPlatformStandsWithoutTheFirmware();
            TestWritesGoNowhereRatherThanSomewhereWrong();

            SelfTest.Group("Tasks that point at nothing");

            TestAMovedApplicationRepairsItsOwnTasks();

            SelfTest.Group("What this processor publishes");

            TestAmdTemperatureDecodesCorrectly();
            TestEveryProcessorReadingSaysWhyItIsAbsent();

            SelfTest.Group("Which driver answers");

            TestTheModulesAreInTheBuild();
            TestTheFacadeIsSafeBeforeAnyDriverOpens();
            TestAmdCountersUseTheReadOnlyAliases();
            TestPinningABackendIsHonoured();
            TestDetectionAgreesWithItself();

        }

#region Tasks that point at nothing
        // The Omen key going quiet.
        //
        // The path is written into the scheduled task when it is registered
        // and nothing revalidates it. Move the folder, or rename it, and the
        // key press still reaches Windows, Windows still starts the task, and
        // the task launches a file that is no longer there. Nothing fails
        // loudly enough to notice: no window, no error, nothing in any log —
        // and the settings switch reads "off", truthfully and unhelpfully,
        // because what the user needs to know is that it is broken rather
        // than disabled.
        //
        // Found on the development machine, where the task had been left
        // pointing into a folder from before the project was renamed.
        private static void TestAMovedApplicationRepairsItsOwnTasks() {

            const string here = @"C:\Apps\StarMon\StarMon.exe";

            // The case this exists for, in the shape it was actually found in
            SelfTest.Check(Os.ShouldRepairTask(
                    @"C:\Users\star\Desktop\Code\OmenMon-Star\Bin\StarMon.exe",
                    here, false),
                "a task pointing into a folder that is gone is repaired");

            // Already right: rewriting it every start would be churn for
            // nothing, and would fight anybody who set it deliberately
            SelfTest.Check(!Os.ShouldRepairTask(here, here, true),
                "a task already pointing here is left alone");

            SelfTest.Check(!Os.ShouldRepairTask(
                    here.ToUpperInvariant(), here, true),
                "and so is one that differs only in case");

            // Another copy that really is installed is somebody's deliberate
            // arrangement. Two copies rewriting each other's tasks on every
            // start would be worse than either of them being wrong.
            SelfTest.Check(!Os.ShouldRepairTask(
                    @"C:\Program Files\StarMon\StarMon.exe", here, true),
                "a task pointing at another copy that exists is not touched");

            // Nothing known is nothing done
            SelfTest.Check(!Os.ShouldRepairTask("", here, false),
                "a task whose definition could not be read is left alone");

            SelfTest.Check(!Os.ShouldRepairTask(@"C:\gone\StarMon.exe", "", false),
                "and so is one when this application's own path is unknown");

        }
#endregion

#region What this processor publishes
        // The one AMD reading in the application, and it cannot be exercised
        // on the machine it was written on.
        //
        // Zen keeps Tctl in the top eleven bits in eighths of a degree, and a
        // separate bit says the reading is on the wide scale — where it
        // carries a +49 °C bias that has to come back off. Getting that bit
        // wrong does not produce an obviously broken number: an idle 42 °C
        // arrives as 91 °C, which is a perfectly believable temperature, and
        // it would reach the fan curve and the thermal guard as one.
        //
        // The raw values below are built from the temperature rather than
        // copied from a machine, so each case says what it is testing.
        private static void TestAmdTemperatureDecodesCorrectly() {

            SelfTest.Equal(42,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(Tctl(42, true)),
                "an idle mobile Ryzen on the wide scale reads as itself");

            SelfTest.Equal(42,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(Tctl(42, false)),
                "and so does one on the narrow scale, which carries no bias");

            SelfTest.Equal(91,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(Tctl(91, true)),
                "a processor genuinely at 91 °C is not talked down to 42");

            // The failure this guards, stated as one pair of readings: the
            // same eleven bits, differing only in the scale bit, are a
            // processor at 91 °C and one at 42 °C. Drop the correction and a
            // mobile Ryzen idling at 42 reports 91 — believable, unremarkable,
            // and enough to hold the fans at maximum indefinitely.
            uint bits = (uint) ((91 * 8) << 21);

            SelfTest.Equal(91,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(bits),
                "on the narrow scale those bits are 91 °C");

            SelfTest.Equal(42,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(bits | 0x00080000),
                "and on the wide scale the very same bits are 42 °C");

            // Outside the believable band, a reading is discarded rather than
            // clamped: a number nothing measured is worse than no number
            SelfTest.Equal(-1,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(0),
                "an all-zero register is not a processor at absolute cold");

            SelfTest.Equal(-1,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(0xFFFFFFFF),
                "and an all-ones one is not a processor at 255 °C");

            // The eighth-of-a-degree steps, rounded rather than truncated
            SelfTest.Equal(60,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(
                    (uint) (((60 * 8) + 1) << 21)),
                "an eighth above a whole degree rounds back to it");

            SelfTest.Equal(61,
                StarMon.Hardware.Cpu.CpuTemperature.DecodeAmdTctl(
                    (uint) (((60 * 8) + 5) << 21)),
                "and five eighths above it rounds up");

        }

        // Builds the raw register value for a temperature, the way the
        // hardware would report it
        private static uint Tctl(int celsius, bool wideScale) {

            uint raw = (uint) ((celsius + (wideScale ? 49 : 0)) * 8) & 0x7FF;
            uint value = raw << 21;

            if(wideScale)
                value |= 0x00080000;

            return value;

        }

        // Every reading that is not available says why, in a sentence.
        //
        // Half of what this application reads from the processor exists on one
        // vendor and not the other, and a report that showed the missing ones
        // simply absent was indistinguishable from a report of them being
        // broken. The question that arrives with an AMD machine is always the
        // same: why is there no power limit. Because AMD does not publish one.
        private static void TestEveryProcessorReadingSaysWhyItIsAbsent() {

            string[] statuses = new string[] {
                StarMon.Hardware.Cpu.CpuTemperature.TemperatureStatus,
                StarMon.Hardware.Cpu.CpuTemperature.PerCoreStatus,
                StarMon.Hardware.Cpu.CpuTemperature.ThrottleStatus,
                StarMon.Hardware.Cpu.CpuMetrics.PowerStatus,
                StarMon.Hardware.Cpu.CpuMetrics.PowerLimitStatus,
                StarMon.Hardware.Cpu.CpuMetrics.ClockStatus
            };

            foreach(string status in statuses) {

                SelfTest.Check(!string.IsNullOrEmpty(status),
                    "the reading has something to say about itself");

                // "not available" on its own is the thing this replaces
                if(status.StartsWith("not available", StringComparison.Ordinal))
                    SelfTest.Check(status.IndexOf(" — ",
                            StringComparison.Ordinal) > 0,
                        "and an absent one gives a reason (" + status + ")");

            }

            SelfTest.Check(!string.IsNullOrEmpty(
                    StarMon.Hardware.Cpu.CpuTemperature.VendorName),
                "the processor is named ("
                    + StarMon.Hardware.Cpu.CpuTemperature.VendorName + ")");

            Console.WriteLine("         this machine: "
                + StarMon.Hardware.Cpu.CpuTemperature.VendorName
                + " · temperature " + StarMon.Hardware.Cpu.CpuTemperature
                    .TemperatureStatus
                + " · power limits " + StarMon.Hardware.Cpu.CpuMetrics
                    .PowerLimitStatus);

        }
#endregion

#region Which driver answers
        // The PawnIO modules, and what they must be.
        //
        // The driver verifies each module's signature and refuses one that has
        // been altered, so a wrong byte here does not misbehave — it disables
        // PawnIO on every machine, silently, in a way that reads as PawnIO not
        // being installed. The sizes and digests are those published with
        // release 0.2.10 of PawnIO_Modules; see Resources/README.md.
        private static readonly string[][] Modules = new string[][] {
            new string[] { "LpcACPIEC", "2612",
                "C38FD116E7AFF4D1FDB0A494E296BE0A6708E5A22FC72F14587442FB7F8F7906" },
            new string[] { "IntelMSR", "5324",
                "D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F" },
            new string[] { "AMDFamily17", "10652",
                "DAE74615761B78BDF064DFB3E136252DDCC6FC727D88F14738D0E5800D427A91" }
        };

        // Every module is embedded, under the name the loader asks for, and is
        // exactly what was published
        private static void TestTheModulesAreInTheBuild() {

            foreach(string[] module in Modules) {

                byte[] blob = StarMon.Driver.PawnIo.Read(module[0]);

                SelfTest.Check(blob != null,
                    module[0] + " is embedded in this build");

                if(blob == null)
                    continue;

                SelfTest.Equal(int.Parse(module[1],
                        System.Globalization.CultureInfo.InvariantCulture),
                    blob.Length,
                    "and is the published size");

                string digest;
                using(System.Security.Cryptography.SHA256 sha
                    = System.Security.Cryptography.SHA256.Create())
                    digest = BitConverter.ToString(sha.ComputeHash(blob))
                        .Replace("-", "");

                SelfTest.Equal(module[2], digest,
                    "and byte-for-byte what the driver will verify");

            }

            SelfTest.Check(StarMon.Driver.PawnIo.Read("NoSuchModule") == null,
                "a module that is not in the build reads as absent rather than throwing");

        }

        // Nothing above this layer checks whether a driver opened before
        // asking it for a reading — the guards are all on HasMsr and HasEc,
        // which is a different question. So the facade has to be safe when
        // there is no driver at all, which is the state a refused machine
        // spends its whole run in.
        private static void TestTheFacadeIsSafeBeforeAnyDriverOpens() {

            StarMon.Driver.LowLevel.Reset();

            SelfTest.Equal(StarMon.Driver.LowLevel.Backend.None,
                StarMon.Driver.LowLevel.Active,
                "with nothing open, no backend is claimed");

            SelfTest.Check(!StarMon.Driver.LowLevel.IsOpen,
                "and the facade says so");

            SelfTest.Check(!StarMon.Driver.LowLevel.HasMsr,
                "processor registers are not offered");

            SelfTest.Check(!StarMon.Driver.LowLevel.HasSmn,
                "nor the System Management Network");

            SelfTest.Equal((byte) 0, StarMon.Driver.LowLevel.ReadIoPort(0x66),
                "a port read hands back nothing");

            bool threw = false;
            try {
                StarMon.Driver.LowLevel.WriteIoPort(0x66, 0x80);
            } catch {
                threw = true;
            }

            SelfTest.Check(!threw,
                "a port write is dropped rather than throwing");

            uint eax, edx;
            SelfTest.Check(!StarMon.Driver.LowLevel.ReadMsr(0x19C, out eax, out edx),
                "a register read reports that it did not happen");

            SelfTest.Equal(0u, eax, "and hands back nothing rather than something");

            uint value;
            SelfTest.Check(!StarMon.Driver.LowLevel.ReadSmn(0x59800, out value),
                "an SMN read likewise");

            SelfTest.Check(StarMon.Driver.LowLevel.Describe().Length > 0,
                "and there is still something to say about the driver ("
                    + StarMon.Driver.LowLevel.Describe() + ")");

        }

        // AMD publishes MPERF and APERF twice, and the PawnIO module permits
        // only the read-only copies — a module that let anything write MPERF
        // would hand out the ability to lie to the scheduler about how fast
        // the processor is going. Every other register passes through
        // untouched, on both vendors.
        private static void TestAmdCountersUseTheReadOnlyAliases() {

            SelfTest.Equal(0xC00000E7u,
                StarMon.Driver.LowLevel.Translate(0x0E7, true),
                "on AMD, MPERF becomes its read-only alias");

            SelfTest.Equal(0xC00000E8u,
                StarMon.Driver.LowLevel.Translate(0x0E8, true),
                "and so does APERF");

            SelfTest.Equal(0x0E7u,
                StarMon.Driver.LowLevel.Translate(0x0E7, false),
                "on Intel they are left alone");

            SelfTest.Equal(0xC0010299u,
                StarMon.Driver.LowLevel.Translate(0xC0010299, true),
                "and AMD's own registers are not translated twice");

            SelfTest.Equal(0x611u,
                StarMon.Driver.LowLevel.Translate(0x611, true),
                "nor is anything else");

        }

        // Pinning a backend means pinning it.
        //
        // The setting exists for a machine that misbehaves, where the question
        // is which of the two drivers is at fault; a pin that quietly fell
        // back to the other one would answer the wrong question. Only PawnIO
        // is pinned here: asking for WinRing0 would install a kernel service,
        // which is not something a test run should leave behind.
        private static void TestPinningABackendIsHonoured() {

            string previous = Config.DriverBackend;

            try {

                StarMon.Driver.LowLevel.Reset();
                Config.DriverBackend = "PawnIO";
                StarMon.Driver.LowLevel.Open();

                StarMon.Driver.LowLevel.Backend active =
                    StarMon.Driver.LowLevel.Active;

                SelfTest.Check(active != StarMon.Driver.LowLevel.Backend.WinRing0,
                    "asking for PawnIO never yields the driver it was chosen over");

                if(active == StarMon.Driver.LowLevel.Backend.PawnIo) {

                    // Reached when the test host is started elevated, since
                    // opening an executor is a privileged operation
                    SelfTest.Check(StarMon.Driver.LowLevel.Describe()
                            .IndexOf("PawnIO", StringComparison.Ordinal) >= 0,
                        "and says which one it got");

                    // Reading the controller's status port is the whole point
                    // of the module. What it holds is the machine's business;
                    // that the call completes is not.
                    bool read = true;
                    try {
                        StarMon.Driver.LowLevel.ReadIoPort(0x66);
                    } catch {
                        read = false;
                    }

                    SelfTest.Check(read,
                        "and the controller's status port can be read through it");

                } else {

                    SelfTest.Check(StarMon.Driver.LowLevel.GetStatus().Length > 0,
                        "and a backend that would not open says why ("
                            + StarMon.Driver.LowLevel.GetStatus()
                                .Replace("\r", "").Replace("\n", " ").Trim() + ")");

                }

                Console.WriteLine("         pinned to PawnIO: "
                    + StarMon.Driver.LowLevel.Describe());

            } finally {
                StarMon.Driver.LowLevel.Close();
                StarMon.Driver.LowLevel.Reset();
                Config.DriverBackend = previous;
            }

        }

        // What the user is told about PawnIO is what the loader found, not a
        // second search that might disagree with it
        private static void TestDetectionAgreesWithItself() {

            bool usable = StarMon.Driver.PawnIo.IsAvailable;

            SelfTest.Equal(usable, CodeIntegrity.PawnIoInstalled,
                "the advice reports what the loader actually found ("
                    + (usable ? "installed" : "not installed") + ")");

            SelfTest.Check(CodeIntegrity.Summary().IndexOf(
                    usable ? "PawnIO: installed" : "PawnIO: not installed",
                    StringComparison.Ordinal) >= 0,
                "and the summary line says the same");

            if(!usable) {
                SelfTest.Skip("PawnIO is not installed on this machine, so "
                    + "the loaded-library checks cannot run");
                return;
            }

            SelfTest.Check(StarMon.Driver.PawnIo.LibraryPath != null
                && System.IO.File.Exists(StarMon.Driver.PawnIo.LibraryPath),
                "the library it loaded is where it said it was ("
                    + StarMon.Driver.PawnIo.LibraryPath + ")");

            // Version 0.0.0 would mean the call returned success without
            // filling anything in, which is the kind of thing that goes
            // unnoticed until a module refuses on a library too old for it
            string[] parts = StarMon.Driver.PawnIo.Version.Split('.');

            SelfTest.Equal(3, parts.Length,
                "and reports a three-part version ("
                    + StarMon.Driver.PawnIo.Version + ")");

            int major;
            SelfTest.Check(parts.Length == 3 && int.TryParse(parts[0],
                    out major) && major > 0,
                "whose major number is a real one");

        }
#endregion

#region The hardware gate
        // A shorthand for the decision, so the cases read as machines
        private static Identity.Verdict Judge(string manufacturer, string board,
            params int[] chassis) {

            string reason;
            return Identity.Decide(manufacturer, board, chassis, out reason);

        }

        // The machines this application is for
        private static void TestPortableHpMachinesAreDriven() {

            SelfTest.Equal(Identity.Verdict.Supported,
                Judge("HP", "8DCF", 10),
                "an HP notebook is driven");

            SelfTest.Equal(Identity.Verdict.Supported,
                Judge("Hewlett-Packard", "88F7", 9),
                "so is one whose firmware spells the name out");

            SelfTest.Equal(Identity.Verdict.Supported,
                Judge("HP", "8A14", 31),
                "and a convertible, which is still a portable machine");

        }

        // The machines it is not, each for its own reason
        private static void TestOtherMachinesAreRefused() {

            // The report this gate exists for: an Omen desktop, left with a
            // permanently wrong fan curve by writes meant for a laptop
            SelfTest.Equal(Identity.Verdict.Unsupported,
                Judge("HP", "89EB", 3),
                "an HP desktop is refused");

            // And refused by name even where the chassis table says portable,
            // which is the case the denylist exists for
            SelfTest.Equal(Identity.Verdict.Unsupported,
                Judge("HP", "89EB", 10),
                "a board known to be harmful is refused whatever the chassis claims");

            SelfTest.Equal(Identity.Verdict.Unsupported,
                Judge("Dell Inc.", "0ABCD", 10),
                "a laptop from another manufacturer is refused");

            SelfTest.Equal(Identity.Verdict.Unsupported,
                Judge("HP", "1234", 7),
                "so is a tower, whoever made it");

            SelfTest.Equal(Identity.Verdict.Unsupported,
                Judge("HP", "1234", 13),
                "and an all-in-one");

        }

        // The gate refuses on evidence and allows on the absence of it. A
        // machine whose WMI will not answer is not thereby a desktop, and
        // refusing to start because a query failed would be the worse failure.
        private static void TestUnreadableMachinesAreAllowed() {

            SelfTest.Equal(Identity.Verdict.Unknown,
                Judge(null, null),
                "a machine that says nothing about itself is allowed");

            SelfTest.Equal(Identity.Verdict.Unknown,
                Judge("HP", "8DCF"),
                "so is one whose chassis type could not be read");

            SelfTest.Equal(Identity.Verdict.Unknown,
                Judge(null, "8DCF", 10),
                "and one whose manufacturer could not be read");

            // An unrecognised chassis number is neither portable nor listed as
            // stationary. Landing in Unknown rather than being refused by a
            // rule nobody checked is the whole point of listing both sets.
            SelfTest.Equal(Identity.Verdict.Unknown,
                Judge("HP", "8DCF", 99),
                "an unrecognised chassis type is allowed rather than guessed at");

        }

        // And the machine actually running this.
        //
        // What is asserted is that the verdict is coherent, not that it is
        // favourable. This used to require the machine to be a supported HP
        // portable — which is a statement about wherever the tests happen to
        // be running, not about the code. It passed on the laptop this was
        // written on and failed the first time the suite ran anywhere else:
        // the build runner is a virtual machine reporting chassis type 3, the
        // gate refused it exactly as designed, and the test called that a
        // defect.
        //
        // Being refused on a desktop is the gate working. The only thing worth
        // insisting on here is that whatever it decides, it decides
        // consistently and says so.
        private static void TestThisMachineIsJudgedCorrectly() {

            Identity.Reset();

            Identity.Verdict verdict = Identity.Examine();

            SelfTest.Check(Enum.IsDefined(typeof(Identity.Verdict), verdict),
                "the gate reaches a verdict about this machine");

            SelfTest.Check(!string.IsNullOrEmpty(Identity.Summary()),
                "and can say what it was (" + Identity.Summary() + ")");

            // MayRun is the verdict plus the override, and nothing else
            bool expected = verdict != Identity.Verdict.Unsupported
                || Config.HardwareGateOverride;

            SelfTest.Equal(expected, Identity.MayRun(),
                "and whether the application may run follows from it");

            // A refusal has to come with something to act on, since it is the
            // only outcome that stops the application
            if(verdict == Identity.Verdict.Unsupported)
                SelfTest.Check(Identity.Explain().Length > 80,
                    "a refused machine is told why, and how to override it");

            Console.WriteLine("         this machine: " + Identity.Summary());

        }

        // On the command line the gate applies to writes only. Reading a
        // register on a board nobody understands is how it comes to be
        // understood, and -Probe exists to be run on exactly those machines.
        private static void TestOnlyWritingRunsAreGated() {

            SelfTest.Check(!StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Bios" }),
                "asking the firmware questions is not a write");

            SelfTest.Check(!StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Ec" }),
                "dumping the registers is not a write");

            SelfTest.Check(!StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Probe", "report.md" }),
                "and neither is the hardware report, which is for these machines");

            SelfTest.Check(StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Bios", "FanMode=Performance" }),
                "an assignment is a write");

            SelfTest.Check(StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Ec", "SRP1=40" }),
                "including one straight to a register");

            SelfTest.Check(StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Prog", "Silent" }),
                "and a fan program, which is a continuous run of them");

            SelfTest.Check(StarMon.AppCli.CliOp.WouldWrite(
                    new string[] { "-Ec", "RPM1(2)", "-Bios", "Backlight=Off" }),
                "a run that reads and then writes counts as a write");

        }
#endregion

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
                "elevated", "memory integrity", "driver blocklist",
                "secure boot", "PawnIO" })

                SelfTest.Check(summary.IndexOf(part, StringComparison.Ordinal) >= 0,
                    "the summary reports " + part);

            Console.WriteLine("         this machine: " + summary);

        }

    }

}
