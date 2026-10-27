// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.IO;
using System.Text;
using StarMon.External;
using StarMon.Library;

namespace StarMon.Driver {

    // The privileged reads, through PawnIO.
    //
    // PawnIO hands out nothing on its own: it runs a verified program inside
    // the kernel, and the programs used here permit two I/O ports and a fixed
    // list of processor registers and refuse the rest. It is the only driver.
    //
    // There used to be a second, WinRing0 1.2.0.5, carried inside the
    // executable and installed as a kernel service wherever PawnIO was not.
    // It hands any administrator raw access to ports, processor registers and
    // physical memory, which is why Microsoft lists it as a vulnerable driver
    // and Defender reports it - and leaving it as a fallback meant every copy
    // of this application carried it. It is gone. A machine without PawnIO
    // keeps its readings and loses fan control, and is told why and what to
    // install, which is a better answer than loading a driver that lets
    // anything on the machine into the kernel.
    //
    // Everything above this class asks for a reading. What it deliberately
    // does not do is pretend every reading is there: PawnIO can be open and
    // still have no processor module, because the module says which
    // processors it is for, and a caller that needs one is told so rather than
    // handed a zero.
    public static class LowLevel {

        public enum Backend {
            None,
            PawnIo
        }

        // Which vendor module answered, when the backend is PawnIO
        private enum MsrVendor {
            None,
            Intel,
            Amd
        }

        private static readonly object Lock = new object();
        private static readonly StringBuilder Log = new StringBuilder();

        private static PawnIoModule EcModule;
        private static PawnIoModule MsrModule;
        private static MsrVendor Vendor;
        private static bool Opened;

        // Which driver is in use
        public static Backend Active { get; private set; }

        public static bool IsOpen {
            get { return Active != Backend.None; }
        }

        // Whether model-specific registers can be read.
        //
        // Separate from IsOpen because it genuinely is: on PawnIO this is the
        // vendor module having loaded, and on a processor neither module
        // claims — anything that is not x64 Intel, or AMD family 17h to 1Ah —
        // there is a working Embedded Controller and no processor registers.
        // Every caller already guards on it; before this they guarded on the
        // driver being open, which was the same question only by accident.
        public static bool HasMsr { get; private set; }

        // Whether AMD's System Management Network can be read, which is where
        // the processor temperature lives on Zen
        public static bool HasSmn { get; private set; }

        // Names of the functions the PawnIO modules export
        private const string PioRead  = "ioctl_pio_read";
        private const string PioWrite = "ioctl_pio_write";
        private const string MsrRead  = "ioctl_read_msr";
        private const string SmnRead  = "ioctl_read_smn";

        // AMD publishes the performance counters twice: at the architectural
        // addresses, which are writable, and at a read-only alias 0xC0000000
        // above them. The PawnIO module permits only the aliases — a module
        // that let anything write MPERF would be handing out the ability to
        // lie to the operating system's scheduler about how fast it is going.
        // The counters are the same counters, so the substitution is exact.
        private const uint MSR_IA32_MPERF   = 0x0E7;
        private const uint MSR_IA32_APERF   = 0x0E8;
        private const uint MSR_AMD_MPERF_RO = 0xC00000E7;
        private const uint MSR_AMD_APERF_RO = 0xC00000E8;

#region Opening & Closing
        // Opens PawnIO, after taking away anything an earlier version left of
        // WinRing0
        public static void Open() {

            lock(Lock) {

                if(Opened)
                    return;

                Opened = true;
                Log.Length = 0;

                RemoveWinRing0();

                if(OpenPawnIo())
                    return;

                Log.AppendLine("PawnIO could not be opened, so the Embedded "
                    + "Controller and the processor registers are unreachable");

            }

        }

        // The service name and the file an earlier version installed WinRing0
        // under: "R0" and the program's name, and the program's own path with
        // ".sys" in place of ".exe".
        //
        // Those versions removed both on the way out, so they are only left
        // behind by one that did not get to - a crash, a power cut, a kill
        // from the task manager. A driver service left registered loads again
        // the next time anything asks for it, and this one is the reason it
        // was taken out. Only this application's own name is looked for:
        // another program's copy of WinRing0 is that program's business.
        internal const string WinRing0Service = "R0StarMon";

        private static void RemoveWinRing0() {

            try {

                IntPtr manager = AdvApi32.OpenSCManager(null, null,
                    AdvApi32.SC_MANAGER_ACCESS_MASK.SC_MANAGER_CONNECT);
                if(manager == IntPtr.Zero)
                    return;

                try {

                    IntPtr service = AdvApi32.OpenService(manager, WinRing0Service,
                        AdvApi32.SERVICE_ACCESS_MASK.SERVICE_ALL_ACCESS);

                    if(service != IntPtr.Zero) {

                        AdvApi32.SERVICE_STATUS status = new AdvApi32.SERVICE_STATUS();
                        AdvApi32.ControlService(service,
                            AdvApi32.SERVICE_CONTROL.SERVICE_CONTROL_STOP, ref status);

                        bool deleted = AdvApi32.DeleteService(service);
                        AdvApi32.CloseServiceHandle(service);

                        Logger.Warning("Driver", "Removed the WinRing0 service an "
                            + "earlier version left behind",
                            WinRing0Service + (deleted ? "" : " (marked; it goes at the next restart)"));

                    }

                } finally {
                    AdvApi32.CloseServiceHandle(manager);
                }

                string file = Path.ChangeExtension(Config.AppFile ?? "", ".sys");
                if(file.Length > 4 && File.Exists(file)) {
                    File.Delete(file);
                    Logger.Warning("Driver", "Removed the WinRing0 file an "
                        + "earlier version left behind", file);
                }

            } catch { }

        }

        // Opens PawnIO and loads the modules this application uses
        private static bool OpenPawnIo() {

            if(!PawnIo.IsAvailable) {
                Log.Append(PawnIo.GetStatus());
                return false;
            }

            EcModule = PawnIo.Load("LpcACPIEC");
            if(EcModule == null) {

                // Without the Embedded Controller module there is no point
                // keeping the rest: the processor registers alone would leave
                // fan control and the backlight dead, which is the same
                // outcome as no driver at all but reached less obviously
                Log.Append(PawnIo.GetStatus());
                return false;

            }

            // Each vendor module decides for itself whether this is its
            // processor and refuses if it is not, so asking both and keeping
            // whichever answers is the detection
            MsrModule = PawnIo.Load("IntelMSR");
            Vendor = MsrModule != null ? MsrVendor.Intel : MsrVendor.None;

            if(MsrModule == null) {
                MsrModule = PawnIo.Load("AMDFamily17");
                Vendor = MsrModule != null ? MsrVendor.Amd : MsrVendor.None;
            }

            Active = Backend.PawnIo;
            HasMsr = MsrModule != null;
            HasSmn = Vendor == MsrVendor.Amd;

            return true;

        }

        public static void Close() {

            lock(Lock) {

                if(EcModule != null) {
                    EcModule.Dispose();
                    EcModule = null;
                }

                if(MsrModule != null) {
                    MsrModule.Dispose();
                    MsrModule = null;
                }

                Active = Backend.None;
                Vendor = MsrVendor.None;
                HasMsr = false;
                HasSmn = false;
                Opened = false;

            }

        }

        // Everything that went wrong on the way to a driver
        public static string GetStatus() {
            return Log.ToString();
        }

        // One line naming the driver and what it can do, for the log and the
        // hardware report
        public static string Describe() {

            switch(Active) {

                case Backend.PawnIo:
                    // The library version, not the product's. PawnIO 2.2.0
                    // ships a library that reports 2.0.0, and a line reading
                    // "PawnIO 2.0.0" beside an installer that says 2.2.0 is
                    // one more thing for somebody to have to work out.
                    return "PawnIO (library " + PawnIo.Version + ", signed, "
                        + "Embedded Controller ports only)"
                        + (HasMsr
                            ? " · processor registers via "
                                + (Vendor == MsrVendor.Amd
                                    ? "AMDFamily17" : "IntelMSR")
                            : " · no processor module for this CPU");

                default:
                    return "none";

            }

        }
#endregion

#region Input/Output Ports
        public static byte ReadIoPort(uint port) {

            if(Active == Backend.PawnIo) {

                ulong[] input = new ulong[] { port };
                ulong[] output = new ulong[1];

                return EcModule != null
                    && EcModule.Execute(PioRead, input, output)
                        ? (byte) (output[0] & 0xFF) : (byte) 0;

            }

            return 0;

        }

        public static void WriteIoPort(uint port, byte value) {

            if(Active == Backend.PawnIo) {

                if(EcModule != null)
                    EcModule.Execute(PioWrite,
                        new ulong[] { port, value }, null);

            }

        }
#endregion

#region Model-Specific Registers
        // Reads a processor register, split into the two halves the callers
        // expect, and reports whether the read happened at all
        public static bool ReadMsr(uint index, out uint eax, out uint edx) {

            eax = 0;
            edx = 0;

            if(!HasMsr)
                return false;

            if(Active == Backend.PawnIo) {

                if(MsrModule == null)
                    return false;

                ulong[] output = new ulong[1];
                if(!MsrModule.Execute(MsrRead,
                    new ulong[] { Translate(index, Vendor == MsrVendor.Amd) }, output))
                    return false;

                eax = (uint) (output[0] & 0xFFFFFFFF);
                edx = (uint) ((output[0] >> 32) & 0xFFFFFFFF);
                return true;

            }

            return false;

        }

        // Substitutes AMD's read-only counter aliases for the architectural
        // addresses, and leaves every other register alone.
        //
        // Separated out and made testable because getting it wrong is silent:
        // the module answers STATUS_ACCESS_DENIED for a register it does not
        // permit, which arrives here as a failed read, and the effective clock
        // simply stops being shown on AMD with nothing saying why.
        internal static uint Translate(uint index, bool amd) {

            if(!amd)
                return index;

            if(index == MSR_IA32_MPERF)
                return MSR_AMD_MPERF_RO;

            if(index == MSR_IA32_APERF)
                return MSR_AMD_APERF_RO;

            return index;

        }
#endregion

#region AMD System Management Network
        // Reads an SMN register, which on Zen is how the processor
        // temperature is reached
        public static bool ReadSmn(uint address, out uint value) {

            value = 0;

            if(!HasSmn)
                return false;

            if(Active == Backend.PawnIo) {

                if(MsrModule == null || Vendor != MsrVendor.Amd)
                    return false;

                ulong[] output = new ulong[1];
                if(!MsrModule.Execute(SmnRead, new ulong[] { address }, output))
                    return false;

                value = (uint) (output[0] & 0xFFFFFFFF);
                return true;

            }

            return false;

        }
#endregion

        // Forgets the driver state, so a test can decide it again
        internal static void Reset() {
            lock(Lock) {
                EcModule = null;
                MsrModule = null;
                Active = Backend.None;
                Vendor = MsrVendor.None;
                HasMsr = false;
                HasSmn = false;
                Opened = false;
                Log.Length = 0;
            }
        }

    }

}
