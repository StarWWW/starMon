use embed_manifest::manifest::{DpiAwareness, ExecutionLevel};
use embed_manifest::{embed_manifest, new_manifest};

fn main() {
    let level = if std::env::var_os("CARGO_FEATURE_ELEVATED").is_some() {
        ExecutionLevel::RequireAdministrator
    } else {
        ExecutionLevel::AsInvoker
    };
    embed_manifest(
        new_manifest("StarWWW.StarMon")
            .requested_execution_level(level)
            .dpi_awareness(DpiAwareness::PerMonitorV2),
    )
    .expect("manifest gömülemedi");
    println!("cargo:rerun-if-changed=build.rs");
}
