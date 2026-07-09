//! Fan kontrol mantığının donanımsız (saf) kısmı: fan programı eşik
//! araması, termal koruma histerezisi ve zamanlama sabitleri.
//! C# karşılıkları: `FanProgram.cs`, `GuiTray.CheckThermalGuard`,
//! `ConfigData.cs` varsayılanları. Donanıma dokunan kısım
//! `starmon-app::fan_ctl`'de; buradaki mantık birim testleriyle doğrulanır.

use std::collections::BTreeMap;

use hp_wmi::data::FanMode;

/// C# `ConfigData.cs` varsayılanları.
pub const THERMAL_HIGH_C: u8 = 95;
pub const THERMAL_LOW_C: u8 = 88;
/// Yüksek eşiğin bu kadar üstünde hâlâ ısınıyorsa tüm manuel kontrol bırakılır.
pub const THERMAL_PANIC_MARGIN_C: u8 = 4;
/// XFCD geri sayımına yazılan yenileme değeri [s].
pub const COUNTDOWN_EXTEND_SECS: u8 = 120;
/// Geri sayım `program tick + bu eşik`in altına inince yenilenir [s].
pub const COUNTDOWN_EXTEND_THRESHOLD: u8 = 5;
/// Fan programı / geri sayım tick aralığı [s] (C# `UpdateProgramInterval`).
pub const PROGRAM_TICK_SECS: u64 = 15;
/// Termal koruma tick aralığı [s] (C# `UpdateMonitorInterval`).
pub const GUARD_TICK_SECS: u64 = 3;
/// Kullanıcı modunun (sticky) yeniden uygulama aralığı [ms].
pub const MODE_KEEPALIVE_MS: u64 = 5000;
/// En yüksek fan seviyesi [krpm*100] (C# `FanLevelMax`).
pub const FAN_LEVEL_MAX: u8 = 55;
/// BIOS `SetFanLevel` için "otomatiğe dön" değeri.
pub const FAN_LEVEL_AUTO: u8 = 0xFF;

/// UI'da gösterilen ve hw thread'in yürüttüğü kontrol durumu.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum FanControl {
    /// EC'nin kendi otomatik yönetimi (varsayılan).
    #[default]
    Auto,
    /// Sabit seviye: (CPU, GPU) [krpm*100]; XFCD yenilemesiyle canlı tutulur.
    Manual { cpu: u8, gpu: u8 },
    /// BIOS maksimum fan modu.
    Max,
}

/// Yüzde (0-100) → BIOS fan seviyesi [krpm*100].
pub fn percent_to_level(percent: u8) -> u8 {
    (percent.min(100) as u16 * FAN_LEVEL_MAX as u16 / 100) as u8
}

/// BIOS fan seviyesi → yüzde.
pub fn level_to_percent(level: u8) -> u8 {
    (level.min(FAN_LEVEL_MAX) as u16 * 100 / FAN_LEVEL_MAX as u16) as u8
}

// ---- Fan programı (C# `FanProgramData` + `GetTemperatureLevel`) ----

/// Sıcaklık eşiği → (CPU, GPU) fan seviyesi eşlemesi.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FanProgram {
    pub name: String,
    /// Program çalışırken korunacak fan modu.
    pub fan_mode: FanMode,
    /// Eşik [°C] → (CPU seviye, GPU seviye) [krpm*100].
    pub levels: BTreeMap<u8, (u8, u8)>,
}

impl FanProgram {
    /// Verilen sıcaklık için hedef seviyeler: en büyük `eşik <= sıcaklık`
    /// girdisi; tüm eşiklerin altındaysa ilk girdiye kenetlenir
    /// (C# `GetTemperatureLevel` binary-search semantiği).
    pub fn levels_for(&self, temperature: u8) -> Option<(u8, u8)> {
        self.levels
            .range(..=temperature)
            .next_back()
            .or_else(|| self.levels.iter().next())
            .map(|(_, v)| *v)
    }
}

/// Yerleşik varsayılan program: sessiz taban, kademeli artış.
/// (OmenMon örnek yapılandırmasındaki eğrinin muadili; P5'te TOML'dan gelecek.)
pub fn default_program() -> FanProgram {
    FanProgram {
        name: "Varsayılan".into(),
        fan_mode: FanMode::Default,
        levels: BTreeMap::from([
            (0, (20, 20)),
            (55, (25, 25)),
            (65, (32, 32)),
            (75, (40, 40)),
            (85, (48, 48)),
            (90, (FAN_LEVEL_MAX, FAN_LEVEL_MAX)),
        ]),
    }
}

// ---- Termal koruma (C# `GuiTray.CheckThermalGuard` histerezisi) ----

/// Koruma adımının donanıma çevrilecek sonucu.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GuardAction {
    /// Yüksek eşik aşıldı: fanları maksimuma al.
    EngageMax,
    /// Maksimuma rağmen ısınma sürüyor: tüm manuel kontrolü bırak,
    /// EC'nin otomatik yönetimine tam teslim + maksimum isteğini koru.
    Panic,
    /// Düşük eşiğin altına inildi: maksimum fan isteğini bırak.
    Release,
}

/// Histerezisli termal koruma durum makinesi. Donanıma dokunmaz;
/// her `step` çağrısı en fazla bir eylem döndürür.
#[derive(Clone, Copy, Debug, Default)]
pub struct ThermalGuard {
    pub active: bool,
    panic_applied: bool,
}

impl ThermalGuard {
    /// 3 saniyelik koruma tick'i; `temperature` sensör kümesinin maksimumu
    /// (0 = güvenilir okuma yok → durum değişmez).
    pub fn step(&mut self, temperature: u8) -> Option<GuardAction> {
        if temperature == 0 {
            return None;
        }
        if !self.active && temperature >= THERMAL_HIGH_C {
            self.active = true;
            return Some(GuardAction::EngageMax);
        }
        if self.active
            && !self.panic_applied
            && temperature >= THERMAL_HIGH_C.saturating_add(THERMAL_PANIC_MARGIN_C)
        {
            self.panic_applied = true;
            return Some(GuardAction::Panic);
        }
        if self.active && temperature <= THERMAL_LOW_C {
            self.active = false;
            self.panic_applied = false;
            return Some(GuardAction::Release);
        }
        None
    }

    /// C# `SafeToKeepManualFans`: manuel fan durumunu canlı tutmak (XFCD
    /// yenilemek) yalnız koruma pasifken ve makul, eşik altı bir sıcaklık
    /// okuması varken güvenlidir. Şüphede failsafe kazanır.
    pub fn safe_to_extend(&self, temperature: u8) -> bool {
        !self.active && temperature > 0 && temperature < THERMAL_HIGH_C
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn program_threshold_lookup_matches_csharp() {
        let p = default_program();
        assert_eq!(p.levels_for(0), Some((20, 20)));   // tam eşleşme
        assert_eq!(p.levels_for(54), Some((20, 20)));  // aradaki değer alta iner
        assert_eq!(p.levels_for(55), Some((25, 25)));
        assert_eq!(p.levels_for(74), Some((32, 32)));
        assert_eq!(p.levels_for(200), Some((55, 55))); // üstte son girdi
        // En düşük eşiğin altı ilk girdiye kenetlenir
        let p2 = FanProgram { levels: BTreeMap::from([(40, (30, 30))]), ..p };
        assert_eq!(p2.levels_for(20), Some((30, 30)));
    }

    #[test]
    fn guard_hysteresis_sequence() {
        let mut g = ThermalGuard::default();
        assert_eq!(g.step(94), None);
        assert_eq!(g.step(95), Some(GuardAction::EngageMax));
        assert!(g.active);
        assert_eq!(g.step(96), None); // aktifken ara bölgede eylem yok
        assert_eq!(g.step(99), Some(GuardAction::Panic));
        assert_eq!(g.step(99), None); // panik bir kez uygulanır
        assert_eq!(g.step(90), None); // düşük eşiğin üstünde bırakmaz
        assert_eq!(g.step(88), Some(GuardAction::Release));
        assert!(!g.active);
        // Bırakmadan sonra döngü baştan çalışır
        assert_eq!(g.step(95), Some(GuardAction::EngageMax));
    }

    #[test]
    fn guard_ignores_missing_reading() {
        let mut g = ThermalGuard::default();
        assert_eq!(g.step(0), None);
        g.step(95);
        assert_eq!(g.step(0), None); // okuma kaybolursa durum korunur
        assert!(g.active);
        assert!(!g.safe_to_extend(0));
        assert!(!g.safe_to_extend(60)); // koruma aktifken asla
        g.step(88);
        assert!(g.safe_to_extend(60));
        assert!(!g.safe_to_extend(95));
    }

    #[test]
    fn percent_level_conversion() {
        assert_eq!(percent_to_level(0), 0);
        assert_eq!(percent_to_level(40), 22);
        assert_eq!(percent_to_level(100), FAN_LEVEL_MAX);
        assert_eq!(percent_to_level(255), FAN_LEVEL_MAX);
        assert_eq!(level_to_percent(FAN_LEVEL_MAX), 100);
        assert_eq!(level_to_percent(22), 40);
    }
}
