// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace StarMon.Hardware {

    // Provides aggregate network throughput across all active interfaces
    public static class NetworkMeter {

        private static bool HasSample;
        private static long LastTicks;

        // The counters of each interface at the last sample, by its id.
        //
        // Per interface, not one sum. The sum of every interface's lifetime
        // counters jumped by the whole of an adapter's history the moment
        // one came up - Wi-Fi reconnecting, a VPN, a virtual switch - and the
        // meter showed gigabits per second for one reading; and fell when one
        // went away, which the clamp turned into nothing at all while the
        // other links were busy. Only an interface seen at both samples adds
        // to the rate, and only by its own change.
        private static Dictionary<string, (long Rx, long Tx)> Last =
            new Dictionary<string, (long Rx, long Tx)>();

        // Reject deltas spanning more than this (e.g. window hidden a long time)
        private const long MaxDeltaMs = 30000;

        // Clears the baseline so the next sample starts a fresh interval
        public static void Reset() {
            HasSample = false;
        }

        // Samples throughput since the previous call. Returns false (with zeros)
        // if this is the first call or the interval is unusable, otherwise true
        // with the download and upload rates in megabits per second.
        public static bool Sample(out double downMbps, out double upMbps) {
            downMbps = 0;
            upMbps = 0;

            Dictionary<string, (long Rx, long Tx)> current =
                new Dictionary<string, (long Rx, long Tx)>();
            try {
                foreach(NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                    try {
                        if(ni.OperationalStatus != OperationalStatus.Up)
                            continue;
                        if(ni.NetworkInterfaceType == NetworkInterfaceType.Loopback
                            || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                            continue;

                        // GetIPStatistics rather than GetIPv4Statistics.
                        //
                        // The IPv4-only counters report nothing on a link
                        // carrying IPv6, which on a mobile network or an
                        // IPv6-only home connection is all of the traffic —
                        // the meter sat at zero while the machine was
                        // downloading. The general form counts both, and on
                        // an IPv4-only link returns the same numbers.
                        IPInterfaceStatistics s = ni.GetIPStatistics();
                        current[ni.Id] = (s.BytesReceived, s.BytesSent);
                    } catch { }
                }
            } catch {
                return false;
            }

            if(current.Count == 0)
                return false;

            long now = Stopwatch.GetTimestamp();

            Dictionary<string, (long Rx, long Tx)> previous = Last;
            bool hadSample = HasSample;
            Last = current;
            HasSample = true;

            double seconds = (double)(now - LastTicks) / Stopwatch.Frequency;
            LastTicks = now;

            if(!hadSample)
                return false;

            if(seconds <= 0 || seconds * 1000 > MaxDeltaMs)
                return false;

            long deltaRx = 0, deltaTx = 0;
            foreach(KeyValuePair<string, (long Rx, long Tx)> entry in current) {
                (long Rx, long Tx) before;
                if(!previous.TryGetValue(entry.Key, out before))
                    continue;
                // A counter that went backwards was reset; it adds nothing
                deltaRx += Math.Max(0, entry.Value.Rx - before.Rx);
                deltaTx += Math.Max(0, entry.Value.Tx - before.Tx);
            }

            downMbps = (deltaRx * 8.0) / (seconds * 1_000_000.0);
            upMbps = (deltaTx * 8.0) / (seconds * 1_000_000.0);
            return true;
        }

    }

}
