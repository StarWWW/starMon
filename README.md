# starmon-rs
## (Yes i used claude bcz RUST is hard.)
[StarMon](https://github.com/StarWWW/StarMon)'un (HP Omen/Victus donanım izleme
ve fan/klavye kontrol aracı) Rust portu. GUI: egui/eframe · Sürücü: PawnIO ·
Lisans: GPL-3.0.

## Workspace

| Crate | Rol |
|---|---|
| `pawnio-client` | PawnIO sürücüsü için ham DeviceIoControl istemcisi |
| `starmon-hw` | EC protokolü, register haritası, MSR/SMN okuyucular |
| `hp-wmi` | HP `hpqBIntM` WMI BIOS köprüsü + packed struct'lar |
| `starmon-metrics` | Sürücüsüz metrikler (system/battery/net/disk/NVIDIA) |
| `starmon-core` | Fan engine, thermal guard, platform modeli, config |
| `starmon-app` | eframe GUI + tray + donanım sampler thread'i |

## Build

```
cargo build --release            # UAC yükseltmeli (son kullanıcı)
cargo build --no-default-features -p starmon-app   # yükseltmesiz (geliştirme)
```

Port fazları ve mimari kararlar için plan dosyasına bakın
(P0 iskelet → P1 sürücüsüz metrikler → P2 WMI BIOS → P3 PawnIO/EC →
P4 fan kontrolü → P5 dashboard → P6 RGB/CLI → P7 paketleme).
