// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon {

    // Hands the fans back to the firmware when the interface ends without
    // doing it itself.
    //
    // A fan level set through the firmware's WMI call stays in force until
    // 0xFF is written - measured on an HP 8DCF, where it held with no
    // countdown at all. The application writes 0xFF on its way out, but a
    // crash, a kill from the task manager or a hang that ends in one never
    // reaches that line, and the fans would then hold whatever level was last
    // asked for, for as long as the machine stays on.
    //
    // The firmware's own countdown used to be the answer, and it is a
    // register: with the controller left to Windows there is no countdown to
    // keep. So a second, tiny process waits on the interface's process handle -
    // which Windows signals however the process ends - and makes the two
    // release calls itself. It touches nothing else: not the mode, not the
    // graphics power, nothing that was the user's choice rather than a level
    // being held.
    internal static class Watchdog {

        internal const string Argument = "-watchdog";

        // Starts the watchdog for this process. Quiet on failure beyond the
        // log: the application without it is the application as it was.
        public static void Start() {

            try {

                ProcessStartInfo info = new ProcessStartInfo(Config.AppFile,
                    Argument + " " + Environment.ProcessId) {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using(Process.Start(info)) { }

                Logger.Info("Watchdog", "Started",
                    "the fans are handed back if this process ends without doing it");

            } catch(Exception e) {
                Logger.Warning("Watchdog", "Could not be started", e.Message);
            }

        }

        // The watchdog itself: waits for the process it was given, then
        // releases the fans
        public static int Run(string[] args) {

            int pid;
            if(args.Length < 2 || !int.TryParse(args[1], out pid))
                return 2;

            try {
                using(Process watched = Process.GetProcessById(pid))
                    watched.WaitForExit();
            } catch {
                // Already gone, which is the same as having just gone
            }

            if(Hw.BiosInit())
                Release(Hw.Bios);

            return 0;

        }

        // The two calls that leave nothing held: no custom level on any fan,
        // and the maximum-speed switch off. Exactly what the application does
        // on a clean exit, so doing it again after one changes nothing.
        internal static void Release(StarMon.Hardware.Bios.IBiosCtl bios) {

            try {
                Hw.BiosSet(bios.SetFanLevel, new byte[] { Byte.MaxValue, Byte.MaxValue });
            } catch { }

            try {
                Hw.BiosSet(bios.SetMaxFan, false);
            } catch { }

            Logger.Info("Watchdog", "Fans handed back to the firmware",
                "the interface process has ended");

        }

    }

}
