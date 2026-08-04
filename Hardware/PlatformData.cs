// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;

namespace StarMon.Hardware.Platform {

    // Holds platform-related data
    public abstract class PlatformData { 

        // Access type flags
        [Flags]
        public enum AccessType {
            None =  0x00,
            Read =  0x01,
            Write = 0x02
        }

        // Data size type
        public enum DataSize {
            Byte =  0x000000FF,
            Word =  0x0000FFFF
        }

        // The most fans this build knows how to address.
        //
        // Not the number this machine has: that is asked of the firmware and
        // kept in DeviceProfile.FanCount. This is only how far the register
        // table below goes, and so how large an answer from the firmware can
        // still be acted on.
        //
        // It used to be a flat FanCount = 2, used both as the array size and
        // as the ceiling on the firmware's own answer — so a board reporting
        // three fans had the third clipped off before anything could look at
        // it, and a board reporting one still got two built.
        public const int MaxFanCount = 4;

        // Which registers belong to which fan.
        //
        // These come from one board's ACPI tables, like the rest of the map in
        // EcData — but written out as a table rather than as two hand-built
        // objects in Platform.InitFans, so that the number of fans can follow
        // the firmware instead of the source.
        //
        // The third and fourth entries are the honest limit of what is known:
        // the tachometers continue in sequence, and the setpoint registers do
        // not appear to. A board with more than two fans is driven through the
        // firmware's own fan-level call, which takes an index and needs no
        // register at all; the EC path is left with nothing to write, which is
        // better than writing to an address picked by guesswork.
        public struct FanRegisters {

            public BiosData.FanType Type;

            // Setpoint: the level being asked for, read back and written
            public EmbeddedControllerData.Register? Setpoint;

            // The rate the fan is actually running at, as a percentage
            public EmbeddedControllerData.Register? Rate;

            // Where a new level is written on boards that take it through the
            // Embedded Controller rather than through the firmware
            public EmbeddedControllerData.Register? Set;

            // Tachometer, a little-endian word across two registers
            public EmbeddedControllerData.Register? Speed;

        }

        public static readonly FanRegisters[] Fan = new FanRegisters[] {

            new FanRegisters {
                Type = BiosData.FanType.Cpu,
                Setpoint = EmbeddedControllerData.Register.SRP1,
                Rate = EmbeddedControllerData.Register.XGS1,
                Set = EmbeddedControllerData.Register.XSS1,
                Speed = EmbeddedControllerData.Register.RPM1
            },

            new FanRegisters {
                Type = BiosData.FanType.Gpu,
                Setpoint = EmbeddedControllerData.Register.SRP2,
                Rate = EmbeddedControllerData.Register.XGS2,
                Set = EmbeddedControllerData.Register.XSS2,

                // Not a mistake, and not RPM2: on the board this map came
                // from, RPM2 is the high half of the first fan's word
                Speed = EmbeddedControllerData.Register.RPM3
            },

            new FanRegisters {
                Type = BiosData.FanType.Exhaust,
                Speed = EmbeddedControllerData.Register.RPM4
            },

            new FanRegisters {
                Type = BiosData.FanType.Intake
            }

        };

        // Fan manual toggle
        public enum FanManual : byte {
            Off = 0x00,
            On  = 0x06
        }

        // Fan on/off switch
        public enum FanSwitch : byte {
            On  = 0x00,
            Off = 0x02
        }

        // Link type
        public enum LinkType {
            EmbeddedController,
            WmiBios,
            Cpu
        }

        // Value trend type
        public enum ValueTrend {
            Descending = -1,
            Unchanged  =  0,
            Ascending  =  1
        }

    }

}
