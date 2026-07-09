//! Sürücü gerektirmeyen metrikler.
//!
//! Tüm okuyucular hataya dayanıklıdır: donanım/sürücü yoksa `None` döner,
//! asla panic olmaz. COM kullanan okuyucular (`battery`) hw sampler
//! thread'inde oluşturulmalıdır.
//!
//! P1 devamında eklenecek: `disk` (NVMe sıcaklık + IOCTL_DISK_PERFORMANCE),
//! `brightness` (WmiMonitorBrightness).

pub mod battery;
pub mod network;
pub mod nvidia;
pub mod system;
