// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using StarMon.Hardware.Cpu;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Hardware {

    // Builds a hardware-capability report for the current machine
    public static class Capabilities {

        // Produces the full multi-line capability report
        public static string Report(StarMon.Hardware.Platform.Platform platform = null) {
            var inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(4096);

            // ── Machine identity ────────────────────────────────────────────
            sb.AppendLine("COMPUTER");

            if(platform != null)
                TryDo(() => sb.AppendLine("  Model         : "
                    + platform.System.GetManufacturer() + " "
                    + platform.System.GetProduct() + " (board v" + platform.System.GetVersion() + ")"));

            TryDo(() => {
                using(RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if(key != null)
                        sb.AppendLine("  Processor     : "
                            + ((key.GetValue("ProcessorNameString") as string) ?? "?").Trim()
                            + " (" + Environment.ProcessorCount + " threads)");
            });

            TryDo(() => {
                using(WmiInfo wmi = new WmiInfo()) {
                    foreach(var gpu in wmi.EnumerateInstances("Win32_VideoController"))
                        if(gpu.TryGetValue("Name", out string name) && name.Length > 0)
                            sb.AppendLine("  Graphics      : " + name);
                    foreach(var bios in wmi.EnumerateInstances("Win32_BIOS"))
                        if(bios.TryGetValue("SMBIOSBIOSVersion", out string ver) && ver.Length > 0)
                            sb.AppendLine("  BIOS          : " + ver);
                    foreach(var disk in wmi.EnumerateInstances("Win32_DiskDrive"))
                        if(disk.TryGetValue("Model", out string model) && model.Length > 0)
                            sb.AppendLine("  Disk          : " + model);
                }
            });

            TryDo(() => {
                if(SystemMetrics.GetMemory(out double used, out double total, out int pct))
                    sb.AppendLine("  Memory        : " + total.ToString("0.0", inv)
                        + " GB (currently %" + pct + " used)");
            });

            TryDo(() => {
                Os.GetPrimaryScreenSize(out int screenWidth, out int screenHeight);
                sb.AppendLine("  Display       : " + screenWidth + "x" + screenHeight
                    + " @ " + Os.GetRefreshRate() + " Hz");
            });

            TryDo(() => {
                Battery.Info b = Battery.Get();
                if(b.Present)
                    sb.AppendLine("  Battery       : %" + b.Percent
                        + (b.FullmWh > 0 ? " · " + (b.FullmWh / 1000.0).ToString("0.0", inv) + " Wh" : "")
                        + (b.HealthPercent >= 0 ? " · health %" + b.HealthPercent : "")
                        + (b.CycleCount > 0 ? " · " + b.CycleCount + " cycles" : ""));
            });

            sb.AppendLine();

            // ── What this process can actually reach ───────────────────────
            //
            // First, because on a machine where the driver is blocked it is
            // the answer to every other question below. The report used to
            // describe a machine's capabilities without saying whether any of
            // them were reachable at all.
            sb.AppendLine("HARDWARE ACCESS");
            sb.AppendLine("  Firmware      : "
                + (Library.Hw.HasBios ? "available" : "NOT AVAILABLE"));
            sb.AppendLine("  Controller    : "
                + (Library.Hw.HasEc ? "available" : "NOT REACHABLE"));
            sb.AppendLine("  Code integrity: " + CodeIntegrity.Summary());

            if(!Library.Hw.HasEc) {
                sb.AppendLine();
                foreach(string line in CodeIntegrity.Explain().Split('\n'))
                    sb.AppendLine("  " + line.Trim());
            }

            sb.AppendLine();

            // ── What was worked out about this board ───────────────────────
            if(DeviceProfile.Probed) {

                sb.AppendLine("DEVICE PROFILE (probed at startup)");
                sb.AppendLine("  Family        : "
                    + (DeviceProfile.Family == DeviceProfile.DeviceFamily.Unknown
                        ? "not recognized as Omen or Victus" : DeviceProfile.Family.ToString())
                    + " · board " + DeviceProfile.Board);
                sb.AppendLine("  Fans          : " + DeviceProfile.FanCount
                    + " · levels " + Config.FanLevelMin + "–" + DeviceProfile.FanLevelCeiling
                    + " (ceiling from " + DeviceProfile.FanLevelCeilingSource + ")");
                sb.AppendLine("  Fan levels via: "
                    + (DeviceProfile.BiosFanLevel
                        ? "BIOS call" : "Embedded Controller (BIOS call unavailable here)"));
                sb.AppendLine("  Software fans : "
                    + (DeviceProfile.SoftwareFanControl ? "supported" : "not reported by the firmware"));
                sb.AppendLine("  Extreme mode  : "
                    + (DeviceProfile.ExtremeMode ? "offered" : "absent — hidden in the interface"));
                sb.AppendLine("  Keyboard      : "
                    + (DeviceProfile.KbdZones == 0 ? "no colour control"
                        : DeviceProfile.KbdZones + " colour zone"
                            + (DeviceProfile.KbdZones == 1 ? "" : "s")
                            + (Config.KbdZoneCount == 4 ? " (set by hand)" : "")));

                // The undocumented part of the firmware's keyboard answer.
                //
                // Printed rather than interpreted. The colour table cannot say
                // whether a deck has one lighting zone or four — it is four
                // entries wide either way — so the application has to be told,
                // and these bytes are where a real answer would most plausibly
                // live. Reports from machines whose deck is known are what
                // would let it be worked out instead of asked.
                if(platform != null)
                    TryDo(() => {
                        string capability = platform.System.KbdCapabilityText();
                        if(capability.Length > 0)
                            sb.AppendLine("  Kbd capability: " + capability
                                + "  (undocumented; bit 0 of the first byte is "
                                + "backlight support)");
                    });
                if(DeviceProfile.RefreshRateHigh > 0)
                    sb.AppendLine("  Panel rates   : " + DeviceProfile.RefreshRateLow
                        + " / " + DeviceProfile.RefreshRateHigh + " Hz");

                // What BIOS setup says about the board, where it is readable.
                // Fan Always On is here because it is the answer to "why do
                // the fans never stop however I set this application".
                if(HpBiosSettings.IsAvailable) {
                    string deck = HpBiosSettings.Get("Keyboard Type");
                    string shape = HpBiosSettings.Get("Keyboard Layout");
                    if(deck.Length > 0 || shape.Length > 0)
                        sb.AppendLine("  Keyboard deck : "
                            + (deck.Length > 0 ? deck : "?")
                            + (shape.Length > 0 ? " · " + shape : ""));

                    bool? always = HpBiosSettings.FanAlwaysOn;
                    if(always.HasValue)
                        sb.AppendLine("  Fan always on : "
                            + (always.Value
                                ? "yes (set in BIOS setup — the fans will not stop)"
                                : "no"));
                }

                sb.AppendLine();

            }

            // ── Live readings ──────────────────────────────────────────────
            sb.AppendLine("LIVE READINGS");

            TryDo(() => {
                int t = CpuTemperature.GetTemperature();
                if(t > 0) sb.AppendLine("  CPU temp      : " + t + "°C");
            });

            TryDo(() => {
                if(GpuNvidia.IsAvailable) {
                    GpuNvidia.GpuInfo g = GpuNvidia.Get();
                    string line = "";
                    if(g.TempC >= 0) line += g.TempC + "°C";
                    if(g.Load >= 0) line += (line.Length > 0 ? " · " : "") + "%" + g.Load + " load";
                    if(g.PowerW >= 0) line += (line.Length > 0 ? " · " : "") + g.PowerW + " W";
                    if(g.VramTotalMB > 0) line += (line.Length > 0 ? " · " : "")
                        + (g.VramUsedMB / 1024.0).ToString("0.0", inv) + "/"
                        + (g.VramTotalMB / 1024.0).ToString("0.0", inv) + " GB VRAM";
                    if(line.Length > 0) sb.AppendLine("  GPU           : " + line);
                }
            });

            if(platform != null)
                TryDo(() => {
                    string speeds = "";
                    foreach(Platform.IFan fan in platform.Fans.Fan)
                        speeds += (speeds.Length > 0 ? " / " : "") + fan.GetSpeed();
                    sb.AppendLine("  Fans          : " + speeds + " rpm"
                        + " · mode: " + Enum.GetName(typeof(Bios.BiosData.FanMode), platform.Fans.GetMode()));
                });

            TryDo(() => {
                int t = DiskTemperature.GetTemperature();
                if(t > 0) sb.AppendLine("  SSD temp      : " + t + "°C");
            });

            // Everything the firmware publishes about itself, which on a
            // machine without the gaming interface is the whole sensor set
            TryDo(() => {
                HpSensors.Sensor[] published = HpSensors.Read();
                if(published.Length == 0) {
                    sb.AppendLine("  HP sensors    : none published on this machine");
                    return;
                }
                sb.AppendLine("  HP sensors    : " + published.Length + " published");
                foreach(HpSensors.Sensor sensor in published)
                    sb.AppendLine("    " + sensor.Name.PadRight(24)
                        + sensor.Reading
                        + (sensor.Type == HpSensors.Kind.Fan ? " rpm" : "°C")
                        + (sensor.Healthy ? "" : "  [firmware reports a fault]"));
            });

            // The operating system's own thermal zones, which every laptop has
            TryDo(() => {
                AcpiThermal.Zone[] zones = AcpiThermal.Read();
                if(zones.Length == 0)
                    return;
                string line = "";
                foreach(AcpiThermal.Zone zone in zones)
                    line += (line.Length > 0 ? " · " : "")
                        + zone.Name + " " + zone.Celsius + "°C";
                sb.AppendLine("  Thermal zones : " + line);
            });

            TryDo(() => {
                int b = DisplayBrightness.Get();
                if(b >= 0) sb.AppendLine("  Brightness    : %" + b);
            });

            TryDo(() => {
                string plan = SystemMetrics.GetPowerPlanName();
                if(!string.IsNullOrEmpty(plan)) sb.AppendLine("  Power mode    : " + plan);
            });

            TryDo(() => {
                if(External.WlanApi.GetSignal(out int sig, out int rx, out int _, out string ssid))
                    sb.AppendLine("  Wi-Fi         : " + (ssid.Length > 0 ? ssid + " · " : "")
                        + "%" + sig + " signal · " + rx + " Mbps");
            });

            sb.AppendLine();

            // ── Supported / unsupported features ───────────────────────────
            IReadOnlyList<FeatureSupport.Feature> features = FeatureSupport.GetAll();

            sb.AppendLine("SUPPORTED FEATURES");
            foreach(FeatureSupport.Feature f in features)
                if(f.Supported)
                    sb.AppendLine("  ✓ " + f.Name
                        + (string.IsNullOrEmpty(f.Detail) ? "" : " (" + f.Detail + ")"));

            sb.AppendLine();
            sb.AppendLine("UNSUPPORTED FEATURES (hidden on this device)");
            bool anyUnsupported = false;
            bool anyStubbed = false;
            foreach(FeatureSupport.Feature f in features)
                if(!f.Supported) {
                    if(f.NotQueried) {
                        anyStubbed = true;
                        continue;
                    }
                    anyUnsupported = true;
                    sb.AppendLine("  ✗ " + f.Name);
                }
            if(!anyUnsupported)
                sb.AppendLine("  (none — everything is supported)");

            // Kept out of the list above rather than listed in it.
            //
            // These read as unsupported because this build does not make the
            // call, not because the machine declined it — the query was
            // stubbed out for everyone after one board answered with an error.
            // Listing them under "hidden on this device" told every reader
            // something about their own machine that was not true of it.
            if(anyStubbed) {

                sb.AppendLine();
                sb.AppendLine("NOT ASKED (this build does not make the call, on any machine)");

                foreach(FeatureSupport.Feature f in features)
                    if(!f.Supported && f.NotQueried)
                        sb.AppendLine("  ? " + f.Name);

            }

            sb.AppendLine();

            // ── Safety ─────────────────────────────────────────────────────
            sb.AppendLine("SAFETY");
            sb.AppendLine("  Thermal guard : " + (Config.ThermalProtectionEnabled
                ? "on — fans are set to maximum at " + Config.ThermalProtectionHighC + "°C; 4°C above that,"
                    + Environment.NewLine
                    + "                  fan control is handed back entirely to the hardware (EC)"
                : "off"));
            sb.AppendLine("  Fan safety    : a manual fan speed is only kept while temperatures are healthy;");
            sb.AppendLine("                  if a sensor cannot be read, EC automatic control takes over");

            return sb.ToString();
        }

        // Runs a step, ignoring any failure so the report always completes
        private static void TryDo(Action step) {
            try { step(); } catch { }
        }

    }

}
