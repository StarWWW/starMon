// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.Hardware {

    // Works out what this particular Omen or Victus can actually do, once, at
    // startup, and adapts the configuration to it.
    //
    // The application used to carry one machine's answers as its defaults: a
    // fan ceiling of 56, no Extreme mode, fan levels always through the BIOS.
    // Those are true of one board and wrong on the next one. Everything here
    // is asked of the firmware instead, and every answer has a fallback, so a
    // machine that declines to answer keeps the compiled default rather than
    // getting a fabricated one.
    public static class DeviceProfile {

        // The two families this application is for
        public enum DeviceFamily { Unknown, Omen, Victus }

#region Findings
        // Which family the machine belongs to, from its marketing name
        public static DeviceFamily Family { get; private set; }

        // Baseboard product identifier, e.g. "8DCF"
        public static string Board { get; private set; }

        // Fans the firmware reports, clamped to what the platform can address
        public static int FanCount { get; private set; }

        // Highest fan level the firmware itself ever uses. This is the 100 %
        // end of the fan curve and the top of the manual level sliders:
        // writing above it is clamped or ignored, so a curve built against a
        // ceiling that is too high never reaches full speed.
        public static int FanLevelCeiling { get; private set; }

        // Where the ceiling came from, for the capability report
        public static string FanLevelCeilingSource { get; private set; }

        // Whether the firmware admits to software fan control at all
        public static bool SoftwareFanControl { get; private set; }

        // Whether this board has the Extreme performance mode, and whether it
        // is unlocked. Omen models generally do; Victus models generally do
        // not, and showing everyone a mode half of them cannot select is the
        // sort of thing that makes an application feel like someone else's.
        public static bool ExtremeMode { get; private set; }

        // Whether the BIOS fan-level call works here. When it does not, levels
        // have to go through the Embedded Controller, which in turn needs the
        // manual toggle raised first.
        public static bool BiosFanLevel { get; private set; }

        // Keyboard colour zones the firmware reports (0 when it has no colour)
        public static int KbdZones { get; private set; }

        // Highest refresh rate the panel offers [Hz], 0 when it could not be
        // enumerated
        public static int RefreshRateHigh { get; private set; }

        // Lowest refresh rate worth switching down to [Hz]
        public static int RefreshRateLow { get; private set; }

        // Whether the probe has run
        public static bool Probed { get; private set; }
#endregion

        // Guards the probe and the later ceiling revisions
        private static readonly object Lock = new object();

        // Kept so a ceiling revised after startup can push the new bound back
        // into the fans, whose believable-speed limit is derived from it
        private static StarMon.Hardware.Platform.Platform Machine;

        // Bounds a believable fan level ceiling. Levels are hundreds of rpm,
        // so 20 is a fan barely turning and 120 is faster than any laptop fan
        // that has ever shipped — outside that, the firmware is not answering
        // the question that was asked.
        private const int CeilingMin = 25;
        private const int CeilingMax = 120;

        // How many table entries make a curve credible enough to lower a
        // ceiling on. A firmware that returns two rows is not describing its
        // fan curve, it is failing to.
        private const int CredibleTableRows = 6;

        // Probes the machine and applies the findings to the configuration
        public static void Probe(StarMon.Hardware.Platform.Platform platform) {

            lock(Lock) {

                if(Probed)
                    return;
                Probed = true;
                Machine = platform;

                // Start from the compiled defaults, so every finding below is
                // an improvement on them rather than a replacement for them
                Board = "?";
                FanCount = StarMon.Hardware.Platform.PlatformData.FanCount;
                FanLevelCeiling = Config.FanLevelMax;
                FanLevelCeilingSource = "configured";
                BiosFanLevel = true;
                SoftwareFanControl = true;

                IdentifyMachine(platform);
                ProbeSystemFlags(platform);
                ProbeFanPath();
                ProbeFanCount();
                ProbeFanCeiling();
                ProbeKeyboard(platform);
                ProbeRefreshRates();

                Apply();

                Logger.Info("Device", "Hardware profile", Summary());

            }

        }

        // Works out the family and board from what the machine calls itself
        private static void IdentifyMachine(StarMon.Hardware.Platform.Platform platform) {

            if(platform == null)
                return;

            try { Board = platform.System.GetProduct(); } catch { }

            string name = "";
            try {
                using(WmiInfo wmi = new WmiInfo())
                    foreach(var system in wmi.EnumerateInstances("Win32_ComputerSystem"))
                        if(system.TryGetValue("Model", out string model))
                            name = model ?? "";
            } catch { }

            Family = FamilyOf(name);

        }

        // The family a marketing name belongs to. Separated out so it can be
        // tested without a machine attached.
        public static DeviceFamily FamilyOf(string modelName) {
            if(string.IsNullOrEmpty(modelName))
                return DeviceFamily.Unknown;
            if(modelName.IndexOf("OMEN", StringComparison.OrdinalIgnoreCase) >= 0)
                return DeviceFamily.Omen;
            if(modelName.IndexOf("Victus", StringComparison.OrdinalIgnoreCase) >= 0)
                return DeviceFamily.Victus;
            return DeviceFamily.Unknown;
        }

        // Reads the firmware's own support flags
        private static void ProbeSystemFlags(StarMon.Hardware.Platform.Platform platform) {

            if(platform == null)
                return;

            try {

                BiosData.SysSupportFlags flags = platform.System.GetSystemData().SupportFlags;

                SoftwareFanControl = (flags & BiosData.SysSupportFlags.SwFanCtl) != 0;

                // Both bits matter: a board can advertise the mode and still
                // have it locked, in which case selecting it does nothing.
                ExtremeMode =
                    (flags & BiosData.SysSupportFlags.ExtremeMode) != 0
                    || (flags & BiosData.SysSupportFlags.ExtremeModeUnlock) != 0;

            } catch { }

        }

        // Establishes whether fan levels can go through the BIOS here
        private static void ProbeFanPath() {

            // Reading the levels is the same call path as writing them, minus
            // the side effect, so a successful read means the write will land
            try {
                byte[] levels = Hw.BiosGet(Hw.Bios.GetFanLevel);
                BiosFanLevel = levels != null && levels.Length > 0;
            } catch {
                BiosFanLevel = false;
            }

        }

        // Asks the firmware how many fans it has
        private static void ProbeFanCount() {

            try {
                int count = Hw.BiosGet<byte>(Hw.Bios.GetFanCount);
                if(count >= 1 && count <= StarMon.Hardware.Platform.PlatformData.FanCount)
                    FanCount = count;
            } catch { }

        }

        // Works out the highest fan level this firmware uses
        private static void ProbeFanCeiling() {

            try {

                BiosData.FanTable table = Hw.BiosGetStruct<BiosData.FanTable>(Hw.Bios.GetFanTable);
                if(table.Level == null)
                    return;

                int top = 0;
                foreach(BiosData.FanLevel level in table.Level) {
                    if(level.Fan1Level > top) top = level.Fan1Level;
                    if(level.Fan2Level > top) top = level.Fan2Level;
                }

                if(top < CeilingMin || top > CeilingMax)
                    return;

                // A table that reaches higher than the configured ceiling is
                // proof the ceiling is too low, whatever the table's length:
                // the firmware is demonstrably driving the fans up there.
                if(top > FanLevelCeiling) {
                    FanLevelCeiling = top;
                    FanLevelCeilingSource = "fan table";
                    return;
                }

                // Lowering is the riskier direction — it costs the user the
                // top of their range — so it takes a table long enough to be
                // an actual curve rather than a stub answer.
                if(top < FanLevelCeiling && table.Level.Length >= CredibleTableRows) {
                    FanLevelCeiling = top;
                    FanLevelCeilingSource = "fan table";
                }

            } catch { }

        }

        // Reads the keyboard's colour-zone count
        private static void ProbeKeyboard(StarMon.Hardware.Platform.Platform platform) {

            if(platform == null)
                return;

            try { KbdZones = platform.System.GetKbdZoneCount(); } catch { }

        }

        // Enumerates the panel's refresh rates, so the high/low presets are
        // this machine's rates rather than one machine's rates
        private static void ProbeRefreshRates() {

            try {

                List<int> rates = Os.GetRefreshRates();
                if(rates == null || rates.Count == 0)
                    return;

                rates.Sort();

                RefreshRateLow = rates[0];
                RefreshRateHigh = rates[rates.Count - 1];

            } catch { }

        }

        // Applies the findings to the live configuration
        private static void Apply() {

            if(Config.FanLevelAutoDetect) {

                Config.FanLevelMax = FanLevelCeiling;

                // The Embedded Controller path needs the manual toggle raised
                // before a level write is honoured; the BIOS path does not.
                Config.FanLevelUseEc = !BiosFanLevel;
                Config.FanLevelNeedManual = !BiosFanLevel;

            } else {

                // Auto-detection off: the configured ceiling stands, and the
                // profile reports what the machine was actually asked for
                FanLevelCeiling = Config.FanLevelMax;
                FanLevelCeilingSource = "configured (auto-detect off)";

            }

            if(Config.RefreshRateAutoDetect && RefreshRateHigh > 0) {
                Config.PresetRefreshRateHigh = RefreshRateHigh;
                if(RefreshRateLow > 0 && RefreshRateLow < RefreshRateHigh)
                    Config.PresetRefreshRateLow = RefreshRateLow;
            }

            PushCeilingToFans();

        }

        // Hands the current ceiling to the fans, whose believable-speed bound
        // is derived from it. They were built before the firmware was asked,
        // so without this every reading above the compiled default is thrown
        // away as implausible on exactly the machines the probe was for.
        private static void PushCeilingToFans() {
            try {
                if(Machine != null && Machine.Fans != null)
                    Machine.Fans.RefreshConstraints();
            } catch { }
        }

        // Revises the ceiling upwards when a fan is seen running past it.
        //
        // Some boards describe a conservative curve in their fan table and
        // then exceed it in practice, so a ceiling that only ever asked would
        // cap the fan curve and the manual sliders below what the hardware
        // does on its own. This is what corrects that, within seconds of the
        // fans first spinning up.
        //
        // Any level above the ceiling is proof, whatever mode the machine is
        // in — the figure comes from the firmware's own fan-level call, which
        // either answers or throws, and every level this application writes is
        // clamped to the ceiling before it goes out. So a reading above the
        // ceiling cannot be one of ours coming back, and cannot be a misread
        // of a busy bus either. Only the direction is restricted: the ceiling
        // never falls here, because a fan running slowly says nothing about
        // how fast it can run.
        public static void Observe(int level, bool firmwareAtMaximum) {

            if(!Config.FanLevelAutoDetect || !Probed)
                return;

            if(level <= FanLevelCeiling || level > CeilingMax)
                return;

            lock(Lock) {

                if(level <= FanLevelCeiling)
                    return;

                int was = FanLevelCeiling;
                FanLevelCeiling = level;
                FanLevelCeilingSource = firmwareAtMaximum
                    ? "observed at maximum" : "observed running";
                Config.FanLevelMax = level;

                PushCeilingToFans();

                Logger.Info("Device", "Fan ceiling raised",
                    was + " to " + level + " (a fan was running there)");

            }

        }

        // The fan modes this machine actually offers, in the order they should
        // be shown. Everything before Extreme is common to every Omen and
        // Victus thermal policy; Extreme is the one that varies.
        public static List<BiosData.FanMode> SupportedFanModes() {

            List<BiosData.FanMode> modes = new List<BiosData.FanMode> {
                BiosData.FanMode.Default,
                BiosData.FanMode.Performance,
                BiosData.FanMode.Cool,
                BiosData.FanMode.Quiet
            };

            // Victus boards that report the flag are the exception rather than
            // the rule, and on the ones that do not, Extreme is either refused
            // or quietly does less than Performance. Only offer it where the
            // firmware says it is there.
            if(ExtremeMode)
                modes.Add(BiosData.FanMode.Extreme);

            return modes;

        }

        // A one-line summary for the log
        public static string Summary() {

            return (Family == DeviceFamily.Unknown ? "HP" : Family.ToString())
                + " " + Board
                + " · " + FanCount + " fan" + (FanCount == 1 ? "" : "s")
                + " · ceiling " + FanLevelCeiling + " (" + FanLevelCeilingSource + ")"
                + " · levels via " + (BiosFanLevel ? "BIOS" : "EC")
                + " · extreme " + (ExtremeMode ? "yes" : "no")
                + " · kbd " + (KbdZones == 0 ? "no colour" : KbdZones + " zone" + (KbdZones == 1 ? "" : "s"))
                + (RefreshRateHigh > 0 ? " · panel " + RefreshRateLow + "/" + RefreshRateHigh + " Hz" : "");

        }

        // Resets the probe, for the self-tests
        internal static void Reset() {
            lock(Lock) {
                Probed = false;
                Machine = null;
                Family = DeviceFamily.Unknown;
                Board = null;
                FanCount = 0;
                FanLevelCeiling = 0;
                FanLevelCeilingSource = null;
                SoftwareFanControl = false;
                ExtremeMode = false;
                BiosFanLevel = false;
                KbdZones = 0;
                RefreshRateHigh = 0;
                RefreshRateLow = 0;
            }
        }

    }

}
