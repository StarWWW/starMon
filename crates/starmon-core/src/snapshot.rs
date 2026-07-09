//! hw sampler thread'inin ürettiği, UI'ın kilitsiz okuduğu anlık görüntü.
//! P3+ ile EC/BIOS alanları (fan RPM, CPU/GPU sıcaklık, fan modu) eklenecek.

use starmon_metrics::battery::BatteryInfo;
use starmon_metrics::disk::DiskInfo;
use starmon_metrics::network::NetworkRates;
use starmon_metrics::nvidia::GpuInfo;
use starmon_metrics::system::MemoryInfo;

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
    pub uptime_secs: u64,
}
