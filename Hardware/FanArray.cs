// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.Hardware.Platform {

#region Interface
    // Defines an interface for interacting with the fan system
    public interface IFanArray {

        public IFan[] Fan { get; }

        // Retrieves or sets the countdown value
        // until automatic settings are restored [s]
        public int GetCountdown();
        public void SetCountdown(int countdown);

        // Retrieves or sets the levels
        // of all fans at the same time
        public byte[] GetLevels();
        public void SetLevels(byte[] levels);

        // Retrieves or sets maximum fan speed
        public bool GetMax();  
        public void SetMax(bool flag);

        // Retrieves or sets manual fan control state
        public bool GetManual();
        public void SetManual(bool flag);

        // Retrieves or sets the current fan mode
        public BiosData.FanMode GetMode();
        public void SetMode(BiosData.FanMode mode);

        // Re-derives every fan's believable-reading bounds from the current
        // fan ceiling, after the firmware has been asked what it is
        public void RefreshConstraints();

        // Retrieves the fan off switch status
        // or switches the fan off
        public bool GetOff();
        public void SetOff(bool flag);


    }
#endregion

#region Implementation
    // Implements a mechanism for interacting with the fan system
    public class FanArray : IFanArray {

        // Fan array
        public IFan[] Fan { get; private set; }

        // Stores the countdown platform component
        protected IPlatformReadWriteComponent Countdown;

        // Stores the manual toggle component
        protected IPlatformReadWriteComponent Manual;

        // Stores the fan mode component
        protected IPlatformReadWriteComponent Mode;

        // Stores the fan on and off switch component
        protected IPlatformReadWriteComponent Switch;

        // Constructs a fan array instance
        public FanArray(
            IFan[] fan,
            IPlatformReadWriteComponent fanCountdown,
            IPlatformReadWriteComponent fanManual,
            IPlatformReadWriteComponent fanMode,
            IPlatformReadWriteComponent fanSwitch) {

            // Initialize the fan array from what the caller actually built.
            // Sizing it to a compiled constant instead left a null entry on
            // any machine with fewer fans than that constant assumes, which
            // every GetLevel/GetRate loop then walked straight into.
            this.Fan = new IFan[fan.Length];
            for(int i = 0; i < fan.Length; i++)
                this.Fan[i] = fan[i];

            // Define the countdown component
            this.Countdown = fanCountdown;

            // Define the mode component
            this.Manual = fanManual;

            // Define the mode component
            this.Mode = fanMode;

            // Define the switch component
            this.Switch = fanSwitch;

        }

        // Re-derives every fan's believable-reading bounds
        public void RefreshConstraints() {
            for(int i = 0; i < this.Fan.Length; i++)
                if(this.Fan[i] != null)
                    this.Fan[i].RefreshConstraints();
        }

        // Retrieves the countdown value [s]
        // until automatic settings are restored
        public int GetCountdown() {
            this.Countdown.Update();
            return this.Countdown.GetValue();
        }

        // Sets the countdown value [s]
        public void SetCountdown(int countdown) {
            this.Countdown.SetValue(countdown);
        }

        // Retrieves the levels of all fans at the same time
        public byte[] GetLevels() {
            try
            {
                return Hw.BiosGet(Hw.Bios.GetFanLevel);
            }
            catch
            {
                return new byte[this.Fan.Length]; // Default zero values
            }
        }

        // Sets the levels of all fans at the same time
        public void SetLevels(byte[] levels) {

            // Set manual fan mode, if needed
            if(Config.FanLevelNeedManual)
                this.SetManual(true);

            // Depending on the configuration setting,
            // use either the BIOS or the EC to set levels
            if(Config.FanLevelUseEc) {

                // Try to set the speed for each fan individually. Bounded by
                // both arrays: a caller passing more levels than there are
                // fans is asking for something that does not exist, and must
                // not walk off the end of the fan array doing so.
                int count = Math.Min(levels.Length, this.Fan.Length);
                for(int i = 0; i < count; i++)
                    this.Fan[i].SetLevel(levels[i]);

            } else {
                try {

                    // Make a WMI BIOS call to set the level of both fans
                    Hw.BiosSet(Hw.Bios.SetFanLevel, levels);

                } catch {

                    // It has been reported on some models the settings
                    // take effect anyway, despite a BIOS error returned

                    // Thus, silently ignore if the call failed

                    // Regardless of the Config.BiosErrorReporting value,
                    // status is always checked, and reported in CLI mode

                }
            }

            // Whatever route the write took, the levels just changed, so the
            // held copy of them is now the state from before the write
            // Qualified: this class has a property called Fan, which would
            // otherwise be what the name resolves to here
            StarMon.Hardware.Platform.Fan.InvalidateLevels();

            // And remember what was asked for, so the next reading can say
            // whether the board took it
            StarMon.Hardware.Platform.Fan.NoteLevelRequest(levels);

        }

        // Retrieves the manual fan speed toggle status.
        //
        // Read from the board, like every other getter here. This one alone
        // reported the last value written, which on a fresh start is none —
        // so manual levels a previous session left in force read as automatic
        // to FanControl.Identify, which calls this the deciding signal.
        public bool GetManual() {

            // Read, not remembered. Every other getter here asks the register
            // first; this one handed back whatever the component last held,
            // which is only ever what this application last wrote — so a
            // reading could not see the toggle as the firmware has it.
            this.Manual.Update();

            return this.Manual.GetValue() == (byte) PlatformData.FanManual.On;
        }

        // Sets the manual fan speed toggle status
        public void SetManual(bool flag) {
            this.Manual.SetValue(flag ?
                (byte) PlatformData.FanManual.On : (byte) PlatformData.FanManual.Off);
        }

        // Retrieves the maximum fan speed status
        public bool GetMax() {
            try
            {
                return Hw.BiosGet<bool>(Hw.Bios.GetMaxFan);
            }
            catch
            {
                return false;
            }
        }

        // Sets the maximum fan speed status
        public void SetMax(bool flag) {
            try { Hw.BiosSet(Hw.Bios.SetMaxFan, flag); } catch { }
        }

        // Retrieves the current fan mode.
        //
        // The mode register does not hold the mode's number on every board.
        // On an HP 8DCF (Victus) it reads 1 for Performance and 0 for both
        // Default and Cool, so cast straight to the enum, Cool read as
        // Default - and everything that compares the mode in force with the
        // one wanted acted on that. So while the register still reads what it
        // read straight after this application set a mode, that mode is the
        // answer. A register that has moved since is the firmware, or someone
        // else, having changed the mode, and is read as it stands.
        public BiosData.FanMode GetMode() {
            this.Mode.Update();
            int value = this.Mode.GetValue();

            ModeWrite written = this.LastModeWrite;
            if(written != null && written.Reads == value)
                return written.Mode;

            return (BiosData.FanMode) value;
        }

        // Sets the current fan mode
        public void SetMode(BiosData.FanMode mode) {
            try {
                Hw.BiosSet<BiosData.FanMode>(Hw.Bios.SetFanMode, mode);
            } catch {
                return;
            }
            // Note: WMI BIOS call preferred over this.Mode.SetValue((byte) mode);

            // What the register reads now the mode has been set. Read twice:
            // a register going to nought is held off once by the component,
            // in case the nought is momentary, and this one is not.
            try {
                this.Mode.Update();
                this.Mode.Update();
                this.LastModeWrite = new ModeWrite(mode, this.Mode.GetValue());
            } catch {
                this.LastModeWrite = null;
            }
        }

        // The last mode set, and what the register read straight after. One
        // object, so a reader on another thread never pairs one mode with the
        // reading of another.
        private sealed class ModeWrite {
            public readonly BiosData.FanMode Mode;
            public readonly int Reads;
            public ModeWrite(BiosData.FanMode mode, int reads) {
                this.Mode = mode;
                this.Reads = reads;
            }
        }

        private volatile ModeWrite LastModeWrite;

        // Retrieves the fan off switch status
        public bool GetOff() {
            this.Switch.Update();
            return ((PlatformData.FanSwitch) this.Switch.GetValue()) == PlatformData.FanSwitch.Off;
        }

        // Switches the fan off or back on
        public void SetOff(bool flag) {
            this.Switch.SetValue(flag ?
                (int) PlatformData.FanSwitch.Off : (int) PlatformData.FanSwitch.On);
        }
#endregion

    }

}
