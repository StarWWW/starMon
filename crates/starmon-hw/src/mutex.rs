//! Süreçler arası adlandırılmış mutex (C# `EcMutex.cs` muadili).

use std::time::Duration;

use windows::core::PCWSTR;
use windows::Win32::Foundation::{CloseHandle, HANDLE, WAIT_ABANDONED, WAIT_OBJECT_0};
use windows::Win32::System::Threading::{CreateMutexW, ReleaseMutex, WaitForSingleObject};

pub struct NamedMutex {
    handle: HANDLE,
}

unsafe impl Send for NamedMutex {}

impl NamedMutex {
    /// Verilen global adla mutex oluşturur ya da mevcut olana bağlanır.
    pub fn open(name: &str) -> Option<Self> {
        let wide: Vec<u16> = name.encode_utf16().chain([0]).collect();
        let handle = unsafe { CreateMutexW(None, false, PCWSTR(wide.as_ptr())) }.ok()?;
        Some(Self { handle })
    }

    /// Kilidi bekler; `WAIT_ABANDONED` da başarı sayılır (C# davranışı:
    /// sahibi ölen mutex devralınır).
    pub fn acquire(&self, timeout: Duration) -> Option<MutexGuard<'_>> {
        let r = unsafe { WaitForSingleObject(self.handle, timeout.as_millis() as u32) };
        (r == WAIT_OBJECT_0 || r == WAIT_ABANDONED).then_some(MutexGuard { mutex: self })
    }
}

impl Drop for NamedMutex {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.handle);
        }
    }
}

pub struct MutexGuard<'a> {
    mutex: &'a NamedMutex,
}

impl Drop for MutexGuard<'_> {
    fn drop(&mut self) {
        unsafe {
            let _ = ReleaseMutex(self.mutex.handle);
        }
    }
}
