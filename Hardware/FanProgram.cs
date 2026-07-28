// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon.Hardware.Platform {

#region Data
    // Stores fan program data
    public class FanProgramData {

        // Fan mode to maintain while the program is running
        public BiosData.FanMode FanMode;

        // GPU power level to set when the program starts
        public BiosData.GpuPowerLevel GpuPower;

        // Stores the mapping of temperature thresholds
        // to fan levels for each fan
        public SortedDictionary<byte, byte[]> Level;

        // Fan program name
        public string Name;

        // Constructs a fan program data instance
        public FanProgramData(
            string name,
            BiosData.FanMode fanMode,
            BiosData.GpuPowerLevel gpuPower,
            SortedDictionary<byte, byte[]> level) {

            this.Name = name;
            this.FanMode = fanMode;
            this.GpuPower = gpuPower;
            this.Level = level;

        }

    }
#endregion

    // Stores fan program logic
    public class FanProgram {

#region Variables & Initialization
        // Status callback importance rating
        public enum Severity {
            Verbose,    // Only for reference
            Notice,     // Might be shown if possible
            Important   // Need to be shown to the user
        }

        // Callback method for status updates
        private Action<FanProgram.Severity, string> Callback;

        // GPU power data for the current program
        private BiosData.GpuPowerData GpuPowerData;

        // Cached program data for the current program
        private FanProgramData ProgramData;

        // State flags
        public bool IsAlternate { get; private set; }
        public bool IsEnabled { get; private set; }
        public bool IsSuspended { get; private set; }

        // Last fan mode and GPU power data before the program started
        private BiosData.FanMode LastFanMode;
        private BiosData.GpuPowerData LastGpuPowerData;

        // Level list for the current program
        // to facilitate threshold look-ups
        private List<byte> Levels;

        // The level currently held, so that stepping back down can be held off
        // until the temperature has fallen clear of the threshold it crossed
        // on the way up (255 means no level has been picked yet)
        private byte HeldLevel = HeldNone;

        // Name of the last running program
        private string Name;

        // Parent class reference
        private Platform Platform;

        // Constructs a fan program instance
        public FanProgram(
            Platform platform,
            Action<FanProgram.Severity, string> callback) {

            this.Callback = callback;
            this.GpuPowerData = default(BiosData.GpuPowerData);
            this.IsAlternate = false;
            this.IsEnabled = false;
            this.IsSuspended = false;
            this.LastFanMode = BiosData.FanMode.Default;
            this.LastGpuPowerData = default(BiosData.GpuPowerData);
            this.Levels = new List<byte>();
            this.Name = "";
            this.ProgramData = null;
            this.Platform = platform;

        }
#endregion

#region Public Methods
        // Retrieves the name of the current fan program
        public string GetName() {

            return this.Name;

        }

        // Re-enable fan program
        // following a resume from suspend
        public bool Resume() {

            // Fail if no program active
            // or if not suspended
            if(!this.IsEnabled || !this.IsSuspended)
                return false;

            // Set the state flag
            this.IsSuspended = false;

            // Update the program
            Update();

            // Report success
            return true;

        }

        // Starts a fan program given its name
        public bool Run(string name, bool isAlternate = false) {

            // Note: no need to terminate
            // the previous program first, if any

            // Try to set up the new program
            // bail out if not succesful
            if(!Setup(name))
                return false;

            // Enable manual fan mode
            if(Config.FanLevelNeedManual)
                Platform.Fans.SetManual(true);

            // Set the alternate flag
            this.IsAlternate = isAlternate;

            // Set the state flag
            this.IsEnabled = true;

            // Save the last fan mode
            this.LastFanMode = Platform.Fans.GetMode();

            // Save the last GPU power state
            this.LastGpuPowerData = Platform.System.GetGpuPower();

            // Update the program
            Update();

            // Report success
            return true;

        }

        // Suspend the running fan program
        public bool Suspend() {

            // Fail if no program active
            // or if already suspended
            if(!this.IsEnabled || this.IsSuspended)
                return false;

            // Set the state flag
            this.IsSuspended = true;

            // Reset fan speed
            SetFanLevel(new byte[] { Byte.MaxValue, Byte.MaxValue } );

            // Disable manual fan mode
            if(Config.FanLevelNeedManual)
                Platform.Fans.SetManual(false);

            // Restore the previous fan mode
            UpdateFanMode(true, this.LastFanMode);

            // Restore the previous GPU power settings
            UpdateGpuPower(true, this.LastGpuPowerData);

            // Report success
            return true;

        }

        // Terminates the running fan program, if any is running
        public bool Terminate() {

            // Fail if no program active
            if(!this.IsEnabled)
                return false;

            // Reset fan speed
            SetFanLevel(new byte[] { Byte.MaxValue, Byte.MaxValue } );

            // Disable manual fan mode
            if(Config.FanLevelNeedManual)
                Platform.Fans.SetManual(false);

            // Restore the previous fan mode
            UpdateFanMode(true, this.LastFanMode);

            // Restore the previous GPU power settings
            UpdateGpuPower(true, this.LastGpuPowerData);

            // Set the state flags
            this.IsAlternate = false;
            this.IsEnabled = false;
            this.IsSuspended = false;

            // Reset data
            Reset();

            // Update the status
            Status(Severity.Notice, Config.Locale.Get(Config.L_PROG + "End"));

            // Report success
            return true;

        }

        // Updates the fan program, if any is running
        public bool Update() {

            // Fail if no program active
            // or program is suspended
            if(!this.IsEnabled || this.IsSuspended)
                return false;

            // Find out the current maximum temperature,
            // the temperature level for the given temperature,
            // and the target fan levels for the given level
            byte temperature = Platform.GetMaxTemperature(true);
            byte level = GetTemperatureLevel(temperature);
            byte[] fans = GetFanLevel(level);

            // Note: the above could all be accomplished with
            // a single nested call, except we also want to report
            Status(Severity.Notice,
                Config.Locale.Get(Config.L_PROG + "T") + Config.Locale.Get(Config.L_PROG + "SubMax") + " " 
                + Conv.GetString(temperature, 2, 10) + Config.Locale.Get(Config.L_UNIT + "Temperature") + " "
                + Config.Locale.Get(Config.L_PROG + "Lvl") + " " + Conv.GetString(level, 2, 10) + " "
                + Config.Locale.Get(Config.L_PROG + "Fans") + " "
                + Conv.GetString(fans[0], 2, 10) + ", " + Conv.GetString(fans[1], 2, 10));

            // Set fan levels
            SetFanLevel(fans);

            // Perform other updates, only if necessary
            // or, in case of the fan mode, configured to do so
            // without checking, so as to reduce the EC burden
            UpdateFanMode(!Config.FanProgramModeCheckFirst);
            UpdateGpuPower();

            // Fan-mode setting resets the countdown,
            // thus no need to update in such case
            if(Config.FanProgramModeCheckFirst)
                UpdateCountdown();

            // Report success
            return true;

        }
#endregion

#region Private Methods
        // Obtains the fan levels for the given temperature level
        private byte[] GetFanLevel(byte level) {

            // Retrieve the value from the configuration data
            return this.ProgramData.Level[level];

        }

        // Obtains the program level for the given temperature
        private byte GetTemperatureLevel(byte temperature) {

            byte held;
            byte level = StepLevel(
                this.Levels, this.HeldLevel, temperature,
                Config.FanProgramHysteresisC, out held);

            this.HeldLevel = held;
            return level;

        }

        // Decides which program level a temperature should map to, given the
        // level currently held. Kept static and free of any hardware or
        // configuration state so the stepping rules can be exercised directly.
        //
        // HeldNone (255) means nothing is held yet. The updated hold is
        // reported through newHeld, and is always the same as the return value.
        internal const byte HeldNone = Byte.MaxValue;

        internal static byte StepLevel(
            List<byte> levels, byte held, byte temperature,
            int margin, out byte newHeld) {

            byte target = LookUpLevel(levels, temperature);

            // Stepping up is immediate: a rising temperature always gets the
            // cooling it asks for straight away
            if(held == HeldNone || target >= held)
                return newHeld = target;

            // Stepping down waits until the temperature has dropped a margin
            // below the threshold that was crossed on the way up. Without this,
            // a temperature hovering on a boundary flips between two levels on
            // every update, and the fans audibly surge back and forth.
            if(temperature + margin > held)
                return newHeld = held;

            // Far enough below: drop to the level the lower temperature maps
            // to, recomputed with the margin applied so a large fall crosses
            // several thresholds at once rather than one per update
            byte lowered = LookUpLevel(
                levels, (byte) Math.Max(0, temperature - margin));

            return newHeld = lowered < target ? lowered : target;

        }

        // Maps a temperature onto the highest program level at or below it
        internal static byte LookUpLevel(List<byte> levels, byte temperature) {
            int value;

            // Binary-search the level list
            // If the result is non-negative, an index was found
            if((value = levels.BinarySearch(temperature)) >= 0)

                // Return the item at the index directly
                return levels[value];

            // The result is a bitwise complement of the next larger item index,
            // or the index of the last element of the list if no larger item exists
            else {

                // Return the item at the binary complement index less one;
                // a temperature below the lowest defined level would yield -1,
                // so clamp to the first entry instead of crashing
                int index = ~value - 1;
                return levels[index < 0 ? 0 : index];

            }

        }

        // Resets the state data when terminating a program
        private void Reset() {

            // Clear the GPU power data
            this.GpuPowerData = default(BiosData.GpuPowerData);

            // Clear the cached program data
            this.ProgramData = null;

            // Clear the last fan mode
            this.LastFanMode = BiosData.FanMode.Default;

            // Note: do not clear the last program name since leaving it
            // is harmless, and also can be used to show a more meaningful
            // status message using GetName() even after the program ended

            // Clear the level keys
            if(this.Levels != null)
                this.Levels.Clear();
            else
                this.Levels = new List<byte>();

            // Forget the held level, so the next program starts unbiased
            this.HeldLevel = HeldNone;

        }

        // Set the fan levels to the given parameter
        private void SetFanLevel(byte[] level) {

            // Set the fan levels
            this.Platform.Fans.SetLevels(level);

            // Note: this does not currently handle the case when both fans are being set to 0
            // since this is not allowed by the hardware through this call, and would depend
            // on calling Fans.SetOff(true) instead; not implemented due to possible long-term
            // implications of the fans being switched off for extended periods of time

        }

        // Sets up a new fan program to be run
        private bool Setup(string name) {

            // Bail out if referring to a non-existent program
            if(!Config.FanProgram.ContainsKey(name))
                return false;

            // Set up the program name
            this.Name = name;

            // Cache the program data
            this.ProgramData = Config.FanProgram[this.Name];

            // Set up the level keys
            this.Levels = new List<byte>(this.ProgramData.Level.Keys);

            // A new program's thresholds have nothing to do with the previous
            // one's, so the held level does not carry over
            this.HeldLevel = HeldNone;

            // Set up the target GPU power data
            this.GpuPowerData = new BiosData.GpuPowerData(this.ProgramData.GpuPower);

            // Report success
            return true;

        }

        // Reports fan program status
        private void Status(Severity severity, string message) {

            // Report status via the callback method
            Callback(severity, message);

        }

        // Updates the fan manual mode countdown, optionally only if necessary
        public void UpdateCountdown(bool forceUpdate = false, bool skipZero = false) {
            int countdown = 1;

            // Avoid unnecessarily querying the countdown,
            // if an update is being forced regardless
            if(!forceUpdate)
                countdown = this.Platform.Fans.GetCountdown();

            // Optionally, do not update if no countdown
            if(skipZero && countdown == 0)
                return;

            // Skip if there still is enough time to do it
            // during the next update, unless forced not to
            if(forceUpdate
                || countdown
                    < (Config.UpdateProgramInterval
                        + Config.FanCountdownExtendThreshold))

               // Set the fan countdown
               this.Platform.Fans.SetCountdown(Config.FanCountdownExtendInterval);

        }

        // Updates the fan mode, optionally only if different than the desired target state
        private void UpdateFanMode(
            bool forceUpdate = false,
            BiosData.FanMode? mode = null) {

            // Set the default mode if empty
            if(mode == null)
                mode = this.ProgramData.FanMode;

            // Skip if the settings are the same already, unless forced not to
            if(forceUpdate
                || this.Platform.Fans.GetMode() != mode)

                // Set the fan mode
                this.Platform.Fans.SetMode((BiosData.FanMode) mode);

        }

        // Updates the GPU power, optionally only if different than the desired target state
        private void UpdateGpuPower(
            bool forceUpdate = false,
            BiosData.GpuPowerData? power = null) {

            // Set the default GPU power data if empty
            if(power == null)
                power = this.GpuPowerData;

            // Skip if the settings are the same already, unless forced not to
            if(forceUpdate
                || this.Platform.System.GetGpuCustomTgp() != this.GpuPowerData.CustomTgp
                || this.Platform.System.GetGpuPpab() != this.GpuPowerData.Ppab)

                // Set the GPU power
                this.Platform.System.SetGpuPower((BiosData.GpuPowerData) power);

        }

#endregion
    }

}
