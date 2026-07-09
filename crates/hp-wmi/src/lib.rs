//! HP WMI BIOS köprüsü: `root\wmi` → `hpqBIntM.hpqBIOSInt{0,4,128}` method
//! çağrıları ve BIOS veri yapıları (C# `Bios.cs` + `BiosData.cs` portu).
//!
//! Çağrı düzeni: `hpqBDataIn` örneği (`Sign` paylaşılan sırrı, `Command`,
//! `CommandType`, `Size`, `hpqBData` byte dizisi) → çıkış boyutuna göre
//! seçilen method → `OutData.rwReturnCode` + `Data`.

mod bios;
pub mod data;
mod variant;

pub use bios::{Capabilities, Cmd, HpWmiBios};

#[derive(Debug, thiserror::Error)]
pub enum HpWmiError {
    #[error("COM/WMI hatası: {0}")]
    Com(#[from] windows::core::Error),
    #[error("BIOS dönüş kodu: {0}")]
    ReturnCode(i32),
    #[error("beklenen veri alınamadı")]
    NoData,
    #[error("geçersiz çıkış boyutu: {0}")]
    BadOutSize(usize),
}
