//! Sürücü gerektirmeyen metrikler.
//!
//! Tüm okuyucular hataya dayanıklıdır: donanım/sürücü yoksa `None` döner,
//! asla panic olmaz. COM kullanan okuyucular (`battery`) hw sampler
//! thread'inde oluşturulmalıdır.
//!
pub mod battery;
pub mod brightness;
pub mod disk;
pub mod network;
pub mod nvidia;
pub mod system;
