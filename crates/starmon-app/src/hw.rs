//! Donanım örnekleme thread'i: tüm donanım handle'larının tek sahibi.
//! 1 saniyelik drift'siz master tick; C# `GuiTray.cs` timer semantiğinin
//! karşılığı. UI, `ArcSwap<Snapshot>` üzerinden kilitsiz okur ve
//! `HwCommand` kanalıyla komut gönderir (P4'te fan komutları eklenecek).

use std::sync::Arc;
use std::time::{Duration, Instant};

use arc_swap::ArcSwap;
use crossbeam_channel::{Receiver, RecvTimeoutError, Sender};
use hp_wmi::HpWmiBios;
use starmon_core::snapshot::{BiosSnapshot, Snapshot};
use starmon_metrics::battery::BatteryReader;
use starmon_metrics::brightness::BrightnessReader;
use starmon_metrics::disk::DiskSampler;
use starmon_metrics::network::NetworkSampler;
use starmon_metrics::nvidia::GpuReader;
use starmon_metrics::system::{self, CpuLoadSampler};

pub enum HwCommand {
    // P4: SetFanMode, SetFanLevels, ...
}

pub struct HwHandle {
    pub snapshot: Arc<ArcSwap<Snapshot>>,
    #[allow(dead_code)] // P4'te UI'dan fan komutları gönderilecek
    pub commands: Sender<HwCommand>,
}

pub fn spawn(ctx: egui::Context) -> HwHandle {
    let snapshot = Arc::new(ArcSwap::from_pointee(Snapshot::default()));
    let (tx, rx) = crossbeam_channel::unbounded();
    let snap = snapshot.clone();
    std::thread::Builder::new()
        .name("hw-sampler".into())
        .spawn(move || {
            // P4'te FanSafetyGuard bu catch_unwind'in İÇİNDE yaratılacak;
            // panik dahil her çıkışta Drop ile fan auto'ya dönecek.
            if let Err(e) = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                run(ctx, snap, rx)
            })) {
                tracing::error!("hw thread panikledi: {e:?}");
            }
        })
        .expect("hw thread başlatılamadı");
    HwHandle { snapshot, commands: tx }
}

fn run(ctx: egui::Context, snapshot: Arc<ArcSwap<Snapshot>>, rx: Receiver<HwCommand>) {
    let mut cpu = CpuLoadSampler::default();
    let mut net = NetworkSampler::default();
    let mut disk = DiskSampler::default();
    let battery = BatteryReader::new(); // COM bu thread'e bağlı
    let brightness = BrightnessReader::new();
    let gpu = GpuReader::new();
    let bios = match HpWmiBios::new() {
        Ok(b) => Some(b),
        Err(e) => {
            tracing::warn!("HP WMI BIOS erişilemedi (HP dışı cihaz olabilir): {e}");
            None
        }
    };

    let mut state = Snapshot::default();
    if let Some(b) = &bios {
        state.bios_caps = Some(std::sync::Arc::new(b.capabilities()));
        tracing::info!(caps = ?state.bios_caps, "BIOS yetenekleri toplandı");
    }
    let mut next_tick = Instant::now() + Duration::from_secs(1);
    loop {
        match rx.recv_deadline(next_tick) {
            Ok(_cmd) => {
                // P4: komutlar tick beklemeden hemen işlenecek
            }
            Err(RecvTimeoutError::Timeout) => {
                next_tick += Duration::from_secs(1);
                state.tick += 1;
                state.cpu_load_percent = cpu.sample();
                state.memory = system::memory();
                state.network = net.sample();
                state.uptime_secs = system::uptime_secs();
                let (disk_r, disk_w) = disk.sample_activity();
                let mut d = state.disk.unwrap_or_default();
                d.read_bytes_per_sec = disk_r;
                d.write_bytes_per_sec = disk_w;
                // WMI/NVML/NVMe-log maliyetli: 3 saniyede bir (ilk tick dahil)
                if state.tick % 3 == 1 {
                    state.battery = battery.sample();
                    state.gpu = gpu.sample();
                    state.brightness_percent = brightness.sample();
                    d.temp_c = disk.sample_temp();
                    state.bios = bios.as_ref().map(|b| BiosSnapshot {
                        fan_level: b.get_fan_level().ok(),
                        temperature_c: b.get_temperature().ok(),
                        max_fan: b.get_max_fan().ok(),
                    });
                }
                state.disk = Some(d);
                snapshot.store(Arc::new(state.clone()));
                ctx.request_repaint();
                tracing::debug!(
                    tick = state.tick,
                    cpu = ?state.cpu_load_percent,
                    mem = ?state.memory.map(|m| m.load_percent),
                    gpu_temp = ?state.gpu.and_then(|g| g.temp_c),
                    batt = ?state.battery.and_then(|b| b.percent),
                    net_rx = ?state.network.map(|n| n.rx_bytes_per_sec),
                    bios_temp = ?state.bios.and_then(|b| b.temperature_c),
                    bios_fan = ?state.bios.and_then(|b| b.fan_level),
                    disk_temp = ?state.disk.and_then(|d| d.temp_c),
                    disk_r = ?state.disk.and_then(|d| d.read_bytes_per_sec),
                    parlaklik = ?state.brightness_percent,
                    "örnekleme"
                );
            }
            Err(RecvTimeoutError::Disconnected) => break,
        }
    }
}
