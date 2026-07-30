// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;

namespace StarMon.Ui.ViewModels {

    // One backlight zone. A four-zone keyboard has four of these left to
    // right; a single-zone one has a single entry covering the whole board.
    public sealed class ZoneViewModel : Observable {

        private Color ColourValue = Colors.White;
        private string CaptionValue;

        public ZoneViewModel(string caption) {
            this.CaptionValue = caption;
        }

        // Settable so a language change can rename the zone in place
        public string Caption {
            get { return this.CaptionValue; }
            set { Set(ref this.CaptionValue, value); }
        }

        public Color Colour {
            get { return this.ColourValue; }
            set {
                if(Set(ref this.ColourValue, value)) {
                    Raise("Brush");
                    Raise("Hex");
                }
            }
        }

        public Brush Brush { get { return new SolidColorBrush(this.ColourValue); } }

        // The value as it is typed and shown, without a leading hash: this is
        // what the hardware and the configuration file both use
        public string Hex {
            get {
                return this.ColourValue.R.ToString("X2")
                    + this.ColourValue.G.ToString("X2")
                    + this.ColourValue.B.ToString("X2");
            }
            set {
                Color parsed;
                if(TryParse(value, out parsed))
                    this.Colour = parsed;
            }
        }

        // Accepts six hex digits, with or without a hash. Anything else is
        // rejected rather than half-applied: a partially-typed colour would
        // otherwise be written to the keyboard on every keystroke.
        public static bool TryParse(string text, out Color colour) {

            colour = Colors.Black;

            if(string.IsNullOrEmpty(text))
                return false;

            string hex = text.Trim().TrimStart('#');
            if(hex.Length != 6)
                return false;

            int value;
            if(!int.TryParse(hex, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out value))
                return false;

            colour = Color.FromRgb(
                (byte) ((value >> 16) & 0xFF),
                (byte) ((value >> 8) & 0xFF),
                (byte) (value & 0xFF));

            return true;

        }

    }

    // The animated backlight modes, as the segmented selector offers them
    public enum BacklightMode {
        Static,
        Temperature,
        Cycle,
        Breathe
    }

    public sealed class KeyboardViewModel : Observable {

        private bool IsSupportedValue = true;
        private bool IsBacklightOnValue = true;
        private BacklightMode ModeValue = BacklightMode.Static;
        private int IdleOffMinutesValue;
        private int EffectSpeedValue = 3;
        private string StatusValue = "";
        private string BrandValue = "";

        public KeyboardViewModel(int zoneCount) {

            this.Zones = new ObservableCollection<ZoneViewModel>();

            // A single-zone keyboard gets one control covering the board
            // rather than four that all do the same thing, which is what the
            // WinForms build showed and which invited the reasonable
            // conclusion that three of them were broken.
            // Zero means the backlight switches but does not take a colour
            // from here — a per-key RGB deck, or a board whose colour table
            // will not read. No swatches at all then, rather than swatches
            // that quietly change nothing.
            if(zoneCount >= 4) {
                this.Zones.Add(new ZoneViewModel(Text("GuiWpfZoneLeft")));
                this.Zones.Add(new ZoneViewModel(Text("GuiWpfZoneCentre")));
                this.Zones.Add(new ZoneViewModel(Text("GuiWpfZoneRight")));
                this.Zones.Add(new ZoneViewModel(Text("GuiWpfZoneWasd")));
            } else if(zoneCount >= 1) {
                this.Zones.Add(new ZoneViewModel(Text("GuiWpfZoneAll")));
            }

            this.Presets = new ObservableCollection<string>();

            // The row of presets appears only when there are some, and the
            // collection is filled after the panel is built — so the view has
            // to be told when that happens
            this.Presets.CollectionChanged += delegate { Raise("HasPresets"); };

        }

        public ObservableCollection<ZoneViewModel> Zones { get; private set; }
        public ObservableCollection<string> Presets { get; private set; }

        // Whether the configuration file carries any saved colour presets.
        //
        // They were loaded into the collection above and displayed by nothing:
        // the only way to reach a preset was the notification-area menu, which
        // is the very limitation filling the collection was meant to lift.
        public bool HasPresets { get { return this.Presets.Count > 0; } }

        public bool IsSingleZone { get { return this.Zones.Count <= 1; } }

        // Whether this deck takes a colour from this application at all
        public bool HasColour { get { return this.Zones.Count > 0; } }

        // Whether this machine has a controllable backlight at all
        public bool IsSupported {
            get { return this.IsSupportedValue; }
            set { Set(ref this.IsSupportedValue, value); }
        }

        public bool IsBacklightOn {
            get { return this.IsBacklightOnValue; }
            set { Set(ref this.IsBacklightOnValue, value); }
        }

        public BacklightMode Mode {
            get { return this.ModeValue; }
            set {
                if(Set(ref this.ModeValue, value)) {
                    Raise("IsStatic");
                    Raise("IsTemperature");
                    Raise("IsCycle");
                    Raise("IsBreathe");
                    Raise("IsEffectActive");
                }
            }
        }

        // Whether an animated effect is running, so the speed control is shown
        // only when it has something to speed up
        public bool IsEffectActive {
            get { return this.Mode == BacklightMode.Cycle
                || this.Mode == BacklightMode.Breathe; }
        }

        // How fast the animated effect runs, 1 (slowest) to 5 (fastest)
        public int EffectSpeed {
            get { return this.EffectSpeedValue; }
            set { Set(ref this.EffectSpeedValue, value); }
        }

        public bool IsStatic {
            get { return this.Mode == BacklightMode.Static; }
            set { if(value) this.Mode = BacklightMode.Static; }
        }

        public bool IsTemperature {
            get { return this.Mode == BacklightMode.Temperature; }
            set { if(value) this.Mode = BacklightMode.Temperature; }
        }

        public bool IsCycle {
            get { return this.Mode == BacklightMode.Cycle; }
            set { if(value) this.Mode = BacklightMode.Cycle; }
        }

        public bool IsBreathe {
            get { return this.Mode == BacklightMode.Breathe; }
            set { if(value) this.Mode = BacklightMode.Breathe; }
        }

        // Zero means the backlight is never switched off for being idle
        public int IdleOffMinutes {
            get { return this.IdleOffMinutesValue; }
            set {
                if(Set(ref this.IdleOffMinutesValue, value))
                    Raise("IdleOffCaption");
            }
        }

        public string IdleOffCaption {
            get {
                return this.IdleOffMinutesValue > 0
                    ? this.IdleOffMinutesValue + " " + Text("GuiWpfKbdMinutes")
                    : Text("GuiWpfKbdNever");
            }
        }

        private static string Text(string key) {
            return Library.Config.Locale.Get(key);
        }

        public string Status {
            get { return this.StatusValue; }
            set { Set(ref this.StatusValue, value); }
        }

        // The machine's short name — "OMEN", "Victus" — shown on the drawn
        // deck so the diagram reads as this keyboard rather than a generic one
        public string Brand {
            get { return this.BrandValue; }
            set { Set(ref this.BrandValue, value); }
        }

        private bool HasNumPadValue = true;

        // Whether this deck carries the numeric pad, from the firmware's
        // keyboard type. The 15- and 17-inch machines have one, the 16-inch
        // ones do not, and the drawn deck follows.
        public bool HasNumPad {
            get { return this.HasNumPadValue; }
            set { Set(ref this.HasNumPadValue, value); }
        }

        private bool? IsIsoBodyValue;

        // Whether the deck is an ISO body, from the firmware's own description
        // of the board. Null where it does not say.
        public bool? IsIsoBody {
            get { return this.IsIsoBodyValue; }
            set { Set(ref this.IsIsoBodyValue, value); }
        }

        // Applies one colour to every zone, which is what a preset and the
        // single-zone case both do
        public void SetAll(Color colour) {
            foreach(ZoneViewModel zone in this.Zones)
                zone.Colour = colour;
        }

    }

}
