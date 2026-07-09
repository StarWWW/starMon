//! Zaman serisi geçmişi: hw thread 3 saniyede bir örnek iter, UI grafik çizer.
//! C# karşılığı `GuiFormMain` history kaydı (30s yavaş mod yerine tek kadans;
//! pencere gizliyken de aynı hızda birikir, maliyeti önemsiz).

use std::collections::VecDeque;

/// Saklanan örnek sayısı: 3 saniyelik kadansta 1 saat.
pub const HISTORY_CAP: usize = 1200;
/// Örnekler arası beklenen aralık [s] (grafik x ekseni için).
pub const HISTORY_STEP_SECS: u64 = 3;

#[derive(Clone, Copy, Debug, Default)]
pub struct HistorySample {
    /// hw thread master tick değeri (saniye sayacı).
    pub tick: u64,
    pub cpu_temp_c: Option<u8>,
    pub gpu_temp_c: Option<u8>,
    pub cpu_load_percent: Option<f32>,
    pub cpu_power_w: Option<f32>,
    pub fan_rpm: (Option<u16>, Option<u16>),
    pub memory_load_percent: Option<u32>,
}

#[derive(Clone, Debug, Default)]
pub struct History {
    samples: VecDeque<HistorySample>,
}

impl History {
    pub fn push(&mut self, sample: HistorySample) {
        if self.samples.len() == HISTORY_CAP {
            self.samples.pop_front();
        }
        self.samples.push_back(sample);
    }

    pub fn is_empty(&self) -> bool {
        self.samples.is_empty()
    }

    /// Son `window_secs` saniyeye düşen örnekler (eski → yeni).
    pub fn window(&self, window_secs: u64) -> impl Iterator<Item = &HistorySample> {
        let cutoff = self
            .samples
            .back()
            .map(|s| s.tick.saturating_sub(window_secs))
            .unwrap_or(0);
        self.samples.iter().filter(move |s| s.tick >= cutoff)
    }

    pub fn latest_tick(&self) -> u64 {
        self.samples.back().map_or(0, |s| s.tick)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn capped_ring_buffer() {
        let mut h = History::default();
        for i in 0..(HISTORY_CAP as u64 + 10) {
            h.push(HistorySample { tick: i, ..Default::default() });
        }
        assert_eq!(h.samples.len(), HISTORY_CAP);
        assert_eq!(h.samples.front().unwrap().tick, 10);
        assert_eq!(h.latest_tick(), HISTORY_CAP as u64 + 9);
    }

    #[test]
    fn window_filters_by_tick() {
        let mut h = History::default();
        for i in (0..300u64).step_by(3) {
            h.push(HistorySample { tick: i, ..Default::default() });
        }
        // Son 30 saniye: tick 267..=297 → 11 örnek
        assert_eq!(h.window(30).count(), 11);
        // Pencere veriden büyükse hepsi döner
        assert_eq!(h.window(10_000).count(), 100);
    }
}
