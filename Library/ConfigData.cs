// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Hardware.Platform;
using StarMon.Library.Locale;

namespace StarMon.Library
{

    // Implements application-wide configuration settings look-up
    // This part only defines the configuration variables
    public static partial class Config
    {

        // Application metadata
        public static string AppFile = Process.GetCurrentProcess().MainModule.FileName;
        public static string AppName = typeof(App).Assembly.GetName().Name;
        public static string AppVersion = FileVersionInfo.GetVersionInfo(AppFile).ProductVersion;
        public static int AppProcessId = Process.GetCurrentProcess().Id;
        // User-facing brand and developer name
        public const string AppBrand = "StarMon";
        public const string AppDeveloper = "Star";
        public const string AppHomepageLink = "https://github.com/StarWWW/StarMon";

        // Global hotkey to switch the display off (0 = unassigned).
        // Mods is a bit-mask of MOD_ALT/CONTROL/SHIFT/WIN; Key is a virtual-key code.
        public static int DisplayOffHotkeyMods = 0;
        public static int DisplayOffHotkeyKey = 0;

        // Automatically apply settings on start
        public static bool AutoConfig = false;

        // Automatically start up with Windows
        public static bool AutoStartup = false;

        // Ignore BIOS errors if false (for not fully compatible devices)
        public static bool BiosErrorReporting = true;

        // Color presets (overriden at runtime if found in the configuration file)
        public static SortedDictionary<string, BiosData.ColorTable> ColorPreset =
            new SortedDictionary<string, BiosData.ColorTable>()
            {
                ["DefaultRed"] = new BiosData.ColorTable("FF0000"),
                ["DefaultWhite"] = new BiosData.ColorTable("FFFFFF")
            };

        // Prefix for default color presets, name to be resolved through locale
        public const string ColorPresetDefaultPrefix = "Default";

        // Embedded Controller operation parameters
        public static int EcMonInterval = 1000; // Embedded Controller monitoring interval

        // How long before bailing out trying to get the shared EC mutex.
        //
        // Must comfortably exceed the worst case a single transaction can hold
        // it for, which is EcWaitTimeoutMs per wait, four waits per byte, two
        // bytes for a word, times EcRetryLimit retries, all under one lock:
        //
        //     20 ms x 4 x 2 x 3 = 480 ms
        //
        // A value below that does not make anything faster, it just makes
        // concurrent callers give up on a controller that is merely busy.
        // Whenever any of those four numbers changes, recompute this one.
        public static int EcMutexTimeout = 1000;

        public static int EcFailLimit = 15;  // Maximum number of failed attempts waiting to read
        public static int EcRetryLimit = 3;  // Maximum number of read and write attempts

        // How long a single wait for the controller may take [ms]. This is the
        // real bound on a transaction, and the figure EcMutexTimeout above is
        // derived from. A responsive controller answers in microseconds, so
        // this only comes into play when something is actually wrong.
        public static int EcWaitTimeoutMs = 20;

        // How many times to poll the status port at full speed before yielding
        // the processor between polls, within the budget above
        public static int EcWaitLimit = 30;

        // Environment variable settings
        public static string EnvVarSelfName = AppName;
        public const string EnvVarSelfValueGui = "Quiet";
        public const string EnvVarSelfValueKey = "Key";
        public const string EnvVarSysRoot = "SystemRoot";

        // Exit status
        public enum ExitStatus : int
        {
            NoError = 0,  // Default
            ErrorBios = 1,  // BIOS initialization error
            ErrorEc = 2,  // Embedded Controller initialization error
            ErrorLocale = 3,  // Localizable message system error
            ErrorTask = 4,  // Invalid task identifier

            // Anything else the command line reported as an error: an
            // unrecognized argument, a value that would not parse, a fan
            // program that is not in the configuration file. Every one of
            // these used to print its message and then exit zero, so a script
            // testing ERRORLEVEL after StarMon.exe was told the run succeeded.
            ErrorOperation = 5,

            // A -SelfTest run with at least one failing check. It used to
            // return 1, which is ErrorBios: a build script could not tell a
            // failing test from a machine whose BIOS interface would not
            // initialize, and those call for opposite responses.
            ErrorSelfTest = 6,

            // The machine was positively identified as one this application
            // should not be writing to. Distinct from a BIOS or controller
            // failure: those are a machine that could not be reached, this is
            // one that could and should not be.
            ErrorUnsupportedHardware = 7
        }

        // Whether to always extend the fan countdown timer, so a manually
        // set fan speed does not revert to the BIOS defaults after ~240 s,
        // including while the window is minimised to the tray
        public static bool FanCountdownExtendAlways = true;

        // Fan countdown extension threshold and interval [s]
        public static int FanCountdownExtendInterval = 120;
        public static int FanCountdownExtendThreshold = 5;

        // Whether the fan ceiling and the fan-level path are worked out from
        // the firmware at startup rather than taken from the values below.
        // The values below are one machine's answers, and every Omen and
        // Victus board has its own; see Hardware/DeviceProfile.cs. Turn this
        // off to pin the settings by hand on a machine whose firmware lies.
        public static bool FanLevelAutoDetect = true;

        // Run on a machine the hardware gate refused.
        //
        // The gate declines to start on anything it can positively identify as
        // not an HP portable, because the registers this application writes to
        // are one laptop family's and on another machine the same addresses
        // belong to something else - which the firmware accepts without
        // reporting an error. An Omen desktop was left with a permanently
        // wrong fan curve that way, through a BIOS reset and a Windows
        // reinstall.
        //
        // The escape hatch exists because a gate that refuses has to have one:
        // an OEM variant reporting an unfamiliar manufacturer string would
        // otherwise be unfixable by the person holding it. It is off, and
        // turning it on is a statement that you know what this machine is.
        public static bool HardwareGateOverride = false;

        // Fan level thresholds (for custom level setting with trackbars in Const
        // mode, and as the 100 % end of the fan curve). The maximum is a
        // hardware ceiling: writing a level above it is silently ignored or
        // clamped by the firmware, so the curve would never reach full speed.
        // Overwritten at startup by the probed value unless auto-detect is off.
        public static int FanLevelMax = 56;
        public static int FanLevelMin = 20;

        // Set manual fan mode first using the Embedded Controller before setting fan levels
        // (auto-detected: needed exactly when the BIOS level call is unavailable)
        public static bool FanLevelNeedManual = false;

        // Whether to use the Embedded Controller instead of a BIOS call to set the fan level
        // (auto-detected: used when the BIOS level call is unavailable)
        public static bool FanLevelUseEc = false;

        // Which fan modes a machine offers is not a setting: it is a fact about
        // the board, and Hardware/DeviceProfile.SupportedFanModes() reads it
        // from the firmware's own support flags. A list here was one machine's
        // answer applied to every machine, which is exactly the mistake that
        // list made — it was labelled a Victus limitation and shipped to Omen
        // owners as well, and nothing read it in the end.

        // How often to re-apply the selected fan mode to prevent BIOS reset [ms]
        public static int FanModeKeepAliveMs = 5000;

        // Automatic thermal protection: when the hottest monitored temperature
        // reaches the high threshold the fans are forced to maximum, and released
        // again once it falls back below the low threshold (hysteresis) [°C]
        public static bool ThermalProtectionEnabled = true;
        public static int ThermalProtectionHighC = ThermalProtectionHighDefaultC;
        public static int ThermalProtectionLowC = ThermalProtectionLowDefaultC;

        // The same two figures as constants, so a configuration file that puts
        // them the wrong way round can be put back to something sane on load
        // rather than driving the guard with a band that cannot hold
        public const int ThermalProtectionHighDefaultC = 95;
        public const int ThermalProtectionLowDefaultC = 88;

        // Whether to show a tray notification when the CPU is thermally throttling
        public static bool ThrottleNotifyEnabled = true;

        // Whether to keep polling the NVIDIA dGPU (NVAPI) while on battery. Off by
        // default so the dGPU can stay asleep on battery (less drain / heat).
        public static bool GpuPollOnBattery = false;

        // Fan programs (populated at runtime)
        public static SortedList<string, FanProgramData> FanProgram =
            new SortedList<string, FanProgramData>();

        // Default fan program, which might be loaded on startup
        public static string FanProgramDefault; // Unset by default, since there is no default fan program

        // Default alternative fan program when the system is not on AC power
        public static string FanProgramDefaultAlt; // Unset by default

        // How far below a fan program's threshold the temperature has to fall
        // before the level steps back down [°C]. Zero restores the old
        // behaviour of following the curve exactly in both directions, at the
        // cost of the fans surging whenever a temperature sits on a boundary.
        public static int FanProgramHysteresisC = 3;

        // Whether to check first (using the EC) if the fan mode is not set already
        // before setting it (using a BIOS WMI call) when a fan program is running
        public static bool FanProgramModeCheckFirst = false;

        // If true, fan program will be suspended whenever the system enters low-power mode
        // such as sleep, standby or hibernation, to be automatically re-enabled upon resume
        public static bool FanProgramSuspend = true;

        // Configuration XML file path
        public static string FilePath = "";

        // Fan speed string format (adds a thousand separator)
        public const string FormatFanSpeed = "N0";

        // Default GPU power setting, which might be loaded on startup
        public static string GpuPowerDefault = "Maximum";

        // Interval between applying the GPU power settings again
        // (repeated, since they don't always take effect the first time)
        public static int GpuPowerSetInterval = 200;

        // Pairs of color values to create either a warm or a cool gradient
        // Note: for some reason, Color.FromArgb() only takes signed input
        // even though it would make much more sense to be unsigned
        public const int GuiColorCoolDark = unchecked((int)0xFF8804FF); // Magenta
        public const int GuiColorCoolLite = unchecked((int)0xFF03EF9B); // Teal
        public const int GuiColorWarmDark = unchecked((int)0xFFFF0802); // Red
        public const int GuiColorWarmLite = unchecked((int)0xFFAC02FF); // Orange

        // Two additional colors for the RTF text box with better readability
        public const int GuiColorTextBlue = unchecked((int)0xFF4182C9); // Blue
        public const int GuiColorTextTeal = unchecked((int)0xFF0C9D7A); // Teal

        // The keyboard's unlit colour, the four zone placeholder colours and
        // the colour picker's sixteen custom slots all belonged to the Windows
        // Forms interface, which drew the deck by recolouring a bitmap and
        // hosted the system colour dialog. The deck is drawn from geometry now
        // and the picker is this application's own, so all three described a
        // mechanism that no longer exists — while still being written into the
        // configuration file, where they read as settings that do something.

        // Whether closing the window closes the whole application
        // (rather than hiding it to the notification area)
        public static bool GuiCloseWindowExit = false;

        // Whether to use a dynamic notification icon by default
        public static bool GuiDynamicIcon = false;

        // Whether the dynamic icon has a background or not
        public static bool GuiDynamicIconHasBackground = false;

        // Font to size ratio, based on empirical values of 23/32, 29/40, 35/48, 44/60, 46/64
        public const float GuiDynamicIconFontSizeRatio = 0.71875f;

        // Multiplier at which to render the dynamic notification icon, defaults to 2
        public const byte GuiDynamicIconUpscaleRatio = 2;

        // Name under which a custom message identifier is registered for cross-instance communication
        public const string GuiMessageId = "WM_STARMON_FOCUS";

        // Whether the main form remains on top of other windows when shown
        public static bool GuiStayOnTop = false;

        // Timer interval, determines how frequently a tick occurs [ms]
        public const int GuiTimerInterval = 1000;

        // How long to show a tip in the notification area, disabled if set to 0
        public static int GuiTipDuration = 30000;

        // Whether the keyboard backlight color follows the hottest temperature
        // reading, sweeping from green when cool through yellow to red when hot
        public static bool KbdColorByTemp = false;

        // Animated keyboard backlight effect:
        // 0 = none, 1 = slow color cycle (rainbow), 2 = breathing
        public static int KbdColorEffect = 0;

        // How fast the animated effect runs, 1 (slowest) to 5 (fastest);
        // 3 is the rate the effects were fixed at before it was adjustable
        public static int KbdEffectSpeed = 3;

        // Switch the keyboard backlight off after this many minutes without
        // any keyboard or mouse input (and back on upon activity); 0 disables
        public static int KbdIdleOffMinutes = 0;

        // Keyboard backlight color-zone count override: 1 or 4 forces the
        // value, anything else auto-detects from the BIOS color table (which
        // some single-zone units falsely report as having four zones)
        public static int KbdZoneCount = 0;

        // Custom action for the Omen key handler
        public static bool KeyCustomActionEnabled = false;
        public static string KeyCustomActionExecCmd = "";
        public static string KeyCustomActionExecArgs = "";
        public static bool KeyCustomActionMinimized = false;

        // Use the Omen key to control fan program
        // (as long as KeyCustomAction is set to false)
        public static bool KeyToggleFanProgram = false;

        // If true, Omen key cycles through all fan programs,
        // instead of toggling the default fan program on and off
        public static bool KeyToggleFanProgramCycleAll = true;

        // Show window first Omen key press (if not shown already),
        // before using subsequent keypresses to control fan program
        public static bool KeyToggleFanProgramShowGuiFirst = true;

        // Do not show a balloon tip notification when changing programs
        public static bool KeyToggleFanProgramSilent = false;

        // Interface language: "Auto" follows the system setting, otherwise the
        // name of one of the languages the locale system knows about
        public static string Language = "Auto";

        // Whether every Embedded Controller and BIOS exchange is written to the
        // log. Off by default: the traffic is voluminous and only of interest
        // when actually diagnosing a hardware problem.
        public static bool LogVerbose = false;

        // Whether the log is also written to a file next to the executable
        public static bool LogToFile = false;

        // Size at which the log file is rolled over to a single backup [bytes]
        public static int LogFileMaxBytes = 4 * 1024 * 1024;

        // Log file extension, appended to the executable's own path
        public const string LogFileExt = ".log";

        // Localizable string prefixes and suffixes
        public const string L_CLI = "Cli";
        public const string L_CLI_BIOS = "CliBios";
        public const string L_CLI_EC = "CliEc";
        public const string L_CLI_PROG = "CliProg";
        public const string L_CLI_TASK = "CliTask";
        public const string L_DATATYPE_NAME = "DataType";
        public const string L_DATATYPE_SYNTAX = "DataSyntax";
        public const string L_GUI = "Gui";
        public const string L_GUI_ABOUT = "GuiAbout";
        public const string L_GUI_MAIN = "GuiMain";
        public const string L_GUI_MENU = "GuiMenu";
        public const string L_GUI_TIP = "GuiTip";
        public const string L_PROG = "Prog";
        public const string L_UNIT = "Unit";
        public const string LS_CUSTOM_FONT = "_CustomFont"; // Suffix

        // Exclusivity lock names or paths
        public static string LockNameMux = AppName + "-Mux";
        public const string LockPathEc = "Global\\Access_EC"; // Commonly-observed value
        public const string LockPathCli = "Global\\StarMonCli";
        public const string LockPathGui = "Global\\StarMonGui";

        // Maximum believable speed percent over maximum value when reading from the Embedded Controller (used for fan speed)
        public const int MaxBelievableFanSpeedPercentOverMax = 10;

        // Maximum believable percent value when reading from the Embedded Controller (used for fan rate)
        public const int MaxBelievablePercent = 100;

        // Maximum believable temperature value when reading from the Embedded Controller
        public const int MaxBelievableTemperature = 99;

        // nVidia Display Container service name
        public const string NvDisplayContainerService = "NVDisplay.ContainerLocalSystem";

        // Location for temporary files (must be declared before OnlyOncePath)
        public static string PathTemp = Environment.GetEnvironmentVariable("TEMP");

        // Parameters for the persistent state until reboot flag implementation
        public static string OnlyOnceFileExt = ".txt";
        public static string OnlyOncePath = PathTemp;

        // Display refresh rate values [Hz]. Replaced at startup by the rates
        // the panel actually offers, unless auto-detection is switched off.
        public static bool RefreshRateAutoDetect = true;
        public static int PresetRefreshRateHigh = 144;
        public static int PresetRefreshRateLow = 60;

        // Whether to drop the display to the low refresh rate on battery and
        // put it back on AC. A high refresh rate is one of the larger fixed
        // drains on a laptop, and the switch is otherwise easy to forget.
        public static bool RefreshRateFollowPower = false;

        // Registry hive prefix
        public const string RegHiveMachine = "HKEY_LOCAL_MACHINE";

        // nVidia Advanced Optimus multiplexer status registry location
        public const string RegMuxKey = "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm\\Global\\NvHybrid\\Persistence\\ACE";
        public const string RegMuxValue = "InternalMuxState";

        // Default shell executable registry location
        public const string RegShellKey = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon";
        public const string RegShellValue = "Shell";

        // The system-information panel was a rich-text box in the Windows
        // Forms interface, and the header, footer and colour table above built
        // its RTF by hand. That panel is a data-bound list now, so all three
        // described a document format nothing produces any more.

        // Folder where scheduled tasks are stored
        public const string TaskFolder = "\\";
        public static string TaskRunPath = Environment.GetEnvironmentVariable(EnvVarSysRoot) + "\\System32\\schtasks.exe";
        public const string TaskRunArgs = "/run /tn ";

        // Structure to hold temperature sensor information
        public struct TemperatureSensorData
        {

            // Resolved numerical value
            // to be passed to the source
            public byte Register;

            // Where the data originates from
            public PlatformData.LinkType Source;

            // Whether the sensor is used
            // or only being displayed
            public bool Use;

            // Constructor with all parameters
            public TemperatureSensorData(
                PlatformData.LinkType source,
                byte register = 0,
                bool use = true)
            {

                // Do not accept empty register values
                // if the source is the Embedded Controller
                if (source == PlatformData.LinkType.EmbeddedController
                    && register == 0)

                    // Throw an exception if that is the case
                    throw new ArgumentOutOfRangeException();

                // Set the structure data
                this.Source = source;
                this.Register = register;
                this.Use = use;

            }

            // Constructor with no register
            public TemperatureSensorData(
                PlatformData.LinkType source,
                bool use = true) : this(source, 0, use) { }

        }

        // Temperature sensors (overriden at runtime if found in the configuration file)
        public static Dictionary<string, TemperatureSensorData> TemperatureSensor =
            new Dictionary<string, TemperatureSensorData>
            {

                // CPU temperature
                ["CPUT"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.CPUT),

                // GPU temperature
                ["GPTM"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.GPTM),

                // Temperature reported by the BIOS
                // (values more or less a third lower than other readings,
                // thus currently makes no sense to use for maximum check)
                ["BIOS"] = new TemperatureSensorData(
                    PlatformData.LinkType.WmiBios, false),

                // Platform Controller Hub temperature
                ["RTMP"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.RTMP),

                // Memory temperature
                ["TMP1"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.TMP1),

                // The four auxiliary probes, shown but not acted on.
                //
                // These are the spare thermistor channels of the board this
                // register map came from. What sits on them — if anything —
                // differs from board to board, and on a board where a channel
                // is unconnected or wired to something else the register still
                // reads: a plausible number, in the plausible range, that
                // never moves.
                //
                // The hottest reading of every used sensor is what drives the
                // fan curve and the thermal guard, so one channel stuck at a
                // high figure pins the fans there and nothing in the reading
                // says why. That is the single most reported failure against
                // this application's upstream, and the reports are consistent:
                // a probe reading 84 C for ever, or 67, on a machine that is
                // idle and cool.
                //
                // Neither the dormancy mechanism nor the plausibility ceiling
                // catches it, and neither should: the register answers, and
                // the answer is a believable temperature. It is simply not a
                // temperature *of anything this application can name*.
                //
                // So they are read and shown — a probe reading is worth seeing,
                // and on the boards where these are real they are the earliest
                // warning there is — and they are kept out of the decision.
                // Use="true" in the configuration file puts one back, for a
                // board where it has been checked against a known load.
                ["TNT2"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.TNT2, false),

                ["TNT3"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.TNT3, false),

                ["TNT4"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.TNT4, false),

                ["TNT5"] = new TemperatureSensorData(
                    PlatformData.LinkType.EmbeddedController,
                    (byte)EmbeddedControllerData.Register.TNT5, false)
            };

        // Maximum number of temperature sensors
        public const int TemperatureSensorMax = 9;

        // Timestamp format in fan program status messages
        public const string TimestampFormat = "HH:mm:ss";

        // Scheduled task identifiers
        public enum TaskId
        {
            Gui,  // Autorun GUI on Windows startup
            Key,  // Omen key capture task
            Mux   // nVidia Advanced Optimus bug fix task
        }

        // Scheduled task data
        public static Dictionary<TaskId, string[]> Task =
            new Dictionary<TaskId, string[]>()
            {
                [TaskId.Gui] = new string[] { AppName, "-Run Gui" },
                [TaskId.Key] = new string[] { AppName + " Key", "-Run Key", "root\\wmi", "SELECT * FROM hpqBEvnt WHERE eventData = 8613 AND eventId = 29" },
                [TaskId.Mux] = new string[] { AppName + " Mux", "-Run Mux", "root\\default", "SELECT * FROM RegistryValueChangeEvent WHERE Hive = \"" + RegHiveMachine + "\" AND KeyPath = \"" + RegMuxKey + "\" AND ValueName = \"" + RegMuxValue + "\"" }
            };


        // How often the dynamic notification icon is updated (in ticks)
        public static int UpdateIconInterval = 3;

        // How often the monitoring data on the main form is updated (in ticks)
        public static int UpdateMonitorInterval = 3;

        // How often the history graph keeps recording while the window is hidden
        // in the tray (in ticks). Much rarer than the visible update to keep the
        // background cost negligible while still accumulating a rolling history.
        public static int UpdateRecordInterval = 30;

        // How often the program settings are updated (in ticks)
        public static int UpdateProgramInterval = 15;

        // Temperature cache duration to avoid redundant EC/WMI reads [ms]
        // Set to 0 to disable caching
        public static int TemperatureCacheMs = 250;

        // Wait duration when stopping a process (Explorer shell) or a service (nVidia Container)
        public const int WaitToStopProcess = 1000;
        public const int WaitToStopService = 500;

        // How long to keep waiting for a service to stop before giving up.
        // A driver stuck in STOP_PENDING would otherwise hold the headless
        // task process open for the rest of the session. [ms]
        public const int WaitToStopServiceTimeout = 30000;

        // WMI event settings
        public const string WmiEventSuffixConsumer = "Consumer";
        public const string WmiEventSuffixFilter = "Filter";
        public const string WmiQueryLang = "WQL";

        // Configuration XML elements and attributes
        private const string XmlElementColorPresets = "ColorPresets";
        private const string XmlElementColorPreset = "Preset";
        private const string XmlElementFanPrograms = "FanPrograms";
        private const string XmlElementFanProgram = "Program";
        private const string XmlElementFanProgramMode = "FanMode";
        private const string XmlElementFanProgramPower = "GpuPower";
        private const string XmlElementFanProgramLevel = "Level";
        private const string XmlElementFanProgramLevelCpu = "Cpu";
        private const string XmlElementFanProgramLevelGpu = "Gpu";
        private const string XmlElementTemperature = "Temperature";
        private const string XmlElementTemperatureSensor = "Sensor";
        private const string XmlAttrColorPresetName = "Name";
        private const string XmlAttrFanProgramName = "Name";
        private const string XmlAttrFanProgramLevelTemperature = "Temperature";
        private const string XmlAttrTemperatureSensorName = "Name";
        private const string XmlAttrTemperatureSensorSource = "Source";
        private const string XmlAttrTemperatureSensorSourceValueBios = "BIOS";
        private const string XmlAttrTemperatureSensorSourceValueEc = "EC";
        private const string XmlAttrTemperatureSensorUse = "Use";
        private const string XmlElementConfig = "Config";
        private const string XmlElementKeyCustomAction = "KeyCustomAction";

        // Configuration XML node prefixes
        private static string XmlPrefix = AppName + "/" + XmlElementConfig + "/"; // Must end with a slash
        private static string XmlPrefixColorPresets = XmlPrefix + XmlElementColorPresets + "/"; // Slash
        private static string XmlPrefixColorPreset = XmlPrefixColorPresets + XmlElementColorPreset; // No slash
        private static string XmlPrefixFanPrograms = XmlPrefix + XmlElementFanPrograms + "/"; // Slash
        private static string XmlPrefixFanProgram = XmlPrefixFanPrograms + XmlElementFanProgram; // No slash
        private static string XmlPrefixKeyCustomAction = XmlPrefix + XmlElementKeyCustomAction + "/"; // Slash
        private static string XmlPrefixTemperature = XmlPrefix + XmlElementTemperature + "/"; // Slash
        private static string XmlPrefixTemperatureSensor = XmlPrefixTemperature + XmlElementTemperatureSensor; // No slash

        // Whether to skip the annoying Byte Order Mark (BOM) when saving the XML configuration
        private const bool XmlSaveBom = false;

        // Strings representing Boolean flags used when saving to XML
        // Note: must be one of the values from Library.Conv.GetBool()
        private const string XmlSaveBoolFalse = "false";
        private const string XmlSaveBoolTrue = "true";
        private const string XmlSaveIndent = "    ";

        // Suffixes for the half-written file a save streams into before it is
        // renamed into place, and for the copy of the previous file the
        // rename leaves behind. Load() reads the second one when the
        // configuration itself will not parse.
        internal const string XmlSaveTempExt = ".saving";
        internal const string XmlSaveBackupExt = ".bak";

        // Template XML configuration file (rudimentary, a better version replaces this when locale is loaded)
        private static string XmlTemplate = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + "<StarMon/>";

    }

}
