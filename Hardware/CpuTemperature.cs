// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Globalization;
using Microsoft.Win32;
using StarMon.Driver;
using StarMon.External;

namespace StarMon.Hardware.Cpu {

    // Provides an accurate, processor-native CPU temperature reading
    public static class CpuTemperature {

        // Recognized CPU vendors
        private enum Vendor { Unknown, Intel, Amd }

        // Current CPU throttling reasons (Intel package thermal status MSR)
        [Flags]
        public enum ThrottleFlags {
            None       = 0,
            Thermal    = 1, // Temperature reached the thermal control threshold
            PowerLimit = 2  // Frequency capped by a running power limit (RAPL/PL)
        }

        // Detected processor characteristics
        // (obtained from the registry, so no driver call is needed for detection)
        private static Vendor DetectedVendor;
        private static int Family;
        private static int Model;
        private static bool Detected;

        // Cached Intel junction temperature [°C], 0 until first determined
        private static int IntelTjMax;

        // Serializes the two-step AMD SMN index/data access
        private static readonly object AmdLock = new object();

        // Intel Model-Specific Register addresses
        private const uint MSR_IA32_THERM_STATUS         = 0x019C; // Per-core thermal status
        private const uint MSR_IA32_TEMPERATURE_TARGET   = 0x01A2; // Holds TjMax in bits 23:16
        private const uint MSR_IA32_PACKAGE_THERM_STATUS = 0x01B1; // Package-wide thermal status

        // AMD host-bridge PCI configuration access (bus 0, device 0, function 0)
        private const uint AMD_PCI_ADDRESS   = 0x00000000;
        private const uint AMD_SMN_INDEX_REG = 0x60;
        private const uint AMD_SMN_DATA_REG  = 0x64;

        // AMD family 17h (Zen) and later SMU thermal register (Tctl)
        private const uint AMD_F17H_THM_TCTL = 0x00059800;

        // Lowest AMD family number (decimal, as reported by the registry)
        // that exposes the Zen-style SMU thermal register: 23 == 0x17
        private const int AMD_ZEN_FAMILY_MIN = 23;

        // Plausible temperature bounds used to reject bogus readings [°C]
        private const int TempMin = 5;
        private const int TempMax = 125;

        // Whether this processor is supported by the MSR / SMU temperature
        // path; independent of the kernel driver state, since the driver is
        // loaded lazily (the actual read in GetTemperature() guards on it)
        public static bool IsAvailable {
            get {
                EnsureDetected();
                return DetectedVendor == Vendor.Intel
                    || (DetectedVendor == Vendor.Amd && Family >= AMD_ZEN_FAMILY_MIN);
            }
        }

        // Returns the current CPU temperature in degrees Celsius,
        // or -1 if it could not be determined for any reason
        public static int GetTemperature() {
            EnsureDetected();

            // The kernel driver is required for register access
            if(!Ring0.IsOpen)
                return -1;

            try {
                switch(DetectedVendor) {
                    case Vendor.Intel:
                        return GetIntelTemperature();
                    case Vendor.Amd:
                        return GetAmdTemperature();
                    default:
                        return -1;
                }
            } catch {
                return -1;
            }
        }

        // Detects the CPU vendor, family and model from the registry exactly once
        private static void EnsureDetected() {
            if(Detected)
                return;
            Detected = true;
            DetectedVendor = Vendor.Unknown;

            try {
                using(RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) {

                    if(key == null)
                        return;

                    string id = (key.GetValue("VendorIdentifier") as string ?? "").Trim();
                    if(id.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                        DetectedVendor = Vendor.Intel;
                    else if(id.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0
                         || id.IndexOf("Authentic", StringComparison.OrdinalIgnoreCase) >= 0)
                        DetectedVendor = Vendor.Amd;

                    // "Identifier" looks like "Intel64 Family 6 Model 165 Stepping 2"
                    // or "AMD64 Family 25 Model 80 Stepping 0" (all values in decimal)
                    string identifier = key.GetValue("Identifier") as string ?? "";
                    Family = ParseValueAfter(identifier, "Family");
                    Model  = ParseValueAfter(identifier, "Model");

                }
            } catch {
                DetectedVendor = Vendor.Unknown;
            }
        }

        // Parses the integer that immediately follows a keyword
        // within a space-separated descriptor string
        private static int ParseValueAfter(string text, string keyword) {
            try {
                int idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if(idx < 0)
                    return 0;

                string rest = text.Substring(idx + keyword.Length).TrimStart();
                int end = 0;
                while(end < rest.Length && char.IsDigit(rest[end]))
                    end++;

                return end > 0 ?
                    int.Parse(rest.Substring(0, end), CultureInfo.InvariantCulture) : 0;
            } catch {
                return 0;
            }
        }

        // Intel: read the Digital Thermal Sensor relative to the junction temperature
        private static int GetIntelTemperature() {
            int tjMax = GetIntelTjMax();

            // Prefer the package-wide reading (identical on every core, one access)
            int temp = DecodeIntelThermal(MSR_IA32_PACKAGE_THERM_STATUS, tjMax);
            if(temp > 0)
                return temp;

            // Fall back to the thermal status of whichever core serviced the call
            return DecodeIntelThermal(MSR_IA32_THERM_STATUS, tjMax);
        }

        // Returns the live CPU throttling status from the Intel package thermal
        // status MSR: bit 0 = thermal status (at the thermal threshold), bit 10 =
        // power limitation status (capped by a power limit). Returns None on
        // anything else (AMD, no driver, read failure).
        public static ThrottleFlags GetThrottleStatus() {
            EnsureDetected();
            if(!Ring0.IsOpen || DetectedVendor != Vendor.Intel)
                return ThrottleFlags.None;

            try {
                if(!Ring0.ReadMsr(MSR_IA32_PACKAGE_THERM_STATUS, out uint eax, out _))
                    return ThrottleFlags.None;

                ThrottleFlags flags = ThrottleFlags.None;
                if((eax & 0x1) != 0)      // bit 0: thermal status (now)
                    flags |= ThrottleFlags.Thermal;
                if((eax & 0x400) != 0)    // bit 10: power limitation status (now)
                    flags |= ThrottleFlags.PowerLimit;
                return flags;
            } catch {
                return ThrottleFlags.None;
            }
        }

        // Reads the temperature of every physical core (Intel only), in °C, by
        // pinning the calling thread to one logical processor of each core in turn
        // and reading its per-core Digital Thermal Sensor. Returns null when not
        // supported; individual entries are -1 when that core could not be read.
        public static int[] GetPerCoreTemperatures() {
            EnsureDetected();
            if(!Ring0.IsOpen || DetectedVendor != Vendor.Intel)
                return null;

            ulong[] masks = Topology.GetPhysicalCoreMasks();
            if(masks == null || masks.Length == 0)
                return null;

            int tjMax = GetIntelTjMax();
            int[] result = new int[masks.Length];

            System.Threading.Thread.BeginThreadAffinity();
            IntPtr thread = Kernel32.GetCurrentThread();
            UIntPtr previous = Kernel32.SetThreadAffinityMask(thread, (UIntPtr)masks[0]);
            try {
                for(int i = 0; i < masks.Length; i++) {
                    Kernel32.SetThreadAffinityMask(thread, (UIntPtr)masks[i]);
                    result[i] = ReadIntelCoreTemperature(tjMax);
                }
            } catch {
                return null;
            } finally {
                if(previous != UIntPtr.Zero)
                    Kernel32.SetThreadAffinityMask(thread, previous);
                System.Threading.Thread.EndThreadAffinity();
            }

            return ExpandToLogical(result, masks);
        }

        // Expands one-temperature-per-core into one-per-logical-processor, in
        // logical processor order, the way the task manager numbers them.
        //
        // The sensor is physically per core, so a hyperthreaded core's two
        // logical processors show the same figure — which is the truth: both
        // threads run on that one piece of silicon. On a hybrid part this
        // makes the strip's length match the processor count everyone knows
        // ("12 threads") instead of a core count nobody recognises, and the
        // P-cores' pairs read as pairs.
        private static int[] ExpandToLogical(int[] perCore, ulong[] masks) {

            int highest = -1;
            for(int i = 0; i < masks.Length; i++)
                for(int bit = 0; bit < 64; bit++)
                    if((masks[i] & (1UL << bit)) != 0 && bit > highest)
                        highest = bit;

            if(highest < 0)
                return perCore;

            int[] logical = new int[highest + 1];
            for(int i = 0; i < logical.Length; i++)
                logical[i] = -1;

            for(int i = 0; i < masks.Length; i++)
                for(int bit = 0; bit <= highest; bit++)
                    if((masks[i] & (1UL << bit)) != 0)
                        logical[bit] = perCore[i];

            return logical;
        }

        // Reads and decodes the per-core thermal status of the current core
        private static int ReadIntelCoreTemperature(int tjMax) {
            if(!Ring0.ReadMsr(MSR_IA32_THERM_STATUS, out uint eax, out _))
                return -1;
            if((eax & 0x80000000) == 0)
                return -1;
            int delta = (int)((eax >> 16) & 0x7F);
            int temp = tjMax - delta;
            return (temp >= TempMin && temp <= TempMax) ? temp : -1;
        }

        // Decodes an Intel thermal-status MSR into an absolute temperature
        private static int DecodeIntelThermal(uint msr, int tjMax) {
            if(!Ring0.ReadMsr(msr, out uint eax, out _))
                return -1;

            // Bit 31 signals that the reading is valid
            if((eax & 0x80000000) == 0)
                return -1;

            // Bits 22:16 give the number of degrees below TjMax
            int delta = (int)((eax >> 16) & 0x7F);
            int temp = tjMax - delta;

            return (temp >= TempMin && temp <= TempMax) ? temp : -1;
        }

        // Determines the Intel junction temperature, defaulting to 100 °C
        private static int GetIntelTjMax() {
            if(IntelTjMax > 0)
                return IntelTjMax;

            int tjMax = 100;
            if(Ring0.ReadMsr(MSR_IA32_TEMPERATURE_TARGET, out uint eax, out _)) {
                int value = (int)((eax >> 16) & 0xFF);
                if(value >= 60 && value <= 130)
                    tjMax = value;
            }

            IntelTjMax = tjMax;
            return tjMax;
        }

        // AMD (Zen, family 17h and later): read Tctl from the SMU thermal register
        private static int GetAmdTemperature() {
            if(Family < AMD_ZEN_FAMILY_MIN)
                return -1;

            uint value;
            lock(AmdLock) {
                if(!ReadAmdSmn(AMD_F17H_THM_TCTL, out value))
                    return -1;
            }

            // Bits 31:21 hold Tctl in steps of 0.125 °C
            double tctl = ((value >> 21) & 0x7FF) * 0.125;

            // Tctl is reported directly, which matches what most monitoring tools
            // display; mobile Ryzen parts (as found in Omen/Victus laptops) do not
            // carry the desktop +27 °C Tctl-to-Tdie offset.
            int temp = (int)Math.Round(tctl);

            return (temp >= TempMin && temp <= TempMax) ? temp : -1;
        }

        // Reads an AMD SMN register through the host-bridge PCI index/data pair
        private static bool ReadAmdSmn(uint address, out uint value) {
            value = 0;

            // Point the index register at the target SMN address
            if(!Ring0.WritePciConfig(AMD_PCI_ADDRESS, AMD_SMN_INDEX_REG, address))
                return false;

            // Read the corresponding value back from the data register
            return Ring0.ReadPciConfig(AMD_PCI_ADDRESS, AMD_SMN_DATA_REG, out value);
        }

    }

}
