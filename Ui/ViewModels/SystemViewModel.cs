// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.ObjectModel;

namespace StarMon.Ui.ViewModels {

    // One thing the application can do, and whether this machine can do it.
    //
    // The WinForms build reported these as a paragraph of prose listing what
    // was missing, which is the wrong shape: the question is per-feature and
    // the answer is yes or no, so it should be a table that can be scanned.
    public sealed class CapabilityViewModel : Observable {

        private bool IsSupportedValue;
        private string DetailValue = "";

        public CapabilityViewModel(string caption, bool supported, string detail = "") {
            this.Caption = caption;
            this.IsSupportedValue = supported;
            this.DetailValue = detail ?? "";
        }

        public string Caption { get; private set; }

        public bool IsSupported {
            get { return this.IsSupportedValue; }
            set { if(Set(ref this.IsSupportedValue, value)) Raise("Health"); }
        }

        public string Detail {
            get { return this.DetailValue; }
            set { Set(ref this.DetailValue, value); }
        }

        // Unsupported is not a fault, so it is drawn in muted ink rather than
        // in the critical colour. A machine without a four-zone keyboard is
        // not broken, and a column of red saying so would suggest it was.
        public Health Health {
            get { return this.IsSupportedValue ? Health.Good : Health.Neutral; }
        }

    }

    public sealed class SystemViewModel : Observable {

        private bool IsProbingValue;
        private string ReportValue = "";
        private string SearchValue = "";
        private string PowerModeValue = "";

        // Every BIOS setting the firmware publishes, before filtering. The
        // shown list is a view onto this, so typing in the box does not
        // destroy what was read.
        private readonly System.Collections.Generic.List<DetailRowViewModel> AllSettings =
            new System.Collections.Generic.List<DetailRowViewModel>();

        public SystemViewModel() {

            this.Capabilities = new ObservableCollection<CapabilityViewModel>();
            this.Facts = new ObservableCollection<DetailRowViewModel>();
            this.BiosSettings = new ObservableCollection<DetailRowViewModel>();

            this.Profile = new DetailGroupViewModel(Text("GuiWpfProfileCaption"));

            this.CopyReportCommand = new RelayCommand(
                () => { System.Action handler = this.CopyRequested;
                        if(handler != null) handler(); },
                () => this.ReportValue.Length > 0);

        }

        private static string Text(string key) {
            return Library.Config.Locale.Get(key);
        }

        public ObservableCollection<CapabilityViewModel> Capabilities { get; private set; }

        // Version, build, model — the things someone reads out when reporting
        // a problem
        public ObservableCollection<DetailRowViewModel> Facts { get; private set; }

        // What the application worked out about this board at startup.
        //
        // DeviceProfile establishes eleven things — the family, the board, the
        // fan count, the level ceiling and where it came from, whether the
        // firmware offers software fan control, which path levels take, the
        // Extreme profile, the keyboard zones, the refresh rates — and the
        // interface used exactly one of them. The rest reached a single log
        // line, which is where the answers to "why does my slider stop at 56"
        // and "why do my fans never stop" have been sitting all along.
        public DetailGroupViewModel Profile { get; private set; }

        // The BIOS setup menu, as the firmware publishes it. Read and cached
        // since HpBiosSettings was written; two of its ninety-odd entries were
        // used and the rest were unreachable.
        public ObservableCollection<DetailRowViewModel> BiosSettings { get; private set; }

        public RelayCommand CopyReportCommand { get; private set; }

        // Raised when the user asks for the report on the clipboard. The view
        // model does not touch the clipboard: that is the view's business, the
        // same way the log panel's export is.
        public event System.Action CopyRequested;

        // The full hardware report.
        //
        // Capabilities.Report() has existed since it was written, runs to two
        // hundred and fifty lines covering the machine, the profile, the live
        // readings, every published sensor and every unsupported feature — and
        // had no caller anywhere in the application. The tray menu item that
        // promised it opened a far thinner table instead.
        public string Report {
            get { return this.ReportValue; }
            set { if(Set(ref this.ReportValue, value)) Raise("HasReport"); }
        }

        public bool HasReport {
            get { return this.ReportValue.Length > 0; }
        }

        // Ninety settings is too many to read; it is not too many to search
        public string Search {
            get { return this.SearchValue; }
            set {
                if(Set(ref this.SearchValue, value ?? ""))
                    Refilter();
            }
        }

        public string SettingsSummary {
            get {
                return this.AllSettings.Count == 0 ? ""
                    : this.BiosSettings.Count + " / " + this.AllSettings.Count;
            }
        }

        // Windows' own power mode, held as the enum's name so a reading sets
        // it back without translation
        public string PowerMode {
            get { return this.PowerModeValue; }
            set {
                if(Set(ref this.PowerModeValue, value ?? "")) {
                    Raise("IsPowerSaver");
                    Raise("IsPowerBalanced");
                    Raise("IsPowerHigh");
                }
            }
        }

        public bool IsPowerSaver {
            get { return this.PowerModeValue == "PowerSaver"; }
            set { if(value) this.PowerMode = "PowerSaver"; }
        }

        public bool IsPowerBalanced {
            get { return this.PowerModeValue == "Balanced"; }
            set { if(value) this.PowerMode = "Balanced"; }
        }

        public bool IsPowerHigh {
            get { return this.PowerModeValue == "HighPerformance"; }
            set { if(value) this.PowerMode = "HighPerformance"; }
        }

        // Replaces the BIOS setting list. Called once: the settings are read
        // and cached by the hardware layer and do not change while the machine
        // is running.
        public void SetBiosSettings(
            System.Collections.Generic.IEnumerable<DetailRowViewModel> settings) {

            this.AllSettings.Clear();

            if(settings != null)
                this.AllSettings.AddRange(settings);

            Refilter();

        }

        private void Refilter() {

            this.BiosSettings.Clear();

            string needle = this.SearchValue.Trim();

            foreach(DetailRowViewModel setting in this.AllSettings)
                if(needle.Length == 0 || Matches(setting, needle))
                    this.BiosSettings.Add(setting);

            Raise("SettingsSummary");

        }

        // The value as well as the name: someone looking for what is Enabled
        // is asking a real question about their firmware
        private static bool Matches(DetailRowViewModel setting, string needle) {

            return setting.Name.IndexOf(needle,
                       System.StringComparison.OrdinalIgnoreCase) >= 0
                || setting.Value.IndexOf(needle,
                       System.StringComparison.OrdinalIgnoreCase) >= 0;

        }

        // Probing asks the firmware about each feature in turn and is not
        // instant, so the panel says it is working rather than looking empty
        public bool IsProbing {
            get { return this.IsProbingValue; }
            set { Set(ref this.IsProbingValue, value); }
        }

    }

}
