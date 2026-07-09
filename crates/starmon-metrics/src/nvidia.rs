//! NVIDIA GPU telemetrisi, NVML üzerinden (C# `GpuNvidia.cs`'in NVML kısmı).
//! NVAPI detayları (hotspot vs.) P5'te `libloading` ile eklenecek.

use nvml_wrapper::enum_wrappers::device::{Clock, TemperatureSensor};
use nvml_wrapper::Nvml;

#[derive(Clone, Copy, Debug, Default)]
pub struct GpuInfo {
    pub temp_c: Option<u32>,
    pub load_percent: Option<u32>,
    pub core_mhz: Option<u32>,
    pub mem_mhz: Option<u32>,
    pub vram_used_mb: Option<u64>,
    pub vram_total_mb: Option<u64>,
    pub power_w: Option<f32>,
}

pub struct GpuReader {
    nvml: Option<Nvml>,
}

impl GpuReader {
    pub fn new() -> Self {
        let nvml = match Nvml::init() {
            Ok(n) => Some(n),
            Err(e) => {
                tracing::info!("NVML kullanılamıyor (NVIDIA GPU yok olabilir): {e}");
                None
            }
        };
        Self { nvml }
    }

    pub fn sample(&self) -> Option<GpuInfo> {
        // Device, Nvml'e borç verdiği için her örneklemede yeniden alınır (ucuz).
        let dev = self.nvml.as_ref()?.device_by_index(0).ok()?;
        let mem = dev.memory_info().ok();
        Some(GpuInfo {
            temp_c: dev.temperature(TemperatureSensor::Gpu).ok(),
            load_percent: dev.utilization_rates().ok().map(|u| u.gpu),
            core_mhz: dev.clock_info(Clock::Graphics).ok(),
            mem_mhz: dev.clock_info(Clock::Memory).ok(),
            vram_used_mb: mem.as_ref().map(|m| m.used / (1024 * 1024)),
            vram_total_mb: mem.as_ref().map(|m| m.total / (1024 * 1024)),
            power_w: dev.power_usage().ok().map(|mw| mw as f32 / 1000.0),
        })
    }
}
