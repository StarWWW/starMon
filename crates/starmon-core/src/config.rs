//! Yapılandırma: TOML kalıcılığı + eski `StarMon.xml`'den tek seferlik içe
//! aktarma (fan programları). Dosya: `%LOCALAPPDATA%\StarMonRs\config.toml`.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use crate::fan::{FanProgram, THERMAL_HIGH_C, THERMAL_LOW_C};
use hp_wmi::data::FanMode;

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(default)]
pub struct Config {
    pub thermal: ThermalConfig,
    pub ui: UiConfig,
    #[serde(rename = "fan_program")]
    pub fan_programs: Vec<FanProgramConfig>,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            thermal: ThermalConfig::default(),
            ui: UiConfig::default(),
            fan_programs: vec![FanProgramConfig::from_program(&crate::fan::default_program())],
        }
    }
}

#[derive(Serialize, Deserialize, Clone, Copy, Debug, PartialEq)]
#[serde(default)]
pub struct ThermalConfig {
    pub enabled: bool,
    pub high_c: u8,
    pub low_c: u8,
}

impl Default for ThermalConfig {
    fn default() -> Self {
        Self { enabled: true, high_c: THERMAL_HIGH_C, low_c: THERMAL_LOW_C }
    }
}

#[derive(Serialize, Deserialize, Clone, Copy, Debug, PartialEq)]
#[serde(default)]
pub struct UiConfig {
    pub history_window_secs: u64,
    pub manual_percent: u8,
}

impl Default for UiConfig {
    fn default() -> Self {
        Self { history_window_secs: 300, manual_percent: 40 }
    }
}

/// TOML'daki fan programı gösterimi (çalışma zamanında `FanProgram`a çevrilir).
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
pub struct FanProgramConfig {
    pub name: String,
    /// "Default" | "Performance" | "Cool" | "Quiet" | "Extreme"
    pub fan_mode: String,
    pub levels: Vec<FanLevelEntry>,
}

#[derive(Serialize, Deserialize, Clone, Copy, Debug, PartialEq)]
pub struct FanLevelEntry {
    pub temp: u8,
    pub cpu: u8,
    pub gpu: u8,
}

fn parse_fan_mode(s: &str) -> FanMode {
    match s {
        "Performance" => FanMode::Performance,
        "Cool" => FanMode::Cool,
        "Quiet" => FanMode::Quiet,
        "Extreme" => FanMode::Extreme,
        "Default" => FanMode::Default,
        other => {
            tracing::warn!("bilinmeyen fan modu '{other}', Default kullanılıyor");
            FanMode::Default
        }
    }
}

impl FanProgramConfig {
    pub fn to_program(&self) -> FanProgram {
        FanProgram {
            name: self.name.clone(),
            fan_mode: parse_fan_mode(&self.fan_mode),
            levels: self
                .levels
                .iter()
                .map(|l| (l.temp, (l.cpu, l.gpu)))
                .collect::<BTreeMap<_, _>>(),
        }
    }

    pub fn from_program(p: &FanProgram) -> Self {
        Self {
            name: p.name.clone(),
            fan_mode: format!("{:?}", p.fan_mode),
            levels: p
                .levels
                .iter()
                .map(|(t, (c, g))| FanLevelEntry { temp: *t, cpu: *c, gpu: *g })
                .collect(),
        }
    }
}

// ---- Yükleme / kaydetme ----

pub fn config_dir() -> Option<PathBuf> {
    Some(PathBuf::from(std::env::var_os("LOCALAPPDATA")?).join("StarMonRs"))
}

fn config_path() -> Option<PathBuf> {
    Some(config_dir()?.join("config.toml"))
}

/// Config'i yükler. Dosya yoksa: exe'nin yanında veya config dizininde
/// `StarMon.xml` varsa fan programlarını içe aktarır, sonucu kaydeder.
pub fn load() -> Config {
    if let Some(path) = config_path() {
        match std::fs::read_to_string(&path) {
            Ok(text) => match toml::from_str::<Config>(&text) {
                Ok(cfg) => return cfg,
                Err(e) => {
                    tracing::warn!("config.toml çözümlenemedi ({e}); varsayılan kullanılıyor");
                    return Config::default();
                }
            },
            Err(_) => {
                // İlk çalıştırma: XML importu dene
                let mut cfg = Config::default();
                for candidate in xml_candidates() {
                    if let Some(programs) = import_xml(&candidate) {
                        tracing::info!(
                            "{} programı {} dosyasından içe aktarıldı",
                            programs.len(),
                            candidate.display()
                        );
                        if !programs.is_empty() {
                            cfg.fan_programs = programs;
                        }
                        break;
                    }
                }
                save(&cfg);
                return cfg;
            }
        }
    }
    Config::default()
}

pub fn save(cfg: &Config) {
    let Some(path) = config_path() else { return };
    let Ok(text) = toml::to_string_pretty(cfg) else { return };
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    if let Err(e) = std::fs::write(&path, text) {
        tracing::warn!("config kaydedilemedi: {e}");
    }
}

fn xml_candidates() -> Vec<PathBuf> {
    let mut v = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            v.push(dir.join("StarMon.xml"));
        }
    }
    if let Some(dir) = config_dir() {
        v.push(dir.join("StarMon.xml"));
    }
    v
}

// ---- Eski StarMon.xml içe aktarma ----

/// `StarMon/Config/FanPrograms/Program` düğümlerini okur; dosya yoksa `None`.
/// Orijinal dosyaya asla yazılmaz.
pub fn import_xml(path: &Path) -> Option<Vec<FanProgramConfig>> {
    let text = std::fs::read_to_string(path).ok()?;
    Some(parse_xml_programs(&text))
}

fn parse_xml_programs(text: &str) -> Vec<FanProgramConfig> {
    use quick_xml::events::Event;

    let mut reader = quick_xml::Reader::from_str(text);
    reader.config_mut().trim_text(true);
    let mut programs = Vec::new();
    let mut current: Option<FanProgramConfig> = None;
    let mut current_level: Option<FanLevelEntry> = None;
    // Metin içeriğinin hangi elemana ait olduğunu izle
    let mut element_stack: Vec<String> = Vec::new();

    let attr = |e: &quick_xml::events::BytesStart, name: &str| -> Option<String> {
        e.attributes().flatten().find_map(|a| {
            (a.key.as_ref() == name.as_bytes())
                .then(|| String::from_utf8_lossy(&a.value).into_owned())
        })
    };

    loop {
        match reader.read_event() {
            Ok(Event::Start(e)) => {
                let name = String::from_utf8_lossy(e.name().as_ref()).into_owned();
                match name.as_str() {
                    "Program" => {
                        current = Some(FanProgramConfig {
                            name: attr(&e, "Name").unwrap_or_else(|| "Adsız".into()),
                            fan_mode: "Default".into(),
                            levels: Vec::new(),
                        });
                    }
                    "Level" => {
                        let temp = attr(&e, "Temperature")
                            .and_then(|t| t.trim().parse::<u8>().ok())
                            .unwrap_or(0);
                        current_level = Some(FanLevelEntry { temp, cpu: 0, gpu: 0 });
                    }
                    _ => {}
                }
                element_stack.push(name);
            }
            Ok(Event::Text(t)) => {
                let Ok(text) = t.xml_content(quick_xml::XmlVersion::Implicit1_0) else {
                    continue;
                };
                let text = text.trim().to_owned();
                match element_stack.last().map(String::as_str) {
                    Some("FanMode") => {
                        if let Some(p) = &mut current {
                            p.fan_mode = text;
                        }
                    }
                    Some("Cpu") => {
                        if let (Some(l), Ok(v)) = (&mut current_level, text.parse()) {
                            l.cpu = v;
                        }
                    }
                    Some("Gpu") => {
                        if let (Some(l), Ok(v)) = (&mut current_level, text.parse()) {
                            l.gpu = v;
                        }
                    }
                    _ => {}
                }
            }
            Ok(Event::End(e)) => {
                match e.name().as_ref() {
                    b"Level" => {
                        if let (Some(p), Some(l)) = (&mut current, current_level.take()) {
                            p.levels.push(l);
                        }
                    }
                    b"Program" => {
                        if let Some(mut p) = current.take() {
                            p.levels.sort_by_key(|l| l.temp);
                            programs.push(p);
                        }
                    }
                    _ => {}
                }
                element_stack.pop();
            }
            Ok(Event::Eof) => break,
            Err(e) => {
                tracing::warn!("XML çözümleme hatası: {e}");
                break;
            }
            _ => {}
        }
    }
    programs
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn toml_roundtrip() {
        let cfg = Config::default();
        let text = toml::to_string_pretty(&cfg).unwrap();
        let back: Config = toml::from_str(&text).unwrap();
        assert_eq!(cfg, back);
        assert_eq!(back.fan_programs.len(), 1);
        assert_eq!(back.thermal.high_c, 95);
    }

    #[test]
    fn xml_import_matches_csharp_layout() {
        let xml = r#"<?xml version="1.0" encoding="utf-8"?>
<StarMon>
  <Config>
    <FanPrograms>
      <Program Name="Güç">
        <FanMode>Performance</FanMode>
        <GpuPower>Maximum</GpuPower>
        <Level Temperature="00"><Cpu>21</Cpu><Gpu>21</Gpu></Level>
        <Level Temperature="65"><Cpu>28</Cpu><Gpu>30</Gpu></Level>
        <Level Temperature="85"><Cpu>55</Cpu><Gpu>57</Gpu></Level>
      </Program>
      <Program Name="Sessiz">
        <FanMode>Quiet</FanMode>
        <GpuPower>Minimum</GpuPower>
        <Level Temperature="70"><Cpu>00</Cpu><Gpu>00</Gpu></Level>
      </Program>
    </FanPrograms>
  </Config>
</StarMon>"#;
        let programs = parse_xml_programs(xml);
        assert_eq!(programs.len(), 2);
        assert_eq!(programs[0].name, "Güç");
        assert_eq!(programs[0].fan_mode, "Performance");
        assert_eq!(
            programs[0].levels,
            vec![
                FanLevelEntry { temp: 0, cpu: 21, gpu: 21 },
                FanLevelEntry { temp: 65, cpu: 28, gpu: 30 },
                FanLevelEntry { temp: 85, cpu: 55, gpu: 57 },
            ]
        );
        let p = programs[0].to_program();
        assert_eq!(p.fan_mode, FanMode::Performance);
        assert_eq!(p.levels_for(70), Some((28, 30)));
        assert_eq!(programs[1].name, "Sessiz");
        assert_eq!(programs[1].to_program().fan_mode, FanMode::Quiet);
    }
}
