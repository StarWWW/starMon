//! EC protokolü (ACPI handshake, `Hardware/Ec.cs` portu), register haritası
//! (`EcData.cs`) ve MSR/SMN tabanlı CPU okuyucuları.
//!
//! P3'te doldurulacak. Tüm EC erişimi süreçler arası `Global\Access_EC`
//! mutex'i arkasında serileştirilir; AMD SMN için `Global\Access_PCI`.
