//! Batarya durumu: `GetSystemPowerStatus` + `root\WMI` batarya sınıfları
//! (C# `Battery.cs` muadili).

use serde::Deserialize;
use windows::Win32::System::Power::{GetSystemPowerStatus, SYSTEM_POWER_STATUS};
use wmi::WMIConnection;

#[derive(Clone, Copy, Debug, Default)]
pub struct BatteryInfo {
    pub percent: Option<u8>,
    pub on_ac: bool,
    pub charging: bool,
    /// Pozitif: şarj oluyor, negatif: deşarj (Watt).
    pub rate_watts: Option<f32>,
    /// FullChargedCapacity / DesignedCapacity.
    pub health_percent: Option<u8>,
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct BatteryStatusRow {
    #[serde(default)]
    charging: bool,
    #[serde(default)]
    charge_rate: Option<i64>,
    #[serde(default)]
    discharge_rate: Option<i64>,
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct FullChargedRow {
    #[serde(default)]
    full_charged_capacity: Option<i64>,
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct StaticDataRow {
    #[serde(default)]
    designed_capacity: Option<i64>,
}

/// WMI bağlantısı thread'e bağlıdır (COM); hw sampler thread'inde oluşturulmalı.
pub struct BatteryReader {
    wmi: Option<WMIConnection>,
    /// Değişmez; başlangıçta bir kez okunur.
    health_percent: Option<u8>,
}

impl BatteryReader {
    pub fn new() -> Self {
        // wmi 0.18 COM'u thread başına kendisi başlatır.
        let wmi = WMIConnection::with_namespace_path("root\\WMI").ok();
        if wmi.is_none() {
            tracing::warn!("root\\WMI bağlantısı kurulamadı; batarya detayları kapalı");
        }
        let health_percent = wmi.as_ref().and_then(|w| {
            let full: Vec<FullChargedRow> = w
                .raw_query("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity")
                .ok()?;
            let design: Vec<StaticDataRow> = w
                .raw_query("SELECT DesignedCapacity FROM BatteryStaticData")
                .ok()?;
            let f = full.first()?.full_charged_capacity? as f64;
            let d = design.first()?.designed_capacity? as f64;
            (d > 0.0).then(|| ((100.0f64 * f / d).round() as u8).min(100))
        });
        Self { wmi, health_percent }
    }

    pub fn sample(&self) -> Option<BatteryInfo> {
        let mut sps = SYSTEM_POWER_STATUS::default();
        unsafe { GetSystemPowerStatus(&mut sps).ok()? };
        if sps.BatteryFlag == 128 {
            return None; // batarya yok
        }
        let mut info = BatteryInfo {
            percent: (sps.BatteryLifePercent != 255).then_some(sps.BatteryLifePercent),
            on_ac: sps.ACLineStatus == 1,
            health_percent: self.health_percent,
            ..Default::default()
        };
        if let Some(w) = &self.wmi {
            if let Ok(rows) = w.raw_query::<BatteryStatusRow>(
                "SELECT Charging, ChargeRate, DischargeRate FROM BatteryStatus",
            ) {
                if let Some(r) = rows.first() {
                    info.charging = r.charging;
                    let mw = if r.charging {
                        r.charge_rate.unwrap_or(0)
                    } else {
                        -r.discharge_rate.unwrap_or(0)
                    };
                    if mw != 0 {
                        info.rate_watts = Some(mw as f32 / 1000.0);
                    }
                }
            }
        }
        Some(info)
    }
}
