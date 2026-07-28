// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;

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

        // Number of fans
        public const int FanCount = 2;

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
