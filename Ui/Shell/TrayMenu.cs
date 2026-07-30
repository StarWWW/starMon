// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon.Ui.Shell {

    // What the tray menu needs from the application.
    //
    // An interface rather than a reference to the tray context, so the menu
    // can be built and looked at without one — and so that when the last of
    // the Windows Forms interface goes, the menu does not go with it.
    public interface ITrayHost {

        Hardware.Platform.Platform Platform { get; }
        Hardware.Platform.FanProgram Program { get; }

        void ToggleWindow();
        void ShowWindow(Views.Section section);
        void Exit();

        // Whether the window is on screen, so the toggle item can say whether
        // it will show it or hide it
        bool IsWindowShown { get; }

        bool IsDynamicIcon { get; set; }
        bool IsDynamicIconBackground { get; set; }
        void RefreshIcon();
        void RefreshFanState();

        void SetKbdColorByTemp(bool enable);
        void SetKbdEffect(int effect);
        void SetKbdBacklight(bool on);
        void SetKbdColor(int colour);
        void SetKbdZoneColors(int[] colours);

        // Re-registers the global "display off" hotkey from the configuration.
        //
        // The binding lives in the configuration file, but the registration is
        // with the operating system and has to be redone when it changes.
        // Without this the settings page could show a new combination — or
        // "None" after a clear — while the one registered at startup carried
        // on intercepting keystrokes until the application was restarted.
        void ApplyDisplayOffHotkey();

    }

    // The tray menu.
    //
    // Rebuilt from the model each time it opens rather than kept and walked.
    // The captions and ticks are functions of the current state, and a menu of
    // thirty items is cheaper to rebuild than to reconcile — which is what the
    // WinForms version's second pass over every item before opening was, and
    // where its captions and settings drifted apart.
    public sealed class TrayMenu {

        private readonly ITrayHost Host;
        private readonly ContextMenu Menu;

        public TrayMenu(ITrayHost host) {

            this.Host = host;

            this.Menu = new ContextMenu {
                Placement = PlacementMode.AbsolutePoint,
                HorizontalOffset = 0,
                VerticalOffset = 0
            };

            this.Menu.Opened += delegate {
                MenuModel.Fill(this.Menu, Build());
            };

        }

        // Shows the menu at the point the shell nominated.
        //
        // The point arrives in physical pixels and WPF places in
        // device-independent ones, so it has to be converted or the menu lands
        // in the wrong place on any display that is not at 100 %. A Popup
        // keeps itself on the monitor from there, which is what the WinForms
        // version had to do by hand by clamping into the working area.
        public void Show(Point physical) {

            Point logical = FromDevice(physical);

            this.Menu.HorizontalOffset = logical.X;
            this.Menu.VerticalOffset = logical.Y;
            this.Menu.IsOpen = true;

        }

        public void Hide() {
            this.Menu.IsOpen = false;
        }

        private static Point FromDevice(Point physical) {

            try {

                System.Windows.Interop.HwndSource source =
                    System.Windows.Interop.HwndSource.FromHwnd(
                        External.User32.FindWindow(null, null));

                if(source != null && source.CompositionTarget != null)
                    return source.CompositionTarget.TransformFromDevice
                        .Transform(physical);

            } catch { }

            // No window to ask: assume the scale the process was told about
            return physical;

        }

        private static string Text(string key) {
            return Config.Locale.Get(Config.L_GUI_MENU + key);
        }

        private IEnumerable<MenuModel> Build() {

            return new List<MenuModel> {

                MenuModel.Item(() => Config.AppBrand + "  " + Config.AppVersion,
                    () => this.Host.ShowWindow(Views.Section.About)),

                MenuModel.Separator(),

                BuildFan(),
                BuildGraphics(),
                BuildKeyboard(),
                BuildSettings(),

                MenuModel.Separator(),

                MenuModel.Item(() => Text(this.Host.IsWindowShown
                    ? "ActToggleFormMainHide" : "ActToggleFormMain"),
                    this.Host.ToggleWindow),

                MenuModel.Item(() => Text("ActToggleFormLog"),
                    () => this.Host.ShowWindow(Views.Section.Log)),

                MenuModel.Separator(),

                MenuModel.Item(() => Text("ActExit"), this.Host.Exit)

            };

        }

        private MenuModel BuildFan() {

            MenuModel branch = MenuModel.Branch(() => Text("SubFan"));

            // The modes the firmware offers, ticked against the one in force.
            //
            // The profile's list rather than every value the enumeration
            // declares. This menu used to offer all of them unconditionally,
            // so a Victus without the Extreme profile was shown an Extreme
            // entry that the firmware refuses — while the window's own
            // selector, reading the same profile, correctly hid it. Two
            // surfaces disagreeing about what the machine can do.
            foreach(BiosData.FanMode mode in Hardware.DeviceProfile.SupportedFanModes()) {

                BiosData.FanMode captured = mode;

                branch.Add(MenuModel.Toggle(
                    () => Config.Locale.Get(Config.L_PROG + "Mode" + captured) is string named
                        && named != Config.L_PROG + "Mode" + captured
                        ? named : captured.ToString(),
                    () => Current() == captured,
                    () => {
                        this.Host.Platform.Fans.SetMode(captured);
                        this.Host.Platform.ClearFanModeSticky();
                        this.Host.RefreshFanState();
                    }));

            }

            branch.Add(MenuModel.Separator());

            branch.Add(MenuModel.Toggle(() => Text("ActFanMax"),
                () => Safe(() => this.Host.Platform.Fans.GetMax()),
                () => {
                    this.Host.Platform.Fans.SetMax(
                        !Safe(() => this.Host.Platform.Fans.GetMax()));
                    this.Host.Platform.ClearFanModeSticky();
                    this.Host.RefreshFanState();
                }));

            branch.Add(MenuModel.Toggle(() => Text("ActFanOff"),
                () => Safe(() => this.Host.Platform.Fans.GetOff()),
                () => {
                    this.Host.Platform.Fans.SetOff(
                        !Safe(() => this.Host.Platform.Fans.GetOff()));
                    this.Host.Platform.ClearFanModeSticky();
                    this.Host.RefreshFanState();
                }));

            branch.Add(MenuModel.Separator());

            // The saved fan programs. Choosing the one already running stops
            // it, which is what makes a single click both start and stop.
            foreach(string name in Config.FanProgram.Keys) {

                string captured = name;

                branch.Add(MenuModel.Item(() => captured, () => {

                    if(this.Host.Program.IsEnabled
                        && this.Host.Program.GetName() == captured)
                        this.Host.Program.Terminate();
                    else
                        this.Host.Program.Run(captured);

                    this.Host.RefreshFanState();

                }));

            }

            branch.Add(MenuModel.Separator());
            branch.Add(MenuModel.Item(() => Text("ActFanCurve"),
                () => this.Host.ShowWindow(Views.Section.Cooling)));

            return branch;

        }

        private BiosData.FanMode Current() {
            try { return this.Host.Platform.Fans.GetMode(); }
            catch { return BiosData.FanMode.Default; }
        }

        private MenuModel BuildGraphics() {

            return MenuModel.Branch(() => Text("SubGpu"),

                MenuModel.Item(
                    () => Config.PresetRefreshRateHigh + " "
                        + Config.Locale.Get(Config.L_UNIT + "Frequency"),
                    () => Os.SetRefreshRate(Config.PresetRefreshRateHigh)),

                MenuModel.Item(
                    () => Config.PresetRefreshRateLow + " "
                        + Config.Locale.Get(Config.L_UNIT + "Frequency"),
                    () => Os.SetRefreshRate(Config.PresetRefreshRateLow)),

                MenuModel.Separator(),

                MenuModel.Item(() => Text("ActGpuDisplayColor"),
                    Os.ReloadColorSettings),

                MenuModel.Item(() => Text("ActGpuDisplayOff"),
                    Os.SetDisplayOff));

        }

        private MenuModel BuildKeyboard() {

            MenuModel branch = MenuModel.Branch(() => Text("SubKbd"),

                MenuModel.Item(() => Text("ActKbdBacklight"),
                    () => this.Host.SetKbdBacklight(
                        !Safe(() => this.Host.Platform.System.GetKbdBacklight()
                            == BiosData.Backlight.On))),

                MenuModel.Toggle(() => Text("ActKbdIdleOff") + ": "
                        + (Config.KbdIdleOffMinutes > 0
                            ? Config.KbdIdleOffMinutes + " "
                                + Config.Locale.Get(Config.L_GUI_MAIN + "DetMinute")
                            : Config.Locale.Get(Config.L_GUI_MENU + "ActKbdIdleOffDisabled")),
                    () => Config.KbdIdleOffMinutes > 0,
                    () => {
                        int minutes = Config.KbdIdleOffMinutes;
                        Config.KbdIdleOffMinutes =
                            minutes < 1 ? 1 : minutes < 3 ? 3
                            : minutes < 5 ? 5 : minutes < 10 ? 10 : 0;
                        Config.Save();
                    }),

                MenuModel.Separator(),

                MenuModel.Toggle(() => Text("ActKbdTempColor"),
                    () => Config.KbdColorByTemp,
                    () => this.Host.SetKbdColorByTemp(!Config.KbdColorByTemp)),

                MenuModel.Toggle(() => Text("ActKbdFxCycle"),
                    () => Config.KbdColorEffect == 1,
                    () => this.Host.SetKbdEffect(Config.KbdColorEffect == 1 ? 0 : 1)),

                MenuModel.Toggle(() => Text("ActKbdFxBreathe"),
                    () => Config.KbdColorEffect == 2,
                    () => this.Host.SetKbdEffect(Config.KbdColorEffect == 2 ? 0 : 2)),

                MenuModel.Separator());

            foreach(string name in Config.ColorPreset.Keys) {

                string captured = name;

                branch.Add(MenuModel.Item(() => captured, () => {
                    try {
                        BiosData.ColorTable table = Config.ColorPreset[captured];
                        this.Host.SetKbdColor((int) table.Zone[0].Value);
                    } catch { }
                }));

            }

            return branch;

        }

        private MenuModel BuildSettings() {

            return MenuModel.Branch(() => Text("SubSet"),

                BuildLanguage(),

                MenuModel.Separator(),

                MenuModel.Toggle(() => Text("ActSetStayTop"),
                    () => Config.GuiStayOnTop,
                    () => { Config.GuiStayOnTop = !Config.GuiStayOnTop; Config.Save(); }),

                MenuModel.Separator(),

                MenuModel.Toggle(() => Text("ActSetIconDyn"),
                    () => this.Host.IsDynamicIcon,
                    () => {
                        this.Host.IsDynamicIcon = !this.Host.IsDynamicIcon;
                        this.Host.RefreshIcon();
                        Config.Save();
                    }),

                MenuModel.Toggle(() => Text("ActSetIconDynBg"),
                    () => this.Host.IsDynamicIconBackground,
                    () => {
                        this.Host.IsDynamicIconBackground =
                            !this.Host.IsDynamicIconBackground;
                        this.Host.RefreshIcon();
                        Config.Save();
                    }).Disable(() => !this.Host.IsDynamicIcon),

                MenuModel.Separator(),

                MenuModel.Toggle(() => Text("ActSetAutoconfig"),
                    () => Config.AutoConfig,
                    () => { Config.AutoConfig = !Config.AutoConfig; Config.Save(); }),

                MenuModel.Toggle(() => Text("ActSetThermal"),
                    () => Config.ThermalProtectionEnabled,
                    () => {
                        Config.ThermalProtectionEnabled =
                            !Config.ThermalProtectionEnabled;
                        Config.Save();
                    }),

                MenuModel.Toggle(() => Text("ActSetThrottleNotify"),
                    () => Config.ThrottleNotifyEnabled,
                    () => {
                        Config.ThrottleNotifyEnabled = !Config.ThrottleNotifyEnabled;
                        Config.Save();
                    }),

                MenuModel.Toggle(() => Text("ActSetRefreshPower"),
                    () => Config.RefreshRateFollowPower,
                    () => {
                        Config.RefreshRateFollowPower = !Config.RefreshRateFollowPower;
                        Config.Save();
                    }),

                MenuModel.Separator(),

                MenuModel.Item(() => Text("ActSetCapabilities"),
                    () => this.Host.ShowWindow(Views.Section.About)));

        }

        // The interface language, ticked against the configured choice. The
        // WinForms build had this and the rewrite lost it: the setting existed,
        // the dictionaries existed, and the only way to reach either was to
        // edit the configuration file by hand.
        private MenuModel BuildLanguage() {

            MenuModel branch = MenuModel.Branch(() => Text("ActSetLanguage"));

            foreach(string name in Config.LanguageNames) {

                string captured = name;

                branch.Add(MenuModel.Toggle(
                    () => Text("ActSetLanguage" + captured),
                    () => string.Equals(
                        string.IsNullOrEmpty(Config.Language) ? "Auto" : Config.Language,
                        captured, StringComparison.OrdinalIgnoreCase),
                    () => {

                        Config.Language = captured;
                        Config.Save();

                        // Resolved first, so "Auto" follows the system and
                        // "English" lands on the Override slot the same way
                        // it does at startup; LocaleInit is what raises the
                        // change every {loc:Str} binding listens for
                        Config.LocaleInit(Config.ResolveLanguage().ToString());

                        Logger.Gui("Config", "Language: " + captured);

                    }));

            }

            return branch;

        }

        // A hardware read inside a caption must never throw: the menu is being
        // built while it is opening, and an exception there takes the menu
        // away rather than showing one wrong tick
        private static bool Safe(Func<bool> read) {
            try { return read(); } catch { return false; }
        }

    }

}
