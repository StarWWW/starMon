// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Windows;
using System.Windows.Controls;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    public partial class KeyboardView : UserControl {

        private readonly KeyboardMap Map = new KeyboardMap();
        private readonly ColorPicker Picker = new ColorPicker();

        // The zone the picker is currently editing, so its colour changes are
        // sent to the right one
        private ZoneViewModel Editing;

        public KeyboardView() {

            InitializeComponent();

            this.MapHost.Content = this.Map;
            this.PickerHost.Content = this.Picker;

            // The picker writes straight into whichever zone opened it. That
            // zone's own change notification is what carries the colour on to
            // the keyboard, debounced, so a drag does not become a write per
            // pixel — the same path the hex box already used.
            this.Picker.ColorChanged += colour => {
                if(this.Editing != null)
                    this.Editing.Colour = colour;
            };

            // Clicking a band on the diagram selects that zone and opens the
            // picker on it, so the diagram is a way in rather than just a
            // picture
            this.Map.ZonePicked += OnZonePicked;

            this.DataContextChanged += OnDataContextChanged;

        }

        private KeyboardViewModel Model;

        private void OnDataContextChanged(object sender,
            DependencyPropertyChangedEventArgs e) {

            if(this.Model != null)
                this.Model.PropertyChanged -= OnModelChanged;

            this.Model = e.NewValue as KeyboardViewModel;
            this.Map.Model = this.Model;

            if(this.Model != null) {
                this.Map.Brand = this.Model.Brand;
                this.Map.HasNumPad = this.Model.HasNumPad;
                this.Map.IsIsoBody = this.Model.IsIsoBody;
                this.Model.PropertyChanged += OnModelChanged;
            }

        }

        private void OnModelChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {
            if(this.Model == null)
                return;
            if(e.PropertyName == "Brand")
                this.Map.Brand = this.Model.Brand;
            else if(e.PropertyName == "HasNumPad")
                this.Map.HasNumPad = this.Model.HasNumPad;
            else if(e.PropertyName == "IsIsoBody")
                this.Map.IsIsoBody = this.Model.IsIsoBody;
        }

        // The swatch beside a zone's hex value: open the picker on that zone,
        // anchored under the swatch
        private void OnSwatchClick(object sender, RoutedEventArgs e) {

            Button button = sender as Button;
            if(button == null)
                return;

            Open(button.DataContext as ZoneViewModel, button);

        }

        // A saved preset: its colours go into the swatches, and each swatch's
        // own change notification carries them on to the keyboard the same way
        // the picker and the hex box do.
        //
        // The configuration stores a preset in the firmware's zone order —
        // right, middle, left, WASD — while the panel lists its zones the way
        // the board looks. Reversed here so a preset lands on the same keys it
        // was saved from.
        private void OnPresetClick(object sender, RoutedEventArgs e) {

            Button button = sender as Button;
            KeyboardViewModel model = this.DataContext as KeyboardViewModel;

            if(button == null || model == null)
                return;

            string name = button.DataContext as string;

            if(string.IsNullOrEmpty(name)
                || !Library.Config.ColorPreset.ContainsKey(name))
                return;

            Hardware.Bios.BiosData.ColorTable table = Library.Config.ColorPreset[name];

            if(table.Zone == null || table.Zone.Length == 0)
                return;

            this.PickerPopup.IsOpen = false;

            for(int i = 0; i < model.Zones.Count; i++) {

                int slot = model.IsSingleZone ? 0
                    : model.Zones.Count == 4 ? HardwareZone(i) : i;

                if(slot >= table.Zone.Length)
                    slot = table.Zone.Length - 1;

                uint packed = table.Zone[slot].ValueReverse & 0xFFFFFF;

                model.Zones[i].Colour = System.Windows.Media.Color.FromRgb(
                    (byte) ((packed >> 16) & 0xFF),
                    (byte) ((packed >> 8) & 0xFF),
                    (byte) (packed & 0xFF));

            }

        }

        // The panel lists its zones left to right; the firmware numbers them
        // the other way round. Its own inverse, so it reads both ways.
        private static int HardwareZone(int visual) {
            switch(visual) {
                case 0: return (int) Hardware.Bios.BiosData.KbdZone.Left;
                case 1: return (int) Hardware.Bios.BiosData.KbdZone.Middle;
                case 2: return (int) Hardware.Bios.BiosData.KbdZone.Right;
                default: return visual;
            }
        }

        private void OnZonePicked(int index) {

            KeyboardViewModel model = this.DataContext as KeyboardViewModel;
            if(model == null || index < 0 || index >= model.Zones.Count)
                return;

            // Anchored to the diagram: it is what the user just clicked, and the
            // swatch for the zone may be scrolled out of view
            Open(model.Zones[index], this.Map);

        }

        private void Open(ZoneViewModel zone, UIElement anchor) {

            if(zone == null)
                return;

            this.Editing = zone;
            this.Picker.Color = zone.Colour;

            this.PickerPopup.PlacementTarget = anchor;
            this.PickerPopup.IsOpen = true;

        }

    }

}
