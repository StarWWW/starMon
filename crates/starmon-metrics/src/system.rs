//! CPU yükü, bellek ve çalışma süresi (C# `SystemMetrics.cs` muadili).

use windows::Win32::Foundation::FILETIME;
use windows::Win32::System::SystemInformation::{
    GetTickCount64, GlobalMemoryStatusEx, MEMORYSTATUSEX,
};
use windows::Win32::System::Threading::GetSystemTimes;

#[derive(Clone, Copy, Debug, Default)]
pub struct MemoryInfo {
    pub load_percent: u32,
    pub used_mb: u64,
    pub total_mb: u64,
}

fn filetime_100ns(ft: FILETIME) -> u64 {
    ((ft.dwHighDateTime as u64) << 32) | ft.dwLowDateTime as u64
}

/// `GetSystemTimes` deltasından toplam CPU yükü. İlk örnekleme referans
/// oluşturur ve `None` döner.
#[derive(Default)]
pub struct CpuLoadSampler {
    /// (idle, kernel, user) — 100 ns birimlerinde kümülatif süreler.
    prev: Option<(u64, u64, u64)>,
}

impl CpuLoadSampler {
    pub fn sample(&mut self) -> Option<f32> {
        let (mut idle, mut kernel, mut user) =
            (FILETIME::default(), FILETIME::default(), FILETIME::default());
        unsafe { GetSystemTimes(Some(&mut idle), Some(&mut kernel), Some(&mut user)).ok()? };
        let now = (
            filetime_100ns(idle),
            filetime_100ns(kernel),
            filetime_100ns(user),
        );
        let load = self.prev.map(|prev| {
            // kernel süresi idle'ı da içerir
            let total = now.1.saturating_sub(prev.1) + now.2.saturating_sub(prev.2);
            let idle_d = now.0.saturating_sub(prev.0);
            if total == 0 {
                0.0
            } else {
                100.0 * total.saturating_sub(idle_d) as f32 / total as f32
            }
        });
        self.prev = Some(now);
        load
    }
}

pub fn memory() -> Option<MemoryInfo> {
    let mut status = MEMORYSTATUSEX {
        dwLength: size_of::<MEMORYSTATUSEX>() as u32,
        ..Default::default()
    };
    unsafe { GlobalMemoryStatusEx(&mut status).ok()? };
    Some(MemoryInfo {
        load_percent: status.dwMemoryLoad,
        used_mb: (status.ullTotalPhys - status.ullAvailPhys) / (1024 * 1024),
        total_mb: status.ullTotalPhys / (1024 * 1024),
    })
}

pub fn uptime_secs() -> u64 {
    unsafe { GetTickCount64() / 1000 }
}
