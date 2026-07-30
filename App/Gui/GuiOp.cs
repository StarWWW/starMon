// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.Threading;
using StarMon.External;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.AppGui {

    // Implements a backend for GUI mode operations
    public class GuiOp {

        // Sensors class reference
        internal Platform Platform;

        // Fan program class reference
        internal FanProgram Program;

        // Parent class reference
        private GuiTray Context;

        // Flag to indicate if running on full power
        public bool FullPower { get; private set; }

        // Constructs the operation-running class
        public GuiOp(GuiTray context) {

            // Initialize the parent class reference
            this.Context = context;

            // Initialize the BIOS and the Embedded Controller
            Hw.BiosInit();
            Hw.EcInit();

            // Initialize the hardware platform
            this.Platform = new Platform();

            // Work out what this particular board can do and adapt the
            // configuration to it, before anything reads a fan ceiling or
            // offers a performance mode. Every Omen and Victus answers these
            // differently, and the compiled defaults are only one machine's.
            try {
                StarMon.Hardware.DeviceProfile.Probe(this.Platform);
            } catch(Exception e) {
                Logger.Error("Device", "Hardware profiling failed", e.Message);
            }

            // Give the feature-support registry its platform reference,
            // so per-feature probes can run (lazily) when first needed
            StarMon.Hardware.FeatureSupport.Initialize(this.Platform);

            // Initialize the fan program
            this.Program = new FanProgram(this.Platform, FanProgramCallback);

            // Set the full power flag
            this.FullPower = this.Platform.System.IsFullPower();

        }

        // Shows the about dialog
        public static void About(string title = "", string text = "") {
            StarMon.Ui.Shell.Dialogs.Error(text);
        }

        // Automatically applies the configuration on startup
        // Note: runs on a background thread, where any unhandled exception
        // would take down the whole process, so every step is guarded
        public void AutoConfig() {

            // Set whether the application should start automatically with Windows
            try {
                Hw.TaskSet(Config.TaskId.Gui, Config.AutoStartup);
            } catch(Exception e) {
                Logger.Error("AutoConfig", "Startup task setup failed", e.Message);
            }

            // Apply the default GPU power settings; an unrecognized
            // configuration value falls back to the minimum level
            try {
                if(!Enum.TryParse(Config.GpuPowerDefault, out BiosData.GpuPowerLevel gpuPowerLevel)) {
                    Logger.Warning("AutoConfig", "Unrecognized GpuPowerDefault, using Minimum", Config.GpuPowerDefault);
                    gpuPowerLevel = BiosData.GpuPowerLevel.Minimum;
                }
                this.Platform.System.SetGpuPower(new BiosData.GpuPowerData(gpuPowerLevel));
            } catch(Exception e) {
                Logger.Error("AutoConfig", "Applying default GPU power failed", e.Message);
            }

            // Apply the default fan program,
            // or the alternative program if no AC
            try {
                if(this.FullPower)
                    this.Program.Run(Config.FanProgramDefault);
                else
                    this.Program.Run(Config.FanProgramDefaultAlt, true);
            } catch(Exception e) {
                Logger.Error("AutoConfig", "Starting the default fan program failed", e.Message);
            }

            // The window picks the result up on its next reading rather than
            // being pushed at from here. This runs on a background thread
            // during startup, when there may well be no window at all yet.

        }

        // Starts the automatic configuration in another thread
        // so as not to increase the application loading time
        public void AutoConfigRun() {

            Thread autoConfig = new Thread(this.AutoConfig);
            autoConfig.IsBackground = true;
            autoConfig.Start();

        }

        // Keeps updating the status as the fan program runs in the background
        public void FanProgramCallback(FanProgram.Severity severity, string message) {

            // Status updates may arrive from a background thread (during the
            // startup auto-configuration), and they touch user-interface
            // objects, so marshal the call back to the interface thread first.
            //
            // This deliberately does not key off the main window: the startup
            // auto-configuration is exactly the case where that window does
            // not exist yet, and the balloon tip and tray tooltip below would
            // then be driven straight from the background thread.
            if(Context.NeedsUiThread()) {
                Context.OnUiThread(() => FanProgramCallback(severity, message));
                return;
            }

            // For important status updates only,
            // show a balloon tray notification
            if(severity == FanProgram.Severity.Important)
                Context.ShowBalloonTip(message);

            // Handle notice-severity messages
            else if(severity == FanProgram.Severity.Notice) {

                // Add a prefix if an alternate fan program
                string name = Context.Op.Program.IsAlternate ?
                    Config.Locale.Get(Config.L_PROG + "Alt") + " "
                    + Context.Op.Program.GetName()
                    : Context.Op.Program.GetName();

                // The window's own status line, which it keeps whether or not
                // it is on screen so that opening it does not show a blank one
                Context.Window.DashboardModel.Status = message + ": " + name;

                // Also put it in the tray icon tooltip
                Context.SetNotifyText(
                    Config.Locale.Get(Config.L_PROG) + ": " + name
                    + " @ " + DateTime.Now.ToString(Config.TimestampFormat)
                    + Environment.NewLine + message);

            }

            // Note: Verbose messages are silently ignored in the GUI mode
            // Run StarMon -Prog <Name> from the command line to see them

        }

        // Launches when the Omen key has been pressed
        public void KeyHandler(Gui.MessageParam lastParam) {

            // A custom action, if one is configured.
            //
            // Tested first because it takes precedence: both the configuration
            // template and the documentation say the fan-program toggle
            // applies "as long as KeyCustomAction is set to false", and with
            // the two tested the other way round a machine with both switched
            // on ran the fan program and never the command.
            if(Config.KeyCustomActionEnabled) {

                // Launch the action. A command that cannot be started throws
                // out of the window hook, and this runs from one.
                try {
                    Process customAction = new Process();
                    customAction.StartInfo.FileName = Config.KeyCustomActionExecCmd;
                    customAction.StartInfo.Arguments = Config.KeyCustomActionExecArgs;
                    customAction.StartInfo.UseShellExecute = false; // Required for environment change
                    customAction.StartInfo.WindowStyle = Config.KeyCustomActionMinimized ?
                        ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal;
                    customAction.Start();
                } catch(Exception e) {
                    Logger.Error("Key", "The Omen key command could not be started",
                        Config.KeyCustomActionExecCmd + " — " + e.Message);
                }

            // If Omen key is set
            // to toggle fan program
            } else if(Config.KeyToggleFanProgram) {

                // Show the form on first press
                // if configured to do so and not already shown
                if(Config.KeyToggleFanProgramShowGuiFirst && !Context.IsWindowVisible)
                    Context.ShowFormMain();

                else {

                    // Configured to cycle
                    // through all fan programs
                    if(Config.KeyToggleFanProgramCycleAll) {

                        // Nothing to cycle through. The Cooling page will let
                        // the last program be deleted, and indexing an empty
                        // list threw out of the window hook — after which the
                        // key did nothing at all and logged an error on every
                        // press.
                        if(Config.FanProgram.Count == 0)
                            return;

                        // Default to the first fan program
                        string next = Config.FanProgram.Keys[0];

                        // If a program is running,
                        // cycle to the next one, if exists
                        if(this.Program.IsEnabled)
                            try {
                                next = Config.FanProgram.Keys[
                                    Config.FanProgram.IndexOfKey(this.Program.GetName()) + 1];
                            } catch { }

                        // Run the next fan program
                        this.Program.Run(next);

                    // Configured to toggle
                    // default fan program on and off
                    } else {

                        // Terminate a program, if there is one running
                        if(this.Program.IsEnabled)
                            this.Program.Terminate();

                        // Run the default program, if no program running
                        else
                            this.Program.Run(Config.FanProgramDefault);

                        }

                    // A balloon tip, unless the window is already on screen —
                    // where the change shows itself — or the user asked for
                    // programs to be toggled silently
                    if(!Context.IsWindowVisible && !Config.KeyToggleFanProgramSilent)
                        this.FanProgramCallback(
                            FanProgram.Severity.Important,
                            this.Program.IsEnabled ?
                                Config.Locale.Get(Config.L_PROG) + ": " + this.Program.GetName()
                                : Config.Locale.Get(Config.L_PROG + "End"));

                }

            } else {

                // Just toggle the main form
                Context.ToggleFormMain();

            }

        }

        // Responds to power-mode status change events
        public void PowerChange() {

            // Only if a fan program is active, if configured to do so,
            // and if the power state actually changed from the last-recorded
            if(Config.AutoConfig && this.Program.IsEnabled
                && this.FullPower != this.Platform.System.IsFullPower()) {

                // Toggle the power state
                this.FullPower = !this.FullPower;

                // Apply the default fan program,
                // or the alternative program if no AC
                if(this.FullPower)
                    this.Program.Run(Config.FanProgramDefault);
                else
                    this.Program.Run(Config.FanProgramDefaultAlt, true);

            }

            // Follow the power source with the display refresh rate, if asked
            // to. Done independently of the fan program above: the two are
            // unrelated settings, and this one is useful on its own.
            if(Config.RefreshRateFollowPower)
                UpdateRefreshRateForPower();

            // Separately also update the main form, if it's visible
        }

        // Drops the display to the low refresh rate on battery and restores
        // the high one on AC. Only ever moves between the two configured
        // presets: a rate the user picked by hand that is neither of them is
        // left alone, rather than being overridden on the next power event.
        internal void UpdateRefreshRateForPower() {

            try {

                bool onAc = this.Platform.System.IsFullPower();
                int wanted = onAc ?
                    Config.PresetRefreshRateHigh : Config.PresetRefreshRateLow;

                int current = Os.GetRefreshRate();

                if(current == wanted)
                    return;

                // Anything outside the two presets is a deliberate choice
                if(current != Config.PresetRefreshRateHigh
                    && current != Config.PresetRefreshRateLow)
                    return;

                Os.SetRefreshRate(wanted);

                Logger.Hardware("Display", "Refresh rate follows power source",
                    current + " Hz -> " + wanted + " Hz ("
                        + (onAc ? "AC" : "battery") + ")");

            } catch(Exception e) {
                Logger.Error("GuiOp", "Refresh rate switch failed", e.Message);
            }

        }

        // Responds to the system entering and resuming from low-power state events
        public uint SuspendResumeCallback(IntPtr context, uint type, IntPtr setting) {

            // System is resuming from suspend
            if(type == PowrProf.PBT_APMRESUMEAUTOMATIC)

                // Resume the fan program
                this.Program.Resume();

            // System is about to be suspended
            // and a fan program is running
            else if(type == PowrProf.PBT_APMSUSPEND)

                // Suspend the fan program
                this.Program.Suspend();

            return 0;

        }

    }

}
