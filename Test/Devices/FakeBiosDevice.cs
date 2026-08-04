// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Bios;

namespace StarMon.Test.Devices {

    // A WMI BIOS interface backed by declared answers rather than by firmware.
    //
    // The interesting behaviour to model is not the happy path. It is refusal:
    // a board that answers some calls, refuses others, and — the case that has
    // cost real users real cooling — accepts a fan level write and does
    // nothing with it. Each of those is a different shape here:
    //
    //   Refuse(name)   the call throws, as Check() makes it when the firmware
    //                  returns an error code
    //   Ignore(name)   the call returns without error and without effect,
    //                  which is what a write that goes nowhere looks like
    //
    // Every call is counted. A board needing a heartbeat is diagnosed by
    // whether anything asks it for the fan count often enough, and that can
    // only be asserted if the asking is recorded.
    internal sealed class FakeBiosDevice : IBiosCtl {

        public bool IsInitialized { get; private set; }

        public void Initialize() { IsInitialized = true; }
        public void Close() { IsInitialized = false; }
        public void Dispose() { Close(); }

#region Declared board behaviour
        // Calls that throw rather than answer
        private readonly HashSet<string> Refused = new HashSet<string>();

        // Calls that succeed and do nothing
        private readonly HashSet<string> Ignored = new HashSet<string>();

        // How many times each call was made
        private readonly Dictionary<string, int> Calls = new Dictionary<string, int>();

        internal FakeBiosDevice Refuse(string call) { Refused.Add(call); return this; }
        internal FakeBiosDevice Ignore(string call) { Ignored.Add(call); return this; }

        internal int CallCount(string call) {
            int n;
            return Calls.TryGetValue(call, out n) ? n : 0;
        }

        internal void ResetCounts() { Calls.Clear(); }

        // Records the call, and applies whatever this board was declared to do
        // about it. Returns false where the caller should do nothing.
        private bool Enter(string call) {

            int n;
            Calls[call] = Calls.TryGetValue(call, out n) ? n + 1 : 1;

            if(Refused.Contains(call))
                throw new BiosException(call + " is not available on this device");

            return !Ignored.Contains(call);

        }
#endregion

#region Board state
        internal byte FanCount = 2;
        internal byte FanType = 0x21;              // CPU fan and GPU fan, per nibble
        internal byte[] FanLevel = new byte[] { 0, 0 };
        internal int[] FanSpeed = new int[] { 0, 0 };
        internal BiosData.FanMode FanMode = BiosData.FanMode.Default;
        internal bool MaxFan;
        internal byte Temperature = 45;
        internal BiosData.FanTable FanTable = DefaultFanTable(56);

        internal BiosData.SystemData System = new BiosData.SystemData();
        internal string BornDate = "20240101";
        internal BiosData.KbdType KbdType = BiosData.KbdType.WithNumPad;
        internal byte[] KbdCapability = new byte[] { 0x07, 0x21, 0x00, 0x00 };
        internal bool Backlit = true;
        internal BiosData.Backlight Backlight = BiosData.Backlight.On;
        internal BiosData.ColorTable ColorTable = new BiosData.ColorTable();
        internal BiosData.AnimTable AnimTable = new BiosData.AnimTable();
        internal BiosData.AdapterStatus Adapter = BiosData.AdapterStatus.MeetsRequirement;
        internal BiosData.GpuMode GpuMode = BiosData.GpuMode.Hybrid;
        internal BiosData.GpuPowerData GpuPower = new BiosData.GpuPowerData();
        internal BiosData.Throttling Throttling = BiosData.Throttling.Default;
        // Zero is what BiosCtl returns for all three today, on every machine:
        // the calls behind them are stubbed out rather than asked. Written out
        // rather than left to the default so that a scenario can say otherwise.
        internal byte Overclock = 0;
        internal byte MemoryOverclock = 0;
        internal byte UndervoltBios = 0;

        // Writes seen, in order, so a sequence can be asserted
        internal readonly List<string> WriteLog = new List<string>();

        // A fan table shaped like the ones boards actually report: a rising
        // ramp of levels against temperature, topping out at the ceiling.
        internal static BiosData.FanTable DefaultFanTable(byte ceiling) {

            BiosData.FanTable table = new BiosData.FanTable();
            byte[] temperature = { 0, 40, 50, 60, 70, 80, 90 };

            table.FanCount = 2;
            table.LevelCount = (byte) temperature.Length;

            for(int i = 0; i < temperature.Length; i++) {

                byte level = (byte) (ceiling * (i + 1) / temperature.Length);

                table.Level[i] = new BiosData.FanLevel(level, level, temperature[i]);

            }

            return table;

        }
#endregion

#region IBios
        public int Send(BiosData.Cmd command, uint commandType,
            byte[] inData, byte outDataSize, out byte[] outData) {
            outData = new byte[outDataSize];
            return 0;
        }

        public int Send(BiosData.Cmd command, uint commandType, byte[] inData) {
            return 0;
        }
#endregion

#region Backlight control
        public BiosData.AnimTable GetAnimTable() {
            Enter("GetAnimTable");
            return AnimTable;
        }

        public void SetAnimTable(BiosData.AnimTable data) {
            if(!Enter("SetAnimTable")) return;
            AnimTable = data;
            WriteLog.Add("SetAnimTable");
        }

        public BiosData.Backlight GetBacklight() {
            Enter("GetBacklight");
            return Backlight;
        }

        public void SetBacklight(BiosData.Backlight value) {
            if(!Enter("SetBacklight")) return;
            Backlight = value;
            WriteLog.Add("SetBacklight=" + value);
        }

        public void SetBacklight(bool value) {
            SetBacklight(value ? BiosData.Backlight.On : BiosData.Backlight.Off);
        }

        public BiosData.ColorTable GetColorTable() {
            Enter("GetColorTable");
            return ColorTable;
        }

        public void SetColorTable(BiosData.ColorTable data) {
            if(!Enter("SetColorTable")) return;
            ColorTable = data;
            WriteLog.Add("SetColorTable");
        }
#endregion

#region Capability query
        public BiosData.AdapterStatus GetAdapter() {
            Enter("GetAdapter");
            return Adapter;
        }

        public string GetBornDate() {
            Enter("GetBornDate");
            return BornDate;
        }

        public BiosData.KbdType GetKbdType() {
            Enter("GetKbdType");
            return KbdType;
        }

        public BiosData.SystemData GetSystem() {
            Enter("GetSystem");
            return System;
        }

        public bool HasBacklight() {
            Enter("HasBacklight");
            return Backlit;
        }

        public byte[] GetKbdCapability() {
            Enter("GetKbdCapability");
            return KbdCapability;
        }

        public byte HasMemoryOverclock() {
            Enter("HasMemoryOverclock");
            return MemoryOverclock;
        }

        public byte HasOverclock() {
            Enter("HasOverclock");
            return Overclock;
        }

        public byte HasUndervoltBios() {
            Enter("HasUndervoltBios");
            return UndervoltBios;
        }
#endregion

#region Performance control
        public void SetCpuPower(BiosData.CpuPowerData data) {
            if(!Enter("SetCpuPower")) return;
            WriteLog.Add("SetCpuPower");
        }

        public void SetCpuPower1(byte value) {
            if(!Enter("SetCpuPower1")) return;
            WriteLog.Add("SetCpuPower1=" + value);
        }

        public void SetCpuPower4(byte value) {
            if(!Enter("SetCpuPower4")) return;
            WriteLog.Add("SetCpuPower4=" + value);
        }

        public void SetCpuPowerWithGpu(byte value) {
            if(!Enter("SetCpuPowerWithGpu")) return;
            WriteLog.Add("SetCpuPowerWithGpu=" + value);
        }

        public BiosData.GpuMode GetGpuMode() {
            Enter("GetGpuMode");
            return GpuMode;
        }

        public void SetGpuMode(BiosData.GpuMode value) {
            if(!Enter("SetGpuMode")) return;
            GpuMode = value;
            WriteLog.Add("SetGpuMode=" + value);
        }

        public BiosData.GpuPowerData GetGpuPower() {
            Enter("GetGpuPower");
            return GpuPower;
        }

        public void SetGpuPower(BiosData.GpuPowerData data) {
            if(!Enter("SetGpuPower")) return;
            GpuPower = data;
            WriteLog.Add("SetGpuPower");
        }

        public void SetGpuPower(BiosData.GpuPowerLevel value) {
            if(!Enter("SetGpuPowerLevel")) return;
            WriteLog.Add("SetGpuPower=" + value);
        }

        public void SetIdle(BiosData.Idle value) {
            if(!Enter("SetIdle")) return;
            WriteLog.Add("SetIdle=" + value);
        }

        public void SetIdle(bool value) {
            SetIdle(value ? BiosData.Idle.On : BiosData.Idle.Off);
        }

        public void SetMemoryXmp(bool value) {
            if(!Enter("SetMemoryXmp")) return;
            WriteLog.Add("SetMemoryXmp=" + value);
        }
#endregion

#region Thermal control
        public byte GetFanCount() {
            Enter("GetFanCount");
            return FanCount;
        }

        public byte GetFanType() {
            Enter("GetFanType");
            return FanType;
        }

        public int GetFanSpeed(byte fan) {
            Enter("GetFanSpeed");
            return fan < FanSpeed.Length ? FanSpeed[fan] : 0;
        }

        public byte[] GetFanLevel() {
            Enter("GetFanLevel");
            return FanLevel;
        }

        public void SetFanLevel(byte[] data) {
            if(!Enter("SetFanLevel")) return;
            FanLevel = (byte[]) data.Clone();
            WriteLog.Add("SetFanLevel=" + string.Join(",", Array.ConvertAll(data, b => b.ToString())));
        }

        public void SetFanMode(BiosData.FanMode value) {
            if(!Enter("SetFanMode")) return;
            FanMode = value;
            WriteLog.Add("SetFanMode=" + value);
        }

        public BiosData.FanTable GetFanTable() {
            Enter("GetFanTable");
            return FanTable;
        }

        public void SetFanTable(BiosData.FanTable data) {
            if(!Enter("SetFanTable")) return;
            FanTable = data;
            WriteLog.Add("SetFanTable");
        }

        public bool GetMaxFan() {
            Enter("GetMaxFan");
            return MaxFan;
        }

        public void SetMaxFan(bool value) {
            if(!Enter("SetMaxFan")) return;
            MaxFan = value;
            WriteLog.Add("SetMaxFan=" + value);
        }

        public byte GetTemperature() {
            Enter("GetTemperature");
            return Temperature;
        }

        public BiosData.Throttling GetThrottling() {
            Enter("GetThrottling");
            return Throttling;
        }
#endregion

    }

}
