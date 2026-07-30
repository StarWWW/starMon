// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.Hardware.Platform {

    // Defines a common interface for all platform components
    public interface IPlatformComponent {

        // Retrieves the access type
        public PlatformData.AccessType GetAccessType();

        // Retrieves the data size
        public PlatformData.DataSize GetDataSize();

        // Retrieves the link type
        public PlatformData.LinkType GetLinkType();

        // Retrieves the name
        public string GetName();

        // Sets the name
        public void SetName(string name);

    }

    // Defines an interface for interacting with a readable component
    public interface IPlatformReadComponent : IPlatformComponent {

        // Retrieves the current constraint value
        public int GetConstraint();

        // Retrieves the current sensor value
        public int GetValue();

        // Retrieves the value trend
        public PlatformData.ValueTrend GetValueTrend();

        // Sets the constraint value
        public void SetConstraint(int constraint);

        // Updates the sensor value
        public bool Update();

    }

    // Defines an interface for interacting with a writeable component
    public interface IPlatformWriteComponent : IPlatformComponent {

        // Sets the component value
        public void SetValue(int value);

    }

    // Defines an interface for interacting with
    // a component that is both readable and writeable
    public interface IPlatformReadWriteComponent :
        IPlatformReadComponent, IPlatformWriteComponent {

    }

    // Provides common base for all kinds of components
    public abstract class PlatformComponentAbstract : IPlatformReadWriteComponent {

        // Stores the access type
        protected PlatformData.AccessType AccessType;

        // Stores the constraint value
        protected int Constraint;

        // Stores the component name
        protected string Name;

        // Stores the data size
        protected PlatformData.DataSize Size;

        // Stores the linktype
        protected PlatformData.LinkType LinkType;

        // Constructs a component instance
        public PlatformComponentAbstract(
            PlatformData.AccessType access = PlatformData.AccessType.Read) {

            // Set the access type
            this.AccessType = access;
        }

        // Retrieves the access type
        public PlatformData.AccessType GetAccessType() {
            return this.AccessType;
        }

        // Retrieves the constraint value
        public int GetConstraint() {
            return this.Constraint;
        }

        // Retrieves the data size
        public PlatformData.DataSize GetDataSize() {
            return this.Size;
        }

        // Retrieves the link type
        public PlatformData.LinkType GetLinkType() {
            return this.LinkType;
        }

        // Retrieves the component name
        public virtual string GetName() {
            return this.Name;
        }

        // Sets the constraint value
        public void SetConstraint(int constraint) {
            this.Constraint = constraint;
        }

        // Sets the component name
        public virtual void SetName(string name) {
            this.Name = name;
        }

        // Reading operations

        // Stores the last and previous values
        protected int LastValue;
        protected int PreviousValue;

        // Checks whether a read or write operation is valid for the component
        protected virtual void AssertHasAccess(PlatformData.AccessType access) {
            if(!this.AccessType.HasFlag(access))
                throw new InvalidOperationException();
        }

        // Retrieves the current component value
        public virtual int GetValue() {
            // Ensure the component can be read from
            AssertHasAccess(PlatformData.AccessType.Read);
            return this.LastValue;
        }

        // Retrieves the component value trend
        public virtual PlatformData.ValueTrend GetValueTrend() {
            // Ensure the component can be read from
            AssertHasAccess(PlatformData.AccessType.Read);
            return LastValue != PreviousValue ?
                LastValue > PreviousValue ?
                    PlatformData.ValueTrend.Ascending
                    : PlatformData.ValueTrend.Descending
                    : PlatformData.ValueTrend.Unchanged;
        }

        // Updates the component value
        public virtual bool Update() {
            // Ensure the component can be read from
            AssertHasAccess(PlatformData.AccessType.Read);

            try {

                // Read the value, and stop here if the exchange never
                // happened. A link that cannot say whether it answered
                // reports success, which is what it did before this existed.
                int value;
                if(!TryRead(out value))
                    return false;

                // Hold off on one additional time
                // for values that might be intermittently zeroed.
                //
                // The reading on offer is discarded, so the value this
                // component reports does not change; the previous value has to
                // follow it, or GetValueTrend compares an unchanged reading
                // against a zero and reports a rise that never happened.
                if(value == 0 && this.PreviousValue != 0) {
                    this.PreviousValue = this.LastValue;
                    return false;
                }

                // Only update if the reading
                // is not obviously incorrect
                if(value <= this.Constraint) {

                    // Update the previous value
                    this.PreviousValue = this.LastValue;
                    this.LastValue = value;

                    // Update succeeded
                    return true;

                }

            } catch { }

                // Update failed
                return false;

        }

        // Writing operations

        // Sets the current component value
        public virtual void SetValue(int value) {
            // Ensure the component can be written to
            AssertHasAccess(PlatformData.AccessType.Write);

            // Set the value
            Write(value);

            // If the component can also be read from, update the value
            if(this.AccessType.HasFlag(PlatformData.AccessType.Read))
                Update();

        }

        // Implements component value retrieval
        protected abstract int Read();

        // Reads the component, saying whether the reading is one the hardware
        // actually gave back.
        //
        // Read() alone cannot say. An Embedded Controller read that never got
        // an answer hands back zero, which is indistinguishable from a
        // register that genuinely holds zero — and a register the board does
        // not carry reads zero for the life of the process. Without this,
        // Update() called every absent sensor a success, so the miss counting
        // in Platform.UpdateSensor never ran and no sensor was ever stood
        // down: one wasted exchange per second per absent register, forever,
        // on the bus the fan readings share.
        //
        // Links that have no way to tell the two apart keep the old
        // behaviour and report success.
        protected virtual bool TryRead(out int value) {
            value = Read();
            return true;
        }

        // Required due to inheritance, and implemented here so that
        // it does not have to be repeated in every derived class,
        // even if the class does not implement the write interface
        protected virtual void Write(int value) {
            return;
        }

    }

    // Implements an Embedded Controller component
    public class EcComponent : PlatformComponentAbstract, IPlatformReadWriteComponent {

        // Stores the Embedded Controller register associated with the sensor
        protected byte Register;

        // Constructs an Embedded Controller readable component instance
        public EcComponent(
            byte register,
            PlatformData.AccessType access = PlatformData.AccessType.Read,
            PlatformData.DataSize size = PlatformData.DataSize.Byte,
            int constraint = int.MaxValue) {

            this.AccessType = access;
            this.Constraint = constraint;
            this.LinkType = PlatformData.LinkType.EmbeddedController;
            this.Register = register;
            this.Size = size;

            SetName();

        }

        // Defines a constructor for read-only non byte-sized data
        public EcComponent(byte register, PlatformData.DataSize size)
            : this(register, PlatformData.AccessType.Read, size) {

        }

        // Defines a constructor for read-only constrained-value data
        public EcComponent(byte register, int constraint)
            : this(register, PlatformData.AccessType.Read, PlatformData.DataSize.Byte, constraint) {

        }

        // Sets the name of an Embedded Controller sensor
        public override void SetName(string name = "") {
            if(name != "")
                this.Name = name;
            else try {
                // Use the DSDT table entry as the name
                this.Name = Enum.GetName(typeof(EmbeddedControllerData.Register), this.Register);
            } catch {
                // Set a generic name based on the register number
                this.Name = "R" + Conv.GetString(this.Register, 3, 10);
            }
        }

        // Reads a value from the Embedded Controller
        protected override int Read() {
            if(this.Size == PlatformData.DataSize.Byte)
                return Hw.EcGetByte(this.Register);
            else
                return Hw.EcGetWord(this.Register);
        }

        // The same read, reporting whether the controller answered at all
        protected override bool TryRead(out int value) {

            if(this.Size == PlatformData.DataSize.Byte) {
                byte read;
                bool answered = Hw.EcTryGetByte(this.Register, out read);
                value = read;
                return answered;
            }

            ushort word;
            bool ok = Hw.EcTryGetWord(this.Register, out word);
            value = word;
            return ok;

        }

        // Writes a value to the Embedded Controller
        protected override void Write(int value) {
            if(this.Size == PlatformData.DataSize.Byte)
                Hw.EcSetByte(this.Register, (byte) value);
            else
                Hw.EcSetWord(this.Register, (ushort) value);
        }

    }

    // Implements a BIOS Temperature component
    public class WmiBiosTemperatureComponent : PlatformComponentAbstract, IPlatformReadComponent {

        // Constructs an BIOS Temperature readable component instance
        public WmiBiosTemperatureComponent(int constraint = int.MaxValue) {

            this.AccessType = PlatformData.AccessType.Read;
            this.Constraint = constraint;
            this.LinkType = PlatformData.LinkType.WmiBios;
            this.Size = PlatformData.DataSize.Byte;

            SetName();

        }

        // Sets the name of an Embedded Controller sensor
        public override void SetName(string name = "") {
            if(name != "")
                this.Name = name;
            else
                this.Name = "BIOS";
        }

        // Reads a temperature value from the BIOS
        protected override int Read() {
            try
            {
                return Hw.BiosGet(Hw.Bios.GetTemperature);
            }
            catch
            {
                return 0; // Return 0 for unsupported devices
            }
        }

    }

    // Implements a CPU temperature component backed by the processor's own
    // on-die thermal sensor, read through Model-Specific Registers (Intel) or
    // the SMU thermal register (AMD) via the WinRing0 driver. This provides a
    // markedly more accurate reading than the Embedded Controller register and
    // is therefore used in place of it whenever the processor supports it.
    public class MsrCpuTemperatureComponent : PlatformComponentAbstract, IPlatformReadComponent {

        // Constructs a CPU temperature readable component instance
        // The constraint defaults high enough to admit genuine full-load
        // temperatures (which routinely exceed the Embedded Controller's cap)
        public MsrCpuTemperatureComponent(int constraint = 110) {

            this.AccessType = PlatformData.AccessType.Read;
            this.Constraint = constraint;
            this.LinkType = PlatformData.LinkType.Cpu;
            this.Size = PlatformData.DataSize.Byte;

            SetName();

        }

        // Sets the name of the CPU temperature sensor
        public override void SetName(string name = "") {
            this.Name = name != "" ? name : "CPUT";
        }

        // Reads the temperature directly from the processor,
        // returning zero when it is momentarily unavailable so that
        // the base class retains the previous valid value
        protected override int Read() {
            int value = StarMon.Hardware.Cpu.CpuTemperature.GetTemperature();
            return value > 0 ? value : 0;
        }

    }

    // Implements a GPU temperature component backed by the NVIDIA driver
    // (NVAPI), used in place of the Embedded Controller's board GPU sensor
    // (GPTM, register 0xB7) whenever an NVIDIA card is present.
    //
    // Two reasons, both real. The driver's reading is the card's own sensor
    // and more accurate than the board one. And the EC's GPTM register is
    // unreliable on Optimus laptops: when the discrete GPU powers down to save
    // battery — which it does whenever nothing is using it — the EC cannot
    // fetch a temperature from a chip that is asleep, and the read fails.
    // Polling it every second then fills the log with failures for a sensor
    // that was redundant anyway. Reading through the driver instead returns a
    // clean "asleep" rather than a failed EC exchange.
    public class NvapiGpuTemperatureComponent : PlatformComponentAbstract, IPlatformReadComponent {

        public NvapiGpuTemperatureComponent(int constraint = 110) {
            this.AccessType = PlatformData.AccessType.Read;
            this.Constraint = constraint;
            this.LinkType = PlatformData.LinkType.WmiBios; // nearest existing kind
            this.Size = PlatformData.DataSize.Byte;
            SetName();
        }

        // Kept as GPTM so the rest of the application, which looks the sensor
        // up by that name, does not have to know where the number came from
        public override void SetName(string name = "") {
            this.Name = name != "" ? name : "GPTM";
        }

        // Reads the GPU temperature through the driver, returning zero when the
        // card is asleep or unavailable so the base class holds the last value
        protected override int Read() {
            try {
                GpuNvidia.GpuInfo gpu = GpuNvidia.Get();
                return gpu.Present && gpu.TempC > 0 ? gpu.TempC : 0;
            } catch {
                return 0;
            }
        }

    }

}
