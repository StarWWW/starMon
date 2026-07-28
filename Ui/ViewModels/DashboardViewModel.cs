// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.ObjectModel;
using StarMon.AppService;
using StarMon.Library;

namespace StarMon.Ui.ViewModels {

    // The fan control modes, as the segmented selector offers them. These are
    // the same four the WinForms build had as a column of radio buttons.
    public enum FanMode {
        Automatic,
        Constant,
        Maximum,
        Program
    }

    // The dashboard: what the machine is doing, and the fan controls.
    //
    // Nothing here talks to hardware. The poller writes into it from a
    // background thread by way of the dispatcher, and the bindings do the
    // rest — which is what retires the five "am I already updating this"
    // flags and the hand-written marshalling the old form needed.
    public sealed class DashboardViewModel : Observable {

        public DashboardViewModel() {

            // The processor is the wide lead card now: its temperature is the
            // one figure the window is opened for, and the per-core strip below
            // it is what the old "hottest sensor" card only ever hinted at.
            this.Cpu = new ReadingViewModel(Text("GuiWpfCpu"));
            this.Gpu = new ReadingViewModel(Text("GuiWpfGpu"));
            this.FanCpu = new ReadingViewModel(Text("GuiWpfFans"));
            this.FanGpu = new ReadingViewModel(Text("GuiWpfFans"));
            this.Battery = new ReadingViewModel(Text("GuiWpfBattery"));

            this.Cards = new ObservableCollection<ReadingViewModel> {
                this.Cpu, this.Gpu, this.FanCpu, this.FanGpu, this.Battery
            };

            this.Details = new ObservableCollection<DetailGroupViewModel>();

            // The dashboard's blocks. Built once with their rows in place and
            // then written to by index, rather than rebuilt each reading:
            // replacing a collection once a second makes the whole panel
            // re-template itself, and the row a user is hovering disappears
            // from under the pointer while they read it.
            //
            // The captions are set here; the values arrive with the first
            // reading, and until then a row honestly shows a dash.
            this.CpuBlock = new DetailGroupViewModel(Text("GuiWpfCpu"))
                .Add(Text("GuiWpfRowLoad"), "-", Text("GuiTipCpuLoad"))
                .Add(Text("GuiWpfRowPower"), "-", Text("GuiTipCpuPower"))
                .Add(Text("GuiWpfRowLimits"), "-", Text("GuiTipCpuLimit"))
                .Add(Text("GuiWpfRowClock"), "-", Text("GuiTipCpuClock"));

            this.GpuBlock = new DetailGroupViewModel(Text("GuiWpfGpu"))
                .Add(Text("GuiWpfRowLoad"), "-", Text("GuiTipGpuLoad"))
                .Add(Text("GuiWpfRowPower"), "-", Text("GuiTipGpuPower"))
                .Add(Text("GuiWpfRowClock"), "-", Text("GuiTipGpuClock"))
                .Add(Text("GuiWpfRowMemClock"), "-", Text("GuiWpfTipGpuMemClock"))
                .Add(Text("GuiWpfRowVram"), "-", Text("GuiTipVram"))
                .Add(Text("GuiWpfRowTgp"), "-", Text("GuiWpfTipGpuPower"));

            this.CoolingBlock = new DetailGroupViewModel(Text("GuiWpfFans"))
                .Add(Text("GuiWpfRowFanCpu"), "-", Text("GuiWpfTipFanLine"))
                .Add(Text("GuiWpfRowFanGpu"), "-", Text("GuiWpfTipFanLine"))
                .Add(Text("GuiWpfRowHottest"), "-", Text("GuiWpfTipHottest"))
                .Add(Text("GuiWpfRowMode"), "-", Text("GuiWpfTipPerfMode"))
                .Add(Text("GuiWpfRowCeiling"), "-", Text("GuiWpfTipCeiling"))
                // The failsafe timer. When it runs out the Embedded Controller
                // takes the fans back, which is the entire explanation for a
                // manual speed reverting on its own, and there has never been
                // anywhere to watch it happen.
                .Add(Text("GuiWpfRowCountdown"), "-", Text("GuiTipCountdown"))
                .Add(Text("GuiWpfRowGuard"), "-", Text("GuiWpfTipProtection"));

            // Not "battery": the block carries the machine's whole power and
            // throughput picture. The disk and network meters had been written
            // and never called at all, so the panel could say how hot the
            // drive was running and never how hard.
            this.PowerBlock = new DetailGroupViewModel(Text("GuiWpfSystemBlock"))
                .Add(Text("GuiWpfRowCharge"), "-", Text("GuiWpfTipCharge"))
                .Add(Text("GuiWpfRowFlow"), "-", Text("GuiTipBatDraw"))
                .Add(Text("GuiWpfRowHealth"), "-", Text("GuiTipBatHealth"))
                .Add(Text("GuiWpfRowPlan"), "-", Text("GuiWpfTipPlanLine"))
                .Add(Text("GuiWpfRowMemory"), "-", Text("GuiTipMemUsed"))
                .Add(Text("GuiWpfRowDisk"), "-", Text("GuiTipDiskRate"))
                .Add(Text("GuiWpfRowNetwork"), "-", Text("GuiTipNetRate"));

            // The series, in the order they take their palette slots in.
            // Temperatures first, because they are what the chart is mostly
            // watched for; the ranges are the sensible full scale for each
            // rather than the observed one, so a quiet minute does not get
            // magnified into a dramatic-looking trace. The fans are carried as
            // a percentage of their ceiling rather than a raw rpm, so they
            // share the 0-100 scale the load does and read against it.
            this.History = new HistoryViewModel();
            this.History.Begin(HistoryViewModel.Windows[0],
                new SeriesSpec("CPU", 0, 100, "°"),
                new SeriesSpec("GPU", 0, 100, "°"),
                new SeriesSpec(Text("GuiWpfSeriesCpuFan"), 0, 100, "%"),
                new SeriesSpec(Text("GuiWpfSeriesGpuFan"), 0, 100, "%"),
                new SeriesSpec(Text("GuiWpfSeriesLoad"), 0, 100, "%"),
                new SeriesSpec(Text("GuiWpfSeriesPower"), 0, 120, " W"));

        }

        // The captions are read once, when the window's models are built. A
        // language change rebuilds them along with the window; the bindings in
        // markup update in place, but a string handed to a constructor cannot.
        private static string Text(string key) {
            return Config.Locale.Get(key);
        }

        public HistoryViewModel History { get; private set; }

        // The stat cards along the top, in a fixed order. A collection rather
        // than five named slots in the markup, so the row lays itself out and
        // a machine without one of the sensors does not leave a hole.
        public ObservableCollection<ReadingViewModel> Cards { get; private set; }

        public ReadingViewModel Cpu { get; private set; }
        public ReadingViewModel Gpu { get; private set; }
        public ReadingViewModel FanCpu { get; private set; }
        public ReadingViewModel FanGpu { get; private set; }
        public ReadingViewModel Battery { get; private set; }

        // One temperature per logical processor, for the strip drawn along the
        // CPU lead card. Held here rather than on the reading model so the
        // drawn element can watch a single property for a change.
        private int[] CoreTemperaturesValue = new int[0];

        public int[] CoreTemperatures {
            get { return this.CoreTemperaturesValue; }
            set { Set(ref this.CoreTemperaturesValue, value ?? new int[0]); }
        }

        // The details, as groups of name-and-value rows. This replaces a
        // hand-built rich-text string: the old form composed the whole panel
        // as RTF markup, colour table and all, which is why nothing in it
        // could be selected, aligned or bound.
        public ObservableCollection<DetailGroupViewModel> Details { get; private set; }

        // The dashboard's four blocks. Each is a heading, a lead figure the
        // reading models above already carry, and a short fixed table.
        public DetailGroupViewModel CpuBlock { get; private set; }
        public DetailGroupViewModel GpuBlock { get; private set; }
        public DetailGroupViewModel CoolingBlock { get; private set; }
        public DetailGroupViewModel PowerBlock { get; private set; }

        private string CpuNameValue = "";
        private string GpuNameValue = "";
        private int[] CoreClocksValue = new int[0];

        // What the parts actually are, beside the block headings. The
        // application has known the processor and card by name since it
        // started and only ever said so in a detail row three pages away.
        public string CpuName {
            get { return this.CpuNameValue; }
            set { Set(ref this.CpuNameValue, value ?? ""); }
        }

        public string GpuName {
            get { return this.GpuNameValue; }
            set { Set(ref this.GpuNameValue, value ?? ""); }
        }

        // One clock per logical processor. CpuMetrics has been able to report
        // these since it was written and nothing has ever asked: the per-core
        // strip showed temperature only, so a core parked at its base clock
        // and a core boosting looked exactly alike.
        public int[] CoreClocks {
            get { return this.CoreClocksValue; }
            set { Set(ref this.CoreClocksValue, value ?? new int[0]); }
        }

#region Fan control
        private FanMode ModeValue = FanMode.Automatic;
        private double LevelCpuValue;
        private double LevelGpuValue;
        private double LevelMaximumValue = 56;
        private bool IsProgramRunningValue;
        private string ProgramNameValue = "";
        private string StatusValue = "";

        public FanMode Mode {
            get { return this.ModeValue; }
            set {
                if(Set(ref this.ModeValue, value)) {
                    Raise("IsAutomatic");
                    Raise("IsConstant");
                    Raise("IsMaximum");
                    Raise("IsProgram");
                }
            }
        }

        // The segmented selector binds each button to one of these. A single
        // enum with a converter would do it in less markup, but two-way
        // binding through a converter on a ToggleButton means every button
        // reports itself unchecked as the user clicks another one, and the
        // group flickers through a state where nothing is selected.
        public bool IsAutomatic {
            get { return this.Mode == FanMode.Automatic; }
            set { if(value) this.Mode = FanMode.Automatic; }
        }

        public bool IsConstant {
            get { return this.Mode == FanMode.Constant; }
            set { if(value) this.Mode = FanMode.Constant; }
        }

        public bool IsMaximum {
            get { return this.Mode == FanMode.Maximum; }
            set { if(value) this.Mode = FanMode.Maximum; }
        }

        public bool IsProgram {
            get { return this.Mode == FanMode.Program; }
            set { if(value) this.Mode = FanMode.Program; }
        }

        // Fan levels, in the hardware's own units rather than a percentage.
        // The ceiling is a property of the machine and is read from the
        // configuration: a slider built against a made-up maximum either never
        // reaches full speed or asks for a level the firmware rejects.
        public double LevelCpu {
            get { return this.LevelCpuValue; }
            set { Set(ref this.LevelCpuValue, value); }
        }

        public double LevelGpu {
            get { return this.LevelGpuValue; }
            set { Set(ref this.LevelGpuValue, value); }
        }

        public double LevelMaximum {
            get { return this.LevelMaximumValue; }
            set { Set(ref this.LevelMaximumValue, value); }
        }

        public bool IsProgramRunning {
            get { return this.IsProgramRunningValue; }
            set { Set(ref this.IsProgramRunningValue, value); }
        }

        private bool HasProgramValue;

        // Whether there is a fan program to run at all.
        //
        // The Program segment was offered unconditionally and did nothing
        // whenever no program was already running: FanControl.Apply returns
        // immediately on an empty name, and nothing on this page had ever set
        // one. A control that cannot work is disabled rather than left to fail
        // silently, and the Cooling section is where a program is chosen.
        public bool HasProgram {
            get { return this.HasProgramValue; }
            set { Set(ref this.HasProgramValue, value); }
        }

        public string ProgramName {
            get { return this.ProgramNameValue; }
            set {
                if(Set(ref this.ProgramNameValue, value))
                    Raise("ProgramLabel");
            }
        }

        // What the Program segment is called.
        //
        // The name of the program once there is one, rather than the word
        // "Program" for ever. Pressing the button used to tell you nothing
        // about what it had started — and with several saved programs, which
        // one is running is the only thing worth knowing about that button.
        public string ProgramLabel {
            get {
                return this.ProgramNameValue != null && this.ProgramNameValue.Length > 0
                    ? this.ProgramNameValue : Text("GuiWpfFanProgram");
            }
        }

        // The status line along the bottom: what the fan program last said,
        // or what the last action did
        public string Status {
            get { return this.StatusValue; }
            set { Set(ref this.StatusValue, value); }
        }
#endregion

#region Graphics power
        private GpuPower GpuValue = GpuPower.Base;
        private bool IsGpuPowerSupportedValue = true;

        // How much power the graphics chip is allowed to draw.
        //
        // Kept as its own control rather than folded into the fan modes,
        // because it is its own decision: someone may want the extra power
        // without the noise, or the quiet without giving the headroom up. The
        // one place they are tied together is Maximum fans, which asks for
        // both — and the selector moves when it does, so nothing is hidden.
        public GpuPower GraphicsPower {
            get { return this.GpuValue; }
            set {
                if(Set(ref this.GpuValue, value)) {
                    Raise("IsGpuBase");
                    Raise("IsGpuCustom");
                    Raise("IsGpuBoost");
                }
            }
        }

        public bool IsGpuBase {
            get { return this.GraphicsPower == GpuPower.Base; }
            set { if(value) this.GraphicsPower = GpuPower.Base; }
        }

        public bool IsGpuCustom {
            get { return this.GraphicsPower == GpuPower.Custom; }
            set { if(value) this.GraphicsPower = GpuPower.Custom; }
        }

        public bool IsGpuBoost {
            get { return this.GraphicsPower == GpuPower.Boost; }
            set { if(value) this.GraphicsPower = GpuPower.Boost; }
        }

        // Several models report no graphics power control at all. On those the
        // row is disabled rather than removed: a control that vanishes between
        // machines reads as a bug, and one that is there but does nothing is
        // worse than one that says it cannot.
        public bool IsGpuPowerSupported {
            get { return this.IsGpuPowerSupportedValue; }
            set {
                if(Set(ref this.IsGpuPowerSupportedValue, value))
                    Raise("GpuPowerNote");
            }
        }

        public string GpuPowerNote {
            get {
                return this.IsGpuPowerSupportedValue
                    ? "" : Text("GuiWpfNotAvailable");
            }
        }
#endregion

#region Performance mode (the firmware's own profile)
        private string PerformanceModeValue = "Default";

        // The firmware's performance profile: Default, Performance, Cool or
        // Quiet. This is not the fan-speed control above — it is the power and
        // thermal envelope the machine runs in, and on this hardware it is what
        // lifts the graphics power (Performance is what takes the GPU past its
        // base draw). Held as the firmware's own mode name so a reading sets it
        // back without translation, and applied stickily so it does not quietly
        // reset — which is what left the GPU stuck at its base wattage.
        public string PerformanceMode {
            get { return this.PerformanceModeValue; }
            set {
                if(Set(ref this.PerformanceModeValue, value)) {
                    Raise("IsPerfDefault");
                    Raise("IsPerfPerformance");
                    Raise("IsPerfCool");
                    Raise("IsPerfQuiet");
                    Raise("IsPerfExtreme");
                }
            }
        }

        public bool IsPerfDefault {
            get { return this.PerformanceModeValue == "Default"; }
            set { if(value) this.PerformanceMode = "Default"; }
        }

        public bool IsPerfPerformance {
            get { return this.PerformanceModeValue == "Performance"; }
            set { if(value) this.PerformanceMode = "Performance"; }
        }

        public bool IsPerfCool {
            get { return this.PerformanceModeValue == "Cool"; }
            set { if(value) this.PerformanceMode = "Cool"; }
        }

        public bool IsPerfQuiet {
            get { return this.PerformanceModeValue == "Quiet"; }
            set { if(value) this.PerformanceMode = "Quiet"; }
        }

        public bool IsPerfExtreme {
            get { return this.PerformanceModeValue == "Extreme"; }
            set { if(value) this.PerformanceMode = "Extreme"; }
        }

        private bool HasExtremeModeValue;

        // Whether this board's firmware carries the Extreme profile. Omen
        // models generally do and it is their top mode; Victus models
        // generally do not, and on those the firmware either refuses it or
        // gives it a smaller envelope than Performance. Shown only where the
        // machine says it exists, rather than offered to everyone and quietly
        // doing nothing for half of them.
        public bool HasExtremeMode {
            get { return this.HasExtremeModeValue; }
            set { Set(ref this.HasExtremeModeValue, value); }
        }
#endregion

    }

    // A named group of detail rows
    public sealed class DetailGroupViewModel : Observable {

        public DetailGroupViewModel(string caption) {
            this.Caption = caption;
            this.Rows = new ObservableCollection<DetailRowViewModel>();
        }

        public string Caption { get; private set; }
        public ObservableCollection<DetailRowViewModel> Rows { get; private set; }

        public DetailGroupViewModel Add(string name, string value) {
            this.Rows.Add(new DetailRowViewModel(name, value, ""));
            return this;
        }

        // The tip explains what the reading is, for the hover — a very
        // detailed panel is only detailed if it also says what its numbers mean
        public DetailGroupViewModel Add(string name, string value, string tip) {
            this.Rows.Add(new DetailRowViewModel(name, value, tip));
            return this;
        }

    }

    public sealed class DetailRowViewModel : Observable {

        private string ValueText;
        private string TipText;

        public DetailRowViewModel(string name, string value, string tip = "") {
            this.Name = name;
            this.ValueText = value;
            this.Tip = tip;
        }

        public string Name { get; private set; }

        // What this reading is, shown on hover.
        //
        // Null rather than empty when there is nothing to say, and that is the
        // whole point of the property: a ToolTip bound to an empty string is
        // still a tooltip, so the pointer resting on a row without one opened
        // a small blank box. Bound to null it opens nothing.
        //
        // Doing it here rather than in each view is deliberate. Three of the
        // four pages that show these rows bound the tooltip straight through
        // and got the blank box; the fourth carried a trigger of its own to
        // suppress it. One of those is a fix and three are a bug waiting for
        // the next page to be written.
        public string Tip {
            get { return string.IsNullOrEmpty(this.TipText) ? null : this.TipText; }
            set { this.TipText = value ?? ""; }
        }

        public string Value {
            get { return this.ValueText; }
            set { Set(ref this.ValueText, value); }
        }

    }

}
