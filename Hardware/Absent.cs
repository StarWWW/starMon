// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;

namespace StarMon.Hardware {

    // Stand-ins for hardware this machine will not give us.
    //
    // The application used to answer a failed initialisation by exiting: no
    // window, no readings, one line naming the step that failed. On a machine
    // where the kernel driver is blocked — which since the Windows 11 2022
    // update is the default configuration of most new machines — that meant
    // the whole application was unavailable, while HP's published sensors, the
    // ACPI thermal zones, the battery, the drive temperature, the network
    // meter, the display brightness and every one of the firmware's BIOS calls
    // would all have worked perfectly well.
    //
    // These take the place of the part that is missing so the rest can run.
    // Nothing else in the codebase has to learn a new shape: every caller
    // already copes with a refused call, because refusing calls is what a
    // partially-implemented firmware does, and the device matrix exercises
    // exactly that path.

    // An Embedded Controller that is not there.
    //
    // Reads report that the exchange did not happen, which is the same answer
    // a register the board does not carry gives — so the sensors stand
    // themselves down through the mechanism that already exists, rather than
    // through a special case. Writes are dropped: silently, because there is
    // nowhere for them to go and because the caller has already been told,
    // once, why.
    public sealed class AbsentEmbeddedController : IEmbeddedController {

        public bool IsInitialized { get { return false; } }

        public void Initialize() { }
        public void Close() { }
        public void Dispose() { }

        // Granted, because there is nothing to serialise against. Refusing it
        // would report a contended lock, which is a different fault with a
        // different remedy, and would be a lie.
        public bool Request(int timeout) { return true; }
        public void Release() { }

        public byte ReadByte(byte register) { return 0; }
        public ushort ReadWord(byte register) { return 0; }

        public bool TryReadByte(byte register, out byte value) {
            value = 0;
            return false;
        }

        public bool TryReadWord(byte register, out ushort value) {
            value = 0;
            return false;
        }

        public void WriteByte(byte register, byte value) { }
        public void WriteWord(byte register, ushort value) { }

    }

    // A BIOS interface that is not there.
    //
    // Every call throws, which is what a refused call does already: Check()
    // turns a bad status code into a BiosException, and the callers that mean
    // to survive one catch it. A stand-in that returned plausible zeroes
    // instead would be worse than absent — the interface would show a machine
    // with no fans, at nought degrees, and nothing would say why.
    public sealed class AbsentBiosCtl : IBiosCtl {

        public bool IsInitialized { get { return false; } }

        public void Initialize() { }
        public void Close() { }
        public void Dispose() { }

        private static BiosException Absent() {
            return new BiosException(
                "the BIOS interface is not available on this machine");
        }

        public int Send(BiosData.Cmd command, uint commandType,
            byte[] inData, byte outDataSize, out byte[] outData) {
            outData = null;
            return -1;
        }

        public int Send(BiosData.Cmd command, uint commandType, byte[] inData) {
            return -1;
        }

        public BiosData.AnimTable GetAnimTable() { throw Absent(); }
        public void SetAnimTable(BiosData.AnimTable data) { throw Absent(); }

        public BiosData.Backlight GetBacklight() { throw Absent(); }
        public void SetBacklight(BiosData.Backlight value) { throw Absent(); }
        public void SetBacklight(bool value) { throw Absent(); }

        public BiosData.ColorTable GetColorTable() { throw Absent(); }
        public void SetColorTable(BiosData.ColorTable data) { throw Absent(); }

        public BiosData.AdapterStatus GetAdapter() { throw Absent(); }
        public string GetBornDate() { throw Absent(); }
        public BiosData.KbdType GetKbdType() { throw Absent(); }
        public BiosData.SystemData GetSystem() { throw Absent(); }

        public bool HasBacklight() { throw Absent(); }
        public byte[] GetKbdCapability() { throw Absent(); }
        public byte HasMemoryOverclock() { throw Absent(); }
        public byte HasOverclock() { throw Absent(); }
        public byte HasUndervoltBios() { throw Absent(); }

        public void SetCpuPower(BiosData.CpuPowerData data) { throw Absent(); }
        public void SetCpuPower1(byte value) { throw Absent(); }
        public void SetCpuPower4(byte value) { throw Absent(); }
        public void SetCpuPowerWithGpu(byte value) { throw Absent(); }

        public BiosData.GpuMode GetGpuMode() { throw Absent(); }
        public void SetGpuMode(BiosData.GpuMode value) { throw Absent(); }

        public BiosData.GpuPowerData GetGpuPower() { throw Absent(); }
        public void SetGpuPower(BiosData.GpuPowerData data) { throw Absent(); }
        public void SetGpuPower(BiosData.GpuPowerLevel value) { throw Absent(); }

        public void SetIdle(BiosData.Idle value) { throw Absent(); }
        public void SetIdle(bool value) { throw Absent(); }
        public void SetMemoryXmp(bool value) { throw Absent(); }

        public byte GetFanCount() { throw Absent(); }
        public byte GetFanType() { throw Absent(); }
        public int GetFanSpeed(byte fan) { throw Absent(); }

        public byte[] GetFanLevel() { throw Absent(); }
        public void SetFanLevel(byte[] data) { throw Absent(); }

        public void SetFanMode(BiosData.FanMode value) { throw Absent(); }

        public BiosData.FanTable GetFanTable() { throw Absent(); }
        public void SetFanTable(BiosData.FanTable data) { throw Absent(); }

        public bool GetMaxFan() { throw Absent(); }
        public void SetMaxFan(bool value) { throw Absent(); }

        public byte GetTemperature() { throw Absent(); }
        public BiosData.Throttling GetThrottling() { throw Absent(); }

    }

}
