//! MSR tabanlı CPU telemetrisi, PawnIO `IntelMSR` modülü üzerinden
//! (C# `CpuTemperature.cs` + `CpuMetrics.cs`'in Intel yolu).
//!
//! AMD (`AMDFamily17` modülü, SMN Tctl + RAPL MSR'ları) henüz bağlanmadı;
//! bu makine Intel olduğu için ilk hedef DTS + paket RAPL.

use std::cell::Cell;
use std::time::Instant;

use pawnio_client::PawnIo;
use windows::Win32::System::Threading::{GetCurrentThread, SetThreadAffinityMask};

/// Intel MSR adresleri.
const IA32_THERM_STATUS: u32 = 0x19C;
const MSR_TEMPERATURE_TARGET: u32 = 0x1A2;
const IA32_PACKAGE_THERM_STATUS: u32 = 0x1B1;
const MSR_RAPL_POWER_UNIT: u32 = 0x606;
const MSR_PKG_ENERGY_STATUS: u32 = 0x611;

const DEFAULT_TJMAX: u32 = 100;

pub struct CpuMsr {
    msr: PawnIo,
    tjmax: u32,
    /// RAPL enerji birimi, Joule.
    energy_unit: f64,
    /// Paket gücü deltası için önceki (zaman, 32-bit enerji sayacı).
    prev_energy: Cell<Option<(Instant, u32)>>,
    logical_cpus: usize,
}

impl CpuMsr {
    /// Intel CPU'da `IntelMSR` modülünü yükler; diğer üreticilerde `None`.
    pub fn new() -> Option<Self> {
        let cpuid = raw_cpuid::CpuId::new();
        let vendor = cpuid.get_vendor_info()?;
        if vendor.as_str() != "GenuineIntel" {
            tracing::info!("CPU üreticisi {} — IntelMSR yolu atlandı (AMD desteği P3 devamında)", vendor.as_str());
            return None;
        }
        let msr = match PawnIo::open_and_load(crate::blobs::INTEL_MSR) {
            Ok(m) => m,
            Err(e) => {
                tracing::warn!("IntelMSR modülü yüklenemedi: {e}");
                return None;
            }
        };

        let read = |addr: u32| -> Option<u64> {
            msr.execute("ioctl_read_msr", &[addr as u64], 1)
                .ok()?
                .first()
                .copied()
        };
        let tjmax = read(MSR_TEMPERATURE_TARGET)
            .map(|v| ((v >> 16) & 0xFF) as u32)
            .filter(|t| (50..=120).contains(t))
            .unwrap_or(DEFAULT_TJMAX);
        let energy_unit = read(MSR_RAPL_POWER_UNIT)
            .map(|v| 1.0 / (1u64 << ((v >> 8) & 0x1F)) as f64)
            .unwrap_or(1.0 / 65536.0);
        let logical_cpus = std::thread::available_parallelism().map_or(1, |n| n.get());
        tracing::info!(tjmax, energy_unit, logical_cpus, "IntelMSR hazır");

        Some(Self {
            msr,
            tjmax,
            energy_unit,
            prev_energy: Cell::new(None),
            logical_cpus,
        })
    }

    fn read(&self, addr: u32) -> Option<u64> {
        self.msr
            .execute("ioctl_read_msr", &[addr as u64], 1)
            .ok()?
            .first()
            .copied()
    }

    /// DTS okumasını °C'ye çevirir (readout = TjMax'a uzaklık; bit 31 geçerlilik).
    fn therm_status_to_celsius(&self, value: u64) -> Option<u32> {
        (value & (1 << 31) != 0).then(|| self.tjmax.saturating_sub(((value >> 16) & 0x7F) as u32))
    }

    /// Paket sıcaklığı, °C.
    pub fn package_temp(&self) -> Option<u32> {
        self.therm_status_to_celsius(self.read(IA32_PACKAGE_THERM_STATUS)?)
    }

    /// Mantıksal işlemci başına çekirdek sıcaklığı; thread o CPU'ya
    /// sabitlenerek okunur, eski affinity geri yüklenir.
    pub fn core_temps(&self) -> Vec<Option<u32>> {
        // SetThreadAffinityMask 64 mantıksal CPU ile sınırlı; üstü için
        // SetThreadGroupAffinity gerekir (bu sınıf makinelerde yeterli).
        let n = self.logical_cpus.min(64);
        let mut temps = Vec::with_capacity(n);
        unsafe {
            let thread = GetCurrentThread();
            let old = SetThreadAffinityMask(thread, 1);
            for cpu in 0..n {
                SetThreadAffinityMask(thread, 1usize << cpu);
                temps.push(
                    self.read(IA32_THERM_STATUS)
                        .and_then(|v| self.therm_status_to_celsius(v)),
                );
            }
            if old != 0 {
                SetThreadAffinityMask(thread, old);
            }
        }
        temps
    }

    /// Paket gücü, Watt (RAPL enerji sayacı deltasından; ilk çağrı None).
    pub fn package_power(&self) -> Option<f32> {
        let energy = (self.read(MSR_PKG_ENERGY_STATUS)? & 0xFFFF_FFFF) as u32;
        let now = Instant::now();
        let prev = self.prev_energy.replace(Some((now, energy)));
        let (t0, e0) = prev?;
        let dt = now.duration_since(t0).as_secs_f64();
        if dt <= 0.0 || dt > 30.0 {
            return None;
        }
        Some((energy.wrapping_sub(e0) as f64 * self.energy_unit / dt) as f32)
    }
}
