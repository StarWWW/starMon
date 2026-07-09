//! PawnIO sürücüsü için ham DeviceIoControl istemcisi (PawnIOLib.dll gerekmez).
//!
//! PawnIO (github.com/namazso/PawnIO), imzalı tek bir kernel driver üzerinde
//! sandbox'lanmış Pawn bytecode modülleri çalıştırır. Her modül ayrı bir
//! device handle'ına yüklenir; fonksiyonlar 32 byte'lık ASCII ad + u64 girdi
//! dizisi ile çağrılır, çıktı u64 dizisidir (LibreHardwareMonitor'un
//! `PawnIo.cs` istemcisiyle aynı protokol).

use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::Storage::FileSystem::{
    CreateFileW, FILE_ATTRIBUTE_NORMAL, FILE_GENERIC_READ, FILE_GENERIC_WRITE, FILE_SHARE_READ,
    FILE_SHARE_WRITE, OPEN_EXISTING,
};
use windows::Win32::System::Registry::{RegGetValueW, HKEY_LOCAL_MACHINE, RRF_RT_REG_SZ};
use windows::Win32::System::IO::DeviceIoControl;

const DEVICE_PATH: PCWSTR = w!(r"\\?\GLOBALROOT\Device\PawnIO");
// Değerler LibreHardwareMonitor PawnIo.cs ile birebir:
// LOAD = DEVICE_TYPE | (0x821 << 2), EXECUTE = DEVICE_TYPE | (0x841 << 2).
const IOCTL_PIO_LOAD_BINARY: u32 = (41394 << 16) | (0x821 << 2);
const IOCTL_PIO_EXECUTE_FN: u32 = (41394 << 16) | (0x841 << 2);

/// Execute girdisinde fonksiyon adına ayrılan alan.
const NAME_LEN: usize = 32;

#[derive(Debug, thiserror::Error)]
pub enum PawnIoError {
    #[error("PawnIO aygıtı açılamadı (sürücü kurulu değil veya yönetici değilsiniz): {0}")]
    Open(windows::core::Error),
    #[error("modül yüklenemedi: {0}")]
    Load(windows::core::Error),
    #[error("çağrı başarısız ({name}): {source}")]
    Execute {
        name: String,
        source: windows::core::Error,
    },
    #[error("fonksiyon adı {NAME_LEN} byte'ı aşıyor")]
    NameTooLong,
}

/// PawnIO kurulumunu registry'den tespit eder (sürüm döner).
pub fn installed_version() -> Option<String> {
    unsafe {
        let mut buf = [0u16; 64];
        let mut len = (buf.len() * 2) as u32;
        RegGetValueW(
            HKEY_LOCAL_MACHINE,
            w!(r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"),
            w!("DisplayVersion"),
            RRF_RT_REG_SZ,
            None,
            Some(buf.as_mut_ptr() as *mut _),
            Some(&mut len),
        )
        .ok()
        .ok()?;
        let n = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
        Some(String::from_utf16_lossy(&buf[..n]))
    }
}

/// Yüklenmiş tek bir PawnIO modülü örneği.
pub struct PawnIo {
    handle: HANDLE,
}

// Handle yalnız DeviceIoControl'de kullanılır; thread'ler arası taşınabilir.
unsafe impl Send for PawnIo {}

impl PawnIo {
    /// Aygıtı açar ve verilen modül blob'unu bu handle'a yükler.
    pub fn open_and_load(blob: &[u8]) -> Result<Self, PawnIoError> {
        unsafe {
            let handle = CreateFileW(
                DEVICE_PATH,
                (FILE_GENERIC_READ | FILE_GENERIC_WRITE).0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                None,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                None,
            )
            .map_err(PawnIoError::Open)?;
            let io = Self { handle };

            let mut returned = 0u32;
            DeviceIoControl(
                io.handle,
                IOCTL_PIO_LOAD_BINARY,
                Some(blob.as_ptr() as *const _),
                blob.len() as u32,
                None,
                0,
                Some(&mut returned),
                None,
            )
            .map_err(PawnIoError::Load)?;
            Ok(io)
        }
    }

    /// Modül fonksiyonunu çağırır. Girdi: 32 byte NUL-padded ASCII ad + u64'ler;
    /// çıktı: en fazla `out_len` u64 (sürücünün gerçekten yazdığı kadarı döner).
    pub fn execute(
        &self,
        name: &str,
        input: &[u64],
        out_len: usize,
    ) -> Result<Vec<u64>, PawnIoError> {
        if name.len() >= NAME_LEN {
            return Err(PawnIoError::NameTooLong);
        }
        let mut in_buf = vec![0u8; NAME_LEN + input.len() * 8];
        in_buf[..name.len()].copy_from_slice(name.as_bytes());
        for (i, v) in input.iter().enumerate() {
            in_buf[NAME_LEN + i * 8..NAME_LEN + (i + 1) * 8].copy_from_slice(&v.to_le_bytes());
        }

        let mut out = vec![0u64; out_len];
        let mut returned = 0u32;
        unsafe {
            DeviceIoControl(
                self.handle,
                IOCTL_PIO_EXECUTE_FN,
                Some(in_buf.as_ptr() as *const _),
                in_buf.len() as u32,
                (!out.is_empty()).then_some(out.as_mut_ptr() as *mut _),
                (out.len() * 8) as u32,
                Some(&mut returned),
                None,
            )
            .map_err(|e| PawnIoError::Execute {
                name: name.into(),
                source: e,
            })?;
        }
        out.truncate(returned as usize / 8);
        Ok(out)
    }
}

impl Drop for PawnIo {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.handle);
        }
    }
}
