//! Sistem NVMe diskinin sıcaklığı ve okuma/yazma hızı
//! (C# `DiskTemperature.cs` + `DiskActivity.cs` muadili).

use std::time::Instant;
use windows::core::PCWSTR;
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::Storage::FileSystem::{
    CreateFileW, FILE_FLAGS_AND_ATTRIBUTES, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
};
use windows::Win32::System::IO::DeviceIoControl;

const IOCTL_STORAGE_QUERY_PROPERTY: u32 = 0x002D_1400;
const IOCTL_DISK_PERFORMANCE: u32 = 0x0007_0020;

// STORAGE_PROPERTY_QUERY başlığı (8) + STORAGE_PROTOCOL_SPECIFIC_DATA (40) + NVMe log (512)
const HEADER_SIZE: usize = 8;
const PROTOCOL_DATA_SIZE: usize = 40;
const LOG_SIZE: usize = 512;
const BUFFER_SIZE: usize = HEADER_SIZE + PROTOCOL_DATA_SIZE + LOG_SIZE;
const DATA_OFFSET: usize = HEADER_SIZE + PROTOCOL_DATA_SIZE;

/// Pencere uzun süre gizli kaldıysa aradaki delta hız olarak anlamsızdır.
const MAX_DELTA_SECS: f64 = 30.0;

#[derive(Clone, Copy, Debug, Default)]
pub struct DiskInfo {
    pub temp_c: Option<i32>,
    pub read_bytes_per_sec: Option<u64>,
    pub write_bytes_per_sec: Option<u64>,
}

struct DriveHandle(HANDLE);

impl DriveHandle {
    /// Sorgu için erişim hakkı gerekmez (dwDesiredAccess = 0).
    fn open(index: u32) -> Option<Self> {
        let path: Vec<u16> = format!("\\\\.\\PhysicalDrive{index}")
            .encode_utf16()
            .chain([0])
            .collect();
        let h = unsafe {
            CreateFileW(
                PCWSTR(path.as_ptr()),
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                None,
                OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES(0),
                None,
            )
        }
        .ok()?;
        Some(Self(h))
    }
}

impl Drop for DriveHandle {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

/// NVMe health log sayfasından kompozit sıcaklık (Kelvin, byte 1-2) → °C.
fn nvme_temp(drive: u32) -> Option<i32> {
    let h = DriveHandle::open(drive)?;
    let mut buf = [0u8; BUFFER_SIZE];
    buf[0..4].copy_from_slice(&50u32.to_le_bytes()); // StorageDeviceProtocolSpecificProperty
    buf[4..8].copy_from_slice(&0u32.to_le_bytes()); // PropertyStandardQuery
    buf[8..12].copy_from_slice(&3u32.to_le_bytes()); // ProtocolTypeNvme
    buf[12..16].copy_from_slice(&2u32.to_le_bytes()); // NVMeDataTypeLogPage
    buf[16..20].copy_from_slice(&0x02u32.to_le_bytes()); // NVMeLogPageHealthInfo
    buf[20..24].copy_from_slice(&0u32.to_le_bytes()); // ProtocolDataRequestSubValue
    buf[24..28].copy_from_slice(&(PROTOCOL_DATA_SIZE as u32).to_le_bytes()); // ProtocolDataOffset
    buf[28..32].copy_from_slice(&(LOG_SIZE as u32).to_le_bytes()); // ProtocolDataLength

    let ptr = buf.as_mut_ptr();
    let mut returned = 0u32;
    unsafe {
        DeviceIoControl(
            h.0,
            IOCTL_STORAGE_QUERY_PROPERTY,
            Some(ptr as *const _),
            BUFFER_SIZE as u32,
            Some(ptr as *mut _),
            BUFFER_SIZE as u32,
            Some(&mut returned),
            None,
        )
        .ok()?;
    }
    let kelvin = buf[DATA_OFFSET + 1] as i32 | ((buf[DATA_OFFSET + 2] as i32) << 8);
    let celsius = kelvin - 273;
    (celsius > 0 && celsius < 120).then_some(celsius)
}

/// Diskin kümülatif okunan/yazılan byte sayaçları (DISK_PERFORMANCE: offset 0 ve 8).
fn counters(drive: u32) -> Option<(i64, i64)> {
    let h = DriveHandle::open(drive)?;
    let mut buf = [0u8; 256];
    let mut returned = 0u32;
    unsafe {
        DeviceIoControl(
            h.0,
            IOCTL_DISK_PERFORMANCE,
            None,
            0,
            Some(buf.as_mut_ptr() as *mut _),
            buf.len() as u32,
            Some(&mut returned),
            None,
        )
        .ok()?;
    }
    Some((
        i64::from_le_bytes(buf[0..8].try_into().unwrap()),
        i64::from_le_bytes(buf[8..16].try_into().unwrap()),
    ))
}

#[derive(Default)]
enum TempDrive {
    #[default]
    Unprobed,
    NotFound,
    Found(u32),
}

#[derive(Default)]
pub struct DiskSampler {
    temp_drive: TempDrive,
    prev: Option<(Instant, i64, i64)>,
}

impl DiskSampler {
    /// İlk birkaç fiziksel diski bir kez yoklar, cevap vereni önbelleğe alır.
    pub fn sample_temp(&mut self) -> Option<i32> {
        match self.temp_drive {
            TempDrive::Found(i) => {
                if let Some(t) = nvme_temp(i) {
                    return Some(t);
                }
                self.temp_drive = TempDrive::Unprobed; // kaybettik; sonraki çağrıda yeniden yokla
                None
            }
            TempDrive::NotFound => None,
            TempDrive::Unprobed => {
                for i in 0..4 {
                    if let Some(t) = nvme_temp(i) {
                        tracing::info!("NVMe health log fiziksel disk {i} üzerinde bulundu");
                        self.temp_drive = TempDrive::Found(i);
                        return Some(t);
                    }
                }
                tracing::info!("NVMe health log sorgusuna cevap veren disk yok; disk sıcaklığı kapalı");
                self.temp_drive = TempDrive::NotFound;
                None
            }
        }
    }

    /// Önceki çağrıdan bu yana sistem diski (PhysicalDrive0) aktarım hızı.
    pub fn sample_activity(&mut self) -> (Option<u64>, Option<u64>) {
        let Some((read, write)) = counters(0) else {
            return (None, None);
        };
        let now = Instant::now();
        let rates = self.prev.map(|(t, pr, pw)| {
            let dt = now.duration_since(t).as_secs_f64();
            if dt <= 0.0 || dt > MAX_DELTA_SECS {
                (None, None)
            } else {
                (
                    Some((read.saturating_sub(pr).max(0) as f64 / dt) as u64),
                    Some((write.saturating_sub(pw).max(0) as f64 / dt) as u64),
                )
            }
        });
        self.prev = Some((now, read, write));
        rates.unwrap_or((None, None))
    }
}
