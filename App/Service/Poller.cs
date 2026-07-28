// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Threading;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.AppService {

    // Gathers a Reading from the hardware, off the interface thread.
    //
    // The WinForms build did this with five methods on the window, each with
    // its own "am I already running" flag, each starting a task, each
    // marshalling its results back by hand. That is five copies of the same
    // careful dance, and five chances to get it wrong — which is what the
    // flags were for, and why one of them sticking left a panel frozen with
    // nothing in the log to say why.
    //
    // Here it is one place. A single reading is taken at a time, the result is
    // handed over whole, and whoever asked for it decides which thread to
    // apply it on.
    public sealed class Poller {

        private readonly Platform Platform;

        // Whether a reading is already being taken. A tick that arrives while
        // the last one is still talking to the Embedded Controller is dropped
        // rather than queued: the readings are a live view, so a backlog of
        // them has nothing anyone wants in it, and queueing turns a slow
        // machine into an unresponsive one.
        private int Busy;

        // Machine identity, read once. The model and BIOS version do not
        // change while the application runs, and asking WMI for them is slow
        // enough that doing it every tick would be felt.
        private string SystemModel;
        private string BiosVersion;
        private string CpuName = "";
        private string GpuName = "";

        // The slow-moving firmware answers, refreshed on a slower cadence.
        //
        // The keyboard colour and the graphics power level are WMI BIOS
        // calls, each a round trip through the firmware, and neither changes
        // between the moments something changes them. Asking every second
        // multiplied the BIOS traffic for answers that were the same answer —
        // and every call this application does not make is one that cannot
        // contend with the Embedded Controller either.
        private const int SlowEvery = 5;
        private int TickCount;
        private int[] KbdColorsCache;
        private bool GpuPowerSupportedCache;
        private GpuPower GpuPowerCache = GpuPower.Base;

        // Slow-changing readings behind heavy calls, cached between slow
        // ticks. The disk temperature opens a device handle and issues an
        // IOCTL, the Wi-Fi query walks the wireless interfaces, and the power
        // plan is a registry-backed lookup — none of them move second to
        // second, and paying for them every tick is load the machine spends
        // for an answer that did not change.
        private string PowerPlanCache = "";
        private int DiskTemperatureCache = -1;
        private bool WifiConnectedCache;
        private string WifiSsidCache = "";
        private int WifiSignalCache = -1, WifiRxCache = -1, WifiTxCache = -1;

        // The firmware's own published sensors, which are a WMI enumeration
        // rather than a register read and so belong firmly on the slow tick
        private Hardware.HpSensors.Sensor[] HpSensorCache;
        private Hardware.AcpiThermal.Zone[] ThermalZoneCache;
        private string[] SensorFaultCache;

        public Poller(Platform platform) {
            this.Platform = platform;
        }

        // Raised on a background thread with a complete reading
        public event Action<Reading> Read;

        // Whether the fan program is running, which the caller knows and this
        // class does not: it is deliberately given no reference back to the
        // application, so it cannot start doing anything but read.
        public Func<string> GetProgramName;
        public Func<bool> IsProgramRunning;
        public Func<bool> IsThermalProtectionActive;

        // Whether anyone is looking at the readings. Used to decide whether
        // waking a sleeping discrete card for a temperature is worth the
        // battery it costs — see the GpuPollOnBattery handling in Gather.
        public Func<bool> IsWindowVisible;

        // Whether the machine was on mains at the previous reading. The
        // battery is read further down than the graphics card is, and the
        // decision about the card needs it now rather than in a tick's time;
        // mains does not come and go inside one second, so last tick's answer
        // is this tick's answer. Starts true so the first reading is complete.
        private bool IsOnAcCache = true;

        // Takes a reading unless one is already being taken
        public void Request() {

            if(Interlocked.CompareExchange(ref this.Busy, 1, 0) != 0)
                return;

            ThreadPool.QueueUserWorkItem(delegate {

                try {

                    Reading reading = Gather();

                    Action<Reading> handler = this.Read;
                    if(handler != null)
                        handler(reading);

                } catch(Exception e) {

                    // A hardware hiccup during a periodic read must never take
                    // the application down, but it should leave a trace
                    Logger.Error("Poller", "Taking a reading failed", e.Message);

                } finally {

                    Interlocked.Exchange(ref this.Busy, 0);

                }

            });

        }

        // Each reading is taken behind its own guard. One sensor a machine
        // does not have must not cost the reading everything after it, which
        // is what a single try around the whole method would do.
        private Reading Gather() {

            Reading reading = new Reading();

            // One in every few ticks refreshes the slow-changing readings; the
            // rest reuse what those found. Computed once up front so every
            // cached section agrees on which kind of tick this is.
            bool slowTick = this.TickCount % SlowEvery == 0;
            this.TickCount++;

            try { this.Platform.UpdateTemperature(true); } catch { }
            try { this.Platform.UpdateFans(); } catch { }

            try {
                reading.MaxTemperature = this.Platform.GetMaxTemperature(false);
            } catch { }

            try {
                var board = new System.Collections.Generic.List<
                    System.Collections.Generic.KeyValuePair<string, int>>(8);

                for(int i = 0; i < this.Platform.Temperature.Length; i++) {

                    IPlatformReadComponent component = this.Platform.Temperature[i];

                    string name;
                    int value;

                    try { name = component.GetName(); } catch { continue; }
                    try { value = component.GetValue(); } catch { continue; }

                    // CPUT is the processor's own sensor; GPTM the graphics
                    // one. The rest are board sensors that go in the details.
                    if(name == "CPUT") { reading.CpuTemperature = value; continue; }
                    if(name == "GPTM") { reading.GpuTemperature = value; continue; }

                    // A sensor this board does not carry is left out rather
                    // than shown as a permanent zero, which reads as a probe
                    // sitting at absolute cold
                    if(this.Platform.TemperatureDormant[i] || value <= 0)
                        continue;

                    board.Add(new System.Collections.Generic.KeyValuePair<string, int>(
                        SensorLabel(name), value));

                }

                // The firmware's own named probes, where it publishes any.
                // Read on the slow tick only: this crosses into the WMI
                // service, which is a different order of cost from a register.
                if(slowTick)
                    try {
                        this.HpSensorCache = Hardware.HpSensors.Read();
                    } catch { }

                // The temperatures go in with the board probes; the fan rows
                // are kept separately rather than discarded, which is what
                // used to happen to them. These are the firmware's own
                // measured speeds, by its own names for the fans, and on a
                // board whose Embedded Controller tachometer is unreliable
                // they are the only honest rpm the machine will give.
                System.Collections.Generic.List<
                    System.Collections.Generic.KeyValuePair<string, int>> hpFans =
                    new System.Collections.Generic.List<
                        System.Collections.Generic.KeyValuePair<string, int>>();

                if(this.HpSensorCache != null)
                    foreach(Hardware.HpSensors.Sensor sensor in this.HpSensorCache) {

                        if(sensor.Reading <= 0)
                            continue;

                        if(sensor.Type == Hardware.HpSensors.Kind.Temperature)
                            board.Add(new System.Collections.Generic.KeyValuePair<string, int>(
                                sensor.Name, sensor.Reading));
                        else if(sensor.Type == Hardware.HpSensors.Kind.Fan)
                            hpFans.Add(new System.Collections.Generic.KeyValuePair<string, int>(
                                sensor.Name, sensor.Reading));

                    }

                reading.HpFanRpm = hpFans.ToArray();

                // The operating system's own thermal zones. Every laptop has
                // these, they come from the same firmware tables the Embedded
                // Controller registers do, and they need no per-model
                // knowledge — which makes them what a machine with no
                // recognised register map falls back to, and a reading this
                // one was not showing at all.
                if(slowTick)
                    try {
                        this.ThermalZoneCache = Hardware.AcpiThermal.Read();
                    } catch { }

                if(this.ThermalZoneCache != null)
                    foreach(Hardware.AcpiThermal.Zone zone in this.ThermalZoneCache)
                        board.Add(new System.Collections.Generic.KeyValuePair<string, int>(
                            Config.Locale.Get("GuiWpfSensorZone") + " " + zone.Name,
                            zone.Celsius));

                reading.BoardSensors = board.ToArray();

            } catch { }

            if(slowTick)
                try {
                    this.SensorFaultCache = Hardware.HpSensors.GetFaults().ToArray();
                } catch { }
            reading.SensorFaults = this.SensorFaultCache;

            // Indexed against the array's own length: a board with a single
            // fan has a single entry, and asking it for a second one is how a
            // machine that is merely unusual turns into a machine that throws
            // on every reading.
            IFan[] fans = this.Platform.Fans.Fan;

            // The measured speeds. These used to be the Embedded Controller's
            // rate register, which is a percentage rather than a count and on
            // some boards not an honest one either, and nothing ever read
            // them. They are now the firmware's tachometer, and they are shown.
            try {
                if(fans.Length > 0) reading.FanRpmCpu = fans[0].GetSpeed();
                if(fans.Length > 1) reading.FanRpmGpu = fans[1].GetSpeed();
            } catch { }

            try {
                if(fans.Length > 0) reading.FanLevelCpu = fans[0].GetLevel();
                if(fans.Length > 1) reading.FanLevelGpu = fans[1].GetLevel();
            } catch { }

            reading.FanCount = fans.Length;

            GatherStaticInfo();
            reading.SystemModel = this.SystemModel ?? "";
            reading.BiosVersion = this.BiosVersion ?? "";
            reading.CpuName = this.CpuName ?? "";
            reading.GpuNvidiaName = this.GpuName ?? "";

            if(slowTick)
                try {
                    this.PowerPlanCache = Hardware.SystemMetrics.GetPowerPlanName() ?? "";
                } catch { }
            reading.PowerPlan = this.PowerPlanCache;

            // Windows' own power mode, which is not the power plan: the plan
            // is the scheme, the mode is the slider in the battery flyout.
            // Both are readable and only one has ever been shown.
            //
            // Read every tick rather than on the slow one. It is a local call
            // into powrprof, not a WMI round trip, so it is cheap — and while
            // it was cached for five ticks, a reading taken a second after the
            // user picked a mode still carried the previous one and snapped
            // the selector back to it.
            try {
                reading.PowerMode =
                    Enum.GetName(typeof(Hardware.SystemMetrics.PowerMode),
                        Hardware.SystemMetrics.GetPowerMode()) ?? "";
            } catch { }

            // How long the machine has been up. The field for this has existed
            // since Reading was written, the locale strings for it exist in
            // both languages, and nothing has ever assigned it.
            try {
                reading.Uptime = Hardware.SystemMetrics.FormatUptime(
                    Hardware.SystemMetrics.GetUptime());
            } catch { }

            // Whether to ask the discrete card anything at all.
            //
            // On an Optimus laptop the dGPU powers itself down when nothing is
            // using it, and querying the driver is enough to bring it back.
            // Doing that once a second on battery costs real runtime for a
            // temperature nobody is looking at, which is what GpuPollOnBattery
            // has always said it prevented — a setting that has been in the
            // configuration file, and documented, while nothing read it.
            //
            // The window being open is treated as someone looking: a reading
            // that goes blank the moment the charger comes out is worse than
            // the drain, and this is the one case where the card is awake
            // because the user asked about it.
            bool askTheCard =
                Config.GpuPollOnBattery
                || this.IsOnAcCache
                || (this.IsWindowVisible != null && this.IsWindowVisible());

            try {
                Hardware.GpuNvidia.GpuInfo gpu = askTheCard
                    ? Hardware.GpuNvidia.Get()
                    : Hardware.GpuNvidia.GetLastKnown();
                reading.GpuNvidiaPresent = gpu.Present;
                if(gpu.Present) {
                    reading.GpuNvidiaTemp = gpu.TempC;
                    reading.GpuNvidiaLoad = gpu.Load;
                    reading.GpuNvidiaCoreMhz = gpu.CoreMhz;
                    reading.GpuNvidiaMemMhz = gpu.MemMhz;
                    reading.GpuNvidiaPowerW = gpu.PowerW;
                    reading.GpuNvidiaPowerLimitW = gpu.PowerLimitW;
                    reading.GpuNvidiaVramUsedMB = gpu.VramUsedMB;
                    reading.GpuNvidiaVramTotalMB = gpu.VramTotalMB;
                }
            } catch { }

            if(slowTick)
                try {
                    int disk = Hardware.DiskTemperature.GetTemperature();
                    if(disk > 0)
                        this.DiskTemperatureCache = disk;
                } catch { }
            reading.DiskTemperature = this.DiskTemperatureCache;

            if(slowTick)
                try {
                    int signal, rx, tx;
                    string ssid;
                    if(External.WlanApi.GetSignal(out signal, out rx, out tx, out ssid)) {
                        this.WifiConnectedCache = true;
                        this.WifiSsidCache = ssid ?? "";
                        this.WifiSignalCache = signal;
                        this.WifiRxCache = rx;
                        this.WifiTxCache = tx;
                    } else {
                        this.WifiConnectedCache = false;
                    }
                } catch { }
            reading.WifiConnected = this.WifiConnectedCache;
            reading.WifiSsid = this.WifiSsidCache;
            reading.WifiSignalPercent = this.WifiSignalCache;
            reading.WifiRxMbps = this.WifiRxCache;
            reading.WifiTxMbps = this.WifiTxCache;

            try {
                reading.CpuCoreTemperatures =
                    Hardware.Cpu.CpuTemperature.GetPerCoreTemperatures();
            } catch { }

            // Per-core clocks. Read from the same MSRs the package clock comes
            // from, and never asked for until now: the strip on the dashboard
            // showed heat only, so a core parked at its base clock and a core
            // boosting looked exactly alike.
            try {
                reading.CpuCoreClocks = Hardware.Cpu.CpuMetrics.GetPerCoreClocks();
            } catch { }

            // Disk and network throughput. Both meters are differential - they
            // report what has moved since the last call - so they are read
            // every tick rather than on the slow one: sampling one tick in
            // five would report five seconds of traffic as though it were one.
            try {
                double read, written;
                if(Hardware.DiskActivity.Sample(out read, out written)) {
                    reading.DiskReadMBs = read;
                    reading.DiskWriteMBs = written;
                }
            } catch { }

            try {
                double down, up;
                if(Hardware.NetworkMeter.Sample(out down, out up)) {
                    reading.NetDownMbps = down;
                    reading.NetUpMbps = up;
                }
            } catch { }

            try {
                reading.FanIsMax = this.Platform.Fans.GetMax();
                reading.FanIsOff = this.Platform.Fans.GetOff();
            } catch { }

            // While the firmware itself has the fans at maximum, whatever level
            // they are running at is by definition a level this board reaches.
            // Some boards describe a conservative curve in their fan table and
            // then exceed it here, and a ceiling that only ever asked would cap
            // the curve and the sliders below what the hardware can actually do.
            try {
                Hardware.DeviceProfile.Observe(reading.FanLevelCpu, reading.FanIsMax);
                Hardware.DeviceProfile.Observe(reading.FanLevelGpu, reading.FanIsMax);
            } catch { }

            reading.FanLevelMaximum = Config.FanLevelMax;

            try {
                reading.FanIsManual = this.Platform.Fans.GetManual();
            } catch { }

            // The failsafe countdown. When it reaches zero the Embedded
            // Controller takes the fans back, which is the whole explanation
            // for a manual speed quietly reverting after a few minutes - and
            // until now there was nowhere at all to see it happening.
            try {
                reading.FanCountdown = this.Platform.Fans.GetCountdown();
            } catch { }

            try {
                reading.FanMode = Enum.GetName(typeof(BiosData.FanMode),
                    this.Platform.Fans.GetMode()) ?? "";
            } catch { }

            // The keyboard's current colour, so the window's swatches follow
            // what the firmware actually holds — an effect's colour, or one the
            // tray menu set — rather than only what the user last picked here
            if(slowTick)
                try {
                    if(this.Platform.System.GetKbdColorSupport()) {
                        BiosData.ColorTable table = this.Platform.System.GetKbdColor();
                        if(table.Zone != null && table.Zone.Length > 0) {
                            int[] colours = new int[table.Zone.Length];
                            for(int i = 0; i < colours.Length; i++)
                                colours[i] = (int) (table.Zone[i].ValueReverse & 0xFFFFFF);
                            this.KbdColorsCache = colours;
                        }
                    }
                } catch { }

            reading.KbdColors = this.KbdColorsCache;

            // Whether the backlight is actually lit. The panel's switch was
            // written to the hardware and never read back from it, so it could
            // sit on while the idle watch or the tray menu had turned the
            // light off - the interface disagreeing with the machine about a
            // state the user can see with their own eyes.
            // The backlight state is deliberately NOT read here.
            //
            // This firmware accepts 0xE4 for on and 0x64 for off, drives the
            // light correctly, and then reports the state back the other way
            // round — so a reading taken from it made the window's switch flip
            // itself back a few seconds after every use. Nothing outside the
            // application switches the backlight, so the application already
            // knows: GuiTray records every change and tells the window
            // directly. See GuiTray.SetKbdBacklightState.

            if(slowTick)
                try {

                    BiosData.GpuPowerData power = this.Platform.System.GetGpuPower(true);
                    this.GpuPowerSupportedCache = this.Platform.System.GpuPowerSupported;

                    // The two flags are read back rather than a level, because the
                    // firmware stores them separately and a machine can be left in
                    // a state no single level describes
                    this.GpuPowerCache =
                        power.Ppab == BiosData.GpuPpab.On ? GpuPower.Boost
                        : power.CustomTgp == BiosData.GpuCustomTgp.On ? GpuPower.Custom
                        : GpuPower.Base;

                } catch { }

            reading.GpuPowerSupported = this.GpuPowerSupportedCache;
            reading.GpuPower = this.GpuPowerCache;

            try { reading.CpuWatts = Hardware.Cpu.CpuMetrics.GetPowerWatts(); } catch { }

            try {
                double pl1, pl2;
                if(Hardware.Cpu.CpuMetrics.GetPowerLimits(out pl1, out pl2)) {
                    reading.CpuPl1W = pl1;
                    reading.CpuPl2W = pl2;
                }
            } catch { }

            try {
                int megahertz = Hardware.Cpu.CpuMetrics.GetClockMhz();
                if(megahertz > 0)
                    reading.CpuGigahertz = megahertz / 1000.0;
            } catch { }

            try {
                reading.CpuLoadPercent = Hardware.SystemMetrics.GetCpuLoadPercent();
            } catch { }

            try {
                double used, total;
                int percent;
                if(Hardware.SystemMetrics.GetMemory(out used, out total, out percent)) {
                    reading.MemoryPercent = percent;
                    reading.MemoryUsedGB = used;
                    reading.MemoryTotalGB = total;
                }
            } catch { }

            try {

                Hardware.Battery.Info battery = Hardware.Battery.Get();

                reading.BatteryPresent = battery.Present;
                reading.BatteryOnAc = battery.OnAc;
                reading.BatteryCharging = battery.Charging;
                reading.BatteryPercent = battery.Percent;
                reading.BatteryMinutesLeft = battery.MinutesLeft;
                reading.BatteryWatts = battery.RateWatts;
                reading.BatteryHealthPercent = battery.HealthPercent;
                reading.BatteryCycleCount = battery.CycleCount;
                reading.BatteryDesignmWh = battery.DesignmWh;
                reading.BatteryFullmWh = battery.FullmWh;

                // Carried to the next reading, where the graphics card is
                // decided before the battery is read
                this.IsOnAcCache = !battery.Present || battery.OnAc;

            } catch { }

            try {
                Hardware.Cpu.CpuTemperature.ThrottleFlags flags =
                    Hardware.Cpu.CpuTemperature.GetThrottleStatus();
                reading.Throttle = Describe(flags);
            } catch { }

            if(this.GetProgramName != null)
                try { reading.ProgramName = this.GetProgramName() ?? ""; } catch { }

            if(this.IsProgramRunning != null)
                try { reading.IsProgramRunning = this.IsProgramRunning(); } catch { }

            if(this.IsThermalProtectionActive != null)
                try {
                    reading.IsThermalProtectionActive = this.IsThermalProtectionActive();
                } catch { }

            return reading;

        }

        // Reads the model and BIOS version once, on the first reading. Both
        // are static for the life of the process, and the WMI query behind the
        // BIOS version is slow enough that repeating it every tick would show.
        private void GatherStaticInfo() {

            if(this.SystemModel != null)
                return;

            this.SystemModel = "";
            this.BiosVersion = "";

            try {
                using(WmiInfo wmi = new WmiInfo()) {

                    // The marketing name ("Victus by HP …") rather than the
                    // baseboard code ("8DCF"): it is what the machine is called
                    // on its lid and in a support call. SystemFamily is the
                    // fuller of the two where a machine fills both in.
                    foreach(var cs in wmi.EnumerateInstances("Win32_ComputerSystem")) {
                        cs.TryGetValue("Manufacturer", out string maker);
                        cs.TryGetValue("SystemFamily", out string family);
                        cs.TryGetValue("Model", out string model);
                        string name = !string.IsNullOrEmpty(family)
                            && (model ?? "").IndexOf(family, StringComparison.OrdinalIgnoreCase) < 0
                            ? (family + " " + model) : model;
                        this.SystemModel = ((maker ?? "") + " " + (name ?? "")).Trim();
                        break;
                    }

                    foreach(var bios in wmi.EnumerateInstances("Win32_BIOS"))
                        if(bios.TryGetValue("SMBIOSBIOSVersion", out string ver)
                            && ver.Length > 0) {
                            this.BiosVersion = ver;
                            break;
                        }

                    // What the processor actually is. Shown beside the block
                    // heading rather than buried three pages away: a reading
                    // of 78 degrees means something different on a 45 W part
                    // than on a 15 W one, and the part number is the context.
                    foreach(var cpu in wmi.EnumerateInstances("Win32_Processor"))
                        if(cpu.TryGetValue("Name", out string name) && name.Length > 0) {
                            this.CpuName = Tidy(name);
                            break;
                        }

                    // The discrete card by name. Taken from WMI rather than
                    // from NVAPI: the vendor interface reports it through yet
                    // another hand-bound entry point, and the display adapter
                    // list already has it. The integrated adapter is skipped -
                    // the block this names is the NVIDIA one.
                    foreach(var video in wmi.EnumerateInstances("Win32_VideoController"))
                        if(video.TryGetValue("Name", out string name)
                            && name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) {
                            this.GpuName = Tidy(name);
                            break;
                        }

                }
            } catch { }

            // Fall back to the baseboard identity if WMI gave nothing
            if(this.SystemModel.Length == 0)
                try {
                    this.SystemModel = (this.Platform.System.GetManufacturer()
                        + " " + this.Platform.System.GetProduct()).Trim();
                } catch { }

        }

        // A part name, without the decoration the vendors put in it.
        //
        // "Intel(R) Core(TM) 5 210H" and "NVIDIA GeForce RTX 5050 Laptop GPU"
        // are what WMI reports; the registered-trademark marks and the
        // repeated vendor name cost width in a card that has none to spare
        // and tell the reader nothing.
        private static string Tidy(string name) {

            if(string.IsNullOrEmpty(name))
                return "";

            name = name.Replace("(R)", "").Replace("(TM)", "").Replace("(C)", "");
            name = name.Replace(" CPU", "").Replace(" Laptop GPU", "");

            // Collapse the runs the removals leave behind
            while(name.IndexOf("  ", StringComparison.Ordinal) >= 0)
                name = name.Replace("  ", " ");

            return name.Trim();

        }

        // A readable name for a board temperature probe.
        //
        // The application reads these by their Embedded Controller register
        // labels, which come from the board's ACPI tables and mean nothing to
        // anyone who has not read them: "TNT4" is a probe, but it does not say
        // where. The known ones get the name of the part they sit on; the rest
        // keep their register label, which is at least honest about being one.
        private static string SensorLabel(string register) {

            switch(register) {
                case "RTMP": return Config.Locale.Get("GuiWpfSensorChipset");
                case "TMP1": return Config.Locale.Get("GuiWpfSensorMemory");
                case "CPUT": return Config.Locale.Get("GuiWpfCpu");
                case "GPTM": return Config.Locale.Get("GuiWpfGraphics");
                case "BIOS": return Config.Locale.Get("GuiWpfSensorBios");
                default:
                    // TNT2 … TNT5 are the board's spare probes, numbered as
                    // the firmware numbers them so two machines can be compared
                    return register != null && register.StartsWith("TNT")
                        ? Config.Locale.Get("GuiWpfSensorProbe") + " " + register.Substring(3)
                        : register;
            }

        }

        private static string Describe(Hardware.Cpu.CpuTemperature.ThrottleFlags flags) {

            bool thermal = (flags & Hardware.Cpu.CpuTemperature.ThrottleFlags.Thermal) != 0;
            bool power = (flags & Hardware.Cpu.CpuTemperature.ThrottleFlags.PowerLimit) != 0;

            if(thermal && power) return Config.Locale.Get("GuiWpfThrottleThermalPower");
            if(thermal) return Config.Locale.Get("GuiWpfThrottleThermal");
            if(power) return Config.Locale.Get("GuiWpfThrottlePower");

            return Config.Locale.Get("GuiWpfThrottleNone");

        }

    }

}
