//! ACPI EC protokolü — C# `Ec.cs`'in birebir portu, port G/Ç'si PawnIO
//! `LpcACPIEC` modülü üzerinden (yalnız 0x62/0x66 portlarına izin verir).
//!
//! Modül handshake/senkronizasyon YAPMAZ; klasik EC el sıkışması burada,
//! süreçler arası `Global\Access_EC` kilidi altında yürütülür.

use std::cell::Cell;
use std::time::Duration;

use pawnio_client::PawnIo;

use crate::ec_data::*;
use crate::mutex::NamedMutex;
use crate::EcError;

pub struct EmbeddedController {
    pio: PawnIo,
    mutex: NamedMutex,
    /// C# `WaitReadFailCount`: art arda okuma beklemesi başarısızlığı sayacı;
    /// limit aşılınca beklemeden okumaya düşer (bazı EC'ler OBF'yi geç kaldırır).
    wait_read_fails: Cell<u32>,
}

impl EmbeddedController {
    pub fn new() -> Result<Self, EcError> {
        let pio = PawnIo::open_and_load(crate::blobs::LPC_ACPI_EC)?;
        let mutex = NamedMutex::open(MUTEX_NAME).ok_or(EcError::MutexTimeout(0))?;
        Ok(Self {
            pio,
            mutex,
            wait_read_fails: Cell::new(0),
        })
    }

    fn read_port(&self, port: u8) -> Option<u8> {
        self.pio
            .execute("ioctl_pio_read", &[port as u64], 1)
            .ok()?
            .first()
            .map(|v| *v as u8)
    }

    fn write_port(&self, port: u8, value: u8) -> Option<()> {
        self.pio
            .execute("ioctl_pio_write", &[port as u64, value as u64], 0)
            .ok()?;
        Some(())
    }

    /// C# `Wait()`: durum bitinin istenen hale gelmesini sıkı döngüyle bekler.
    fn wait(&self, status: u8, is_set: bool) -> bool {
        for _ in 0..WAIT_LIMIT {
            let Some(mut value) = self.read_port(PORT_COMMAND) else {
                return false;
            };
            if is_set {
                value = !value;
            }
            if status & value == 0 {
                return true;
            }
        }
        false
    }

    fn wait_write(&self) -> bool {
        self.wait(STATUS_IN_FULL, false)
    }

    fn wait_read(&self) -> bool {
        if self.wait_read_fails.get() > FAIL_LIMIT {
            true
        } else if self.wait(STATUS_OUT_FULL, true) {
            self.wait_read_fails.set(0);
            true
        } else {
            self.wait_read_fails.set(self.wait_read_fails.get() + 1);
            false
        }
    }

    /// C# `WriteByteImpl`: wait → CMD_WRITE → wait → register → wait → değer.
    fn write_byte_impl(&self, register: u8, value: u8) -> Option<()> {
        if self.wait_write() {
            self.write_port(PORT_COMMAND, CMD_WRITE)?;
            if self.wait_write() {
                self.write_port(PORT_DATA, register)?;
                if self.wait_write() {
                    self.write_port(PORT_DATA, value)?;
                    return Some(());
                }
            }
        }
        None
    }

    fn read_byte_impl(&self, register: u8) -> Option<u8> {
        if self.wait_write() {
            self.write_port(PORT_COMMAND, CMD_READ)?;
            if self.wait_write() {
                self.write_port(PORT_DATA, register)?;
                if self.wait_write() && self.wait_read() {
                    return self.read_port(PORT_DATA);
                }
            }
        }
        None
    }

    /// Bir byte okur: mutex + retry sarmalayıcısı (C# `ReadByte` + `Hw.EcExec`).
    pub fn read_byte(&self, register: u8) -> Result<u8, EcError> {
        let _guard = self
            .mutex
            .acquire(Duration::from_millis(MUTEX_TIMEOUT_MS))
            .ok_or(EcError::MutexTimeout(MUTEX_TIMEOUT_MS as u32))?;
        for _ in 0..RETRY_LIMIT {
            if let Some(v) = self.read_byte_impl(register) {
                return Ok(v);
            }
        }
        Err(EcError::Handshake(register))
    }

    /// Bir word okur (little-endian, ardışık iki register).
    pub fn read_word(&self, register: u8) -> Result<u16, EcError> {
        let _guard = self
            .mutex
            .acquire(Duration::from_millis(MUTEX_TIMEOUT_MS))
            .ok_or(EcError::MutexTimeout(MUTEX_TIMEOUT_MS as u32))?;
        for _ in 0..RETRY_LIMIT {
            let lo = self.read_byte_impl(register);
            let hi = lo.and_then(|_| self.read_byte_impl(register.wrapping_add(1)));
            if let (Some(lo), Some(hi)) = (lo, hi) {
                return Ok(u16::from_le_bytes([lo, hi]));
            }
        }
        Err(EcError::Handshake(register))
    }

    /// Bir byte yazar: yalnız `EcWritable` allowlist'indeki hedeflere,
    /// mutex + retry sarmalayıcısıyla (C# `WriteByte` + `Hw.EcExec`).
    pub fn write_byte(&self, target: EcWritable, value: u8) -> Result<(), EcError> {
        let register = target.register();
        let _guard = self
            .mutex
            .acquire(Duration::from_millis(MUTEX_TIMEOUT_MS))
            .ok_or(EcError::MutexTimeout(MUTEX_TIMEOUT_MS as u32))?;
        for _ in 0..RETRY_LIMIT {
            if self.write_byte_impl(register, value).is_some() {
                tracing::debug!(?target, register, value, "EC yazma");
                return Ok(());
            }
        }
        Err(EcError::Handshake(register))
    }

    /// 0x00-0xFF aralığının tam dökümü (C# `-Ec` dump muadili; tanılama için).
    pub fn dump(&self) -> Vec<Option<u8>> {
        (0..=0xFFu8).map(|r| self.read_byte(r).ok()).collect()
    }
}
