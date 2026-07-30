// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Windows.Media;
using StarMon.AppService;
using StarMon.Library;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Design {

    // Plausible readings for the design surfaces.
    //
    // Deliberately not the tidy ones. A machine under load with one sensor
    // missing is what shows whether a layout holds: cards of unequal width,
    // a value that has to be trimmed, a reading in the critical band next to
    // one in the good band. A dashboard that only ever gets looked at showing
    // 50 °C everywhere is a dashboard whose edge cases are found by the user.
    public static class DesignData {

        // The tray menu, with the states worth looking at: a ticked setting, a
        // disabled one, a caption carrying a value, and branches
        public static System.Collections.Generic.List<Shell.MenuModel> Menu() {

            return new System.Collections.Generic.List<Shell.MenuModel> {

                Shell.MenuModel.Item("StarMon  1.4.0", null),
                Shell.MenuModel.Separator(),

                Shell.MenuModel.Branch(() => "Fan"),
                Shell.MenuModel.Branch(() => "Ekran Kartı"),
                Shell.MenuModel.Branch(() => "Klavye"),
                Shell.MenuModel.Branch(() => "Ayarlar"),

                Shell.MenuModel.Separator(),

                Shell.MenuModel.Toggle(() => "Her Zaman Üstte", () => true, null),
                Shell.MenuModel.Toggle(() => "Dinamik Simge", () => true, null),
                Shell.MenuModel.Toggle(() => "Dinamik Arka Plan",
                    () => false, null).Disable(() => false),
                Shell.MenuModel.Toggle(() => "Güncelleme: 3 sn", () => false, null),
                Shell.MenuModel.Toggle(() => "Otomatik termal koruma", () => true, null),

                Shell.MenuModel.Separator(),

                Shell.MenuModel.Item("İzleyiciyi Göster", null),
                Shell.MenuModel.Item("Günlük", null),
                Shell.MenuModel.Item("Çıkış", null)

            };

        }

        // The strip under the tabs, in the state worth checking: a machine
        // that is hot, being held back, and running a fan program — so all
        // three badges are up at once. That is the crowded case, and the one
        // where a narrow window would push them into the readings.
        public static SummaryViewModel Summary() {

            SummaryViewModel model = new SummaryViewModel();

            model.Cpu.Caption = "CPU";
            model.Cpu.SetTemperature(78, "");
            model.Cpu.Unit = "°C";

            model.Gpu.Caption = "GPU";
            model.Gpu.SetTemperature(91, "");
            model.Gpu.Unit = "°C";

            model.Fan.Caption = "FAN";
            model.Fan.Figure = "77/84";
            model.Fan.Unit = "%";

            model.Battery.Caption = "PİL";
            model.Battery.Figure = "38";
            model.Battery.Unit = "%";
            model.Battery.Detail = "pilde";
            model.Battery.Health = HealthScale.FromCharge(38);

            model.IsThermalProtection = true;
            model.IsThrottling = true;
            model.ThrottleText = "Termal";
            model.IsProgramRunning = true;
            model.ProgramName = "Silent";

            // A load arriving, so the trends have a shape rather than a line.
            // Pushed one reading at a time because that is how the strip is
            // fed in the running application, and a sample that bypasses the
            // path being checked checks nothing.
            for(int i = 0; i < 48; i++)
                model.Push(52 + i * 0.55 + System.Math.Sin(i / 3.0) * 3,
                           58 + i * 0.70 + System.Math.Cos(i / 4.0) * 2);

            return model;

        }

        public static SystemViewModel SystemInfo() {

            SystemViewModel model = new SystemViewModel();

            // A real machine's answers, mixed: this laptop has fan control and
            // a single-zone keyboard, and does not have several of the things
            // the Omen line does
            model.Capabilities.Add(new CapabilityViewModel("Fan hızı okuma (EC)", true));
            model.Capabilities.Add(new CapabilityViewModel("Azami fan modu (BIOS)", true));
            model.Capabilities.Add(new CapabilityViewModel("Fan seviyesi denetimi (BIOS)", true, "0-56"));
            model.Capabilities.Add(new CapabilityViewModel("Sıcaklık sensörleri", true, "6 sensör"));
            model.Capabilities.Add(new CapabilityViewModel("CPU güç / saat (RAPL)", true));
            model.Capabilities.Add(new CapabilityViewModel("Klavye arka ışığı (BIOS)", true));
            model.Capabilities.Add(new CapabilityViewModel("Klavye ışık rengi", true, "tek bölge"));
            model.Capabilities.Add(new CapabilityViewModel("NVIDIA GPU izleme (NVAPI)", true));
            model.Capabilities.Add(new CapabilityViewModel("NVMe sürücü sıcaklığı", true));
            model.Capabilities.Add(new CapabilityViewModel("Ekran parlaklığı denetimi", true));
            model.Capabilities.Add(new CapabilityViewModel("GPU güç seviyesi (Özel TGP / PPAB)", false, "bu modelde yok"));
            model.Capabilities.Add(new CapabilityViewModel("GPU modu değiştirme (MUX)", false, "bu modelde yok"));
            model.Capabilities.Add(new CapabilityViewModel("Bellek hız aşırtma (XMP)", false));
            model.Capabilities.Add(new CapabilityViewModel("Pil sağlığı / şarj döngüleri", true));
            model.Capabilities.Add(new CapabilityViewModel("Üretim tarihi", false));

            model.Facts.Add(new DetailRowViewModel("Sürüm", "1.4.0"));
            model.Facts.Add(new DetailRowViewModel("Model", "Victus by HP Gaming Laptop 15-fa2xxx"));
            model.Facts.Add(new DetailRowViewModel("Anakart", "8DCF"));
            model.Facts.Add(new DetailRowViewModel("BIOS", "F.14"));
            model.Facts.Add(new DetailRowViewModel("Windows", "11 · yapım 26200"));

            // The eleven things DeviceProfile establishes at every start, of
            // which the interface used to show one
            model.Profile.Add("Aile", "Victus")
                .Add("Anakart", "8DCF")
                .Add("Fan", "2")
                .Add("Tavan", "56 · azamide görüldü")
                .Add("Yazılım denetimi", "var")
                .Add("Seviye yolu", "BIOS")
                .Add("Extreme kipi", "yok")
                .Add("Klavye bölgesi", "1")
                .Add("Yenileme hızları", "60 / 144 Hz")
                .Add("Fanlar hep açık", "var")
                .Add("Yoklandı", "var");

            model.PowerMode = "HighPerformance";

            // A handful of the ninety-odd entries the firmware publishes, in
            // the alphabetical order the panel sorts them into
            model.SetBiosSettings(new[] {
                new DetailRowViewModel("Adaptive Battery Optimizer", "Enabled"),
                new DetailRowViewModel("Battery Health Manager", "Let HP manage my battery charging"),
                new DetailRowViewModel("Boot Mode", "UEFI Native (Without CSM)"),
                new DetailRowViewModel("Fan Always On", "Enabled"),
                new DetailRowViewModel("Intel VT-d", "Enabled"),
                new DetailRowViewModel("Product Name", "Victus by HP Gaming Laptop 15-fa2xxx"),
                new DetailRowViewModel("Secure Boot", "Enabled"),
                new DetailRowViewModel("System Board ID", "8DCF"),
                new DetailRowViewModel("TPM Device", "Available"),
                new DetailRowViewModel("Virtualization Technology", "Enabled")
            });

            model.Report =
                "STARMON HARDWARE REPORT" + System.Environment.NewLine +
                "=======================" + System.Environment.NewLine + System.Environment.NewLine +
                "COMPUTER" + System.Environment.NewLine +
                "  Manufacturer      HP" + System.Environment.NewLine +
                "  Model             Victus by HP Gaming Laptop 15-fa2xxx" + System.Environment.NewLine +
                "  Board             8DCF" + System.Environment.NewLine +
                "  BIOS              F.14" + System.Environment.NewLine + System.Environment.NewLine +
                "DEVICE PROFILE" + System.Environment.NewLine +
                "  Victus 8DCF - 2 fans - ceiling 56 (observed at maximum)" + System.Environment.NewLine +
                "  levels via BIOS - extreme no - kbd 1 zone" + System.Environment.NewLine + System.Environment.NewLine +
                "SUPPORTED FEATURES" + System.Environment.NewLine +
                "  Fan speed reading (EC)" + System.Environment.NewLine +
                "  Maximum fan mode (BIOS)" + System.Environment.NewLine +
                "  Fan level control (BIOS)" + System.Environment.NewLine + System.Environment.NewLine +
                "UNSUPPORTED FEATURES" + System.Environment.NewLine +
                "  GPU power level (cTGP / PPAB)" + System.Environment.NewLine +
                "  Graphics mode switching (MUX)" + System.Environment.NewLine;

            return model;

        }

        public static SettingsViewModel Settings() {

            // A machine that offers the boost and brightness controls but whose
            // firmware does not switch the graphics mode: the mixed case is
            // what shows the disabled state sits right beside the live ones
            SettingsViewModel model = new SettingsViewModel {
                IsGpuModeSupported = false,
                IsDiscrete = false,
                IsBoostSupported = true,
                BoostMode = 2,
                IsBrightnessSupported = true,
                Brightness = 72
            };

            return model;

        }

        public static LogViewModel Log() {

            LogViewModel model = new LogViewModel();

            // A plausible minute on a machine doing something: a fan program
            // stepping, an error, an entry that arrived hundreds of times and
            // was stacked, and enough hardware chatter to show what the log
            // mostly is
            Add(model, LogLevel.Info, "App", "StarMon 1.4 started");
            Add(model, LogLevel.Hardware, "Bios", "WMI interface opened", "ACPI\\PNP0C14");
            Add(model, LogLevel.Hardware, "Ec", "Embedded controller ready");
            Add(model, LogLevel.Config, "Config", "Loaded StarMon.xml", "41 settings");
            Add(model, LogLevel.Hardware, "Fan", "Program \"Silent\" started");
            Add(model, LogLevel.Hardware, "Fan", "Level 24/26 at 58 °C");
            Add(model, LogLevel.Warning, "Thermal", "Protection engaged at 90 °C");
            Add(model, LogLevel.Hardware, "Fan", "Maximum requested");
            Add(model, LogLevel.Error, "Ec", "Exclusive lock could not be taken",
                "timeout after 1000 ms");
            Add(model, LogLevel.Hardware, "Fan", "Level 43/47 at 78 °C");
            Add(model, LogLevel.Gui, "Window", "Dashboard shown");
            Add(model, LogLevel.Hardware, "Display", "Refresh rate 165 Hz -> 60 Hz",
                "on battery");
            Add(model, LogLevel.Info, "Kbd", "Backlight switched off after 5 minutes idle");

            LogEntry stacked = new LogEntry(LogLevel.Hardware, "Fan",
                "Countdown extended") { RepeatCount = 217 };
            model.Add(stacked);

            Add(model, LogLevel.Hardware, "Fan", "Level 47/51 at 82 °C");

            return model;

        }

        private static void Add(LogViewModel model, LogLevel level,
            string source, string message, string details = null) {
            model.Add(new LogEntry(level, source, message, details));
        }

        public static KeyboardViewModel Keyboard() {

            KeyboardViewModel model = new KeyboardViewModel(4);

            // Four different colours rather than four of the same, so the
            // diagram is looked at doing the thing it exists for
            model.Zones[0].Colour = Color.FromRgb(0xE6, 0x2E, 0x2E);
            model.Zones[1].Colour = Color.FromRgb(0x2E, 0x8A, 0xE6);
            model.Zones[2].Colour = Color.FromRgb(0x2E, 0xE6, 0x86);
            model.Zones[3].Colour = Color.FromRgb(0xF0, 0xC0, 0x20);

            // The saved-colour row is only drawn when the configuration file
            // has presets, so the surface has to carry some or the row goes
            // unlooked-at
            model.Presets.Add("Gündüz");
            model.Presets.Add("Gece");
            model.Presets.Add("Oyun");

            model.IsBacklightOn = true;
            model.Mode = BacklightMode.Cycle;
            model.EffectSpeed = 4;
            model.IdleOffMinutes = 5;
            model.Brand = "OMEN";
            model.Status = "Dört bölge · 5 dakika boşta kalınca kapanır";

            return model;

        }

        public static FanCurveViewModel Curve() {

            FanCurveViewModel model = new FanCurveViewModel();

            // Not the default ramp: a curve someone has actually shaped, so
            // the editor is looked at drawing something other than the line it
            // ships with
            model.Percent = new[] { 20, 30, 52, 74, 92, 100 };

            model.CurrentTemperature = 78;
            model.IsRunning = true;
            model.Status = "\"Curve\" fan programı olarak çalışıyor — seviye 41 · 78 °C";

            return model;

        }

        // The cooling section: a curve someone has shaped, a handful of saved
        // programs with one of them running, and a machine that says it does
        // offer software fan control but keeps its fans permanently on.
        //
        // That last combination is the case worth having a sample for. It is
        // exactly the state a user complains about — "the fans never stop" —
        // and until this page existed the application knew the answer and had
        // nowhere to put it.
        public static CoolingViewModel Cooling() {

            CoolingViewModel model = new CoolingViewModel(Curve());

            model.Programs.Add(new FanProgramViewModel("Silent", "Default · Minimum · 6 adım") {
                IsRunning = true
            });
            model.Programs.Add(new FanProgramViewModel("Balanced", "Performance · Medium · 6 adım"));
            model.Programs.Add(new FanProgramViewModel("Cool", "Performance · Maximum · 8 adım"));
            model.Programs.Add(new FanProgramViewModel("Curve", "Performance · Minimum · 7 adım"));

            model.Selected = model.Programs[0];
            model.Status = "Silent çalışıyor";

            Rows(model.State,
                "56 · azamide görüldü", "212 s", "var", "var",
                "2", "BIOS", "izliyor");

            return model;

        }

        // Fills a block's rows in place, the way the controller does
        private static void Rows(DetailGroupViewModel block, params string[] values) {

            for(int i = 0; i < block.Rows.Count && i < values.Length; i++)
                block.Rows[i].Value = values[i];

        }

        public static DashboardViewModel Dashboard() {

            DashboardViewModel model = new DashboardViewModel();

            model.Cpu.SetTemperature(78, "63 % · 46 W · 3.94 GHz");
            model.Cpu.Unit = "°C";

            // Twelve logical processors, the shape of this machine's 4P+4E
            // part: the P-cores' hyperthread pairs share a figure, and one
            // pinned core stands up — the shape the strip exists to show
            model.CoreTemperatures = new[] {
                72, 72, 74, 74, 88, 88, 90, 90,
                70, 69, 75, 73 };

            model.Gpu.SetTemperature(91, "97 % · 88 W · 1815 MHz");
            model.Gpu.Unit = "°C";

            model.FanCpu.Figure = "77";
            model.FanCpu.Second = "84";
            model.FanCpu.Unit = "%";
            model.FanCpu.Detail = "seviye 43 / 47 · maks. 56";
            model.FanCpu.Health = Health.Neutral;
            model.FanCpu.Portion = 0.77;

            model.Battery.Figure = "38";
            model.Battery.Unit = "%";
            model.Battery.Detail = "pilde · 1h 12m";
            model.Battery.Health = HealthScale.FromCharge(38);
            model.Battery.Portion = 0.38;

            // The four blocks. Written by index the way the controller writes
            // them, so the sample and the running application take the same
            // path through the same rows.
            model.CpuName = "Intel Core 5 210H";
            model.GpuName = "NVIDIA GeForce RTX 5050";

            // One core parked at its base clock while the rest boost, which is
            // the case the second strip exists to make visible
            model.CoreClocks = new[] {
                3900, 3940, 3880, 4100, 2200, 2210, 4090, 3870,
                3910, 3860, 3930, 3990 };

            Rows(model.CpuBlock, "63 %", "46.2 W", "45 / 60 W", "3.94 GHz");
            Rows(model.GpuBlock, "97 %", "88 / 90 W", "1815 MHz", "8001 MHz",
                "5.8 / 8.0 GB", "bu modelde yok");
            Rows(model.CoolingBlock, "77 % · 4.100 rpm", "84 % · 4.400 rpm",
                "91 °C", "Performans", "56 · azamide görüldü", "212 s", "devrede");
            Rows(model.PowerBlock, "38 %", "18.3 W", "94 %",
                "Dengeli · En iyi performans", "11.4 / 15.7 GB",
                "42.6 / 8.1 MB/s", "18.3 / 2.4 Mb/s");

            model.Mode = FanMode.Program;
            model.LevelMaximum = 56;
            model.LevelCpu = 43;
            model.LevelGpu = 47;
            model.IsProgramRunning = true;
            model.HasProgram = true;
            model.ProgramName = "Silent";

            // This machine reports no graphics power control, which is the
            // state worth looking at: the row has to say so rather than
            // offering three buttons that do nothing
            model.GraphicsPower = GpuPower.Custom;
            model.IsGpuPowerSupported = false;
            model.PerformanceMode = "Performance";
            model.Status = "Fan programı: Silent — seviye 43/47 · 78 °C";

            // Two minutes of plausible history: a machine that was idle, had
            // something asked of it, and is now sitting at its thermal limit
            // with the fans up. Flat sample data would hide everything the
            // chart has to get right — a trace that leaves the top of its
            // range, two lines crossing, and a sensor that dropped out.
            for(int i = 0; i < 120; i++) {

                double t = i / 119.0;
                double ramp = t < 0.3 ? 0 : (t - 0.3) / 0.7;
                double wobble = System.Math.Sin(i / 7.0) * 2.5;

                double cpu = 46 + ramp * 33 + wobble;
                double gpu = 41 + ramp * 51 + System.Math.Sin(i / 5.0) * 2;
                double fanCpu = 30 + ramp * 47 + wobble;
                double fanGpu = 33 + ramp * 51 + wobble;
                double load = 8 + ramp * 78 + System.Math.Sin(i / 3.0) * 6;

                // The power sensor drops out for a stretch in the middle,
                // which is what a gap in the trace has to look like
                double power = i > 52 && i < 63 ? 0 : 14 + ramp * 76 + wobble;

                model.History.Push(cpu, gpu, fanCpu, fanGpu, load, power);

            }

            // Mirrors the live panel's groups and order (see
            // WindowController.ApplyDetails), in the interface language
            model.Details.Add(new DetailGroupViewModel("SİSTEM")
                .Add("Model", "Victus by HP Gaming Laptop 15-fa2xxx")
                .Add("BIOS", "F.14")
                .Add("Plan", "Dengeli")
                .Add("Kip", "En iyi performans")
                .Add("Açık", "3g 7s"));

            model.Details.Add(new DetailGroupViewModel("İŞLEMCİ")
                .Add("Sıcaklık", "78 °C")
                .Add("Yük", "63 %")
                .Add("Güç", "46 W")
                .Add("Sınır", "45 / 60 W")
                .Add("Frekans", "3.94 GHz")
                .Add("Kısıtlama", "Termal")
                .Add("Çekirdekler", "90 °C · 12")
                .Add("Çekirdek frekansları", "2.20 - 4.10 · 12"));

            model.Details.Add(new DetailGroupViewModel("EKRAN KARTI")
                .Add("Sıcaklık", "91 °C")
                .Add("Yük", "97 %")
                .Add("Güç", "88 W")
                .Add("Güç sınırı", "90 W")
                .Add("Frekans", "1815 MHz")
                .Add("Bellek frekansı", "8001 MHz")
                .Add("VRAM", "5.8 / 8.0 GB"));

            model.Details.Add(new DetailGroupViewModel("BELLEK")
                .Add("Yük", "72 %")
                .Add("Kullanım", "11.4 / 15.7 GB"));

            model.Details.Add(new DetailGroupViewModel("DEPOLAMA VE AĞ")
                .Add("Disk", "54 °C")
                .Add("Disk hızı", "42.6 / 8.1 MB/s")
                .Add("Wi-Fi", "EvAğı · 82 %")
                .Add("Bağlantı", "573 / 480 Mb/s")
                .Add("Ağ hızı", "18.3 / 2.4 Mb/s"));

            model.Details.Add(new DetailGroupViewModel("PİL")
                .Add("Sağlık", "94 %")
                .Add("Döngü", "212")
                .Add("Kapasite", "67.4 / 70.0 Wh")
                .Add("Kalan", "1h 12m")
                .Add("Güç çekişi", "18.3 W")
                .Add("Durum", "pilde"));

            // The fans and the board's own probes. In the running application
            // this group is built from the first reading, because which probes
            // a machine has is not known until one has been taken.
            model.Details.Add(new DetailGroupViewModel("FANLAR VE ANAKART")
                .Add("İşlemci fanı", "4.100 rpm")
                .Add("Ekran kartı fanı", "4.400 rpm")
                .Add("Geri sayım", "212 s")
                .Add("Yonga seti", "62 °C")
                .Add("Bellek", "51 °C")
                .Add("Anakart ölçümü 2", "48 °C")
                .Add("CPU Fan", "4.096 rpm")
                .Add("GPU Fan", "4.352 rpm")
                .Add("Algılayıcı sağlığı", "tümü normal bildiriyor"));

            return model;

        }

    }

}
