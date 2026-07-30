// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.External;
using StarMon.Hardware.Bios;
using StarMon.Library;

namespace StarMon.Hardware.Platform
{

    // Defines an interface for obtaining system information
    public interface ISettings
    {

        // API queries
        public bool IsFullPower();

        // BIOS raw data
        public Nullable<BiosData.GpuMode> GpuMode { get; }
        public Nullable<BiosData.GpuPowerData> GpuPower { get; }
        public Nullable<BiosData.SystemData> SystemData { get; }

        // BIOS queries
        public BiosData.AdapterStatus GetAdapterStatus();  // Smart AC adapter status
        public string GetBornDate();                       // "Born-on" date
        public byte GetDefaultCpuPowerLimit4();            // CPU Power Limit 4 default value
        public BiosData.SystemData GetSystemData();        // System information from the BIOS
        public BiosData.Throttling GetThrottling();        // Whether the system is throttling

        // BIOS GPU queries
        public BiosData.GpuMode GetGpuMode(bool forceUpdate = false);            // Optimus or Discrete
        public bool GetGpuModeSupport();
        public void SetGpuMode(BiosData.GpuMode value);
        public BiosData.GpuCustomTgp GetGpuCustomTgp(bool forceUpdate = false);  // Custom Total Graphics Power
        public BiosData.GpuDState GetGpuDState(bool forceUpdate = false);        // Device power state
        public BiosData.GpuPpab GetGpuPpab(bool forceUpdate = false);            // Processing Power AI Boost
        public BiosData.GpuPowerData GetGpuPower(bool forceUpdate = false);      // DState, Custom TGP & PPAB
        public bool GpuPowerSupported { get; }                                   // Whether the BIOS reports GPU power
        public void SetGpuPower(BiosData.GpuPowerData value);

        // BIOS keyboard queries
        public BiosData.Backlight GetKbdBacklight();            // Backlight status
        public bool? GetKbdBacklightOn();                       // ... the way this board reports it
        public bool GetKbdBacklightSupport();
        public void SetKbdBacklight(bool flag);
        public void SetKbdBacklight(BiosData.Backlight value);
        public BiosData.ColorTable GetKbdColor();               // Backlight color
        public bool GetKbdColorSupport();
        public int GetKbdZoneCount();                           // Color zones: 1 (single) or 4
        public void SetKbdColor(BiosData.ColorTable value);
        public BiosData.KbdType GetKbdType();                   // Keyboard type
        public string KbdCapabilityText();                      // Raw capability bytes, for the report

        // WMI raw data
        public Dictionary<string, string> BaseBoard { get; }

        // WMI queries
        public string GetManufacturer();  // Baseboard manufacturer name
        public string GetProduct();       // Baseboard product identifier
        public string GetSerial();        // Baseboard serial number
        public string GetVersion();       // Baseboard version identifier

    }

    // Implements a mechanism for obtaining system information
    public class Settings : ISettings
    {

        // WMI data identifiers
        public const string WMI_BASEBOARD_MANUFACTURER = "Manufacturer";
        public const string WMI_BASEBOARD_PRODUCT = "Product";
        public const string WMI_BASEBOARD_SERIAL = "SerialNumber";
        public const string WMI_BASEBOARD_VERSION = "Version";

        // WMI raw information
        public Dictionary<string, string> BaseBoard { get; private set; }

        // Guards every lazily-initialized cache below.
        //
        // This object is shared by the interface thread and the background
        // pollers, and the caches are Nullable<T> and multi-field structures,
        // neither of which is written atomically. Without the lock a reader
        // can catch a cache half-populated: a value paired with a support flag
        // that has not been updated yet, or a Nullable seen as having a value
        // before that value has actually been stored. Holding the lock across
        // the firmware call also stops two threads asking the BIOS the same
        // question at once.
        private readonly object CacheLock = new object();

        // BIOS cached capabilities
        private Nullable<bool> KbdBacklightSupport;
        private Nullable<bool> KbdColorSupport;

        // BIOS raw information
        private string BornDate;
        public Nullable<BiosData.GpuMode> GpuMode { get; private set; }
        public Nullable<BiosData.GpuPowerData> GpuPower { get; private set; }
        public Nullable<BiosData.KbdType> KbdType { get; private set; }
        public Nullable<BiosData.SystemData> SystemData { get; private set; }

        // Constructs a system information instance
        public Settings()
        {

            // Set cached data to initial values
            this.BornDate = "";
            this.GpuMode = null;
            this.GpuPower = null;
            this.SystemData = null;

            // Create an instance to query WMI for information
            using WmiInfo wmiInfo = new WmiInfo();

            // Obtain baseboard information
            this.BaseBoard = wmiInfo.GetBaseBoard();

            // Handle the case of empty baseboard data
            if (!this.BaseBoard.ContainsKey(WMI_BASEBOARD_MANUFACTURER))
                this.BaseBoard[WMI_BASEBOARD_MANUFACTURER] = "?";
            if (!this.BaseBoard.ContainsKey(WMI_BASEBOARD_PRODUCT))
                this.BaseBoard[WMI_BASEBOARD_PRODUCT] = "?";
            if (!this.BaseBoard.ContainsKey(WMI_BASEBOARD_SERIAL))
                this.BaseBoard[WMI_BASEBOARD_SERIAL] = "?";
            if (!this.BaseBoard.ContainsKey(WMI_BASEBOARD_VERSION))
                this.BaseBoard[WMI_BASEBOARD_VERSION] = "?";

        }

        // Checks if the system is running with full power
        // (AC power check now, can extend to smart AC adapter status)
        public bool IsFullPower()
        {
            return Kernel32.GetSystemPowerStatus(out Kernel32.SYSTEM_POWER_STATUS sps)
                && sps.ACLineStatus == 1;
        }

        private Nullable<BiosData.AdapterStatus> AdapterStatus;

        // Retrieves the smart AC adapter status
        public BiosData.AdapterStatus GetAdapterStatus()
        {
            lock (this.CacheLock)
            {
                if (this.AdapterStatus == null)
                {
                    try
                    {
                        this.AdapterStatus = Hw.BiosGet<BiosData.AdapterStatus>(Hw.Bios.GetAdapter);
                    }
                    catch
                    {
                        // Return default value for devices that don't support this feature (e.g., Victus)
                        this.AdapterStatus = BiosData.AdapterStatus.NotSupported;
                    }
                }
                return (BiosData.AdapterStatus)this.AdapterStatus;
            }
        }

        // Retrieves the "born-on" date
        public string GetBornDate()
        {
            lock (this.CacheLock)
            {
                if (this.BornDate == "")
                {
                    try
                    {
                        this.BornDate = Hw.BiosGet(Hw.Bios.GetBornDate) ?? "Unknown";
                    }
                    catch
                    {
                        // Return default value for devices that don't support this feature (e.g., Victus)
                        this.BornDate = "Unknown";
                    }
                }
                return this.BornDate;
            }
        }

        // Retrieves the default CPU Power Limit 4 value
        public byte GetDefaultCpuPowerLimit4()
        {
            return GetSystemData().DefaultCpuPowerLimit4;
        }

        // Queries the Custom Total Graphics Power state
        public BiosData.GpuCustomTgp GetGpuCustomTgp(bool forceUpdate = false)
        {
            return GetGpuPower(forceUpdate).CustomTgp;
        }

        // Queries the device power state
        public BiosData.GpuDState GetGpuDState(bool forceUpdate = false)
        {
            return GetGpuPower(forceUpdate).DState;
        }

        // Queries the GPU mode (Optimus or Discrete)
        public BiosData.GpuMode GetGpuMode(bool forceUpdate = false)
        {
            lock (this.CacheLock)
            {
                if (forceUpdate || this.GpuMode == null)
                {
                    try
                    {
                        this.GpuMode = Hw.BiosGet<BiosData.GpuMode>(Hw.Bios.GetGpuMode);
                    }
                    catch
                    {
                        // Return default value for devices that don't support this feature (e.g., Victus)
                        this.GpuMode = BiosData.GpuMode.Hybrid;
                    }
                }
                return (BiosData.GpuMode)this.GpuMode;
            }
        }

        // Checks whether GPU mode switching is supported
        public bool GetGpuModeSupport()
        {
            // BiosData.SysGpuModeSwitch
            // == 0x0C: Observed on model 8A14 where switching is supported
            // == 0x08: Interpreted as support flag based on original information
            // == 0x06: Also means supported based on user reports, add 0x04 to flags
            return ((byte)(GetSystemData().GpuModeSwitch &
                (BiosData.SysGpuModeSwitch.Supported4 | BiosData.SysGpuModeSwitch.Supported8)) != 0);
        }

        // Sets the GPU mode (Optimus or Discrete)
        public void SetGpuMode(BiosData.GpuMode value)
        {
            try { Hw.BiosSet<BiosData.GpuMode>(Hw.Bios.SetGpuMode, value); } catch { }
        }

        // Whether the BIOS actually reports GPU power data on this device
        // (false once a GetGpuPower query has failed, e.g. on Victus models)
        public bool GpuPowerSupported
        {
            get { lock (this.CacheLock) { return this.GpuPowerSupportedValue; } }
            private set { this.GpuPowerSupportedValue = value; }
        }
        private bool GpuPowerSupportedValue = true;

        // GPU power (Custom TGP & PPAB)
        public BiosData.GpuPowerData GetGpuPower(bool forceUpdate = false)
        {
            // The power data and the support flag are two separate fields that
            // describe one answer, so they are updated and read as a pair: a
            // caller must never see one refreshed and the other stale
            lock (this.CacheLock)
            {
                if (forceUpdate || this.GpuPower == null)
                {
                    try
                    {
                        this.GpuPower = Hw.BiosGet<BiosData.GpuPowerData>(Hw.Bios.GetGpuPower);
                        this.GpuPowerSupportedValue = true;
                    }
                    catch
                    {
                        // Return default values for devices that don't support this feature (e.g., Victus)
                        this.GpuPower = new BiosData.GpuPowerData();
                        this.GpuPowerSupportedValue = false;
                    }
                }
                return (BiosData.GpuPowerData)this.GpuPower;
            }
        }

        // Sets the GPU power (Custom TGP & PPAB)
        public void SetGpuPower(BiosData.GpuPowerData value)
        {
            try { Hw.BiosSetStruct(Hw.Bios.SetGpuPower, value); } catch { }
        }

        // Queries the Processing Power AI Boost state
        public BiosData.GpuPpab GetGpuPpab(bool forceUpdate = false)
        {
            return GetGpuPower(forceUpdate).Ppab;
        }

        // Queries the keyboard backlight status
        public BiosData.Backlight GetKbdBacklight()
        {
            try
            {
                return Hw.BiosGet<BiosData.Backlight>(Hw.Bios.GetBacklight);
            }
            catch
            {
                return BiosData.Backlight.Off;
            }
        }

        // Checks whether keyboard backlight toggling is supported
        public bool GetKbdBacklightSupport()
        {
            // The lock is reentrant, so the nested capability queries this
            // makes (keyboard type, zone count) can take it again safely
            lock (this.CacheLock)
            {
            // Only query the first time,
            // then cache the response
            if (this.KbdBacklightSupport == null)
            {
                bool support = false;

                try
                {
                    // Whether the board has a backlight to switch at all.
                    //
                    // Per-key RGB decks used to be excluded here, which took
                    // the whole keyboard section away from the Omen models
                    // that have one. But the switch, the idle-off timer and
                    // the temperature-follow behaviour all work on those decks
                    // exactly as they do on any other — it is only the
                    // four-zone colour table they cannot accept. That belongs
                    // in GetKbdColorSupport below, which now excludes them,
                    // and not here.
                    support = Hw.BiosGet<bool>(Hw.Bios.HasBacklight);
                }
                catch
                {
                    support = false;
                }

                // Some models (e.g. certain Victus units) answer the backlight
                // state query even though the capability query itself fails, so
                // fall back to probing the state directly before giving up
                if (!support)
                    try
                    {
                        Hw.BiosGet<BiosData.Backlight>(Hw.Bios.GetBacklight);
                        support = true;
                    }
                    catch
                    {
                        support = false;
                    }

                this.KbdBacklightSupport = support;
            }
            return (bool)this.KbdBacklightSupport;
            }
        }

        // Sets the keyboard backlight status given an enumerated value
        public void SetKbdBacklight(BiosData.Backlight value)
        {
            try { Hw.BiosSet(Hw.Bios.SetBacklight, value); } catch { }
        }

        // Sets the keyboard backlight status given a Boolean flag
        public void SetKbdBacklight(bool flag)
        {
            BiosData.Backlight value = flag ? BiosData.Backlight.On : BiosData.Backlight.Off;

            // Remembered so the reading path can work out which way round this
            // firmware reports the state - see GetKbdBacklightOn
            this.BacklightWanted = flag;
            this.BacklightWrittenAt = Environment.TickCount;

            try { Hw.BiosSet(Hw.Bios.SetBacklight, value); } catch { }
        }

        // What was last asked for, when, and whether this board reports the
        // state back the same way round as it accepts it
        private bool? BacklightWanted;
        private int BacklightWrittenAt;
        private bool? BacklightReportsInverted;

        // How long to let a write settle before a reading is trusted to
        // describe it. The firmware does not necessarily report a new state on
        // the very next query, and calibrating against a stale answer would
        // learn exactly the wrong polarity.
        private const int BacklightSettleMs = 3000;

        // Whether the keyboard backlight is lit, as this board actually
        // reports it - or null when that cannot yet be said.
        //
        // The enum has 0x64 for off and 0xE4 for on, and the write path is
        // right: on this Victus the light follows the switch. The read path is
        // not. The firmware answers with the opposite constant to the one it
        // was just given, so believing it turned the window's switch into a
        // control that flipped itself back a few seconds after every use.
        //
        // Rather than hard-code one board's polarity, it is learned. The first
        // reading taken a moment after a write this application made says
        // whether the firmware agrees with the enum; every later reading is
        // interpreted through that. A board that agrees is unaffected, and
        // until something has been written there is nothing to calibrate
        // against, so the answer is null and the caller keeps its own state
        // rather than being told something that might be backwards.
        public bool? GetKbdBacklightOn()
        {
            if (!GetKbdBacklightSupport())
                return null;

            bool reportsOn;

            try
            {
                reportsOn = Hw.BiosGet<BiosData.Backlight>(Hw.Bios.GetBacklight)
                    == BiosData.Backlight.On;
            }
            catch
            {
                return null;
            }

            // Nothing written yet: there is no way to know which way round
            // this board answers, so no claim is made
            if (this.BacklightWanted == null)
                return this.BacklightReportsInverted == null
                    ? (bool?) null
                    : (this.BacklightReportsInverted.Value ? !reportsOn : reportsOn);

            // Learn the polarity once, from a reading taken after the write
            // has had time to take effect
            if (this.BacklightReportsInverted == null)
            {
                if (unchecked(Environment.TickCount - this.BacklightWrittenAt) < BacklightSettleMs)
                    return this.BacklightWanted;

                this.BacklightReportsInverted = reportsOn != this.BacklightWanted.Value;

                Logger.Info("Kbd", "Backlight state reporting calibrated",
                    this.BacklightReportsInverted.Value
                        ? "this board reports the backlight inverted"
                        : "this board reports the backlight as written");
            }

            return this.BacklightReportsInverted.Value ? !reportsOn : reportsOn;
        }

        // Queries the keyboard backlight color
        public BiosData.ColorTable GetKbdColor()
        {
            try
            {
                return Hw.BiosGetStruct<BiosData.ColorTable>(Hw.Bios.GetColorTable);
            }
            catch
            {
                return new BiosData.ColorTable();
            }
        }

        // Cached keyboard color-zone count (0 = no color support)
        private Nullable<int> KbdZoneCount;

        // The configured override the cache above was built from, so a change
        // made while the application is running is noticed
        private int KbdZoneCountFrom = -1;

        // Firmware-reported zone count, which may differ from the configured
        // override and governs the zone-count byte in color-table writes
        private Nullable<int> KbdZoneCountHw;

        // Checks whether keyboard color switching is supported
        // (both single-zone RGB and four-zone RGB keyboards)
        public bool GetKbdColorSupport()
        {
            lock (this.CacheLock)
            {
                // Only query the first time,
                // then cache the response
                if (this.KbdColorSupport == null)
                    // All keyboards that don't support backlight toggling,
                    // don't support color setting either; otherwise any
                    // keyboard whose color table can be read is supported.
                    //
                    // Per-key RGB decks are the exception: they have a
                    // backlight and they have colour, but not through the
                    // four-zone table this application writes. Offering zone
                    // swatches there would show four controls that change
                    // nothing, so the colour section stays out of the way and
                    // the backlight switch stays.
                    this.KbdColorSupport =
                        GetKbdBacklightSupport()
                        && GetKbdType() != BiosData.KbdType.PerKeyRgb
                        && GetKbdZoneCount() > 0;
                return (bool)this.KbdColorSupport;
            }
        }

        // Returns the number of color zones the keyboard reports:
        // 4 for the classic four-zone RGB layout, 1 for single-zone RGB,
        // or 0 when the color table cannot be read at all
        public int GetKbdZoneCount()
        {

            lock (this.CacheLock)
            {
                // A configured zone count takes precedence over auto-detection,
                // since some single-zone units falsely report a four-zone table.
                //
                // Recomputed when the configured value changes, so the switch
                // on the Settings page takes effect there and then rather than
                // on the next launch — the firmware call behind it is cached
                // separately and is not repeated.
                if (this.KbdZoneCount == null
                    || this.KbdZoneCountFrom != Config.KbdZoneCount)
                {
                    this.KbdZoneCountFrom = Config.KbdZoneCount;
                    this.KbdZoneCount =
                        Config.KbdZoneCount == 1 || Config.KbdZoneCount == 4 ?
                            Config.KbdZoneCount : GetKbdZoneCountEffective();
                }

                return (int)this.KbdZoneCount;
            }
        }

        // Returns the zone count as reported by the firmware itself,
        // ignoring any configured override
        private int GetKbdZoneCountHw()
        {
            lock (this.CacheLock)
            {
                if (this.KbdZoneCountHw == null)
                {
                    try
                    {
                        // The color table must be read directly (not through
                        // GetKbdColor, which masks failures with an empty table)
                        BiosData.ColorTable table = Hw.BiosGetStruct<BiosData.ColorTable>(Hw.Bios.GetColorTable);
                        int zones = table.ZoneCount + 1;
                        this.KbdZoneCountHw = zones >= 1 && zones <= 4 ? zones : 0;
                    }
                    catch
                    {
                        this.KbdZoneCountHw = 0;
                    }
                }
                return (int)this.KbdZoneCountHw;
            }
        }

        // How many zones the interface should actually offer.
        //
        // Not the same question as GetKbdZoneCountHw, and this is the whole
        // difficulty: the colour table is a fixed four-entry structure, and
        // single-zone Victus decks report four in its zone byte just as a
        // genuine four-zone Omen does. The count is the shape of the table,
        // not the shape of the keyboard, so a four there proves nothing.
        //
        // Believing it put four swatches in front of people whose deck has one
        // light string: the first swatch coloured the whole keyboard and the
        // other three did nothing at all, with no way to tell which case you
        // were in. The two mistakes are not equally bad —
        //
        //   claiming one zone on a four-zone deck costs a feature: the colour
        //   is written to all four entries, so the keyboard lights correctly,
        //   in one colour;
        //
        //   claiming four on a one-zone deck ships three dead controls.
        //
        // — so an unproven four resolves to one, and a four-zone owner turns it
        // on once from Settings. That is a click; the alternative was editing
        // XML by hand, which is what the shipped configuration file used to do
        // on everyone's behalf with one particular laptop's answer.
        //
        // If a firmware signal that really distinguishes the two is ever found,
        // this is the one method that has to change. GetKbdCapability() carries
        // the undocumented bytes it would most likely live in, and the hardware
        // report prints them.
        private int GetKbdZoneCountEffective()
        {
            int reported = GetKbdZoneCountHw();

            if (reported <= 1)
                return reported;

            // Once: the answer is cached by both callers above this
            Logger.Info("Keyboard",
                "The firmware reports " + reported + " colour zones",
                "the colour table says four on single-zone decks too, so this "
                    + "is treated as one zone unless the four-zone setting is "
                    + "turned on; capability bytes " + KbdCapabilityText());

            return 1;
        }

        // The raw keyboard capability answer as hexadecimal, for the report
        public string KbdCapabilityText()
        {
            try
            {
                byte[] raw = Hw.Bios.GetKbdCapability();
                if (raw == null || raw.Length == 0)
                    return "unavailable";

                System.Text.StringBuilder text = new System.Text.StringBuilder();
                foreach (byte b in raw)
                {
                    if (text.Length > 0) text.Append(' ');
                    text.Append(b.ToString("X2"));
                }
                return text.ToString();
            }
            catch
            {
                return "unavailable";
            }
        }

        // Sets the keyboard backlight color
        public void SetKbdColor(BiosData.ColorTable value)
        {

            // The zone-count byte written back must match what the firmware
            // reports, which is not necessarily the count the interface shows
            // (single-zone units may still expect a four-zone table)
            int zonesHw = GetKbdZoneCountHw();
            if (zonesHw > 0)
                value.ZoneCount = (byte)(zonesHw - 1);

            try { Hw.BiosSetStruct(Hw.Bios.SetColorTable, value); } catch { }
        }

        // Retrieves keyboard type from the BIOS
        public BiosData.KbdType GetKbdType()
        {
            lock (this.CacheLock)
            {
                if (this.KbdType == null)
                {
                    try
                    {
                        this.KbdType = Hw.BiosGet<BiosData.KbdType>(Hw.Bios.GetKbdType);
                    }
                    catch
                    {
                        // Return default value for devices that don't support this feature (e.g., Victus)
                        this.KbdType = BiosData.KbdType.Standard;
                    }
                }
                return (BiosData.KbdType)this.KbdType;
            }
        }

        // Retrieves the baseboard manufacturer name
        public string GetManufacturer()
        {
            return this.BaseBoard[WMI_BASEBOARD_MANUFACTURER];
        }

        // Retrieves the baseboard product identifier
        public string GetProduct()
        {
            return this.BaseBoard[WMI_BASEBOARD_PRODUCT];
        }

        // Retrieves the baseboard serial number
        public string GetSerial()
        {
            return this.BaseBoard[WMI_BASEBOARD_SERIAL];
        }

        // Retrieves system data from the BIOS
        public BiosData.SystemData GetSystemData()
        {
            lock (this.CacheLock)
            {
                if (this.SystemData == null)
                {
                    try
                    {
                        this.SystemData = Hw.BiosGetStruct<BiosData.SystemData>(Hw.Bios.GetSystem);
                    }
                    catch
                    {
                        // Return default value for devices that don't support this feature (e.g., Victus)
                        this.SystemData = new BiosData.SystemData(new byte[128]);
                    }
                }
                return (BiosData.SystemData)this.SystemData;
            }
        }

        // Queries whether the system is throttling
        public BiosData.Throttling GetThrottling()
        {
            try
            {
                return Hw.BiosGet<BiosData.Throttling>(Hw.Bios.GetThrottling);
            }
            catch
            {
                return BiosData.Throttling.Unknown;
            }
        }

        // Retrieves the baseboard version identifier
        public string GetVersion()
        {
            return this.BaseBoard[WMI_BASEBOARD_VERSION];
        }

    }

}
