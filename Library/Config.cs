// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Hardware.Platform;
using StarMon.Library.Locale;

namespace StarMon.Library {

    // Implements application-wide configuration settings look-up
    // This part only contains the implementing methods
    public static partial class Config {

#region Initialization
        // State flag
        public static bool IsInitialized { get; private set; }

        // Localization strings interface
        public static ILocale Locale;

        // Initializes the configuration class
        public static void Initialize() {

            // Only do it once
            if(!IsInitialized) {

                // Set the configuration file location
                // Note: also used by locale, so must happen before
                try {

                    // Establish where to look for the XML configuration file
                    FilePath = Path.ChangeExtension(AppFile, ".xml");

                } catch { }

                // Initialize the message system.
                // English is used until the configuration has been read, since
                // the language is itself one of the settings stored in it
                if(!LocaleInit("Override"))
                    App.Exit(Config.ExitStatus.ErrorLocale);

                // Load the configuration
                Load();

                // Switch to the configured language, now that it is known
                Locale.SetLanguage(ResolveLanguage());

                // Start writing the log to a file, if asked to
                if(LogToFile)
                    Logger.EnableFileLogging(
                        LogFilePath);

                // Done
                IsInitialized = true;

            }

        }

        // Initializes the locale system
        // Raised after the language has changed.
        //
        // A hook rather than a direct call, because Library must not depend on
        // the interface: this is the layer the command line uses too, and it
        // has no bindings to tell.
        public static event Action LocaleChangedHandler;

        private static void LocaleChanged() {
            Action handler = LocaleChangedHandler;
            if(handler != null)
                try { handler(); } catch { }
        }

        public static bool LocaleInit() {

            // Instantiate the localization system
            if((Locale = StarMon.Library.Locale.Locale.Instance) == null) {

                // Show an error if failed
                App.Error("ErrLocaleNull");
                return false;

            }

            return true;

        }

        // Initializes the locale system and sets the language
        public static bool LocaleInit(string language) {

            // Instantiate the locale
            if(!LocaleInit())
                return false;

            // Set the application language
            Locale.SetLanguage(language);

            // Tell every binding in the interface its string may have moved.
            // This is what lets the language change redraw the window instead
            // of the window being closed and built again, which is what the
            // Windows Forms build had to do.
            LocaleChanged();

            return true;

        }

        // Turns the configured language name into the locale slot to use.
        //
        // "Auto" follows the system's user-interface culture. English maps to
        // the Override slot rather than to Fallback, so that a translation
        // supplied through the configuration file's Messages section keeps
        // taking effect; every other language has its own built-in slot.
        public static LocaleData.Language ResolveLanguage() {

            string name = (Language ?? "").Trim();

            if(string.Equals(name, "Auto", StringComparison.OrdinalIgnoreCase))
                name = System.Globalization.CultureInfo.CurrentUICulture
                    .TwoLetterISOLanguageName == "tr" ? "Turkish" : "English";

            if(string.Equals(name, "English", StringComparison.OrdinalIgnoreCase))
                return LocaleData.Language.Override;

            try {
                return (LocaleData.Language) Enum.Parse(
                    typeof(LocaleData.Language), name, true);
            } catch {
                // An unrecognized name is not worth failing over
                return LocaleData.Language.Override;
            }

        }

        // The language names that can be selected in the interface
        public static string[] LanguageNames = new string[] {
            "Auto", "English", "Turkish" };
#endregion

#region Configuration Retrieval
        // Retrieves a Boolean flag value from the XML configuration file
        private static bool GetBool(XmlDocument xml, string node, out bool value) {
            value = false;
            try {
                if(Conv.GetBool(xml.SelectSingleNode(node).InnerText, out value))
                    return true;
            } catch {  }
            return false;

        }

        // Retrieves a string value from the XML configuration file
        private static string GetString(XmlDocument xml, string node) {
            string value = "";
            try {
                value = xml.SelectSingleNode(node).InnerText;
            } catch {  }
            return (value == null ? "" : value);
        }

        // Retrieves an unsigned word-sized value from the XML configuration file
        private static bool GetWord(XmlDocument xml, string node, out ushort value) {
            value = 0;
            try {
                if(Conv.GetWord(xml.SelectSingleNode(node).InnerText, out value))
                    return true;
            } catch {  }
            return false;

        }

        // Retrieves a value too large for a word from the XML configuration.
        //
        // LogFileMaxBytes is why this exists. It is written in kilobytes with
        // SetUInt and was read back with GetWord, so the slider's own maximum
        // of 64 MB - 65536 kilobytes - was one past what a ushort holds. The
        // parse threw, GetWord swallowed it and returned false, and the
        // setting silently reverted to the compiled 4 MB on the next launch.
        // Saved correctly, discarded on load, with nothing in the log: the
        // top of the slider was a position that would not stick.
        private static bool GetUInt(XmlDocument xml, string node, out uint value) {

            value = 0;

            try {

                XmlNode found = xml.SelectSingleNode(node);
                if(found == null)
                    return false;

                return uint.TryParse(found.InnerText.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value);

            } catch { }

            return false;

        }

        // Loads the configuration data from the XML file
        public static void Load() {

            // Proceed only if the file exists
            if(FilePath != "" && File.Exists(FilePath)) {

                try {

                    // Load the file, falling back to the copy the last
                    // successful save left behind. A configuration that will
                    // not parse is otherwise indistinguishable from no
                    // configuration at all, and the run that follows silently
                    // replaces every setting with a compiled-in default.
                    XmlDocument xml = new XmlDocument();

                    try {

                        xml.Load(FilePath);

                    } catch(Exception damaged) {

                        string backup = FilePath + XmlSaveBackupExt;

                        if(!File.Exists(backup))
                            throw;

                        xml.Load(backup);

                        Logger.Warning("Config",
                            "The configuration file would not parse; "
                                + "the previous copy was read instead",
                            damaged.Message);

                    }

                    // Replace the hard-coded XML template with a localized one
                    // Only possible once the localizable message class is instantiated
                    XmlTemplate = Config.Locale.Get("_ConfigXmlTemplate");

                    // Read the configuration and parse it into values
                    bool flag;
                    ushort value;

                    if(GetBool(xml, XmlPrefix + "AutoConfig", out flag))
                        AutoConfig = flag;

                    if(GetBool(xml, XmlPrefix + "AutoStartup", out flag))
                        AutoStartup = flag;

                    if(GetBool(xml, XmlPrefix + "BiosErrorReporting", out flag))
                        BiosErrorReporting = flag;

                    if(GetWord(xml, XmlPrefix + "EcFailLimit", out value))
                        EcFailLimit = value;

                    if(GetWord(xml, XmlPrefix + "EcMonInterval", out value))
                        EcMonInterval = value;

                    if(GetWord(xml, XmlPrefix + "EcMutexTimeout", out value))
                        EcMutexTimeout = value;

                    if(GetWord(xml, XmlPrefix + "EcRetryLimit", out value))
                        EcRetryLimit = value;

                    if(GetWord(xml, XmlPrefix + "EcWaitLimit", out value))
                        EcWaitLimit = value;

                    if(GetWord(xml, XmlPrefix + "EcWaitTimeoutMs", out value))
                        EcWaitTimeoutMs = value;

                    if(GetBool(xml, XmlPrefix + "FanCountdownExtendAlways", out flag))
                        FanCountdownExtendAlways = flag;

                    if(GetWord(xml, XmlPrefix + "FanCountdownExtendInterval", out value))
                        FanCountdownExtendInterval = value;

                    if(GetWord(xml, XmlPrefix + "FanCountdownExtendThreshold", out value))
                        FanCountdownExtendThreshold = value;

                    if(GetBool(xml, XmlPrefix + "FanLevelAutoDetect", out flag))
                        FanLevelAutoDetect = flag;

                    if(GetBool(xml, XmlPrefix + "HardwareGateOverride", out flag))
                        HardwareGateOverride = flag;

                    if(GetWord(xml, XmlPrefix + "FanLevelMax", out value))
                        FanLevelMax = value;

                    if(GetWord(xml, XmlPrefix + "FanLevelMin", out value))
                        FanLevelMin = value;

                    if(GetBool(xml, XmlPrefix + "FanLevelNeedManual", out flag))
                        FanLevelNeedManual = flag;

                    if(GetBool(xml, XmlPrefix + "FanLevelUseEc", out flag))
                        FanLevelUseEc = flag;

                    FanProgramDefault =
                        GetString(xml, XmlPrefix + "FanProgramDefault");

                    FanProgramDefaultAlt =
                        GetString(xml, XmlPrefix + "FanProgramDefaultAlt");

                    if(GetWord(xml, XmlPrefix + "FanProgramHysteresisC", out value))
                        FanProgramHysteresisC = value;

                    if(GetBool(xml, XmlPrefix + "FanProgramModeCheckFirst", out flag))
                        FanProgramModeCheckFirst = flag;

                    if(GetBool(xml, XmlPrefix + "FanProgramSuspend", out flag))
                        FanProgramSuspend = flag;

                    if(GetWord(xml, XmlPrefix + "FanModeKeepAliveMs", out value))
                        FanModeKeepAliveMs = value;

                    if(GetBool(xml, XmlPrefix + "ThermalProtectionEnabled", out flag))
                        ThermalProtectionEnabled = flag;

                    if(GetWord(xml, XmlPrefix + "ThermalProtectionHighC", out value))
                        ThermalProtectionHighC = value;

                    if(GetWord(xml, XmlPrefix + "ThermalProtectionLowC", out value))
                        ThermalProtectionLowC = value;

                    // The two are a hysteresis band, and the file can put them
                    // in any order: both parse as plain numbers. With the
                    // release threshold at or above the engage one, the guard
                    // engages the moment the machine passes the lower figure
                    // and releases again on the very next reading, so the fans
                    // slam to maximum and back on every poll instead of
                    // holding. Both are put back to the compiled-in defaults
                    // rather than nudged, because a file this wrong is not
                    // saying anything worth half-honouring.
                    if(ThermalProtectionLowC >= ThermalProtectionHighC) {

                        Logger.Warning("Config",
                            "The thermal thresholds do not form a band; "
                                + "the defaults were used instead",
                            "high " + ThermalProtectionHighC + " °C, low "
                                + ThermalProtectionLowC + " °C");

                        ThermalProtectionHighC = ThermalProtectionHighDefaultC;
                        ThermalProtectionLowC = ThermalProtectionLowDefaultC;

                    }

                    if(GetBool(xml, XmlPrefix + "ThrottleNotifyEnabled", out flag))
                        ThrottleNotifyEnabled = flag;

                    if(GetBool(xml, XmlPrefix + "GpuPollOnBattery", out flag))
                        GpuPollOnBattery = flag;

                    if(GetBool(xml, XmlPrefix + "RefreshRateFollowPower", out flag))
                        RefreshRateFollowPower = flag;

                    if(GetWord(xml, XmlPrefix + "DisplayOffHotkeyMods", out value))
                        DisplayOffHotkeyMods = value;

                    if(GetWord(xml, XmlPrefix + "DisplayOffHotkeyKey", out value))
                        DisplayOffHotkeyKey = value;

                    GpuPowerDefault =
                        GetString(xml, XmlPrefix + "GpuPowerDefault");

                    if(GetWord(xml, XmlPrefix + "GpuPowerSetInterval", out value))
                        GpuPowerSetInterval = value;

                    if(GetBool(xml, XmlPrefix + "GuiCloseWindowExit", out flag))
                        GuiCloseWindowExit = flag;

                    if(GetBool(xml, XmlPrefix + "GuiDynamicIcon", out flag))
                        GuiDynamicIcon = flag;

                    if(GetBool(xml, XmlPrefix + "GuiDynamicIconHasBackground", out flag))
                        GuiDynamicIconHasBackground = flag;

                    if(GetBool(xml, XmlPrefix + "GuiStayOnTop", out flag))
                        GuiStayOnTop = flag;

                    if(GetWord(xml, XmlPrefix + "GuiTipDuration", out value))
                        GuiTipDuration = value;

                    // The language is read before anything else that produces
                    // a user-visible string, so nothing is formatted twice
                    string language = GetString(xml, XmlPrefix + "Language");
                    if(language != "")
                        Language = language;

                    if(GetBool(xml, XmlPrefix + "LogVerbose", out flag))
                        LogVerbose = flag;

                    if(GetBool(xml, XmlPrefix + "LogToFile", out flag))
                        LogToFile = flag;

                    // Stored in kilobytes, and read as something wider than a
                    // word: the slider's own maximum does not fit in one
                    uint kilobytes;
                    if(GetUInt(xml, XmlPrefix + "LogFileMaxBytes", out kilobytes)
                        && kilobytes > 0 && kilobytes <= LogFileMaxKilobytes)
                        LogFileMaxBytes = (int) (kilobytes * 1024);

                    if(GetBool(xml, XmlPrefix + "KbdColorByTemp", out flag))
                        KbdColorByTemp = flag;

                    if(GetWord(xml, XmlPrefix + "KbdColorEffect", out value))
                        KbdColorEffect = value;

                    if(GetWord(xml, XmlPrefix + "KbdEffectSpeed", out value))
                        KbdEffectSpeed = value < 1 ? 1 : value > 5 ? 5 : value;

                    if(GetWord(xml, XmlPrefix + "KbdIdleOffMinutes", out value))
                        KbdIdleOffMinutes = value;

                    if(GetWord(xml, XmlPrefix + "KbdZoneCount", out value))
                        KbdZoneCount = value;

                    if(GetBool(xml, XmlPrefix + "KeyToggleFanProgram", out flag))
                        KeyToggleFanProgram = flag;

                    if(GetBool(xml, XmlPrefix + "KeyToggleFanProgramCycleAll", out flag))
                        KeyToggleFanProgramCycleAll = flag;

                    if(GetBool(xml, XmlPrefix + "KeyToggleFanProgramShowGuiFirst", out flag))
                        KeyToggleFanProgramShowGuiFirst = flag;

                    if(GetBool(xml, XmlPrefix + "KeyToggleFanProgramSilent", out flag))
                        KeyToggleFanProgramSilent = flag;

                    if(GetBool(xml, XmlPrefix + "RefreshRateAutoDetect", out flag))
                        RefreshRateAutoDetect = flag;

                    if(GetWord(xml, XmlPrefix + "PresetRefreshRateHigh", out value))
                        PresetRefreshRateHigh = value;

                    if(GetWord(xml, XmlPrefix + "PresetRefreshRateLow", out value))
                        PresetRefreshRateLow = value;

                    if(GetWord(xml, XmlPrefix + "UpdateIconInterval", out value))
                        UpdateIconInterval = value;

                    if(GetWord(xml, XmlPrefix + "UpdateMonitorInterval", out value))
                        UpdateMonitorInterval = value;

                    if(GetWord(xml, XmlPrefix + "UpdateProgramInterval", out value))
                        UpdateProgramInterval = value;

                    // The cadence with the window hidden. Settable from the
                    // interface since it was written, and read from nowhere:
                    // a changed value worked for the rest of the session and
                    // was back to thirty seconds on the next launch.
                    if(GetWord(xml, XmlPrefix + "UpdateRecordInterval", out value))
                        UpdateRecordInterval = value;

                    if(GetWord(xml, XmlPrefix + "TemperatureCacheMs", out value))
                        TemperatureCacheMs = value;

                    // Load the key custom action settings
                    if(GetBool(xml, XmlPrefixKeyCustomAction + "Enabled", out flag))
                        KeyCustomActionEnabled = flag;

                    KeyCustomActionExecCmd =
                        GetString(xml, XmlPrefixKeyCustomAction + "ExecCmd");

                    KeyCustomActionExecArgs =
                        GetString(xml, XmlPrefixKeyCustomAction + "ExecArgs");

                    if(GetBool(xml, XmlPrefixKeyCustomAction + "Minimized", out flag))
                        KeyCustomActionMinimized = flag;

                    // Load the color presets
                    SortedDictionary<string, BiosData.ColorTable> ColorPresetXml
                        = new SortedDictionary<string, BiosData.ColorTable>();
                    foreach(XmlNode node in xml.SelectNodes(XmlPrefixColorPreset)) {
                        // Invalid entries will be discarded at this step
                        try {
                            BiosData.ColorTable colorTable = new BiosData.ColorTable(node.InnerText);
                            ColorPresetXml[node.Attributes[XmlAttrColorPresetName].Value] = colorTable;
                        } catch { }
                    }

                    // Replace the defaults with configured color presets unless none
                    if(ColorPresetXml.Count > 0)
                        ColorPreset = ColorPresetXml;

                    // Load the temperature sensors
                    bool usable = false;
                    Dictionary<string, TemperatureSensorData> TemperatureSensorXml
                        = new Dictionary<string, TemperatureSensorData>();
                    foreach(XmlNode node in xml.SelectNodes(XmlPrefixTemperatureSensor)) {
                        // Invalid entries will be discarded at this step
                        try {

                            // Abort if more than the maximum number of sensors defined already
                            if(TemperatureSensorXml.Count >= TemperatureSensorMax)
                                break;

                            // Set the optional use flag
                            // based on the XML attribute
                            bool use = true;
                            try {
                                Conv.GetBool(node.Attributes[XmlAttrTemperatureSensorUse].Value, out use);
                            } catch {  }

                            // Check for Embedded Controller sensor source
                            if(node.Attributes[XmlAttrTemperatureSensorSource].Value
                                == XmlAttrTemperatureSensorSourceValueEc)

                                // Adding a sensor sourced from the Embedded Controller
                                TemperatureSensorXml[node.Attributes[XmlAttrTemperatureSensorName].Value] =
                                    new TemperatureSensorData(
                                        PlatformData.LinkType.EmbeddedController,
                                        (byte) Enum.Parse(typeof(EmbeddedControllerData.Register),
                                            node.Attributes[XmlAttrTemperatureSensorName].Value), use);

                            // Check for WMI BIOS sensor source
                            else if(node.Attributes[XmlAttrTemperatureSensorSource].Value
                                == XmlAttrTemperatureSensorSourceValueBios)

                                // Adding a sensor sourced from the WMI BIOS
                                TemperatureSensorXml[XmlAttrTemperatureSensorSourceValueBios] =
                                    new TemperatureSensorData(PlatformData.LinkType.WmiBios, use);

                            // Throw an exception for any unknown sources
                            else throw new ArgumentOutOfRangeException();

                            // Record found usable
                            if(use) usable = true;

                        } catch { }

                    }

                    // Replace the defaults with configured temperature sensors unless none
                    // were configured or not a single sensor was set to actually be used
                    if(TemperatureSensorXml.Count > 0 && usable)
                        TemperatureSensor = TemperatureSensorXml;

                    // Load the fan programs
                    foreach(XmlNode node in xml.SelectNodes(XmlPrefixFanProgram)) {
                        // Invalid entries will be discarded at this step
                        try {

                            // Set up a variable to read the level configuration into
                            SortedDictionary<byte, byte[]> levels =
                                new SortedDictionary<byte, byte[]>();

                            // Iterate through the levels specified in the XML file
                            foreach(XmlNode subnode in node.SelectNodes(XmlElementFanProgramLevel)) {

                                // Populate the level data
                                levels[Conv.GetByte(
                                    subnode.Attributes[XmlAttrFanProgramLevelTemperature].Value)] =
                                        new byte[] {
                                            Conv.GetByte(subnode[XmlElementFanProgramLevelCpu].InnerText),
                                            Conv.GetByte(subnode[XmlElementFanProgramLevelGpu].InnerText)};

                            }

                            // Create a new fan program from the configuration data
                            FanProgram[node.Attributes[XmlAttrFanProgramName].Value] =
                                new FanProgramData(
                                    node.Attributes[XmlAttrFanProgramName].Value,
                                    (BiosData.FanMode) Enum.Parse(typeof(BiosData.FanMode), node[XmlElementFanProgramMode].InnerText),
                                    (BiosData.GpuPowerLevel) Enum.Parse(typeof(BiosData.GpuPowerLevel), node[XmlElementFanProgramPower].InnerText),
                                    levels);

                        } catch { }

                    }

                } catch(Exception e) {

                    // Carry on with default values, but leave a trace: the user
                    // otherwise has no way to tell why their settings were ignored
                    Logger.Error("Config", "Loading the configuration file failed, using defaults", e.Message);

                }

            }

        }
#endregion

#region Configuration Saving
        // Save the configuration data to the XML file
        public static void Save() {

            // Proceed only if the filename is not empty
            if(FilePath != "") {

                try {

                    // Create a new XML document
                    XmlDocument xml = new XmlDocument();

                    try {

                        // Try to load the existing configuration file
                        xml.Load(FilePath);

                    } catch {

                        // Otherwise, start with a pre-defined template
                        // and do not preserve the formatting
                        xml.LoadXml(Config.XmlTemplate);

                    }

                    // Create or update the configuration values
                    SetBool(xml, XmlPrefix + "AutoConfig", AutoConfig);
                    SetBool(xml, XmlPrefix + "AutoStartup", AutoStartup);
                    SetBool(xml, XmlPrefix + "BiosErrorReporting", BiosErrorReporting);

                    // Color presets (so that the settings are sorted alphabetically)
                    // Ensure the parent element node exists, or create it
                    XmlElement xmlColor = (XmlElement) SetPath(xml, XmlPrefixColorPresets);

                    // Remove all currently-defined presets
                    // (the user might have already deleted some of them)
                    xmlColor.RemoveAll();

                    // Iterate through the color presets
                    foreach(string name in ColorPreset.Keys) {

                        // Create an element for each preset
                        XmlElement node = (XmlElement) xmlColor.AppendChild(
                                xml.CreateElement(XmlElementColorPreset));

                        // Store the preset name in an attribute
                        node.SetAttribute(XmlAttrColorPresetName, name);

                        // Store the preset parameter value as inner text
                        node.InnerText = (Conv.GetColorString((int) ColorPreset[name].Zone[(int) BiosData.KbdZone.Right].ValueReverse)
                            + ":" + Conv.GetColorString((int) ColorPreset[name].Zone[(int) BiosData.KbdZone.Middle].ValueReverse)
                            + ":" + Conv.GetColorString((int) ColorPreset[name].Zone[(int) BiosData.KbdZone.Left].ValueReverse)
                            + ":" + Conv.GetColorString((int) ColorPreset[name].Zone[(int) BiosData.KbdZone.Wasd].ValueReverse))
                                .ToUpperInvariant();

                    }

                    // Continue with the configuration values
                    SetUInt(xml, XmlPrefix + "EcFailLimit", (uint) EcFailLimit);
                    SetUInt(xml, XmlPrefix + "EcMonInterval", (uint) EcMonInterval);
                    SetUInt(xml, XmlPrefix + "EcMutexTimeout", (uint) EcMutexTimeout);
                    SetUInt(xml, XmlPrefix + "EcRetryLimit", (uint) EcRetryLimit);
                    SetUInt(xml, XmlPrefix + "EcWaitLimit", (uint) EcWaitLimit);
                    SetUInt(xml, XmlPrefix + "EcWaitTimeoutMs", (uint) EcWaitTimeoutMs);
                    SetBool(xml, XmlPrefix + "FanCountdownExtendAlways", FanCountdownExtendAlways);
                    SetUInt(xml, XmlPrefix + "FanCountdownExtendInterval", (uint) FanCountdownExtendInterval);
                    SetUInt(xml, XmlPrefix + "FanCountdownExtendThreshold", (uint) FanCountdownExtendThreshold);
                    SetBool(xml, XmlPrefix + "FanLevelAutoDetect", FanLevelAutoDetect);
                    SetBool(xml, XmlPrefix + "HardwareGateOverride", HardwareGateOverride);
                    SetUInt(xml, XmlPrefix + "FanLevelMax", (uint) FanLevelMax);
                    SetUInt(xml, XmlPrefix + "FanLevelMin", (uint) FanLevelMin);
                    SetBool(xml, XmlPrefix + "FanLevelNeedManual", FanLevelNeedManual);
                    SetBool(xml, XmlPrefix + "FanLevelUseEc", FanLevelUseEc);
                    SetString(xml, XmlPrefix + "FanProgramDefault", FanProgramDefault);
                    SetString(xml, XmlPrefix + "FanProgramDefaultAlt", FanProgramDefaultAlt);
                    SetUInt(xml, XmlPrefix + "FanProgramHysteresisC", (uint) FanProgramHysteresisC);
                    SetBool(xml, XmlPrefix + "FanProgramModeCheckFirst", FanProgramModeCheckFirst);
                    SetBool(xml, XmlPrefix + "FanProgramSuspend", FanProgramSuspend);
                    SetUInt(xml, XmlPrefix + "FanModeKeepAliveMs", (uint) FanModeKeepAliveMs);
                    SetBool(xml, XmlPrefix + "ThermalProtectionEnabled", ThermalProtectionEnabled);
                    SetUInt(xml, XmlPrefix + "ThermalProtectionHighC", (uint) ThermalProtectionHighC);
                    SetUInt(xml, XmlPrefix + "ThermalProtectionLowC", (uint) ThermalProtectionLowC);
                    SetBool(xml, XmlPrefix + "ThrottleNotifyEnabled", ThrottleNotifyEnabled);
                    SetBool(xml, XmlPrefix + "GpuPollOnBattery", GpuPollOnBattery);
                    SetBool(xml, XmlPrefix + "RefreshRateFollowPower", RefreshRateFollowPower);
                    SetUInt(xml, XmlPrefix + "DisplayOffHotkeyMods", (uint) DisplayOffHotkeyMods);
                    SetUInt(xml, XmlPrefix + "DisplayOffHotkeyKey", (uint) DisplayOffHotkeyKey);

                    // Fan programs (again, so that the settings are
                    // sorted alphabetically for the user's convenience)

                    // Ensure the parent element node exists, or create it
                    XmlElement xmlFan = (XmlElement) SetPath(xml, XmlPrefixFanPrograms);

                    // Remove all currently-defined presets
                    // (the user might have already deleted some of them)
                    xmlFan.RemoveAll();

                    // Iterate through the fan programs
                    foreach(string name in FanProgram.Keys) {

                        // Create an element for each program
                        XmlElement node = (XmlElement) xmlFan.AppendChild(
                                xml.CreateElement(XmlElementFanProgram));

                        // Store the program name in an attribute
                        node.SetAttribute(XmlAttrFanProgramName, name);

                        // Create an element to store the fan mode
                        node.AppendChild(xml.CreateElement(XmlElementFanProgramMode)).InnerText =
                            Enum.GetName(typeof(BiosData.FanMode), FanProgram[name].FanMode);

                        // Create an element to store the GPU power level
                        node.AppendChild(xml.CreateElement(XmlElementFanProgramPower)).InnerText =
                            Enum.GetName(typeof(BiosData.GpuPowerLevel), FanProgram[name].GpuPower);

                        // For each programmed fan level
                        foreach(byte temperature in FanProgram[name].Level.Keys) {

                            // Create an element to store the level data
                            XmlElement level = (XmlElement) node.AppendChild(xml.CreateElement(XmlElementFanProgramLevel));

                            // Store the temperature
                            level.SetAttribute(XmlAttrFanProgramLevelTemperature, Conv.GetString(temperature, 2, 10));

                            // Store the CPU fan level
                            level.AppendChild(xml.CreateElement(XmlElementFanProgramLevelCpu)).InnerText =
                                Conv.GetString(FanProgram[name].Level[temperature][0], 2, 10);

                            // Store the GPU fan level
                            level.AppendChild(xml.CreateElement(XmlElementFanProgramLevelGpu)).InnerText =
                                Conv.GetString(FanProgram[name].Level[temperature][1], 2, 10);

                        }

                    }

                    // Continue with the configuration values
                    SetString(xml, XmlPrefix + "GpuPowerDefault", GpuPowerDefault);
                    SetUInt(xml, XmlPrefix + "GpuPowerSetInterval", (uint) GpuPowerSetInterval);
                    SetBool(xml, XmlPrefix + "GuiCloseWindowExit", GuiCloseWindowExit);
                    SetBool(xml, XmlPrefix + "GuiDynamicIcon", GuiDynamicIcon);
                    SetBool(xml, XmlPrefix + "GuiDynamicIconHasBackground", GuiDynamicIconHasBackground);
                    SetBool(xml, XmlPrefix + "GuiStayOnTop", GuiStayOnTop);
                    SetUInt(xml, XmlPrefix + "GuiTipDuration", (uint) GuiTipDuration);
                    SetString(xml, XmlPrefix + "Language", Language);
                    SetBool(xml, XmlPrefix + "LogVerbose", LogVerbose);
                    SetBool(xml, XmlPrefix + "LogToFile", LogToFile);
                    SetUInt(xml, XmlPrefix + "LogFileMaxBytes", (uint) (LogFileMaxBytes / 1024));
                    SetBool(xml, XmlPrefix + "KbdColorByTemp", KbdColorByTemp);
                    SetUInt(xml, XmlPrefix + "KbdColorEffect", (uint) KbdColorEffect);
                    SetUInt(xml, XmlPrefix + "KbdEffectSpeed", (uint) KbdEffectSpeed);
                    SetUInt(xml, XmlPrefix + "KbdIdleOffMinutes", (uint) KbdIdleOffMinutes);
                    SetUInt(xml, XmlPrefix + "KbdZoneCount", (uint) KbdZoneCount);
                    SetBool(xml, XmlPrefixKeyCustomAction + "Enabled", KeyCustomActionEnabled);
                    SetString(xml, XmlPrefixKeyCustomAction + "ExecCmd", KeyCustomActionExecCmd);
                    SetString(xml, XmlPrefixKeyCustomAction + "ExecArgs", KeyCustomActionExecArgs);
                    SetBool(xml, XmlPrefixKeyCustomAction + "Minimized", KeyCustomActionMinimized);
                    SetBool(xml, XmlPrefix + "KeyToggleFanProgram", KeyToggleFanProgram);
                    SetBool(xml, XmlPrefix + "KeyToggleFanProgramCycleAll", KeyToggleFanProgramCycleAll);
                    SetBool(xml, XmlPrefix + "KeyToggleFanProgramShowGuiFirst", KeyToggleFanProgramShowGuiFirst);
                    SetBool(xml, XmlPrefix + "KeyToggleFanProgramSilent", KeyToggleFanProgramSilent);
                    SetBool(xml, XmlPrefix + "RefreshRateAutoDetect", RefreshRateAutoDetect);
                    SetUInt(xml, XmlPrefix + "PresetRefreshRateHigh", (uint) PresetRefreshRateHigh);
                    SetUInt(xml, XmlPrefix + "PresetRefreshRateLow", (uint) PresetRefreshRateLow);

                    // Temperature sensors (alphabetical order maintained)
                    // Ensure the parent element node exists, or create it
                    XmlElement xmlTemperature = (XmlElement) SetPath(xml, XmlPrefixTemperature);

                    // Remove all currently-defined sensor entries
                    xmlTemperature.RemoveAll();

                    // Iterate through the sensor entries
                    foreach(string name in TemperatureSensor.Keys) {

                        // Create an element for each sensor
                        XmlElement node = (XmlElement) xmlTemperature.AppendChild(
                                xml.CreateElement(XmlElementTemperatureSensor));

                        // Store the preset name and source in attributes
                        node.SetAttribute(XmlAttrTemperatureSensorName, name);
                        node.SetAttribute(XmlAttrTemperatureSensorSource,
                            TemperatureSensor[name].Source == PlatformData.LinkType.EmbeddedController ?
                                XmlAttrTemperatureSensorSourceValueEc : XmlAttrTemperatureSensorSourceValueBios);

                        if(!TemperatureSensor[name].Use)
                            node.SetAttribute(XmlAttrTemperatureSensorUse, XmlSaveBoolFalse);

                    }

                    // The remaining configuration values
                    SetUInt(xml, XmlPrefix + "UpdateIconInterval", (uint) UpdateIconInterval);
                    SetUInt(xml, XmlPrefix + "UpdateMonitorInterval", (uint) UpdateMonitorInterval);
                    SetUInt(xml, XmlPrefix + "UpdateProgramInterval", (uint) UpdateProgramInterval);
                    SetUInt(xml, XmlPrefix + "UpdateRecordInterval", (uint) UpdateRecordInterval);
                    SetUInt(xml, XmlPrefix + "TemperatureCacheMs", (uint) TemperatureCacheMs);

                    // Save the file.
                    //
                    // Written beside the destination and moved into place,
                    // never over the top of it. Creating the writer on
                    // FilePath truncates the file first, so a process that
                    // dies between that and the last byte — a crash, a forced
                    // reboot, a battery cutout, all of which a laptop utility
                    // should expect — left an empty or half-written document
                    // behind. Load() then failed to parse it, fell back to the
                    // compiled-in defaults with only a log line to say so, and
                    // the next save made the loss permanent: every fan
                    // program, threshold and preset gone. A rename is atomic,
                    // so the file on disk is either the old one or the new one.
                    XmlWriterSettings xmlWriterSettings = new XmlWriterSettings();
                    xmlWriterSettings.Encoding = new UTF8Encoding(XmlSaveBom);
                    xmlWriterSettings.Indent = true;
                    xmlWriterSettings.IndentChars = XmlSaveIndent;
                    xmlWriterSettings.NewLineHandling = NewLineHandling.Replace;

                    string temporary = FilePath + XmlSaveTempExt;

                    using(XmlWriter xmlWriter = XmlWriter.Create(temporary, xmlWriterSettings))
                        xml.Save(xmlWriter);

                    if(File.Exists(FilePath)) {

                        // Replace keeps a copy of what was there, which is the
                        // one thing that makes a bad save recoverable; it needs
                        // the destination to exist, hence the branch
                        File.Replace(temporary, FilePath, FilePath + XmlSaveBackupExt,
                            true);

                    } else {

                        File.Move(temporary, FilePath);

                    }

                } catch(Exception e) {

                    // Show an error message if the settings could not be saved.
                    //
                    // With the reason recorded, the way Load() already does.
                    // A save that fails is the one failure the user cannot
                    // investigate afterwards — the settings are simply back to
                    // where they were on the next launch — and the exception
                    // was being dropped on the floor, so "could not be saved"
                    // was the whole of what anyone ever learned.
                    Logger.Error("Config", "Saving the configuration file failed",
                        e.Message);

                    App.Error("ErrConfigSave");

                }

            }

        }

        // Sets a Boolean flag in the XML configuration file
        private static bool SetBool(XmlDocument xml, string node, bool value) {
            try {
                (SetPath(xml, node)).InnerText = value ? XmlSaveBoolTrue : XmlSaveBoolFalse;
                return true;
            } catch {  }
            return false;
        }

        // Ensures all intermediate nodes exist along an XML search path,
        // then returns the requested node as an object
        private static XmlNode SetPath(XmlDocument xml, XmlNode parent, string path) {
            XmlNode node;

            // Split the path into individual node names
            string[] nodes = path.Trim('/').Split('/');

            // If the next node name is empty, return the parent node
            if(string.IsNullOrEmpty(nodes[0]))
                return parent;

            // Create the node if it does not exist
            if((node = parent.SelectSingleNode(nodes[0])) == null)
                node = parent.AppendChild(xml.CreateElement(nodes[0]));

            // Recursively process the remaining nodes along the path
            return SetPath(xml, node,
                path.Length > nodes[0].Length ?
                    path.Substring(nodes[0].Length + 1) : "");

        }

        // Wrapper for SetPath() starting at document root
        private static XmlNode SetPath(XmlDocument xml, string path) {
            return SetPath(xml, (XmlNode) xml, path);
        }

        // Sets a string value in the XML configuration file
        private static bool SetString(XmlDocument xml, string node, string value) {
            try {
                (SetPath(xml, node)).InnerText = value;
                return true;
            } catch {  }
            return false;
        }

        // Sets an unsigned double word-sized value in the XML configuration file
        private static bool SetUInt(XmlDocument xml, string node, uint value, int padding = 1, int nbase = 10) {
            try {
                (SetPath(xml, node)).InnerText = Conv.GetString(value, padding, nbase);
                return true;
            } catch {  }
            return false;
        }
#endregion

#region Error Handling
        // Retrieves a concatenated error message
        public static string GetError(string messageIds, Exception e = null) {

            // A bit of a chicken and egg problem
            if(Config.Locale == null)
                return "Failed to instantiate the localizable message system";

            int messageCount = 0;
            string message = "";

            // Obtain a localization string for each message identifier
            foreach(string messageId in messageIds.Split('|')) {

                // If multiple messages, add a separator
                // between the first and the second only
                message += messageCount > 0 ? messageCount > 1 ? "" : ": " : "";

                // Append the next message
                // Exception message is a special case
                if(messageId == "EXCEPTION")
                    message += e != null ? e.Message : Config.Locale.Get("ErrUnexpectedReally");
                else
                    message += Config.Locale.Get(messageId);

                // Count the messages processed so far
                messageCount++;
            }

            return message;
        }
#endregion

    }

}
