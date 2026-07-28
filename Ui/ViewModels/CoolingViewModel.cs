// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Ui.ViewModels {

    // One saved fan program, as the list shows it
    public sealed class FanProgramViewModel : Observable {

        private bool IsRunningValue;

        public FanProgramViewModel(string name, string detail) {
            this.Name = name;
            this.Detail = detail;
        }

        public string Name { get; private set; }

        // The firmware mode it holds and the graphics power it asks for, in
        // one line: a program is more than its curve, and a list that shows
        // only names hides the half of it that changes how the machine runs
        public string Detail { get; private set; }

        public bool IsRunning {
            get { return this.IsRunningValue; }
            set { Set(ref this.IsRunningValue, value); }
        }

    }

    // The cooling section: the curve, the saved programs, and what the
    // machine will actually let the application do about cooling.
    //
    // The programs are the part that was missing. The configuration file has
    // carried a list of them since long before this interface, complete with
    // a fan mode and a graphics power level each — and the only way to start
    // one was the tray menu. The dashboard offered a Program button that
    // called FanControl.Apply with an empty name, which returns immediately,
    // so the button did nothing at all and said nothing about it.
    public sealed class CoolingViewModel : Observable {

        private FanProgramViewModel SelectedValue;
        private string NewNameValue = "";
        private string StatusValue = "";

        public CoolingViewModel(FanCurveViewModel curve) {

            this.Curve = curve;
            this.Programs = new ObservableCollection<FanProgramViewModel>();

            this.State = new DetailGroupViewModel(Text("GuiWpfCoolingState"))
                .Add(Text("GuiWpfRowCeiling"), "-", Text("GuiWpfTipCeiling"))
                .Add(Text("GuiWpfRowCountdown"), "-", Text("GuiTipCountdown"))
                .Add(Text("GuiWpfRowSoftware"), "-", Text("GuiWpfTipSoftware"))
                .Add(Text("GuiWpfRowAlwaysOn"), "-", Text("GuiWpfTipAlwaysOn"))
                .Add(Text("GuiWpfRowFanCount"), "-", Text("GuiWpfTipFanCount"))
                .Add(Text("GuiWpfRowLevelPath"), "-", Text("GuiWpfTipLevelPath"))
                .Add(Text("GuiWpfRowGuard"), "-", Text("GuiWpfTipProtection"));

            this.RunCommand = new RelayCommand(
                () => Raise(this.RunRequested, Named()),
                () => this.SelectedValue != null);

            this.StopCommand = new RelayCommand(
                () => { Action handler = this.StopRequested; if(handler != null) handler(); });

            this.DeleteCommand = new RelayCommand(
                () => Raise(this.DeleteRequested, Named()),
                () => this.SelectedValue != null);

            this.SaveCommand = new RelayCommand(
                () => Raise(this.SaveRequested, this.NewNameValue.Trim()),
                () => this.NewNameValue.Trim().Length > 0);

        }

        private static string Text(string key) {
            return Config.Locale.Get(key);
        }

        public FanCurveViewModel Curve { get; private set; }

        public ObservableCollection<FanProgramViewModel> Programs { get; private set; }

        // What the machine will let the application do about cooling, and why
        // the controls have the limits they do. Every one of these was already
        // known at startup and reached nothing but a log line.
        public DetailGroupViewModel State { get; private set; }

        public RelayCommand RunCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }
        public RelayCommand DeleteCommand { get; private set; }
        public RelayCommand SaveCommand { get; private set; }

        // The view model touches no hardware and no configuration file: it
        // says what was asked for and whatever owns it decides how
        public event Action<string> RunRequested;
        public event Action StopRequested;
        public event Action<string> DeleteRequested;
        public event Action<string> SaveRequested;

        // Which program the list has selected. Selecting one loads its curve
        // into the editor, so the two halves of the page are looking at the
        // same thing rather than at whatever each was last shown.
        public FanProgramViewModel Selected {
            get { return this.SelectedValue; }
            set {
                if(Set(ref this.SelectedValue, value))
                    Raise("HasSelection");
            }
        }

        public bool HasSelection {
            get { return this.SelectedValue != null; }
        }

        // What a newly saved program will be called
        public string NewName {
            get { return this.NewNameValue; }
            set { Set(ref this.NewNameValue, value ?? ""); }
        }

        public string Status {
            get { return this.StatusValue; }
            set { Set(ref this.StatusValue, value ?? ""); }
        }

        // Rebuilds the list from the configuration, keeping the selection
        // where the program it named still exists
        public void Reload(string running) {

            string wanted = this.SelectedValue != null ? this.SelectedValue.Name : null;

            this.Programs.Clear();

            if(Config.FanProgram != null)
                foreach(KeyValuePair<string, FanProgramData> entry in Config.FanProgram)
                    this.Programs.Add(new FanProgramViewModel(
                        entry.Key, Describe(entry.Value)) {
                        IsRunning = entry.Key == running
                    });

            foreach(FanProgramViewModel program in this.Programs)
                if(program.Name == wanted) {
                    this.Selected = program;
                    return;
                }

            this.Selected = this.Programs.Count > 0 ? this.Programs[0] : null;

        }

        // Marks which program is running without rebuilding the list, so the
        // selection and the scroll position survive a reading
        public void SetRunning(string running) {

            foreach(FanProgramViewModel program in this.Programs)
                program.IsRunning = program.Name == running;

        }

        // A program in one line: the firmware mode it holds, the graphics
        // power it asks for, and how many steps its curve has
        private static string Describe(FanProgramData program) {

            if(program == null)
                return "";

            string mode = Config.Locale.Get("ProgMode" + program.FanMode);
            if(mode.StartsWith("ProgMode", StringComparison.Ordinal))
                mode = program.FanMode.ToString();

            int steps = program.Level != null ? program.Level.Count : 0;

            return mode + " · " + program.GpuPower
                + " · " + steps + " " + Config.Locale.Get("GuiWpfSteps");

        }

        private string Named() {
            return this.SelectedValue != null ? this.SelectedValue.Name : "";
        }

        private static void Raise(Action<string> handler, string name) {
            if(handler != null)
                handler(name);
        }

    }

}
