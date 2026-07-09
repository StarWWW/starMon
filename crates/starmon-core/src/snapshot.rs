//! hw sampler thread'inin ürettiği, UI'ın kilitsiz okuduğu anlık görüntü.
//! P3+ ile EC/BIOS alanları (fan RPM, CPU/GPU sıcaklık, fan modu) eklenecek.

use std::sync::Arc;

use hp_wmi::Capabilities;
use starmon_metrics::battery::BatteryInfo;
use starmon_metrics::disk::DiskInfo;
use starmon_metrics::network::NetworkRates;
use starmon_metrics::nvidia::GpuInfo;
use starmon_metrics::system::MemoryInfo;

/// HP WMI BIOS'tan periyodik okunan canlı değerler.
#[derive(Clone, Copy, Debug, Default)]
pub struct BiosSnapshot {
    /// (CPU fanı, GPU fanı) hız seviyeleri.
    pub fan_level: Option<(u8, u8)>,
    /// BIOS termal sensörü, °C.
    pub temperature_c: Option<u8>,
    pub max_fan: Option<bool>,
}

#[derive(Clone, Debug, Default)]
pub struct Snapshot {
    /// 1 saniyelik master tick sayacı.
    pub tick: u64,
    pub cpu_load_percent: Option<f32>,
    pub memory: Option<MemoryInfo>,
    pub battery: Option<BatteryInfo>,
    pub network: Option<NetworkRates>,
    pub gpu: Option<GpuInfo>,
    pub disk: Option<DiskInfo>,
    pub brightness_percent: Option<u8>,
    pub bios: Option<BiosSnapshot>,
    /// Başlangıçta bir kez toplanır, değişmez.
    pub bios_caps: Option<Arc<Capabilities>>,
    pub uptime_secs: u64,
}
