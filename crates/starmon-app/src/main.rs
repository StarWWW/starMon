#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod fan_ctl;
mod fan_test;
mod hw;
mod logging;
mod single_instance;

use anyhow::Result;
use crossbeam_channel::Sender;
use fan_ctl::HwCommand;
use hp_wmi::data::FanMode;
use starmon_core::fan::{percent_to_level, FanControl};
use starmon_core::snapshot::Snapshot;

const WINDOW_TITLE: &str = "StarMon";

fn main() -> Result<()> {
    let _log_guard = logging::init()?;

    // Smoke testleri için: "--exit-after <saniye>" ile sınırlı süre çalış.
    // Yükseltilmiş süreç dışarıdan öldürülemediğinden kendi kendine kapanır.
    let args: Vec<String> = std::env::args().collect();
    if let Some(i) = args.iter().position(|a| a == "--exit-after") {
        if let Some(secs) = args.get(i + 1).and_then(|s| s.parse::<u64>().ok()) {
            std::thread::spawn(move || {
                std::thread::sleep(std::time::Duration::from_secs(secs));
                tracing::info!("--exit-after {secs} doldu, çıkılıyor");
                std::process::exit(0);
            });
        }
    }

    let Some(_instance) = single_instance::acquire() else {
        single_instance::focus_existing(WINDOW_TITLE);
        return Ok(());
    };

    // P4 aşamalı canlı fan yazma testi: GUI açılmadan koşar ve çıkar.
    if let Some(i) = args.iter().position(|a| a == "--fan-test") {
        return match args.get(i + 1).map(String::as_str) {
            Some("kill") => fan_test::run(true),
            Some("watch") => fan_test::watch(),
            Some("auto") => fan_test::restore_auto(),
            Some("ec") => fan_test::ec_level_test(),
            _ => fan_test::run(false),
        };
    }

    tracing::info!(version = env!("CARGO_PKG_VERSION"), "StarMon başlatılıyor");

    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title(WINDOW_TITLE)
            .with_inner_size([714.0, 590.0])
            .with_min_inner_size([714.0, 590.0]),
        ..Default::default()
    };
    // hw thread'in JoinHandle'ı: kapanışta beklenir ki FanSafetyGuard
    // temizliği (fanı otomatiğe döndürme) süreç ölmeden tamamlansın.
    let join = std::sync::Mutex::new(None);
    eframe::run_native(
        WINDOW_TITLE,
        options,
        Box::new(|cc| {
            cc.egui_ctx.set_theme(egui::Theme::Dark);
            let (hw, hw_join) = hw::spawn(cc.egui_ctx.clone());
            *join.lock().unwrap() = Some(hw_join);
            Ok(Box::new(StarMonApp { hw, manual_percent: 40 }))
        }),
    )
    .map_err(|e| anyhow::anyhow!("eframe başlatılamadı: {e}"))?;

    // Pencere kapandı → uygulama (ve komut kanalı) düştü → hw thread
    // döngüden çıkıp fan temizliğini yapar; burada bitmesini bekleriz.
    if let Some(h) = join.lock().unwrap().take() {
        tracing::info!("hw thread kapanışı bekleniyor (fan temizliği)");
        let _ = h.join();
    }
    tracing::info!("StarMon kapandı");
    Ok(())
}

struct StarMonApp {
    hw: hw::HwHandle,
    /// Sabit fan modu için hedef yüzde (UI durumu).
    manual_percent: u8,
}

impl eframe::App for StarMonApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let snap = self.hw.snapshot.load();
        egui::CentralPanel::default().show(ui, |ui| {
            ui.heading("StarMon");
            if snap.driver_version.is_none() {
                ui.colored_label(
                    egui::Color32::from_rgb(230, 180, 60),
                    "PawnIO kurulu değil — EC sıcaklıkları, fan RPM ve MSR telemetrisi devre dışı (pawnio.eu)",
                );
            }
            ui.add_space(8.0);
            ui.horizontal(|ui| {
                cpu_card(ui, &snap);
                gpu_card(ui, &snap);
                fan_card(ui, &snap);
                battery_card(ui, &snap);
                memory_card(ui, &snap);
            });
            ui.add_space(8.0);
            fan_controls(ui, &snap, &mut self.manual_percent, &self.hw.commands);
            ui.add_space(8.0);
            ui.label(status_line(&snap));
            ui.add_space(8.0);
            core_temps_section(ui, &snap);
            caps_section(ui, &snap);
        });
    }
}

fn stat_card(ui: &mut egui::Ui, title: &str, value: String, sub: String) {
    egui::Frame::group(ui.style())
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.set_min_width(150.0);
            ui.vertical(|ui| {
                ui.label(egui::RichText::new(title).small().weak());
                ui.label(egui::RichText::new(value).size(24.0).strong());
                ui.label(egui::RichText::new(sub).small());
            });
        });
}

fn cpu_card(ui: &mut egui::Ui, s: &Snapshot) {
    // Sıcaklık önceliği: MSR paket > EC CPUT > BIOS termal sensörü.
    let temp = s
        .cpu_msr
        .as_ref()
        .and_then(|m| m.package_temp_c)
        .or_else(|| s.ec.and_then(|e| e.cpu_temp_c.map(u32::from)))
        .or_else(|| s.bios.and_then(|b| b.temperature_c.map(u32::from)));
    let mut sub = Vec::new();
    if let Some(l) = s.cpu_load_percent {
        sub.push(format!("{l:.0}% yük"));
    }
    if let Some(w) = s.cpu_msr.as_ref().and_then(|m| m.package_power_w) {
        sub.push(format!("{w:.0} W"));
    }
    let value = temp.map_or("—".into(), |t| format!("{t}°C"));
    stat_card(ui, "CPU", value, sub.join(" · "));
}

fn fan_card(ui: &mut egui::Ui, s: &Snapshot) {
    // RPM (EC) varsa onu, yoksa BIOS seviyelerini göster.
    if let Some((Some(r1), Some(r2))) = s.ec.map(|e| e.fan_rpm) {
        let mut sub = Vec::new();
        if let (Some(p1), Some(p2)) = s.ec.map(|e| e.fan_percent).unwrap_or_default() {
            sub.push(format!("%{p1} / %{p2}"));
        }
        if s.bios.and_then(|b| b.max_fan) == Some(true) {
            sub.push("MAX".into());
        }
        stat_card(ui, "Fan", format!("{r1} / {r2} rpm"), sub.join(" · "));
        return;
    }
    match &s.bios {
        Some(b) => {
            let value = b
                .fan_level
                .map_or("—".into(), |(c, g)| format!("{c} / {g}"));
            let mut sub = vec!["seviye (CPU/GPU)".to_string()];
            if b.max_fan == Some(true) {
                sub.push("MAX".into());
            }
            stat_card(ui, "Fan", value, sub.join(" · "));
        }
        None => stat_card(ui, "Fan", "—".into(), "BIOS erişimi yok".into()),
    }
}

/// P4 fan kontrol paneli; yalnız EC+BIOS yazma yolu açıkken görünür.
fn fan_controls(ui: &mut egui::Ui, s: &Snapshot, manual_percent: &mut u8, tx: &Sender<HwCommand>) {
    let Some(f) = &s.fan_ctl else { return };
    let send = |cmd: HwCommand| {
        let _ = tx.send(cmd);
    };
    egui::Frame::group(ui.style())
        .inner_margin(egui::Margin::same(10))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.label(egui::RichText::new("Fan kontrolü").strong());
                ui.separator();
                let is_auto = f.control == FanControl::Auto && f.program.is_none();
                let is_manual = matches!(f.control, FanControl::Manual { .. });
                let is_max = f.control == FanControl::Max;
                if ui.selectable_label(is_auto, "Otomatik").clicked() && !is_auto {
                    if f.program.is_some() {
                        send(HwCommand::FanProgram(false));
                    }
                    send(HwCommand::FanAuto);
                }
                if ui.selectable_label(is_manual, "Sabit").clicked() && !is_manual {
                    let level = percent_to_level(*manual_percent);
                    send(HwCommand::FanManual { cpu: level, gpu: level });
                }
                if ui.selectable_label(is_max, "Maks").clicked() && !is_max {
                    send(HwCommand::FanMax(true));
                }
                ui.separator();
                let slider = ui.add(
                    egui::Slider::new(manual_percent, 20..=100)
                        .suffix("%")
                        .show_value(true),
                );
                // Sabit moddayken sürükleme bitince yeni seviyeyi uygula
                if is_manual && slider.drag_stopped() {
                    let level = percent_to_level(*manual_percent);
                    send(HwCommand::FanManual { cpu: level, gpu: level });
                }
            });
            ui.horizontal(|ui| {
                ui.label("Mod:");
                let current = f.sticky_mode;
                let text = current.map_or("(sistem)".to_string(), |m| format!("{m:?}"));
                egui::ComboBox::from_id_salt("fan_mode")
                    .selected_text(text)
                    .show_ui(ui, |ui| {
                        let options = [
                            ("(sistem)", None),
                            ("Default", Some(FanMode::Default)),
                            ("Performance", Some(FanMode::Performance)),
                            ("Cool", Some(FanMode::Cool)),
                            ("Quiet", Some(FanMode::Quiet)),
                        ];
                        for (label, value) in options {
                            if ui.selectable_label(current == value, label).clicked()
                                && current != value
                            {
                                send(HwCommand::FanMode(value));
                            }
                        }
                    });
                let mut program_on = f.program.is_some();
                if ui.checkbox(&mut program_on, "Fan programı").changed() {
                    send(HwCommand::FanProgram(program_on));
                }
                if let Some(m) = f.mode {
                    ui.label(format!("EC modu: {m:?}"));
                }
                if let Some(cd) = f.countdown {
                    if cd > 0 {
                        ui.label(format!("failsafe sayacı: {cd}s")).on_hover_text(
                            "Yenilenmezse manuel seviye bırakılıp otomatiğe dönülür \
                             (uygulama yazar; bu modelde EC kendisi geri almıyor)",
                        );
                    }
                }
                if f.guard_active {
                    ui.colored_label(
                        egui::Color32::from_rgb(240, 80, 80),
                        format!("TERMAL KORUMA ({}\u{b0}C)", f.max_temp_c),
                    );
                }
            });
        });
}

fn core_temps_section(ui: &mut egui::Ui, s: &Snapshot) {
    let Some(m) = &s.cpu_msr else { return };
    if m.core_temps.is_empty() {
        return;
    }
    egui::CollapsingHeader::new("Çekirdek sıcaklıkları (MSR)").show(ui, |ui| {
        ui.horizontal_wrapped(|ui| {
            for (i, t) in m.core_temps.iter().enumerate() {
                let text = t.map_or(format!("#{i}: —"), |t| format!("#{i}: {t}°C"));
                ui.label(text);
            }
        });
    });
}

fn caps_section(ui: &mut egui::Ui, s: &Snapshot) {
    let Some(caps) = &s.bios_caps else { return };
    egui::CollapsingHeader::new("BIOS yetenekleri").show(ui, |ui| {
        let mut line = |k: &str, v: String| {
            ui.label(format!("{k}: {v}"));
        };
        if let Some(sys) = &caps.system {
            line("Termal politika", format!("V{}", sys.thermal_policy));
            line(
                "Yazılımla fan kontrolü",
                if sys.supports_sw_fan_ctl() { "var" } else { "yok" }.into(),
            );
            line("Varsayılan PL4", format!("{} W", sys.default_cpu_power_limit4));
            line("Durum bayrakları", format!("{:#06x}", sys.status_flags.get()));
        }
        if let Some(d) = &caps.born_date {
            line("Üretim tarihi (BOD)", d.clone());
        }
        if let (Some(n), Some(t)) = (caps.fan_count, caps.fan_type) {
            line("Fan", format!("{n} adet · tip {:#04x}", t));
        }
        if let Some(ft) = &caps.fan_table {
            line(
                "Fan tablosu",
                format!("{} fan · {} seviye", ft.fan_count, ft.level_count),
            );
        }
        if let Some(g) = caps.gpu_mode {
            line("GPU modu", format!("{:?}", hp_wmi::data::GpuMode::from_u8(g)));
        }
        if let Some(gp) = &caps.gpu_power {
            line(
                "GPU güç",
                format!(
                    "cTGP {} · PPAB {} · tepe {}°C",
                    gp.custom_tgp, gp.ppab, gp.peak_temperature
                ),
            );
        }
        if let Some(k) = caps.kbd_type {
            line("Klavye tipi", format!("{k:#04x}"));
        }
        if let Some(b) = caps.has_backlight {
            line("Klavye aydınlatması", if b { "destekleniyor" } else { "yok" }.into());
        }
    });
}

fn memory_card(ui: &mut egui::Ui, s: &Snapshot) {
    match s.memory {
        Some(m) => stat_card(
            ui,
            "Bellek",
            format!("{}%", m.load_percent),
            format!("{:.1} / {:.1} GB", m.used_mb as f32 / 1024.0, m.total_mb as f32 / 1024.0),
        ),
        None => stat_card(ui, "Bellek", "—".into(), String::new()),
    }
}

fn gpu_card(ui: &mut egui::Ui, s: &Snapshot) {
    match &s.gpu {
        Some(g) => {
            let value = g.temp_c.map_or("—".into(), |t| format!("{t}°C"));
            let mut sub = Vec::new();
            if let Some(l) = g.load_percent {
                sub.push(format!("{l}% yük"));
            }
            if let Some(p) = g.power_w {
                sub.push(format!("{p:.0} W"));
            }
            if let (Some(u), Some(t)) = (g.vram_used_mb, g.vram_total_mb) {
                sub.push(format!("{:.1}/{:.1} GB VRAM", u as f32 / 1024.0, t as f32 / 1024.0));
            }
            stat_card(ui, "GPU", value, sub.join(" · "));
        }
        None => stat_card(ui, "GPU", "—".into(), "NVML yok".into()),
    }
}

fn battery_card(ui: &mut egui::Ui, s: &Snapshot) {
    match &s.battery {
        Some(b) => {
            let value = b.percent.map_or("—".into(), |p| format!("{p}%"));
            let mut sub = vec![if b.on_ac { "AC" } else { "pil" }.to_string()];
            if let Some(w) = b.rate_watts {
                sub.push(format!("{w:+.1} W"));
            }
            if let Some(h) = b.health_percent {
                sub.push(format!("sağlık {h}%"));
            }
            stat_card(ui, "Batarya", value, sub.join(" · "));
        }
        None => stat_card(ui, "Batarya", "—".into(), "batarya yok".into()),
    }
}

fn status_line(s: &Snapshot) -> String {
    let net = s.network.map_or("— / —".into(), |n| {
        format!(
            "↓ {} · ↑ {}",
            fmt_rate(n.rx_bytes_per_sec),
            fmt_rate(n.tx_bytes_per_sec)
        )
    });
    let mut parts = vec![format!("Ağ {net}")];
    if let Some(d) = &s.disk {
        let mut disk = Vec::new();
        if let Some(t) = d.temp_c {
            disk.push(format!("{t}°C"));
        }
        if let (Some(r), Some(w)) = (d.read_bytes_per_sec, d.write_bytes_per_sec) {
            disk.push(format!("O {} · Y {}", fmt_rate(r), fmt_rate(w)));
        }
        if !disk.is_empty() {
            parts.push(format!("Disk {}", disk.join(" · ")));
        }
    }
    if let Some(b) = s.brightness_percent {
        parts.push(format!("Parlaklık {b}%"));
    }
    parts.push(format!("Açık kalma {}", fmt_duration(s.uptime_secs)));
    parts.join("   ·   ")
}

fn fmt_rate(bytes_per_sec: u64) -> String {
    match bytes_per_sec {
        b if b >= 1024 * 1024 => format!("{:.1} MB/s", b as f32 / (1024.0 * 1024.0)),
        b if b >= 1024 => format!("{:.0} KB/s", b as f32 / 1024.0),
        b => format!("{b} B/s"),
    }
}

fn fmt_duration(secs: u64) -> String {
    let (d, h, m) = (secs / 86400, (secs % 86400) / 3600, (secs % 3600) / 60);
    if d > 0 {
        format!("{d}g {h}s {m}d")
    } else {
        format!("{h}s {m}d")
    }
}
