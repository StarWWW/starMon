// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

namespace StarMon.Ui.ViewModels {

    // The hardware controls this machine exposes, and their current state.
    //
    // Like the dashboard, nothing here talks to hardware. The controller reads
    // the firmware once to fill these in, then answers the changes the user
    // makes; a control the machine does not support is left unsupported and the
    // view shows it disabled rather than hiding it, so the panel is the same
    // shape on every machine and the absence is itself an answer.
    public sealed class SettingsViewModel : Observable {

#region Graphics mode (MUX)
        private bool IsGpuModeSupportedValue;
        private bool IsDiscreteValue;
        private string GpuModeNoteValue = "";

        public bool IsGpuModeSupported {
            get { return this.IsGpuModeSupportedValue; }
            set { Set(ref this.IsGpuModeSupportedValue, value); }
        }

        // The mode itself. Discrete routes the panel to the NVIDIA GPU;
        // Optimus lets the firmware switch. The two segmented buttons each bind
        // to one of the paired booleans below, for the same reason the fan
        // selector does: a converter on a two-way ToggleButton flickers through
        // an unselected state as the user moves between options.
        public bool IsDiscrete {
            get { return this.IsDiscreteValue; }
            set {
                if(Set(ref this.IsDiscreteValue, value)) {
                    Raise("IsGpuOptimus");
                    Raise("IsGpuDiscrete");
                }
            }
        }

        public bool IsGpuOptimus {
            get { return !this.IsDiscreteValue; }
            set { if(value) this.IsDiscrete = false; }
        }

        public bool IsGpuDiscrete {
            get { return this.IsDiscreteValue; }
            set { if(value) this.IsDiscrete = true; }
        }

        // Shown after the mode is changed, because the change does not take
        // effect until the machine restarts and a control that looks like it
        // did nothing is a control that gets clicked again
        public string GpuModeNote {
            get { return this.GpuModeNoteValue; }
            set { Set(ref this.GpuModeNoteValue, value); }
        }
#endregion

#region CPU turbo boost
        private bool IsBoostSupportedValue;
        private int BoostModeValue = 1;

        public bool IsBoostSupported {
            get { return this.IsBoostSupportedValue; }
            set { Set(ref this.IsBoostSupportedValue, value); }
        }

        // 0 off, 1 on, 2 aggressive — the values Windows itself uses for the
        // processor performance boost mode
        public int BoostMode {
            get { return this.BoostModeValue; }
            set {
                if(Set(ref this.BoostModeValue, value)) {
                    Raise("IsBoostOff");
                    Raise("IsBoostOn");
                    Raise("IsBoostAggressive");
                }
            }
        }

        public bool IsBoostOff {
            get { return this.BoostModeValue == 0; }
            set { if(value) this.BoostMode = 0; }
        }

        public bool IsBoostOn {
            get { return this.BoostModeValue == 1; }
            set { if(value) this.BoostMode = 1; }
        }

        public bool IsBoostAggressive {
            get { return this.BoostModeValue == 2; }
            set { if(value) this.BoostMode = 2; }
        }
#endregion

#region Display brightness
        private bool IsBrightnessSupportedValue;
        private double BrightnessValue = 50;

        public bool IsBrightnessSupported {
            get { return this.IsBrightnessSupportedValue; }
            set { Set(ref this.IsBrightnessSupportedValue, value); }
        }

        public double Brightness {
            get { return this.BrightnessValue; }
            set { Set(ref this.BrightnessValue, value); }
        }
#endregion

#region Application preferences
        // These are the application's own behaviour rather than the machine's,
        // and every one of them has always been in the configuration file and
        // in the notification-area menu. Neither is somewhere a user looks:
        // the file is not meant to be edited by hand, and a right-click menu
        // on a tray icon is not where anyone goes to find a preference. They
        // read and write Library.Config directly, because there is nothing to
        // mirror — the configuration is the state.

        public bool StartWithWindows {
            get { return Library.Config.AutoStartup; }
            set {
                if(Library.Config.AutoStartup == value) return;
                Library.Config.AutoStartup = value;
                Raise("StartWithWindows");
                Raise("Dirty");
            }
        }

        public bool ApplyOnStart {
            get { return Library.Config.AutoConfig; }
            set {
                if(Library.Config.AutoConfig == value) return;
                Library.Config.AutoConfig = value;
                Raise("ApplyOnStart");
                Raise("Dirty");
            }
        }

        public bool CloseExits {
            get { return Library.Config.GuiCloseWindowExit; }
            set {
                if(Library.Config.GuiCloseWindowExit == value) return;
                Library.Config.GuiCloseWindowExit = value;
                Raise("CloseExits");
                Raise("Dirty");
            }
        }

        public bool StayOnTop {
            get { return Library.Config.GuiStayOnTop; }
            set {
                if(Library.Config.GuiStayOnTop == value) return;
                Library.Config.GuiStayOnTop = value;
                Raise("StayOnTop");
                Raise("Dirty");
            }
        }

        public bool ThermalProtection {
            get { return Library.Config.ThermalProtectionEnabled; }
            set {
                if(Library.Config.ThermalProtectionEnabled == value) return;
                Library.Config.ThermalProtectionEnabled = value;
                Raise("ThermalProtection");
                Raise("Dirty");
            }
        }

        // The temperature at which the fans are forced to maximum. The low
        // threshold follows it rather than being set separately: the gap
        // between them is hysteresis, and a user who can set the two
        // independently can set them the wrong way round.
        public double ThermalThreshold {
            get { return Library.Config.ThermalProtectionHighC; }
            set {
                int high = (int) value;
                if(Library.Config.ThermalProtectionHighC == high) return;
                Library.Config.ThermalProtectionHighC = high;
                Library.Config.ThermalProtectionLowC = high - ThermalHysteresisC;
                Raise("ThermalThreshold");
                Raise("Dirty");
            }
        }

        private const int ThermalHysteresisC = 7;

        public bool ThrottleNotify {
            get { return Library.Config.ThrottleNotifyEnabled; }
            set {
                if(Library.Config.ThrottleNotifyEnabled == value) return;
                Library.Config.ThrottleNotifyEnabled = value;
                Raise("ThrottleNotify");
                Raise("Dirty");
            }
        }

        public bool RefreshRateFollowsPower {
            get { return Library.Config.RefreshRateFollowPower; }
            set {
                if(Library.Config.RefreshRateFollowPower == value) return;
                Library.Config.RefreshRateFollowPower = value;
                Raise("RefreshRateFollowsPower");
                Raise("Dirty");
            }
        }

        public bool PollGpuOnBattery {
            get { return Library.Config.GpuPollOnBattery; }
            set {
                if(Library.Config.GpuPollOnBattery == value) return;
                Library.Config.GpuPollOnBattery = value;
                Raise("PollGpuOnBattery");
                Raise("Dirty");
            }
        }

        public bool LogVerbose {
            get { return Library.Config.LogVerbose; }
            set {
                if(Library.Config.LogVerbose == value) return;
                Library.Config.LogVerbose = value;
                Raise("LogVerbose");
                Raise("Dirty");
            }
        }

        public bool LogToFile {
            get { return Library.Config.LogToFile; }
            set {
                if(Library.Config.LogToFile == value) return;
                Library.Config.LogToFile = value;
                Raise("LogToFile");
                Raise("Dirty");
            }
        }
#endregion

#region The rest of the configuration file
        // Everything below was configurable and unreachable.
        //
        // These settings have always existed, always been documented in
        // StarMon.xml, and always been editable only by closing the
        // application and opening the file in a text editor. Several of them
        // govern controls that ARE in the window — the fan sliders are scaled
        // against the level ceiling, the refresh-rate toggle switches between
        // two presets — so the interface offered the switch and hid the thing
        // it switched between.
        //
        // Same shape as the preferences above: the configuration is the state,
        // so there is no backing field to keep in step. Change() is just the
        // two Raise calls every one of them ended with.

        private void Change(string name) {
            Raise(name);
            Raise("Dirty");
        }

        // -- Fans ------------------------------------------------------------

        // The highest level the firmware will accept, which every fan control
        // in the window is scaled against. DeviceProfile works this out at
        // startup and may raise it on evidence; setting it by hand is for the
        // board that lies about its own ceiling.
        public double FanLevelCeiling {
            get { return Library.Config.FanLevelMax; }
            set {
                int level = (int) value;
                if(Library.Config.FanLevelMax == level) return;
                Library.Config.FanLevelMax = level;
                Change("FanLevelCeiling");
            }
        }

        public double FanLevelFloor {
            get { return Library.Config.FanLevelMin; }
            set {
                int level = (int) value;
                if(Library.Config.FanLevelMin == level) return;
                Library.Config.FanLevelMin = level;
                Change("FanLevelFloor");
            }
        }

        // Whether the ceiling is allowed to move on its own. Off pins it to
        // whatever is set above, which is what someone tuning a stubborn board
        // wants.
        public bool FanLevelAutoDetect {
            get { return Library.Config.FanLevelAutoDetect; }
            set {
                if(Library.Config.FanLevelAutoDetect == value) return;
                Library.Config.FanLevelAutoDetect = value;
                Change("FanLevelAutoDetect");
            }
        }

        // Whether to keep pushing the Embedded Controller's failsafe timer
        // back. Off means a manual fan speed reverts on its own after about
        // four minutes — which is the firmware's intent, and a surprise to
        // anyone who has not been told.
        public bool KeepFansSet {
            get { return Library.Config.FanCountdownExtendAlways; }
            set {
                if(Library.Config.FanCountdownExtendAlways == value) return;
                Library.Config.FanCountdownExtendAlways = value;
                Change("KeepFansSet");
            }
        }

        // How far the temperature has to fall below a curve step before the
        // level steps back down. Zero follows the curve exactly in both
        // directions and makes the fans surge whenever a reading sits on a
        // boundary.
        public double CurveHysteresis {
            get { return Library.Config.FanProgramHysteresisC; }
            set {
                int degrees = (int) value;
                if(Library.Config.FanProgramHysteresisC == degrees) return;
                Library.Config.FanProgramHysteresisC = degrees;
                Change("CurveHysteresis");
            }
        }

        public bool SuspendProgramOnSleep {
            get { return Library.Config.FanProgramSuspend; }
            set {
                if(Library.Config.FanProgramSuspend == value) return;
                Library.Config.FanProgramSuspend = value;
                Change("SuspendProgramOnSleep");
            }
        }

        // -- Display ---------------------------------------------------------

        // The two rates the machine switches between when the refresh rate
        // follows the power source. That switch is in this same panel and has
        // been since it was written; the rates it switches between were only
        // in the file, so the toggle depended on values the user could not see.
        public double RefreshRateHigh {
            get { return Library.Config.PresetRefreshRateHigh; }
            set {
                int hz = (int) value;
                if(Library.Config.PresetRefreshRateHigh == hz) return;
                Library.Config.PresetRefreshRateHigh = hz;
                Change("RefreshRateHigh");
            }
        }

        public double RefreshRateLow {
            get { return Library.Config.PresetRefreshRateLow; }
            set {
                int hz = (int) value;
                if(Library.Config.PresetRefreshRateLow == hz) return;
                Library.Config.PresetRefreshRateLow = hz;
                Change("RefreshRateLow");
            }
        }

        public bool RefreshRateAutoDetect {
            get { return Library.Config.RefreshRateAutoDetect; }
            set {
                if(Library.Config.RefreshRateAutoDetect == value) return;
                Library.Config.RefreshRateAutoDetect = value;
                Change("RefreshRateAutoDetect");
            }
        }

        // -- The Omen key ----------------------------------------------------

        public bool KeyTogglesProgram {
            get { return Library.Config.KeyToggleFanProgram; }
            set {
                if(Library.Config.KeyToggleFanProgram == value) return;
                Library.Config.KeyToggleFanProgram = value;
                Change("KeyTogglesProgram");
            }
        }

        public bool KeyCyclesAll {
            get { return Library.Config.KeyToggleFanProgramCycleAll; }
            set {
                if(Library.Config.KeyToggleFanProgramCycleAll == value) return;
                Library.Config.KeyToggleFanProgramCycleAll = value;
                Change("KeyCyclesAll");
            }
        }

        public bool KeyShowsWindowFirst {
            get { return Library.Config.KeyToggleFanProgramShowGuiFirst; }
            set {
                if(Library.Config.KeyToggleFanProgramShowGuiFirst == value) return;
                Library.Config.KeyToggleFanProgramShowGuiFirst = value;
                Change("KeyShowsWindowFirst");
            }
        }

        public bool KeyIsSilent {
            get { return Library.Config.KeyToggleFanProgramSilent; }
            set {
                if(Library.Config.KeyToggleFanProgramSilent == value) return;
                Library.Config.KeyToggleFanProgramSilent = value;
                Change("KeyIsSilent");
            }
        }

        // Running something else instead. Takes precedence over the fan
        // program toggle above, which is why the two are shown together.
        public bool KeyRunsCommand {
            get { return Library.Config.KeyCustomActionEnabled; }
            set {
                if(Library.Config.KeyCustomActionEnabled == value) return;
                Library.Config.KeyCustomActionEnabled = value;
                Change("KeyRunsCommand");
            }
        }

        public string KeyCommand {
            get { return Library.Config.KeyCustomActionExecCmd ?? ""; }
            set {
                string command = value ?? "";
                if(Library.Config.KeyCustomActionExecCmd == command) return;
                Library.Config.KeyCustomActionExecCmd = command;
                Change("KeyCommand");
            }
        }

        public string KeyArguments {
            get { return Library.Config.KeyCustomActionExecArgs ?? ""; }
            set {
                string arguments = value ?? "";
                if(Library.Config.KeyCustomActionExecArgs == arguments) return;
                Library.Config.KeyCustomActionExecArgs = arguments;
                Change("KeyArguments");
            }
        }

        public bool KeyCommandMinimised {
            get { return Library.Config.KeyCustomActionMinimized; }
            set {
                if(Library.Config.KeyCustomActionMinimized == value) return;
                Library.Config.KeyCustomActionMinimized = value;
                Change("KeyCommandMinimised");
            }
        }

        // -- The display-off hotkey ------------------------------------------

        // A global hotkey is registered at startup and there has never been a
        // way to bind it: the two values live in the configuration file and
        // nothing in the interface reads or writes them, so the feature
        // shipped with whatever combination happened to be in the file.
        public string HotkeyText {
            get {

                int key = Library.Config.DisplayOffHotkeyKey;
                if(key == 0)
                    return Library.Config.Locale.Get("GuiWpfHotkeyNone");

                string text = "";
                int mods = Library.Config.DisplayOffHotkeyMods;

                if((mods & ModControl) != 0) text += "Ctrl + ";
                if((mods & ModAlt) != 0) text += "Alt + ";
                if((mods & ModShift) != 0) text += "Shift + ";
                if((mods & ModWindows) != 0) text += "Win + ";

                return text + System.Windows.Input.KeyInterop
                    .KeyFromVirtualKey(key).ToString();

            }
        }

        public bool HasHotkey {
            get { return Library.Config.DisplayOffHotkeyKey != 0; }
        }

        // The modifier bits RegisterHotKey uses
        public const int ModAlt = 0x0001, ModControl = 0x0002,
                         ModShift = 0x0004, ModWindows = 0x0008;

        // Set from the view, which is where a keystroke can be caught
        public void SetHotkey(int mods, int key) {

            if(Library.Config.DisplayOffHotkeyMods == mods
                && Library.Config.DisplayOffHotkeyKey == key)
                return;

            Library.Config.DisplayOffHotkeyMods = mods;
            Library.Config.DisplayOffHotkeyKey = key;

            Raise("HotkeyText");
            Raise("HasHotkey");
            Raise("HotkeyChanged");
            Raise("Dirty");

        }

        // -- Logging and timing ----------------------------------------------

        public double LogFileMaxMb {
            get { return Library.Config.LogFileMaxBytes / (1024.0 * 1024.0); }
            set {
                int bytes = (int) (value * 1024 * 1024);
                if(Library.Config.LogFileMaxBytes == bytes) return;
                Library.Config.LogFileMaxBytes = bytes;
                Change("LogFileMaxMb");
            }
        }

        // How often the window asks the hardware anything, in seconds. The
        // machinery to change this while running has been in GuiTray since it
        // was written — its own comment says "the menu lets the user change the
        // monitoring cadence", and the menu never did.
        public double MonitorSeconds {
            get { return Library.Config.UpdateMonitorInterval; }
            set {
                int ticks = (int) value;
                if(ticks < 1 || Library.Config.UpdateMonitorInterval == ticks) return;
                Library.Config.UpdateMonitorInterval = ticks;
                Change("MonitorSeconds");
            }
        }

        // The cheap cadence used while the window is hidden, so the history
        // keeps accruing without paying for a full reading every second
        public double RecordSeconds {
            get { return Library.Config.UpdateRecordInterval; }
            set {
                int ticks = (int) value;
                if(ticks < 1 || Library.Config.UpdateRecordInterval == ticks) return;
                Library.Config.UpdateRecordInterval = ticks;
                Change("RecordSeconds");
            }
        }

        public double ProgramSeconds {
            get { return Library.Config.UpdateProgramInterval; }
            set {
                int ticks = (int) value;
                if(ticks < 1 || Library.Config.UpdateProgramInterval == ticks) return;
                Library.Config.UpdateProgramInterval = ticks;
                Change("ProgramSeconds");
            }
        }

        // Whether a refused firmware call is written to the log. Off by
        // default because a machine that does not implement a call refuses it
        // on every single reading.
        public bool ReportBiosErrors {
            get { return Library.Config.BiosErrorReporting; }
            set {
                if(Library.Config.BiosErrorReporting == value) return;
                Library.Config.BiosErrorReporting = value;
                Change("ReportBiosErrors");
            }
        }
#endregion

    }

}
