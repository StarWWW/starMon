// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using Register = StarMon.Hardware.Ec.EmbeddedControllerData.Register;

namespace StarMon.Test.Devices {

    // Machines to run the code against.
    //
    // Every entry here is drawn from a report against this application's
    // upstream, OmenMon, where the same register map and the same compiled
    // assumptions produced the behaviour described. They are written down as
    // scenarios rather than as prose because a machine nobody here owns is
    // otherwise untestable, and "works on every device" is not a claim that
    // can be made from one laptop.
    //
    // What a scenario is not: a simulation of a particular laptop. It is the
    // one property of that laptop which broke something, isolated so it can be
    // checked. A board reporting one fan is here because a board reporting one
    // fan gets a phantom second one; the rest of that machine is irrelevant.
    internal sealed class DeviceScenario {

        // What to call it in a failure message
        internal string Name;

        // The board identifier, where the report carried one
        internal string Board;

        // Where this came from, so a surprising expectation can be traced back
        internal string Source;

        // What this scenario exists to prove
        internal string Property;

        internal FakeEcDevice Ec;
        internal FakeBiosDevice Bios;

        public override string ToString() {
            return Name + (Board == null ? "" : " [" + Board + "]");
        }

    }

    internal static class DeviceCatalogue {

        // The register map compiled into this application, populated with
        // values a healthy machine of that shape would report. Every scenario
        // starts from this and then differs in one stated way.
        internal static FakeEcDevice HealthyEc(byte ceiling = 56) {

            FakeEcDevice ec = new FakeEcDevice();

            // Fan setpoints and their read-back
            ec.Set(Register.SRP1, 0).Set(Register.SRP2, 0);
            ec.Set(Register.XGS1, 0).Set(Register.XGS2, 0);
            ec.Set(Register.XSS1, 0).Set(Register.XSS2, 0);

            // Tachometers. RPM3 rather than RPM2 for the second fan is not a
            // mistake here either - it mirrors Platform.InitFans.
            ec.SetWord(Register.RPM1, 2400);
            ec.SetWord(Register.RPM3, 2600);

            // Controls
            ec.Set(Register.OMCC, 0);   // manual toggle
            ec.Set(Register.XFCD, 0);   // failsafe countdown
            ec.Set(Register.HPCM, 0);   // performance mode
            ec.Set(Register.SFAN, 0);   // fan on/off
            ec.Set(Register.FFFF, 0);   // maximum fan

            // Temperature probes, in the plausible range
            ec.Set(Register.CPUT, 52);
            ec.Set(Register.GPTM, 48);
            ec.Set(Register.RTMP, 44);
            ec.Set(Register.TMP1, 41);
            ec.Set(Register.TNT2, 39);
            ec.Set(Register.TNT3, 38);
            ec.Set(Register.TNT4, 37);
            ec.Set(Register.TNT5, 36);

            return ec;

        }

        internal static FakeBiosDevice HealthyBios(byte ceiling = 56) {

            FakeBiosDevice bios = new FakeBiosDevice();
            bios.Initialize();
            bios.FanTable = FakeBiosDevice.DefaultFanTable(ceiling);
            return bios;

        }

        // Every scenario, built fresh on each call so that one cannot leak
        // into the next through shared state.
        internal static List<DeviceScenario> All() {

            List<DeviceScenario> list = new List<DeviceScenario>();

            list.Add(Reference());
            list.Add(SingleFanBoard());
            list.Add(ThreeFanBoard());
            list.Add(StuckAuxiliarySensor());
            list.Add(FrozenCpuSensor());
            list.Add(FanLevelWriteIgnored());
            list.Add(FanLevelWriteRefused());
            list.Add(NeedsHeartbeat());
            list.Add(EcLockHeldElsewhere());
            list.Add(SilentController());
            list.Add(MissingAuxiliarySensors());
            list.Add(LowCeilingBoard());
            list.Add(HighCeilingBoard());
            list.Add(FirmwareReportsNoFanTable());

            return list;

        }

        // The machine this codebase grew up on: everything present, everything
        // answering. The control against which the others are read.
        internal static DeviceScenario Reference() {
            return new DeviceScenario {
                Name = "Reference board, everything answering",
                Board = "8A14",
                Source = "Hardware/EcData.cs:45 - the DSDT the register map came from",
                Property = "the compiled register map is correct here, so nothing should be stood down",
                Ec = HealthyEc(),
                Bios = HealthyBios()
            };
        }

        // A board with one fan.
        //
        // DeviceProfile asks the firmware how many fans there are and
        // Platform.InitFans builds two regardless, so the second fan's
        // registers are polled forever and the interface shows a fan that is
        // not there reading nought per cent.
        internal static DeviceScenario SingleFanBoard() {

            FakeEcDevice ec = HealthyEc();

            // No second fan means no second set of registers
            ec.Remove(Register.SRP2);
            ec.Remove(Register.XGS2);
            ec.Remove(Register.XSS2);
            ec.RemoveWord(Register.RPM3);

            FakeBiosDevice bios = HealthyBios();
            bios.FanCount = 1;
            bios.FanType = 0x01;                        // CPU fan only
            bios.FanLevel = new byte[] { 0 };
            bios.FanSpeed = new int[] { 2400 };

            return new DeviceScenario {
                Name = "Single-fan board",
                Source = "Hardware/Platform.cs:170-225 against DeviceProfile.cs:196-204",
                Property = "the second fan is neither polled nor shown",
                Ec = ec,
                Bios = bios
            };

        }

        // A board with three fans. PlatformData.FanCount caps the probe at
        // two, so the third is invisible even though the firmware reports it.
        internal static DeviceScenario ThreeFanBoard() {

            FakeEcDevice ec = HealthyEc();
            ec.SetWord(Register.RPM4, 2100);

            FakeBiosDevice bios = HealthyBios();
            bios.FanCount = 3;

            // Distinct per fan, so that a fan reading the wrong index shows up
            // as the wrong number rather than as another zero
            bios.FanLevel = new byte[] { 30, 35, 40 };
            bios.FanSpeed = new int[] { 2400, 2600, 2100 };

            return new DeviceScenario {
                Name = "Three-fan board",
                Source = "Hardware/PlatformData.cs:26 caps the firmware's own answer",
                Property = "a third fan is not silently discarded",
                Ec = ec,
                Bios = bios
            };

        }

        // The failure behind the loudest class of report: an auxiliary probe
        // that answers, plausibly, and never changes - so the hottest-reading
        // check acts on a number that is not a temperature.
        internal static DeviceScenario StuckAuxiliarySensor() {

            FakeEcDevice ec = HealthyEc();
            ec.Set(Register.TNT2, 84);

            return new DeviceScenario {
                Name = "Auxiliary probe stuck at 84 C",
                Source = "OmenMon#97, OmenMon#93 - fans driven up by a probe that never moves",
                Property = "a stuck auxiliary probe does not drive the fans or the thermal guard",
                Ec = ec,
                Bios = HealthyBios()
            };

        }

        // The same shape from the other end: a probe that reads far too low,
        // so the machine looks cold and is not cooled.
        internal static DeviceScenario FrozenCpuSensor() {

            FakeEcDevice ec = HealthyEc();
            ec.Set(Register.CPUT, 15);

            return new DeviceScenario {
                Name = "Processor probe frozen at 15 C",
                Board = "8BAD",
                Source = "OmenMon#76 - CPUT always 15 C, GPTM wrong",
                Property = "a reading that never moves is not trusted as the machine's temperature",
                Ec = ec,
                Bios = HealthyBios()
            };

        }

        // A board that accepts a fan level and does nothing with it. This is
        // the one that reads as "the software completely ignores commands".
        internal static DeviceScenario FanLevelWriteIgnored() {

            FakeBiosDevice bios = HealthyBios();
            bios.Ignore("SetFanLevel");

            return new DeviceScenario {
                Name = "Fan level write accepted and discarded",
                Source = "OmenMon#116, OmenMon#122 - fans do not move, or stick at maximum",
                Property = "a write that does nothing is noticed rather than repeated forever",
                Ec = HealthyEc(),
                Bios = bios
            };

        }

        // A board that refuses the call outright
        internal static DeviceScenario FanLevelWriteRefused() {

            FakeBiosDevice bios = HealthyBios();
            bios.Refuse("SetFanLevel");
            bios.Refuse("GetFanLevel");

            return new DeviceScenario {
                Name = "Fan level call refused by firmware",
                Source = "Hardware/Fan.cs:152-168 - a refusal arriving as -1 was retried forever",
                Property = "a refused call is reported, not retried without end",
                Ec = HealthyEc(),
                Bios = bios
            };

        }

        // 2022 and later boards drop back to firmware defaults unless
        // something keeps asking. The official software runs a thread that
        // queries the fan count for exactly this reason.
        internal static DeviceScenario NeedsHeartbeat() {
            return new DeviceScenario {
                Name = "Board needing a keep-alive",
                Board = "8BB3",
                Source = "OmenMon#68 - performance control reverts after about two minutes",
                Property = "something re-asserts the fan mode inside the revert window",
                Ec = HealthyEc(),
                Bios = HealthyBios()
            };
        }

        // Another application holding the Embedded Controller lock
        internal static DeviceScenario EcLockHeldElsewhere() {

            FakeEcDevice ec = HealthyEc();
            ec.LockAvailable = false;

            return new DeviceScenario {
                Name = "Embedded Controller lock held elsewhere",
                Source = "OmenMon#89 - failed to acquire embedded controller lock",
                Property = "readings are refused cleanly, without a crash and without a hang",
                Ec = ec,
                Bios = HealthyBios()
            };

        }

        // A controller that accepts commands and never answers
        internal static DeviceScenario SilentController() {

            FakeEcDevice ec = HealthyEc();
            ec.Answers = false;

            return new DeviceScenario {
                Name = "Controller accepts commands and never answers",
                Source = "Hardware/Ec.cs:352-380 - the wait protocol's pathological case",
                Property = "every sensor is stood down rather than costing an exchange a second",
                Ec = ec,
                Bios = HealthyBios()
            };

        }

        // A board carrying the two named probes and none of the auxiliaries.
        // The configured sensor list is the union of every register any board
        // has been seen to have, so this is the common case, not the odd one.
        internal static DeviceScenario MissingAuxiliarySensors() {

            FakeEcDevice ec = HealthyEc();
            ec.Remove(Register.TNT2);
            ec.Remove(Register.TNT3);
            ec.Remove(Register.TNT4);
            ec.Remove(Register.TNT5);
            ec.Remove(Register.RTMP);

            return new DeviceScenario {
                Name = "Board without the auxiliary probes",
                Source = "Hardware/Platform.cs:51-62 - what the dormancy mechanism is for",
                Property = "absent probes go dormant and stop being polled",
                Ec = ec,
                Bios = HealthyBios()
            };

        }

        internal static DeviceScenario LowCeilingBoard() {

            FakeBiosDevice bios = HealthyBios(39);
            bios.FanTable = FakeBiosDevice.DefaultFanTable(39);

            return new DeviceScenario {
                Name = "Board with a low fan ceiling",
                Source = "Library/ConfigData.cs:128 - 56 is one machine's number",
                Property = "the ceiling comes from the board, and full speed reaches it",
                Ec = HealthyEc(39),
                Bios = bios
            };

        }

        internal static DeviceScenario HighCeilingBoard() {

            FakeBiosDevice bios = HealthyBios(120);
            bios.FanTable = FakeBiosDevice.DefaultFanTable(120);

            return new DeviceScenario {
                Name = "Board with a high fan ceiling",
                Source = "Hardware/DeviceProfile.cs:84-85 - the probe accepts up to 120",
                Property = "the ceiling comes from the board, and full speed reaches it",
                Ec = HealthyEc(120),
                Bios = bios
            };

        }

        // A board whose firmware will not describe its own fan table. The
        // ceiling then has nothing to be derived from and must not be invented.
        internal static DeviceScenario FirmwareReportsNoFanTable() {

            FakeBiosDevice bios = HealthyBios();
            bios.Refuse("GetFanTable");
            bios.Refuse("GetFanCount");

            return new DeviceScenario {
                Name = "Firmware will not describe its fans",
                Source = "Hardware/DeviceProfile.cs:186-243 - every probe step may fail",
                Property = "the compiled defaults stand, and the application still runs",
                Ec = HealthyEc(),
                Bios = bios
            };

        }

    }

}
