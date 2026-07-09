//! GUI'siz test edilebilir iş mantığı: platform modeli (Victus capability
//! matrisi), fan programları, thermal guard (95/88°C histerezis), config
//! (TOML + eski `StarMon.xml` importer).
//!
//! P2 ve sonrasında kademeli doldurulacak.

pub mod config;
pub mod fan;
pub mod history;
pub mod snapshot;
