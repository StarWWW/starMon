//! Ağ arayüzü toplam aktarım hızları, `GetIfTable2` deltasından
//! (C# `NetworkMeter.cs` muadili).

use std::time::Instant;
use windows::Win32::NetworkManagement::IpHelper::{FreeMibTable, GetIfTable2, MIB_IF_TABLE2};

#[derive(Clone, Copy, Debug, Default)]
pub struct NetworkRates {
    pub rx_bytes_per_sec: u64,
    pub tx_bytes_per_sec: u64,
}

const IF_TYPE_SOFTWARE_LOOPBACK: u32 = 24;
const IF_OPER_STATUS_UP: i32 = 1;

#[derive(Default)]
pub struct NetworkSampler {
    prev: Option<(Instant, u64, u64)>,
}

impl NetworkSampler {
    pub fn sample(&mut self) -> Option<NetworkRates> {
        let (rx, tx) = totals()?;
        let now = Instant::now();
        let rates = self.prev.map(|(t, prx, ptx)| {
            let dt = now.duration_since(t).as_secs_f64().max(0.001);
            NetworkRates {
                rx_bytes_per_sec: (rx.saturating_sub(prx) as f64 / dt) as u64,
                tx_bytes_per_sec: (tx.saturating_sub(ptx) as f64 / dt) as u64,
            }
        });
        self.prev = Some((now, rx, tx));
        rates
    }
}

/// Aktif, loopback olmayan arayüzlerin kümülatif okto toplamları.
fn totals() -> Option<(u64, u64)> {
    let mut table: *mut MIB_IF_TABLE2 = std::ptr::null_mut();
    unsafe {
        GetIfTable2(&mut table).ok().ok()?;
        let t = &*table;
        let rows = std::slice::from_raw_parts(t.Table.as_ptr(), t.NumEntries as usize);
        let (mut rx, mut tx) = (0u64, 0u64);
        for row in rows {
            if row.Type != IF_TYPE_SOFTWARE_LOOPBACK && row.OperStatus.0 == IF_OPER_STATUS_UP {
                rx += row.InOctets;
                tx += row.OutOctets;
            }
        }
        FreeMibTable(table as *const _);
        Some((rx, tx))
    }
}
