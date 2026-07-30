// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Library;

namespace StarMon.Library.Locale
{

    // Defines locale constants, variables, and structures
    // for subsequent use by the localization routines
    public abstract partial class LocaleData
    {

        // Language list.
        //
        // Fallback holds the built-in English strings and is what every other
        // language falls back to for a key it does not define. Override is the
        // slot the XML configuration file loads into, so a user-supplied
        // translation keeps working; English therefore resolves to Override
        // rather than to Fallback directly.
        public enum Language : int
        {
            Fallback,  // Default fallback (built-in English)
            Override,  // Loaded from file (English plus any user overrides)
            Turkish    // Built-in Turkish, see LocaleDataTr.cs
        }

        // Default fallback message data
        protected Dictionary<string, string> msgFallback
            = new Dictionary<string, string>()
            {

                // CLI
                ["CliHeader"] = "Hardware Monitoring & Control Utility",
                ["CliHeaderVersion"] = "Version",
                ["CliActionGet"] = "-",
                ["CliActionSet"] = "+",
                ["CliDetailsFollow"] = Conv.GetChar(Conv.SpecialChar.ArrowDown),
                ["CliStateOn"] = "Yes",
                ["CliStateOff"] = "No",
                ["CliTranslated"] = "", // Only filled out for translations

                // CLI: BIOS
                ["CliBios"] = "BIOS",
                ["CliBiosAdapter"] = "Smart Power Adapter Status",
                ["CliBiosAnim"] = "LED Animation Table",
                ["CliBiosBacklight"] = "Keyboard Backlight",
                ["CliBiosBornDate"] = "Born-On Date",
                ["CliBiosBornDateNote"] = "YYYYMMDD",
                ["CliBiosColor"] = "Keyboard Backlight Color Table",
                ["CliBiosColorZones"] = "Zones",
                ["CliBiosCpuPowerLimit1"] = "CPU Power Limit 1",
                ["CliBiosCpuPowerLimit4"] = "CPU Power Limit 4",
                ["CliBiosCpuPowerLimitWithGpu"] = "CPU Power Limit Concurrent with GPU",
                ["CliBiosFanCount"] = "Fan Count",
                ["CliBiosFanLevelN"] = "Fan #{0} Level",
                ["CliBiosFanMax"] = "Maximum Fan Speed",
                ["CliBiosFanMode"] = "Fan Mode",
                ["CliBiosFanTable"] = "Fan Speed Level Table",
                ["CliBiosFanTableFans"] = "Fans",
                ["CliBiosFanTableLevels"] = "Levels",
                ["CliBiosFanType"] = "Fan Type",
                ["CliBiosFanTypeN"] = "Fan #{0} Type",
                ["CliBiosGpuMode"] = "Graphics Mode (Legacy)",
                ["CliBiosGpuPower"] = "GPU Power Settings",
                ["CliBiosGpuPowerCustomTgp"] = "GPU Custom Total Graphics Power (cTGP)",
                ["CliBiosGpuPowerDState"] = "GPU Device Power State (DState)",
                ["CliBiosGpuPowerPeakTemperature"] = "GPU Peak Temperature Sensor Threshold",
                ["CliBiosGpuPowerPpab"] = "GPU Processing Performance AI Boost (PPAB)",
                ["CliBiosHasBacklight"] = "Keyboard Backlight Support",
                ["CliBiosHasMemoryOverclock"] = "Memory Overclocking Support",
                ["CliBiosHasOverclock"] = "Overclocking Support",
                ["CliBiosHasUndervolt"] = "BIOS Undervolt Support",
                ["CliBiosIdle"] = "Idle Mode",
                ["CliBiosKbdType"] = "Keyboard Type",
                ["CliBiosSystem"] = "System Design Data",
                ["CliBiosSystemBiosOc"] = "BIOS-Defined Overclocking",
                ["CliBiosSystemDefaultCpuPowerLimit4"] = "Default CPU Power Limit 4",
                ["CliBiosSystemDefaultCpuPowerLimitWithGpu"] = "Default CPU Concurrent Power Limit w/GPU",
                ["CliBiosSystemDefaultCpuPowerLimitWithGpuNote"] = "Cybug 23C1 Onwards",
                ["CliBiosSystemGpuModeSwitch"] = "Graphics Mode Switching Support",
                ["CliBiosSystemStatusFlags"] = "Status Flags",
                ["CliBiosSystemSupportFlags"] = "Support Flags",
                ["CliBiosSystemThermalPolicyVersion"] = "Thermal Policy Version",
                ["CliBiosSystemUnknown2"] = "Unknown Byte",
                ["CliBiosSystemUnknown2Note"] = "Observed Constant 0x35 = 53",
                ["CliBiosTemp"] = "Temperature",
                ["CliBiosThrottling"] = "Thermal Throttling Status",
                ["CliBiosXmp"] = "Memory XMP Profile",

                // CLI: Embedded Controller
                ["CliEc"] = "Embedded Controller",
                ["CliEcMon"] = "Embedded Controller Monitor",
                ["CliEcByte"] = "Byte",
                ["CliEcRegister"] = "Register",
                ["CliEcWord"] = "Word",
                ["CliEcWordNote"] = "(Little-Endian)",

                // CLI: Program
                ["CliProg"] = "Program",
                ["CliProgCallback"] = "Callback",
                ["CliProgName"] = "Program",
                ["CliProgFanMode"] = "Fan Mode",
                ["CliProgGpuPower"] = "GPU Power",

                // CLI: Task
                ["CliTask"] = "Task Scheduling",
                ["CliTaskGui"] = "Autorun on User Logon",
                ["CliTaskKey"] = "Omen Key Interception",
                ["CliTaskMux"] = "Advanced Optimus Bug Fix",

                // CLI: Usage
                ["CliUsage"] = "Usage Information",
                ["CliUsageText"] =
                    "Usage: {0} [-<Arg1> [...] [-<ArgN> [...]]]" + Environment.NewLine +
                    "Where:" + Environment.NewLine +
                    "<Arg#>" + Environment.NewLine +
                    "  -Bios                     Run all the BIOS operations that only retrieve information" + Environment.NewLine +
                    "  -Bios <BiosOp>[=<Data>]+  Perform one or more BIOS operations with optional parameters" + Environment.NewLine +
                    "  -Ec                       Get the value of all Embedded Controller registers in a table format" + Environment.NewLine +
                    "  -Ec [<Reg>][=<Byte>]+     Get or set byte value(s) of one or more specific registers" + Environment.NewLine +
                    "  -Ec [<Reg>(2)][=<Word>]+  Get or set word value(s) of one or more pair(s) of consecutive specific registers" + Environment.NewLine +
                    "  -EcMon [FileName]         Monitor the values of all registers for changes and report, optionally save to file" + Environment.NewLine +
                    "  -Prog                     List available fan control programs loaded from the configuration file" + Environment.NewLine +
                    "  -Prog <Name>              Run a specified fan control program" + Environment.NewLine +
                    "  -Run <TName> [<Args>]     Run a specified task (in headless mode, no console output)" + Environment.NewLine +
                    "  -Task                     Check the status of all scheduled tasks" + Environment.NewLine +
                    "  -Task <TName>[=<Flag>]+   Enable or disable a scheduled task" + Environment.NewLine +
                    "  -SelfTest                 Run the built-in tests (touches no hardware)" + Environment.NewLine +
                    "  -?|-H|[-]-Help|[-]-Usage  Show usage information" + Environment.NewLine +
                    "<BiosOp>" + Environment.NewLine +
                    "  Cpu:PL1=<Byte> Cpu:PL4=<Byte> Cpu:PLGpu=<Byte> Gpu[=<GpuPreset>] GpuMode[=<GpuMode>] Xmp=<Flag>" + Environment.NewLine +
                    "  FanCount FanLevel[=<FanLevel>] FanMax[=<Flag>] FanMode=<FanMode> FanTable[=<FanTable>] FanType" + Environment.NewLine +
                    "  Idle[=<Flag>] Temp Throttling BornDate System Adapter HasOverclock HasMemoryOverclock HasUndervolt" + Environment.NewLine +
                    "  KbdType HasBacklight Backlight[=<Flag>] Color[=<Color>] Anim[=<ByteArray>]" + Environment.NewLine +
                    "<Data>" + Environment.NewLine +
                    "{1}" +
                    "Arguments are case-insensitive. Any argument can appear any number of times.",

                // GUI
                ["GuiAlreadyRunning"] = "Already running in the background: click on the notification area icon or run StarMon -Usage for command-line parameters",
                ["GuiBtnDel"] = Conv.GetChar(Conv.SpecialChar.HeavyMultiplication),
                ["GuiBtnSet"] = Conv.GetChar(Conv.SpecialChar.HeavyCheckmark),
                ["GuiPromptReboot"] = "A system restart is required\r\nfor the change to take effect\r\n\r\nRestart now?",
                ["GuiTranslated"] = "", // Only filled out for translations

                // GUI: About (doubles as an error form)
                ["GuiAboutTitle"] = "About StarMon",
                ["GuiAboutTitleError"] = "StarMon Error",
                ["GuiAboutCaption"] = "Hardware Monitoring & Control",
                ["GuiAboutText"] = "{\\rtf1\\ansi Monitor temperatures and control fan speeds using WMI BIOS and the Embedded Controller. Lightweight, runs in the background with minimal footprint. Developed by Star. Includes code © 2023-2024 Piotr Szczepański, licensed under GPL-3.0.}",
                ["GuiAboutTextErrorPrefix"] = "{\\rtf1\\ansi\\deff0{\\colortbl;\\red255\\green0\\blue0;}\\cf1",
                ["GuiAboutTextErrorSuffix"] = "}",

                // GUI: Main
                ["GuiMainFan"] = "Fan Monitoring & Control",
                ["GuiMainFan0"] = "CPU",
                ["GuiMainFan1"] = "GPU",
                ["GuiMainFanAuto"] = "Auto",
                ["GuiMainFanConst"] = "Const",
                ["GuiMainFanMax"] = "Max",
                ["GuiMainFanProg"] = "Prog",
                ["GuiMainFanProgSet"] = "Set Fan Program",
                ["GuiMainFanProgSetNoSel"] = "No program selected",
                ["GuiMainFanOff"] = "Off",
                ["GuiMainKbd"] = "Keyboard Backlight & Color",
                ["GuiMainKbdColorPickLeft"] = "Left Zone Color",
                ["GuiMainKbdColorPickMiddle"] = "Middle Zone Color",
                ["GuiMainKbdColorPickRight"] = "Right Zone Color",
                ["GuiMainKbdColorPickWasd"] = "WASD Keys Color",
                ["GuiMainKbdColorPickKeyboard"] = "Keyboard Color",
                ["GuiMainKbdColorPresetAdd"] = "Save Preset",
                ["GuiMainKbdColorPresetAddValueDefault"] = "New Preset",
                ["GuiMainKbdColorPresetDel"] = "Delete Preset",
                ["GuiMainKbdColorPresetDelConfirm"] = "Are you sure?",
                ["GuiMainKbdColorPresetDelNoSel"] = "No preset selected",
                ["GuiMainKbdColorPresetDelPrompt"] = "Delete",
                // GUI: Main, unsupported-feature panel (replaces the keyboard
                // card on devices with no backlight control at all)
                ["GuiMainKbdUnsupported"] = "Unsupported Features",
                ["GuiMainKbdUnsupportedWait"] = "Determining which features this device does not support…",
                ["GuiMainKbdUnsupportedNone"] = "This device supports every feature in the application.",
                ["GuiMainKbdUnsupportedList"] = "The following are not supported on this device (hidden in the interface):",
                ["GuiMainKbdUnsupportedFail"] = "The list could not be built:",

                ["GuiMainSys"] = "System Status & Information",
                ["GuiMainSysAdapterNotSupported"] = Conv.RTF_CF1 + "AC Unknown",
                ["GuiMainSysAdapterMeetsRequirement"] = Conv.RTF_CF3 + "AC Power OK",
                ["GuiMainSysAdapterBelowRequirement"] = Conv.RTF_CF4 + "AC Power Low",
                ["GuiMainSysAdapterBatteryPower"] = Conv.RTF_CF1 + "No AC Power",
                ["GuiMainSysAdapterNotFunctioning"] = Conv.RTF_CF4 + "AC Fail",
                ["GuiMainSysAdapterError"] = Conv.RTF_CF4 + "AC Error",
                ["GuiMainSysBorn"] = "*",
                ["GuiMainSysGpu"] = "GPU",
                ["GuiMainSysGpuPpab"] = "PPAB",
                ["GuiMainSysGpuCustomTgp"] = "cTGP",
                ["GuiMainSysGpuDState"] = "DState",
                ["GuiMainSysThrottlingUnknown"] = Conv.RTF_CF1 + "",
                ["GuiMainSysThrottlingDefault"] = Conv.RTF_CF5 + "Not Throttling",
                ["GuiMainSysThrottlingOn"] = Conv.RTF_CF4 + "Throttling",
                ["GuiMainSysMsgWelcome"] = "Welcome!",
                ["GuiMainTitle"] = "StarMon — Hardware Monitoring & Control",
                ["GuiMainTmp"] = "Temperature Sensor Readings",
                ["GuiMainTmpCPUT"] = "CPUT",
                ["GuiMainTmpGPTM"] = "GPTM",
                ["GuiMainTmpIRSN"] = "IRSN",
                ["GuiMainTmpRTMP"] = "RTMP",
                ["GuiMainTmpTMP1"] = "TMP1",
                ["GuiMainTmpTNT2"] = "TNT2",
                ["GuiMainTmpTNT3"] = "TNT3",
                ["GuiMainTmpTNT4"] = "TNT4",
                ["GuiMainTmpTNT5"] = "TNT5",

                // GUI: Menu
                ["GuiMenuSubFan"] = "Fan",
                ["GuiMenuActFanMax"] = "Maximum",
                ["GuiMenuActFanModeDefault"] = "Default",
                ["GuiMenuActFanModePerformance"] = "Performance",
                ["GuiMenuActFanModeCool"] = "Cool",
                ["GuiMenuActFanModeQuiet"] = "Quiet",
                ["GuiMenuActFanModeExtreme"] = "Extreme",
                ["GuiMenuActFanOff"] = "Off",
                ["GuiMenuSubGpu"] = "Graphics",
                ["GuiMenuActGpuDisplayColor"] = "Reload Color Profile",
                ["GuiMenuActGpuDisplayOff"] = "Set Display Off",
                ["GuiMenuActGpuPowerMin"] = "Base Power",
                ["GuiMenuActGpuPowerMed"] = "Extra Power",
                ["GuiMenuActGpuPowerMax"] = "Extra Power with Boost",
                ["GuiMenuActGpuRefreshHigh"] = "High Refresh Rate",
                ["GuiMenuActGpuRefreshLow"] = "Standard Refresh Rate",
                ["GuiMenuActGpuModeDiscrete"] = "Discrete Exclusive",
                ["GuiMenuActGpuModeOptimus"] = "Optimus Soft-Switching",
                ["GuiMenuSubKbd"] = "Keyboard",
                ["GuiMenuActKbdBacklight"] = "Backlight",
                ["GuiMenuActKbdColorPresetDefaultRed"] = "Omen Red",
                ["GuiMenuActKbdColorPresetDefaultWhite"] = "Omen White",
                ["GuiMenuSubSet"] = "Settings",
                ["GuiMenuActSetStayTop"] = "Stay on Top",
                ["GuiMenuActSetIconDyn"] = "Dynamic Icon",
                ["GuiMenuActSetIconDynBg"] = "Dynamic Background",
                ["GuiMenuActSetTaskGui"] = "Start with Windows",
                ["GuiMenuActSetAutoconfig"] = "Apply Settings on Startup",
                ["GuiMenuActSetTaskKey"] = "Intercept Omen Key",
                ["GuiMenuActSetTaskMux"] = "Advanced Optimus Fix",
                ["GuiMenuActFanCurve"] = "Fan Curve…",
                ["GuiMenuActGpuBrightness"] = "Brightness",
                ["GuiMenuActKbdTempColor"] = "Temperature-reactive color",
                ["GuiMenuActKbdFxCycle"] = "Color cycle",
                ["GuiMenuActKbdFxBreathe"] = "Breathing effect",
                ["GuiMenuActSetThermal"] = "Automatic thermal protection",
                ["GuiMenuActSetThermalLevel"] = "Thermal limit",
                ["GuiMenuActSetThrottleNotify"] = "Thermal throttling notification",
                ["GuiMenuActSetGpuBattery"] = "Poll GPU on battery",
                ["GuiMenuActSetFanKeepAlive"] = "Keep manual fan speed",
                ["GuiMenuActSetUpdateInterval"] = "Update",
                ["GuiMenuActSetPowerMode"] = "Power mode",
                ["GuiMenuActSetCpuBoost"] = "CPU Boost",
                ["GuiMenuActSetRefreshPower"] = "Refresh rate follows power source",
                ["GuiMenuActSetCapabilities"] = "Capabilities…",
                ["GuiMenuActSetLanguage"] = "Language",
                ["GuiMenuActSetLanguageAuto"] = "Automatic",
                ["GuiMenuActSetLanguageEnglish"] = "English",
                ["GuiMenuActSetLanguageTurkish"] = "Türkçe",
                ["GuiMenuActToggleFormLog"] = "Log Viewer",
                ["GuiMenuActToggleFormMain"] = "Show Monitor",
                ["GuiMenuActToggleFormMainHide"] = "Hide Monitor",
                ["GuiMenuActExit"] = "Exit",

                // GUI: Menu, runtime-built captions with a value baked into the text
                ["GuiMenuActKbdIdleOff"] = "Idle switch-off",
                ["GuiMenuActKbdIdleOffDisabled"] = "off",
                ["GuiMenuActGpuDisplayOffHotkey"] = "Display-off hotkey",
                ["GuiMenuPowerModePerformance"] = "Performance",
                ["GuiMenuPowerModeSaver"] = "Power saver",
                ["GuiMenuPowerModeBalanced"] = "Balanced",
                ["GuiMenuPowerModeUnknown"] = "?",
                ["GuiMenuCpuBoostOff"] = "Off",
                ["GuiMenuCpuBoostOn"] = "On",
                ["GuiMenuCpuBoostAggressive"] = "Aggressive",
                ["GuiMenuCpuBoostOnEfficient"] = "On (efficient)",
                ["GuiMenuCpuBoostAggressiveEfficient"] = "Aggressive (efficient)",
                ["GuiMenuCpuBoostUnknown"] = "?",

                // GUI: Stat cards
                ["GuiMainCardBatCharging"] = "charging",
                ["GuiMainCardBatPluggedIn"] = "plugged in",
                ["GuiMainCardBatOnBattery"] = "on battery",
                ["GuiMainCardBatNone"] = "no battery",
                ["GuiMainCardBatCycles"] = "cycles",
                ["GuiMainCardBatHealth"] = "health",
                ["GuiMainCardGpuIdle"] = "idle (on battery)",
                ["GuiMainCardGpuOnBattery"] = "on battery",
                ["GuiMainCardNotAvailable"] = "n/a",

                // GUI: Battery tooltip
                ["GuiMainBatTipNone"] = "No battery detected",
                ["GuiMainBatTipBattery"] = "Battery",
                ["GuiMainBatTipPower"] = "Power",
                ["GuiMainBatTipCharging"] = "charging",
                ["GuiMainBatTipDischarging"] = "discharging",
                ["GuiMainBatTipRemaining"] = "Time remaining",
                ["GuiMainBatTipHealth"] = "Health (wear)",
                ["GuiMainBatTipCycles"] = "Charge cycles",

                // GUI: Details panel row labels (a fixed-width label column)
                ["GuiMainDetSystem"] = "System",
                ["GuiMainDetStatus"] = "Status",
                ["GuiMainDetUsage"] = "Usage",
                ["GuiMainDetGpu"] = "GPU",
                ["GuiMainDetDisk"] = "Disk",
                ["GuiMainDetNetwork"] = "Network",
                ["GuiMainDetPower"] = "Power",
                ["GuiMainDetCore"] = "Core",
                ["GuiMainDetTemp"] = "Temp",
                ["GuiMainDetClock"] = "Clock",
                ["GuiMainDetCaption"] = "System · GPU · Disk · Network · Power · Cores",

                // GUI: Details panel values
                ["GuiMainDetPluggedIn"] = "Plugged in",
                ["GuiMainDetThrottle"] = "Throttle",
                ["GuiMainDetThrottleNone"] = "None",
                ["GuiMainDetThrottleThermal"] = "Thermal",
                ["GuiMainDetThrottlePower"] = "Power",
                ["GuiMainDetThrottleBoth"] = "Thermal+Power",
                ["GuiMainDetUptime"] = "Up",
                ["GuiMainDetPlan"] = "Plan",
                ["GuiMainDetLoad"] = "Load",
                ["GuiMainDetVram"] = "VRAM",
                ["GuiMainDetSsd"] = "SSD",
                ["GuiMainDetRead"] = "Read",
                ["GuiMainDetWrite"] = "Write",
                ["GuiMainDetDown"] = "Down",
                ["GuiMainDetUp"] = "Up",
                ["GuiMainDetLink"] = "link",
                ["GuiMainDetSystemDraw"] = "System",
                ["GuiMainDetBatterySource"] = "battery",
                ["GuiMainDetCharge"] = "Charge",
                ["GuiMainDetLeft"] = "Left",
                ["GuiMainDetHour"] = "h",
                ["GuiMainDetMinute"] = "min",
                ["GuiMainDetNotAvailable"] = "n/a",

                // GUI: Thermal protection and throttle notifications
                ["GuiThermalProtectOn"] = "Thermal protection: fans set to maximum",
                ["GuiThermalProtectPanic"] = "Emergency thermal protection: fan control handed back to hardware",
                ["GuiThermalProtectOff"] = "Thermal protection released",
                ["GuiThrottleNotify"] = "CPU thermal throttling detected",

                // GUI: Fan curve editor
                ["GuiCurveTitle"] = "Fan Curve Editor",
                ["GuiCurveHint"] = "Drag the points: X = temperature (°C), Y = fan speed (%). Apply runs it in performance mode.",
                ["GuiCurveApply"] = "Apply",
                ["GuiCurveDefault"] = "Default",
                ["GuiCurveStop"] = "Stop",
                ["GuiCurveClose"] = "Close",
                ["GuiCurveApplied"] = "Applied (performance mode)",
                ["GuiCurveStopped"] = "Stopped",
                ["GuiCurveError"] = "Error:",

                // GUI: Tooltips
                ["GuiTipBtnAccept"] = "Confirm and proceed",
                ["GuiTipBtnCancel"] = "Cancel and close the dialog",
                ["GuiTipFan0Cap"] = "The left-hand side shows the first (CPU) fan readings",
                ["GuiTipFan1Cap"] = "The right-hand side shows the second (GPU) fan readings",
                ["GuiTipFanUnitVal"] = "Fan speed is measured in revolutions per minute (rpm)",
                ["GuiTipFan0Val"] = "Real-time CPU fan speed reading [rpm]",
                ["GuiTipFan1Val"] = "Real-time GPU fan speed reading [rpm]",
                ["GuiTipFanUnitRte"] = "Fan relative rate is measured in percent (%)",
                ["GuiTipFan0Rte"] = "CPU fan relative rate [%]",
                ["GuiTipFan0RteBar"] = "CPU fan relative rate illustrated on a bar scale",
                ["GuiTipFan1Rte"] = "GPU fan relative rate [%]",
                ["GuiTipFan1RteBar"] = " GPU fan relative rate illustrated on a bar scale" + Environment.NewLine + " Note the origin is on the right-hand side",
                ["GuiTipFan0Lvl"] = "CPU fan level [krpm]" + Environment.NewLine + "Custom speed: move slider" + Environment.NewLine + "and click button to apply",
                ["GuiTipFan1Lvl"] = "GPU fan level [krpm]" + Environment.NewLine + "Custom speed: move slider" + Environment.NewLine + "and click button to apply",
                ["GuiTipFanCountdown"] = "If applicable, this area shows the countdown until" + Environment.NewLine + "the BIOS reverts back to the automatic defaults" + Environment.NewLine + "Select Const to prevent the timer from running out",
                ["GuiTipFanProg"] = "Fan program" + Environment.NewLine + "Speed will follow temperature" + Environment.NewLine + "according to your preferences",
                ["GuiTipFanProgCmb"] = "Choose a fan program from the drop-down list",
                ["GuiTipFanAuto"] = "Automatic mode (the default setting)",
                ["GuiTipFanMode"] = "Choose a fan mode from the drop-down list",
                ["GuiTipFanConst"] = "Constant speed mode" + Environment.NewLine + "Use trackbars to set each fan level",
                ["GuiTipFanMax"] = "Maximum speed mode" + Environment.NewLine + "Fans operate at maximum speed" + Environment.NewLine + "(5,500 and 5,700 rpm)",
                ["GuiTipFanOff"] = "Fans off" + Environment.NewLine + "Power off the fans completely",
                ["GuiTipFanSet"] = "Click to apply current settings" + Environment.NewLine + "Button is highlighted when settings have changed",
                ["GuiTipKbdBacklight"] = "Toggle keyboard backlight on and off",
                ["GuiTipKbdColorPreset"] = "Choose a color preset to apply" + Environment.NewLine + "from the drop-down box",
                ["GuiTipKbdColorPresetDel"] = "Delete the currently-selected preset",
                ["GuiTipKbdColorPresetSet"] = "Save the current settings as a preset",
                ["GuiTipKbdColorVal"] = "Adjust colors using their hexadecimal values with this parameter" + Environment.NewLine + "Set colors from the command line with: StarMon -Bios Color=<Param>",
                ["GuiTipKbdPic"] = "Click anywhere on the keyboard to change the color" + Environment.NewLine + "with a color picker, changes take place immediately",
                ["GuiTipSys"] = "System status information is shown here",
                ["GuiTipTmpCPUT"] = "CPU Temperature",
                ["GuiTipTmpGPTM"] = "GPU Temperature",
                ["GuiTipTmpBIOS"] = "Temperature reported by the BIOS" + Environment.NewLine + "Values observed are much lower" + Environment.NewLine + "than for any other sensor",
                ["GuiTipTmpIRSN"] = "Infrared Sensor Temperature",
                ["GuiTipTmpRTMP"] = "Platform Controller Hub Temperature",
                ["GuiTipTmpTMP1"] = "Memory Temperature",
                ["GuiTipTmpTNT2"] = "Interpretation Unknown",
                ["GuiTipTmpTNT3"] = "Storage",
                ["GuiTipTmpTNT4"] = "Storage",
                ["GuiTipTmpTNT5"] = "Interpretation Unknown",
                ["GuiTipTmpUnknown"] = "Custom Sensor",
                ["GuiTipTxtInput"] = "Enter the value",

                // GUI: Main, stat-card and details-panel tooltips
                ["GuiTipCardCpu"] =
                    "Current processor (CPU) temperature." + Environment.NewLine +
                    "Subline: load percentage · power draw (W) · core clock (GHz)." + Environment.NewLine +
                    "The color shifts from blue to red with temperature.",
                ["GuiTipCardGpu"] =
                    "Current graphics card (GPU) temperature." + Environment.NewLine +
                    "Subline: load percentage · power draw (W) · core clock (GHz)." + Environment.NewLine +
                    "Values may stay empty on battery when NVIDIA polling is off.",
                ["GuiTipCardFan"] =
                    "Fan speed." + Environment.NewLine +
                    "The large value is the highest fan speed (rpm); the subline shows the CPU and GPU fan percentages.",
                ["GuiTipSysInfo"] =
                    "System summary:" + Environment.NewLine +
                    "System line — manufacturer/model, born-on date, CPU power limit (PL4) and adapter/battery state." + Environment.NewLine +
                    "Status line — GPU mode, D-state and throttling state." + Environment.NewLine +
                    "Usage line — RAM usage, uptime and the active power plan.",
                ["GuiTipExtra"] =
                    "GPU, I/O and power details:" + Environment.NewLine +
                    "GPU line — load, temperature, power (W), clocks (core/memory) and VRAM from NVAPI." + Environment.NewLine +
                    "Disk line — SSD temperature with read/write throughput." + Environment.NewLine +
                    "Network line — download/upload rates and the Wi-Fi link (signal, link rate)." + Environment.NewLine +
                    "Power line — CPU/GPU draw, battery flow (system draw) and the projected battery time.",
                // The per-core table this described belonged to the Windows
                // Forms build and went with it. The key was left behind and
                // defined a second time further down for the row that
                // replaced it, so this one was only ever overwritten.
                ["GuiTipGrpDetails"] =
                    "Details panel: system, GPU, disk/network and processor core information." + Environment.NewLine +
                    "Hover over each row for its description.",

                // GUI: Hotkey capture dialog (Gui.cs)
                ["GuiHotkeyNotAssigned"] = "not assigned",
                ["GuiHotkeyModCtrl"] = "Ctrl+",
                ["GuiHotkeyModAlt"] = "Alt+",
                ["GuiHotkeyModShift"] = "Shift+",
                ["GuiHotkeyModWin"] = "Win+",
                ["GuiHotkeyDialogTitle"] = "Display-off hotkey",
                ["GuiHotkeyInstructions"] =
                    "Press the key combination you want to assign." + Environment.NewLine +
                    "At least one modifier is required (Ctrl / Alt / Shift).",
                ["GuiHotkeyOk"] = "OK",
                ["GuiHotkeyClear"] = "Clear",
                ["GuiHotkeyCancel"] = "Cancel",

                // GUI: Hardware capabilities dialog (GuiFormCaps.cs)
                ["GuiCapsTitle"] = "Hardware Capabilities",
                ["GuiCapsGathering"] = "Gathering hardware capabilities…",
                ["GuiCapsCopy"] = "Copy",
                ["GuiCapsClose"] = "Close",
                ["GuiCapsBuildError"] = "The report could not be built: ",

                // GUI: Log viewer (GuiFormLog.cs)
                ["GuiLogTitle"] = "— Log Viewer",
                ["GuiLogClear"] = "Clear",
                ["GuiLogExport"] = "Export",
                ["GuiLogPause"] = "Pause",
                ["GuiLogResume"] = "Resume",
                ["GuiLogAutoScroll"] = "Auto-Scroll",
                ["GuiLogSearch"] = "Search:",
                ["GuiLogFilter"] = "Filter:",
                ["GuiLogFilterBios"] = "BIOS",
                ["GuiLogFilterEc"] = "EC",
                ["GuiLogFilterHardware"] = "Hardware",
                ["GuiLogFilterError"] = "Error",
                ["GuiLogFilterInfo"] = "Info",
                ["GuiLogFilterGui"] = "GUI",
                ["GuiLogEntries"] = "{0} entries",
                ["GuiLogSaveFilter"] = "Text file (*.txt)|*.txt|Log file (*.log)|*.log",
                ["GuiLogSaveSuccess"] = "The log file was saved successfully.",
                ["GuiLogSaveFail"] = "The log file could not be saved.",
                ["GuiLogErrorCaption"] = "Error",

                // GUI: History graph context menu and hover hint (SparklineGraph.cs)
                ["GuiGraphCopy"] = "Copy to clipboard",
                ["GuiGraphSavePng"] = "Save as PNG…",
                ["GuiGraphExportCsv"] = "Export data as CSV…",
                ["GuiGraphTimeWindow"] = "Time window",
                ["GuiGraphRangeShort"] = "Short",
                ["GuiGraphRangeMedium"] = "Medium",
                ["GuiGraphRangeLong"] = "Long",
                ["GuiGraphHintTitle"] = "History graph — changes over time",
                ["GuiGraphHintLegend"] = "Click the legend: hide/show a series · ",
                ["GuiGraphHintContext"] = "Right-click: copy, save PNG, time window",
                ["GuiGraphFilterPng"] = "PNG image|*.png",
                ["GuiGraphFilterCsv"] = "Comma-separated values|*.csv",

                // Data formats
                ["DataTypeBool"] = "<Flag>",
                ["DataSyntaxBool"] = "<On|True|Yes|1> | <Off|False|No|0>",

                ["DataTypeByte"] = "<Byte>",
                ["DataSyntaxByte"] = "<0-255|0x00-0xFF|0b00000000-0b11111111>",

                ["DataTypeByteArray"] = "<ByteArray>",
                ["DataSyntaxByteArray"] = "<00-FF>+",

                ["DataTypeColor4"] = "<Color>",
                ["DataSyntaxColor4"] = "<PresetName> | <RGB0>:<RGB1>:<RGB2>:<RGB3> (<RGB#>: 000000-FFFFFF)",

                ["DataTypeFanLevel"] = "<FanLevel>",
                ["DataSyntaxFanLevel"] = "<Fan1>,<Fan2> (<Fan#>: 0-255|0x00-0xFF|0b00000000-0b11111111)",

                ["DataTypeFanMode"] = "<FanMode>",
                ["DataSyntaxFanMode"] = "<FanModeId|0-255|0x00-0xFF|0b...> (<FanModeId>: Default|Performance|Cool|L#, <#>: 0-8)",

                ["DataTypeFanTable"] = "<FanTable>",
                ["DataSyntaxFanTable"] = "<Fan1>,<Fan2>,<Temp>[:...[:...]] (<Fan#>, <Temp>: <Byte>)",

                ["DataTypeGpuMode"] = "<GpuMode>",
                ["DataSyntaxGpuMode"] = "<GpuModeId|0-255|0x00-0xFF|0b...> (<GpuModeId>: Hybrid|Discrete|Optimus)",

                ["DataTypeGpuPowerLevel"] = "<GpuPreset>",
                ["DataSyntaxGpuPowerLevel"] = "Max[imum] | Med[ium]|Mid[dle] | Min[imum]",

                ["DataTypeReg"] = "<Reg>",
                ["DataSyntaxReg"] = "<NAME|0-255|0x00-0xFF|0b00000000-0b11111111>",
                ["DataSyntaxOrTwo"] = "[(2)]",

                ["DataTypeTName"] = "<TName>",
                // The words here are the ones -Task and -Run actually accept,
                // which are the TaskId names. This said "Autorun", which is
                // what the GUI task does rather than what it is called, and
                // anyone who typed it back was told the argument was unknown.
                ["DataSyntaxTName"] = "Gui (Autorun on Logon) | Key (Omen Key Capture) | Mux (Advanced Optimus Fix)",

                ["DataTypeWord"] = "<Word>",
                ["DataSyntaxWord"] = "<0-65535|0x0000-0xFFFF|0b0000000000000000-0b1111111111111111>",

                // Error messages
                ["ErrArgUnknown"] = "Unknown argument",
                ["ErrBiosCall"] = "BIOS call failed",
                ["ErrBiosInit"] = "Failed to initialize the BIOS controls. Please make sure you have a compatible HP system, and that the ACPI\\PNP0C14 driver is installed.",
                ["ErrBiosNull"] = "Failed to instantiate the BIOS controls",
                ["ErrBiosSend"] = "Failed to make the BIOS call",
                ["ErrBiosSendCommand"] = "Command not available",
                ["ErrBiosSendSize"] = "Input or output size too small",
                ["ErrBiosSendUnknown"] = "Unknown response from BIOS: {0}",
                ["ErrConfigLoad"] = "Failed to load configuration data",
                ["ErrConfigSave"] = "Failed to save configuration data",
                ["ErrEcInit"] = "Failed to initialize the embedded controller",
                ["ErrEcLock"] = "Failed to acquire embedded controller exclusive lock",
                ["ErrEcNull"] = "Failed to instantiate the embedded controller",
                ["ErrFileSave"] = "Failed to save the file",
                ["ErrLocaleNull"] = "Failed to instantiate the localizable message system",
                ["ErrLocaleLoad"] = "Failed to load localizable messages from the external file",
                ["ErrNeedRegisterRead"] = "Expected a register to read from",
                ["ErrNeedRegisterWrite"] = "Expected a register to write to",
                ["ErrNeedValueBool"] = "Expected a Boolean flag",
                ["ErrNeedValueByte"] = "Expected a byte value to set",
                ["ErrNeedValueByteArray"] = "Expected a byte array value to set",
                ["ErrNeedValueColor4"] = "Expected an array of four color values",
                ["ErrNeedValueFanLevel"] = "Expected a pair of fan speed levels",
                ["ErrNeedValueFanMode"] = "Expected a fan mode",
                ["ErrNeedValueFanTable"] = "Expected an array of fan table entries",
                ["ErrNeedValueGpuMode"] = "Expected a GPU mode",
                ["ErrNeedValueGpuPowerLevel"] = "Expected a GPU power preset",
                ["ErrNeedValueWord"] = "Expected a word value to set",
                ["ErrNotImplemented"] = "Not implemented",
                ["ErrProgName"] = "No such program",
                ["ErrProgNone"] = "No programs configured",
                ["ErrUnexpected"] = "Exception",
                ["ErrUnexpectedReally"] = "No details available",

                // Program
                ["Prog"] = "Program",
                ["ProgAlt"] = "[Alt]",
                ["ProgEnd"] = "Program Ended",
                ["ProgModeDefault"] = "Default",
                ["ProgModePerformance"] = "Performance",
                ["ProgModeCool"] = "Cool",
                ["ProgModeQuiet"] = "Quiet",
                ["ProgModeExtreme"] = "Extreme",
                ["ProgFans"] = "Fans",
                ["ProgLvl"] = "Lvl",
                ["ProgT"] = "T",
                ["ProgSubMax"] = "max",

                // Units
                // GUI: the WPF window. Prefixed so it is obvious which set a
                // key belongs to while the older ones are still being retired.
                ["GuiWpfDashboard"] = "Dashboard",
                ["GuiWpfSensors"] = "Sensors",
                ["GuiWpfSensorsCaption"] = "ALL READINGS",
                ["GuiWpfSensorsHint"] = "Everything the machine reports, grouped and updated live. The dashboard shows the headline figures; this is the full set.",
                ["GuiWpfCurve"] = "Fan curve",
                ["GuiWpfCooling"] = "Cooling",
                ["GuiWpfKeyboard"] = "Keyboard",
                ["GuiWpfLog"] = "Log",
                ["GuiWpfSystem"] = "System",
                ["GuiWpfAbout"] = "About",

                ["GuiWpfHottest"] = "HOTTEST",
                ["GuiWpfCpu"] = "CPU",
                ["GuiWpfGpu"] = "GPU",
                ["GuiWpfFans"] = "FANS",
                ["GuiWpfBattery"] = "BATTERY",

                // The summary strip under the tabs. Separate keys from the
                // card captions above because they have to be far shorter:
                // four labels, four figures, four trends and three badges
                // share one line, and "GRAPHICS CARD" there would push the
                // badges off the end of a narrow window.
                ["GuiWpfStripCpu"] = "CPU",
                ["GuiWpfStripGpu"] = "GPU",
                ["GuiWpfStripFan"] = "FAN",
                ["GuiWpfStripBattery"] = "BAT",

                // The badges. These say the machine is in a state the user did
                // not necessarily choose, which nothing in the window has ever
                // been able to say.
                // The dashboard blocks' table rows. Short: they sit in a
                // quarter-width card beside their values.
                ["GuiWpfRowLoad"] = "Load",
                ["GuiWpfRowPower"] = "Power",
                ["GuiWpfRowLimits"] = "Limits",
                ["GuiWpfRowClock"] = "Clock",
                ["GuiWpfRowVram"] = "VRAM",
                ["GuiWpfRowFanCpu"] = "CPU fan",
                ["GuiWpfRowFanGpu"] = "GPU fan",
                ["GuiWpfRowHottest"] = "Hottest",
                ["GuiWpfRowCeiling"] = "Ceiling",
                ["GuiWpfRowCharge"] = "Charge",
                ["GuiWpfRowFlow"] = "Flow",
                ["GuiWpfRowPlan"] = "Plan",
                ["GuiWpfRowUptime"] = "Up",
                ["GuiWpfCoreClocks"] = "Core clocks",
                ["GuiWpfDiskRate"] = "Disk rate",
                ["GuiWpfNetRate"] = "Net rate",
                ["GuiTipDiskRate"] = "How much the boot drive is actually reading and writing, in megabytes a second. Separate from its temperature, which says how hard it has been working, not how hard it is working now.",
                ["GuiTipNetRate"] = "Traffic actually moving over the network, in megabits a second. The link speed above is what the connection could carry; this is what it is carrying.",
                ["GuiTipPowerMode"] = "Windows' own power mode — the slider in the battery flyout. Not the same thing as the power plan above it.",
                ["GuiTipUptime"] = "How long the machine has been running since it last started.",
                ["GuiTipCountdown"] = "The Embedded Controller's failsafe timer. When it reaches zero the firmware takes the fans back, whatever level was set by hand.",
                ["GuiTipHpFan"] = "A fan speed the firmware publishes through its own sensor interface, under its own name for the fan. Where the Embedded Controller's tachometer is unreliable, this is the honest figure.",
                ["GuiWpfRowMemClock"] = "Mem clock",
                ["GuiWpfRowTgp"] = "TGP",
                ["GuiWpfSystemBlock"] = "SYSTEM",
                ["GuiWpfRowMemory"] = "Memory",
                ["GuiWpfRowDisk"] = "Disk",
                ["GuiWpfRowNetwork"] = "Network",
                ["GuiWpfRowMode"] = "Mode",
                ["GuiWpfRowCountdown"] = "Countdown",
                ["GuiWpfRowGuard"] = "Protection",
                ["GuiWpfRowHealth"] = "Health",
                ["GuiWpfRowCycles"] = "Cycles",

                // Where the fan level ceiling came from. The profile records
                // this at every start and the only place it has ever reached
                // is a log line, which left the slider's limit looking like a
                // number somebody made up.
                ["GuiWpfCeilingTable"] = "from the fan table",
                ["GuiWpfCeilingMaximum"] = "seen at maximum",
                ["GuiWpfCeilingRunning"] = "seen running",
                ["GuiWpfCeilingSet"] = "configured",
                ["GuiWpfCeilingFixed"] = "fixed by hand",
                ["GuiWpfCountdownOff"] = "off",
                ["GuiWpfGuardActive"] = "engaged",
                ["GuiWpfGuardIdle"] = "watching",
                ["GuiWpfPowerModeHigh"] = "Best performance",
                ["GuiWpfPowerModeBalanced"] = "Balanced",
                ["GuiWpfPowerModeSaver"] = "Best efficiency",
                ["GuiWpfCoreClockTip"] = "The clock each logical processor is running at. One flat colour rather than the temperature bands: a core running slowly is not a core in trouble.",

                // The chart's window and export
                ["GuiWpfWindowShort"] = "2 min",
                ["GuiWpfWindowMedium"] = "5 min",
                ["GuiWpfWindowLong"] = "10 min",
                ["GuiWpfTipWindow"] = "How far back the plot reaches. Changing it keeps the history already recorded.",
                ["GuiWpfTipExportCsv"] = "Save the plotted history as a CSV file, one row per sample.",

                // The cooling section: the curve, the saved programs, and the
                // limits the firmware actually imposes
                // The System section
                ["GuiWpfLicenceCaption"] = "LICENCE",
                ["GuiWpfMachineCaption"] = "THIS MACHINE",
                ["GuiWpfProfileCaption"] = "WHAT WAS WORKED OUT",
                ["GuiWpfBiosCaption"] = "BIOS SETUP",
                ["GuiWpfBiosHint"] = "Every setting the firmware publishes, read once at startup. Read-only: these are changed in the BIOS setup screen itself, not here.",
                ["GuiWpfBiosSearchTip"] = "Filters by name and by value, so searching for Enabled finds every option that is.",
                ["GuiWpfReportCaption"] = "FULL HARDWARE REPORT",
                ["GuiWpfReportHint"] = "Everything the application can establish about this machine, in one text. This is what to attach when reporting a problem.",
                ["GuiWpfProbing"] = "asking the firmware...",
                ["GuiWpfPowerModeCaption"] = "WINDOWS POWER MODE",
                ["GuiWpfPowerModeHint"] = "The same slider as the one in the battery flyout. Not the power plan, and not the firmware performance profile on the dashboard — all three exist and all three matter.",
                ["GuiWpfRowFamily"] = "Family",
                ["GuiWpfRowExtreme"] = "Extreme mode",
                ["GuiWpfRowZones"] = "Keyboard zones",
                ["GuiWpfRowRefresh"] = "Refresh rates",
                ["GuiWpfRowProbed"] = "Probed",
                ["GuiWpfNoColour"] = "no colour",
                // The settings the configuration file used to be the only way
                // to reach
                ["GuiWpfFanSection"] = "FANS",
                ["GuiWpfFanSectionHint"] = "These scale the fan sliders and the curve. The application works the ceiling out at startup; set it by hand only for a board that misreports its own.",
                ["GuiWpfFanCeiling"] = "Level ceiling",
                ["GuiWpfFanFloor"] = "Level floor",
                ["GuiWpfCurveHysteresis"] = "Curve hysteresis",
                ["GuiWpfTipHysteresis"] = "How far the temperature has to fall below a curve step before the level steps back down. Zero follows the curve exactly and makes the fans surge whenever a reading sits on a boundary.",
                ["GuiWpfFanAutoDetect"] = "Let the ceiling rise when the fans are seen running higher",
                ["GuiWpfKeepFansSet"] = "Keep a manual fan speed from reverting on its own",
                ["GuiWpfSuspendOnSleep"] = "Suspend the fan program while the machine sleeps",
                ["GuiWpfDisplaySection"] = "DISPLAY",
                ["GuiWpfRefreshOnAc"] = "ON MAINS",
                ["GuiWpfRefreshOnBattery"] = "ON BATTERY",
                ["GuiWpfRefreshAutoDetect"] = "Take the rates from what the display reports",
                ["GuiWpfHotkey"] = "SWITCH THE DISPLAY OFF",
                ["GuiWpfHotkeyHint"] = "A global hotkey that blanks the screen. It has been registered at startup since the feature was written and there was no way to choose it.",
                ["GuiWpfHotkeyNone"] = "Not set",
                ["GuiWpfHotkeyPress"] = "Press a combination...",
                ["GuiWpfHotkeyClear"] = "Clear",
                ["GuiWpfOmenKey"] = "THE OMEN KEY",
                ["GuiWpfOmenKeyHint"] = "What the dedicated key on the keyboard does. Running a command takes precedence over the fan program.",
                ["GuiWpfKeyToggles"] = "Start and stop the fan program",
                ["GuiWpfKeyCycles"] = "Cycle through every saved program",
                ["GuiWpfKeyShowsFirst"] = "Show the window on the first press",
                ["GuiWpfKeySilent"] = "No notification when the program changes",
                ["GuiWpfKeyRuns"] = "Run a command instead",
                ["GuiWpfKeyCommandTip"] = "The program to run. A full path, or anything on the search path.",
                ["GuiWpfKeyArgumentsTip"] = "Arguments passed to it.",
                ["GuiWpfKeyMinimised"] = "Start it minimised",
                ["GuiWpfReportBiosErrors"] = "Log firmware calls this machine refuses",
                ["GuiWpfLogFileSize"] = "Roll the file over at",
                ["GuiWpfCadence"] = "HOW OFTEN",
                ["GuiWpfCadenceHint"] = "How often the hardware is asked anything. Lower is more responsive and costs more; the machinery to change it while running has always been there with nothing to change it from.",
                ["GuiWpfCadenceMonitor"] = "Window open",
                ["GuiWpfCadenceRecord"] = "Window hidden",
                ["GuiWpfCadenceProgram"] = "Fan program step",
                ["GuiWpfCoolingState"] = "WHAT THIS MACHINE ALLOWS",
                ["GuiWpfPrograms"] = "FAN PROGRAMS",
                ["GuiWpfRun"] = "Run",
                ["GuiWpfDelete"] = "Delete",
                ["GuiWpfSave"] = "Save",
                ["GuiWpfSteps"] = "steps",
                ["GuiWpfRowSoftware"] = "Software control",
                ["GuiWpfRowAlwaysOn"] = "Fans always on",
                ["GuiWpfRowFanCount"] = "Fans",
                ["GuiWpfRowLevelPath"] = "Levels via",
                ["GuiWpfYes"] = "yes",
                ["GuiWpfNo"] = "no",
                ["GuiWpfUnknown"] = "not stated",
                ["GuiWpfTipCeiling"] = "The highest fan level this board accepts, and how the application worked that out. The sliders and the curve are scaled against it.",
                ["GuiWpfTipSoftware"] = "Whether the firmware admits to offering software fan control at all. Where it does not, a level written to it may simply be ignored.",
                ["GuiWpfTipAlwaysOn"] = "A BIOS setup option. When it is on the fans never stop, whatever level is asked for — which is the explanation for a machine that will not go silent.",
                ["GuiWpfTipLevelPath"] = "Whether fan levels are written through the BIOS interface or straight to the Embedded Controller. Boards differ, and the wrong path is silently ignored.",
                ["GuiWpfTipRun"] = "Start the selected program. It keeps running while the window is closed.",
                ["GuiWpfTipDelete"] = "Remove the selected program from the configuration file.",
                ["GuiWpfTipSave"] = "Save the curve above as a program under this name. An existing program of the same name is replaced.",
                ["GuiWpfProgramRunning"] = "Running {0}",
                ["GuiWpfProgramStopped"] = "Fan program stopped",
                ["GuiWpfProgramSaved"] = "Saved {0}",
                ["GuiWpfProgramDeleted"] = "Deleted {0}",
                ["GuiWpfProgramGone"] = "That program is no longer in the configuration",
                ["GuiWpfChipProtection"] = "THERMAL PROTECTION",
                ["GuiWpfChipThrottle"] = "THROTTLING",
                ["GuiWpfChipProgram"] = "PROGRAM",
                ["GuiWpfTipProtection"] = "The application is holding the fans at maximum to protect the machine. This is not a setting you made — it clears on its own once the temperature falls.",

                // Chart legend. Short on purpose: a legend entry is read at a
                // glance beside its value, and one that wraps onto a second
                // line costs more room than the whole chart can spare.
                ["GuiWpfSeriesCpuFan"] = "CPU fan",
                ["GuiWpfSeriesGpuFan"] = "GPU fan",
                ["GuiWpfSeriesLoad"] = "Load",
                ["GuiWpfSeriesPower"] = "Power",

                ["GuiWpfFanControl"] = "FAN CONTROL",
                ["GuiWpfFanAutomatic"] = "Automatic",
                ["GuiWpfFanConstant"] = "Constant",
                ["GuiWpfFanMaximum"] = "Maximum",
                ["GuiWpfFanProgram"] = "Program",

                ["GuiWpfGraphicsPower"] = "GRAPHICS POWER",
                ["GuiWpfPerfMode"] = "PERFORMANCE MODE",

                // Live supporting-line text (battery state, throttle, fan level)
                ["GuiWpfBatNone"] = "no battery",
                ["GuiWpfBatCharging"] = "charging",
                ["GuiWpfBatAc"] = "plugged in",
                ["GuiWpfBatDc"] = "on battery",
                ["GuiWpfThrottleThermalPower"] = "Thermal + power",
                ["GuiWpfThrottleThermal"] = "Thermal",
                ["GuiWpfThrottlePower"] = "Power",
                ["GuiWpfThrottleNone"] = "None",
                ["GuiWpfLevelFmt"] = "level {0} / {1} of {2}",
                ["GuiWpfCouldNotApply"] = "Could not apply: {0}",
                ["GuiWpfGpuBase"] = "Base",
                ["GuiWpfGpuCustom"] = "Custom TGP",
                ["GuiWpfGpuBoost"] = "Boost",
                ["GuiWpfNotAvailable"] = "not available on this model",

                ["GuiWpfCurveCaption"] = "FAN CURVE",
                ["GuiWpfCurveHint"] =
                    "Drag a point to set how hard the fans work at that temperature. "
                    + "Applying the curve runs it as a fan program in Performance mode.",
                ["GuiWpfReset"] = "Reset",
                ["GuiWpfStop"] = "Stop",
                ["GuiWpfApply"] = "Apply",
                ["GuiWpfApplied"] = "Applied",

                ["GuiWpfBacklight"] = "BACKLIGHT",
                ["GuiWpfColour"] = "COLOUR",
                ["GuiWpfMode"] = "MODE",
                ["GuiWpfKbdStatic"] = "Static colour",
                ["GuiWpfKbdTemperature"] = "Follow temperature",
                ["GuiWpfKbdCycle"] = "Colour cycle",
                ["GuiWpfKbdBreathe"] = "Breathing",
                ["GuiWpfKbdSpeed"] = "EFFECT SPEED",
                ["GuiWpfKbdIdleOff"] = "SWITCH OFF WHEN IDLE",
                ["GuiWpfKbdNever"] = "never",
                ["GuiWpfKbdMinutes"] = "min",
                ["GuiWpfKbdPresets"] = "SAVED COLOURS",
                ["GuiWpfZoneLeft"] = "LEFT",
                ["GuiWpfZoneCentre"] = "CENTRE",
                ["GuiWpfZoneRight"] = "RIGHT",
                ["GuiWpfZoneWasd"] = "WASD",
                ["GuiWpfZoneAll"] = "KEYBOARD",

                ["GuiWpfLogCaption"] = "LOG",
                ["GuiWpfPause"] = "PAUSE",
                ["GuiWpfClear"] = "Clear",
                ["GuiWpfSearch"] = "Search",
                ["GuiWpfFilterProblems"] = "Problems",
                ["GuiWpfFilterHardware"] = "Hardware",
                ["GuiWpfFilterInterface"] = "Interface",
                ["GuiWpfFilterBios"] = "BIOS calls",
                ["GuiWpfFilterEc"] = "EC access",
                ["GuiWpfEntries"] = "entries",
                ["GuiWpfEntriesOf"] = "of",

                ["GuiWpfSupportCaption"] = "WHAT THIS MACHINE SUPPORTS",
                ["GuiWpfSupportHint"] =
                    "Anything unsupported is hidden from the rest of the interface "
                    + "rather than shown as a control that does nothing.",
                ["GuiWpfTagline"] =
                    "Fan, sensor and keyboard control for HP Omen and Victus laptops.",
                ["GuiWpfLicence"] =
                    "Released under the GPL-3.0. Portions copyright © 2023-2024 Piotr Szczepański.",
                ["GuiWpfVersion"] = "Version",
                ["GuiWpfBuilt"] = "Built",
                ["GuiWpfModel"] = "Model",
                ["GuiWpfBoard"] = "Board",
                ["GuiWpfBios"] = "BIOS",
                ["GuiWpfWindows"] = "Windows",

                // Details panel — extra groups and rows
                ["GuiWpfGraphics"] = "GRAPHICS",
                ["GuiWpfStorageNet"] = "STORAGE & NETWORK",
                ["GuiWpfBehaviour"] = "BEHAVIOUR",
                // Not GuiWpfLog: this is the settings card's heading, and
                // defining it under that name a second time overwrote the
                // navigation tab's own label, which is why the tab read "LOG"
                // in capitals while every other tab was in sentence case
                ["GuiWpfLogSection"] = "LOG",
                ["GuiWpfThermalGuard"] = "THERMAL PROTECTION",
                ["GuiWpfThermalGuardHint"] = "Forces the fans to maximum when the hottest sensor reaches the threshold, and hands cooling back to the firmware entirely a few degrees above it. Leave this on unless you have a reason not to.",
                ["GuiWpfThermalThreshold"] = "Threshold",
                ["GuiWpfStartWithWindows"] = "Start with Windows",
                ["GuiWpfApplyOnStart"] = "Apply saved settings on start",
                ["GuiWpfCloseExits"] = "Close button exits instead of hiding to the tray",
                ["GuiWpfStayOnTop"] = "Keep the window above other windows",
                ["GuiWpfThrottleNotify"] = "Notify when the processor is throttling",
                ["GuiWpfRefreshFollows"] = "Drop the refresh rate on battery, restore it on mains",
                ["GuiWpfPollGpuOnBattery"] = "Keep reading the graphics card on battery (uses more power)",
                ["GuiWpfLogVerbose"] = "Record every hardware exchange (verbose)",
                ["GuiWpfLogToFile"] = "Also write the log to a file beside the application",
                // Not GuiWpfFans: that is the dashboard's fan card, and
                // redefining it here renamed the card to "FANS & BOARD"
                ["GuiWpfFansBoard"] = "FANS & BOARD",
                ["GuiWpfFanCpuRpm"] = "CPU fan",
                ["GuiWpfFanGpuRpm"] = "GPU fan",
                ["GuiWpfSensorChipset"] = "Chipset",
                ["GuiWpfSensorMemory"] = "Memory",
                ["GuiWpfSensorBios"] = "BIOS probe",
                ["GuiWpfSensorProbe"] = "Board probe",
                ["GuiWpfSensorZone"] = "Thermal zone",
                ["GuiWpfSensorHealth"] = "Sensor health",
                ["GuiWpfSensorHealthOk"] = "all reporting normal",
                ["GuiWpfSensorHealthBad"] = "reported a fault",
                ["GuiTipFanRpm"] = "The speed the fan is actually turning at, counted by the firmware's own tachometer. Blank when this machine does not publish one — the level and the percentage above are then the honest figures.",
                ["GuiTipBoardSensor"] = "A temperature probe on the mainboard itself, not on the processor or the graphics chip. The hottest of these is what the fan curve and the thermal guard respond to.",
                ["GuiTipSensorHealth"] = "The firmware's own opinion of its sensors. Anything other than normal means the machine has flagged a part, which is worth looking at even when the readings still look sensible.",
                ["GuiWpfTemp"] = "Temp",
                ["GuiWpfGpuClock"] = "Clock",
                ["GuiWpfGpuVram"] = "VRAM",
                ["GuiWpfDisk"] = "Disk",
                ["GuiWpfWifi"] = "Wi-Fi",
                ["GuiWpfCores"] = "Cores",
                ["GuiWpfCoresTip"] = "Temperature of each logical processor core",
                ["GuiWpfMemory"] = "MEMORY",
                ["GuiWpfMemUsed"] = "In use",
                ["GuiWpfLinkSpeed"] = "Link",
                ["GuiWpfBatState"] = "State",
                ["GuiWpfBatPower"] = "Draw",
                ["GuiWpfPowerLimit"] = "Limit",
                ["GuiWpfGpuPowerLimit"] = "Power cap",
                ["GuiWpfCopy"] = "Copy",
                ["GuiWpfCopyAll"] = "Copy all",

                // Hover hints, so a control says what it does before it is used
                ["GuiWpfTipCpu"] = "Processor temperature, with its load, power and clock, and a bar for each core",
                ["GuiWpfTipGpu"] = "Graphics temperature, with its load, power and clock",
                ["GuiWpfTipFans"] = "Fan speed as a percentage of the hardware maximum",
                ["GuiWpfTipBattery"] = "Battery charge and the time remaining on it",
                ["GuiWpfTipFanAutomatic"] = "Hand fan control back to the firmware",
                ["GuiWpfTipFanConstant"] = "Hold both fans at the levels you set below",
                ["GuiWpfTipFanMaximum"] = "Both fans at full speed, and the graphics power lifted with them",
                ["GuiWpfTipFanProgram"] = "Run the saved fan program that follows temperature",
                ["GuiWpfTipPerfMode"] = "The firmware's power and thermal profile. Performance is what lifts the graphics power past its base draw.",
                ["GuiWpfTipGpuPower"] = "How much power the graphics chip is allowed to draw",
                ["GuiWpfTipLevels"] = "Fan level, from stopped to the hardware maximum. Only in effect in the Constant mode.",

                // The dashboard blocks. Every other row on these cards reuses a
                // sensor tip from the Sensors page; these are the readings the
                // dashboard shows in a combined form of its own.
                ["GuiWpfTipGpuMemClock"] = "The clock the card's own memory is running at, which the firmware moves separately from the core.",
                ["GuiWpfTipFanLine"] = "The level this fan was told to hold, as a percentage of the hardware maximum, and beside it the speed it is actually turning at.",
                ["GuiWpfTipHottest"] = "The highest reading of every temperature sensor on the machine. This is the figure the fan curve and the thermal protection both respond to.",
                ["GuiWpfTipCharge"] = "How much charge is left in the battery.",
                ["GuiWpfTipPlanLine"] = "The Windows power plan, and beside it the power mode from the battery flyout. They are two separate settings and both apply.",

                // The System page: what the machine is, and what the
                // application worked out about it at startup.
                ["GuiWpfTipVersion"] = "The version of this application, and the date it was built.",
                ["GuiWpfTipBoard"] = "The mainboard's own model code. This is what decides which firmware calls the machine answers, far more than the marketing name does.",
                ["GuiWpfTipWindows"] = "The edition and build of Windows this is running on.",
                ["GuiWpfTipFamily"] = "The product family the firmware reports itself as belonging to.",
                ["GuiWpfTipFanCount"] = "How many fans the firmware says this machine has. Both are driven together, so a second fan is not a second control.",
                ["GuiWpfTipExtreme"] = "Whether this board offers the Extreme performance profile. Most do not, and it is hidden rather than offered and refused.",
                ["GuiWpfTipZones"] = "How many separately-coloured regions the keyboard backlight has. A board can claim more than it physically has.",
                ["GuiWpfTipRefreshRates"] = "The refresh rates the display reports it can run at.",
                ["GuiWpfTipProbed"] = "Whether the application was able to ask the firmware these questions at startup. When it could not, the answers above are defaults rather than findings.",
                ["GuiWpfTipBiosSetting"] = "A setting as the firmware publishes it. Read-only here: it is changed in the BIOS setup screen.",

                // The Keyboard page.
                ["GuiWpfTipBacklightSwitch"] = "Switches the keyboard backlight on and off. The firmware is not asked what state it is in — the application holds it, because asking gave the wrong answer on this machine.",
                ["GuiWpfTipSwatch"] = "Opens a colour picker for this zone. The colour is applied as soon as it is chosen.",
                ["GuiWpfTipHex"] = "The zone's colour as a hexadecimal value, the same form the command line takes.",
                ["GuiWpfTipKbdStatic"] = "One colour, held. The zones below are what it uses.",
                ["GuiWpfTipKbdTemperature"] = "The backlight follows the hottest sensor: cool through to hot as the machine warms up.",
                ["GuiWpfTipKbdCycle"] = "The backlight moves through the colour wheel continuously.",
                ["GuiWpfTipKbdBreathe"] = "The backlight fades down and back up, in the colours set below.",
                ["GuiWpfTipKbdSpeed"] = "How fast an animated effect runs. Shown only while one is running.",
                ["GuiWpfTipKbdPreset"] = "Applies this saved colour set to the zones. Presets live in the configuration file.",
                ["GuiWpfTipKbdIdle"] = "Switches the backlight off after this long without a keypress. At zero it never switches itself off.",

                // The Log page.
                ["GuiWpfTipPauseSwitch"] = "Stops new entries arriving while you read. Nothing is lost — they appear when it is switched back.",
                ["GuiWpfTipLogExport"] = "Saves everything currently shown to a text file.",
                ["GuiWpfTipLogClear"] = "Empties the list. This does not touch the log file, if one is being written.",
                ["GuiWpfTipFilterProblems"] = "Warnings and errors only — what went wrong.",
                ["GuiWpfTipFilterHardware"] = "What the application asked the machine and what it answered.",
                ["GuiWpfTipFilterInterface"] = "What was done in the window and in the tray menu.",
                ["GuiWpfTipFilterBios"] = "Individual firmware calls. Noisy, and off unless something is being diagnosed.",
                ["GuiWpfTipFilterEc"] = "Individual reads and writes to the Embedded Controller. Noisier still.",
                ["GuiWpfTipLogSearch"] = "Shows only the entries containing this text.",
                ["GuiWpfTipLogList"] = "What the application has done, newest last. An entry with more to say carries it on hover.",

                // The Settings page. The labels say what each control does;
                // these say what it costs, or what it is for.
                ["GuiWpfTipOptimus"] = "The display goes through the integrated graphics and the NVIDIA chip idles when nothing needs it. The default, and the one that lasts on battery.",
                ["GuiWpfTipDiscrete"] = "The display is wired straight to the NVIDIA chip. More performance, noticeably less battery, and it needs a restart.",
                ["GuiWpfTipBoostOff"] = "The processor is held at its base clock. Coolest and quietest, and slowest.",
                ["GuiWpfTipBoostOn"] = "The processor clocks above its base speed when it has the thermal headroom. The normal setting.",
                ["GuiWpfTipBoostAggressive"] = "The processor boosts harder and holds it longer. Hotter, and the fans follow it up.",
                ["GuiWpfTipBrightness"] = "The panel backlight, the same control as the one on the function keys.",
                ["GuiWpfTipThreshold"] = "The temperature at which the fans are forced to maximum. Lower reacts sooner and runs louder.",
                ["GuiWpfTipFanFloor"] = "The lowest level the sliders and the curve will ask for. Above zero keeps the fans turning at all times.",
                ["GuiWpfTipFanAutoDetect"] = "The ceiling only ever rises: seeing the fans run above the recorded ceiling raises it, and nothing lowers it again.",
                ["GuiWpfTipSuspendOnSleep"] = "A program left running across sleep wakes up acting on a temperature taken before the machine slept. This stops it and starts it again on resume.",
                ["GuiWpfTipStartWithWindows"] = "Registers a scheduled task so the application starts elevated at logon. It needs the elevation to reach the hardware at all.",
                ["GuiWpfTipApplyOnStart"] = "Re-applies the saved fan, graphics and keyboard settings at startup instead of leaving whatever the firmware came up with.",
                ["GuiWpfTipCloseExits"] = "With this off the close button hides the window and the application keeps running in the notification area.",
                ["GuiWpfTipStayOnTop"] = "Keeps the window above other windows.",
                ["GuiWpfTipThrottleNotify"] = "Shows a notification when the processor is being held back by heat. At most one every five minutes.",
                ["GuiWpfTipPollGpu"] = "Reading the NVIDIA chip wakes it up. On battery that spends power on figures nothing is watching.",
                ["GuiWpfTipRefreshFollows"] = "Applies the two rates below by itself whenever the power source changes.",
                ["GuiWpfTipRefreshHigh"] = "The rate to use on mains.",
                ["GuiWpfTipRefreshLow"] = "The rate to use on battery.",
                ["GuiWpfTipRefreshAuto"] = "Takes both rates from what the display reports it can do, instead of the values typed above.",
                ["GuiWpfTipHotkeyClear"] = "Removes the shortcut. Nothing is registered until one is set again.",
                ["GuiWpfTipHotkeyCapture"] = "Click, then press the combination you want. A key on its own is refused, and Escape abandons.",
                ["GuiWpfTipKeyToggles"] = "The key starts the fan program, and stops it when pressed again.",
                ["GuiWpfTipKeyCycles"] = "Each press moves on to the next saved program rather than toggling a single one.",
                ["GuiWpfTipKeyShowsFirst"] = "The first press opens the window; from the second press on the key does its usual job.",
                ["GuiWpfTipKeySilent"] = "No notification when the program changes.",
                ["GuiWpfTipKeyRuns"] = "Runs the command below instead. This takes precedence over the fan program.",
                ["GuiWpfTipKeyMinimised"] = "Starts the command with its window minimised.",
                ["GuiWpfTipLogVerbose"] = "Records every exchange with the hardware. What to turn on while diagnosing something, and off afterwards.",
                ["GuiWpfTipLogToFile"] = "Also writes the log to a file beside the executable, so it survives the application closing.",
                ["GuiWpfTipReportBiosErrors"] = "Records the firmware calls this machine refuses. Most machines refuse several and it is not a fault.",
                ["GuiWpfTipLogFileSize"] = "The log file starts again from empty once it reaches this size.",
                ["GuiWpfTipCadenceMonitor"] = "How often the hardware is read while the window is open.",
                ["GuiWpfTipCadenceRecord"] = "How often the hardware is read while the window is hidden. Longer costs less power.",
                ["GuiWpfTipCadenceProgram"] = "How often a running fan program looks at the temperature again and steps the level.",

                // The shell. The tabs carry their own names, so these say what
                // is behind each one rather than repeating the word on it.
                ["GuiWpfTipNavDashboard"] = "The headline readings, the history plot and the fan controls.",
                ["GuiWpfTipNavSensors"] = "Every reading the machine publishes, in full.",
                ["GuiWpfTipNavCooling"] = "The fan curve editor and the saved fan programs.",
                ["GuiWpfTipNavKeyboard"] = "Backlight, colours and effects.",
                ["GuiWpfTipNavSystem"] = "What this machine is, what it supports, and its firmware settings.",
                ["GuiWpfTipNavLog"] = "What the application has been doing and what the hardware answered.",
                ["GuiWpfTipMinimise"] = "Minimise the window.",
                ["GuiWpfTipClose"] = "Close the window. The application keeps running in the notification area unless it is set to exit.",
                ["GuiWpfTipStripCpu"] = "Processor temperature, and its trend over the last minute.",
                ["GuiWpfTipStripGpu"] = "Graphics temperature, and its trend over the last minute.",
                ["GuiWpfTipStripFan"] = "Both fans as a percentage of the hardware maximum.",
                ["GuiWpfTipCardToggle"] = "Show or hide this section.",
                ["GuiWpfTipLegend"] = "Show or hide this series on the plot. The history behind it is still recorded either way.",

                // The Cooling page.
                ["GuiWpfTipCurveReset"] = "Puts the curve back to the shape it starts with.",
                ["GuiWpfTipCurveStop"] = "Stops the curve and hands the fans back to the firmware.",
                ["GuiWpfTipCurveApply"] = "Runs the curve as it is drawn, without saving it as a program.",
                ["GuiWpfTipProgramList"] = "The programs saved in the configuration file. Selecting one draws it on the curve above.",
                ["GuiWpfTipProgramStop"] = "Stops the running program and hands the fans back to the firmware.",
                ["GuiWpfTipProgramName"] = "The name to save the curve under. An existing program of the same name is replaced.",

                // The System page.
                ["GuiWpfTipPowerModeSaver"] = "Windows holds the machine back to make the battery last.",
                ["GuiWpfTipPowerModeBalanced"] = "Windows decides as it goes. The default.",
                ["GuiWpfTipPowerModeHigh"] = "Windows stops holding the machine back, at the cost of power and heat.",
                ["GuiWpfTipCopyReport"] = "Copies the whole report to the clipboard.",
                ["GuiWpfBatCycles"] = "Cycles",
                ["GuiWpfBatCapacity"] = "Capacity",

                // Sensor-row hints, so every reading says what it is on hover
                ["GuiTipCpuTemp"] = "The processor package temperature, read from its own on-die sensor.",
                ["GuiTipCpuLoad"] = "How busy the processor is across all logical cores, as Windows reports it.",
                ["GuiTipCpuPower"] = "The power the processor package is drawing right now, measured through Intel RAPL.",
                ["GuiTipCpuLimit"] = "The power budgets the firmware holds the processor to: the sustained limit (PL1) and the short-burst limit (PL2).",
                ["GuiTipCpuClock"] = "The average frequency the active cores are running at, from the performance counters.",
                ["GuiTipThrottle"] = "Whether the processor is being held back, and by what — heat or a power limit — or not at all.",
                ["GuiTipCores"] = "The hottest logical core and how many there are; the strip on the dashboard shows each one.",
                ["GuiTipGpuTemp"] = "The graphics chip temperature, read through the NVIDIA driver.",
                ["GuiTipGpuLoad"] = "How busy the graphics chip is right now.",
                ["GuiTipGpuPower"] = "The board power the graphics card is drawing, measured through NVML.",
                ["GuiTipGpuLimit"] = "The power cap the driver is enforcing on the card — the live TGP, which the performance profile can raise or lower.",
                ["GuiTipGpuClock"] = "The graphics core clock frequency right now.",
                ["GuiTipVram"] = "Dedicated video memory in use, of the total on the card.",
                ["GuiTipMemLoad"] = "How much of the physical memory is in use.",
                ["GuiTipMemUsed"] = "Physical memory in use, of the total installed.",
                ["GuiTipDisk"] = "The temperature of the drive Windows booted from, read from its NVMe health log.",
                ["GuiTipWifi"] = "The wireless network you are on and its signal strength.",
                ["GuiTipLink"] = "The negotiated wireless link rate: receive, then transmit.",
                ["GuiTipBatHealth"] = "The battery's full-charge capacity against the capacity it was designed for — how much it has worn.",
                ["GuiTipBatCycles"] = "How many full charge cycles the battery has been through.",
                ["GuiTipBatCapacity"] = "The energy the battery holds fully charged, against its designed capacity.",
                ["GuiTipBatRemaining"] = "The estimated time left on the current charge.",
                ["GuiTipBatDraw"] = "How fast the battery is charging or discharging right now.",
                ["GuiTipBatState"] = "Whether the machine is on mains, on battery, or charging.",
                ["GuiTipModel"] = "The machine's marketing name, as it appears on its lid and in a support call.",
                ["GuiTipBios"] = "The firmware version the machine is running.",
                ["GuiTipPlan"] = "The active Windows power plan.",

                // Settings section — hardware controls
                ["GuiWpfSettings"] = "Settings",
                ["GuiWpfSettingsCaption"] = "HARDWARE CONTROLS",
                ["GuiWpfSettingsHint"] = "Controls this machine exposes. A control the firmware does not offer is shown disabled.",
                ["GuiWpfGpuMode"] = "GRAPHICS MODE",
                ["GuiWpfGpuModeHint"] = "Discrete mode wires the display straight to the NVIDIA GPU for more performance, at the cost of battery life. Takes effect after a restart.",
                ["GuiWpfOptimus"] = "Optimus",
                ["GuiWpfDiscrete"] = "Discrete",
                ["GuiWpfBoost"] = "CPU TURBO BOOST",
                ["GuiWpfBoostHint"] = "Lets the processor clock above its base speed. Turn down to run cooler and quieter.",
                ["GuiWpfBoostOff"] = "Off",
                ["GuiWpfBoostOn"] = "On",
                ["GuiWpfBoostAggressive"] = "Aggressive",
                ["GuiWpfBrightness"] = "DISPLAY BRIGHTNESS",
                ["GuiWpfRestartNeeded"] = "Applied — restart to take effect",

                // Capability names, for the About panel's support table
                ["GuiCapKbdBacklight"] = "Keyboard backlight (BIOS)",
                ["GuiCapKbdColor"] = "Keyboard backlight color",
                ["GuiCapGpuModeSwitch"] = "GPU mode switching (MUX)",
                ["GuiCapGpuPower"] = "GPU power level (Custom TGP / PPAB)",
                ["GuiCapAdapter"] = "Smart power adapter status",
                ["GuiCapBornDate"] = "Born-on date",
                ["GuiCapFanSpeed"] = "Fan speed reading (EC)",
                ["GuiCapMaxFan"] = "Maximum fan mode (BIOS)",
                ["GuiCapFanLevel"] = "Fan level control (BIOS)",
                ["GuiCapFanTable"] = "Fan speed table (BIOS)",
                ["GuiCapBiosTemp"] = "BIOS temperature sensor",
                ["GuiCapBiosThrottle"] = "BIOS throttling status",
                ["GuiCapMemOc"] = "Memory overclocking (XMP)",
                ["GuiCapUndervolt"] = "Undervolt support (BIOS)",
                ["GuiCapLedAnim"] = "LED animation table",
                ["GuiCapCpuMsr"] = "CPU temperature (MSR)",
                ["GuiCapCpuRapl"] = "CPU power / clocks (RAPL)",
                ["GuiCapCpuCores"] = "Per-core temperature",
                ["GuiCapCpuBoost"] = "CPU Turbo Boost control",
                ["GuiCapNvapi"] = "NVIDIA GPU monitoring (NVAPI)",
                ["GuiCapNvml"] = "GPU power draw (NVML)",
                ["GuiCapBrightness"] = "Display brightness control",
                ["GuiCapPowerMode"] = "Windows power mode switching",
                ["GuiCapDiskTemp"] = "NVMe drive temperature",
                ["GuiCapWifi"] = "Wi-Fi signal / SSID (when connected)",
                ["GuiCapBatteryHealth"] = "Battery health / charge cycles",
                ["GuiCapZones4"] = "4 zones",
                ["GuiCapZone1"] = "single zone",

                ["UnitFrequency"] = "Hz",
                ["UnitPercent"] = "%",
                ["UnitPower"] = "W",
                ["UnitRotationRate"] = "rpm",
                ["UnitRotationRate_CustomFont"] = Conv.GetChar(Conv.SpecialChar.Prime1) + Conv.GetChar(Conv.SpecialChar.SupMinus) + Conv.GetChar(Conv.SpecialChar.Sup1),
                ["UnitTemperature"] = "°C",
                ["UnitTemperature_CustomFont"] = Conv.GetChar(Conv.SpecialChar.DegreeCelsius),
                ["UnitTimeSecond_CustomFont"] = Conv.GetChar(Conv.SpecialChar.SpacePerEm6) + Conv.GetChar(Conv.SpecialChar.Prime2),

                // XML
                ["_ConfigXmlTemplate"] =
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
                    "<StarMon>" + Environment.NewLine +
                    "    <!-- Automatically generated because no prior configuration file was found." + Environment.NewLine +
                    "         A version annotated with extensive comments is distributed with StarMon.  -->" + Environment.NewLine +
                    "    <Config/>" + Environment.NewLine +
                    "    <Messages>" + Environment.NewLine +
                    "    </Messages>" + Environment.NewLine +
                    "</StarMon>" + Environment.NewLine,

                // Language identifier
                ["_Language"] = "Fallback"

            };

    }

}
