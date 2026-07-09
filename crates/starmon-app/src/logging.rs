use anyhow::{Context, Result};
use std::path::PathBuf;
use tracing_appender::non_blocking::WorkerGuard;
use tracing_subscriber::EnvFilter;

/// Log dizini: `%LOCALAPPDATA%\StarMonRs\logs` (exe yanına yazmak Program Files
/// altında başarısız olur).
fn log_dir() -> Result<PathBuf> {
    let base = std::env::var_os("LOCALAPPDATA").context("LOCALAPPDATA tanımlı değil")?;
    Ok(PathBuf::from(base).join("StarMonRs").join("logs"))
}

/// Günlük dönen dosya log'unu başlatır. Dönen guard süreç ömrü boyunca
/// tutulmalı; düşerse tamponlanan satırlar kaybolur.
pub fn init() -> Result<WorkerGuard> {
    let dir = log_dir()?;
    std::fs::create_dir_all(&dir)?;
    let (writer, guard) =
        tracing_appender::non_blocking(tracing_appender::rolling::daily(&dir, "starmon.log"));
    tracing_subscriber::fmt()
        .with_env_filter(
            // RunAs ile başlatılan süreçlere env geçmediği için varsayılan
            // filtre uygulamanın kendi debug satırlarını da içerir.
            EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| EnvFilter::new("info,starmon=debug,hp_wmi=debug")),
        )
        .with_writer(writer)
        .with_ansi(false)
        .init();
    Ok(guard)
}
