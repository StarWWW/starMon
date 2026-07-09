//! BIOS veri yapıları — C# `BiosData.cs`'teki `Pack=1` struct'ların birebir
//! karşılıkları. Tüm alanlar padding'siz dizilir; boyutlar derleme zamanında
//! doğrulanır. Byte dizilimleri WMI'a aynen gidip geldiği için değiştirilemez.

use zerocopy::byteorder::little_endian::U16;
use zerocopy::{FromBytes, Immutable, IntoBytes, KnownLayout};

// ---- Enum değerleri (ham u8 alanları için yorumlayıcılar) ----

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u8)]
pub enum FanMode {
    Default = 0,
    Performance = 1,
    Cool = 2,
    Quiet = 3,
    Extreme = 4,
}

impl FanMode {
    /// EC `HPCM` okuması için; bilinmeyen değerler `None`.
    pub fn from_u8(v: u8) -> Option<Self> {
        match v {
            0 => Some(Self::Default),
            1 => Some(Self::Performance),
            2 => Some(Self::Cool),
            3 => Some(Self::Quiet),
            4 => Some(Self::Extreme),
            _ => None,
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u8)]
pub enum GpuMode {
    Hybrid = 0x00,
    Discrete = 0x01,
    Optimus = 0x02,
}

impl GpuMode {
    pub fn from_u8(v: u8) -> Self {
        match v {
            0x01 => Self::Discrete,
            0x02 => Self::Optimus,
            _ => Self::Hybrid, // BIOS çağrısı başarısızsa da Hybrid varsayılır
        }
    }
}

/// Klavye aydınlatması: 0x64 kapalı, 0xE4 açık (bit 7).
pub const BACKLIGHT_OFF: u8 = 0x64;
pub const BACKLIGHT_ON: u8 = 0xE4;

/// `SystemData::support_flags` bitleri.
pub const SUPPORT_SW_FAN_CTL: u8 = 0x01;
pub const SUPPORT_EXTREME_MODE: u8 = 0x02;
pub const SUPPORT_EXTREME_MODE_UNLOCK: u8 = 0x04;

// ---- Packed yapılar ----

#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct RgbColor {
    pub red: u8,
    pub green: u8,
    pub blue: u8,
}

/// Klavye renk tablosu (128 byte): zone sayısı + 24 byte padding + 4 zone RGB.
#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug)]
#[repr(C)]
pub struct ColorTable {
    pub zone_count: u8,
    padding: [u8; 24],
    pub zone: [RgbColor; 4],
    tail: [u8; 91],
}
const _: () = assert!(size_of::<ColorTable>() == 128);

/// Fan seviyesi girdisi: (fan1, fan2) seviyeleri + sıcaklık eşiği.
#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct FanLevel {
    pub fan1_level: u8,
    pub fan2_level: u8,
    pub temperature: u8,
}

/// Fan hız tablosu (128 byte): sayılar + 14 seviye girdisi.
#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug)]
#[repr(C)]
pub struct FanTable {
    pub fan_count: u8,
    pub level_count: u8,
    pub level: [FanLevel; 14],
    tail: [u8; 84],
}
const _: () = assert!(size_of::<FanTable>() == 128);

/// CPU güç limitleri (4 byte); 0xFF = değiştirme.
#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug, PartialEq, Eq)]
#[repr(C)]
pub struct CpuPowerData {
    pub limit1: u8,
    pub limit2: u8,
    pub limit4: u8,
    pub limit_with_gpu: u8,
}
const _: () = assert!(size_of::<CpuPowerData>() == 4);

impl Default for CpuPowerData {
    fn default() -> Self {
        Self { limit1: 0xFF, limit2: 0xFF, limit4: 0xFF, limit_with_gpu: 0xFF }
    }
}

/// GPU güç ayarları (4 byte): custom TGP, PPAB, D-state, tepe sıcaklık eşiği.
#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(C)]
pub struct GpuPowerData {
    pub custom_tgp: u8,
    pub ppab: u8,
    pub dstate: u8,
    pub peak_temperature: u8,
}
const _: () = assert!(size_of::<GpuPowerData>() == 4);

/// Sistem tasarım verisi (128 byte). Gözlenen örnek: E6 00 35 01 01 D7 00 0C 00 ...
#[derive(FromBytes, IntoBytes, Immutable, KnownLayout, Clone, Copy, Debug)]
#[repr(C)]
pub struct SystemData {
    pub status_flags: U16,
    pub unknown2: u8,
    /// 0 = legacy, 1 = güncel cihazlar.
    pub thermal_policy: u8,
    /// Bkz. `SUPPORT_*` bitleri.
    pub support_flags: u8,
    /// Varsayılan PL4, Watt.
    pub default_cpu_power_limit4: u8,
    pub bios_oc: u8,
    pub gpu_mode_switch: u8,
    pub default_cpu_power_limit_with_gpu: u8,
    raw_block: [u8; 119],
}
const _: () = assert!(size_of::<SystemData>() == 128);

/// 128-byte WMI cevabından okuma; kısa/uzun cevaplara tolerans için tek nokta.
macro_rules! parse_impl {
    ($($t:ty),+) => {$(
        impl $t {
            pub fn parse(raw: &[u8]) -> Option<Self> {
                Self::read_from_bytes(raw.get(..size_of::<Self>())?).ok()
            }
        }
    )+};
}
parse_impl!(ColorTable, FanTable, CpuPowerData, GpuPowerData, SystemData);

impl SystemData {
    pub fn supports_sw_fan_ctl(&self) -> bool {
        self.support_flags & SUPPORT_SW_FAN_CTL != 0
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use core::mem::offset_of;

    /// C# `Marshal.OffsetOf` değerleriyle birebir.
    #[test]
    fn offsets_match_csharp_layout() {
        assert_eq!(offset_of!(ColorTable, zone), 25);
        assert_eq!(offset_of!(FanTable, level), 2);
        assert_eq!(offset_of!(SystemData, thermal_policy), 3);
        assert_eq!(offset_of!(SystemData, default_cpu_power_limit4), 5);
        assert_eq!(offset_of!(SystemData, default_cpu_power_limit_with_gpu), 8);
    }

    /// BiosData.cs'te belgelenen gözlenmiş SystemData örneği.
    #[test]
    fn parses_observed_system_data() {
        let mut raw = [0u8; 128];
        raw[..9].copy_from_slice(&[0xE6, 0x00, 0x35, 0x01, 0x01, 0xD7, 0x00, 0x0C, 0x00]);
        let sys = SystemData::parse(&raw).unwrap();
        assert_eq!(sys.status_flags.get(), 0x00E6);
        assert_eq!(sys.thermal_policy, 1);
        assert!(sys.supports_sw_fan_ctl());
        assert_eq!(sys.default_cpu_power_limit4, 215);
        assert_eq!(sys.gpu_mode_switch, 0x0C);
    }

    #[test]
    fn fan_table_roundtrip() {
        let mut raw = [0u8; 128];
        raw[0] = 2; // fan_count
        raw[1] = 2; // level_count
        raw[2..8].copy_from_slice(&[10, 12, 40, 30, 32, 60]);
        let t = FanTable::parse(&raw).unwrap();
        assert_eq!(t.level[0], FanLevel { fan1_level: 10, fan2_level: 12, temperature: 40 });
        assert_eq!(t.level[1].temperature, 60);
        assert_eq!(t.as_bytes(), &raw);
    }
}
