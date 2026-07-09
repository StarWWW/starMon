//! EC protokolü (ACPI handshake, `Hardware/Ec.cs` portu), register haritası
//! (`EcData.cs`) ve MSR tabanlı CPU okuyucuları — hepsi PawnIO üzerinden.
//!
//! Tüm EC erişimi süreçler arası `Global\Access_EC` mutex'i arkasında
//! serileştirilir (PawnIO `LpcACPIEC` modül sözleşmesi ve diğer izleme
//! araçlarıyla birlikte çalışabilirlik için aynı isim).

pub mod cpu;
pub mod ec;
pub mod ec_data;
pub mod mutex;

/// PawnIO.Modules 0.2.9 imzalı blob'ları (LGPL-2.1, bkz. blobs/COPYING).
pub mod blobs {
    pub const LPC_ACPI_EC: &[u8] = include_bytes!("../blobs/LpcACPIEC.bin");
    pub const INTEL_MSR: &[u8] = include_bytes!("../blobs/IntelMSR.bin");
    pub const AMD_FAMILY_17: &[u8] = include_bytes!("../blobs/AMDFamily17.bin");
}

#[derive(Debug, thiserror::Error)]
pub enum EcError {
    #[error("EC mutex'i {0} ms içinde alınamadı")]
    MutexTimeout(u32),
    #[error("EC handshake başarısız (register {0:#04x})")]
    Handshake(u8),
    #[error(transparent)]
    PawnIo(#[from] pawnio_client::PawnIoError),
}
