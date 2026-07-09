#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod hw;
mod logging;
mod single_instance;

use anyhow::Result;
use starmon_core::snapshot::Snapshot;

const WINDOW_TITLE: &str = "StarMon";

fn main() -> Result<()> {
    let _log_guard = logging::init()?;

    let Some(_instance) = single_instance::acquire() else {
        single_instance::focus_existing(WINDOW_TITLE);
        return Ok(());
    };

    tracing::info!(version = env!("CARGO_PKG_VERSION"), "StarMon başlatılıyor");

    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title(WINDOW_TITLE)
            .with_inner_size([714.0, 590.0])
            .with_min_inner_size([714.0, 590.0]),
        ..Default::default()
    };
    eframe::run_native(
        WINDOW_TITLE,
        options,
        Box::new(|cc| {
            cc.egui_ctx.set_theme(egui::Theme::Dark);
            let hw = hw::spawn(cc.egui_ctx.clone());
            Ok(Box::new(StarMonApp { hw }))
        }),
    )
    .map_err(|e| anyhow::anyhow!("eframe başlatılamadı: {e}"))?;

    tracing::info!("StarMon kapandı");
    Ok(())
}

struct StarMonApp {
    hw: hw::HwHandle,
}

impl eframe::App for StarMonApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let snap = self.hw.snapshot.load();
        egui::CentralPanel::default().show(ui, |ui| {
            ui.heading("StarMon");
            ui.add_space(8.0);
            ui.horizontal(|ui| {
                cpu_card(ui, &snap);
                memory_card(ui, &snap);
                gpu_card(ui, &snap);
                battery_card(ui, &snap);
            });
            ui.add_space(8.0);
            ui.label(status_line(&snap));
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
    let value = s
        .cpu_load_percent
        .map_or("—".into(), |v| format!("{v:.0}%"));
    // P3'te sıcaklık + saat hızı eklenecek (MSR)
    stat_card(ui, "CPU", value, "yük".into());
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
    format!("Ağ {net}   ·   Açık kalma {}", fmt_duration(s.uptime_secs))
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
