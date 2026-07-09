//! Donanım örnekleme thread'i: tüm donanım handle'larının tek sahibi.
//! 1 saniyelik drift'siz master tick; C# `GuiTray.cs` timer semantiğinin
//! karşılığı. UI, `ArcSwap<Snapshot>` üzerinden kilitsiz okur ve
//! `HwCommand` kanalıyla komut gönderir; EC/BIOS'a yalnız bu thread yazar.

use std::sync::{Arc, RwLock};
use std::time::{Duration, Instant};

use arc_swap::ArcSwap;
use crossbeam_channel::{Receiver, RecvTimeoutError, Sender};
use hp_wmi::HpWmiBios;
use starmon_core::fan::{GUARD_TICK_SECS, PROGRAM_TICK_SECS};
use starmon_core::history::{History, HistorySample};
use starmon_core::snapshot::{BiosSnapshot, CpuMsrSnapshot, EcSnapshot, Snapshot};
use starmon_hw::cpu::CpuMsr;
use starmon_hw::ec::EmbeddedController;
use starmon_hw::ec_data::reg;
use starmon_metrics::battery::BatteryReader;
use starmon_metrics::brightness::BrightnessReader;
use starmon_metrics::disk::DiskSampler;
use starmon_metrics::network::NetworkSampler;
use starmon_metrics::nvidia::GpuReader;
use starmon_metrics::system::{self, CpuLoadSampler};

use crate::fan_ctl::{FanController, HwCommand, HwDevs};

pub struct HwHandle {
    pub snapshot: Arc<ArcSwap<Snapshot>>,
    pub commands: Sender<HwCommand>,
    /// Grafikler için zaman serisi; hw thread yazar, UI okur.
    pub history: Arc<RwLock<History>>,
}

pub fn spawn(
    ctx: egui::Context,
    cfg: starmon_core::config::Config,
) -> (HwHandle, std::thread::JoinHandle<()>) {
    let snapshot = Arc::new(ArcSwap::from_pointee(Snapshot::default()));
    let history = Arc::new(RwLock::new(History::default()));
    let (tx, rx) = crossbeam_channel::unbounded();
    let snap = snapshot.clone();
    let hist = history.clone();
    let join = std::thread::Builder::new()
        .name("hw-sampler".into())
        .spawn(move || {
            // Dış emniyet kemeri: okuyucu kurulumunda panik olursa log'la.
            // Döngü panikleri run() içindeki iç catch_unwind'de yakalanır ve
            // fan temizliği (restore) orada her koşulda çalışır.
            if let Err(e) = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                run(ctx, cfg, snap, hist, rx)
            })) {
                tracing::error!("hw thread panikledi: {e:?}");
            }
        })
        .expect("hw thread başlatılamadı");
    (HwHandle { snapshot, commands: tx, history }, join)
}

fn run(
    ctx: egui::Context,
    cfg: starmon_core::config::Config,
    snapshot: Arc<ArcSwap<Snapshot>>,
    history: Arc<RwLock<History>>,
    rx: Receiver<HwCommand>,
) {
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

    // PawnIO katmanı: kurulu değilse driverless modda devam (P1+P2 tam çalışır).
    state.driver_version = pawnio_client::installed_version();
    let (ec, msr) = if state.driver_version.is_some() {
        let ec = EmbeddedController::new()
            .map_err(|e| tracing::warn!("EC başlatılamadı: {e}"))
            .ok();
        (ec, CpuMsr::new())
    } else {
        tracing::info!("PawnIO kurulu değil — EC/MSR katmanı devre dışı (driverless mod)");
        (None, None)
    };

    let devs = HwDevs { ec: ec.as_ref(), bios: bios.as_ref() };
    // Yazma yolu: seviye/mod BIOS'tan, failsafe geri sayımı EC'den gider;
    // ikisi de yoksa fan kontrolü kapalı kalır (UI kontrolleri gizler).
    let fan_write_ok = devs.bios.is_some() && devs.ec.is_some();
    let mut ctl = FanController::new(&cfg);
    let mut last_hpcm: Option<u8> = None;
    let mut last_xfcd: Option<u8> = None;

    let mut next_tick = Instant::now() + Duration::from_secs(1);
    // İç emniyet kemeri: döngü ne şekilde biterse bitsin (normal kapanış
    // veya panik) çıkışta fan otomatik kontrole döndürülür.
    let loop_result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| loop {
        match rx.recv_deadline(next_tick) {
            Ok(cmd) => {
                if !fan_write_ok {
                    tracing::warn!(?cmd, "fan komutu yok sayıldı: EC/BIOS yazma yolu kapalı");
                    continue;
                }
                ctl.handle(cmd, devs, last_hpcm);
                // Komut sonucunu tick beklemeden UI'a yansıt
                state.fan_ctl = Some(ctl.snapshot(last_hpcm, last_xfcd));
                snapshot.store(Arc::new(state.clone()));
                ctx.request_repaint();
            }
            Err(RecvTimeoutError::Timeout) => {
                // Uyku/uyanma tespiti: uykuda Instant ilerlediği için deadline
                // çok geride kalır. Yeniden hizala (tick fırtınasını önler) ve
                // EC'de uçmuş olabilecek kullanıcı durumunu yeniden uygula.
                let now = Instant::now();
                if now > next_tick + Duration::from_secs(5) {
                    tracing::info!(
                        gecikme_s = (now - next_tick).as_secs(),
                        "uyku/duraklama dönüşü algılandı; tick hizalanıyor"
                    );
                    next_tick = now + Duration::from_secs(1);
                    if fan_write_ok {
                        ctl.on_resume(devs, last_hpcm);
                    }
                } else {
                    next_tick += Duration::from_secs(1);
                }
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
                if state.tick % GUARD_TICK_SECS == 1 {
                    state.battery = battery.sample();
                    state.gpu = gpu.sample();
                    state.brightness_percent = brightness.sample();
                    d.temp_c = disk.sample_temp();
                    state.bios = bios.as_ref().map(|b| BiosSnapshot {
                        fan_level: b.get_fan_level().ok(),
                        temperature_c: b.get_temperature().ok(),
                        max_fan: b.get_max_fan().ok(),
                    });
                    state.ec = ec.as_ref().map(|e| EcSnapshot {
                        cpu_temp_c: e.read_byte(reg::CPUT).ok(),
                        gpu_temp_c: e.read_byte(reg::GPTM).ok(),
                        fan_rpm: (e.read_word(reg::RPM1).ok(), e.read_word(reg::RPM3).ok()),
                        fan_percent: (e.read_byte(reg::XGS1).ok(), e.read_byte(reg::XGS2).ok()),
                    });
                    state.cpu_msr = msr.as_ref().map(|m| CpuMsrSnapshot {
                        package_temp_c: m.package_temp(),
                        package_power_w: m.package_power(),
                        core_temps: m.core_temps(),
                    });

                    if fan_write_ok {
                        last_hpcm = ec.as_ref().and_then(|e| e.read_byte(reg::HPCM).ok());
                        last_xfcd = ec.as_ref().and_then(|e| e.read_byte(reg::XFCD).ok());
                        // Koruma sıcaklığı: kullanılan sensör kümesinin maksimumu
                        // (C# varsayılanı CPUT/GPTM + MSR; RTMP/TMP1 vb. P5'te
                        // yapılandırılabilir kümeye eklenecek)
                        let max_temp = [
                            state.ec.and_then(|e| e.cpu_temp_c),
                            state.ec.and_then(|e| e.gpu_temp_c),
                            state
                                .cpu_msr
                                .as_ref()
                                .and_then(|m| m.package_temp_c)
                                .map(|t| t.min(255) as u8),
                        ]
                        .into_iter()
                        .flatten()
                        .max()
                        .unwrap_or(0);
                        ctl.guard_tick(devs, max_temp, last_hpcm);
                        // Fan programı / geri sayım yenileme: 15 saniyede bir
                        // (15, 3'ün katı olduğundan taze okumalarla çakışır)
                        if state.tick % PROGRAM_TICK_SECS == 1 {
                            ctl.program_tick(devs, max_temp, last_xfcd, last_hpcm);
                            // Program seviye yazdıysa geri sayım değişmiştir; tazele
                            last_xfcd =
                                ec.as_ref().and_then(|e| e.read_byte(reg::XFCD).ok());
                        }
                        state.fan_ctl = Some(ctl.snapshot(last_hpcm, last_xfcd));
                    }

                    // Geçmişe örnek it (grafikler); kilit yalnız burada yazılır
                    if let Ok(mut h) = history.write() {
                        h.push(HistorySample {
                            tick: state.tick,
                            cpu_temp_c: best_cpu_temp(&state),
                            gpu_temp_c: state
                                .ec
                                .and_then(|e| e.gpu_temp_c)
                                .or(state.gpu.and_then(|g| g.temp_c.map(|t| t as u8))),
                            cpu_load_percent: state.cpu_load_percent,
                            cpu_power_w: state.cpu_msr.as_ref().and_then(|m| m.package_power_w),
                            fan_rpm: state.ec.map(|e| e.fan_rpm).unwrap_or_default(),
                            memory_load_percent: state.memory.map(|m| m.load_percent),
                        });
                    }
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
                    ec_cpu = ?state.ec.and_then(|e| e.cpu_temp_c),
                    ec_gpu = ?state.ec.and_then(|e| e.gpu_temp_c),
                    ec_rpm = ?state.ec.map(|e| e.fan_rpm),
                    msr_pkg = ?state.cpu_msr.as_ref().and_then(|m| m.package_temp_c),
                    msr_w = ?state.cpu_msr.as_ref().and_then(|m| m.package_power_w),
                    fan_ctl = ?state.fan_ctl.as_ref().map(|f| (f.control, f.countdown, f.guard_active)),
                    disk_temp = ?state.disk.and_then(|d| d.temp_c),
                    parlaklik = ?state.brightness_percent,
                    "örnekleme"
                );
            }
            Err(RecvTimeoutError::Disconnected) => break,
        }
    }));

    // FanSafetyGuard: normal kapanışta da panik sonrasında da fanı
    // otomatiğe döndür. Bu Victus'ta XFCD donanımsal failsafe'i çalışmadığı
    // için (canlı test 2026-07-10) sert kill sonrası fan manuel kalır;
    // tek donanım güvencesi EC firmware'inin kendi kritik termal eşiğidir.
    ctl.restore(devs);
    if let Err(e) = loop_result {
        tracing::error!("örnekleme döngüsü panikledi: {e:?}");
    }
}

/// CPU sıcaklığı önceliği: MSR paket > EC CPUT > BIOS sensörü (UI ile aynı).
fn best_cpu_temp(s: &Snapshot) -> Option<u8> {
    s.cpu_msr
        .as_ref()
        .and_then(|m| m.package_temp_c)
        .map(|t| t.min(255) as u8)
        .or_else(|| s.ec.and_then(|e| e.cpu_temp_c))
        .or_else(|| s.bios.and_then(|b| b.temperature_c))
}
