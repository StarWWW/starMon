// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Threading;
using System.Windows.Threading;
using StarMon.AppService;
using StarMon.Library;
using StarMon.Ui.Shell;
using StarMon.Ui.ViewModels;
using StarMon.Ui.Views;

namespace StarMon.Ui.Windows {

    // Owns the window, the sections and the view models behind them.
    //
    // The sections are built once and kept, not rebuilt each time the rail is
    // clicked. Rebuilding would throw away the log's scroll position, the
    // curve halfway through being drawn and the chart's history — and the
    // history is the one thing in the window that cannot be recovered by
    // asking the hardware again.
    public sealed class WindowController {

        private readonly Dispatcher Dispatcher;

        private MainWindow Window;
        private DashboardView Dashboard;
        private SensorsView Sensors;
        private CoolingView Curve;
        private KeyboardView Keyboard;
        private LogView Log;
        private SettingsView Settings;
        private SystemView SystemPanel;
        private AboutView AboutPanel;

        // Hardware writes are held back briefly rather than made as the user
        // moves a control. A slider drag raises a change for every pixel, and
        // each one is a transaction with the Embedded Controller behind a
        // machine-wide lock — so writing on every one turns a drag into a
        // stutter and floods the log. The delay is short enough not to be
        // felt and long enough to collapse a drag into one write.
        private readonly DispatcherTimer Pending;
        private readonly DispatcherTimer PendingColour;
        private readonly DispatcherTimer PendingBrightness;
        private readonly DispatcherTimer PendingSave;

        // Set while a reading is being applied, so that writing a value into
        // the view model does not read as the user having asked for it. Every
        // second the poller sets the fan mode from the hardware, and without
        // this the window would ask the hardware for the mode it just
        // reported — once a second, forever.
        private bool IsApplyingReading;

        private ITrayHost Host;

        public WindowController(Dispatcher dispatcher) {

            this.Dispatcher = dispatcher;

            this.DashboardModel = new DashboardViewModel();
            this.CurveModel = new FanCurveViewModel();
            this.LogModel = new LogViewModel();
            this.SystemModel = new SystemViewModel();
            this.SettingsModel = new SettingsViewModel();
            this.SummaryModel = new SummaryViewModel();
            this.CoolingModel = new CoolingViewModel(this.CurveModel);

            this.Pending = new DispatcherTimer(DispatcherPriority.Background, dispatcher) {
                Interval = TimeSpan.FromMilliseconds(350)
            };

            this.Pending.Tick += delegate {
                this.Pending.Stop();
                ApplyFans();
            };

            this.PendingColour = new DispatcherTimer(
                DispatcherPriority.Background, dispatcher) {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            this.PendingColour.Tick += delegate {
                this.PendingColour.Stop();
                ApplyColours();
            };

            this.PendingBrightness = new DispatcherTimer(
                DispatcherPriority.Background, dispatcher) {
                Interval = TimeSpan.FromMilliseconds(120)
            };

            this.PendingBrightness.Tick += delegate {
                this.PendingBrightness.Stop();
                ApplyBrightness();
            };

            // Writing the configuration file is cheap but not free, and a
            // slider drag would otherwise rewrite it once per pixel. A second
            // after the user stops is soon enough for a preference.
            this.PendingSave = new DispatcherTimer(
                DispatcherPriority.Background, dispatcher) {
                Interval = TimeSpan.FromMilliseconds(800)
            };

            this.PendingSave.Tick += delegate {
                this.PendingSave.Stop();
                Guard(() => Config.Save(), "Preferences saved");
            };

            ConnectLog();

            // Most of the interface follows a language change on its own: the
            // markup binds through {loc:Str}, and the tray menu is rebuilt as
            // it opens. What is left is the strings read once into view models
            // when they were built, and those are refreshed here.
            Config.LocaleChangedHandler += OnLocaleChanged;

        }

        // The section on show, so a language change can re-issue its caption
        private Section CurrentSection = Section.Dashboard;

        private void OnLocaleChanged() {

            if(!this.Dispatcher.CheckAccess()) {
                this.Dispatcher.BeginInvoke((Action) OnLocaleChanged);
                return;
            }

            DashboardViewModel model = this.DashboardModel;

            model.Cpu.Caption = Name("GuiWpfCpu");
            model.Gpu.Caption = Name("GuiWpfGpu");
            model.FanCpu.Caption = Name("GuiWpfFans");
            model.FanGpu.Caption = Name("GuiWpfFans");
            model.Battery.Caption = Name("GuiWpfBattery");

            RelabelSummary();

            model.History.Relabel("CPU", "GPU",
                Name("GuiWpfSeriesCpuFan"), Name("GuiWpfSeriesGpuFan"),
                Name("GuiWpfSeriesLoad"), Name("GuiWpfSeriesPower"));

            // The detail groups carry their captions in their rows; cleared,
            // they are rebuilt in the new language on the next reading
            model.Details.Clear();

            if(this.KeyboardModel != null) {

                System.Collections.ObjectModel.ObservableCollection<ZoneViewModel> zones =
                    this.KeyboardModel.Zones;

                if(zones.Count >= 4) {
                    zones[0].Caption = Name("GuiWpfZoneLeft");
                    zones[1].Caption = Name("GuiWpfZoneCentre");
                    zones[2].Caption = Name("GuiWpfZoneRight");
                    zones[3].Caption = Name("GuiWpfZoneWasd");
                } else if(zones.Count == 1) {
                    zones[0].Caption = Name("GuiWpfZoneAll");
                }

            }

            // The capabilities table and machine facts carry localised
            // captions of their own; probing again rebuilds them
            if(this.Host != null)
                ProbeCapabilities();

            // The title bar names the section in the old language until it is
            // shown again, so it is shown again
            if(this.Window != null)
                Show(this.CurrentSection);

        }

        // Feeds the log view from the central logger.
        //
        // Nothing did this before, which is why the log section was empty on a
        // running machine however much the application had to say: the logger
        // recorded everything and no one was listening. The entries already
        // buffered are copied in first, then new ones are followed — each
        // marshalled onto the interface thread, because the logger is written
        // to from every thread there is and the list behind the view is not.
        private void ConnectLog() {

            foreach(LogEntry entry in Logger.GetAll())
                this.LogModel.Add(entry);

            Logger.LogAdded += delegate(object sender, LogEventArgs e) {
                LogEntry entry = e.Entry;
                if(this.Dispatcher.CheckAccess())
                    this.LogModel.Add(entry);
                else
                    this.Dispatcher.BeginInvoke(
                        (Action) delegate { this.LogModel.Add(entry); });
            };

            Logger.LogsCleared += delegate {
                if(this.Dispatcher.CheckAccess())
                    this.LogModel.Clear();
                else
                    this.Dispatcher.BeginInvoke((Action) this.LogModel.Clear);
            };

        }

        // Gives the window somewhere to send what the user asks for. Until
        // this is called the controls show readings and change nothing, which
        // is the right behaviour for the design renderer and the wrong one
        // for the application.
        public void Connect(ITrayHost host) {

            this.Host = host;

            this.DashboardModel.PropertyChanged += OnDashboardChanged;

            this.CurveModel.Applied += ApplyCurve;
            this.CurveModel.Stopped += delegate {
                Guard(() => host.Program.Terminate(), "Stopping the fan curve");
                host.RefreshFanState();
            };

            // The programs. Every one of these paths existed in the service
            // layer already; none of them had a control anywhere in the window.
            this.CoolingModel.RunRequested += RunProgram;
            this.CoolingModel.StopRequested += StopProgram;
            this.CoolingModel.SaveRequested += SaveProgram;
            this.CoolingModel.DeleteRequested += DeleteProgram;
            this.CoolingModel.PropertyChanged += OnCoolingChanged;

            this.CoolingModel.Reload("");

            this.SystemModel.PropertyChanged += OnSystemChanged;

            this.SettingsModel.PropertyChanged += OnSettingsChanged;

            ProbeCapabilities();
            ReadSettings();

        }

        // Fills the capabilities table and the machine facts, once.
        //
        // The probe asks the firmware about each feature in turn and is not
        // instant, so it runs off the interface thread and the panel says it
        // is working until the answers arrive. Nothing populated this before,
        // which is why the panel was blank; the probe existed, but no one ran
        // it or showed what it found.
        private void ProbeCapabilities() {

            this.SystemModel.IsProbing = true;

            ThreadPool.QueueUserWorkItem(delegate {

                System.Collections.Generic.List<CapabilityViewModel> caps =
                    new System.Collections.Generic.List<CapabilityViewModel>();

                try {
                    foreach(Hardware.FeatureSupport.Feature f
                        in Hardware.FeatureSupport.GetAll())
                        caps.Add(new CapabilityViewModel(
                            CapCaption(f.Key, f.Name), f.Supported, CapDetail(f.Detail)));
                } catch(Exception e) {
                    Logger.Error("Window", "Probing capabilities failed", e.Message);
                }

                System.Collections.Generic.List<DetailRowViewModel> facts = Facts();
                System.Collections.Generic.List<DetailRowViewModel> profile = Profile();
                System.Collections.Generic.List<DetailRowViewModel> bios = BiosSettings();

                // The full hardware report. Two hundred and fifty lines of it
                // have been generatable since Capabilities was written and had
                // no caller anywhere in the application — the tray menu item
                // that promised it opened a far thinner table instead.
                //
                // Built here rather than on the interface thread because it
                // asks the firmware, the Embedded Controller and WMI in turn,
                // which is the same reason the capability probe is out here.
                string report = "";
                try {
                    report = Hardware.Capabilities.Report(
                        this.Host != null ? this.Host.Platform : null);
                } catch(Exception e) {
                    report = "";
                    Logger.Error("Window", "Building the hardware report failed", e.Message);
                }

                this.Dispatcher.BeginInvoke((Action) delegate {

                    this.SystemModel.Capabilities.Clear();
                    foreach(CapabilityViewModel c in caps)
                        this.SystemModel.Capabilities.Add(c);

                    this.SystemModel.Facts.Clear();
                    foreach(DetailRowViewModel f in facts)
                        this.SystemModel.Facts.Add(f);

                    this.SystemModel.Profile.Rows.Clear();
                    foreach(DetailRowViewModel p in profile)
                        this.SystemModel.Profile.Rows.Add(p);

                    this.SystemModel.SetBiosSettings(bios);
                    this.SystemModel.Report = report;

                    this.SystemModel.IsProbing = false;

                });

            });

        }

        // The localised name for a capability, falling back to the English one
        // the probe carries when the current language has none. The probe is
        // shared with the command line, whose output stays English, so the
        // translation lives here rather than in the feature list.
        private static string CapCaption(string key, string fallback) {
            string localised = Config.Locale.Get("GuiCap" + key);
            return localised == "GuiCap" + key ? fallback : localised;
        }

        // The one detail the probe fills in is a keyboard zone count, in words
        private static string CapDetail(string detail) {
            if(detail == "4 zones") return Config.Locale.Get("GuiCapZones4");
            if(detail == "single zone") return Config.Locale.Get("GuiCapZone1");
            return detail;
        }

        // The machine's identity, gathered for the About panel
        private System.Collections.Generic.List<DetailRowViewModel> Facts() {

            System.Collections.Generic.List<DetailRowViewModel> facts =
                new System.Collections.Generic.List<DetailRowViewModel>();

            facts.Add(new DetailRowViewModel(Name("GuiWpfVersion"), Config.AppVersion,
                Name("GuiWpfTipVersion")));

            string model = "", bios = "";
            try {
                using(Hardware.Platform.WmiInfo wmi = new Hardware.Platform.WmiInfo()) {
                    foreach(var cs in wmi.EnumerateInstances("Win32_ComputerSystem")) {
                        cs.TryGetValue("Manufacturer", out string maker);
                        cs.TryGetValue("SystemFamily", out string family);
                        cs.TryGetValue("Model", out string m);
                        string name = !string.IsNullOrEmpty(family)
                            && (m ?? "").IndexOf(family, StringComparison.OrdinalIgnoreCase) < 0
                            ? (family + " " + m) : m;
                        model = ((maker ?? "") + " " + (name ?? "")).Trim();
                        break;
                    }
                    foreach(var b in wmi.EnumerateInstances("Win32_BIOS"))
                        if(b.TryGetValue("SMBIOSBIOSVersion", out string v) && v.Length > 0) {
                            bios = v; break;
                        }
                }
            } catch { }

            if(model.Length > 0)
                facts.Add(new DetailRowViewModel(Name("GuiWpfModel"), model,
                    Name("GuiTipModel")));

            try {
                string board = (this.Host.Platform.System.GetManufacturer()
                    + " " + this.Host.Platform.System.GetProduct()).Trim();
                if(board.Length > 0)
                    facts.Add(new DetailRowViewModel(Name("GuiWpfBoard"), board,
                        Name("GuiWpfTipBoard")));
            } catch { }

            if(bios.Length > 0)
                facts.Add(new DetailRowViewModel(Name("GuiWpfBios"), bios,
                    Name("GuiTipBios")));

            facts.Add(new DetailRowViewModel(Name("GuiWpfWindows"), WindowsName(),
                Name("GuiWpfTipWindows")));

            return facts;

        }

        // A short Windows description: the marketing number and the build,
        // which is what a bug report needs and Environment.OSVersion does not
        // give directly (it reports 10.0 for eleven)
        private static string WindowsName() {

            try {
                Version v = Environment.OSVersion.Version;
                string edition = v.Build >= 22000 ? "Windows 11" : "Windows 10";
                return edition + " · " + Config.Locale.Get("GuiWpfBuilt").ToLower()
                    + " " + v.Build;
            } catch {
                return "Windows";
            }

        }

        // What the application worked out about this board at startup.
        //
        // DeviceProfile establishes eleven things and the interface consumed
        // exactly one of them — the Extreme profile. The other ten reached a
        // single log line, and two of them are the answers to the questions
        // people actually ask: why the fan slider stops where it does, and
        // whether the firmware offers software fan control at all.
        private System.Collections.Generic.List<DetailRowViewModel> Profile() {

            System.Collections.Generic.List<DetailRowViewModel> rows =
                new System.Collections.Generic.List<DetailRowViewModel>();

            try {

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowFamily"),
                    Hardware.DeviceProfile.Family.ToString(),
                    Name("GuiWpfTipFamily")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfBoard"),
                    Hardware.DeviceProfile.Board ?? "-",
                    Name("GuiWpfTipBoard")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowFanCount"),
                    Hardware.DeviceProfile.FanCount.ToString(),
                    Name("GuiWpfTipFanCount")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowCeiling"),
                    Hardware.DeviceProfile.FanLevelCeiling + " · " + CeilingSource(),
                    Name("GuiWpfTipCeiling")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowSoftware"),
                    YesNo(Hardware.DeviceProfile.SoftwareFanControl),
                    Name("GuiWpfTipSoftware")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowLevelPath"),
                    Hardware.DeviceProfile.BiosFanLevel ? "BIOS" : "EC",
                    Name("GuiWpfTipLevelPath")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowExtreme"),
                    YesNo(Hardware.DeviceProfile.ExtremeMode),
                    Name("GuiWpfTipExtreme")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowZones"),
                    Hardware.DeviceProfile.KbdZones == 0
                        ? Name("GuiWpfNoColour")
                        : Hardware.DeviceProfile.KbdZones.ToString(),
                    Name("GuiWpfTipZones")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowRefresh"),
                    Hardware.DeviceProfile.RefreshRateLow + " / "
                        + Hardware.DeviceProfile.RefreshRateHigh + " Hz",
                    Name("GuiWpfTipRefreshRates")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowAlwaysOn"),
                    YesNo(Hardware.HpBiosSettings.FanAlwaysOn),
                    Name("GuiWpfTipAlwaysOn")));

                rows.Add(new DetailRowViewModel(Name("GuiWpfRowProbed"),
                    YesNo(Hardware.DeviceProfile.Probed),
                    Name("GuiWpfTipProbed")));

            } catch(Exception e) {
                Logger.Error("Window", "Reading the device profile failed", e.Message);
            }

            return rows;

        }

        private static string CeilingSource() {

            string source = Hardware.DeviceProfile.FanLevelCeilingSource ?? "";

            switch(source) {
                case "fan table": return Name("GuiWpfCeilingTable");
                case "observed at maximum": return Name("GuiWpfCeilingMaximum");
                case "observed running": return Name("GuiWpfCeilingRunning");
                case "configured": return Name("GuiWpfCeilingSet");
                case "configured (auto-detect off)": return Name("GuiWpfCeilingFixed");
                default: return source;
            }

        }

        // The BIOS setup menu, as the firmware publishes it. Read and cached
        // by the hardware layer since it was written; the interface used two
        // of its ninety-odd entries and the rest were unreachable.
        private static System.Collections.Generic.List<DetailRowViewModel> BiosSettings() {

            System.Collections.Generic.List<DetailRowViewModel> rows =
                new System.Collections.Generic.List<DetailRowViewModel>();

            try {

                System.Collections.Generic.Dictionary<string, string> settings =
                    Hardware.HpBiosSettings.All();

                if(settings == null)
                    return rows;

                // By name: the firmware publishes them in whatever order it
                // enumerates, which is not an order anybody can look things up in
                System.Collections.Generic.List<string> names =
                    new System.Collections.Generic.List<string>(settings.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);

                foreach(string name in names)
                    rows.Add(new DetailRowViewModel(name, settings[name],
                        Name("GuiWpfTipBiosSetting")));

            } catch(Exception e) {
                Logger.Error("Window", "Reading the BIOS settings failed", e.Message);
            }

            return rows;

        }

        public DashboardViewModel DashboardModel { get; private set; }
        public FanCurveViewModel CurveModel { get; private set; }
        public KeyboardViewModel KeyboardModel { get; private set; }
        public LogViewModel LogModel { get; private set; }
        public SystemViewModel SystemModel { get; private set; }
        public SettingsViewModel SettingsModel { get; private set; }

        // The strip under the tabs. Not a section: it is the shell's own data
        // context, so it is on show whatever page the user is looking at.
        public SummaryViewModel SummaryModel { get; private set; }

        // The cooling section: the curve, the saved programs, and what the
        // firmware will actually allow
        public CoolingViewModel CoolingModel { get; private set; }

        // The strip's four labels. Kept short — they sit on one line with the
        // figures, and a full caption there would push the badges off the end
        // of a narrow window.
        private void RelabelSummary() {

            SummaryViewModel strip = this.SummaryModel;
            if(strip == null)
                return;

            strip.Cpu.Caption = Name("GuiWpfStripCpu");
            strip.Gpu.Caption = Name("GuiWpfStripGpu");
            strip.Fan.Caption = Name("GuiWpfStripFan");
            strip.Battery.Caption = Name("GuiWpfStripBattery");

        }

        // Set once the keyboard's shape is known, which needs hardware:
        // how many colour zones it has (zero for a deck that lights but takes
        // no colour from here) and whether it carries a numeric pad
        public void SetKeyboard(int zoneCount, bool hasNumPad = true,
            bool? isIsoBody = null) {

            this.KeyboardModel = new KeyboardViewModel(zoneCount);
            this.KeyboardModel.HasNumPad = hasNumPad;
            this.KeyboardModel.IsIsoBody = isIsoBody;
            this.KeyboardModel.PropertyChanged += OnKeyboardChanged;

            // Whether the machine has a controllable backlight at all.
            //
            // The property defaulted to true and was never assigned from
            // anything, so a machine with no backlight got a panel that
            // claimed it had one and a switch that did nothing. The firmware
            // has been able to answer this since Settings was written.
            try {
                this.KeyboardModel.IsSupported = this.Host != null
                    && this.Host.Platform.System.GetKbdBacklightSupport();
            } catch {
                // A firmware that will not say is treated as having one: the
                // colour zones were discovered somehow, and hiding the panel
                // on a failed probe would be worse than showing a control that
                // turns out to do nothing
                this.KeyboardModel.IsSupported = zoneCount > 0;
            }

            // The saved colour presets, which the list has been constructed
            // empty and never filled since it was written — so the presets in
            // the configuration file were reachable only from the tray menu
            this.KeyboardModel.Presets.Clear();
            if(Config.ColorPreset != null)
                foreach(string preset in Config.ColorPreset.Keys)
                    this.KeyboardModel.Presets.Add(preset);

            foreach(ZoneViewModel zone in this.KeyboardModel.Zones)
                zone.PropertyChanged += OnZoneChanged;

            // Show the mode and settings the machine is actually in rather than
            // the defaults, so the panel opens agreeing with the hardware. Held
            // behind the applying-a-reading guard so filling it in does not read
            // as the user reaching for it and write straight back.
            this.IsApplyingReading = true;
            try {
                this.KeyboardModel.IdleOffMinutes = Config.KbdIdleOffMinutes;
                this.KeyboardModel.EffectSpeed = Config.KbdEffectSpeed;
                this.KeyboardModel.Mode =
                    Config.KbdColorByTemp ? BacklightMode.Temperature
                    : Config.KbdColorEffect == 1 ? BacklightMode.Cycle
                    : Config.KbdColorEffect == 2 ? BacklightMode.Breathe
                    : BacklightMode.Static;
            } finally {
                this.IsApplyingReading = false;
            }

            if(this.Keyboard != null)
                this.Keyboard.DataContext = this.KeyboardModel;

        }

        // Told by the tray host when the backlight has been switched — by the
        // window's own control, by the tray menu, or by the idle watch.
        //
        // Pushed rather than polled. The firmware on this hardware reports the
        // backlight state back inverted, so a window that asked contradicted
        // itself a few seconds after every use; and nothing outside this
        // application switches the light, so there was never anything to
        // discover by asking.
        public void SetBacklightState(bool on) {

            if(!this.Dispatcher.CheckAccess()) {
                this.Dispatcher.BeginInvoke((Action) delegate { SetBacklightState(on); });
                return;
            }

            KeyboardViewModel kbd = this.KeyboardModel;
            if(kbd == null || kbd.IsBacklightOn == on)
                return;

            // Behind the guard: this is the hardware's state arriving, not the
            // user asking for it, and answering it by writing it back would be
            // a loop
            this.IsApplyingReading = true;
            try {
                kbd.IsBacklightOn = on;
            } finally {
                this.IsApplyingReading = false;
            }

        }

        private void OnKeyboardChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(this.Host == null || this.IsApplyingReading)
                return;

            KeyboardViewModel model = this.KeyboardModel;

            switch(e.PropertyName) {

                case "IsBacklightOn":
                    Guard(() => this.Host.SetKbdBacklight(model.IsBacklightOn),
                        "Switching the backlight");
                    break;

                case "Mode":
                    // The animated effects and the temperature-reactive mode
                    // are mutually exclusive, and each takes over from the
                    // other; the host already knows how to do that handover,
                    // including putting back the colour an effect replaced.
                    Guard(delegate {
                        if(model.Mode == BacklightMode.Temperature) {
                            this.Host.SetKbdColorByTemp(true);
                        } else {
                            this.Host.SetKbdColorByTemp(false);
                            this.Host.SetKbdEffect(
                                model.Mode == BacklightMode.Cycle ? 1
                                : model.Mode == BacklightMode.Breathe ? 2 : 0);
                        }
                    }, "Changing the backlight mode");
                    break;

                case "IdleOffMinutes":
                    Config.KbdIdleOffMinutes = model.IdleOffMinutes;
                    Guard(Config.Save, "Saving the idle timeout");
                    break;

                // The effect speed is saved and takes effect on the next tick,
                // where the effect reads it back
                case "EffectSpeed":
                    Config.KbdEffectSpeed = model.EffectSpeed;
                    Guard(Config.Save, "Saving the effect speed");
                    break;

            }

        }

        private void OnZoneChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(this.Host == null || this.IsApplyingReading
                || e.PropertyName != "Colour")
                return;

            // Held back like the fan levels are: a colour picker being dragged
            // raises a change per pixel, and each is a write to the keyboard
            this.PendingColour.Stop();
            this.PendingColour.Start();

        }

        private void ApplyColours() {

            if(this.Host == null || this.KeyboardModel == null)
                return;

            KeyboardViewModel model = this.KeyboardModel;

            // A deck with no zones is one whose backlight this application can
            // switch but not colour, so there is nothing here to send
            if(!model.HasColour)
                return;

            Guard(delegate {

                // Picking a colour by hand means the user no longer wants a
                // mode that chooses colours for them
                if(model.Mode != BacklightMode.Static)
                    model.Mode = BacklightMode.Static;

                if(model.IsSingleZone) {
                    this.Host.SetKbdColor(Packed(model.Zones[0]));
                    return;
                }

                int[] zones = new int[model.Zones.Count];
                for(int i = 0; i < zones.Length; i++)
                    zones[i] = Packed(model.Zones[i]);

                this.Host.SetKbdZoneColors(zones);

            }, "Backlight colour: " + model.Zones[0].Hex
                + (model.IsSingleZone ? "" : " …"));

        }

        private static int Packed(ZoneViewModel zone) {
            return zone.Colour.R << 16 | zone.Colour.G << 8 | zone.Colour.B;
        }

        // Keeps the keyboard section in step with the hardware: the deck's name,
        // once, and the swatch colours as the firmware currently holds them —
        // so an effect's colour, or one the tray menu set, shows here too. Held
        // back while the user is dragging the picker, so a reading taken
        // mid-edit does not snatch the colour back from under them.
        private void ApplyKeyboard(Reading reading) {

            KeyboardViewModel kbd = this.KeyboardModel;
            if(kbd == null)
                return;

            if(kbd.Brand.Length == 0) {
                string brand = BrandOf(reading.SystemModel);
                if(brand.Length > 0)
                    kbd.Brand = brand;
            }

            if(reading.KbdColors == null || reading.KbdColors.Length == 0
                || this.PendingColour.IsEnabled)
                return;

            int[] cols = reading.KbdColors;
            for(int i = 0; i < kbd.Zones.Count; i++) {
                int packed = cols[i < cols.Length ? i : cols.Length - 1];
                System.Windows.Media.Color colour = System.Windows.Media.Color.FromRgb(
                    (byte) ((packed >> 16) & 0xFF),
                    (byte) ((packed >> 8) & 0xFF),
                    (byte) (packed & 0xFF));
                if(kbd.Zones[i].Colour != colour)
                    kbd.Zones[i].Colour = colour;
            }

        }

        // The machine's short marketing name from its model string, for the
        // keyboard deck. Only the two families this application is for.
        private static string BrandOf(string model) {
            if(string.IsNullOrEmpty(model))
                return "";
            if(model.IndexOf("OMEN", StringComparison.OrdinalIgnoreCase) >= 0)
                return "OMEN";
            if(model.IndexOf("Victus", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Victus";
            return "";
        }

        // Builds the window on first use. A tray application spends most of
        // its life with no window at all, and building one that is never
        // looked at costs a startup nobody asked to pay for.
        // The window, if it has been built. Null before the first time it is
        // shown, which is the ordinary state of a tray application.
        public MainWindow Current { get { return this.Window; } }

        public MainWindow Ensure() {

            if(this.Window != null)
                return this.Window;

            this.Window = new MainWindow();

            this.Dashboard = new DashboardView { DataContext = this.DashboardModel };
            this.Sensors = new SensorsView { DataContext = this.DashboardModel };
            this.Curve = new CoolingView { DataContext = this.CoolingModel };
            this.Log = new LogView { DataContext = this.LogModel };
            this.Settings = new SettingsView { DataContext = this.SettingsModel };
            this.SystemPanel = new SystemView { DataContext = this.SystemModel };
            this.AboutPanel = new AboutView { DataContext = this.SystemModel };

            if(this.KeyboardModel != null)
                this.Keyboard = new KeyboardView { DataContext = this.KeyboardModel };

            // The strip belongs to the frame rather than to any section, so
            // the frame is what it is bound to
            this.Window.View.DataContext = this.SummaryModel;
            RelabelSummary();

            this.Window.View.SectionSelected += Show;
            Show(Section.Dashboard);

            return this.Window;

        }

        // Brings a section to the front, as the tray menu does when it sends
        // the user somewhere specific
        public void Select(Section section) {
            Show(section);
        }

        private void Show(Section section) {

            if(this.Window == null)
                return;

            this.CurrentSection = section;

            switch(section) {

                case Section.Sensors:
                    this.Window.View.SetSection(section, Name("GuiWpfSensors"), this.Sensors);
                    break;

                case Section.Cooling:
                    this.Window.View.SetSection(section, Name("GuiWpfCooling"), this.Curve);
                    break;

                case Section.Keyboard:
                    this.Window.View.SetSection(section, Name("GuiWpfKeyboard"), this.Keyboard);
                    break;

                case Section.System:
                    this.Window.View.SetSection(section, Name("GuiWpfSystem"), this.SystemPanel);
                    break;

                case Section.Log:
                    this.Window.View.SetSection(section, Name("GuiWpfLog"), this.Log);
                    break;

                case Section.Settings:
                    this.Window.View.SetSection(section, Name("GuiWpfSettings"), this.Settings);
                    break;

                case Section.About:
                    this.Window.View.SetSection(section, Name("GuiWpfAbout"), this.AboutPanel);
                    break;

                default:
                    this.Window.View.SetSection(Section.Dashboard,
                        Name("GuiWpfDashboard"), this.Dashboard);
                    break;

            }

        }

#region Hardware controls (the settings section)
        // Reads the current state of each control from the firmware, once, off
        // the interface thread. The reads are set back through the same
        // applying-a-reading guard the poller uses, so filling the controls in
        // does not read as the user having reached for them.
        private void ReadSettings() {

            // File logging, if it was left on.
            //
            // This was wired to the preference changing and to nothing else,
            // so a configuration with LogToFile already true wrote no file
            // until the switch was toggled off and on again — the setting
            // survived a restart and its effect did not.
            if(Config.LogToFile)
                Guard(() => Logger.EnableFileLogging(Config.AppFile + Config.LogFileExt),
                    "Log to file: on (from the saved preference)");

            if(this.Host == null)
                return;

            ThreadPool.QueueUserWorkItem(delegate {

                bool gpuSupported = false, discrete = false;
                try {
                    gpuSupported = this.Host.Platform.System.GetGpuModeSupport();
                    if(gpuSupported)
                        discrete = this.Host.Platform.System.GetGpuMode()
                            == Hardware.Bios.BiosData.GpuMode.Discrete;
                } catch { }

                int boost = -1;
                try { boost = Hardware.CpuBoost.Get(); } catch { }

                int brightness = -1;
                try { brightness = Hardware.DisplayBrightness.Get(); } catch { }

                this.Dispatcher.BeginInvoke((Action) delegate {

                    this.IsApplyingReading = true;
                    try {

                        this.SettingsModel.IsGpuModeSupported = gpuSupported;
                        this.SettingsModel.IsDiscrete = discrete;

                        this.SettingsModel.IsBoostSupported = boost >= 0;
                        if(boost >= 0)
                            this.SettingsModel.BoostMode = boost > 2 ? 1 : boost;

                        this.SettingsModel.IsBrightnessSupported = brightness >= 0;
                        if(brightness >= 0)
                            this.SettingsModel.Brightness = brightness;

                    } finally {
                        this.IsApplyingReading = false;
                    }

                });

            });

        }

        private void OnSettingsChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(this.Host == null || this.IsApplyingReading)
                return;

            switch(e.PropertyName) {

                case "IsDiscrete":
                    ApplyGpuMode();
                    break;

                case "BoostMode":
                    Guard(() => Hardware.CpuBoost.Set(this.SettingsModel.BoostMode),
                        "Turbo boost: " + (this.SettingsModel.BoostMode == 0 ? "off"
                            : this.SettingsModel.BoostMode == 2 ? "aggressive" : "on"));
                    break;

                // A brightness drag raises a change per pixel, and each is a
                // WMI call — so it is held back the way the fan levels are
                case "Brightness":
                    this.PendingBrightness.Stop();
                    this.PendingBrightness.Start();
                    break;

                // The preferences write straight into the configuration, so
                // most of them need nothing beyond the file being brought up
                // to date. The three below are the ones that also change
                // something outside it, and each raises its own name first.
                //
                // Every one of these was reachable before only from the
                // notification-area menu or by editing the configuration file
                // by hand, neither of which is where anyone looks for a
                // preference.

                case "StartWithWindows":
                    // Not a value in a file: a scheduled task that exists or
                    // does not
                    Guard(() => Hw.TaskSet(Config.TaskId.Gui, Config.AutoStartup),
                        "Start with Windows: " + (Config.AutoStartup ? "on" : "off"));
                    break;

                case "StayOnTop":
                    if(this.Window != null)
                        Guard(() => this.Window.Topmost = Config.GuiStayOnTop,
                            "Stay on top: " + (Config.GuiStayOnTop ? "on" : "off"));
                    break;

                case "LogToFile":
                    // Opening a writer, rather than merely recording that one
                    // was wanted
                    Guard(() => {
                        if(Config.LogToFile)
                            Logger.EnableFileLogging(Config.AppFile + Config.LogFileExt);
                        else
                            Logger.DisableFileLogging();
                    }, "Log to file: " + (Config.LogToFile ? "on" : "off"));
                    break;

                // Raised after any preference, once its own side effect above
                // has run. Held back the way the fan levels are: dragging the
                // thermal threshold raises a change per pixel, and each one
                // would otherwise rewrite the configuration file.
                case "Dirty":
                    this.PendingSave.Stop();
                    this.PendingSave.Start();
                    break;

            }

        }

        private void ApplyGpuMode() {

            if(this.Host == null)
                return;

            Guard(delegate {
                this.Host.Platform.System.SetGpuMode(
                    this.SettingsModel.IsDiscrete
                        ? Hardware.Bios.BiosData.GpuMode.Discrete
                        : Hardware.Bios.BiosData.GpuMode.Optimus);
            }, "Setting the graphics mode");

            // The switch only takes effect on the next boot, so the panel says
            // so rather than looking as though nothing happened
            this.SettingsModel.GpuModeNote = Config.Locale.Get("GuiWpfRestartNeeded");

        }

        private void ApplyBrightness() {

            if(this.Host == null)
                return;

            Guard(() => Hardware.DisplayBrightness.Set(
                (int) this.SettingsModel.Brightness),
                "Display brightness: " + (int) this.SettingsModel.Brightness + " %");

        }
#endregion

#region Acting on what the user asked for
        private void OnDashboardChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(this.Host == null || this.IsApplyingReading)
                return;

            switch(e.PropertyName) {

                // A mode is a decision and takes effect at once. The levels
                // are a value being adjusted, so they wait for the adjusting
                // to stop.
                case "Mode":
                    Requested();
                    this.Pending.Stop();
                    ApplyFans();
                    break;

                case "LevelCpu":
                case "LevelGpu":
                    if(this.DashboardModel.Mode == FanMode.Constant) {
                        Requested();
                        this.Pending.Stop();
                        this.Pending.Start();
                    }
                    break;

                case "GraphicsPower":
                    ApplyGpuPower();
                    break;

                // The firmware profile takes effect at once, like a mode does,
                // and is held sticky so the machine cannot quietly drop it
                case "PerformanceMode":
                    Requested();
                    ApplyPerformanceMode();
                    break;

            }

        }

        private void ApplyPerformanceMode() {
            if(SetPerformanceSticky())
                this.Host.RefreshFanState();
        }

        // Applies the performance profile the selector is showing, stickily.
        //
        // Sticky, not a one-shot set: the firmware resets its profile on its
        // own schedule, and re-asserting it is what keeps the graphics power up
        // rather than sliding back to the base draw a moment later. Returns
        // whether it did anything, so the caller can decide whether a refresh
        // is warranted.
        private bool SetPerformanceSticky() {

            if(this.Host == null)
                return false;

            Hardware.Bios.BiosData.FanMode mode;
            if(!Enum.TryParse(this.DashboardModel.PerformanceMode, out mode))
                return false;

            Guard(() => this.Host.Platform.SetFanModeSticky(mode),
                "Performance mode: " + mode);

            return true;

        }

        private void ApplyGpuPower() {

            if(this.Host == null)
                return;

            bool applied = FanControl.ApplyGpuPower(
                this.Host.Platform, this.DashboardModel.GraphicsPower);

            // The firmware is the authority on whether it has this to give.
            // A machine that reports no support leaves the row disabled and
            // saying so, rather than accepting clicks that go nowhere.
            this.DashboardModel.IsGpuPowerSupported = applied
                || Read(() => this.Host.Platform.System.GpuPowerSupported);

        }

        private static bool Read(Func<bool> read) {
            try { return read(); } catch { return false; }
        }

        private void ApplyFans() {

            if(this.Host == null)
                return;

            DashboardViewModel model = this.DashboardModel;

            FanRequest request;
            switch(model.Mode) {
                case FanMode.Constant: request = FanRequest.Constant; break;
                case FanMode.Maximum:  request = FanRequest.Maximum;  break;
                case FanMode.Program:  request = FanRequest.Program;  break;
                default:               request = FanRequest.Automatic; break;
            }

            // Which program the Program mode means.
            //
            // FanControl.Apply returns immediately when handed an empty name,
            // and nothing on the dashboard had ever set one — so the Program
            // segment was a live button that did nothing whenever a program
            // was not already running. It falls back to the configured default
            // and then to the first saved program, and puts the selector back
            // where it was if there is nothing at all to run.
            if(request == FanRequest.Program) {

                string program = ResolveProgram(model.ProgramName);

                if(program.Length == 0) {
                    model.Mode = FanMode.Automatic;
                    return;
                }

                model.ProgramName = program;

            }

            // The last argument means two different things.
            //
            // For Program it is the program to run. For Automatic it is the
            // firmware profile to hold — which is what the Enum.TryParse in
            // FanControl's default branch is there for, and it has never been
            // given anything it could parse: the call always passed the fan
            // program's name, which is never a mode name, so the branch always
            // fell through to re-asserting whatever the firmware happened to
            // report at that instant.
            //
            // Two seconds after asking for Maximum, that is still the old
            // mode. So switching back to Automatic quietly threw the user's
            // chosen profile away — and with it the graphics power that
            // profile is the only way to release. The profile is the user's
            // decision and it outlives a change of fan mode.
            string argument = request == FanRequest.Program
                ? model.ProgramName : model.PerformanceMode;

            string what = "Fan mode: " + request
                + (request == FanRequest.Constant
                    ? " " + (int) model.LevelCpu + "/" + (int) model.LevelGpu
                    : request == FanRequest.Program ? " " + model.ProgramName
                    : " (" + model.PerformanceMode + ")");

            Guard(() => FanControl.Apply(this.Host.Platform, this.Host.Program,
                request, (int) model.LevelCpu, (int) model.LevelGpu,
                argument), what);

            if(request == FanRequest.Program && this.CoolingModel != null) {
                this.CoolingModel.SetRunning(model.ProgramName);
                this.CoolingModel.Status = string.Format(
                    Name("GuiWpfProgramRunning"), model.ProgramName);
            }

            // The settle window runs from when the hardware was written to,
            // not from when the user clicked: a level drag is held back before
            // this point, and starting the clock at the click would let it
            // lapse while the write was still waiting to be made
            Requested();

            // Maximum fans is a request for the machine to work hard, not just
            // to be loud, so the graphics limits come up with them. Setting the
            // view model rather than calling straight through is deliberate:
            // the selector has to show it, or the window would be doing
            // something it is not admitting to.
            if(request == FanRequest.Maximum && model.GraphicsPower != GpuPower.Boost)
                model.GraphicsPower = GpuPower.Boost;

            // Holding the fans at a constant speed clears the firmware-profile
            // stickiness (FanControl.Apply does, so a stale Maximum→Performance
            // is not left re-asserting). But the performance selector is its
            // own control now, and the profile it shows has to survive a fan
            // choice — otherwise picking a fan speed silently drops the power
            // envelope, and the graphics power the user asked for slides back
            // to its base draw a moment later. Maximum owns the profile itself,
            // so it is left alone.
            if(request == FanRequest.Constant)
                SetPerformanceSticky();

            this.Host.RefreshFanState();

        }

        // Saves the drawn curve as a fan program and runs it, which is what
        // the separate curve dialog used to do
        private void ApplyCurve() {

            if(this.Host == null)
                return;

            try {

                Config.FanProgram[CurveProgramName] = BuildProgram(CurveProgramName);
                Config.Save();

                // The list has just gained or replaced an entry, so it is
                // rebuilt: without this, applying a curve for the first time
                // left the programs panel claiming there were none
                this.CoolingModel.Reload(CurveProgramName);

                this.Host.Platform.ClearFanModeSticky();
                this.Host.Program.Run(CurveProgramName);
                this.Host.RefreshFanState();

                this.CurveModel.Status = Config.Locale.Get("GuiWpfApplied");

            } catch(Exception e) {

                this.CurveModel.Status = string.Format(
                    Config.Locale.Get("GuiWpfCouldNotApply"), e.Message);
                Logger.Error("Window", "Applying the fan curve failed", e.Message);

            }

        }

        // Windows' own power mode.
        //
        // SystemMetrics has been able to read and set this since it was
        // written, FeatureSupport advertises it as a supported capability, and
        // both locale files carry a menu string for it — and there was no
        // control anywhere in the application. Cheap and instantly reversible,
        // which is why it is a plain segmented selector rather than something
        // behind a confirmation.
        private void OnSystemChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(e.PropertyName != "PowerMode" || this.IsApplyingReading)
                return;

            Hardware.SystemMetrics.PowerMode mode;

            switch(this.SystemModel.PowerMode) {
                case "HighPerformance":
                    mode = Hardware.SystemMetrics.PowerMode.HighPerformance; break;
                case "PowerSaver":
                    mode = Hardware.SystemMetrics.PowerMode.PowerSaver; break;
                case "Balanced":
                    mode = Hardware.SystemMetrics.PowerMode.Balanced; break;
                default:
                    return;
            }

            Guard(() => Hardware.SystemMetrics.SetPowerMode(mode),
                "Power mode: " + mode);

            // The reading already in flight when the click happened still
            // carries the old mode. Trusting the user's choice over the
            // hardware for a moment is the same bargain the fan controls
            // strike, and for the same reason.
            this.PowerModeSetAt = Environment.TickCount;

        }

        // When the power mode was last written from here
        private int PowerModeSetAt = int.MinValue;

        // How long the user's choice outranks what the machine reports. Short:
        // the mode is read every tick now, so one second is several readings.
        private const int PowerModeSettleMs = 2500;

        // Which program to run, given what the user last had in front of them.
        //
        // In order: the one already named — the Cooling section's selection,
        // or the one that was running when the window opened — then the
        // configured default, then whatever the configuration holds first.
        // Empty only when the machine has no saved programs at all.
        private static string ResolveProgram(string named) {

            if(Config.FanProgram == null || Config.FanProgram.Count == 0)
                return "";

            if(!string.IsNullOrEmpty(named) && Config.FanProgram.ContainsKey(named))
                return named;

            string preferred = Config.FanProgramDefault;
            if(!string.IsNullOrEmpty(preferred) && Config.FanProgram.ContainsKey(preferred))
                return preferred;

            return Config.FanProgram.Keys[0];

        }

        // Starts a saved program.
        //
        // This is what the dashboard's Program segment never had behind it.
        // FanControl.Apply returns immediately on an empty name, and nothing
        // in the window had ever set one, so the button was live, did nothing,
        // and said nothing about doing nothing.
        private void RunProgram(string name) {

            if(this.Host == null || string.IsNullOrEmpty(name))
                return;

            if(Config.FanProgram == null || !Config.FanProgram.ContainsKey(name)) {
                this.CoolingModel.Status = Name("GuiWpfProgramGone");
                return;
            }

            Guard(delegate {
                this.Host.Platform.ClearFanModeSticky();
                this.Host.Program.Run(name);
            }, "Running the fan program " + name);

            // The dashboard's own selector follows, so the two pages cannot
            // disagree about what the machine is doing
            this.DashboardModel.ProgramName = name;
            this.DashboardModel.Mode = FanMode.Program;

            this.Host.RefreshFanState();
            this.CoolingModel.SetRunning(name);
            this.CoolingModel.Status = string.Format(Name("GuiWpfProgramRunning"), name);

            Requested();

        }

        private void StopProgram() {

            if(this.Host == null)
                return;

            Guard(() => this.Host.Program.Terminate(), "Stopping the fan program");

            this.Host.RefreshFanState();
            this.CoolingModel.SetRunning("");
            this.CoolingModel.Status = Name("GuiWpfProgramStopped");

            Requested();

        }

        // Saves the drawn curve as a program of its own.
        //
        // The editor could previously only write one program, called Curve,
        // which it overwrote every time — so a second curve meant losing the
        // first, and the configuration file's whole list was unreachable from
        // the interface.
        private void SaveProgram(string name) {

            if(string.IsNullOrEmpty(name))
                return;

            try {

                Config.FanProgram[name] = BuildProgram(name);
                Config.Save();

                this.CoolingModel.Reload(this.DashboardModel.ProgramName);
                this.CoolingModel.NewName = "";
                this.CoolingModel.Status = string.Format(Name("GuiWpfProgramSaved"), name);

                Logger.Gui("Window", "Saved the fan program " + name);

            } catch(Exception e) {

                this.CoolingModel.Status = string.Format(
                    Name("GuiWpfCouldNotApply"), e.Message);
                Logger.Error("Window", "Saving the fan program failed", e.Message);

            }

        }

        private void DeleteProgram(string name) {

            if(string.IsNullOrEmpty(name)
                || Config.FanProgram == null || !Config.FanProgram.ContainsKey(name))
                return;

            try {

                // A running program that is deleted has to be stopped as well,
                // or the machine keeps following a curve that no longer exists
                // anywhere the user can see it
                if(this.Host != null && this.Host.Program.IsEnabled
                    && this.Host.Program.GetName() == name)
                    StopProgram();

                Config.FanProgram.Remove(name);
                Config.Save();

                this.CoolingModel.Reload(this.DashboardModel.ProgramName);
                this.CoolingModel.Status = string.Format(Name("GuiWpfProgramDeleted"), name);

                Logger.Gui("Window", "Deleted the fan program " + name);

            } catch(Exception e) {

                this.CoolingModel.Status = string.Format(
                    Name("GuiWpfCouldNotApply"), e.Message);
                Logger.Error("Window", "Deleting the fan program failed", e.Message);

            }

        }

        // Selecting a program in the list loads its curve into the editor, so
        // the two halves of the page are looking at the same thing
        private void OnCoolingChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(e.PropertyName != "Selected" || this.IsApplyingReading)
                return;

            FanProgramViewModel selected = this.CoolingModel.Selected;
            if(selected == null || Config.FanProgram == null)
                return;

            Hardware.Platform.FanProgramData program;
            if(!Config.FanProgram.TryGetValue(selected.Name, out program) || program == null)
                return;

            this.CurveModel.Percent = ToCurve(program);

        }

        // The drawn curve as a program, and back. Both directions live in
        // AppService.FanCurve, where the tests can reach them without a
        // window: a conversion that is quietly wrong is not a crash, it is a
        // machine cooling itself differently from the picture on the screen.
        private Hardware.Platform.FanProgramData BuildProgram(string name) {

            return FanCurve.ToProgram(name, FanCurveViewModel.Columns,
                this.CurveModel.Percent, Config.FanLevelMax,
                Hardware.Bios.BiosData.FanMode.Performance,
                Hardware.Bios.BiosData.GpuPowerLevel.Minimum);

        }

        private static int[] ToCurve(Hardware.Platform.FanProgramData program) {
            return FanCurve.ReadCurve(program,
                FanCurveViewModel.Columns, Config.FanLevelMax);
        }

        // The reserved program name the drawn curve is saved under. ASCII, so
        // it is safe as a configuration key whatever the interface language.
        private const string CurveProgramName = "Curve";

        private static string Name(string key) {
            return Config.Locale.Get(key);
        }

        // The firmware's performance profile, in the user's language.
        //
        // The profile is carried around as the bare enum name because that is
        // what gets parsed back and written to the firmware — translating the
        // stored value would break the round trip. But the bare name is also
        // what reached the screen, so a Turkish panel read "Mod — Performance"
        // directly above a selector offering "Performans". The translation
        // belongs here, at the last step, and nowhere earlier.
        //
        // A profile this build has no key for falls back to its own name
        // rather than to the key: Locale.Get returns the key it was given when
        // there is no entry, and "ProgModeWhatever" on screen is worse than
        // the untranslated word.
        private static string FanModeName(string mode) {

            if(string.IsNullOrEmpty(mode))
                return "";

            string key = Config.L_PROG + "Mode" + mode;
            string name = Config.Locale.Get(key);

            return name == key ? mode : name;

        }

        // Runs an action the user asked for, and logs it either way: the log
        // is the application's memory, and a log that only speaks when
        // something breaks reads as an application that never does anything.
        private static void Guard(Action action, string what) {
            try {
                action();
                Logger.Gui("Window", what);
            } catch(Exception e) {
                Logger.Error("Window", what + " failed", e.Message);
            }
        }
#endregion

        // Applies a reading. Called from the poller's background thread, so
        // the whole thing is moved across in one go rather than property by
        // property: a binding updated from the wrong thread throws, and one
        // updated from the right thread in twenty separate posts makes the
        // window redraw twenty times for one tick.
        public void Apply(Reading reading) {

            if(this.Dispatcher.CheckAccess())
                ApplyHere(reading);
            else
                this.Dispatcher.BeginInvoke(
                    (Action) delegate { ApplyHere(reading); });

        }

        private void ApplyHere(Reading reading) {

            // Writing a reading into the view model raises the same change
            // notifications the user's own edits do. Without this flag the
            // window would answer every one of them by asking the hardware
            // for the state it had just been told — once a second, for ever.
            this.IsApplyingReading = true;

            try {

                DashboardViewModel model = this.DashboardModel;

                // The processor is the lead card. Its supporting line carries
                // the same three figures in the same order every card does —
                // load, power, clock — so the eye reads down the row without
                // relearning the layout at each card.
                model.Cpu.SetTemperature(reading.CpuTemperature, Line(
                    Percent(reading.CpuLoadPercent),
                    Describe(reading.CpuWatts, " W", 0),
                    reading.CpuGigahertz > 0
                        ? reading.CpuGigahertz.ToString("F2",
                            System.Globalization.CultureInfo.InvariantCulture) + " GHz" : ""));
                model.Cpu.Unit = "°C";

                // The per-core strip along the lead card
                model.CoreTemperatures = reading.CpuCoreTemperatures ?? new int[0];

                // The card prefers the NVIDIA card's own temperature to the
                // board sensor when it can get it, and carries the load, power
                // and clock underneath — the numbers a game is watched with,
                // which the Embedded Controller never had to give
                int gpuTemp = reading.GpuNvidiaPresent && reading.GpuNvidiaTemp > 0
                    ? reading.GpuNvidiaTemp : reading.GpuTemperature;
                model.Gpu.SetTemperature(gpuTemp, GpuDetail(reading));
                model.Gpu.Unit = "°C";

                // The fans are shown as a percentage of their ceiling rather
                // than a raw rpm: the Embedded Controller's rate reading on this
                // hardware is not a real tachometer figure, and a level out of
                // the maximum is both honest and reads at a glance
                model.FanCpu.Figure = FanPercent(reading.FanLevelCpu, reading.FanLevelMaximum);
                model.FanCpu.Second = FanPercent(reading.FanLevelGpu, reading.FanLevelMaximum);
                model.FanCpu.Unit = "%";
                model.FanCpu.Detail = Levels(reading);
                model.FanCpu.Portion =
                    FanPercentValue(reading.FanLevelCpu, reading.FanLevelMaximum) / 100.0;

                ApplyBattery(model, reading);

                model.LevelMaximum = reading.FanLevelMaximum > 0
                    ? reading.FanLevelMaximum : Config.FanLevelMax;
                model.IsProgramRunning = reading.IsProgramRunning;
                model.ProgramName = reading.ProgramName;

                ApplyMode(model, reading);

                // The control stays live even where the firmware will not
                // report the level back.
                //
                // GpuPowerSupported only ever meant "the read was refused",
                // and this board refuses it while still accepting the write —
                // its card defaults to 60 W and goes to 80 W, and asking is
                // the only way there. Disabling the control on a failed read
                // took that away. The selector simply keeps what the user
                // chose, because there is nothing to read it back from.
                model.IsGpuPowerSupported = true;

                if(reading.GpuPowerSupported)
                    model.GraphicsPower = reading.GpuPower;

                model.HasExtremeMode = Hardware.DeviceProfile.ExtremeMode;

                ApplyKeyboard(reading);

                this.CurveModel.CurrentTemperature = reading.MaxTemperature;
                this.CurveModel.IsRunning = reading.IsProgramRunning;

                ApplyCoolingState(reading);

                // The chart records whatever it can. A reading of zero is a
                // gap rather than a value, which the buffer already knows. The
                // fans go in as a percentage of the ceiling, to match the cards
                // and share the load's scale.
                model.History.Push(
                    reading.CpuTemperature,
                    reading.GpuTemperature,
                    FanPercentValue(reading.FanLevelCpu, reading.FanLevelMaximum),
                    FanPercentValue(reading.FanLevelGpu, reading.FanLevelMaximum),
                    reading.CpuLoadPercent > 0 ? reading.CpuLoadPercent : 0,
                    reading.CpuWatts > 0 ? reading.CpuWatts : 0);

                ApplyDetails(model, reading);
                ApplyBlocks(model, reading, gpuTemp);
                ApplySummary(reading, gpuTemp);

                // Whether the Program segment can do anything. Checked against
                // the configuration rather than against what is running, so
                // the button is live when a program exists and disabled when
                // there is nothing for it to start.
                model.HasProgram = Config.FanProgram != null && Config.FanProgram.Count > 0;

                if(this.Dashboard != null)
                    this.Dashboard.RefreshChart();

            } catch(Exception e) {

                Logger.Error("Window", "Applying a reading failed", e.Message);

            } finally {

                this.IsApplyingReading = false;

            }

        }

        // What the machine will let the application do about cooling.
        //
        // Every one of these was known at the first reading and reached
        // nothing: DeviceProfile works out the ceiling, where it came from,
        // whether the firmware offers software fan control at all and which
        // path the levels take, and the only place any of it went was one log
        // line. HpBiosSettings reads the BIOS setup menu and the one answer
        // anybody actually wants from it — whether the fans are configured
        // never to stop — was unreachable.
        private void ApplyCoolingState(Reading reading) {

            CoolingViewModel cooling = this.CoolingModel;
            if(cooling == null)
                return;

            DetailGroupViewModel state = cooling.State;

            Set(state, 0, Ceiling(reading));
            Set(state, 1, Countdown(reading));
            Set(state, 2, YesNo(Hardware.DeviceProfile.SoftwareFanControl));
            Set(state, 3, YesNo(Hardware.HpBiosSettings.FanAlwaysOn));
            Set(state, 4, reading.FanCount > 0 ? reading.FanCount.ToString() : "");
            Set(state, 5, Hardware.DeviceProfile.BiosFanLevel ? "BIOS" : "EC");
            Set(state, 6, reading.IsThermalProtectionActive
                ? Name("GuiWpfGuardActive") : Name("GuiWpfGuardIdle"));

            cooling.SetRunning(reading.IsProgramRunning ? reading.ProgramName ?? "" : "");

        }

        // Three states, not two: a machine that does not say is not the same
        // as one that says no, and showing "no" for both is a lie the user
        // cannot check
        private static string YesNo(bool? value) {

            if(value == null)
                return Name("GuiWpfUnknown");

            return value.Value ? Name("GuiWpfYes") : Name("GuiWpfNo");

        }

        private static string YesNo(bool value) {
            return value ? Name("GuiWpfYes") : Name("GuiWpfNo");
        }

        // The strip under the tabs, from the same reading the cards get.
        //
        // The badges are the part that was missing altogether. The poller has
        // been setting Reading.IsThermalProtectionActive since it was written
        // and nothing in the interface has ever read it: when the guard forced
        // the fans to maximum, the dashboard's fan selector moved to Maximum
        // on its own and there was nowhere at all to learn that the
        // application had done it. Same for the processor being held back —
        // the throttle status was collected, shown in one detail row on one
        // page, and easy to sit next to for an hour without noticing.
        private void ApplySummary(Reading reading, int gpuTemperature) {

            SummaryViewModel strip = this.SummaryModel;
            if(strip == null)
                return;

            strip.Cpu.SetTemperature(reading.CpuTemperature, "");
            strip.Cpu.Unit = "°C";

            strip.Gpu.SetTemperature(gpuTemperature, "");
            strip.Gpu.Unit = "°C";

            // The pair, not one of them: the firmware drives the two fans
            // together and either alone is half the answer
            strip.Fan.Figure = FanPair(reading);
            strip.Fan.Unit = "%";

            strip.Battery.Figure = reading.BatteryPercent >= 0
                ? reading.BatteryPercent.ToString() : "-";
            strip.Battery.Unit = "%";
            strip.Battery.Health = reading.BatteryPercent >= 0
                ? HealthScale.FromCharge(reading.BatteryPercent) : Health.Neutral;
            strip.Battery.Detail = BatteryState(reading);

            // The System section's selector follows what Windows actually
            // reports, so a change made from the battery flyout shows here —
            // but not while a change made from here is still settling
            if(reading.PowerMode.Length > 0
                && unchecked(Environment.TickCount - this.PowerModeSetAt) > PowerModeSettleMs)
                this.SystemModel.PowerMode = reading.PowerMode;

            strip.IsThermalProtection = reading.IsThermalProtectionActive;
            strip.IsProgramRunning = reading.IsProgramRunning;
            strip.ProgramName = reading.ProgramName ?? "";

            string throttle = reading.Throttle ?? "";
            strip.IsThrottling = throttle.Length > 0;
            strip.ThrottleText = throttle;

            strip.Push(reading.CpuTemperature, gpuTemperature);

        }

        // Both fans as one figure. A single slash rather than two labelled
        // readings: at strip size the label costs more room than the second
        // number is worth, and the two are almost always within a few points
        // of each other anyway.
        private static string FanPair(Reading reading) {

            int cpu = FanPercentValue(reading.FanLevelCpu, reading.FanLevelMaximum);
            int gpu = FanPercentValue(reading.FanLevelGpu, reading.FanLevelMaximum);

            if(cpu <= 0 && gpu <= 0)
                return "-";

            return cpu == gpu ? cpu.ToString() : cpu + "/" + gpu;

        }

        // Charging, discharging, or full on the mains. Short enough to sit on
        // the strip beside the percentage.
        private static string BatteryState(Reading reading) {

            if(reading.BatteryPercent < 0)
                return "";

            // The same three words the battery card uses, from the same keys:
            // the strip and the card must never describe one state two ways
            if(reading.BatteryCharging)
                return Config.Locale.Get("GuiWpfBatCharging");

            return reading.BatteryOnAc
                ? Config.Locale.Get("GuiWpfBatAc")
                : Config.Locale.Get("GuiWpfBatDc");

        }

        // How long a request is trusted over what the hardware reports.
        //
        // Asking the firmware for something and reading it back are not the
        // same transaction: a mode set now may not be visible for a tick or
        // two, and a reading taken in between says the old thing. Without
        // this the selector moves where the user put it and then jumps back a
        // second later, which reads as the application refusing the request.
        private const int SettleMilliseconds = 4000;

        private int RequestedAt;
        private bool HasRequested;

        // Marks the moment the user asked for something
        private void Requested() {
            this.RequestedAt = Environment.TickCount;
            this.HasRequested = true;
        }

        private bool IsSettling {
            get {
                return this.HasRequested
                    && unchecked(Environment.TickCount - this.RequestedAt)
                        < SettleMilliseconds;
            }
        }

        // Whether the fan mode has ever been settled from a reading. Until it
        // has, the very first reading is allowed to set it — so opening the
        // window shows the state the machine is actually in — after which the
        // user's choice is what holds.
        private bool ModeKnown;

        // Keeps the fan-mode selector honest without letting an ambiguous
        // reading move it.
        //
        // The trap this machine sets: in Automatic the firmware spins the fans
        // itself and reports the levels they happen to be at, which looks
        // exactly like the levels a user set for Constant — and it reports no
        // manual bit either way, because this Victus does not use one. So a
        // reading genuinely cannot tell Automatic from Constant, and deriving
        // the selector from one snapped it back to Constant every second.
        //
        // Only the unambiguous states are ever read from the hardware: a fan
        // program running, or the firmware's own maximum flag — those the
        // firmware reports plainly and the user may not have set (thermal
        // protection forces maximum). Automatic versus Constant is the user's
        // last choice, left untouched by readings.
        private void ApplyMode(DashboardViewModel model, Reading reading) {

            // While a request is settling the selector stays where the user
            // put it. The hardware has not caught up yet.
            if(this.IsSettling)
                return;

            if(reading.IsProgramRunning) {
                model.Mode = FanMode.Program;
                this.ModeKnown = true;
            } else if(reading.FanIsMax) {
                model.Mode = FanMode.Maximum;
                this.ModeKnown = true;
            } else if(!this.ModeKnown) {

                // The first reading, and no unambiguous state: seed the
                // selector from whatever can be inferred, once. After this the
                // user's choice holds and readings no longer move it.
                FanRequest seed = FanControl.Identify(
                    reading.IsProgramRunning, reading.FanIsMax, reading.FanIsOff,
                    reading.FanIsManual,
                    reading.FanLevelCpu, reading.FanLevelGpu,
                    reading.FanLevelMaximum > 0
                        ? reading.FanLevelMaximum : Config.FanLevelMax);

                model.Mode = seed == FanRequest.Constant
                    ? FanMode.Constant : FanMode.Automatic;
                this.ModeKnown = true;

            } else if(model.Mode == FanMode.Program || model.Mode == FanMode.Maximum) {

                // The machine left a program or maximum on its own (a program
                // finished, thermal protection released): fall back to
                // Automatic rather than staying on a mode no longer in force.
                model.Mode = FanMode.Automatic;

            }

            // The firmware profile the machine reports it is in, shown on the
            // performance selector. Behind the same settle guard as the mode:
            // a reading taken just after a change still names the old one.
            if(!string.IsNullOrEmpty(reading.FanMode))
                model.PerformanceMode = reading.FanMode;

        }

        // The details panel.
        //
        // The rows are made once and then written to, rather than the groups
        // being rebuilt each second. Replacing the collection would rebuild
        // every element under it — and would lose the scroll position, once a
        // second, which makes the panel impossible to read on a busy machine.
        private void ApplyDetails(DashboardViewModel model, Reading reading) {

            if(model.Details.Count == 0) {

                // 0 — the machine itself
                model.Details.Add(new DetailGroupViewModel(Name("GuiWpfSystem"))
                    .Add(Name("GuiWpfModel"), "", Name("GuiTipModel"))
                    .Add(Name("GuiWpfBios"), "", Name("GuiTipBios"))
                    .Add(Name("GuiMainDetPlan"), "", Name("GuiTipPlan"))
                    .Add(Name("GuiWpfRowMode"), "", Name("GuiTipPowerMode"))
                    // The row this key was written for. It has existed in both
                    // languages since the panel was first laid out, the field
                    // behind it has existed in Reading, and no one had ever
                    // assigned either.
                    .Add(Name("GuiMainDetUptime"), "", Name("GuiTipUptime")));

                // 1 — processor. Temperature first, then load, power, clock:
                // the same order as the card.
                model.Details.Add(new DetailGroupViewModel(Name("GuiWpfCpu"))
                    .Add(Name("GuiWpfTemp"), "", Name("GuiTipCpuTemp"))
                    .Add(Name("GuiMainDetLoad"), "", Name("GuiTipCpuLoad"))
                    .Add(Name("GuiMainDetPower"), "", Name("GuiTipCpuPower"))
                    .Add(Name("GuiWpfPowerLimit"), "", Name("GuiTipCpuLimit"))
                    .Add(Name("GuiMainDetClock"), "", Name("GuiTipCpuClock"))
                    .Add(Name("GuiMainDetThrottle"), "", Name("GuiTipThrottle"))
                    .Add(Name("GuiWpfCores"), "", Name("GuiTipCores"))
                    .Add(Name("GuiWpfCoreClocks"), "", Name("GuiWpfCoreClockTip")));

                // 2 — discrete graphics
                model.Details.Add(new DetailGroupViewModel(Name("GuiWpfGraphics"))
                    .Add(Name("GuiWpfTemp"), "", Name("GuiTipGpuTemp"))
                    .Add(Name("GuiMainDetLoad"), "", Name("GuiTipGpuLoad"))
                    .Add(Name("GuiMainDetPower"), "", Name("GuiTipGpuPower"))
                    .Add(Name("GuiWpfGpuPowerLimit"), "", Name("GuiTipGpuLimit"))
                    .Add(Name("GuiWpfGpuClock"), "", Name("GuiTipGpuClock"))
                    .Add(Name("GuiWpfRowMemClock"), "", Name("GuiTipGpuClock"))
                    .Add(Name("GuiWpfGpuVram"), "", Name("GuiTipVram")));

                // 3 — memory
                model.Details.Add(new DetailGroupViewModel(Name("GuiWpfMemory"))
                    .Add(Name("GuiMainDetLoad"), "", Name("GuiTipMemLoad"))
                    .Add(Name("GuiWpfMemUsed"), "", Name("GuiTipMemUsed")));

                // 4 — storage and network
                model.Details.Add(new DetailGroupViewModel(Name("GuiWpfStorageNet"))
                    .Add(Name("GuiWpfDisk"), "", Name("GuiTipDisk"))
                    // What the drive and the link are actually doing, as
                    // opposed to how hot one is and how fast the other could
                    // go. Both meters had been written and never called.
                    .Add(Name("GuiWpfDiskRate"), "", Name("GuiTipDiskRate"))
                    .Add(Name("GuiWpfWifi"), "", Name("GuiTipWifi"))
                    .Add(Name("GuiWpfLinkSpeed"), "", Name("GuiTipLink"))
                    .Add(Name("GuiWpfNetRate"), "", Name("GuiTipNetRate")));

                // 5 — battery
                model.Details.Add(new DetailGroupViewModel(Name("GuiWpfBattery"))
                    .Add(Name("GuiMainBatTipHealth"), "", Name("GuiTipBatHealth"))
                    .Add(Name("GuiWpfBatCycles"), "", Name("GuiTipBatCycles"))
                    .Add(Name("GuiWpfBatCapacity"), "", Name("GuiTipBatCapacity"))
                    .Add(Name("GuiMainBatTipRemaining"), "", Name("GuiTipBatRemaining"))
                    .Add(Name("GuiWpfBatPower"), "", Name("GuiTipBatDraw"))
                    .Add(Name("GuiWpfBatState"), "", Name("GuiTipBatState")));

            }

            // 6 — the fans and the board's own probes. Built on the first
            // reading rather than with the rest, because which probes this
            // machine has is not known until one has been taken, and a row
            // for a sensor that does not exist is worse than no row.
            if(model.Details.Count == 6 && reading.BoardSensors != null) {

                DetailGroupViewModel fans = new DetailGroupViewModel(Name("GuiWpfFansBoard"))
                    .Add(Name("GuiWpfFanCpuRpm"), "", Name("GuiTipFanRpm"))
                    .Add(Name("GuiWpfFanGpuRpm"), "", Name("GuiTipFanRpm"))
                    .Add(Name("GuiWpfRowCountdown"), "", Name("GuiTipCountdown"));

                this.BoardSensorNames = new string[reading.BoardSensors.Length];
                for(int i = 0; i < reading.BoardSensors.Length; i++) {
                    this.BoardSensorNames[i] = reading.BoardSensors[i].Key;
                    fans.Add(reading.BoardSensors[i].Key, "", Name("GuiTipBoardSensor"));
                }

                // The firmware's own fan readings, where it publishes any.
                // These were being read on every slow tick and thrown away:
                // the poller kept the temperature rows out of the HP sensor
                // class and dropped the fan rows on the floor. On a board
                // whose Embedded Controller tachometer is unreliable they are
                // the only honest speed the machine will give.
                this.HpFanNames = reading.HpFanRpm != null
                    ? new string[reading.HpFanRpm.Length] : new string[0];

                for(int i = 0; i < this.HpFanNames.Length; i++) {
                    this.HpFanNames[i] = reading.HpFanRpm[i].Key;
                    fans.Add(reading.HpFanRpm[i].Key, "", Name("GuiTipHpFan"));
                }

                fans.Add(Name("GuiWpfSensorHealth"), "", Name("GuiTipSensorHealth"));

                model.Details.Add(fans);

            }

            // The machine — its real name and firmware, not the application's.
            // These are what the labels have always claimed to show.
            Set(model, 0, 0, reading.SystemModel);
            Set(model, 0, 1, reading.BiosVersion);
            Set(model, 0, 2, reading.PowerPlan);
            Set(model, 0, 3, PowerModeName(reading.PowerMode));
            Set(model, 0, 4, reading.Uptime);

            Set(model, 1, 0, reading.CpuTemperature > 0
                ? reading.CpuTemperature + " °C" : "-");
            Set(model, 1, 1, Percent(reading.CpuLoadPercent));
            Set(model, 1, 2, Describe(reading.CpuWatts, " W", 0));
            Set(model, 1, 3, PowerLimits(reading));
            Set(model, 1, 4, reading.CpuGigahertz > 0
                ? reading.CpuGigahertz.ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture) + " GHz" : "-");
            Set(model, 1, 5, reading.Throttle);
            Set(model, 1, 6, Cores(reading.CpuCoreTemperatures));
            Set(model, 1, 7, CoreClocks(reading.CpuCoreClocks));

            Set(model, 2, 0, reading.GpuNvidiaPresent && reading.GpuNvidiaTemp > 0
                ? reading.GpuNvidiaTemp + " °C" : "-");
            Set(model, 2, 1, reading.GpuNvidiaPresent && reading.GpuNvidiaLoad >= 0
                ? reading.GpuNvidiaLoad + " %" : "-");
            Set(model, 2, 2, reading.GpuNvidiaPresent && reading.GpuNvidiaPowerW > 0
                ? reading.GpuNvidiaPowerW + " W" : "-");
            Set(model, 2, 3, reading.GpuNvidiaPresent && reading.GpuNvidiaPowerLimitW > 0
                ? reading.GpuNvidiaPowerLimitW + " W" : "-");
            Set(model, 2, 4, reading.GpuNvidiaPresent && reading.GpuNvidiaCoreMhz > 0
                ? reading.GpuNvidiaCoreMhz + " MHz" : "-");
            Set(model, 2, 5, reading.GpuNvidiaPresent && reading.GpuNvidiaMemMhz > 0
                ? reading.GpuNvidiaMemMhz + " MHz" : "-");
            Set(model, 2, 6, Vram(reading));

            Set(model, 3, 0, Percent(reading.MemoryPercent));
            Set(model, 3, 1, Memory(reading));

            Set(model, 4, 0, reading.DiskTemperature > 0
                ? reading.DiskTemperature + " °C" : "-");
            Set(model, 4, 1, Storage(reading));
            Set(model, 4, 2, reading.WifiConnected
                ? (reading.WifiSsid.Length > 0 ? reading.WifiSsid : "Wi-Fi")
                    + (reading.WifiSignalPercent >= 0
                        ? " · " + reading.WifiSignalPercent + " %" : "")
                : "-");
            Set(model, 4, 3, reading.WifiConnected && reading.WifiRxMbps > 0
                ? reading.WifiRxMbps
                    + (reading.WifiTxMbps > 0
                        && reading.WifiTxMbps != reading.WifiRxMbps
                        ? " / " + reading.WifiTxMbps : "") + " Mb/s"
                : "-");
            Set(model, 4, 4, Network(reading));

            Set(model, 5, 0, Percent(reading.BatteryHealthPercent));
            Set(model, 5, 1, reading.BatteryCycleCount >= 0
                ? reading.BatteryCycleCount.ToString() : "-");
            Set(model, 5, 2, Capacity(reading));
            Set(model, 5, 3, reading.BatteryMinutesLeft > 0
                ? (reading.BatteryMinutesLeft / 60) + "h "
                    + (reading.BatteryMinutesLeft % 60) + "m" : "-");
            Set(model, 5, 4, !double.IsNaN(reading.BatteryWatts)
                && System.Math.Abs(reading.BatteryWatts) > 0.05
                ? System.Math.Abs(reading.BatteryWatts).ToString("F1",
                    System.Globalization.CultureInfo.InvariantCulture) + " W"
                : "-");
            Set(model, 5, 5, !reading.BatteryPresent ? "-"
                : reading.BatteryCharging ? Name("GuiWpfBatCharging")
                : reading.BatteryOnAc ? Name("GuiWpfBatAc") : Name("GuiWpfBatDc"));

            ApplyFanDetails(model, reading);

        }

        // The names of the board probes, in the order the rows were built, so
        // a reading whose sensor list has shifted is matched by name rather
        // than by position
        private string[] BoardSensorNames;

        // The same, for the firmware's own published fan sensors
        private string[] HpFanNames;

        // A named sensor's reading, or zero where it has dropped out of this
        // tick's list
        private static int Value(
            System.Collections.Generic.KeyValuePair<string, int>[] sensors, string name) {

            if(sensors == null)
                return 0;

            foreach(System.Collections.Generic.KeyValuePair<string, int> sensor in sensors)
                if(sensor.Key == name)
                    return sensor.Value;

            return 0;

        }

        // The per-core clocks as one line: the range they are spread across
        // and how many there are. The strip on the dashboard draws each one;
        // this says the same thing in a row.
        private static string CoreClocks(int[] clocks) {

            if(clocks == null || clocks.Length == 0)
                return "";

            int low = int.MaxValue, high = 0, seen = 0;

            foreach(int clock in clocks) {
                if(clock <= 0)
                    continue;
                if(clock < low) low = clock;
                if(clock > high) high = clock;
                seen++;
            }

            if(seen == 0)
                return "";

            string range = low == high
                ? Ghz(high)
                : Ghz(low) + " - " + Ghz(high);

            return range + " · " + seen;

        }

        private static string Ghz(int megahertz) {
            return (megahertz / 1000.0).ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        // The fan and board-probe rows. Separate because the group is built
        // from the first reading rather than declared, so writing to it has to
        // survive the group not being there yet.
        private void ApplyFanDetails(DashboardViewModel model, Reading reading) {

            if(model.Details.Count < 7 || this.BoardSensorNames == null)
                return;

            Set(model, 6, 0, Rpm(reading.FanRpmCpu));
            Set(model, 6, 1, reading.FanCount > 1 ? Rpm(reading.FanRpmGpu) : "-");
            Set(model, 6, 2, Countdown(reading));

            int row = 3;

            for(int i = 0; i < this.BoardSensorNames.Length; i++)
                Set(model, 6, row++, Lookup(reading.BoardSensors, this.BoardSensorNames[i]));

            if(this.HpFanNames != null)
                for(int i = 0; i < this.HpFanNames.Length; i++)
                    Set(model, 6, row++,
                        Rpm(Value(reading.HpFanRpm, this.HpFanNames[i])));

            int health = row;
            Set(model, 6, health,
                reading.SensorFaults == null || reading.SensorFaults.Length == 0
                    ? Name("GuiWpfSensorHealthOk")
                    : string.Join(", ", reading.SensorFaults)
                        + " — " + Name("GuiWpfSensorHealthBad"));

        }

        // A probe's reading by name, so the rows stay with their sensors even
        // if one drops out of a reading and shifts the rest along
        private static string Lookup(
            System.Collections.Generic.KeyValuePair<string, int>[] sensors, string name) {

            if(sensors != null)
                foreach(System.Collections.Generic.KeyValuePair<string, int> sensor in sensors)
                    if(sensor.Key == name)
                        return sensor.Value > 0 ? sensor.Value + " °C" : "-";

            return "-";

        }

        // A fan speed, or a dash where the firmware does not count one. Zero
        // is "did not answer" rather than "stopped": a stopped fan on these
        // machines still reports, and showing 0 rpm for a fan that is plainly
        // running is the kind of number that makes the rest suspect.
        private static string Rpm(int rpm) {
            return rpm > 0
                ? rpm.ToString(Config.FormatFanSpeed,
                    System.Globalization.CultureInfo.CurrentCulture) + " rpm"
                : "-";
        }

        // The processor's power budgets, sustained and burst: "45 / 60 W",
        // or just the one when the two match. The figures a laptop's
        // processor is really named after.
        private static string PowerLimits(Reading reading) {

            if(reading.CpuPl1W <= 0)
                return "-";

            System.Globalization.CultureInfo inv =
                System.Globalization.CultureInfo.InvariantCulture;

            int pl1 = (int) System.Math.Round(reading.CpuPl1W);
            int pl2 = (int) System.Math.Round(reading.CpuPl2W);

            return reading.CpuPl2W > 0 && pl2 != pl1
                ? pl1 + " / " + pl2 + " W"
                : pl1 + " W";

        }

        // Physical memory as used-of-total in gigabytes
        private static string Memory(Reading reading) {

            if(reading.MemoryTotalGB <= 0)
                return "-";

            System.Globalization.CultureInfo inv =
                System.Globalization.CultureInfo.InvariantCulture;

            return reading.MemoryUsedGB.ToString("F1", inv)
                + " / " + reading.MemoryTotalGB.ToString("F1", inv) + " GB";

        }

        // Battery capacity as full-of-designed in watt-hours, the figure that
        // makes the health percentage concrete
        private static string Capacity(Reading reading) {

            if(reading.BatteryFullmWh <= 0)
                return "-";

            System.Globalization.CultureInfo inv =
                System.Globalization.CultureInfo.InvariantCulture;

            string full = (reading.BatteryFullmWh / 1000.0).ToString("F1", inv);

            return reading.BatteryDesignmWh > 0
                ? full + " / " + (reading.BatteryDesignmWh / 1000.0).ToString("F1", inv) + " Wh"
                : full + " Wh";

        }

        // The per-core temperatures reduced to a line: the hottest core and
        // how many there are. The whole column of twenty numbers belongs in a
        // view built for it, not in a supporting row of the details panel.
        private static string Cores(int[] temperatures) {

            if(temperatures == null || temperatures.Length == 0)
                return "-";

            int hottest = 0;
            foreach(int t in temperatures)
                if(t > hottest)
                    hottest = t;

            return hottest > 0
                ? hottest + " °C · " + temperatures.Length
                : "-";

        }

        // Video memory as used-of-total in gigabytes, the way a task manager
        // shows it. Blank when there is no discrete card to report it.
        private static string Vram(Reading reading) {

            if(!reading.GpuNvidiaPresent || reading.GpuNvidiaVramTotalMB <= 0)
                return "-";

            System.Globalization.CultureInfo inv =
                System.Globalization.CultureInfo.InvariantCulture;

            return (reading.GpuNvidiaVramUsedMB / 1024.0).ToString("F1", inv)
                + " / " + (reading.GpuNvidiaVramTotalMB / 1024.0).ToString("F1", inv)
                + " GB";

        }

        private static void Set(DashboardViewModel model, int group, int row, string value) {

            if(group >= model.Details.Count)
                return;

            DetailGroupViewModel rows = model.Details[group];
            if(row < rows.Rows.Count)
                rows.Rows[row].Value = string.IsNullOrEmpty(value) ? "-" : value;

        }

        private static string Percent(int value) {
            return value >= 0 ? value + " %" : "-";
        }

        private static void ApplyBattery(DashboardViewModel model, Reading reading) {

            if(!reading.BatteryPresent) {
                model.Battery.Figure = "-";
                model.Battery.Unit = "";
                model.Battery.Detail = Name("GuiWpfBatNone");
                model.Battery.Health = Health.Neutral;
                model.Battery.Portion = -1;
                return;
            }

            model.Battery.Figure = reading.BatteryPercent >= 0
                ? reading.BatteryPercent.ToString() : "-";
            model.Battery.Unit = "%";
            model.Battery.Portion = reading.BatteryPercent >= 0
                ? reading.BatteryPercent / 100.0 : -1;

            model.Battery.Health = reading.BatteryPercent >= 0 && !reading.BatteryOnAc
                ? HealthScale.FromCharge(reading.BatteryPercent) : Health.Neutral;

            string state = reading.BatteryCharging ? Name("GuiWpfBatCharging")
                : reading.BatteryOnAc ? Name("GuiWpfBatAc") : Name("GuiWpfBatDc");

            // Written tight rather than spelled out. The card is the narrowest
            // in the row and this is its supporting line, so "1h 12m" fits
            // where "1 h 12 m" is trimmed to "1 h 1…" — which reads as a
            // different, wrong number rather than as a shortened one.
            string remaining = reading.BatteryMinutesLeft > 0
                ? " · " + (reading.BatteryMinutesLeft / 60) + "h "
                    + (reading.BatteryMinutesLeft % 60) + "m"
                : "";

            model.Battery.Detail = state + remaining;

        }

        // The GPU card's supporting line: load, clock and power, whichever of
        // them the card reports, joined with the same middle dot the other
        // cards use. Empty when there is no discrete card to ask.
        private static string GpuDetail(Reading reading) {

            if(!reading.GpuNvidiaPresent)
                return "";

            // Same order as the CPU card: load, power, clock
            return Line(
                reading.GpuNvidiaLoad >= 0 ? reading.GpuNvidiaLoad + " %" : "",
                reading.GpuNvidiaPowerW > 0 ? reading.GpuNvidiaPowerW + " W" : "",
                reading.GpuNvidiaCoreMhz > 0 ? reading.GpuNvidiaCoreMhz + " MHz" : "");

        }

        // A fan level as a percentage of its ceiling, for the card figure
        private static string FanPercent(int level, int ceiling) {
            int pct = FanPercentValue(level, ceiling);
            return pct > 0 ? pct.ToString() : "0";
        }

        // The same as a number the chart can plot; zero reads as a gap there,
        // which is right — a fan sitting still has nothing to draw
        private static int FanPercentValue(int level, int ceiling) {
            if(ceiling <= 0 || level <= 0)
                return 0;
            int pct = (int) (level * 100.0 / ceiling + 0.5);
            return pct > 100 ? 100 : pct;
        }

        // Joins the parts of a supporting line with the middle dot the cards
        // use, dropping any that are empty so a missing sensor leaves no gap
        private static string Line(params string[] parts) {
            System.Text.StringBuilder line = new System.Text.StringBuilder();
            foreach(string part in parts)
                if(!string.IsNullOrEmpty(part)) {
                    if(line.Length > 0) line.Append(" · ");
                    line.Append(part);
                }
            return line.ToString();
        }

        private static string Levels(Reading reading) {

            if(reading.FanLevelMaximum <= 0
                || (reading.FanLevelCpu <= 0 && reading.FanLevelGpu <= 0))
                return "";

            string levels = string.Format(Config.Locale.Get("GuiWpfLevelFmt"),
                reading.FanLevelCpu, reading.FanLevelGpu, reading.FanLevelMaximum);

            // The measured speeds beside the levels, where the firmware counts
            // them. The percentage above is what the fans were told to do;
            // this is what they are doing.
            if(reading.FanRpmCpu > 0)
                levels += " · " + Rpm(reading.FanRpmCpu)
                    + (reading.FanCount > 1 && reading.FanRpmGpu > 0
                        ? " / " + reading.FanRpmGpu.ToString(Config.FormatFanSpeed,
                            System.Globalization.CultureInfo.CurrentCulture) : "");

            return levels;

        }

        private static string Describe(double value, string unit, int decimals) {
            return value > 0
                ? value.ToString("F" + decimals,
                    System.Globalization.CultureInfo.InvariantCulture) + unit
                : "";
        }

        // The dashboard's four blocks.
        //
        // Written into rows that already exist rather than rebuilt: the block
        // is constructed once with its captions and only the values move, so
        // the panel never re-templates itself under the pointer.
        //
        // Everything here was already being collected. Most of it was reaching
        // only the Sensors page, and three of these rows — the fan ceiling,
        // the uptime, the battery flow — were reaching nothing at all.
        private void ApplyBlocks(DashboardViewModel model, Reading reading, int gpuTemperature) {

            model.CpuName = reading.CpuName ?? "";
            model.GpuName = reading.GpuNvidiaName ?? "";
            model.CoreClocks = reading.CpuCoreClocks ?? new int[0];

            Set(model.CpuBlock, 0, Percent(reading.CpuLoadPercent));
            Set(model.CpuBlock, 1, Describe(reading.CpuWatts, " W", 1));
            Set(model.CpuBlock, 2, PowerLimits(reading));
            Set(model.CpuBlock, 3, reading.CpuGigahertz > 0
                ? reading.CpuGigahertz.ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture) + " GHz" : "");

            Set(model.GpuBlock, 0, Percent(reading.GpuNvidiaLoad));
            Set(model.GpuBlock, 1, GpuWatts(reading));
            Set(model.GpuBlock, 2, reading.GpuNvidiaCoreMhz > 0
                ? reading.GpuNvidiaCoreMhz + " MHz" : "");
            Set(model.GpuBlock, 3, reading.GpuNvidiaMemMhz > 0
                ? reading.GpuNvidiaMemMhz + " MHz" : "");
            Set(model.GpuBlock, 4, Vram(reading));
            Set(model.GpuBlock, 5, reading.GpuPowerSupported
                ? GpuPowerName(reading.GpuPower) : Name("GuiWpfNotAvailable"));

            Set(model.CoolingBlock, 0, FanLine(reading.FanLevelCpu,
                reading.FanLevelMaximum, reading.FanRpmCpu));
            Set(model.CoolingBlock, 1, FanLine(reading.FanLevelGpu,
                reading.FanLevelMaximum, reading.FanRpmGpu));
            Set(model.CoolingBlock, 2, reading.MaxTemperature > 0
                ? reading.MaxTemperature + " °C" : "");
            Set(model.CoolingBlock, 3, FanModeName(reading.FanMode));

            // Why the slider stops where it does. DeviceProfile has worked
            // this out at every start since it was written and the only place
            // it reached was a log line, which left the ceiling looking like
            // an arbitrary number somebody chose.
            Set(model.CoolingBlock, 4, Ceiling(reading));
            Set(model.CoolingBlock, 5, Countdown(reading));
            Set(model.CoolingBlock, 6, reading.IsThermalProtectionActive
                ? Name("GuiWpfGuardActive") : Name("GuiWpfGuardIdle"));

            Set(model.PowerBlock, 0, reading.BatteryPercent >= 0
                ? reading.BatteryPercent + " %" : "");
            Set(model.PowerBlock, 1, Describe(reading.BatteryWatts, " W", 1));
            Set(model.PowerBlock, 2, reading.BatteryHealthPercent >= 0
                ? reading.BatteryHealthPercent + " %" : "");
            Set(model.PowerBlock, 3, PowerLine(reading));
            Set(model.PowerBlock, 4, Memory(reading));
            Set(model.PowerBlock, 5, Storage(reading));
            Set(model.PowerBlock, 6, Network(reading));

        }

        // Writes one row of a block, leaving a reading the machine did not
        // give as a dash rather than as an empty gap that reads like a bug
        private static void Set(DetailGroupViewModel group, int row, string value) {

            if(group == null || row < 0 || row >= group.Rows.Count)
                return;

            group.Rows[row].Value = string.IsNullOrEmpty(value) ? "-" : value;

        }

        // A fan as the level it was told to run at and the speed it is
        // actually turning, which are not the same claim
        private static string FanLine(int level, int ceiling, int rpm) {

            int percent = FanPercentValue(level, ceiling);
            if(percent <= 0 && rpm <= 0)
                return "";

            string text = percent + " %";

            if(rpm > 0)
                text += " · " + Rpm(rpm);

            return text;

        }

        // The fan level ceiling and where it came from.
        //
        // DeviceProfile records the source as an English token for the
        // capability report; the window says it in the interface's language,
        // and falls back to the token where a new source appears that has no
        // string yet — a strange word beats a blank.
        private static string Ceiling(Reading reading) {

            int ceiling = reading.FanLevelMaximum > 0
                ? reading.FanLevelMaximum : Config.FanLevelMax;

            if(ceiling <= 0)
                return "";

            string source = Hardware.DeviceProfile.FanLevelCeilingSource ?? "";

            switch(source) {
                case "fan table": source = Name("GuiWpfCeilingTable"); break;
                case "observed at maximum": source = Name("GuiWpfCeilingMaximum"); break;
                case "observed running": source = Name("GuiWpfCeilingRunning"); break;
                case "configured": source = Name("GuiWpfCeilingSet"); break;
                case "configured (auto-detect off)": source = Name("GuiWpfCeilingFixed"); break;
            }

            return source.Length > 0 ? ceiling + " · " + source : ceiling.ToString();

        }

        // The failsafe countdown, in the units it is actually felt in
        private static string Countdown(Reading reading) {

            if(reading.FanCountdown < 0)
                return "";

            if(reading.FanCountdown == 0)
                return Name("GuiWpfCountdownOff");

            return reading.FanCountdown + " s";

        }

        // The plan and the mode on one line. They are two different things —
        // the plan is the scheme, the mode is the slider in the battery
        // flyout — and neither is worth a row of its own beside the other.
        private static string PowerLine(Reading reading) {

            string plan = reading.PowerPlan ?? "";
            string mode = PowerModeName(reading.PowerMode);

            if(plan.Length == 0)
                return mode;

            return mode.Length > 0 && mode != plan ? plan + " · " + mode : plan;

        }

        private static string GpuPowerName(GpuPower power) {

            switch(power) {
                case GpuPower.Boost: return Name("GuiWpfGpuBoost");
                case GpuPower.Custom: return Name("GuiWpfGpuCustom");
                default: return Name("GuiWpfGpuBase");
            }

        }

        // Windows' own power mode. The enum names are code; these are words.
        private static string PowerModeName(string mode) {

            switch(mode) {
                case "HighPerformance": return Name("GuiWpfPowerModeHigh");
                case "PowerSaver": return Name("GuiWpfPowerModeSaver");
                case "Balanced": return Name("GuiWpfPowerModeBalanced");
                default: return "";
            }

        }

        // Disk and network throughput. Both meters have existed since they
        // were written with no caller at all, so the panel could say how hot
        // the drive was and never how busy.
        private static string Storage(Reading reading) {

            if(reading.DiskReadMBs < 0 && reading.DiskWriteMBs < 0)
                return "";

            return Describe(Math.Max(reading.DiskReadMBs, 0), "", 1) + " / "
                + Describe(Math.Max(reading.DiskWriteMBs, 0), " MB/s", 1);

        }

        private static string Network(Reading reading) {

            if(reading.NetDownMbps < 0 && reading.NetUpMbps < 0)
                return "";

            return Describe(Math.Max(reading.NetDownMbps, 0), "", 1) + " / "
                + Describe(Math.Max(reading.NetUpMbps, 0), " Mb/s", 1);

        }

        // What the card is drawing against what it is allowed to. The limit is
        // the number that explains a card sitting still at 97 % load.
        private static string GpuWatts(Reading reading) {

            if(reading.GpuNvidiaPowerW <= 0)
                return "";

            return reading.GpuNvidiaPowerLimitW > 0
                ? reading.GpuNvidiaPowerW + " / " + reading.GpuNvidiaPowerLimitW + " W"
                : reading.GpuNvidiaPowerW + " W";

        }

        // Core and memory clock. The memory clock has been read from NVAPI all
        // along and dropped before it reached the interface.
        private static string GpuClocks(Reading reading) {

            if(reading.GpuNvidiaCoreMhz <= 0)
                return "";

            return reading.GpuNvidiaMemMhz > 0
                ? reading.GpuNvidiaCoreMhz + " · " + reading.GpuNvidiaMemMhz + " MHz"
                : reading.GpuNvidiaCoreMhz + " MHz";

        }

    }

}
