use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{CloseHandle, GetLastError, ERROR_ALREADY_EXISTS, HANDLE};
use windows::Win32::System::Threading::CreateMutexW;
use windows::Win32::UI::WindowsAndMessaging::{
    FindWindowW, SetForegroundWindow, ShowWindow, SW_RESTORE,
};

/// Süreç ömrü boyunca tutulan tek-örnek mutex'i (C#'taki `Global\StarMonGui`
/// deseninin karşılığı).
pub struct InstanceLock(HANDLE);

// Mutex handle'ı thread'ler arasında güvenle taşınabilir.
unsafe impl Send for InstanceLock {}

impl Drop for InstanceLock {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

/// `Global\StarMonRs` mutex'ini alır; zaten varsa `None` döner (ikinci örnek).
pub fn acquire() -> Option<InstanceLock> {
    unsafe {
        let handle = CreateMutexW(None, true, w!("Global\\StarMonRs")).ok()?;
        if GetLastError() == ERROR_ALREADY_EXISTS {
            let _ = CloseHandle(handle);
            return None;
        }
        Some(InstanceLock(handle))
    }
}

/// Çalışan örneğin ana penceresini öne getirir.
///
/// P5'te pencere tray'e gizlenebileceği için `RegisterWindowMessageW` broadcast
/// modeline geçilecek (C# `GuiFilter.cs` muadili); P0'da başlıkla FindWindow yeterli.
pub fn focus_existing(title: &str) {
    let wide: Vec<u16> = title.encode_utf16().chain(std::iter::once(0)).collect();
    unsafe {
        if let Ok(hwnd) = FindWindowW(PCWSTR::null(), PCWSTR(wide.as_ptr())) {
            let _ = ShowWindow(hwnd, SW_RESTORE);
            let _ = SetForegroundWindow(hwnd);
        }
    }
}
