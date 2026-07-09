//! HP WMI BIOS köprüsü: `root\wmi` → `hpqBIntM.hpqBIOSInt{0,4,128,1024,4096}`
//! method çağrıları ve 128-byte packed veri yapıları (`Hardware/BiosData.cs` portu).
//!
//! P2'de doldurulacak: raw COM (windows crate) ile method invoke,
//! zerocopy türetmeli struct'lar + byte-eşdeğerlik testleri.
