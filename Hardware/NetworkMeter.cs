// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace StarMon.Hardware {

    // Provides aggregate network throughput across all active interfaces
    public static class NetworkMeter {

        private static bool HasSample;
        private static long LastRx;
        private static long LastTx;
        private static long LastTicks;

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

            long rx = 0, tx = 0;
            bool any = false;
            try {
                foreach(NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                    try {
                        if(ni.OperationalStatus != OperationalStatus.Up)
                            continue;
                        if(ni.NetworkInterfaceType == NetworkInterfaceType.Loopback
                            || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                            continue;

                        IPv4InterfaceStatistics s = ni.GetIPv4Statistics();
                        rx += s.BytesReceived;
                        tx += s.BytesSent;
                        any = true;
                    } catch { }
                }
            } catch {
                return false;
            }

            if(!any)
                return false;

            long now = Stopwatch.GetTimestamp();

            if(!HasSample) {
                LastRx = rx;
                LastTx = tx;
                LastTicks = now;
                HasSample = true;
                return false;
            }

            double seconds = (double)(now - LastTicks) / Stopwatch.Frequency;
            long deltaRx = rx - LastRx;
            long deltaTx = tx - LastTx;
            LastRx = rx;
            LastTx = tx;
            LastTicks = now;

            if(seconds <= 0 || seconds * 1000 > MaxDeltaMs)
                return false;

            if(deltaRx < 0) deltaRx = 0; // counter reset / interface change
            if(deltaTx < 0) deltaTx = 0;

            downMbps = (deltaRx * 8.0) / (seconds * 1_000_000.0);
            upMbps = (deltaTx * 8.0) / (seconds * 1_000_000.0);
            return true;
        }

    }

}
