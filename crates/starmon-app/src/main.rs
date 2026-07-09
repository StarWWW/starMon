#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod logging;
mod single_instance;

use anyhow::Result;

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
            Ok(Box::new(StarMonApp))
        }),
    )
    .map_err(|e| anyhow::anyhow!("eframe başlatılamadı: {e}"))?;

    tracing::info!("StarMon kapandı");
    Ok(())
}

struct StarMonApp;

impl eframe::App for StarMonApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        egui::CentralPanel::default().show(ui, |ui| {
            ui.heading("StarMon");
            ui.label("P0 iskeleti — donanım katmanı sonraki fazlarda bağlanacak.");
        });
    }
}
