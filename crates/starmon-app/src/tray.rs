//! Tepsi ikonu + menüsü ve dinamik sıcaklık ikonu (C# `GuiTray`/`GuiIcon`).
//!
//! Tepsi, UI thread'inde (winit event loop'u içinde) yaratılır. Menü ve tıklama
//! olayları callback'le gelir; callback'ler pencere gizliyken de çalışsın diye
//! yalnız thread-güvenli şeylere dokunur: `egui::Context` (viewport komutları +
//! repaint) ve `Sender<HwCommand>`.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use crossbeam_channel::Sender;
use tray_icon::menu::{Menu, MenuEvent, MenuItem, PredefinedMenuItem};
use tray_icon::{Icon, MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};

use crate::fan_ctl::HwCommand;

pub struct Tray {
    icon: TrayIcon,
    /// Son çizilen (sıcaklık, sıcak-mod) anahtarı; değişmedikçe ikon yenilenmez.
    last_key: Option<(u8, bool)>,
}

/// Pencereyi gösterip öne getirir (tepsiden veya ikinci örnekten).
fn show_window(ctx: &egui::Context) {
    ctx.send_viewport_cmd(egui::ViewportCommand::Visible(true));
    ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
    ctx.request_repaint();
}

impl Tray {
    pub fn new(
        ctx: egui::Context,
        commands: Sender<HwCommand>,
        allow_exit: Arc<AtomicBool>,
    ) -> anyhow::Result<Self> {
        let menu = Menu::new();
        let show_item = MenuItem::new("Göster / Gizle", true, None);
        let auto_item = MenuItem::new("Fan: Otomatik", true, None);
        let max_item = MenuItem::new("Fan: Maksimum", true, None);
        let exit_item = MenuItem::new("Çıkış", true, None);
        menu.append_items(&[
            &show_item,
            &PredefinedMenuItem::separator(),
            &auto_item,
            &max_item,
            &PredefinedMenuItem::separator(),
            &exit_item,
        ])?;

        let (show_id, auto_id, max_id, exit_id) = (
            show_item.id().clone(),
            auto_item.id().clone(),
            max_item.id().clone(),
            exit_item.id().clone(),
        );
        {
            let ctx = ctx.clone();
            let commands = commands.clone();
            MenuEvent::set_event_handler(Some(move |event: MenuEvent| {
                let id = event.id();
                if *id == show_id {
                    show_window(&ctx);
                } else if *id == auto_id {
                    let _ = commands.send(HwCommand::FanAuto);
                } else if *id == max_id {
                    let _ = commands.send(HwCommand::FanMax(true));
                } else if *id == exit_id {
                    allow_exit.store(true, Ordering::Relaxed);
                    // Görünür yap ki bir frame koşsun ve kapanış işlensin
                    show_window(&ctx);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            }));
        }
        {
            let ctx = ctx.clone();
            TrayIconEvent::set_event_handler(Some(move |event: TrayIconEvent| {
                if let TrayIconEvent::Click {
                    button: MouseButton::Left,
                    button_state: MouseButtonState::Up,
                    ..
                } = event
                {
                    show_window(&ctx);
                }
            }));
        }

        let icon = TrayIconBuilder::new()
            .with_menu(Box::new(menu))
            .with_tooltip("StarMon")
            .with_icon(render_icon(None, false))
            .build()?;
        Ok(Self { icon, last_key: None })
    }

    /// Sıcaklık değiştiyse tepsi ikonunu yeniden çizer (3 sn kadansla çağrılır).
    pub fn update(&mut self, temp_c: Option<u8>, warm: bool) {
        let key = (temp_c.unwrap_or(0), warm);
        if self.last_key == Some(key) {
            return;
        }
        self.last_key = Some(key);
        let _ = self.icon.set_icon(Some(render_icon(temp_c, warm)));
        if let Some(t) = temp_c {
            let _ = self.icon.set_tooltip(Some(format!("StarMon — {t}°C")));
        }
    }
}

// ---- İkon çizimi: 32×32 RGBA, iki basamak, el yapımı 3×5 piksel font ----

const ICON_SIZE: usize = 32;
/// 3×5 rakam bitmap'leri; her satır 3 bit (MSB solda).
const DIGITS: [[u8; 5]; 10] = [
    [0b111, 0b101, 0b101, 0b101, 0b111], // 0
    [0b010, 0b110, 0b010, 0b010, 0b111], // 1
    [0b111, 0b001, 0b111, 0b100, 0b111], // 2
    [0b111, 0b001, 0b111, 0b001, 0b111], // 3
    [0b101, 0b101, 0b111, 0b001, 0b001], // 4
    [0b111, 0b100, 0b111, 0b001, 0b111], // 5
    [0b111, 0b100, 0b111, 0b101, 0b111], // 6
    [0b111, 0b001, 0b001, 0b010, 0b010], // 7
    [0b111, 0b101, 0b111, 0b101, 0b111], // 8
    [0b111, 0b101, 0b111, 0b001, 0b111], // 9
];

fn render_icon(temp_c: Option<u8>, warm: bool) -> Icon {
    // Arka plan: soğuk mavi / (Performance modunda) sıcak turuncu-kırmızı
    let bg: [u8; 4] = if warm { [190, 60, 30, 255] } else { [30, 90, 170, 255] };
    let mut rgba = vec![0u8; ICON_SIZE * ICON_SIZE * 4];
    for y in 0..ICON_SIZE {
        for x in 0..ICON_SIZE {
            // Kaba yuvarlatılmış köşe: köşe 3×3 üçgenlerini şeffaf bırak
            let (cx, cy) = (
                x.min(ICON_SIZE - 1 - x),
                y.min(ICON_SIZE - 1 - y),
            );
            let px = &mut rgba[(y * ICON_SIZE + x) * 4..][..4];
            if cx + cy >= 3 {
                px.copy_from_slice(&bg);
            }
        }
    }
    let Some(t) = temp_c else {
        return Icon::from_rgba(rgba, ICON_SIZE as u32, ICON_SIZE as u32)
            .expect("ikon tamponu geçerli");
    };
    let t = t.min(99);
    // İki basamak, 4× ölçek: her rakam 12×20; toplam 26 px geniş, ortala
    let scale = 4usize;
    let digits = [t / 10, t % 10];
    let total_w = 2 * 3 * scale + 2; // iki rakam + 2 px boşluk
    let x0 = (ICON_SIZE - total_w) / 2;
    let y0 = (ICON_SIZE - 5 * scale) / 2;
    for (di, d) in digits.into_iter().enumerate() {
        if di == 0 && d == 0 {
            continue; // baştaki sıfırı çizme
        }
        let dx0 = x0 + di * (3 * scale + 2);
        let rows = DIGITS[d as usize];
        for (ry, bits) in rows.into_iter().enumerate() {
            for rx in 0..3 {
                if bits & (0b100 >> rx) == 0 {
                    continue;
                }
                for sy in 0..scale {
                    for sx in 0..scale {
                        let (x, y) = (dx0 + rx * scale + sx, y0 + ry * scale + sy);
                        rgba[(y * ICON_SIZE + x) * 4..][..4]
                            .copy_from_slice(&[255, 255, 255, 255]);
                    }
                }
            }
        }
    }
    Icon::from_rgba(rgba, ICON_SIZE as u32, ICON_SIZE as u32).expect("ikon tamponu geçerli")
}
