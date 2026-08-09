// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Text;
using StarMon.Library;

namespace StarMon.Driver {

    // Which driver the privileged reads actually go through.
    //
    // There are two, and they are not interchangeable. WinRing0 hands out raw
    // access to ports, model-specific registers and PCI configuration space;
    // it is what this application has always used, and it is on Microsoft's
    // vulnerable-driver list precisely because raw access is what it hands
    // out. PawnIO hands out nothing: it runs a verified program inside the
    // kernel, and the programs used here permit two I/O ports and a fixed list
    // of processor registers and refuse the rest.
    //
    // Everything above this class asks for a reading. This is the only place
    // that knows which driver answered, so it is the only place that has to
    // change when one of them is not there. What it deliberately does not do
    // is pretend the two are equivalent: PawnIO can be open and still have no
    // processor module, because the module says which processors it is for,
    // and a caller that needs one is told so rather than handed a zero.
    public static class LowLevel {

        public enum Backend {
            None,
            WinRing0,
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
        // Loads whichever driver this machine can have.
        //
        // The order is not arbitrary. PawnIO is preferred wherever it is
        // installed: it is signed, it loads with memory integrity left on, and
        // it does not install a service Windows Defender reports as a
        // vulnerable driver. WinRing0 remains the fallback, because it is
        // carried in this executable and therefore works on a machine with
        // nothing installed — which is most of them.
        public static void Open() {

            lock(Lock) {

                if(Opened)
                    return;

                Opened = true;
                Log.Length = 0;

                string preference = (Config.DriverBackend ?? "").Trim();

                bool mayPawn = !preference.Equals("WinRing0",
                    StringComparison.OrdinalIgnoreCase);
                bool mayRing = !preference.Equals("PawnIO",
                    StringComparison.OrdinalIgnoreCase);

                if(mayPawn && OpenPawnIo())
                    return;

                if(mayRing && OpenRing0())
                    return;

                Log.AppendLine("No driver could be loaded, so the Embedded "
                    + "Controller and the processor registers are unreachable");

            }

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

        // Opens WinRing0
        private static bool OpenRing0() {

            Ring0.Open();

            if(!Ring0.IsOpen) {
                Log.Append(Ring0.GetStatus());
                return false;
            }

            Active = Backend.WinRing0;

            // WinRing0 hands out raw access, so everything it can do is
            // available the moment it is open. Whether a given register exists
            // on this processor is the caller's question, not the driver's.
            HasMsr = true;
            HasSmn = true;

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

                if(Active == Backend.WinRing0)
                    try {
                        Ring0.Close();
                    } catch { }

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
                    return "PawnIO " + PawnIo.Version + " (signed, "
                        + "Embedded Controller ports only)"
                        + (HasMsr
                            ? " · processor registers via "
                                + (Vendor == MsrVendor.Amd
                                    ? "AMDFamily17" : "IntelMSR")
                            : " · no processor module for this CPU");

                case Backend.WinRing0:
                    return "WinRing0 1.2.0.5 (raw port, register and PCI access)";

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

            return Active == Backend.WinRing0 ? Ring0.ReadIoPort(port) : (byte) 0;

        }

        public static void WriteIoPort(uint port, byte value) {

            if(Active == Backend.PawnIo) {

                if(EcModule != null)
                    EcModule.Execute(PioWrite,
                        new ulong[] { port, value }, null);

                return;

            }

            if(Active == Backend.WinRing0)
                Ring0.WriteIoPort(port, value);

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

            return Ring0.ReadMsr(index, out eax, out edx);

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

            if(Active != Backend.WinRing0)
                return false;

            // WinRing0 has no notion of SMN, so it is done the long way it has
            // always been done: point the host bridge's index register at the
            // address and read the value back out of the data register. The
            // caller holds a lock across both, since the pair is not atomic.
            if(!Ring0.WritePciConfig(0, 0x60, address))
                return false;

            return Ring0.ReadPciConfig(0, 0x64, out value);

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
