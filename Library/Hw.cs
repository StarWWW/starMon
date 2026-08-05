// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Win32;
using StarMon.Hardware.Ec;
using StarMon.Hardware.Bios;

namespace StarMon.Library {

    // Implements hardware interaction routines
    // reusable between the CLI and the GUI
    public static class Hw {

#region Initialization & Termination
        // State flag
        public static bool IsInitialized { get; private set; }

        // Initializes the helper class
        public static void Initialize() {

            // Only do it once
            if(!IsInitialized) {

                // Initialization is currently handled individually
                // by calling BiosInit() and EcInit() when required

                // Done
                IsInitialized = true;

            }

        }

        // Closes the hardware
        public static void Close() {

                // Close the BIOS session, if established
                if(Bios != null)
                try {
                    Bios.Close();
                } catch { }

                // Close the embedded controller, if loaded
                if(Ec != null)
                try {
                    Ec.Close();
                } catch { }

        }
#endregion

#region BIOS
        // BIOS Control Interface
        public static IBiosCtl Bios;

        // Whether the firmware interface is genuinely there.
        //
        // False means a stand-in is installed and every call through it will
        // refuse. Nothing has to test this before calling — the refusals are
        // the same ones a partially-implemented firmware produces, and are
        // handled the same way — but the interface uses it to say so, and the
        // command line uses it to decide whether an operation was possible.
        public static bool HasBios { get; private set; }

        // Prepares the BIOS for use, and reports whether it is there.
        //
        // This used to exit the process on failure. On a machine without the
        // HP firmware interface that meant no window and no readings, while
        // the ACPI thermal zones, the battery, the drive temperature, the
        // network meter and the system metrics would all have worked — none of
        // which need it. Refusing to start is a decision for the identity
        // gate, which knows whether this is a machine the application should
        // be driving at all; a missing interface on a machine that is one is a
        // reduced application, not a stopped one.
        public static bool BiosInit() {

            Bios = BiosInterface();
            HasBios = Bios != null && Bios.IsInitialized;

            if(!HasBios) {
                Bios = new StarMon.Hardware.AbsentBiosCtl();
                Logger.Warning("Bios", "Firmware interface unavailable",
                    "BIOS-backed features are switched off for this session");
            }

            return HasBios;

        }

        // Returns the BIOS interface
        public static IBiosCtl BiosInterface() {
            var bios = BiosCtl.Instance;
            if(bios == null) {
                Logger.Error("Bios", "No interface instance", "");
                return null;
            }
            bios.Initialize();
            if(bios.IsInitialized) {
                return bios;
            } else {
                // Logged rather than shown. This is reached on every machine
                // without the HP firmware interface, and BiosInit's caller is
                // in a better position to decide whether that is worth a
                // dialog than a routine that only knows a call did not work.
                Logger.Error("Bios", "Interface would not initialize", "");
                bios.Close();
            }
            return null;
        }

        // Performs BIOS operations
        public static void BiosExec(Action<IBiosCtl> callback, IBiosCtl bios) {
            callback(bios);
        }

        // Performs BIOS operations and returns a result
        public static TResult BiosExec<TResult>(Func<IBiosCtl,TResult> callback, IBiosCtl bios) {
            return (TResult) callback(bios);
        }

        // Prepares the BIOS for use and then performs operations
        public static void BiosExec(Action<IBiosCtl> callback) {
            using(Bios = BiosInterface()) {
                if(Bios != null && Bios.IsInitialized) {
                    BiosExec(callback, Bios);
                }
            }
        }

        // Prepares the BIOS for use and then performs operations that return a result
        public static TResult BiosExec<TResult>(Func<IBiosCtl,TResult> callback) {
            using(Bios = BiosInterface()) {
                if(Bios != null && Bios.IsInitialized) {
                    return (TResult) BiosExec(callback, Bios);
                } else {
                    return default(TResult);
                }
            }
        }

        // Performs a BIOS operation and returns a numeric (possibly an enumerated or an array) result
        public static TResult BiosGet<TResult>(Func<TResult> biosMethod) {
            return Hw.BiosExec<TResult>(bios => {
                return (TResult) (object) biosMethod();
            }, Hw.Bios);
        }

        // Performs a BIOS operation and returns a struct result
        public static TResult BiosGetStruct<TResult>(Func<TResult> biosMethod) where TResult : struct {
            return Hw.BiosExec<TResult>(bios => {
                return (TResult) biosMethod();
            }, Hw.Bios);
        }

        // Sets a BIOS toggle to a Boolean value passed as a parameter
        public static void BiosSet(Action<bool> biosMethod, bool flag) {
            Hw.BiosExec(bios => {
                // Send the command to the BIOS
                biosMethod(flag);
            }, Hw.Bios);
        }

        // Sets a BIOS setting to a numerical or enumerated value passed as a parameter
        public static void BiosSet<T>(Action<T> biosMethod, T param) {
            Hw.BiosExec(bios => {
                // Send the command to the BIOS
                biosMethod((T) param);
            }, Hw.Bios);

        }

        // Sets the BIOS LED animation table based on the value passed as a parameter
        public static void BiosSetStruct(Action<BiosData.AnimTable> biosMethod, BiosData.AnimTable animTable) {
            Hw.BiosExec(bios => {
                // Send the updated animation table to the BIOS
                biosMethod(animTable);
            }, Hw.Bios);
        }

        // Sets the BIOS keyboard backlight color table based on the value passed as a parameter
        public static void BiosSetStruct(Action<BiosData.ColorTable> biosMethod, BiosData.ColorTable colorTable) {
            Hw.BiosExec(bios => {
                // Send the updated color table to the BIOS
                biosMethod(colorTable);
            }, Hw.Bios);
        }

        // Sets the BIOS fan table based on the value passed as a parameter
        public static void BiosSetStruct(Action<BiosData.FanTable> biosMethod, BiosData.FanTable fanTable) {
            Hw.BiosExec(bios => {
                // Send the updated fan table to the BIOS
                biosMethod(fanTable);
            }, Hw.Bios);
        }

        // Sets the BIOS GPU power settings based on the value passed as a parameter
        public static void BiosSetStruct(Action<BiosData.GpuPowerData> biosMethod, BiosData.GpuPowerData gpuPowerData) {
            Hw.BiosExec(bios => {
                // Not a mistake: seems this need to run twice to take effect,
                // at least in certain scenarios (such as switching PPAB off)
                biosMethod(gpuPowerData);
                Thread.Sleep(Config.GpuPowerSetInterval);
                biosMethod(gpuPowerData);
            }, Hw.Bios);
        }
#endregion

#region Embedded Controller
        // Embedded Controller interface
        public static IEmbeddedController Ec;

        // Whether the Embedded Controller is genuinely reachable
        public static bool HasEc { get; private set; }

        // Prepares the embedded controller for use, and reports whether it is
        // reachable.
        //
        // The failure this handles is not rare and not the user's mistake:
        // reaching the controller needs a kernel driver, the driver this
        // application carries is on Microsoft's vulnerable-driver list, and
        // that list has been enforced by default since the Windows 11 2022
        // update. Exiting told those users nothing they could act on.
        //
        // See Hardware/CodeIntegrity.cs for what is in the way and what to
        // say about it.
        public static bool EcInit() {

            Ec = EcInterface();
            HasEc = Ec != null && Ec.IsInitialized;

            if(!HasEc) {
                Ec = new StarMon.Hardware.AbsentEmbeddedController();
                Logger.Warning("Ec", "Controller unreachable",
                    StarMon.Hardware.CodeIntegrity.Summary());
            }

            return HasEc;

        }

        // Returns the Embedded Controller interface
        public static IEmbeddedController EcInterface() {
            var ec = EmbeddedController.Instance;
            if(ec == null) {
                Logger.Error("Ec", "No controller instance", "");
                return null;
            }
            ec.Initialize();
            if(ec.IsInitialized) {
                return ec;
            } else {
                // Logged rather than shown, for the same reason as the BIOS
                // above: the driver being blocked is the common case now, and
                // what to say about it is CodeIntegrity's job.
                Logger.Error("Ec", "Controller would not initialize", "");
                ec.Close();
            }
            return null;
        }

        // Whether the last attempt to take the controller lock failed.
        //
        // The lock is taken per register access, and a failure used to be
        // answered with App.Error every single time. In the interface that is
        // a modal dialog: another application holding the lock — the
        // manufacturer's own tray program, or any hardware monitor, all of
        // which take the same named mutex — put up one dialog per sensor per
        // tick, for as long as it held it. The hardware report reads 256
        // registers, so it would have produced 256.
        //
        // The condition is worth telling the user about once. Telling them
        // about it several hundred times is not more information, it is a
        // machine they cannot use.
        private static bool EcLockReported;

        // How many refusals have actually reached the user. Counted so the
        // once-per-episode rule can be asserted rather than assumed.
        internal static int EcLockReports { get; private set; }

        internal static void ResetEcLockReports() {
            EcLockReports = 0;
            EcLockReported = false;
        }

        // Reports a refused lock at most once per episode, and logs every one
        private static void ReportEcLockFailure() {

            Logger.Warning("Ec", "Controller lock refused",
                "another application is holding it");

            if(EcLockReported)
                return;

            EcLockReported = true;
            EcLockReports++;
            App.Error("ErrEcLock");

        }

        // Clears the latch, so the next episode is reported again
        private static void EcLockSucceeded() {
            EcLockReported = false;
        }

        // Runs operations while the Embedded Controller is locked for exclusive use
        public static void EcExec(Action<IEmbeddedController> callback, IEmbeddedController ec) {
            if(ec.Request(Config.EcMutexTimeout)) {
                EcLockSucceeded();
                try {
                    callback(ec);
                } finally {
                    ec.Release();
                }
            }
            else {
                ReportEcLockFailure();
            }
        }

        // Runs operations while the Embedded Controller is locked for exclusive use and returns a result
        public static TResult EcExec<TResult>(Func<IEmbeddedController,TResult> callback, IEmbeddedController ec) {
            if(ec.Request(Config.EcMutexTimeout)) {
                EcLockSucceeded();
                try {
                    return (TResult) callback(ec);
                } finally {
                    ec.Release();
                }
            } else {
                ReportEcLockFailure();
                return default(TResult);
            }
        }

        // Prepares the Embedded Controller and then runs operations while it is locked for exclusive use
        public static void EcExec(Action<IEmbeddedController> callback) {
            using(Ec = EcInterface()) {
                if(Ec != null) {
                    EcExec(callback, Ec);
                }
            }
        }

        // Prepares the Embedded Controller, runs operations while it is locked for exclusive use, and returns a result
        public static TResult EcExec<TResult>(Func<IEmbeddedController,TResult> callback) {
            using(Ec = EcInterface()) {
                if(Ec != null) {
                    return (TResult) EcExec(callback, Ec);
                } else {
                    return default(TResult);
                }
            }
        }

        // Retrieves the value of a specific byte-sized register
        public static byte EcGetByte(byte register) {
            return Hw.EcExec<byte>(ec => {
                return ec.ReadByte(register);
            }, Hw.Ec);
        }

        // Prints out the value of a little-endian word stored in two consecutive registers
        public static ushort EcGetWord(byte register) {
            return Hw.EcExec<ushort>(ec => {
                return ec.ReadWord(register);
            }, Hw.Ec);
        }

        // The same two reads, reporting whether the exchange actually
        // happened.
        //
        // EcGetByte hands back zero both for a register that reads zero and
        // for one that never answered, and a caller cannot tell them apart.
        // That matters for a register the board does not carry: it reads zero
        // for the life of the process, and anything that treats zero as an
        // answer will keep paying for the exchange forever. A failure to take
        // the lock counts as a failed exchange, which is what the default
        // false from EcExec already gives.
        public static bool EcTryGetByte(byte register, out byte value) {
            byte read = 0;
            bool ok = Hw.EcExec<bool>(ec => ec.TryReadByte(register, out read), Hw.Ec);
            value = read;
            return ok;
        }

        public static bool EcTryGetWord(byte register, out ushort value) {
            ushort read = 0;
            bool ok = Hw.EcExec<bool>(ec => ec.TryReadWord(register, out read), Hw.Ec);
            value = read;
            return ok;
        }

        // Performs an Embedded Controller operation
        // and returns a byte-sized numeric (possibly an enumerated) result
        public static TResult EcGet<TResult>(byte register) {
            return (TResult) (object) EcGetByte(register);
        }

        // Sets the value of a specific byte-sized register
        public static void EcSetByte(byte register, byte value) {
            Hw.EcExec(ec => {
                ec.WriteByte(register, value);
            }, Hw.Ec);
        }

        // Sets the value of a little-endian word stored in two consecutive registers
        public static void EcSetWord(byte register, ushort value) {
            Hw.EcExec(ec => {
                ec.WriteWord(register, value);
            }, Hw.Ec);
        }

        // Sets the value of a specific byte-sized register
        public static void EcSet(byte register, byte value) {
            EcSetByte(register, value);
        }

        // Sets the value of a little-endian word stored in two consecutive registers
        public static void EcSet(byte register, ushort value) {
            EcSetWord(register, value);
        }
#endregion

#region Graphics
        // nVidia multiplexer states
        public enum NvMuxState : int {
            Optimus  = 0x00000001,  // Software-switching
            Discrete = 0x00000002   // Discrete GPU only
        }

        // Retrieves the current nVidia multiplexer state from the Registry
        public static NvMuxState NvMuxGetState() {
            try
            {
                using(RegistryKey key = Registry.LocalMachine.OpenSubKey(Config.RegMuxKey, true))
                {
                    if (key == null) return NvMuxState.Optimus; // Default fallback for Victus/Unsupported
                    return (NvMuxState) (int) key.GetValue(Config.RegMuxValue, (int)NvMuxState.Optimus);
                }
            }
            catch
            {
                return NvMuxState.Optimus;
            }
        }
#endregion

#region Tasks
        // Returns the status of a specific task
        public static bool TaskGet(Config.TaskId task) {
            return Os.HasTask(Config.TaskFolder, Config.Task[task][0]);
        }

        // Installs or removes a specific task
        public static void TaskSet(Config.TaskId task, bool flag) {
            using WmiEvent wmiEvent = new WmiEvent();
            string taskName = Enum.GetName(typeof(Config.TaskId), task);

            // Remove the given task first, regardless
            try {

                // Delete the task from the Task Scheduler
                Os.DeleteTask(Config.TaskFolder, Config.Task[task][0]);

            } catch { }

            // The GUI task simply uses a logon trigger,
            // so no further steps are needed for removal
            if(task != Config.TaskId.Gui) {

                    // Remove WMI event triggers for the task
                    wmiEvent.DeleteBinding(Config.AppName + taskName + Config.WmiEventSuffixFilter, WmiEvent.BindingLookup.ByFilter);
                    wmiEvent.DeleteConsumer(Config.AppName + taskName + Config.WmiEventSuffixConsumer);
                    wmiEvent.DeleteFilter(Config.AppName + taskName + Config.WmiEventSuffixFilter);

                }

            // If asked to add the task
            if(flag) {

                try {

                    // Every task is added to the Task Scheduler first of all
                    Os.AddTask(
                        Config.TaskFolder, Config.Task[task][0], "", // Executable path, defaults to current process if empty
                        Config.Task[task][1], task == Config.TaskId.Gui ? true : false); // Logon trigger for the GUI task only

                } catch { }

                // The GUI task, triggered by logon, does not have any extra steps
                if(task != Config.TaskId.Gui) {

                    // Other tasks depend on the WMI event filter binding
                    wmiEvent.CreateBinding(

                        wmiEvent.CreateConsumer(new Dictionary<string, object>() {
                            ["CommandLineTemplate"] = Config.TaskRunPath + " "
                                + Config.TaskRunArgs + "\"" + Config.AppName + " " + taskName + "\"",
                            ["ExecutablePath"] = Config.TaskRunPath,
                            ["Name"] = Config.AppName + taskName + Config.WmiEventSuffixConsumer }),

                        wmiEvent.CreateFilter(new Dictionary<string, object>() {
                                ["EventNameSpace"] = Config.Task[task][2],
                                ["Query"] = Config.Task[task][3].Replace("\\", "\\\\"), // The WMI query needs double backlashes
                                ["QueryLanguage"] = Config.WmiQueryLang,
                                ["Name"] = Config.AppName + taskName + Config.WmiEventSuffixFilter }));

                }

            }

        }
#endregion

    }

}
