//! Fan kontrolünün donanım yürütücüsü: `starmon-core::fan`'daki saf mantığı
//! EC/BIOS yazmalarına çevirir. Tek yazar ilkesi — yalnız hw thread'i çağırır.
//!
//! Güvenlik katmanları (içten dışa):
//! 1. EC yazmaları `EcWritable` allowlist'i ile sınırlı.
//! 2. Termal koruma: 95°C'de maksimum fan, 99°C'de tüm manuel kontrolü bırakma.
//! 3. `restore()` her çıkış yolunda (normal + panik) fanı otomatiğe döndürür.
//! 4. XFCD geri sayımı yine de kurulur/yenilenir; ancak canlı testte
//!    (2026-07-10, Victus) sayaç 0'a inse bile EC'nin fanı KENDİLİĞİNDEN
//!    otomatiğe almadığı görüldü — donanımsal failsafe bu modelde yok.
//!    Bu yüzden sayaç düşerse otomatiğe dönüşü de biz yazarız; sert kill
//!    (taskkill /f) sonrası tek kalan güvence termal koruma değil, EC
//!    firmware'inin kendi kritik eşiği + CPU'nun TjMax kısmasıdır.

use std::time::Instant;

use hp_wmi::data::FanMode;
use hp_wmi::HpWmiBios;
use starmon_core::fan::{
    default_program, FanControl, FanProgram, GuardAction, ThermalGuard, COUNTDOWN_EXTEND_SECS,
    COUNTDOWN_EXTEND_THRESHOLD, FAN_LEVEL_AUTO, MODE_KEEPALIVE_MS, PROGRAM_TICK_SECS,
};
use starmon_core::snapshot::FanCtlSnapshot;
use starmon_hw::ec::EmbeddedController;
use starmon_hw::ec_data::{EcWritable, FAN_MANUAL_OFF};

/// UI'dan hw thread'ine gönderilen komutlar; tick beklemeden işlenir.
#[derive(Clone, Copy, Debug)]
pub enum HwCommand {
    /// Otomatik kontrole dön (tüm manuel durumu temizler).
    FanAuto,
    /// Sabit seviye: (CPU, GPU) [krpm*100].
    FanManual { cpu: u8, gpu: u8 },
    /// BIOS maksimum fan modu.
    FanMax(bool),
    /// Kalıcı (sticky) fan modu; `None` = isteği temizle.
    FanMode(Option<FanMode>),
    /// Yerleşik varsayılan fan programını başlat/durdur.
    FanProgram(bool),
}

/// hw thread'inin sahip olduğu donanım handle'larına kısa erişim.
#[derive(Clone, Copy)]
pub struct HwDevs<'a> {
    pub ec: Option<&'a EmbeddedController>,
    pub bios: Option<&'a HpWmiBios>,
}

pub struct FanController {
    control: FanControl,
    guard: ThermalGuard,
    program: Option<FanProgram>,
    sticky_mode: Option<FanMode>,
    sticky_applied: Instant,
    /// İlk mod değişikliğinden önceki mod; çıkışta geri yüklenir.
    saved_mode: Option<FanMode>,
    /// Donanıma en az bir yazma yapıldı → çıkışta temizlik gerekir.
    dirty: bool,
    /// Maksimum fan isteğini biz açtık (kullanıcı veya koruma).
    max_fan_set: bool,
    mode_dirty: bool,
    last_max_temp: u8,
}

fn log_err<T, E: std::fmt::Display>(what: &str, r: Result<T, E>) -> Option<T> {
    r.map_err(|e| tracing::warn!("{what}: {e}")).ok()
}

impl FanController {
    pub fn new() -> Self {
        Self {
            control: FanControl::Auto,
            guard: ThermalGuard::default(),
            program: None,
            sticky_mode: None,
            sticky_applied: Instant::now(),
            saved_mode: None,
            dirty: false,
            max_fan_set: false,
            mode_dirty: false,
            last_max_temp: 0,
        }
    }

    // ---- Donanım ilkelleri (best-effort; hatalar log'a düşer) ----

    fn set_levels(&mut self, hw: HwDevs, cpu: u8, gpu: u8) {
        // C# varsayılanı FanLevelUseEc=false: BIOS çağrısı tercih edilir
        if let Some(b) = hw.bios {
            log_err("SetFanLevel", b.set_fan_level(cpu, gpu));
            self.dirty = true;
        }
    }

    fn set_countdown(&mut self, hw: HwDevs, secs: u8) {
        if let Some(ec) = hw.ec {
            log_err("XFCD yazma", ec.write_byte(EcWritable::Countdown, secs));
            self.dirty = true;
        }
    }

    fn set_manual_toggle_off(&mut self, hw: HwDevs) {
        if let Some(ec) = hw.ec {
            log_err("OMCC kapatma", ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_OFF));
        }
    }

    fn set_max_fan(&mut self, hw: HwDevs, on: bool) {
        if let Some(b) = hw.bios {
            log_err("SetMaxFan", b.set_max_fan(on));
            self.max_fan_set = on;
            self.dirty = true;
        }
    }

    fn apply_mode(&mut self, hw: HwDevs, mode: FanMode, current_hpcm: Option<u8>) {
        let Some(b) = hw.bios else { return };
        // İlk mod değişikliğinden önceki modu sakla (çıkışta geri yüklenir)
        if !self.mode_dirty {
            self.saved_mode = current_hpcm.and_then(FanMode::from_u8);
            self.mode_dirty = true;
        }
        log_err("SetFanMode", b.set_fan_mode(mode));
        self.sticky_applied = Instant::now();
        self.dirty = true;
    }

    // ---- Komut işleme ----

    pub fn handle(&mut self, cmd: HwCommand, hw: HwDevs, current_hpcm: Option<u8>) {
        tracing::info!(?cmd, "fan komutu");
        match cmd {
            HwCommand::FanAuto => self.to_auto(hw),
            HwCommand::FanManual { cpu, gpu } => {
                self.program = None;
                if self.control == FanControl::Max {
                    self.set_max_fan(hw, false);
                }
                self.control = FanControl::Manual { cpu, gpu };
                self.set_levels(hw, cpu, gpu);
                // Failsafe'i garantiye al: donanım geri sayımı başlatmadıysa biz kur
                self.set_countdown(hw, COUNTDOWN_EXTEND_SECS);
            }
            HwCommand::FanMax(true) => {
                self.program = None;
                self.control = FanControl::Max;
                self.set_max_fan(hw, true);
            }
            HwCommand::FanMax(false) => self.to_auto(hw),
            HwCommand::FanMode(Some(mode)) => {
                self.sticky_mode = Some(mode);
                self.apply_mode(hw, mode, current_hpcm);
            }
            HwCommand::FanMode(None) => self.sticky_mode = None,
            HwCommand::FanProgram(true) => {
                if self.control == FanControl::Max {
                    self.set_max_fan(hw, false);
                }
                self.control = FanControl::Auto;
                self.program = Some(default_program());
                // İlk adımı beklemeden uygula (henüz sıcaklık okuması yoksa
                // ilk program tick'ine bırak — 0°C en düşük seviyeye kenetlenirdi)
                if self.last_max_temp > 0 {
                    self.program_tick(hw, self.last_max_temp, None, current_hpcm);
                }
            }
            HwCommand::FanProgram(false) => {
                if self.program.take().is_some() {
                    // C# Terminate: seviyeler auto, manuel kapalı, mod geri
                    self.set_levels(hw, FAN_LEVEL_AUTO, FAN_LEVEL_AUTO);
                    self.set_manual_toggle_off(hw);
                    self.set_countdown(hw, 0);
                    if let Some(m) = self.sticky_mode.or(self.saved_mode) {
                        self.apply_mode(hw, m, current_hpcm);
                    }
                }
            }
        }
    }

    fn to_auto(&mut self, hw: HwDevs) {
        self.program = None;
        if self.max_fan_set {
            self.set_max_fan(hw, false);
        }
        self.control = FanControl::Auto;
        self.set_levels(hw, FAN_LEVEL_AUTO, FAN_LEVEL_AUTO);
        self.set_manual_toggle_off(hw);
        self.set_countdown(hw, 0);
    }

    // ---- Periyodik tick'ler ----

    /// 3 saniyelik koruma tick'i (C# `CheckThermalGuard` + sticky mod bakımı).
    /// `max_temp` = sensör kümesinin maksimumu (EC CPUT/GPTM + MSR paket).
    pub fn guard_tick(&mut self, hw: HwDevs, max_temp: u8, current_hpcm: Option<u8>) {
        self.last_max_temp = max_temp;
        match self.guard.step(max_temp) {
            Some(GuardAction::EngageMax) => {
                tracing::warn!(max_temp, "termal koruma: fanlar maksimuma alındı");
                self.set_max_fan(hw, true);
            }
            Some(GuardAction::Panic) => {
                tracing::error!(
                    max_temp,
                    "acil termal koruma: tüm manuel kontrol donanıma bırakıldı"
                );
                self.program = None;
                self.sticky_mode = None;
                self.control = FanControl::Auto;
                self.set_levels(hw, FAN_LEVEL_AUTO, FAN_LEVEL_AUTO);
                self.set_manual_toggle_off(hw);
                self.set_countdown(hw, 0);
                self.set_max_fan(hw, true);
            }
            Some(GuardAction::Release) => {
                tracing::info!(max_temp, "termal koruma bırakıldı");
                // Kullanıcı Max istemediyse maksimumu kapat
                if self.control != FanControl::Max {
                    self.set_max_fan(hw, false);
                }
            }
            None => {}
        }

        // Sticky mod bakımı: koruma pasifken, program yokken, 5 sn'de bir
        // veya EC'deki mod istekten saptıysa yeniden uygula
        if !self.guard.active && self.program.is_none() && self.control != FanControl::Max {
            if let Some(mode) = self.sticky_mode {
                let drifted = current_hpcm.is_some_and(|h| h != mode as u8);
                if drifted || self.sticky_applied.elapsed().as_millis() as u64 >= MODE_KEEPALIVE_MS
                {
                    self.apply_mode(hw, mode, current_hpcm);
                }
            }
        }
    }

    /// 15 saniyelik program tick'i (C# `FanProgram.Update` + `UpdateCountdown`).
    pub fn program_tick(
        &mut self,
        hw: HwDevs,
        max_temp: u8,
        current_countdown: Option<u8>,
        current_hpcm: Option<u8>,
    ) {
        if let Some(program) = self.program.clone() {
            let Some((cpu, gpu)) = program.levels_for(max_temp) else { return };
            tracing::debug!(max_temp, cpu, gpu, program = %program.name, "program adımı");
            self.set_levels(hw, cpu, gpu);
            // Mod istenen durumdan saptıysa düzelt (mod yazmak geri sayımı sıfırlar)
            if current_hpcm.is_some_and(|h| h != program.fan_mode as u8) {
                self.apply_mode(hw, program.fan_mode, current_hpcm);
            }
            self.set_countdown(hw, COUNTDOWN_EXTEND_SECS);
        } else if matches!(self.control, FanControl::Manual { .. }) {
            // Manuel seviye canlı tutma: yalnız güvenliyken; şüphede EC failsafe kazanır
            match current_countdown {
                Some(0) => {
                    // Sayaç doldu (yenilemeyi güvensiz koşullar nedeniyle
                    // atlamışız). Bu donanım kendisi otomatiğe dönmediği için
                    // dönüşü biz yazarız; max-fan'a dokunmayız (koruma yönetir).
                    tracing::info!("XFCD 0 — manuel seviye bırakılıyor, otomatik yazılıyor");
                    self.control = FanControl::Auto;
                    self.set_levels(hw, FAN_LEVEL_AUTO, FAN_LEVEL_AUTO);
                    self.set_manual_toggle_off(hw);
                }
                Some(cd)
                    if self.guard.safe_to_extend(max_temp)
                        && u64::from(cd)
                            < PROGRAM_TICK_SECS + u64::from(COUNTDOWN_EXTEND_THRESHOLD) =>
                {
                    self.set_countdown(hw, COUNTDOWN_EXTEND_SECS);
                }
                _ => {}
            }
        }
    }

    // ---- Çıkış temizliği ----

    /// Her çıkış yolunda çağrılır (normal kapanış + panik sonrası).
    /// Fanı EC'nin otomatik yönetimine döndürür; en kötü durumda bile
    /// XFCD geri sayımı donanımsal failsafe olarak kalır.
    pub fn restore(&mut self, hw: HwDevs) {
        if !self.dirty {
            return;
        }
        tracing::info!("fan güvenlik temizliği: otomatik kontrole dönülüyor");
        if let Some(b) = hw.bios {
            log_err("SetFanLevel(auto)", b.set_fan_level(FAN_LEVEL_AUTO, FAN_LEVEL_AUTO));
            if self.max_fan_set {
                log_err("SetMaxFan(false)", b.set_max_fan(false));
            }
            if self.mode_dirty {
                log_err(
                    "SetFanMode(geri yükleme)",
                    b.set_fan_mode(self.saved_mode.unwrap_or(FanMode::Default)),
                );
            }
        }
        if let Some(ec) = hw.ec {
            log_err("OMCC kapatma", ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_OFF));
            log_err("XFCD sıfırlama", ec.write_byte(EcWritable::Countdown, 0));
        }
        self.dirty = false;
    }

    pub fn snapshot(&self, hpcm: Option<u8>, countdown: Option<u8>) -> FanCtlSnapshot {
        FanCtlSnapshot {
            control: self.control,
            mode: hpcm.and_then(FanMode::from_u8),
            sticky_mode: self.sticky_mode,
            countdown,
            guard_active: self.guard.active,
            program: self.program.as_ref().map(|p| p.name.clone()),
            max_temp_c: self.last_max_temp,
        }
    }
}
