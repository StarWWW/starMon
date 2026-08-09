// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Security.Principal;
using Microsoft.Win32;
using StarMon.Library;

namespace StarMon.Hardware {

    // Why the kernel driver would not load.
    //
    // This application reaches the Embedded Controller through a driver, and
    // on a growing share of machines that driver does not load at all. Until
    // now the application answered that by exiting: no window, no reading, and
    // a message naming the failed step rather than the reason for it.
    //
    // The reason is knowable, and it is nearly always one of four things.
    // Windows 11 has shipped with the vulnerable-driver blocklist on by
    // default since the 2022 update, and enforces it whenever memory
    // integrity, Smart App Control or S mode is active — and memory integrity
    // is on by default on most new machines. The driver this application
    // carries is WinRing0 1.2.0.5, which is on that list; Defender reports it
    // as VulnerableDriver:WinNT/Winring0.
    //
    // None of that is a bug in the driver's loading code, and none of it is
    // fixable by retrying. It is fixable by telling the user what is in the
    // way, which is what this is for.
    public static class CodeIntegrity {

        // What stands between this process and a loaded driver
        public enum Obstacle {
            None,
            NotElevated,
            MemoryIntegrity,
            DriverBlocklist,
            SecureBoot,
            Unknown
        }

        private static readonly object Lock = new object();
        private static bool Examined;
        private static Obstacle Found;
        private static string Detail;

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

        // Whether hypervisor-enforced code integrity is actually running.
        //
        // The configured value and the running value are different questions,
        // and only the second one blocks a driver. Windows publishes both
        // through Win32_DeviceGuard, where service 2 is memory integrity;
        // SecurityServicesConfigured says what was asked for and
        // SecurityServicesRunning says what is in force.
        public static bool MemoryIntegrityRunning {
            get {

                // Read through CimSession rather than through WmiInfo: the
                // property is an array of service identifiers, and WmiInfo
                // flattens every value with ToString(), which turns a uint[]
                // into the text "System.UInt32[]".
                try {

                    using(Microsoft.Management.Infrastructure.CimSession session
                        = Microsoft.Management.Infrastructure.CimSession.Create(null))

                        foreach(Microsoft.Management.Infrastructure.CimInstance instance
                            in session.EnumerateInstances(
                                "root\\Microsoft\\Windows\\DeviceGuard", "Win32_DeviceGuard"))

                            using(instance) {

                                Microsoft.Management.Infrastructure.CimProperty property =
                                    instance.CimInstanceProperties["SecurityServicesRunning"];

                                Array running = property == null ? null : property.Value as Array;
                                if(running == null)
                                    continue;

                                foreach(object service in running)
                                    if(Convert.ToInt32(service,
                                        System.Globalization.CultureInfo.InvariantCulture) == 2)
                                        return true;

                            }

                } catch { }

                // Falling back to what was asked for, where what is in force
                // could not be read. Configured and running differ only
                // between switching it on and the reboot that applies it.
                return ToInt(ReadRegistry(
                    @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\"
                        + "HypervisorEnforcedCodeIntegrity",
                    "Enabled")) == 1;

            }
        }

        // Whether the vulnerable-driver blocklist is switched on.
        //
        // Absent means on: it has been the default since the Windows 11 2022
        // update, and the value only appears once somebody has changed it.
        public static bool DriverBlocklistEnabled {
            get {
                object value = ReadRegistry(
                    @"SYSTEM\CurrentControlSet\Control\CI\Config",
                    "VulnerableDriverBlocklistEnable");

                return value == null || ToInt(value) != 0;
            }
        }

        // Whether PawnIO is installed on this machine.
        //
        // PawnIO is a signed, blocklist-clean input-output driver that loads
        // with memory integrity on — it is what LibreHardwareMonitor moved to
        // for the same reason this application needs it, and since 1.2 it is
        // what this application reaches for first. Whether it is there changes
        // the advice completely: "install this" is useful, and telling
        // somebody to install what they already have is not.
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

        public static bool SecureBootEnabled {
            get {
                return ToInt(ReadRegistry(
                    @"SYSTEM\CurrentControlSet\Control\SecureBoot\State",
                    "UEFISecureBootEnabled")) == 1;
            }
        }

        // The obstacle most worth telling the user about, examined once.
        //
        // Ordered by what they can act on. Elevation first because it is the
        // only one that is this application's own fault; memory integrity next
        // because it is the usual answer and the one with a switch in Windows
        // Security; the blocklist after it, since it is enforced by memory
        // integrity anyway and is a separate switch only when that is off.
        //
        // What this returns is a correlation, not a proven cause, and the
        // wording it produces says so. Measured on the development machine:
        // memory integrity running, the blocklist on, secure boot on — and the
        // driver loads anyway, because whether a particular binary is on that
        // list depends on its own signature rather than on its ancestry. This
        // is only ever consulted once the driver has already failed to load,
        // so naming the most likely reason is useful; asserting it as the
        // reason would be telling the user something not known to be true.
        public static Obstacle Diagnose() {

            lock(Lock) {

                if(Examined)
                    return Found;

                Examined = true;
                Found = Obstacle.Unknown;
                Detail = "";

                if(!IsElevated) {
                    Found = Obstacle.NotElevated;
                    Detail = "the process is not running with administrator rights";
                } else if(MemoryIntegrityRunning) {
                    Found = Obstacle.MemoryIntegrity;
                    Detail = "memory integrity is running, which enforces the "
                        + "vulnerable-driver blocklist";
                } else if(DriverBlocklistEnabled) {
                    Found = Obstacle.DriverBlocklist;
                    Detail = "the vulnerable-driver blocklist is switched on";
                } else if(SecureBootEnabled) {
                    Found = Obstacle.SecureBoot;
                    Detail = "secure boot is enabled";
                } else {
                    Detail = "no cause could be identified";
                }

                // Deliberately silent.
                //
                // This is an examination of the machine, not a report of a
                // failure, and it is run for both. The capability report asks
                // for it every time the System page is built — so on a machine
                // where the driver loads perfectly well, opening that page
                // wrote "Kernel driver unavailable" into the log, in a session
                // that went on to make seven hundred Embedded Controller reads.
                //
                // It shipped that way in 1.1.0 and turned up in a log from a
                // five-hour run. Whoever knows there is a problem does the
                // telling: Hw.EcInit logs the summary when the controller
                // actually could not be reached.
                return Found;

            }

        }

        // A sentence for the user, and what they can do about it
        public static string Explain() {

            Obstacle obstacle = Diagnose();

            switch(obstacle) {

                case Obstacle.NotElevated:
                    return "StarMon needs administrator rights to reach the "
                        + "Embedded Controller. Run it elevated.";

                case Obstacle.MemoryIntegrity:
                    return "The driver StarMon uses to reach the Embedded "
                        + "Controller could not be loaded, and memory integrity "
                        + "is running on this machine — which enforces "
                        + "Microsoft's vulnerable-driver list, and is the usual "
                        + "reason.\n\n"
                        + "Temperatures, battery and system readings still work. "
                        + "Fan control and the keyboard backlight do not.\n\n"
                        + Remedy();

                case Obstacle.DriverBlocklist:
                    return "The driver StarMon uses could not be loaded, and the "
                        + "vulnerable-driver blocklist is switched on.\n\n"
                        + "Monitoring still works; fan control does not.\n\n"
                        + Remedy();

                case Obstacle.SecureBoot:
                    return "The driver StarMon uses could not be loaded, and "
                        + "secure boot is enabled.\n\n" + Remedy();

                default:
                    return "The driver StarMon uses to reach the Embedded "
                        + "Controller could not be loaded"
                        + (string.IsNullOrEmpty(Detail) ? "" : " — " + Detail)
                        + ".\n\nMonitoring still works; fan control does not.";

            }

        }

        // What to do about it, which depends on what is already there.
        //
        // Telling somebody to install what they already have is worse than
        // saying nothing: it reads as though nothing had actually been checked.
        private static string Remedy() {

            return PawnIoInstalled

                // Reaching this text with PawnIO installed means StarMon tried
                // it and PawnIO itself did not work — it is preferred over the
                // blocklisted driver wherever it is present. Opening it is a
                // privileged operation, so much the likeliest cause is that
                // this process is not elevated; the driver log carries the
                // exact refusal.
                ? "PawnIO is installed here and StarMon prefers it, so "
                    + "something stopped it from opening rather than it not "
                    + "being tried. Opening it needs administrator rights — "
                    + "start StarMon elevated. The log records the exact "
                    + "refusal under \"Driver\"."

                : "Installing PawnIO (pawnio.eu) is the option worth taking: "
                    + "it is signed, it loads with these protections left on, "
                    + "and StarMon uses it in preference to the driver Windows "
                    + "is blocking. Switching memory integrity off would also "
                    + "work, and is not worth it.";

        }

        // The one-line form, for the log and the capability report
        public static string Summary() {

            Diagnose();

            return "elevated: " + (IsElevated ? "yes" : "no")
                + " · memory integrity: " + (MemoryIntegrityRunning ? "running" : "off")
                + " · driver blocklist: " + (DriverBlocklistEnabled ? "on" : "off")
                + " · secure boot: " + (SecureBootEnabled ? "on" : "off")
                + " · PawnIO: " + (PawnIoInstalled ? "installed" : "not installed");

        }

        // Forgets the examination, so a test can run it again
        internal static void Reset() {
            lock(Lock) {
                Examined = false;
                Found = Obstacle.None;
                Detail = "";
            }
        }

#region Helpers
        private static object ReadRegistry(string path, string name) {
            try {
                using(RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                    return key == null ? null : key.GetValue(name);
            } catch {
                return null;
            }
        }

        private static int ToInt(object value) {
            try {
                return value == null ? 0 : Convert.ToInt32(value,
                    System.Globalization.CultureInfo.InvariantCulture);
            } catch {
                return 0;
            }
        }
#endregion

    }

}
