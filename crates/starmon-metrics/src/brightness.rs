//! Dahili ekran parlaklığı, `WmiMonitorBrightness` üzerinden
//! (C# `DisplayBrightness.cs` muadili; Set tarafı P5'te eklenecek).

use serde::Deserialize;
use wmi::WMIConnection;

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct BrightnessRow {
    #[serde(default)]
    current_brightness: Option<i64>,
}

/// WMI bağlantısı thread'e bağlıdır; hw sampler thread'inde oluşturulmalı.
pub struct BrightnessReader {
    wmi: Option<WMIConnection>,
}

impl BrightnessReader {
    pub fn new() -> Self {
        Self {
            wmi: WMIConnection::with_namespace_path("root\\WMI").ok(),
        }
    }

    pub fn sample(&self) -> Option<u8> {
        let rows: Vec<BrightnessRow> = self
            .wmi
            .as_ref()?
            .raw_query("SELECT CurrentBrightness FROM WmiMonitorBrightness")
            .ok()?;
        rows.first()?
            .current_brightness
            .map(|v| v.clamp(0, 100) as u8)
    }
}
