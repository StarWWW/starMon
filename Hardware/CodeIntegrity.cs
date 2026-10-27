// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Security.Principal;
using StarMon.Library;

namespace StarMon.Hardware {

    // Why the kernel driver would not load.
    //
    // This application reaches the Embedded Controller through PawnIO, a
    // signed driver that runs small verified programs and nothing else. It
    // used to carry WinRing0 1.2.0.5 as well, inside the executable, as a
    // fallback for machines without PawnIO - and that driver is on Microsoft's
    // vulnerable-driver list: it hands any administrator raw access to ports,
    // processor registers and physical memory, and Defender reports it as
    // VulnerableDriver:WinNT/Winring0. It is gone, and with it the reasons a
    // driver would be refused that only ever applied to it: memory integrity,
    // the blocklist and secure boot do not stand in PawnIO's way.
    //
    // What is left is short. The process is not elevated, PawnIO is not
    // installed, or PawnIO is installed and still would not open - and each
    // has one thing to do about it, which is what this says.
    public static class CodeIntegrity {

        // What stands between this process and a loaded driver
        public enum Obstacle {
            None,
            NotElevated,
            PawnIoMissing,
            Unknown
        }

        private static readonly object Lock = new object();
        private static bool Examined;
        private static Obstacle Found;

        // Whether this process is running with administrator rights.
        //
        // The manifest asks for them, so an ordinary launch either has them or
        // never started — but the test host is built without that manifest,
        // and a scheduled task can be registered to run without elevation. The
        // check costs nothing and turns an unexplained failure into a sentence.
        public static bool IsElevated {
            get {
                try {
                    using(WindowsIdentity identity = WindowsIdentity.GetCurrent())
                        return new WindowsPrincipal(identity)
                            .IsInRole(WindowsBuiltInRole.Administrator);
                } catch {
                    return false;
                }
            }
        }

        // Whether PawnIO is installed on this machine.
        //
        // Asked of the loader rather than answered again here. There used to
        // be a second search in this file, looking under Program Files while
        // the loader consulted the installer's own record of where it put
        // itself. Two searches for one thing is two answers waiting to
        // disagree, and the one that would have been wrong is this one — the
        // one the user is shown.
        public static bool PawnIoInstalled {
            get { return StarMon.Driver.PawnIo.IsAvailable; }
        }

        // The obstacle most worth telling the user about, examined once.
        //
        // Elevation first, because without it nothing else can be concluded:
        // opening PawnIO is a privileged operation, so an unelevated process
        // fails whether it is installed or not.
        //
        // Only consulted once the driver has failed to load, so "Unknown" -
        // installed, elevated and refused all the same - is a real answer and
        // not a gap: the driver log carries the exact refusal.
        public static Obstacle Diagnose() {

            lock(Lock) {

                if(Examined)
                    return Found;

                Examined = true;

                Found = !IsElevated ? Obstacle.NotElevated
                    : !PawnIoInstalled ? Obstacle.PawnIoMissing
                    : Obstacle.Unknown;

                // Deliberately silent: this is an examination of the machine,
                // run by the capability report on machines where the driver
                // loads perfectly well. Hw.EcInit logs the summary when the
                // controller actually could not be reached.
                return Found;

            }

        }

        // A sentence for the user, and what they can do about it, in the
        // interface's language: it is the body of the notice across the top
        // of the window
        public static string Explain() {

            switch(Diagnose()) {

                case Obstacle.NotElevated:
                    return Text("GuiDriverNotElevated");

                case Obstacle.PawnIoMissing:
                    return Text("GuiDriverPawnIoMissing");

                default:
                    return Text("GuiDriverPawnIoRefused");

            }

        }

        // The one-line form, for the log and the capability report
        public static string Summary() {

            return "elevated: " + (IsElevated ? "yes" : "no")
                + " · PawnIO: " + (PawnIoInstalled ? "installed" : "not installed");

        }

        // Forgets the examination, so a test can run it again
        internal static void Reset() {
            lock(Lock) {
                Examined = false;
                Found = Obstacle.None;
            }
        }

        // A string from the locale, or the key itself where the locale has
        // not been set up - the command line can ask before it has been
        private static string Text(string key) {
            return Config.Locale != null ? Config.Locale.Get(key) : key;
        }

    }

}
