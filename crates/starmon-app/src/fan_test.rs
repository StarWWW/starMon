//! Aşamalı canlı fan yazma testi (`--fan-test [kill]`) — P4 doğrulama planı.
//!
//! GUI açılmadan, yükseltilmiş süreçte koşar; her adım log'a ve konsola
//! yazılır. `kill` varyantı sabit seviye ayarladıktan sonra süreci temizlik
//! YAPMADAN düşürür (`abort`): EC'nin XFCD donanımsal failsafe'inin fanı
//! ~2 dakikada otomatiğe geri aldığı ayrı bir salt-okuma koşusuyla doğrulanır.
//!
//! Aşamalar:
//! 1. XFCD geri sayım yazma/okuma (en zararsız register).
//! 2. Sabit %40 seviye → RPM/XGS değişimi + geri sayım gözlemi → otomatiğe dönüş.

use std::time::Duration;

use anyhow::{bail, Context, Result};
use hp_wmi::HpWmiBios;
use starmon_core::fan::{percent_to_level, COUNTDOWN_EXTEND_SECS, FAN_LEVEL_AUTO};
use starmon_hw::ec::EmbeddedController;
use starmon_hw::ec_data::{reg, EcWritable, FAN_MANUAL_OFF, FAN_MANUAL_ON};

/// Bu sıcaklığın üstünde test hiç başlamaz.
const MAX_SAFE_START_TEMP_C: u8 = 85;

macro_rules! step {
    ($($arg:tt)*) => {{
        tracing::info!($($arg)*);
        println!($($arg)*);
    }};
}

/// Salt-okuma izleme: kill testinden sonra EC'nin kendi başına otomatiğe
/// döndüğünü kanıtlamak için ~3 dk boyunca fan durumunu örnekler. Yazmaz.
pub fn watch() -> Result<()> {
    let ec = EmbeddedController::new().context("EC erişimi (PawnIO gerekir)")?;
    step!("=== salt-okuma fan izleme (failsafe doğrulaması) ===");
    for i in 0..=9u32 {
        if i > 0 {
            std::thread::sleep(Duration::from_secs(20));
        }
        step!("t+{}s: {}", i * 20, read_fans(&ec));
    }
    step!("=== izleme bitti ===");
    Ok(())
}

/// Fanı derhal otomatiğe döndürür (kill testi sonrası temizlik).
pub fn restore_auto() -> Result<()> {
    let bios = HpWmiBios::new().context("BIOS erişimi")?;
    let ec = EmbeddedController::new().context("EC erişimi")?;
    step!("=== fan otomatiğe döndürülüyor ===");
    step!("önce: {}", read_fans(&ec));
    bios.set_fan_level(FAN_LEVEL_AUTO, FAN_LEVEL_AUTO)?;
    ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_OFF)?;
    ec.write_byte(EcWritable::Countdown, 0)?;
    std::thread::sleep(Duration::from_secs(6));
    step!("sonra: {}", read_fans(&ec));
    Ok(())
}

/// EC yolu (SRP1/SRP2) seviye testi: BIOS yolunda XFCD failsafe'inin
/// işlemediği görüldü; gerçek donanımsal geri dönüş EC yolunda mı çalışıyor
/// onu ölçer. Sonuç, uygulamanın seviye yazma yolunu belirler.
pub fn ec_level_test() -> Result<()> {
    let bios = HpWmiBios::new().context("BIOS erişimi")?;
    let ec = EmbeddedController::new().context("EC erişimi")?;
    step!("=== EC (SRP) seviye + failsafe testi ===");
    let cpu_t = ec.read_byte(reg::CPUT).unwrap_or(0);
    if cpu_t == 0 || cpu_t >= MAX_SAFE_START_TEMP_C {
        bail!("sıcaklık uygunsuz ({cpu_t}°C) — test iptal");
    }
    let baseline = read_fans(&ec);
    step!("taban çizgisi: {baseline}");
    let level = percent_to_level(40);

    // Önce C# varsayılanı gibi: yalnız SRP yaz (OMCC'siz)
    step!("--- SRP={level} (OMCC'siz) ---");
    ec.write_byte(EcWritable::LeftFanTargetLevel, level)?;
    ec.write_byte(EcWritable::RightFanTargetLevel, level)?;
    std::thread::sleep(Duration::from_secs(9));
    let srp_only = read_fans(&ec);
    step!("t+9s: {srp_only}");
    let mut took = rpm_delta(&baseline, &srp_only) > 300;

    if !took {
        // OMCC=6 (manuel aç) ile tekrar dene
        step!("--- OMCC=0x06 + SRP={level} ---");
        ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_ON)?;
        ec.write_byte(EcWritable::LeftFanTargetLevel, level)?;
        ec.write_byte(EcWritable::RightFanTargetLevel, level)?;
        std::thread::sleep(Duration::from_secs(9));
        let with_omcc = read_fans(&ec);
        step!("t+9s: {with_omcc}");
        took = rpm_delta(&baseline, &with_omcc) > 300;
    }

    if !took {
        step!("SONUÇ: EC/SRP yolu bu makinede fan hızını DEĞİŞTİRMİYOR — temizlenip çıkılıyor");
        ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_OFF)?;
        ec.write_byte(EcWritable::Countdown, 0)?;
        return Ok(());
    }

    // Manuel etki etti: XFCD kur ve süresi dolunca kendiliğinden dönüş var mı izle
    step!("--- XFCD=120 kuruldu; süre dolumu izleniyor (~3 dk) ---");
    ec.write_byte(EcWritable::Countdown, COUNTDOWN_EXTEND_SECS)?;
    let mut expired_at: Option<u32> = None;
    for i in 1..=14u32 {
        std::thread::sleep(Duration::from_secs(15));
        let now = read_fans(&ec);
        step!("t+{}s: {now}", i * 15);
        if expired_at.is_none() && now.countdown == Some(0) {
            expired_at = Some(i);
        }
        // Süre dolduktan sonra ~45 sn daha gözle
        if expired_at.is_some_and(|e| i >= e + 3) {
            let reverted = rpm_delta(&baseline, &now) < 400;
            step!(
                "SONUÇ: XFCD doldu; fanlar otomatiğe {} (EC failsafe {})",
                if reverted { "DÖNDÜ" } else { "DÖNMEDİ" },
                if reverted { "ÇALIŞIYOR" } else { "ÇALIŞMIYOR" }
            );
            break;
        }
    }

    // Her durumda temizlik
    step!("temizlik: otomatiğe dönülüyor");
    bios.set_fan_level(FAN_LEVEL_AUTO, FAN_LEVEL_AUTO)?;
    ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_OFF)?;
    ec.write_byte(EcWritable::Countdown, 0)?;
    std::thread::sleep(Duration::from_secs(6));
    step!("son durum: {}", read_fans(&ec));
    Ok(())
}

fn rpm_delta(a: &FanReading, b: &FanReading) -> u16 {
    match (a.rpm1, b.rpm1) {
        (Some(x), Some(y)) => x.abs_diff(y),
        _ => 0,
    }
}

pub fn run(kill_mode: bool) -> Result<()> {
    step!("=== P4 fan yazma testi (kill={kill_mode}) ===");
    let bios = HpWmiBios::new().context("BIOS erişimi (yükseltme gerekir)")?;
    let ec = EmbeddedController::new().context("EC erişimi (PawnIO gerekir)")?;

    // Emniyet: sıcakken test yok
    let cpu_t = ec.read_byte(reg::CPUT).unwrap_or(0);
    let gpu_t = ec.read_byte(reg::GPTM).unwrap_or(0);
    step!("başlangıç: CPU {cpu_t}°C, GPU {gpu_t}°C");
    if cpu_t == 0 || cpu_t >= MAX_SAFE_START_TEMP_C || gpu_t >= MAX_SAFE_START_TEMP_C {
        bail!("sıcaklık okunamadı veya {MAX_SAFE_START_TEMP_C}°C üstü — test iptal");
    }
    let baseline = read_fans(&ec);
    step!("taban çizgisi: {baseline}");

    // --- Aşama 1: XFCD yazma/okuma ---
    step!("--- aşama 1: XFCD geri sayımı ---");
    ec.write_byte(EcWritable::Countdown, COUNTDOWN_EXTEND_SECS)?;
    let cd1 = ec.read_byte(reg::XFCD)?;
    step!("XFCD={} yazıldı, okunan: {cd1}", COUNTDOWN_EXTEND_SECS);
    let stage1_write_ok = cd1 > 100;
    ec.write_byte(EcWritable::Countdown, 0)?;
    let cd0 = ec.read_byte(reg::XFCD)?;
    step!("XFCD=0 yazıldı, okunan: {cd0}");
    let stage1 = stage1_write_ok && cd0 == 0;
    step!("aşama 1: {}", if stage1 { "BAŞARILI" } else { "BAŞARISIZ" });
    if !stage1 {
        bail!("XFCD yazma yolu doğrulanamadı; sonraki aşamalar iptal");
    }

    // --- Aşama 2: sabit %40 seviye ---
    let level = percent_to_level(40);
    step!("--- aşama 2: sabit %40 (seviye {level}) ---");
    bios.set_fan_level(level, level)?;
    ec.write_byte(EcWritable::Countdown, COUNTDOWN_EXTEND_SECS)?;

    if kill_mode {
        step!(
            "KILL TESTİ: sabit seviye ayarlı, süreç temizliksiz düşürülüyor. \
             ~2,5 dk sonra salt-okuma koşusuyla XFCD=0 ve RPM'in otomatiğe \
             döndüğünü doğrulayın."
        );
        // stdout/log'un diske inmesi için kısa bekleme
        std::thread::sleep(Duration::from_millis(500));
        std::process::abort();
    }

    let mut rpm_moved = false;
    let mut countdown_ticking = false;
    let mut last_cd = COUNTDOWN_EXTEND_SECS;
    for i in 1..=5u32 {
        std::thread::sleep(Duration::from_secs(3));
        let now = read_fans(&ec);
        step!("t+{}s: {now}", i * 3);
        if let (Some(b1), Some(n1)) = (baseline.rpm1, now.rpm1) {
            if b1.abs_diff(n1) > 300 {
                rpm_moved = true;
            }
        }
        if let Some(cd) = now.countdown {
            if cd > 0 && cd < last_cd {
                countdown_ticking = true;
            }
            last_cd = cd;
        }
    }

    // Otomatiğe dönüş
    step!("otomatiğe dönülüyor...");
    bios.set_fan_level(FAN_LEVEL_AUTO, FAN_LEVEL_AUTO)?;
    ec.write_byte(EcWritable::ManualToggle, FAN_MANUAL_OFF)?;
    ec.write_byte(EcWritable::Countdown, 0)?;
    std::thread::sleep(Duration::from_secs(6));
    let after = read_fans(&ec);
    step!("otomatik sonrası: {after}");

    step!(
        "aşama 2: RPM değişimi {} · geri sayım işliyor {} → {}",
        if rpm_moved { "EVET" } else { "HAYIR" },
        if countdown_ticking { "EVET" } else { "HAYIR" },
        if rpm_moved { "BAŞARILI" } else { "BAŞARISIZ" }
    );
    step!("=== test bitti ===");
    if !rpm_moved {
        bail!("fan seviyesi yazması RPM'e yansımadı");
    }
    Ok(())
}

struct FanReading {
    rpm1: Option<u16>,
    rpm2: Option<u16>,
    xgs: (Option<u8>, Option<u8>),
    countdown: Option<u8>,
    omcc: Option<u8>,
}

fn read_fans(ec: &EmbeddedController) -> FanReading {
    FanReading {
        rpm1: ec.read_word(reg::RPM1).ok(),
        rpm2: ec.read_word(reg::RPM3).ok(),
        xgs: (ec.read_byte(reg::XGS1).ok(), ec.read_byte(reg::XGS2).ok()),
        countdown: ec.read_byte(reg::XFCD).ok(),
        omcc: ec.read_byte(reg::OMCC).ok(),
    }
}

impl std::fmt::Display for FanReading {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "rpm={:?}/{:?} xgs={:?}/{:?} xfcd={:?} omcc={:?}",
            self.rpm1, self.rpm2, self.xgs.0, self.xgs.1, self.countdown, self.omcc
        )
    }
}
