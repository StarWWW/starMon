// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows.Threading;
using Microsoft.Win32;
using StarMon.AppService;
using StarMon.External;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon.AppGui
{

    // The application, running in the background with an icon in the
    // notification area and no window until one is asked for.
    //
    // This was a Windows Forms ApplicationContext. What it needed from that
    // was a message loop, a timer and a window handle to marshal onto — and
    // WPF supplies all three: Application.Run turns the pump, DispatcherTimer
    // is the timer, and the Dispatcher is the marshalling. The notification
    // icon's own window is the handle.
    public class GuiTray : StarMon.Ui.Shell.ITrayHost
    {

        #region Data
        // Stores the first instance's context,
        // so that it can be accessed from elsewhere
        internal static GuiTray Context { get; private set; }

        // Handles the message another instance of the application sends
        private GuiFilter Filter;

        // The thread the interface lives on
        private readonly Dispatcher Interface = Dispatcher.CurrentDispatcher;

        // The WPF window and the class that gathers what it shows
        internal readonly StarMon.Ui.Windows.WindowController Window;
        private readonly StarMon.AppService.Poller Poll;

        // The WPF tray menu, which has replaced the ToolStrip one
        private readonly StarMon.Ui.Shell.TrayMenu WpfMenu;

        // The notification icon, which has replaced NotifyIcon, and the class
        // that draws the temperature into it
        internal readonly StarMon.Ui.Shell.TrayIcon Tray;
        internal readonly StarMon.Ui.Shell.DynamicIcon TrayPainter;

        // Hidden window receiving the global "display off" hotkey
        private HotkeyWindow Hotkey;


        // Stores the operation-running class
        internal GuiOp Op;

        // The once-a-second heartbeat everything periodic hangs off
        private DispatcherTimer Timer;

        // Cached temperature unit label for tray icon
        private string TemperatureUnitText;

        // The periodic slots on the once-a-second heartbeat. The scheduling
        // arithmetic, the thermal hysteresis and the backlight modes all live
        // in AppService, where they have no window attached and can be tested;
        // what is left here is the wiring to the hardware and the tray.
        private readonly Ticker TickIcon = new Ticker(Config.UpdateIconInterval);
        private readonly Ticker TickMonitor = new Ticker(Config.UpdateMonitorInterval);
        private readonly Ticker TickProgram = new Ticker(Config.UpdateProgramInterval);
        private readonly Ticker TickRecord = new Ticker(Config.UpdateRecordInterval);
        private readonly Ticker TickGuard = new Ticker(Config.UpdateMonitorInterval);

        // Kept as properties so the forms and the menu, which nudge these
        // counters to make the next tick pick something up, do not have to
        // know how the scheduler is built
        internal int UpdateIconTick {
            get { return this.TickIcon.Count; } set { this.TickIcon.Count = value; } }
        internal int UpdateMonitorTick {
            get { return this.TickMonitor.Count; } set { this.TickMonitor.Count = value; } }
        internal int UpdateProgramTick {
            get { return this.TickProgram.Count; } set { this.TickProgram.Count = value; } }

        // Automatic thermal protection and the throttle notification
        private readonly ThermalGuard Guard = new ThermalGuard();
        internal bool ThermalProtectionActive { get { return this.Guard.IsActive; } }

        // Last backlight color applied by the temperature-reactive mode,
        // so the EC is only written to when the color actually changes
        private int KbdTempColorLast = -1;

        // The idle switch-off and the animated effects
        private readonly IdleWatch KbdIdle = new IdleWatch();
        private readonly BacklightEffect KbdFx = new BacklightEffect();
        #endregion

        #region Construction & Disposal
        // Constructs the tray notification application context
        public GuiTray()
        {

            // Retain the context for future use
            if (Context == null)
                Context = this;

            // Create the notification icon
            this.Tray = new StarMon.Ui.Shell.TrayIcon(
                Config.AppBrand + " " + Config.AppVersion);

            this.Tray.Selected += ToggleFormMain;
            this.Tray.ContextMenu += point => ShowTrayMenu(point);
            this.Tray.BalloonClicked += ShowFormMain;

            // The painter draws as it is constructed, so the icon exists
            // before the shell is asked to add it. Adding first and drawing
            // afterwards leaves a gap with no picture in it — and if the
            // dynamic icon is switched off, which is the default, that gap
            // never closes.
            this.TrayPainter = new StarMon.Ui.Shell.DynamicIcon(this.Tray);

            this.Tray.Show();

            // Initialize the operation-running class
            this.Op = new GuiOp(Context);

            // The window and its poller. The poller is given delegates rather
            // than a reference back here, so it can read the hardware and
            // nothing else.
            this.Window = new StarMon.Ui.Windows.WindowController(
                System.Windows.Threading.Dispatcher.CurrentDispatcher);

            this.Poll = new StarMon.AppService.Poller(this.Op.Platform) {
                GetProgramName = () => this.Op.Program.GetName(),
                IsProgramRunning = () => this.Op.Program.IsEnabled,
                IsThermalProtectionActive = () => this.Guard.IsActive,

                // Someone with the window open is looking at the graphics
                // readings, which is the one case where waking the card on
                // battery is what they asked for.
                //
                // The cached flag rather than the window itself: this runs on
                // the poller's thread-pool thread, and asking a WPF window
                // anything from there throws.
                IsWindowVisible = () => this.WindowVisibleCache
            };

            this.Poll.Read += this.Window.Apply;

            this.WpfMenu = new StarMon.Ui.Shell.TrayMenu(this);

            // Zero zones means a backlight that switches but takes no colour
            // from this application — a per-key RGB deck, or a board whose
            // colour table cannot be read. The panel still belongs there: the
            // switch, the idle-off timer and the effects all work without it.
            // The deck's shape comes from BIOS setup where it says — it holds
            // "Keyboard Layout" and "Keyboard Type", which describe the board
            // the machine was actually built with — and from the gaming
            // interface's keyboard-type enumeration where it does not.
            try {
                bool? padFromSetup = StarMon.Hardware.HpBiosSettings.HasNumericPad;

                this.Window.SetKeyboard(
                    this.Op.Platform.System.GetKbdColorSupport()
                        ? this.Op.Platform.System.GetKbdZoneCount() : 0,
                    padFromSetup ?? (this.Op.Platform.System.GetKbdType()
                        != BiosData.KbdType.TenKeyLess),
                    StarMon.Hardware.HpBiosSettings.IsIsoKeyboard);
            } catch {
                this.Window.SetKeyboard(1);
            }

            // Until this, the window shows readings and changes nothing. It is
            // the last step because everything the window can ask for has to
            // exist before it is given somewhere to ask.
            this.Window.Connect(this);

            Update();

            // Cache localized temperature unit string
            this.TemperatureUnitText =
                Config.Locale.Get(Config.L_UNIT + "Temperature" + Config.LS_CUSTOM_FONT);

            // Set up the global hotkey window and apply any saved binding
            this.Hotkey = new HotkeyWindow();
            this.Hotkey.HotkeyPressed += delegate { Os.SetDisplayOff(); };
            ApplyDisplayOffHotkey();

            // Listen for the message another instance sends. The notification
            // icon's window is what receives it: broadcasts only reach
            // top-level windows, and it is the one this application owns
            // whether or not anything is on screen.
            this.Filter = new GuiFilter(Context);
            this.Tray.Message += (message, wParam, lParam) =>
                this.Filter.Handle(message, wParam, lParam);

            // Receive suspend and resume event notifications.
            //
            // Always, not only when the fan program is set to suspend with the
            // machine. That setting says what happens to the program; it does
            // not say whether this application would like to know the machine
            // went to sleep. Gated on it, a user who had turned it off got no
            // resume notification at all — so the performance profile, the
            // graphics power and the backlight were left as whatever the
            // firmware decided on the way back up, and nothing here knew to
            // put them right. The program's own suspend still respects the
            // setting; see GuiOp.SuspendResumeCallback.
            Gui.RegisterSuspendResumeNotification(this.Op.SuspendResumeCallback);

            // What to put back when the machine wakes. The tray host owns the
            // backlight state and the last colours, so it hands the operation
            // class a way to ask rather than a reference to itself.
            this.Op.RestoreBacklight = includeColour => ReapplyKbdState(includeColour);

            // Set up the timer. Background priority, so a tick that arrives
            // while the window is laying itself out waits rather than
            // competing with it: nothing here is worth a dropped frame.
            this.Timer = new DispatcherTimer(DispatcherPriority.Background, this.Interface) {
                Interval = TimeSpan.FromMilliseconds(Config.GuiTimerInterval)
            };
            this.Timer.Tick += EventTimerTick;
            this.Timer.Start();

            // Show the main form if requested by the environment variable
            if (Environment.GetEnvironmentVariable(Config.EnvVarSelfName) != null
                && Environment.GetEnvironmentVariable(Config.EnvVarSelfName).Contains(Config.EnvVarSelfValueKey))

                this.Op.KeyHandler(Gui.MessageParam.NoLastParam);

            // Unset the environment variable, so that
            // it does not propagate to child processes
            Environment.SetEnvironmentVariable(Config.EnvVarSelfName, null);

            // Automatically apply settings, if enabled
            if (Config.AutoConfig)
                this.Op.AutoConfigRun();

            // Register the power-mode change event handler
            SystemEvents.PowerModeChanged += EventPowerChange;

        }

        // Puts everything back before the process ends.
        //
        // The notification icon is the one that cannot be skipped: left
        // behind, it lingers in the tray as a ghost until the user happens to
        // wave the cursor over it.
        public void Shutdown()
        {

            this.Timer.Stop();

            if (this.Tray != null)
                this.Tray.Dispose();

            SystemEvents.PowerModeChanged -= EventPowerChange;
            Gui.UnregisterSuspendResumeNotification();

            // Terminate the fan program, if any
            if (this.Op != null && this.Op.Program.IsEnabled)
                this.Op.Program.Terminate();

            ReleaseTheFans();

            // Release the global hotkey and its hidden window
            if (this.Hotkey != null)
                this.Hotkey.Destroy();

        }

        // Hands the fans back to the firmware.
        //
        // Nothing on any exit path did this. Quitting with the fans switched
        // off left them off; quitting while thermal protection had them pinned
        // at maximum left them there. Recovery depended entirely on the
        // controller's own failsafe countdown, which governs the manual level
        // and not the off switch or the maximum toggle - and which the panic
        // path deliberately zeroes.
        //
        // The comment on the dispatcher exception handler in App.cs names the
        // concern exactly: "a tray application that vanishes leaves the fans
        // wherever it last set them". It was identified and not acted on.
        //
        // Each step is separate and each is allowed to fail. A machine that
        // will not take one of these is a machine that will not take it, and
        // stopping at the first refusal would leave the rest of the state
        // exactly as it was.
        internal void ReleaseTheFans() {

            if(this.Op == null || this.Op.Platform == null)
                return;

            StarMon.Hardware.Platform.IFanArray fans = this.Op.Platform.Fans;
            if(fans == null)
                return;

            // Order matters as much here as it does when setting them: the
            // overrides come off before the mode goes back to automatic, or
            // the mode is applied under an override that then outlives it.
            StarMon.AppService.FanControl.ReleaseToFirmware(fans);

            try { this.Op.Platform.ClearFanModeSticky(); } catch { }
            try { this.Op.Platform.ClearGpuPowerSticky(); } catch { }

            Library.Logger.Info("Fan", "Fans handed back to the firmware",
                "on shutdown");

        }
        #endregion

        #region Event Handlers
        // Whether the caller is on some other thread and therefore has to
        // marshal before touching anything the interface owns
        internal bool NeedsUiThread()
        {
            return !this.Interface.CheckAccess();
        }

        // Runs an action on the interface thread. Anything that touches the
        // notification icon or a window from a background thread has to go
        // through here.
        internal void OnUiThread(Action action)
        {

            if (this.Interface.CheckAccess())
            {
                action();
                return;
            }

            try
            {
                this.Interface.BeginInvoke(DispatcherPriority.Background, action);
            }
            catch (Exception ex)
            {
                // The only expected failure here is the dispatcher shutting
                // down between the check above and the call, which is harmless
                // during exit, but it should not vanish without a trace either
                Logger.Error("GuiTray", "Could not marshal to the interface thread", ex.Message);
            }

        }

        // Shows the tray context menu at the point the shell nominated
        private void ShowTrayMenu(System.Windows.Point anchor)
        {

            // Make this application the foreground one first, so the menu is
            // dismissed when the user clicks anywhere outside it. A tray menu
            // belongs to a process that owns no visible window, and without
            // this it opens and then simply stays there.
            //
            // The old ToolStrip menu had a window of its own to raise; the WPF
            // popup does not exist until it opens, so the notification icon's
            // window stands in — any window of this process will do.
            if (this.Tray != null)
                User32.SetForegroundWindow(this.Tray.Handle);

            // The anchor comes from the shell rather than from the cursor.
            // Version 4 of the notification protocol reports where the menu
            // belongs, which is the same place for a right-click and for the
            // keyboard — the cursor position is right only for the first.
            this.WpfMenu.Show(anchor);

        }

        // Handles a power-mode change event
        private void EventPowerChange(object sender, PowerModeChangedEventArgs e)
        {

            // Only respond to status change events,
            // which excludes Resume and Suspend
            if (e.Mode == PowerModes.StatusChange)
                this.Op.PowerChange();

        }

        // Handles a timer tick
        private void EventTimerTick(object sender, EventArgs e)
        {

            // Perform the updates as scheduled; a hardware hiccup leaking out
            // of a periodic update must never take down the whole application
            // (identical consecutive entries are stacked by the logger)
            try
            {
                Update();
            }
            catch (Exception ex)
            {
                Logger.Error("GuiTray", "Periodic update failed", ex.Message);
            }

        }
        #endregion

        #region Visual Methods
        // Brings the already-running application instance to the user's attention
        public void BringFocus()
        {

            // Show a balloon notification
            ShowBalloonTip(Config.Locale.Get(Config.L_GUI + "AlreadyRunning"));

            // Show the main GUI form
            ShowFormMain();

        }

        // Sets the notification icon tooltip text
        public void SetNotifyText(string text = "")
        {

            // The reflection trick that used to defeat NotifyIcon's
            // 64-character limit is gone with it: the shell's version-4
            // protocol allows 128, and the wrapper asks for that protocol
            this.Tray.SetTooltip(text);

        }

        // Shows a balloon tip above the notification area icon
        public void ShowBalloonTip(string message, string title = null, Shell32.NotifyIconInfoFlags icon = Shell32.NotifyIconInfoFlags.None)
        {

            // A duration of zero means the user has asked for no balloons.
            // The shell no longer honours a duration at all — the notification
            // stays for as long as the accessibility timeout says — so the
            // setting now only decides whether one is shown.
            if (Config.GuiTipDuration <= 0)
                return;

            this.Tray.ShowBalloon(message, title ?? Config.AppName,
                icon);

        }

        #region Tray host
        // What the WPF tray menu needs from the application. Deliberately a
        // small surface: the menu is meant to outlive this class.

        StarMon.Hardware.Platform.Platform StarMon.Ui.Shell.ITrayHost.Platform
        {
            get { return this.Op.Platform; }
        }

        StarMon.Hardware.Platform.FanProgram StarMon.Ui.Shell.ITrayHost.Program
        {
            get { return this.Op.Program; }
        }

        void StarMon.Ui.Shell.ITrayHost.ToggleWindow()
        {
            ToggleFormMain();
        }

        void StarMon.Ui.Shell.ITrayHost.ShowWindow(StarMon.Ui.Views.Section section)
        {
            ShowFormMain();
            this.Window.Select(section);
        }

        void StarMon.Ui.Shell.ITrayHost.Exit()
        {
            System.Windows.Application.Current.Shutdown();
        }

        bool StarMon.Ui.Shell.ITrayHost.IsWindowShown
        {
            get { return IsWindowVisible; }
        }

        bool StarMon.Ui.Shell.ITrayHost.IsDynamicIcon
        {
            get { return this.TrayPainter.IsDynamic; }
            set { Config.GuiDynamicIcon = value; this.TrayPainter.IsDynamic = value; }
        }

        bool StarMon.Ui.Shell.ITrayHost.IsDynamicIconBackground
        {
            get { return this.TrayPainter.HasBackdrop; }
            set
            {
                Config.GuiDynamicIconHasBackground = value;
                this.TrayPainter.HasBackdrop = value;
            }
        }

        void StarMon.Ui.Shell.ITrayHost.RefreshIcon()
        {
            // The painter skips a redraw when nothing about the icon changed,
            // which is what keeps the tray from flickering once a second — so
            // a settings change has to say explicitly that something did
            this.TrayPainter.Invalidate();

            // Zero rather than one: redrawn on the next tick rather than an
            // interval later, so a setting changed from the menu is visible
            // before the user has finished closing it
            this.UpdateIconTick = 0;
        }

        void StarMon.Ui.Shell.ITrayHost.RefreshFanState()
        {
            this.UpdateProgramTick = 1;
            this.Poll.Request();
        }

        void StarMon.Ui.Shell.ITrayHost.SetKbdColorByTemp(bool enable)
        {
            SetKbdColorByTemp(enable);
        }

        void StarMon.Ui.Shell.ITrayHost.SetKbdEffect(int effect)
        {
            SetKbdEffect(effect);
        }

        void StarMon.Ui.Shell.ITrayHost.SetKbdBacklight(bool on)
        {
            SetKbdBacklightState(on);
        }

        void StarMon.Ui.Shell.ITrayHost.SetKbdColor(int colour)
        {
            DisableKbdColorByTemp();
            ApplyKbdColor(colour, true);
        }

        void StarMon.Ui.Shell.ITrayHost.SetKbdZoneColors(int[] colours)
        {

            if (colours == null || colours.Length == 0)
                return;

            DisableKbdColorByTemp();

            // The colour table always carries four zones, whatever the
            // keyboard has: a machine with fewer simply ignores the rest,
            // and a short array would be rejected outright
            int[] zones = new int[4];
            for (int i = 0; i < zones.Length; i++)
                zones[i] = colours[i < colours.Length ? i : colours.Length - 1];

            this.KbdLastColors = zones;

            this.Op.Platform.System.SetKbdColor(
                new BiosData.ColorTable(zones, true));

        }
        #endregion

        // The last answer IsWindowVisible gave, kept for the poll thread.
        //
        // A window is a DependencyObject, and IsVisible cannot be read from
        // anywhere but the dispatcher thread: the attempt throws. The poller
        // runs its gathering on a thread-pool thread, so the delegate it was
        // given caught that exception and answered "not on screen" every
        // single time. That answer is what decides whether the discrete card
        // may be woken while on battery, so the graphics readings froze for
        // precisely the case the setting exists to allow — someone on battery
        // with the window open, looking at them.
        private volatile bool WindowVisibleCache;

        // Whether the window is on screen. Dispatcher thread only; anything
        // else reads WindowVisibleCache, which this keeps current.
        internal bool IsWindowVisible
        {
            get
            {
                System.Windows.Window window = this.Window.Current;
                bool visible = window != null && window.IsVisible;
                this.WindowVisibleCache = visible;
                return visible;
            }
        }

        // Shows the main window
        public void ShowFormMain()
        {

            System.Windows.Window window = this.Window.Ensure();

            if (!window.IsVisible)
                window.Show();

            if (window.WindowState == System.Windows.WindowState.Minimized)
                window.WindowState = System.Windows.WindowState.Normal;

            // Briefly topmost, then back. This is what brings the application
            // into focus even when it was started by the task scheduler from a
            // background process, which is otherwise not allowed to steal it.
            window.Topmost = true;
            window.Activate();
            window.Topmost = Config.GuiStayOnTop;

            // Take a reading straight away rather than leaving the window
            // showing dashes until the next tick comes round — and let the
            // poll thread know the window is up before it gathers, so that
            // very first reading already includes the graphics card on battery
            this.WindowVisibleCache = window.IsVisible;
            this.Poll.Request();

            Logger.Gui("Window", "Window shown");

        }

        // Toggles the main window
        public void ToggleFormMain()
        {

            if (!IsWindowVisible)
                ShowFormMain();
            else
            {
                this.Window.Current.Hide();
                this.WindowVisibleCache = false;
                Logger.Gui("Window", "Window hidden to the tray");
            }

        }
        #endregion

        // Performs update operations as scheduled
        // This method is called periodically by a timer event
        public void Update()
        {

            // Re-read the intervals rather than keeping the values they had at
            // startup: the menu lets the user change the monitoring cadence
            // while the application is running, and a slot holding a stale
            // interval would simply ignore that
            this.TickIcon.Interval = Config.UpdateIconInterval;
            this.TickMonitor.Interval = Config.UpdateMonitorInterval;
            this.TickProgram.Interval = Config.UpdateProgramInterval;
            this.TickRecord.Interval = Config.UpdateRecordInterval;
            this.TickGuard.Interval = Config.UpdateMonitorInterval;

            // Wind back every slot that has run its interval. This is a pass
            // of its own because the foreground refresh and the background
            // recording below are mutually exclusive: the one that is not
            // asked keeps its counter at zero and so fires the moment it is.
            this.TickIcon.Rewind();
            this.TickMonitor.Rewind();
            this.TickRecord.Rewind();
            this.TickProgram.Rewind();
            this.TickGuard.Rewind();

            // Update the fan program or extend the countdown
            if (this.TickProgram.Due())
            {

                // Update the program, if active
                if (this.Op.Program.IsEnabled)
                    this.Op.Program.Update();

                // Alternatively, update any non-zero countdown, but never
                // while running hot or without a plausible temperature reading:
                // letting the countdown lapse hands fan control back to the
                // EC's own automatic failsafe, which must always stay available
                else if (Config.FanCountdownExtendAlways && SafeToKeepManualFans())
                    this.Op.Program.UpdateCountdown(false, true);

            }

            // Automatic thermal protection and throttle notification, at the
            // monitoring cadence, regardless of whether the window is visible
            if (this.TickGuard.Due())
            {
                CheckThermalGuard();

                // Keep the user-selected fan mode from resetting, unless
                // thermal protection is forcing the fans; rate-limited so
                // the EC/BIOS is not queried on every second tick
                if (!this.ThermalProtectionActive
                    && this.Op.Platform.HasDesiredFanMode && !this.Op.Program.IsEnabled)
                {
                    bool isFanMax = this.Op.Platform.Fans.GetMax();
                    bool isFanOff = this.Op.Platform.Fans.GetOff();

                    if (!isFanMax && !isFanOff)
                        this.Op.Platform.MaintainFanModeSticky();
                }

                // The graphics power the user asked for, on the same cadence.
                // Outside the fan-mode guard above: a fan override says
                // nothing about how much power the card may draw, and thermal
                // protection forcing the fans is not a reason to quietly give
                // the wattage back.
                if (this.Op.Platform.HasDesiredGpuPower)
                    try { this.Op.Platform.MaintainGpuPowerSticky(); } catch { }

                // Recolor the keyboard backlight to match the temperature,
                // right after the guard check refreshed the hottest reading
                // (held still while the idle timer has the backlight off,
                // matching the behavior of the animated effects)
                if (!this.KbdIdle.IsEngaged)
                    UpdateKbdColorByTemp();
            }

            // The idle backlight switch-off and the animated color effects
            // run at the base tick rate (once a second); both only touch
            // the EC when something actually changes
            UpdateKbdIdleAndEffects();

            // Take a reading for the window at the monitoring cadence while it
            // is on screen, and at the much slower recording one while it is
            // hidden in the tray — so the history carries on accumulating
            // without paying for a full refresh nobody is looking at
            bool visible = IsWindowVisible;

            if (visible && this.TickMonitor.Due())
                this.Poll.Request();
            else if (!visible && this.TickRecord.Due())
                this.Poll.Request();

            // Update the notification icon, if dynamic
            if (this.TickIcon.Due())
            {

                // The hottest sensor, forced only if neither the window nor a
                // running fan program has already refreshed it this tick
                byte hottest = this.Op.Platform.GetMaxTemperature(
                    !visible && (!this.Op.Program.IsEnabled || this.UpdateProgramTick != 1));

                // The icon carries the bare number: two digits fill sixteen
                // pixels and can be read at a glance, where "78°C" shrinks to
                // an unreadable smear. The unit stays in the tooltip beside it.
                string number = Conv.GetString(hottest, 2, 10);
                string temperature = number + this.TemperatureUnitText;

                // Update the background depending on the fan mode
                if (this.TrayPainter.IsDynamic)
                    this.TrayPainter.Background =
                        this.Op.Platform.Fans.GetMode() == BiosData.FanMode.Performance ?
                            StarMon.Ui.Shell.DynamicIcon.Backdrop.Warm
                            : StarMon.Ui.Shell.DynamicIcon.Backdrop.Cool;

                // Always drive the painter, whether or not the icon is dynamic:
                // the painter itself draws the static mark when it is not, and
                // gating this call behind the dynamic flag is what left the icon
                // stuck showing the last temperature after it was switched off.
                this.TrayPainter.Update(number);

                // Keep the tooltip saying something the number in the tray can
                // be read against: the hottest reading and, when one is running,
                // the fan program driving it. Left alone, the tooltip held the
                // version string it was given at startup — which explained the
                // icon beside it not at all.
                UpdateTooltip(temperature);

            }

        }

        // Composes the notification-icon tooltip: the application, the hottest
        // temperature, and the running fan program if there is one
        private void UpdateTooltip(string temperature)
        {

            string tip = Config.AppBrand + " · " + temperature;

            if (this.Op.Program.IsEnabled)
                tip += " · " + this.Op.Program.GetName();

            SetNotifyText(tip);

        }

        // Whether it is safe to keep re-extending the manually set fan state:
        // requires a recent, plausible temperature reading below the protection
        // threshold. On any doubt (no reading, sensors failing, running hot)
        // the EC's automatic failsafe wins and the countdown is left to lapse.
        internal bool SafeToKeepManualFans()
        {

            int cpu = 0, gpu = 0;
            try
            {
                byte[] levels = this.Op.Platform.Fans.GetLevels();
                if (levels != null && levels.Length > 1)
                {
                    cpu = levels[0];
                    gpu = levels[1];
                }
            }
            catch { }

            bool safe = this.Guard.SafeToKeepManualFans(
                this.Op.Platform.LastMaxTemperature, Config.ThermalProtectionHighC,
                cpu, gpu, Config.FanLevelMax);

            // Said out loud, once per episode. This is the mechanism behind
            // "the fans went back to automatic on their own", which the manual
            // has an entry for and the application never actually reported —
            // so it read as the setting undoing itself for no reason.
            if (safe)
            {
                this.CountdownWarned = false;
            }
            else if (!this.CountdownWarned && Config.FanCountdownExtendAlways)
            {
                this.CountdownWarned = true;
                Logger.Warning("Fan",
                    "The manual fan speed is being allowed to lapse",
                    "hottest " + this.Op.Platform.LastMaxTemperature + " °C at or above "
                        + Config.ThermalProtectionHighC + " °C and the fans are not near "
                        + "the ceiling, so the Embedded Controller's own failsafe is "
                        + "left to take over");
            }

            return safe;

        }

        // Whether the lapse above has already been reported this episode
        private bool CountdownWarned;

        // Lifts the switched-off override, if it is in force.
        //
        // Read before written: SetOff is an Embedded Controller write, and
        // issuing one every second on a machine that never had the fans
        // switched off costs a bus exchange for nothing.
        private void ReleaseFanOff()
        {
            try {
                if (this.Op.Platform.Fans.GetOff())
                    this.Op.Platform.Fans.SetOff(false);
            } catch { }
        }

        // Forces the fans to maximum at the high threshold, releasing below
        // the low threshold, and notifies on CPU thermal throttling; if the
        // temperature keeps climbing regardless, all manual fan overrides
        // are dropped so the EC's own thermal management takes over
        private void CheckThermalGuard()
        {

            try
            {

                // Refresh the hottest reading only when something actually
                // consumes it: the protection logic itself, the countdown
                // safety check, or the temperature-reactive backlight
                byte temp = 0;
                if (Config.ThermalProtectionEnabled || Config.KbdColorByTemp
                    || Config.FanCountdownExtendAlways)
                    temp = this.Op.Platform.GetMaxTemperature(true);

                // Automatic thermal protection with hysteresis. The decision is
                // the guard's; carrying it out is this method's.
                switch (this.Guard.Step(Config.ThermalProtectionEnabled, temp,
                    Config.ThermalProtectionHighC, Config.ThermalProtectionLowC))
                {

                    case ThermalAction.Engage:
                        // The switched-off override sits above the maximum one:
                        // asking for maximum while the fans are held off changes
                        // a number nothing is reading, and the machine carries on
                        // heating with the fans stopped. Everything else in the
                        // application clears one before asserting the other; the
                        // one path that exists to guarantee cooling has to as well.
                        ReleaseFanOff();
                        try { this.Op.Platform.Fans.SetMax(true); } catch { }
                        Logger.Warning("Thermal", "Protection engaged at " + temp + " °C");
                        ShowBalloonTip(
                            Config.Locale.Get(Config.L_GUI + "ThermalProtectOn")
                                + " (" + temp + "°C)",
                            Config.AppName, Shell32.NotifyIconInfoFlags.Warning);
                        break;

                    case ThermalAction.Panic:
                        // Still getting hotter despite the max-fan request:
                        // clear every manual override (program, sticky mode,
                        // custom levels, manual toggle, countdown) so the EC
                        // regains full automatic control immediately
                        Logger.Warning("Thermal",
                            "Still climbing at " + temp + " °C — all overrides dropped");
                        try { if (this.Op.Program.IsEnabled) this.Op.Program.Terminate(); } catch { }
                        try { this.Op.Platform.ClearFanModeSticky(); } catch { }
                        ReleaseFanOff();
                        try { this.Op.Platform.Fans.SetLevels(new byte[] { Byte.MaxValue, Byte.MaxValue }); } catch { }
                        try { this.Op.Platform.Fans.SetManual(false); } catch { }
                        try { this.Op.Platform.Fans.SetCountdown(0); } catch { }
                        try { this.Op.Platform.Fans.SetMax(true); } catch { }
                        ShowBalloonTip(
                            Config.Locale.Get(Config.L_GUI + "ThermalProtectPanic")
                                + " (" + temp + "°C)",
                            Config.AppName, Shell32.NotifyIconInfoFlags.Warning);
                        break;

                    case ThermalAction.Release:
                        try { this.Op.Platform.Fans.SetMax(false); } catch { }
                        Logger.Info("Thermal", "Protection released at " + temp + " °C");
                        ShowBalloonTip(
                            Config.Locale.Get(Config.L_GUI + "ThermalProtectOff")
                                + " (" + temp + "°C)");
                        break;

                    case ThermalAction.ReleaseQuiet:
                        // The user switched protection off while it was
                        // engaged; release without a notification they did
                        // not ask for
                        try { this.Op.Platform.Fans.SetMax(false); } catch { }
                        break;

                }

                // Thermal-throttle notification, rate-limited by the guard
                if (Config.ThrottleNotifyEnabled
                    && (StarMon.Hardware.Cpu.CpuTemperature.GetThrottleStatus()
                        & StarMon.Hardware.Cpu.CpuTemperature.ThrottleFlags.Thermal) != 0
                    && this.Guard.ShouldNotifyThrottle(Environment.TickCount))
                {
                    ShowBalloonTip(
                        Config.Locale.Get(Config.L_GUI + "ThrottleNotify"),
                        Config.AppName, Shell32.NotifyIconInfoFlags.Warning);
                }

            }
            catch (Exception ex)
            {
                // The guard must never take the tick down with it, but a
                // failing thermal check is worth knowing about
                Logger.Error("GuiTray", "Thermal guard check failed", ex.Message);
            }

        }

        // Recolors the keyboard backlight to reflect the hottest temperature
        // reading, if configured to do so: green when cool, through yellow,
        // to red when hot. Only writes to the EC when the color changes.
        internal void UpdateKbdColorByTemp()
        {

            if (!Config.KbdColorByTemp)
            {
                this.KbdTempColorLast = -1;
                return;
            }

            // Requires color control (the answer is cached after first use)
            if (!this.Op.Platform.System.GetKbdColorSupport())
                return;

            byte temp = this.Op.Platform.LastMaxTemperature;
            if (temp == 0)
                return;

            int color = BacklightColor.FromTemperature(temp);
            if (color == this.KbdTempColorLast)
                return;

            try
            {
                ApplyKbdColor(color, true);
                this.KbdTempColorLast = color;
            }
            catch (Exception ex)
            {
                Logger.Error("GuiTray", "Temperature-reactive backlight update failed", ex.Message);
            }

        }

        // Applies one colour to every zone.
        //
        // Straight to the hardware. The Windows Forms build went through a
        // keyboard class that also held the picture it drew, so the two could
        // not get out of step; the window keeps its own view model and is told
        // separately, which is why the parameter that used to ask for a
        // repaint is gone.
        private void ApplyKbdColor(int color, bool refreshForm)
        {

            this.KbdLastColors = new int[] { color, color, color, color };

            this.Op.Platform.System.SetKbdColor(
                new BiosData.ColorTable(this.KbdLastColors, true));

        }

        // Switches the temperature-reactive mode and any animated effect off
        // and saves the setting; called when the user picks an explicit
        // color or preset
        internal void DisableKbdColorByTemp()
        {

            if (Config.KbdColorByTemp || Config.KbdColorEffect != 0)
            {
                Config.KbdColorByTemp = false;
                Config.KbdColorEffect = 0;
                this.KbdTempColorLast = -1;
                this.KbdFx.Reset();
                this.KbdFx.BaseColor = -1;
                Config.Save();
            }

        }

        // Switches the temperature-reactive backlight mode on or off from the
        // menu, taking over from any animated effect (which puts the color it
        // had replaced back first)
        internal void SetKbdColorByTemp(bool enable)
        {

            if (enable && Config.KbdColorEffect != 0)
                SetKbdEffect(0);

            Config.KbdColorByTemp = enable;
            this.KbdTempColorLast = -1;
            Config.Save();

            // Apply the current temperature color right away when enabling,
            // instead of waiting for the next monitoring tick
            if (enable)
                UpdateKbdColorByTemp();

        }

        // Switches the animated backlight effect (0 = none, 1 = color cycle,
        // 2 = breathing), taking over from the temperature-reactive mode and
        // restoring the original color when the effects are switched off
        internal void SetKbdEffect(int effect)
        {

            if (effect != 0)
            {
                Config.KbdColorByTemp = false;
                this.KbdTempColorLast = -1;
            }

            // The color from before the first effect took over,
            // to be put back once no effect is running anymore
            int restore = effect == 0 ? this.KbdFx.BaseColor : -1;

            Config.KbdColorEffect = effect;
            this.KbdFx.Reset();
            if (effect == 0)
                this.KbdFx.BaseColor = -1;

            if (restore >= 0)
                try { ApplyKbdColor(restore, true); } catch { }

            Config.Save();

        }

        // Runs the idle backlight switch-off and advances the animated color
        // effect; called on every timer tick (once a second)
        private void UpdateKbdIdleAndEffects()
        {

            try
            {
                UpdateKbdIdle();

                // The effects hold still while the backlight is idled off
                if (!this.KbdIdle.IsEngaged)
                    UpdateKbdEffect();
            }
            catch (Exception ex)
            {
                Logger.Error("GuiTray", "Keyboard idle/effect update failed", ex.Message);
            }

        }

        // Switches the keyboard backlight off after the configured minutes
        // without any keyboard or mouse input, and back on upon activity
        private void UpdateKbdIdle()
        {

            if (!this.KbdIdle.IsEngaged && Config.KbdIdleOffMinutes <= 0)
                return;

            // Requires backlight control (the answer is cached after first use)
            if (!this.Op.Platform.System.GetKbdBacklightSupport())
                return;

            // Milliseconds since the last user input, tick-wrap-safe
            User32.LASTINPUTINFO info = new User32.LASTINPUTINFO();
            info.cbSize = (uint) System.Runtime.InteropServices.Marshal.SizeOf(
                typeof(User32.LASTINPUTINFO));
            if (!User32.GetLastInputInfo(ref info))
                return;
            uint idleMs = unchecked((uint) Environment.TickCount - info.dwTime);

            switch (this.KbdIdle.Step(idleMs, Config.KbdIdleOffMinutes))
            {

                case IdleAction.TurnOn:
                    SetKbdBacklightState(true);
                    break;

                // The watch only asks about the hardware when the answer can
                // change what it does, and remembers a backlight the user had
                // already switched off rather than asking again every tick
                case IdleAction.Query:
                    if (this.KbdIdle.Resolve(GetKbdBacklightState()))
                        SetKbdBacklightState(false);
                    break;

            }

        }

        // Advances the animated backlight effect by one step: a slow sweep
        // around the hue circle, or a breathing swell of the current color;
        // writes at most one color a second, and only when it changed
        private void UpdateKbdEffect()
        {

            if (Config.KbdColorEffect == 0 || Config.KbdColorByTemp)
                return;

            // Requires color control (the answer is cached after first use)
            if (!this.Op.Platform.System.GetKbdColorSupport())
                return;

            // The color in use when the effect started doubles as the
            // breathing base and is restored when the effects are done
            if (this.KbdFx.BaseColor < 0)
                this.KbdFx.BaseColor = GetCurrentKbdColor();

            // The configured speed, 1-5, as a multiple of the fixed base rate
            // (3 keeps the rate the effects always ran at)
            this.KbdFx.Speed = Config.KbdEffectSpeed / 3f;

            int color = this.KbdFx.Step(Config.KbdColorEffect);

            if (color == this.KbdFx.LastColor)
                return;

            try
            {
                ApplyKbdColor(color, false);
                this.KbdFx.LastColor = color;
            }
            catch { }

        }

        // Returns the current (first-zone) backlight color,
        // falling back to white if it cannot be determined
        private int GetCurrentKbdColor()
        {

            try
            {
                BiosData.ColorTable table = this.Op.Platform.System.GetKbdColor();
                if (table.Zone != null && table.Zone.Length > 0)
                    return (int) table.Zone[0].ValueReverse & 0xFFFFFF;
            }
            catch { }

            return 0xFFFFFF;

        }

        // Reads the current backlight state
        // Whether the backlight is lit.
        //
        // The application's own record rather than a question put to the
        // firmware, because on this hardware the firmware's answer is not
        // trustworthy: it accepts 0xE4 for on and 0x64 for off, drives the
        // light correctly, and then reports the state back the other way
        // round. Anything that believed the answer contradicted itself — the
        // window's switch flipped itself back a few seconds after every use,
        // and the idle watch could decide a lit keyboard was already dark.
        //
        // Nothing outside this application changes the backlight, so there is
        // nothing to discover by asking: every path that switches it goes
        // through SetKbdBacklightState below. The firmware is read exactly
        // once, to seed the state at startup, and never consulted again.
        private bool? KbdBacklightOn;

        private bool GetKbdBacklightState()
        {

            if (this.KbdBacklightOn == null)
                try
                {
                    this.KbdBacklightOn =
                        this.Op.Platform.System.GetKbdBacklight() == BiosData.Backlight.On;
                }
                catch { this.KbdBacklightOn = false; }

            return this.KbdBacklightOn.Value;

        }

        // Sets the backlight state, and remembers it.
        //
        // The single place the backlight is switched, whichever surface asked
        // — the window's switch, the tray menu, the idle watch. So it is also
        // the single place that knows what the state now is, and the one that
        // tells the window rather than leaving it to find out.
        private void SetKbdBacklightState(bool flag)
        {

            try
            {
                this.Op.Platform.System.SetKbdBacklight(flag);
            }
            catch { }

            this.KbdBacklightOn = flag;

            // Keeps the window's switch in step with a change made from the
            // tray menu or by the idle watch, without it having to poll
            if (this.Window != null)
                this.Window.SetBacklightState(flag);

        }

        // Writes the backlight state and colours to the hardware again.
        //
        // Called after a resume. The state above is held rather than read
        // because the firmware answers the question wrongly — which also means
        // nothing here can notice the light having been reset while the
        // machine was asleep, so it has to be re-asserted rather than checked.
        // The colours go with it: a backlight switched back on lights in
        // whatever the controller came up with otherwise.
        internal void ReapplyKbdState(bool includeColour)
        {

            if (this.KbdBacklightOn.HasValue)
                try
                {
                    this.Op.Platform.System.SetKbdBacklight(this.KbdBacklightOn.Value);
                }
                catch { }

            if (!includeColour || this.KbdLastColors == null)
                return;

            try
            {
                this.Op.Platform.System.SetKbdColor(
                    new BiosData.ColorTable(this.KbdLastColors, true));
            }
            catch { }

        }

        // The colours last written, so a resume can put them back
        private int[] KbdLastColors;

        // (Re-)applies the configured global "display off" hotkey
        public void ApplyDisplayOffHotkey()
        {
            if (this.Hotkey != null)
                this.Hotkey.Set((uint) Config.DisplayOffHotkeyMods, (uint) Config.DisplayOffHotkeyKey);
        }

    }

    // A hidden window that owns a single global hotkey and raises an event
    // when it is pressed.
    //
    // The hotkey is registered against a window handle rather than the thread.
    // A thread registration is delivered as a message with no window to
    // receive it, so whether anything sees it depends on which pump happens to
    // be turning at the time — reliable in a plain message loop and not in a
    // dispatcher one.
    //
    // Message-only is right here, unlike the notification icon's window: this
    // one wants nothing broadcast to it, only what it asked for.
    internal class HotkeyWindow
    {

        private const int HOTKEY_ID = 0xB001;

        private readonly System.Windows.Interop.HwndSource Source;

        // Raised on the interface thread whenever the hotkey is pressed
        internal event Action HotkeyPressed;

        internal HotkeyWindow()
        {

            this.Source = new System.Windows.Interop.HwndSource(
                new System.Windows.Interop.HwndSourceParameters("StarMonHotkey")
                {
                    Width = 0,
                    Height = 0,
                    ParentWindow = new IntPtr(-3)   // HWND_MESSAGE
                });

            this.Source.AddHook(Hook);

        }

        // Registers (or clears, when vk is 0) the global hotkey
        internal void Set(uint mods, uint vk)
        {
            try
            {
                User32.UnregisterHotKey(this.Source.Handle, HOTKEY_ID);
                if (vk != 0 && mods != 0)
                    User32.RegisterHotKey(this.Source.Handle, HOTKEY_ID,
                        mods | User32.MOD_NOREPEAT, vk);
            }
            catch { }
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
            ref bool handled)
        {

            if (msg == User32.WM_HOTKEY && (int) wParam == HOTKEY_ID)
            {
                Action handler = this.HotkeyPressed;
                if (handler != null)
                    try { handler(); }
                    catch (Exception e)
                    {
                        Logger.Error("Hotkey", "The hotkey handler failed", e.Message);
                    }
            }

            return IntPtr.Zero;

        }

        // Unregisters the hotkey and destroys the hidden window
        internal void Destroy()
        {
            try { User32.UnregisterHotKey(this.Source.Handle, HOTKEY_ID); }
            catch { }

            this.Source.RemoveHook(Hook);
            this.Source.Dispose();
        }

    }

}
