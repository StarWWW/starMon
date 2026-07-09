//! EC register haritası ve protokol sabitleri (C# `EcData.cs` portu).

/// ACPI EC portları.
pub const PORT_COMMAND: u8 = 0x66; // EC_SC
pub const PORT_DATA: u8 = 0x62; // EC_DATA

/// EC komutları.
pub const CMD_READ: u8 = 0x80; // RD_EC
pub const CMD_WRITE: u8 = 0x81; // WR_EC

/// EC durum bitleri (port 0x66).
pub const STATUS_OUT_FULL: u8 = 0x01; // EC_OBF
pub const STATUS_IN_FULL: u8 = 0x02; // EC_IBF

/// C# `ConfigData.cs` varsayılanları.
pub const RETRY_LIMIT: u32 = 3;
pub const WAIT_LIMIT: u32 = 30;
pub const FAIL_LIMIT: u32 = 15;
pub const MUTEX_TIMEOUT_MS: u64 = 200;
pub const MUTEX_NAME: &str = "Global\\Access_EC";

/// Yazılabilir EC registerları — yazma API'si yalnız bu allowlist'i kabul eder.
/// Rastgele register yazmak EC durumunu bozabilir; yeni hedefler ancak C#
/// referansındaki karşılığı doğrulanarak eklenmeli.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum EcWritable {
    /// Sol fan hedef hız [%] (XSS1).
    LeftFanTargetPercent,
    /// Sağ fan hedef hız [%] (XSS2).
    RightFanTargetPercent,
    /// Sol fan hedef hız [krpm*100] (SRP1).
    LeftFanTargetLevel,
    /// Sağ fan hedef hız [krpm*100] (SRP2).
    RightFanTargetLevel,
    /// Manuel fan kontrolü aç/kapa (OMCC, 0x06 = açık, 0x00 = kapalı).
    ManualToggle,
    /// Manuel fan geri sayımı [s] (XFCD). Bazı Omen modellerinde 0'a inince
    /// EC fan kontrolünü otomatiğe geri alır; test edilen Victus'ta (2026-07)
    /// sayaç işlese de geri alma YOK — failsafe'e donanım güvencesi gibi
    /// yaslanılmamalı.
    Countdown,
    /// Performans modu (HPCM).
    PerformanceMode,
    /// Fan aç/kapa anahtarı (SFAN, 0x02 = kapalı).
    FanSwitch,
}

impl EcWritable {
    pub const fn register(self) -> u8 {
        match self {
            Self::LeftFanTargetPercent => reg::XSS1,
            Self::RightFanTargetPercent => reg::XSS2,
            Self::LeftFanTargetLevel => reg::SRP1,
            Self::RightFanTargetLevel => reg::SRP2,
            Self::ManualToggle => reg::OMCC,
            Self::Countdown => reg::XFCD,
            Self::PerformanceMode => reg::HPCM,
            Self::FanSwitch => reg::SFAN,
        }
    }
}

/// OMCC manuel fan kontrolü değerleri (C# `PlatformData.FanManual`).
pub const FAN_MANUAL_ON: u8 = 0x06;
pub const FAN_MANUAL_OFF: u8 = 0x00;

/// Sık kullanılan registerlar (tam liste `EcData.cs`'te; port ilerledikçe genişler).
pub mod reg {
    pub const XSS1: u8 = 0x2C; // Sol fan hedef hız [%]
    pub const XSS2: u8 = 0x2D; // Sağ fan hedef hız [%]
    pub const XGS1: u8 = 0x2E; // Sol fan mevcut hız [%]
    pub const XGS2: u8 = 0x2F; // Sağ fan mevcut hız [%]
    pub const SRP1: u8 = 0x34; // Sol fan hedef hız [krpm]
    pub const SRP2: u8 = 0x35; // Sağ fan hedef hız [krpm]
    pub const TNT2: u8 = 0x47; // Sıcaklık [°C]
    pub const TNT3: u8 = 0x48; // Sıcaklık [°C]
    pub const TNT4: u8 = 0x49; // Sıcaklık [°C]
    pub const IRSN: u8 = 0x4A; // Sıcaklık [°C]
    pub const TNT5: u8 = 0x4B; // Sıcaklık [°C]
    pub const CPUT: u8 = 0x57; // CPU sıcaklığı [°C]
    pub const RTMP: u8 = 0x58; // Sıcaklık [°C]
    pub const TMP1: u8 = 0x59; // Sıcaklık [°C]
    pub const OMCC: u8 = 0x62; // Manuel fan kontrolü
    pub const XFCD: u8 = 0x63; // Manuel fan otomatik geri sayımı [s]
    pub const HPCM: u8 = 0x95; // Performans modu
    pub const XBCH: u8 = 0x96; // Batarya şarj seviyesi
    pub const QBHK: u8 = 0xA0; // Son kısayol tuşu
    pub const RPM1: u8 = 0xB0; // Sol fan hızı [rpm] 1/2
    pub const RPM2: u8 = 0xB1; // Sol fan hızı [rpm] 2/2
    pub const RPM3: u8 = 0xB2; // Sağ fan hızı [rpm] 1/2
    pub const RPM4: u8 = 0xB3; // Sağ fan hızı [rpm] 2/2
    pub const GPTM: u8 = 0xB7; // GPU sıcaklığı [°C]
    pub const SFAN: u8 = 0xF4; // Fan aç/kapa
}
