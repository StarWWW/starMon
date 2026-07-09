//! PawnIO sürücüsü için ham DeviceIoControl istemcisi.
//!
//! P3'te doldurulacak: `is_installed()` (registry tespiti), `open_and_load(blob)`
//! (`\\?\GLOBALROOT\Device\PawnIO` + IOCTL_PIO_LOAD_BINARY), `execute(name, &[u64], out_len)`
//! (32 byte ASCII fonksiyon adı + u64 girdi dizisi).
