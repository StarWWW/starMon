// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    public partial class SettingsView : UserControl {

        // Whether the next keystroke is the hotkey being chosen rather than
        // ordinary typing
        private bool Capturing;

        public SettingsView() {

            InitializeComponent();

            this.CaptureHotkey.Click += OnCaptureClicked;
            this.CaptureHotkey.LostKeyboardFocus += (s, e) => StopCapturing();
            this.CaptureHotkey.PreviewKeyDown += OnCaptureKeyDown;

            this.ClearHotkey.Click += delegate {
                SettingsViewModel model = DataContext as SettingsViewModel;
                if(model != null)
                    model.SetHotkey(0, 0);
            };

        }

        private void OnCaptureClicked(object sender, RoutedEventArgs e) {

            this.Capturing = true;
            this.CaptureHotkey.Content = Library.Config.Locale.Get("GuiWpfHotkeyPress");
            this.CaptureHotkey.Focus();

        }

        // Catching the keystroke is the view's business: the view model has no
        // business knowing about WPF key codes, and there is nowhere else in
        // the application a key press can be observed.
        private void OnCaptureKeyDown(object sender, KeyEventArgs e) {

            if(!this.Capturing)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // A modifier on its own is half a combination. Waiting for the
            // rest of it is what the user is doing while holding it down.
            if(key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LWin || key == Key.RWin) {
                e.Handled = true;
                return;
            }

            e.Handled = true;

            // Escape abandons the capture rather than binding Escape, which is
            // what pressing it means everywhere else
            if(key == Key.Escape) {
                StopCapturing();
                return;
            }

            SettingsViewModel model = DataContext as SettingsViewModel;
            if(model == null) {
                StopCapturing();
                return;
            }

            int mods = 0;
            ModifierKeys held = Keyboard.Modifiers;

            if((held & ModifierKeys.Control) != 0) mods |= SettingsViewModel.ModControl;
            if((held & ModifierKeys.Alt) != 0) mods |= SettingsViewModel.ModAlt;
            if((held & ModifierKeys.Shift) != 0) mods |= SettingsViewModel.ModShift;
            if((held & ModifierKeys.Windows) != 0) mods |= SettingsViewModel.ModWindows;

            // A bare letter would be captured system-wide and make the key
            // unusable everywhere else on the machine
            if(mods == 0) {
                StopCapturing();
                return;
            }

            model.SetHotkey(mods, KeyInterop.VirtualKeyFromKey(key));
            StopCapturing();

        }

        // Puts the button back to showing the binding rather than the prompt
        private void StopCapturing() {

            if(!this.Capturing)
                return;

            this.Capturing = false;

            this.CaptureHotkey.ClearValue(ContentControl.ContentProperty);
            this.CaptureHotkey.SetBinding(ContentControl.ContentProperty,
                new System.Windows.Data.Binding("HotkeyText"));

        }

    }

}
