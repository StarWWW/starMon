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

                Logger.Warning("Driver", "Kernel driver unavailable", Detail);

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
                    return "Windows is blocking the driver StarMon uses to reach "
                        + "the Embedded Controller, because memory integrity is "
                        + "switched on and the driver is on Microsoft's "
                        + "vulnerable-driver list.\n\n"
                        + "Temperatures, battery and system readings still work. "
                        + "Fan control and the keyboard backlight do not.\n\n"
                        + "Installing PawnIO (pawnio.eu) gives StarMon a signed "
                        + "driver it can use with memory integrity left on, which "
                        + "is the option worth taking.";

                case Obstacle.DriverBlocklist:
                    return "Windows is blocking the driver StarMon uses, because "
                        + "the vulnerable-driver blocklist is switched on.\n\n"
                        + "Monitoring still works; fan control does not. "
                        + "Installing PawnIO (pawnio.eu) resolves this without "
                        + "turning any protection off.";

                case Obstacle.SecureBoot:
                    return "The driver StarMon uses could not be loaded, and "
                        + "secure boot is enabled. Installing PawnIO "
                        + "(pawnio.eu) gives StarMon a signed driver instead.";

                default:
                    return "The driver StarMon uses to reach the Embedded "
                        + "Controller could not be loaded"
                        + (string.IsNullOrEmpty(Detail) ? "" : " — " + Detail)
                        + ".\n\nMonitoring still works; fan control does not.";

            }

        }

        // The one-line form, for the log and the capability report
        public static string Summary() {

            Diagnose();

            return "elevated: " + (IsElevated ? "yes" : "no")
                + " · memory integrity: " + (MemoryIntegrityRunning ? "running" : "off")
                + " · driver blocklist: " + (DriverBlocklistEnabled ? "on" : "off")
                + " · secure boot: " + (SecureBootEnabled ? "on" : "off");

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
