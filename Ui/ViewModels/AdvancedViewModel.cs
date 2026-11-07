// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon.Ui.ViewModels {

    // What the Advanced page asks the controller to do. Every one of these
    // either writes to the firmware or reads from it, so none is carried out
    // here: the controller confirms, queues and reports back.
    public enum AdvancedAction {
        CpuPl1,
        CpuPl4,
        CpuPlGpu,
        XmpOn,
        XmpOff,
        IdleOn,
        IdleOff,
        FanTable,
        Anim,
        Reload
    }

    // The settings the command line could reach and the window could not.
    //
    // HP's firmware takes a dozen commands the interface never offered: the
    // processor's power limits, the memory profile, the idle mode, the fan
    // table and the keyboard's animation table. They are here, each behind a
    // confirmation, together with what the firmware says about itself and
    // the processor's own power registers, read live, so the effect of a
    // power limit can be seen rather than assumed.
    public sealed class AdvancedViewModel : Observable {

        private string Pl1TextValue = "";
        private string Pl4TextValue = "";
        private string PlGpuTextValue = "";
        private string CpuPowerNoteValue = "";
        private string Pl4DefaultValue = "";
        private string PlGpuDefaultValue = "";
        private bool CanXmpValue = true;
        private string MemoryNoteValue = "";
        private string FanTableTextValue = "";
        private string FanTableNoteValue = "";
        private string AnimTextValue = "";
        private string AnimNoteValue = "";
        private bool IsBusyValue;

        public AdvancedViewModel() {

            this.Registers = new ObservableCollection<DetailRowViewModel>();
            this.Firmware = new ObservableCollection<DetailRowViewModel>();
            this.FanTableRows = new ObservableCollection<DetailRowViewModel>();

            this.ApplyPl1Command = Command(AdvancedAction.CpuPl1);
            this.ApplyPl4Command = Command(AdvancedAction.CpuPl4);
            this.ApplyPlGpuCommand = Command(AdvancedAction.CpuPlGpu);
            this.XmpOnCommand = new RelayCommand(() => Request(AdvancedAction.XmpOn), () => this.CanXmpValue);
            this.XmpOffCommand = new RelayCommand(() => Request(AdvancedAction.XmpOff), () => this.CanXmpValue);
            this.IdleOnCommand = Command(AdvancedAction.IdleOn);
            this.IdleOffCommand = Command(AdvancedAction.IdleOff);
            this.ApplyFanTableCommand = Command(AdvancedAction.FanTable);
            this.ApplyAnimCommand = Command(AdvancedAction.Anim);
            this.ReloadCommand = Command(AdvancedAction.Reload);

        }

        private RelayCommand Command(AdvancedAction action) {
            return new RelayCommand(() => Request(action));
        }

        // Raised when the user asks for something on this page
        public event Action<AdvancedAction> ActionRequested;

        private void Request(AdvancedAction action) {
            Action<AdvancedAction> handler = this.ActionRequested;
            if(handler != null)
                handler(action);
        }

        public RelayCommand ApplyPl1Command { get; private set; }
        public RelayCommand ApplyPl4Command { get; private set; }
        public RelayCommand ApplyPlGpuCommand { get; private set; }
        public RelayCommand XmpOnCommand { get; private set; }
        public RelayCommand XmpOffCommand { get; private set; }
        public RelayCommand IdleOnCommand { get; private set; }
        public RelayCommand IdleOffCommand { get; private set; }
        public RelayCommand ApplyFanTableCommand { get; private set; }
        public RelayCommand ApplyAnimCommand { get; private set; }
        public RelayCommand ReloadCommand { get; private set; }

        // The processor's power registers, refreshed once a second while the
        // page is on show
        public ObservableCollection<DetailRowViewModel> Registers { get; private set; }

        // What the firmware says about the machine
        public ObservableCollection<DetailRowViewModel> Firmware { get; private set; }

        // The firmware's fan table, one row per step
        public ObservableCollection<DetailRowViewModel> FanTableRows { get; private set; }

        public string Pl1Text {
            get { return this.Pl1TextValue; }
            set { Set(ref this.Pl1TextValue, value ?? ""); }
        }

        public string Pl4Text {
            get { return this.Pl4TextValue; }
            set { Set(ref this.Pl4TextValue, value ?? ""); }
        }

        public string PlGpuText {
            get { return this.PlGpuTextValue; }
            set { Set(ref this.PlGpuTextValue, value ?? ""); }
        }

        // What happened to the last power limit sent. The firmware has no
        // command to read these back, so this is all there is to show.
        public string CpuPowerNote {
            get { return this.CpuPowerNoteValue; }
            set { Set(ref this.CpuPowerNoteValue, value ?? ""); }
        }

        public string Pl4Default {
            get { return this.Pl4DefaultValue; }
            set { Set(ref this.Pl4DefaultValue, value ?? ""); }
        }

        public string PlGpuDefault {
            get { return this.PlGpuDefaultValue; }
            set { Set(ref this.PlGpuDefaultValue, value ?? ""); }
        }

        // Whether the firmware admits to memory overclocking. XMP is offered
        // only where it does.
        public bool CanXmp {
            get { return this.CanXmpValue; }
            set { Set(ref this.CanXmpValue, value); }
        }

        public string MemoryNote {
            get { return this.MemoryNoteValue; }
            set { Set(ref this.MemoryNoteValue, value ?? ""); }
        }

        // The fan table in the command line's own syntax, so what is read can
        // be edited and sent back
        public string FanTableText {
            get { return this.FanTableTextValue; }
            set { Set(ref this.FanTableTextValue, value ?? ""); }
        }

        public string FanTableNote {
            get { return this.FanTableNoteValue; }
            set { Set(ref this.FanTableNoteValue, value ?? ""); }
        }

        public string AnimText {
            get { return this.AnimTextValue; }
            set { Set(ref this.AnimTextValue, value ?? ""); }
        }

        public string AnimNote {
            get { return this.AnimNoteValue; }
            set { Set(ref this.AnimNoteValue, value ?? ""); }
        }

        // Reading the firmware takes a moment, so the page says it is working
        public bool IsBusy {
            get { return this.IsBusyValue; }
            set { Set(ref this.IsBusyValue, value); }
        }

        // Replaces the rows of a list, keeping the row objects where the names
        // match so a once-a-second refresh does not rebuild the list
        public static void Fill(ObservableCollection<DetailRowViewModel> rows,
            System.Collections.Generic.IList<DetailRowViewModel> fresh) {

            bool same = rows.Count == fresh.Count;
            for(int i = 0; same && i < rows.Count; i++)
                same = rows[i].Name == fresh[i].Name;

            if(same) {
                for(int i = 0; i < rows.Count; i++)
                    rows[i].Value = fresh[i].Value;
                return;
            }

            rows.Clear();
            foreach(DetailRowViewModel row in fresh)
                rows.Add(row);

        }

#region Parsing
        // A power limit in whole watts. The firmware takes a byte; nought and
        // 255 are left out, 255 being the firmware's own "no change".
        public static bool TryParseWatts(string text, out byte watts) {

            watts = 0;

            if(!int.TryParse((text ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value))
                return false;

            if(value < 1 || value > 254)
                return false;

            watts = (byte) value;
            return true;

        }

        // A fan table in the command line's syntax: steps separated by colons,
        // each step "fan 1 level, fan 2 level, temperature". At most fourteen
        // steps, which is all the firmware's table holds.
        public static bool TryParseFanTable(string text, out BiosData.FanTable table) {

            table = new BiosData.FanTable();

            string[] steps = (text ?? "").Replace(" ", "").Replace("\r", "")
                .Replace("\n", ":").Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

            if(steps.Length == 0 || steps.Length > 14)
                return false;

            for(int i = 0; i < steps.Length; i++) {

                string[] parts = steps[i].Split(',');
                if(parts.Length != 3)
                    return false;

                byte[] values = new byte[3];
                for(int j = 0; j < 3; j++)
                    if(!Conv.GetByte(parts[j], out values[j]))
                        return false;

                table.Level[i] = new BiosData.FanLevel(values);

            }

            table.LevelCount = (byte) steps.Length;
            return true;

        }

        // The same table written back out, one step per line
        public static string FormatFanTable(BiosData.FanTable table) {

            StringBuilder text = new StringBuilder();
            int count = Math.Min((int) table.LevelCount, table.Level == null ? 0 : table.Level.Length);

            for(int i = 0; i < count; i++) {
                if(i > 0)
                    text.Append(":\n");
                text.Append(table.Level[i].Fan1Level).Append(',')
                    .Append(table.Level[i].Fan2Level).Append(',')
                    .Append(table.Level[i].Temperature);
            }

            return text.ToString();

        }

        // The animation table as bytes, the way -Bios Anim= takes it
        public static bool TryParseAnim(string text, out BiosData.AnimTable table) {

            table = new BiosData.AnimTable();

            string hex = (text ?? "").Replace(" ", "").Replace("\r", "").Replace("\n", "");

            // Hexadecimal digits in pairs, and nothing else: the converter
            // underneath maps any character to some nibble without complaint
            if(hex.Length == 0 || hex.Length % 2 != 0 || hex.Length > 256
                || !System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]+$"))
                return false;

            try {
                byte[] bytes = Conv.GetByteArray(hex);
                table = new BiosData.AnimTable(bytes);
                return true;
            } catch {
                return false;
            }

        }

        public static string FormatAnim(BiosData.AnimTable table) {

            if(table.Raw == null)
                return "";

            // Trailing zeroes are the unused end of a fixed 128-byte table
            int length = table.Raw.Length;
            while(length > 0 && table.Raw[length - 1] == 0)
                length--;

            StringBuilder text = new StringBuilder();
            for(int i = 0; i < length; i++)
                text.Append(table.Raw[i].ToString("X2"));

            return text.ToString();

        }
#endregion

    }

}
