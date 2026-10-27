// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.Threading;
using StarMon.Driver;
using StarMon.Library;

namespace StarMon.Hardware.Ec
{

    #region Interface
    // Defines an interface for interacting with the Embedded Controller
    public interface IEmbeddedController : IDisposable
    {

        public bool IsInitialized { get; }

        public void Initialize();
        public void Close();

        // Lock
        public bool Request(int timeout);
        public void Release();

        // Read
        public byte ReadByte(byte register);
        public ushort ReadWord(byte register);

        // Read, distinguishing a failed exchange from a genuine zero
        public bool TryReadByte(byte register, out byte value);
        public bool TryReadWord(byte register, out ushort value);

        // Write
        public void WriteByte(byte register, byte value);
        public void WriteWord(byte register, ushort value);

    }
    #endregion

    // Implements as much of Embedded Controller functionality as possible
    // without getting into the kernel driver-specific routines
    // Builds up on the Embedded Controller data values and structures defined earlier
    public abstract class EmbeddedControllerAbstract
        : EmbeddedControllerData, IEmbeddedController
    {

        public bool IsInitialized { get; protected set; }

        // Failed waits for a read, counted per register.
        //
        // Per register, not per controller. This used to be one instance-wide
        // counter, and that made a register's failures vouch for a different
        // register's data: a board missing six of the configured temperature
        // probes fails three reads on each of them in a single polling pass,
        // eighteen in a row with nothing in between to clear the count, and
        // from that point the bypass below is open. The next ordinary read
        // anywhere in the application — a fan tachometer, the countdown —
        // that meets a momentarily busy controller is then accepted without
        // the data ever having been confirmed ready, and whatever byte
        // happens to be sitting in the port is reported as a fan speed.
        //
        // The bypass is for a controller that never raises the flag at all,
        // which shows up as one register failing over and over; keeping the
        // count where the failures happen preserves that and nothing else.
        private readonly System.Collections.Generic.Dictionary<byte, int>
            WaitReadFails = new System.Collections.Generic.Dictionary<byte, int>();

        // How many waits for a read have failed in a row on one register
        protected int WaitReadFailCount(byte register) {
            int count;
            this.WaitReadFails.TryGetValue(register, out count);
            return count;
        }

        // Upper bound on the stale output bytes drained before a transaction,
        // so a permanently-full output buffer cannot spin here forever
        protected const int FlushObfLimit = 8;

        // How many times a register has failed in a row.
        //
        // A single failed read is not news: the Embedded Controller is shared
        // with the firmware, and it is momentarily busy often — most of all
        // for a register whose value it has to fetch from a device that may be
        // asleep, which on this hardware is exactly the GPU temperature
        // (0xB7) while the discrete card is powered down. Logging every one of
        // those as a warning fills the log with red that means nothing. So a
        // lone failure is recorded quietly and only a sustained run of them —
        // a register that has genuinely stopped answering — is raised as a
        // warning. A success clears the count, so a register that recovers is
        // forgiven silently.
        private readonly System.Collections.Generic.Dictionary<byte, int>
            ConsecutiveFails = new System.Collections.Generic.Dictionary<byte, int>();

        // The run length past which repeated failure is treated as a real
        // fault rather than transient contention
        private const int FailWarnAfter = 5;

        // Records a failed read/write and decides how loudly to report it
        private void NoteFailure(byte register, bool isWrite, int attempts) {

            int run;
            this.ConsecutiveFails.TryGetValue(register, out run);
            run++;
            this.ConsecutiveFails[register] = run;

            if(run >= FailWarnAfter)
                Logger.EcFail(register, isWrite, attempts);
            else
                Logger.EcTransient(register, isWrite);

        }

        // Records a successful read/write, clearing the register's failure run
        private void NoteSuccess(byte register) {
            if(this.ConsecutiveFails.Count > 0)
                this.ConsecutiveFails.Remove(register);
        }

        #region Abstract Methods
        // Initialization and disposal
        // Implementation is driver-specific
        public abstract void Initialize();
        public abstract void Close();

        // Mutex lock request and release
        // Implementation depends on the EmbeddedControllerMutex class
        public abstract bool Request(int timeout);
        public abstract void Release();

        // Actual driver-specific read and write routines
        protected abstract byte ReadIoPort(Port port);
        protected abstract void WriteIoPort(Port port, byte value);

        // Dispose() is just a wrapper for Close()
        public virtual void Dispose()
        {
            Close();
        }
        #endregion

        #region Public Read & Write Methods
        // Wrapper to read a byte from an Embedded Controller register
        public virtual byte ReadByte(byte register)
        {
            byte value;
            TryReadByte(register, out value);
            return value;
        }

        // Reads a byte, reporting whether the exchange actually succeeded:
        // a failed read yields zero, which is indistinguishable from a
        // genuine zero reading unless the caller checks the return value
        public virtual bool TryReadByte(byte register, out byte value)
        {
            int count = 0;
            value = 0;
            while (count < Config.EcRetryLimit)
            {
                if (ReadByteImpl(register, out value))
                {
                    Logger.EcRead(register, value);
                    NoteSuccess(register);
                    return true;
                }
                count++;
            }
            NoteFailure(register, false, count);
            return false;
        }

        // Wrapper to read a word (two bytes) from an Embedded Controller register
        public virtual ushort ReadWord(byte register)
        {
            ushort value;
            TryReadWord(register, out value);
            return value;
        }

        // Reads a word, reporting whether the exchange actually succeeded
        public virtual bool TryReadWord(byte register, out ushort value)
        {
            int count = 0;
            value = 0;
            while (count < Config.EcRetryLimit)
            {
                if (ReadWordImpl(register, out value))
                {
                    Logger.EcReadWord(register, value);
                    NoteSuccess(register);
                    return true;
                }
                count++;
            }
            NoteFailure(register, false, count);
            return false;
        }

        // Wrapper to write a byte to an Embedded Controller register
        public virtual void WriteByte(byte register, byte value)
        {
            int count = 0;
            while (count < Config.EcRetryLimit)
            {
                if (WriteByteImpl(register, value))
                {
                    Logger.EcWrite(register, value);
                    NoteSuccess(register);
                    return;
                }
                count++;
            }
            NoteFailure(register, true, count);
        }

        // Wrapper to write a word (two bytes) to an Embedded Controller register
        public virtual void WriteWord(byte register, ushort value)
        {
            int count = 0;
            while (count < Config.EcRetryLimit)
            {
                if (WriteWordImpl(register, value))
                {
                    Logger.EcWriteWord(register, value);
                    NoteSuccess(register);
                    return;
                }
                count++;
            }
            NoteFailure(register, true, count);
        }
        #endregion

        #region Protected Read & Write Implementation Methods
        // Reads a byte from an Embedded Controller register
        protected bool ReadByteImpl(byte register, out byte value)
        {
            // Drop anything the controller left in its output buffer before
            // starting: a stale byte from an earlier, abandoned exchange would
            // otherwise be mistaken for this register's value, and every
            // subsequent read would stay one step behind (protocol desync)
            FlushObf();

            if (WaitWrite())
            {
                WriteIoPort(Port.Command, (byte)Command.Read);
                if (WaitWrite())
                {
                    WriteIoPort(Port.Data, register);
                    if (WaitWrite() && WaitRead(register))
                    {
                        value = ReadIoPort(Port.Data);
                        return true;
                    }
                }
            }
            value = 0;
            return false;
        }

        // Reads a word (two bytes) from an Embedded Controller register
        protected bool ReadWordImpl(byte register, out ushort value)
        {
            byte result = 0;
            value = 0;
            if (!ReadByteImpl(register, out result))
                return false;
            value = result;
            if (!ReadByteImpl((byte)(register + 1), out result))
            {
                // Discard the low byte too. Leaving it in place would hand
                // back a number that looks entirely plausible but is missing
                // its high byte: a fan reading of 0x1234 rpm arrives as
                // 0x0034. Callers that ignore the return value would have no
                // way to tell, so the value has to be as invalid as the read.
                value = 0;
                return false;
            }
            value |= (ushort)(result << 8);
            return true;
        }

        // Writes a byte to an Embedded Controller register
        protected bool WriteByteImpl(byte register, byte value)
        {
            // As for reads: never start a transaction on top of a leftover
            // output byte, or the controller and the host disagree on where
            // in the protocol they are
            FlushObf();

            if (WaitWrite())
            {
                WriteIoPort(Port.Command, (byte)Command.Write);
                if (WaitWrite())
                {
                    WriteIoPort(Port.Data, register);
                    if (WaitWrite())
                    {
                        WriteIoPort(Port.Data, value);
                        if (WaitWrite())
                            return true;
                    }
                }
            }
            return false;
        }

        // Writes a word (two bytes) to an Embedded Controller register
        protected bool WriteWordImpl(byte register, ushort value)
        {
            byte high = (byte)(value >> 8);
            byte low = (byte)value;
            if (!WriteByteImpl(register, low))
                return false;
            if (!WriteByteImpl((byte)(register + 1), high))
                return false;
            return true;
        }
        #endregion

        #region Protected Wait Methods
        // Drains any byte the controller left sitting in its output buffer,
        // so a fresh transaction starts from a known-clean state
        protected void FlushObf()
        {
            for (int i = 0; i < FlushObfLimit; i++)
            {
                if ((ReadIoPort(Port.Command) & (byte) Status.OutFull) == 0)
                    return;

                // Reading the data port is what clears the output-buffer-full
                // flag; the value itself belongs to an exchange nobody is
                // waiting for anymore, so it is deliberately discarded
                ReadIoPort(Port.Data);
            }
        }

        // Waits until the Embedded Controller is in a suitable state.
        //
        // The budget is a span of time, not a count of iterations. How long an
        // iteration takes is not this code's to decide: Thread.Sleep(1) does
        // not sleep a millisecond, it sleeps until the next scheduler tick,
        // which on a default Windows configuration is about 15 ms. Counting
        // iterations and sleeping between them therefore produces a real limit
        // fifteen times longer than it reads as, and every read that hits a
        // momentarily busy controller pays it. Bounding the wall-clock time
        // instead makes the worst case exactly what it says it is.
        //
        // Within the budget the loop escalates from spinning to yielding, and
        // never sleeps: the shortest sleep available would overshoot the whole
        // budget in a single step.
        protected bool Wait(Status status, bool isSet)
        {
            long deadline = Stopwatch.GetTimestamp()
                + (long)(Config.EcWaitTimeoutMs * (Stopwatch.Frequency / 1000.0));

            for (int i = 0; ; i++)
            {
                byte value = ReadIoPort(Port.Command);

                if (isSet)
                    value = (byte)~value;

                if (((byte)status & value) == 0)
                    return true;

                // Alternatively, a much less legible one-liner:
                // if(((byte) status & (isSet ? (byte) ~value : value)) == 0)
                //     return true;

                if (Stopwatch.GetTimestamp() >= deadline)
                    return false;

                // A healthy controller answers within the first few polls, so
                // the common path never gets past the spin
                if (i < Config.EcWaitLimit)
                    Thread.SpinWait(64);
                else
                    Thread.Yield();
            }
        }

        // Waits for a read operation.
        //
        // An honest wait is always attempted first, and a success clears the
        // failure counter, so a controller that recovers heals this state by
        // itself. Only once the wait has genuinely failed more often than the
        // limit allows does the read proceed blind, and even then the next
        // call still tries the real wait: the bypass is a last resort for a
        // controller that never raises the flag, never a permanent mode.
        protected bool WaitRead(byte register)
        {
            if (Wait(Status.OutFull, true))
            {
                this.WaitReadFails.Remove(register);
                return true;
            }

            int count;
            this.WaitReadFails.TryGetValue(register, out count);
            count++;
            this.WaitReadFails[register] = count;

            return count > Config.EcFailLimit;
        }

        // Waits for a write operation
        protected bool WaitWrite()
        {
            return Wait(Status.InFull, false);
        }
        #endregion

    }

    #region Driver Implementation
    // Links the abstract Embedded Controller implementation
    // to the low-level routines in the Ring0 kernel driver
    public sealed class EmbeddedController : EmbeddedControllerAbstract, IEmbeddedController
    {

        // The following three statements ensure the class can be instantiated only once
        private static readonly EmbeddedController instance = new EmbeddedController();

        private EmbeddedController() { }

        public static EmbeddedController Instance
        {
            get { return instance; }
        }

        // Initializes the kernel driver and creates a lock on the Embedded Controller
        public override void Initialize()
        {
            if (!this.IsInitialized)
            {
                LowLevel.Open();
                if (LowLevel.IsOpen)
                {
                    this.IsInitialized = true;
                    EmbeddedControllerMutex.Open();

                    // Which driver answered is worth one line. The two are not
                    // equivalent — one of them can be open and still have no
                    // processor registers — and on a machine where a reading
                    // is missing this is the first thing to look at.
                    Logger.Info("Driver", "Using " + LowLevel.Describe());
                }
                else
                {
                    // Logged, not shown.
                    //
                    // This was App.Error, which in the interface is a modal
                    // dialog and on the command line sets a failure exit code.
                    // Both are wrong here. The text is the driver loader's own
                    // running commentary - "1st Try: OpenSCManager Error:
                    // 00000005" - which is untranslated, is not addressed to a
                    // user, and says nothing they can act on; and it appeared
                    // before the explanation that can, since Hw.EcInit only
                    // gets to run after this returns.
                    //
                    // It also made -Probe exit 5 while writing a perfectly good
                    // report, on exactly the machines the report is for.
                    //
                    // The detail is worth keeping: it is the first thing to
                    // look at when a driver will not load. It belongs in the
                    // log, which the report carries.
                    Logger.Error("Driver", "Kernel driver would not open",
                        LowLevel.GetStatus());
                }
            }
        }

        // Closes the kernel driver and clears the Embedded Controller lock
        public override void Close()
        {
            if (this.IsInitialized)
            {
                this.IsInitialized = false;
                try
                {
                    EmbeddedControllerMutex.Close();
                }
                catch
                {
                }
                try
                {
                    LowLevel.Close();
                }
                catch
                {
                }
            }
        }

        // Requests a lock on the Embedded Controller
        public override bool Request(int timeout)
        {
            return EmbeddedControllerMutex.Wait(timeout);
        }

        // Releases a lock on the Embedded Controller
        public override void Release()
        {
            EmbeddedControllerMutex.Release();
        }

        // Wrapper for the I/O port read routine in the kernel driver
        protected override byte ReadIoPort(Port port)
        {
            return LowLevel.ReadIoPort((uint)port);
        }

        // Wrapper for the I/O port write routine in the kernel driver
        protected override void WriteIoPort(Port port, byte value)
        {
            LowLevel.WriteIoPort((uint)port, value);
        }

    }
    #endregion

}
