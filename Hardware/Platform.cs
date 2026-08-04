// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.Hardware.Platform {

    // Manages the hardware sensors
    public class Platform {

#region Data
        // Last maximum temperature reading
        public byte LastMaxTemperature { get; private set; }

        // Timestamp of the last maximum temperature update (Environment.TickCount)
        private int LastMaxTemperatureTimestamp;

        // Flag indicating whether a cached temperature is available
        private bool HasLastMaxTemperature;

        // System information
        public ISettings System { get; private set; }

        // Fan sensors and controls
        public IFanArray Fans { get; private set; }

        // Desired fan mode to keep alive (if set via GUI)
        private BiosData.FanMode? DesiredFanMode;
        private int DesiredFanModeTimestamp;

        // Indicates whether a desired fan mode is set
        public bool HasDesiredFanMode { get { return this.DesiredFanMode.HasValue; } }

        // Temperature sensor array and which of these values are used
        public IPlatformReadComponent[] Temperature { get; private set; }
        public bool[] TemperatureUse { get; private set; }

        // Which sensors have gone quiet — see UpdateTemperature below
        public bool[] TemperatureDormant { get; private set; }

        // Consecutive updates each sensor has failed to produce a value for
        private int[] TemperatureMisses;

        // Counts updates, so dormant sensors can still be retried now and then
        private int TemperatureTick;

        // How many consecutive fruitless updates make a sensor dormant. The
        // configured sensor list is the union of every register any Omen or
        // Victus board has been seen to carry, and no single board carries
        // them all: on any given machine some of those registers are simply
        // not there. Polling them anyway costs an Embedded Controller
        // exchange per sensor per second for a number that never arrives.
        private const int DormantAfter = 30;

        // How often a dormant sensor is tried again, in updates. A sensor can
        // be quiet because the part behind it is powered down rather than
        // absent, so dormancy has to be a reduced rate, not a verdict.
        private const int DormantRetryEvery = 60;

        // Cached list of temperature indices used for max temperature
        private int[] TemperatureUseIndices;
#endregion

#region Initialization
        // Initializes the class
        public Platform() {

            // Initialize the system settings
            InitSystem();

            // Initialize the fan controls
            InitFans();

            // Initialize the temperature controls
            InitTemperature();

            // Initialize desired fan mode state
            this.DesiredFanMode = null;
            this.DesiredFanModeTimestamp = 0;

        }

        // Sets the desired fan mode and applies it immediately
        public void SetFanModeSticky(BiosData.FanMode mode) {
            this.DesiredFanMode = mode;
            this.DesiredFanModeTimestamp = Environment.TickCount;
            this.Fans.SetMode(mode);
        }

        // Clears any desired fan mode
        public void ClearFanModeSticky() {
            this.DesiredFanMode = null;
        }

        // The graphics power level the user asked for, and when it was last
        // asserted.
        //
        // The same treatment the fan mode gets, for the same reason and with
        // the same firmware behind it: the profile the chassis holds resets on
        // its own schedule, and a TGP asked for once slid back to the base
        // draw a while later with nothing to notice. It had no keep-alive at
        // all outside a running fan program, which is the one place it was
        // being re-applied — so the setting worked while a program ran and
        // quietly expired when one did not.
        private BiosData.GpuPowerLevel? DesiredGpuPower;
        private int DesiredGpuPowerTimestamp;

        public bool HasDesiredGpuPower { get { return this.DesiredGpuPower.HasValue; } }

        // Sets the desired graphics power level and applies it immediately
        public void SetGpuPowerSticky(BiosData.GpuPowerLevel level) {
            this.DesiredGpuPower = level;
            this.DesiredGpuPowerTimestamp = Environment.TickCount;
            this.System.SetGpuPower(new BiosData.GpuPowerData(level));
        }

        public void ClearGpuPowerSticky() {
            this.DesiredGpuPower = null;
        }

        // Re-applies the desired graphics power level if it is time
        public void MaintainGpuPowerSticky() {

            if(!this.DesiredGpuPower.HasValue)
                return;

            int now = Environment.TickCount;

            if(unchecked(now - this.DesiredGpuPowerTimestamp) < Config.FanModeKeepAliveMs)
                return;

            // Written blind rather than compared first: this board refuses the
            // read (GpuPowerSupported) while accepting the write, so there is
            // nothing to compare against on the machine that needs it most
            this.System.SetGpuPower(new BiosData.GpuPowerData(this.DesiredGpuPower.Value));
            this.DesiredGpuPowerTimestamp = now;

        }

        // Re-applies the desired fan mode if needed
        public void MaintainFanModeSticky() {
            if(!this.DesiredFanMode.HasValue)
                return;

            int now = Environment.TickCount;
            bool shouldReapply = unchecked(now - this.DesiredFanModeTimestamp) >= Config.FanModeKeepAliveMs;

            if(shouldReapply || this.Fans.GetMode() != this.DesiredFanMode.Value) {
                this.Fans.SetMode(this.DesiredFanMode.Value);
                this.DesiredFanModeTimestamp = now;
            }
        }

        // Initializes the fan controls
        private void InitFans() {

            // The register set is the one HP's Omen and Victus firmware has
            // shared across every generation this application has seen:
            // SRP/XGS/XSS/RPM per fan, plus the countdown, manual, mode and
            // switch registers. It lives in PlatformData.Fan as a table.
            //
            // How many of those entries are used is the firmware's answer, not
            // this file's. It used to build exactly two fans here, always —
            // while DeviceProfile asked the firmware how many there were and
            // nothing consumed the answer, and while the probe itself ran
            // *after* this did. A board with one fan got a second one that
            // read nought per cent forever and cost a round trip per tick; a
            // board with three had the third clipped off.
            this.Fans = new FanArray(
                BuildFans(),

                // Define the countdown component
                new EcComponent(
                    (byte) EmbeddedControllerData.Register.XFCD,
                    PlatformData.AccessType.Read | PlatformData.AccessType.Write),

                // Define the manual toggle component
                new EcComponent(
                    (byte) EmbeddedControllerData.Register.OMCC,
                    PlatformData.AccessType.Read | PlatformData.AccessType.Write),

                // Define the mode component
                new EcComponent(
                    (byte) EmbeddedControllerData.Register.HPCM,
                    PlatformData.AccessType.Read | PlatformData.AccessType.Write),

                // Define the switch component
                new EcComponent(
                    (byte) EmbeddedControllerData.Register.SFAN,
                    PlatformData.AccessType.Read | PlatformData.AccessType.Write));

        }

        // One fan per fan the firmware says this board has.
        //
        // The count is clamped to what the register table describes, and to at
        // least one: a board that answers nothing still has a fan, and showing
        // none of them would be a worse answer than showing one.
        private static IFan[] BuildFans() {

            int count = DeviceProfile.FanCount;

            if(count < 1)
                count = 1;
            if(count > PlatformData.MaxFanCount)
                count = PlatformData.MaxFanCount;

            IFan[] fans = new IFan[count];

            for(int i = 0; i < count; i++) {

                PlatformData.FanRegisters registers = PlatformData.Fan[i];

                fans[i] = new Fan(
                    registers.Type,
                    Rw(registers.Setpoint),
                    Ro(registers.Rate),
                    Wo(registers.Set),
                    Ro(registers.Speed, PlatformData.DataSize.Word),
                    i);

            }

            return fans;

        }

        // A component for a register, or null where this build has no register
        // for that part of a fan. Fan tolerates the null and reaches the fan
        // through the firmware instead.
        private static EcComponent Ro(EmbeddedControllerData.Register? register,
            PlatformData.DataSize size = PlatformData.DataSize.Byte) {

            return register.HasValue
                ? new EcComponent((byte) register.Value,
                    PlatformData.AccessType.Read, size)
                : null;

        }

        private static EcComponent Wo(EmbeddedControllerData.Register? register) {
            return register.HasValue
                ? new EcComponent((byte) register.Value, PlatformData.AccessType.Write)
                : null;
        }

        private static EcComponent Rw(EmbeddedControllerData.Register? register) {
            return register.HasValue
                ? new EcComponent((byte) register.Value,
                    PlatformData.AccessType.Read | PlatformData.AccessType.Write)
                : null;
        }

        // Initializes the system settings
        private void InitSystem() {
            this.System = new Settings();
        }

        // Initializes the temperature controls
        private void InitTemperature() {

            // Set up the temperature sensor array based on the configuration data
            this.Temperature = new IPlatformReadComponent[Config.TemperatureSensor.Count];
            this.TemperatureUse = new bool[Config.TemperatureSensor.Count];
            this.TemperatureDormant = new bool[Config.TemperatureSensor.Count];
            this.TemperatureMisses = new int[Config.TemperatureSensor.Count];

            // Populate the temperature sensor array
            int i = 0;
            var usedIndices = new List<int>(Config.TemperatureSensor.Count);
            foreach(string name in Config.TemperatureSensor.Keys) {

                // Set whether the sensor can be used for maximum temperature
                this.TemperatureUse[i] = Config.TemperatureSensor[name].Use;
                if(this.TemperatureUse[i])
                    usedIndices.Add(i);

                // Replace the Embedded Controller CPU sensor with the more accurate
                // processor-native reading (MSR / SMU) whenever it is supported
                if(name == "CPUT"
                    && Config.TemperatureSensor[name].Source == PlatformData.LinkType.EmbeddedController
                    && StarMon.Hardware.Cpu.CpuTemperature.IsAvailable) {

                    this.Temperature[i++] = new MsrCpuTemperatureComponent();
                    continue;

                }

                // Replace the Embedded Controller GPU sensor with the NVIDIA
                // driver's own reading whenever a card is present. The board
                // GPTM register (0xB7) is both less accurate and unreliable on
                // Optimus laptops — it fails whenever the discrete GPU has
                // powered down — so polling it just fills the log with
                // failures for a redundant sensor.
                if(name == "GPTM"
                    && Config.TemperatureSensor[name].Source == PlatformData.LinkType.EmbeddedController
                    && StarMon.Hardware.GpuNvidia.IsAvailable) {

                    this.Temperature[i++] = new NvapiGpuTemperatureComponent();
                    continue;

                }

                // Process each sensor loaded from the configuration
                switch(Config.TemperatureSensor[name].Source) {

                    // Add an Embedded Controller sensor
                    case PlatformData.LinkType.EmbeddedController:
                        this.Temperature[i++] = new EcComponent(
                            Config.TemperatureSensor[name].Register,
                            Config.MaxBelievableTemperature);
                        break;

                    // Add a WMI BIOS sensor
                    case PlatformData.LinkType.WmiBios:
                        this.Temperature[i++] =
                            new WmiBiosTemperatureComponent(Config.MaxBelievableTemperature);
                        break;

                }

            }

            // Cache the used indices for faster updates
            this.TemperatureUseIndices = usedIndices.ToArray();

        }
#endregion

#region Information Retrieval
        // Obtains the maximum value from the platform temperature array
        public byte GetMaxTemperature(bool forceUpdate = false) {

            // Return cached value if within cache window
            if(!forceUpdate
                && Config.TemperatureCacheMs > 0
                && this.HasLastMaxTemperature) {

                int now = Environment.TickCount;
                if(unchecked(now - this.LastMaxTemperatureTimestamp) <= Config.TemperatureCacheMs)
                    return this.LastMaxTemperature;
            }

            // Update the platform temperature readings first
            // if forced to do so
            if(forceUpdate)
                UpdateTemperature(true);

            // Quick return if there are no active sensors
            if(this.TemperatureUseIndices.Length == 0) {
                this.LastMaxTemperature = 0;
                return 0;
            }

            // The running maximum is built up in a local, and only published
            // once it is final. LastMaxTemperature is read from other threads
            // (the thermal guard and the manual-fan safety check), and a
            // partially-computed value there reads as a spurious drop, which
            // those callers would act on.
            byte max = 0;
            byte value;

            // Iterate through the used temperature sensors
            for(int i = 0; i < this.TemperatureUseIndices.Length; i++) {
                int idx = this.TemperatureUseIndices[i];

                // Obtain the reading from each temperature sensor
                // If the value is higher than the current candidate
                if((value = (byte) this.Temperature[idx].GetValue()) > max)

                    // Update the candidate
                    max = value;
            }

            // The sensors the board does not expose as Embedded Controller
            // registers.
            //
            // This walked the EC/MSR/NVAPI array only, so the thermal guard —
            // the thing that decides when to force the fans to maximum to
            // protect the machine — was blind to two whole sources the
            // application already reads and already shows on the sensors page:
            // the temperatures the firmware publishes through its own WMI
            // class, and the ACPI thermal zones. Both have had a
            // GetMaxTemperature of their own since they were written, and
            // neither had a caller.
            //
            // A board whose hottest point is published only through one of
            // those was being protected by a maximum that could not see it.
            // Both are cached reads, so this costs nothing per call.
            max = Higher(max, SafeMax(HpSensors.GetMaxTemperature));
            max = Higher(max, SafeMax(AcpiThermal.GetMaxTemperature));

            // Publish the result
            this.LastMaxTemperature = max;
            this.HasLastMaxTemperature = true;
            this.LastMaxTemperatureTimestamp = Environment.TickCount;
            return max;

        }
        // The higher of two readings, guarding the implausible.
        //
        // A source that is not answering reports zero or something absurd, and
        // either would be taken as the machine's hottest point if it were
        // simply compared. The ceiling is the same one the sensor components
        // are constrained to.
        private static byte Higher(byte current, int candidate) {

            return candidate > current && candidate <= Config.MaxBelievableTemperature
                ? (byte) candidate : current;

        }

        // A source that throws is a source with nothing to say, not a reason
        // to stop working out the maximum: this figure drives the thermal
        // guard, and abandoning it because one probe failed is the one outcome
        // worse than a slightly low reading.
        private static int SafeMax(Func<int> read) {

            try {
                return read();
            } catch {
                return 0;
            }

        }
#endregion

#region Updates
        // Updates everything
        public void UpdateAll() {
            UpdateFans();
            UpdateSystem();
            UpdateTemperature();
        }

        // Updates the fan readings
        public void UpdateFans() {
            // Fan readings updated at retrieval time
        }

        // Updates the system settings
        public void UpdateSystem() {
            // System settings updated either only once
            // during initialization, or at retrieval time
        }

        // Updates the temperature readings
        public void UpdateTemperature(bool onlyUsed = false) {

            this.TemperatureTick++;

            if(onlyUsed) {
                for(int i = 0; i < this.TemperatureUseIndices.Length; i++)
                    UpdateSensor(this.TemperatureUseIndices[i]);
            } else {
                for(int i = 0; i < this.Temperature.Length; i++)
                    UpdateSensor(i);
            }

        }

        // Updates one sensor, and keeps track of whether it is answering.
        //
        // A register a board does not carry fails every read, forever. Left
        // alone that is one wasted Embedded Controller exchange per second per
        // absent sensor, on a bus the fan and temperature readings share. So a
        // sensor that produces nothing for long enough is stood down to an
        // occasional retry, and comes straight back the moment it answers.
        private void UpdateSensor(int index) {

            if(this.TemperatureDormant[index]
                && this.TemperatureTick % DormantRetryEvery != 0)
                return;

            bool updated = this.Temperature[index].Update();

            // A reading that arrived and is not zero means the sensor is there.
            //
            // The zero qualification carries more weight than it looks. The
            // controller lets a read go out blind once the honest wait has
            // failed EcFailLimit times for a register — right for a controller
            // that holds an answer and never raises the flag, but on a
            // register the board does not implement there is no answer, and
            // the blind read reports success with a value of nought.
            //
            // Taken as evidence the sensor is present, that reset this counter
            // at 15 while it needs to reach 30, every time, forever. So the
            // mechanism written for boards with absent registers never
            // engaged on a board with absent registers, and the exchange per
            // sensor per second it exists to avoid was paid for the life of
            // the process. Found by running the code against a board that does
            // not carry the auxiliary probes; it cannot be seen on a machine
            // that carries them all.
            //
            // Zero is not a temperature this application acts on anywhere
            // else either: the poller drops it from the board list, the
            // history records it as a gap rather than a reading, and the
            // thermal guard refuses to act on it.
            if(updated && this.Temperature[index].GetValue() != 0) {
                this.TemperatureMisses[index] = 0;
                if(this.TemperatureDormant[index]) {
                    this.TemperatureDormant[index] = false;
                    Logger.Info("Platform", "Sensor answering again",
                        this.Temperature[index].GetName());
                }
                return;
            }

            // A refused update on a sensor that has never held a value is the
            // signature of a register that is not there. One that does hold a
            // value is merely between readings, and is left alone.
            if(this.Temperature[index].GetValue() != 0)
                return;

            if(this.TemperatureDormant[index])
                return;

            if(++this.TemperatureMisses[index] >= DormantAfter) {
                this.TemperatureDormant[index] = true;
                Logger.Info("Platform", "Sensor not present on this board",
                    this.Temperature[index].GetName() + " — polling it rarely from now on");
            }

        }
#endregion

    }

}
