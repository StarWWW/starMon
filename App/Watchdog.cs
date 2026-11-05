// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using Microsoft.Management.Infrastructure;
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
        //
        // Started through WMI rather than as a child where that works, so it
        // is not part of this process's tree. As a child it went down with
        // the interface whenever the tree went: Task Manager's "End task" on
        // the application, "End process tree", the Task Scheduler ending the
        // logon task - exactly the ends it is there for - and the fans stayed
        // held. Created by WMI it has a parent of its own and sits outside
        // the scheduler's job. Where that fails, as a child, as before.
        public static void Start() {

            string arguments = Argument + " " + Environment.ProcessId;

            try {

                uint pid = StartOutOfTree("\"" + Config.AppFile + "\" " + arguments);
                Logger.Info("Watchdog", "Started",
                    "process " + pid + ", outside this process's tree; "
                    + "the fans are handed back if this process ends without doing it");
                return;

            } catch(Exception e) {
                Logger.Warning("Watchdog", "Could not be started on its own, starting it as a child",
                    e.Message);
            }

            try {

                ProcessStartInfo info = new ProcessStartInfo(Config.AppFile, arguments) {
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

        // Win32_Process.Create, which starts the process from the WMI host
        // under the caller's own token - elevated, as this one is - and
        // returns its id
        private static uint StartOutOfTree(string commandLine) {

            using(CimSession session = CimSession.Create(null))
            using(CimMethodParametersCollection parameters = new CimMethodParametersCollection()) {

                parameters.Add(CimMethodParameter.Create("CommandLine", commandLine, CimFlags.In));
                parameters.Add(CimMethodParameter.Create("CurrentDirectory",
                    System.IO.Path.GetDirectoryName(Config.AppFile), CimFlags.In));

                using(CimMethodResult result = session.InvokeMethod(
                    @"root\cimv2", "Win32_Process", "Create", parameters)) {

                    uint code = Convert.ToUInt32(result.ReturnValue.Value);
                    if(code != 0)
                        throw new InvalidOperationException("Win32_Process.Create returned " + code);

                    return Convert.ToUInt32(result.OutParameters["ProcessId"].Value);

                }

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

            // A few tries at reaching the firmware: the interface ending with
            // the machine resuming, or WMI restarting, is when the first one
            // is likeliest to find nothing there
            for(int attempt = 0; attempt < 5; attempt++) {
                if(Hw.BiosInit()) {
                    Release(Hw.Bios);
                    return 0;
                }
                System.Threading.Thread.Sleep(2000);
            }

            return 1;

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
