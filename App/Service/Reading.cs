// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.Generic;

namespace StarMon.AppService {

    // Everything the interface shows, as it stood at one moment.
    //
    // A snapshot passed whole rather than a set of properties read one at a
    // time. The readings are gathered on a background thread and shown on the
    // interface thread, and a reader that pulls fields across that boundary
    // individually can see half of one tick and half of the next — a fan
    // speed from before a mode change beside a mode from after it. Which is
    // exactly the sort of thing nobody notices until it is on a screenshot.
    //
    // Every field carries its own "not known" value rather than throwing:
    // a sensor this machine does not have is an ordinary state, not an error,
    // and the interface has to be able to say so.
    public sealed class Reading {

        // Temperatures [°C]; zero means the sensor said nothing
        public int CpuTemperature;
        public int GpuTemperature;
        public int MaxTemperature;

        // Measured fan speeds [rpm], from the firmware's own tachometer where
        // it has one. Zero means it did not answer, which is not the same as
        // a stopped fan and is shown as such.
        public int FanRpmCpu;
        public int FanRpmGpu;

        // The levels behind them
        public int FanLevelCpu;
        public int FanLevelGpu;
        public int FanLevelMaximum;

        // Fans this board actually has, from the firmware, by way of the fan
        // array the platform built from it.
        //
        // The default is one rather than two. It is only ever seen by a
        // reading that has not been through the poller, and one is the count
        // that cannot be wrong in a way that invents hardware: showing a
        // second fan that is not there is the failure this field exists to
        // prevent, and its own default used to be an instance of it. The
        // comment here used to claim this was the firmware's answer while
        // nothing consumed the firmware's answer at all.
        public int FanCount = 1;

        // Every board temperature probe that is answering, by its own name.
        //
        // The application has always read these — the chipset probe, the
        // memory probe, the auxiliary ones — and used them for the hottest-
        // reading check that drives the thermal guard, while never showing a
        // single one of them. A user looking at the sensors panel could not
        // see the readings their fan curve was responding to.
        public KeyValuePair<string, int>[] BoardSensors;

        // Anything the firmware itself has flagged as unhealthy, by name.
        // Empty on a machine that publishes no opinion, which is most of them.
        public string[] SensorFaults;

        // Processor. Negative means unavailable.
        public double CpuWatts = -1;
        public double CpuGigahertz = -1;
        public int CpuLoadPercent = -1;

        // The package power budgets the processor is held to: PL1 sustained,
        // PL2 burst — the figures a "45 W processor" is named after
        public double CpuPl1W = -1;
        public double CpuPl2W = -1;

        // Memory, as the task manager reports it
        public int MemoryPercent = -1;
        public double MemoryUsedGB = -1;
        public double MemoryTotalGB = -1;

        // Battery
        public bool BatteryPresent;
        public bool BatteryOnAc;
        public bool BatteryCharging;
        public int BatteryPercent = -1;
        public int BatteryMinutesLeft = -1;
        public double BatteryWatts = double.NaN;
        public int BatteryHealthPercent = -1;
        public int BatteryCycleCount = -1;
        public int BatteryDesignmWh;
        public int BatteryFullmWh;

        // The two overrides that sit above the levels, and the Embedded
        // Controller's manual toggle. Without all three the state cannot be
        // told apart: fans held at zero and fans left to the firmware both
        // report levels of zero, and the firmware's own levels under load
        // look exactly like levels a user set — the manual bit is what
        // records whose they are.
        public bool FanIsOff;
        public bool FanIsMax;
        public bool FanIsManual;

        // Fan control state
        public string FanMode = "";
        public string ProgramName = "";
        public bool IsProgramRunning;
        public bool IsThermalProtectionActive;

        // Graphics power. Several models have none to report, which is an
        // answer rather than a failure.
        public bool GpuPowerSupported;
        public GpuPower GpuPower = GpuPower.Base;

        // System
        public string PowerPlan = "";
        public string Uptime = "";
        public string Throttle = "";

        // Machine identity. Static across a session, so the poller reads it
        // once and copies it into every reading rather than asking the
        // firmware and WMI for it sixty times a minute.
        public string SystemModel = "";
        public string BiosVersion = "";

        // Discrete graphics, read through the vendor's own interface rather
        // than the board sensor. Present says whether an NVIDIA card answered
        // at all; the rest are that card's own figures, negative when unknown.
        public bool GpuNvidiaPresent;
        public int GpuNvidiaTemp = -1;
        public int GpuNvidiaLoad = -1;
        public int GpuNvidiaCoreMhz = -1;
        public int GpuNvidiaPowerW = -1;
        public int GpuNvidiaPowerLimitW = -1;
        public int GpuNvidiaVramUsedMB = -1;
        public int GpuNvidiaVramTotalMB = -1;

        // Storage. The temperature of the drive Windows booted from, in °C;
        // negative when no drive would report one.
        public int DiskTemperature = -1;

        // Wireless. Connected says whether an association exists at all; the
        // rest describe it, and are meaningless without it.
        public bool WifiConnected;
        public string WifiSsid = "";
        public int WifiSignalPercent = -1;
        public int WifiRxMbps = -1;
        public int WifiTxMbps = -1;

        // One temperature per logical processor, or null where the machine
        // will not report per-core figures
        public int[] CpuCoreTemperatures;

        // One clock per logical processor [MHz].
        //
        // CpuMetrics has been able to report these since it was written and
        // nothing ever asked for them, so the per-core strip showed heat only:
        // a core parked at its base clock and a core boosting looked alike.
        public int[] CpuCoreClocks;

        // What the parts are called. Read once per session like the machine
        // identity above, and for the same reason.
        public string CpuName = "";
        // The graphics card's name, whoever made it. Named for the vendor
        // until now, and filled in only for that vendor — a machine with
        // Radeon or Arc graphics showed a temperature beside a blank.
        public string GpuName = "";

        // What this application itself is costing [MB]. Shown because it was
        // claimed and never measured; see Hardware/SystemMetrics.cs.
        public double SelfMemoryMB = -1;
        public double SelfPrivateMB = -1;

        // When this reading was started, and when the graphics power in it was
        // actually read [Environment.TickCount].
        //
        // A reading is not an instant. Gathering one is dozens of round trips
        // through the firmware and the Embedded Controller, and on a contended
        // machine it takes seconds — so a reading that arrives now may describe
        // a machine as it was several seconds ago, from before the user
        // touched anything.
        //
        // That is what made the fan selector jump back to Automatic after
        // being set to Maximum: the answer to "is the maximum flag set" had
        // been fetched before the click and delivered after it. Stamping when
        // the question was asked is what lets the window tell a stale answer
        // from a current one.
        //
        // The graphics power carries its own stamp because it is not read
        // every time — it is refreshed one tick in five, so a reading taken
        // now can carry an answer from four seconds before it.
        public int TakenAt;
        public int GpuPowerReadAt;

        // The card's memory clock [MHz]. Read from NVAPI alongside the core
        // clock all along and dropped on the floor here.
        public int GpuNvidiaMemMhz = -1;

        // Throughput, as opposed to the temperature and the link rate which
        // are all the interface has ever shown. Both meters existed and
        // neither had a caller.
        public double DiskReadMBs = -1;
        public double DiskWriteMBs = -1;
        public double NetDownMbps = -1;
        public double NetUpMbps = -1;

        // Windows' own power mode - the slider in the battery flyout. The
        // application can read and set it and did neither.
        public string PowerMode = "";

        // Whether the keyboard backlight is lit, as the firmware reports it.
        // The panel's switch was written to and never read back, so it could
        // sit on while the idle watch or the tray menu had turned the light
        // off. Null where the machine will not say.
        public bool? KbdBacklightOn;

        // The Embedded Controller's failsafe countdown [s]. It hands cooling
        // back to the firmware when it reaches zero, which is the explanation
        // for a manual fan speed quietly reverting - and there was nowhere at
        // all to see it.
        public int FanCountdown = -1;

        // Fan speeds the firmware publishes through its own sensor class, by
        // name. HpSensors reads these on every slow tick and the poller has
        // been filtering them out and keeping only the temperatures.
        public KeyValuePair<string, int>[] HpFanRpm;

        // The keyboard backlight colour of each zone as the firmware currently
        // holds it, packed as RGB, or null where it cannot be read. The window
        // follows this so its swatches stay in step with the hardware — with an
        // effect running, or after the tray menu changed a colour.
        public int[] KbdColors;

    }

}
