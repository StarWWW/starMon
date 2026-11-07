// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using StarMon.AppService;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Cpu;
using StarMon.Library;
using StarMon.Ui.Shell;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Windows {

    // Drives the Advanced page: reads what the firmware and the processor say,
    // and carries out the firmware commands the page offers.
    //
    // Every write is confirmed first, with a default of No, then handed to the
    // hardware queue like every other firmware call in the window. Reads of the
    // firmware go through the same queue. The processor's power registers are
    // read once a second, off the interface thread, and only while the page is
    // on show: a closed page has no reason to keep asking.
    public sealed class AdvancedController {

        private readonly Dispatcher Dispatcher;
        private readonly AdvancedViewModel Model;
        private readonly Func<bool> IsOnShow;
        private readonly DispatcherTimer Ticker;
        private readonly PowerRegisters.Sampler Sampler = new PowerRegisters.Sampler();

        private bool Loaded;
        private int Reading;  // 1 while a register read is in flight

        public AdvancedController(Dispatcher dispatcher, AdvancedViewModel model, Func<bool> isOnShow) {

            this.Dispatcher = dispatcher;
            this.Model = model;
            this.IsOnShow = isOnShow;

            this.Ticker = new DispatcherTimer(DispatcherPriority.Background, dispatcher) {
                Interval = TimeSpan.FromSeconds(1)
            };
            this.Ticker.Tick += delegate { Tick(); };

            this.Model.ActionRequested += OnAction;

        }

        private static string Text(string key) {
            return Config.Locale.Get(key);
        }

        private static string Text(string key, params object[] args) {
            return string.Format(Config.Locale.Get(key), args);
        }

        // The page has come on show
        public void Shown() {

            if(!this.Loaded) {
                this.Loaded = true;
                Reload();
            }

            Tick();
            this.Ticker.Start();

        }

#region Processor registers
        private void Tick() {

            if(!this.IsOnShow()) {
                this.Ticker.Stop();
                return;
            }

            // One read at a time: a driver call that stalls must not pile up
            // a queue of them behind it
            if(Interlocked.Exchange(ref this.Reading, 1) == 1)
                return;

            ThreadPool.QueueUserWorkItem(delegate {

                PowerRegisters.Snapshot snapshot;
                try {
                    snapshot = this.Sampler.Read();
                } catch {
                    snapshot = new PowerRegisters.Snapshot();
                }

                this.Dispatcher.BeginInvoke((Action) delegate {
                    Interlocked.Exchange(ref this.Reading, 0);
                    AdvancedViewModel.Fill(this.Model.Registers, Describe(snapshot));
                });

            });

        }

        // The rows the page shows for one reading of the registers
        internal static List<DetailRowViewModel> Describe(PowerRegisters.Snapshot s) {

            List<DetailRowViewModel> rows = new List<DetailRowViewModel>();

            if(!s.Readable) {
                rows.Add(Row("GuiWpfAdvRegUnavailable", Text("GuiWpfAdvRegUnavailableValue")));
                return rows;
            }

            rows.Add(Row("GuiWpfAdvRegPower",
                s.PackageWatts >= 0 ? s.PackageWatts.ToString("0.0") + " W" : "—"));
            rows.Add(Row("GuiWpfAdvRegPkgPl1", Limit(s.Package.Readable, s.Package.Pl1, true)));
            rows.Add(Row("GuiWpfAdvRegPkgPl2", Limit(s.Package.Readable, s.Package.Pl2, false)));
            rows.Add(Row("GuiWpfAdvRegPsysPl1", Limit(s.Platform.Readable, s.Platform.Pl1, true)));
            rows.Add(Row("GuiWpfAdvRegPsysPl2", Limit(s.Platform.Readable, s.Platform.Pl2, false)));
            rows.Add(Row("GuiWpfAdvRegLock",
                !s.Package.Readable ? "—"
                    : s.Package.Locked ? Text("GuiWpfAdvLocked") : Text("GuiWpfAdvUnlocked")));
            rows.Add(Row("GuiWpfAdvRegReasons",
                s.HasReasons ? PowerRegisters.DescribeLimitReasons(s.ReasonsNow) : "—"));
            rows.Add(Row("GuiWpfAdvRegCtdp",
                s.ConfigTdpLevel >= 0 ? s.ConfigTdpLevel.ToString() : "—"));
            rows.Add(Row("GuiWpfAdvRegOcLock",
                s.OverclockingLocked < 0 ? "—"
                    : s.OverclockingLocked == 1 ? Text("GuiWpfAdvLocked") : Text("GuiWpfAdvUnlocked")));

            return rows;

        }

        private static string Limit(bool readable, PowerRegisters.LimitHalf half, bool window) {

            if(!readable)
                return "—";

            if(!half.Enabled)
                return Text("GuiWpfAdvOffValue");

            return half.Watts.ToString("0.#") + " W"
                + (window ? " · " + half.WindowSeconds.ToString("0.#") + " s" : "");

        }

        private static DetailRowViewModel Row(string key, string value) {
            return DetailRowViewModel.Keyed(key, value, null);
        }
#endregion

#region Firmware
        // Reads what the firmware will say, through the hardware queue
        private void Reload() {

            this.Model.IsBusy = true;

            HardwareQueue.Default.Post(delegate {

                List<DetailRowViewModel> rows = new List<DetailRowViewModel>();
                BiosData.SystemData system = default(BiosData.SystemData);
                bool hasSystem = false;
                byte memoryOc = 0;
                bool hasMemoryOc = false;
                string fanTable = null;
                BiosData.FanTable table = default(BiosData.FanTable);
                string anim = null;

                Try(() => {
                    system = Hw.BiosGetStruct<BiosData.SystemData>(Hw.Bios.GetSystem);
                    hasSystem = true;
                });

                Try(() => rows.Add(Row("GuiWpfAdvFwBornDate", Hw.BiosGet(Hw.Bios.GetBornDate) ?? "—")));
                Try(() => rows.Add(Row("GuiWpfAdvFwAdapter",
                    Hw.BiosGet<BiosData.AdapterStatus>(Hw.Bios.GetAdapter).ToString())));
                Try(() => rows.Add(Row("GuiWpfAdvFwThrottling",
                    Hw.BiosGet<BiosData.Throttling>(Hw.Bios.GetThrottling).ToString())));
                Try(() => rows.Add(Row("GuiWpfAdvFwTemp",
                    Hw.BiosGet<byte>(Hw.Bios.GetTemperature) + " °C")));
                Try(() => rows.Add(Row("GuiWpfAdvFwOverclock",
                    YesNo(Hw.BiosGet<byte>(Hw.Bios.HasOverclock)))));
                Try(() => {
                    memoryOc = Hw.BiosGet<byte>(Hw.Bios.HasMemoryOverclock);
                    hasMemoryOc = true;
                    rows.Add(Row("GuiWpfAdvFwMemoryOverclock", YesNo(memoryOc)));
                });
                Try(() => rows.Add(Row("GuiWpfAdvFwUndervolt",
                    YesNo(Hw.BiosGet<byte>(Hw.Bios.HasUndervoltBios)))));

                if(hasSystem) {
                    rows.Add(Row("GuiWpfAdvFwBiosOc", system.BiosOc.ToString()));
                    rows.Add(Row("GuiWpfAdvFwThermalPolicy", system.ThermalPolicy.ToString()));
                    rows.Add(Row("GuiWpfAdvFwPl4Default", system.DefaultCpuPowerLimit4 + " W"));
                    rows.Add(Row("GuiWpfAdvFwPlGpuDefault",
                        system.DefaultCpuPowerLimitWithGpu == 0 ? Text("GuiWpfAdvNotDefined")
                            : system.DefaultCpuPowerLimitWithGpu + " W"));
                }

                Try(() => {
                    table = Hw.BiosGetStruct<BiosData.FanTable>(Hw.Bios.GetFanTable);
                    fanTable = AdvancedViewModel.FormatFanTable(table);
                });

                Try(() => anim = AdvancedViewModel.FormatAnim(
                    Hw.BiosGetStruct<BiosData.AnimTable>(Hw.Bios.GetAnimTable)));

                this.Dispatcher.BeginInvoke((Action) delegate {

                    AdvancedViewModel.Fill(this.Model.Firmware, rows);

                    if(hasSystem) {
                        this.Model.Pl4Default = Text("GuiWpfAdvDefault", system.DefaultCpuPowerLimit4 + " W");
                        this.Model.PlGpuDefault = Text("GuiWpfAdvDefault",
                            system.DefaultCpuPowerLimitWithGpu == 0 ? Text("GuiWpfAdvNotDefined")
                                : system.DefaultCpuPowerLimitWithGpu + " W");
                    }

                    // XMP is offered where the firmware admits to memory
                    // overclocking, and where it could not be asked
                    this.Model.CanXmp = !hasMemoryOc || memoryOc != 0;
                    if(hasMemoryOc && memoryOc == 0)
                        this.Model.MemoryNote = Text("GuiWpfAdvXmpUnsupported");

                    if(fanTable != null) {
                        this.Model.FanTableText = fanTable.Replace("\n", Environment.NewLine);
                        FillFanRows(table);
                    } else {
                        this.Model.FanTableNote = Text("GuiWpfAdvUnreadable");
                    }

                    if(anim != null)
                        this.Model.AnimText = anim;
                    else
                        this.Model.AnimNote = Text("GuiWpfAdvUnreadable");

                    this.Model.IsBusy = false;

                });

            }, "Reading the firmware for the Advanced page");

        }

        private void FillFanRows(BiosData.FanTable table) {

            List<DetailRowViewModel> rows = new List<DetailRowViewModel>();
            int count = Math.Min((int) table.LevelCount, table.Level == null ? 0 : table.Level.Length);

            for(int i = 0; i < count; i++) {
                BiosData.FanLevel level = table.Level[i];
                rows.Add(new DetailRowViewModel(
                    level.Temperature + " °C",
                    (level.Fan1Level * 100) + " · " + (level.Fan2Level * 100) + " rpm"));
            }

            AdvancedViewModel.Fill(this.Model.FanTableRows, rows);

        }

        private static string YesNo(byte value) {
            return value != 0 ? Text("GuiWpfAdvYes") + " (" + value + ")" : Text("GuiWpfAdvNo");
        }

        // One firmware question, whose refusal is an answer rather than a
        // reason to stop asking the others
        private static void Try(Action read) {
            try { read(); } catch(Exception e) {
                Logger.Info("Window", "A firmware read on the Advanced page was refused", e.Message);
            }
        }
#endregion

#region Commands
        private void OnAction(AdvancedAction action) {

            byte watts;

            switch(action) {

                case AdvancedAction.Reload:
                    Reload();
                    return;

                case AdvancedAction.CpuPl1:
                    if(!Watts(this.Model.Pl1Text, out watts))
                        return;
                    Send(Text("GuiWpfAdvConfirmPl1", watts),
                        () => Hw.BiosSet<byte>(Hw.Bios.SetCpuPower1, watts),
                        "CPU PL1 set to " + watts + " W through the firmware",
                        note => this.Model.CpuPowerNote = note,
                        Text("GuiWpfAdvSentPl1", watts));
                    return;

                case AdvancedAction.CpuPl4:
                    if(!Watts(this.Model.Pl4Text, out watts))
                        return;
                    Send(Text("GuiWpfAdvConfirmPl4", watts),
                        () => Hw.BiosSet<byte>(Hw.Bios.SetCpuPower4, watts),
                        "CPU PL4 set to " + watts + " W through the firmware",
                        note => this.Model.CpuPowerNote = note,
                        Text("GuiWpfAdvSentPl4", watts));
                    return;

                case AdvancedAction.CpuPlGpu:
                    if(!Watts(this.Model.PlGpuText, out watts))
                        return;
                    Send(Text("GuiWpfAdvConfirmPlGpu", watts),
                        () => Hw.BiosSet<byte>(Hw.Bios.SetCpuPowerWithGpu, watts),
                        "CPU power limit with the GPU set to " + watts + " W through the firmware",
                        note => this.Model.CpuPowerNote = note,
                        Text("GuiWpfAdvSentPlGpu", watts));
                    return;

                case AdvancedAction.XmpOn:
                case AdvancedAction.XmpOff:
                    bool xmp = action == AdvancedAction.XmpOn;
                    Send(Text(xmp ? "GuiWpfAdvConfirmXmpOn" : "GuiWpfAdvConfirmXmpOff"),
                        () => Hw.BiosSet(Hw.Bios.SetMemoryXmp, xmp),
                        "Memory XMP " + (xmp ? "enabled" : "disabled") + " through the firmware",
                        note => this.Model.MemoryNote = note,
                        Text(xmp ? "GuiWpfAdvSentXmpOn" : "GuiWpfAdvSentXmpOff"));
                    return;

                case AdvancedAction.IdleOn:
                case AdvancedAction.IdleOff:
                    bool idle = action == AdvancedAction.IdleOn;
                    Send(Text(idle ? "GuiWpfAdvConfirmIdleOn" : "GuiWpfAdvConfirmIdleOff"),
                        () => Hw.BiosSet(Hw.Bios.SetIdle, idle),
                        "Idle mode " + (idle ? "enabled" : "disabled") + " through the firmware",
                        note => this.Model.MemoryNote = note,
                        Text(idle ? "GuiWpfAdvSentIdleOn" : "GuiWpfAdvSentIdleOff"));
                    return;

                case AdvancedAction.FanTable:
                    if(!AdvancedViewModel.TryParseFanTable(this.Model.FanTableText, out BiosData.FanTable table)) {
                        this.Model.FanTableNote = Text("GuiWpfAdvBadFanTable");
                        return;
                    }
                    Send(Text("GuiWpfAdvConfirmFanTable", table.LevelCount),
                        () => Hw.BiosSetStruct(Hw.Bios.SetFanTable, table),
                        "Fan table of " + table.LevelCount + " steps sent to the firmware",
                        note => this.Model.FanTableNote = note,
                        Text("GuiWpfAdvSentFanTable", table.LevelCount),
                        () => FillFanRows(table));
                    return;

                case AdvancedAction.Anim:
                    if(!AdvancedViewModel.TryParseAnim(this.Model.AnimText, out BiosData.AnimTable anim)) {
                        this.Model.AnimNote = Text("GuiWpfAdvBadAnim");
                        return;
                    }
                    Send(Text("GuiWpfAdvConfirmAnim"),
                        () => Hw.BiosSetStruct(Hw.Bios.SetAnimTable, anim),
                        "Keyboard animation table sent to the firmware",
                        note => this.Model.AnimNote = note,
                        Text("GuiWpfAdvSentAnim"));
                    return;

            }

        }

        private bool Watts(string text, out byte watts) {

            if(AdvancedViewModel.TryParseWatts(text, out watts))
                return true;

            this.Model.CpuPowerNote = Text("GuiWpfAdvBadWatts");
            return false;

        }

        // Asks, then queues the write, then says what became of it
        private void Send(string question, Action write, string what,
            Action<string> report, string done, Action after = null) {

            if(!Dialogs.Confirm(question))
                return;

            report(Text("GuiWpfAdvSending"));

            // The queue logs the outcome under the description given it; the
            // refusal is passed back on so it logs a failure, not a success
            HardwareQueue.Default.Post(delegate {

                try {
                    write();
                } catch(Exception e) {
                    string refused = Text("GuiWpfAdvRefused", e.Message);
                    this.Dispatcher.BeginInvoke((Action) delegate { report(refused); });
                    throw;
                }

                this.Dispatcher.BeginInvoke((Action) delegate {
                    report(done);
                    if(after != null)
                        after();
                });

            }, what);

        }
#endregion

    }

}
