// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Cpu;
using StarMon.Library;

namespace StarMon.Hardware {

    // Probes and caches per-feature hardware support
    public static class FeatureSupport {

        // A single probed feature
        public class Feature {
            public string Key;        // Stable identifier
            public string Name;       // User-facing name
            public bool Supported;    // Probe outcome
            public string Detail;     // Optional extra info (e.g. zone count)

            // Set where the answer is not this machine's answer.
            //
            // Three capability queries are stubbed out in BiosCtl for every
            // machine, because one board returned an error for them. The
            // report listed the results under "hidden on this device", which
            // is not what a stub means: they are hidden on all devices, and
            // saying otherwise tells the reader something about their machine
            // that is not true of it.
            public bool NotQueried;
        }

        // Platform reference, set once at startup by the GUI
        private static StarMon.Hardware.Platform.Platform Platform;

        // Probe results, populated on first use
        private static List<Feature> Cache;
        private static readonly object Lock = new object();

        // Provides the platform reference the BIOS/EC probes need
        public static void Initialize(StarMon.Hardware.Platform.Platform platform) {
            Platform = platform;
        }

        // Returns all probed features (probing on the first call)
        public static IReadOnlyList<Feature> GetAll() {
            lock(Lock) {
                if(Cache == null)
                    Cache = Probe();
                return Cache;
            }
        }

        // Returns whether a feature is supported (unknown keys count as unsupported)
        public static bool Has(string key) {
            foreach(Feature f in GetAll())
                if(f.Key == key)
                    return f.Supported;
            return false;
        }

        // Returns the user-facing names of everything this machine does not support
        public static List<string> GetUnsupportedNames() {
            List<string> result = new List<string>();
            foreach(Feature f in GetAll())
                if(!f.Supported)
                    result.Add(f.Name);
            return result;
        }

        // Runs a probe, returning false on any exception
        private static bool Try(Func<bool> probe) {
            try { return probe(); } catch { return false; }
        }

        // Performs every probe once
        private static List<Feature> Probe() {
            List<Feature> list = new List<Feature>();
            var p = Platform;

            // HP BIOS / Embedded Controller (model-specific)
            if(p != null) {

                int zones = 0;
                try { zones = p.System.GetKbdZoneCount(); } catch { }

                list.Add(new Feature { Key = "KbdBacklight", Name = "Keyboard backlight (BIOS)",
                    Supported = Try(() => p.System.GetKbdBacklightSupport()) });
                list.Add(new Feature { Key = "KbdColor", Name = "Keyboard backlight color",
                    Supported = Try(() => p.System.GetKbdColorSupport()),
                    Detail = zones == 4 ? "4 zones" : zones == 1 ? "single zone" : "" });
                list.Add(new Feature { Key = "GpuModeSwitch", Name = "GPU mode switching (MUX)",
                    Supported = Try(() => p.System.GetGpuModeSupport()) });
                list.Add(new Feature { Key = "GpuPower", Name = "GPU power level (Custom TGP / PPAB)",
                    Supported = Try(() => { p.System.GetGpuPower(true); return p.System.GpuPowerSupported; }) });
                list.Add(new Feature { Key = "Adapter", Name = "Smart power adapter status",
                    Supported = Try(() => p.System.GetAdapterStatus() != BiosData.AdapterStatus.NotSupported) });
                list.Add(new Feature { Key = "BornDate", Name = "Born-on date",
                    Supported = Try(() => p.System.GetBornDate() != "Unknown") });
                list.Add(new Feature { Key = "FanSpeed", Name = "Fan speed reading (EC)",
                    Supported = Try(() => p.Fans.Fan[0].GetSpeed() > 0) });
                list.Add(new Feature { Key = "MaxFan", Name = "Maximum fan mode (BIOS)",
                    Supported = Try(() => { Hw.BiosGet<bool>(Hw.Bios.GetMaxFan); return true; }) });
                list.Add(new Feature { Key = "FanLevel", Name = "Fan level control (BIOS)",
                    Supported = Try(() => Hw.BiosGet(Hw.Bios.GetFanLevel) != null) });
                list.Add(new Feature { Key = "FanTable", Name = "Fan speed table (BIOS)",
                    Supported = Try(() => { Hw.BiosGetStruct<BiosData.FanTable>(Hw.Bios.GetFanTable); return true; }) });
                list.Add(new Feature { Key = "BiosTemp", Name = "BIOS temperature sensor",
                    Supported = Try(() => Hw.BiosGet(Hw.Bios.GetTemperature) > 0) });
                list.Add(new Feature { Key = "BiosThrottle", Name = "BIOS throttling status",
                    Supported = Try(() => p.System.GetThrottling() != BiosData.Throttling.Unknown),
                    NotQueried = true });
                list.Add(new Feature { Key = "MemOc", Name = "Memory overclocking (XMP)",
                    Supported = Try(() => Hw.BiosGet<byte>(Hw.Bios.HasMemoryOverclock) != 0) });
                list.Add(new Feature { Key = "Undervolt", Name = "Undervolt support (BIOS)",
                    Supported = Try(() => Hw.BiosGet<byte>(Hw.Bios.HasUndervoltBios) != 0),
                    NotQueried = true });
                list.Add(new Feature { Key = "LedAnim", Name = "LED animation table",
                    Supported = Try(() => { Hw.BiosGetStruct<BiosData.AnimTable>(Hw.Bios.GetAnimTable); return true; }) });

            }

            // The interface every HP notebook publishes, gaming line or not
            list.Add(new Feature { Key = "HpWmiSensors",
                Name = "HP published sensors (WMI)",
                Supported = Try(() => HpSensors.IsAvailable),
                Detail = Try(() => HpSensors.IsAvailable)
                    ? HpSensors.Read().Length + " sensors" : "" });

            list.Add(new Feature { Key = "HpBiosSettings",
                Name = "HP BIOS setup (readable)",
                Supported = Try(() => HpBiosSettings.IsAvailable),
                Detail = Try(() => HpBiosSettings.IsAvailable)
                    ? HpBiosSettings.All().Count + " settings" : "" });

            list.Add(new Feature { Key = "AcpiThermal",
                Name = "ACPI thermal zones",
                Supported = Try(() => AcpiThermal.IsAvailable),
                Detail = Try(() => AcpiThermal.IsAvailable)
                    ? AcpiThermal.Read().Length + " zones" : "" });

            // Processor
            list.Add(new Feature { Key = "CpuMsr", Name = "CPU temperature (MSR)",
                Supported = Try(() => CpuTemperature.IsAvailable) });
            list.Add(new Feature { Key = "CpuRapl", Name = "CPU power / clocks (RAPL)",
                Supported = Try(() => CpuMetrics.IsAvailable) });
            list.Add(new Feature { Key = "CpuCores", Name = "Per-core temperature",
                Supported = Try(() => CpuTemperature.GetPerCoreTemperatures() != null) });
            list.Add(new Feature { Key = "CpuBoost", Name = "CPU Turbo Boost control",
                Supported = Try(() => CpuBoost.Get() >= 0) });

            // Graphics and display
            list.Add(new Feature { Key = "Nvapi", Name = "NVIDIA GPU monitoring (NVAPI)",
                Supported = Try(() => GpuNvidia.IsAvailable) });
            list.Add(new Feature { Key = "Nvml", Name = "GPU power draw (NVML)",
                Supported = Try(() => GpuNvidia.IsAvailable && GpuNvidia.Get().PowerW >= 0) });
            list.Add(new Feature { Key = "Brightness", Name = "Display brightness control",
                Supported = Try(() => DisplayBrightness.Get() >= 0) });
            list.Add(new Feature { Key = "PowerMode", Name = "Windows power mode switching",
                Supported = Try(() => SystemMetrics.GetPowerMode() != SystemMetrics.PowerMode.Unknown) });

            // Storage, network, battery
            list.Add(new Feature { Key = "DiskTemp", Name = "NVMe drive temperature",
                Supported = Try(() => DiskTemperature.GetTemperature() > 0) });
            list.Add(new Feature { Key = "Wifi", Name = "Wi-Fi signal / SSID (when connected)",
                Supported = Try(() => External.WlanApi.GetSignal(out int _, out int _, out int _)) });
            list.Add(new Feature { Key = "BatteryHealth", Name = "Battery health / charge cycles",
                Supported = Try(() => Battery.Get().CycleCount >= 0 || Battery.Get().HealthPercent >= 0) });

            return list;
        }

    }

}
