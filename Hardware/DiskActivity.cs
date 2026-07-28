// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.IO;
using StarMon.External;

namespace StarMon.Hardware {

    // Provides read / write throughput for the system disk, in MB/s
    public static class DiskActivity {

        // IOCTL_DISK_PERFORMANCE = CTL_CODE(IOCTL_DISK_BASE 0x7, 0x0008, METHOD_BUFFERED, FILE_ANY_ACCESS)
        private const uint IOCTL_DISK_PERFORMANCE = 0x00070020;

        // Reject deltas spanning more than this (e.g. window hidden a long time)
        private const long MaxDeltaMs = 30000;

        private static bool HasSample;
        private static long LastRead, LastWrite, LastTicks;

        // Physical drive to query (the system / boot drive)
        private static int Drive = 0;

        // Clears the baseline so the next sample starts a fresh interval
        public static void Reset() {
            HasSample = false;
        }

        // Samples throughput since the previous call. Returns false on the first
        // call or an unusable interval, otherwise true with read / write MB/s.
        public static bool Sample(out double readMBs, out double writeMBs) {
            readMBs = 0; writeMBs = 0;

            if(!ReadCounters(out long read, out long write))
                return false;

            long now = Stopwatch.GetTimestamp();

            if(!HasSample) {
                LastRead = read; LastWrite = write; LastTicks = now;
                HasSample = true;
                return false;
            }

            double seconds = (double)(now - LastTicks) / Stopwatch.Frequency;
            long deltaR = read - LastRead;
            long deltaW = write - LastWrite;
            LastRead = read; LastWrite = write; LastTicks = now;

            if(seconds <= 0 || seconds * 1000 > MaxDeltaMs)
                return false;

            if(deltaR < 0) deltaR = 0;
            if(deltaW < 0) deltaW = 0;

            const double mega = 1024.0 * 1024.0;
            readMBs = deltaR / (seconds * mega);
            writeMBs = deltaW / (seconds * mega);
            return true;
        }

        // Reads the cumulative read / written byte counters of the drive
        private static bool ReadCounters(out long read, out long write) {
            read = 0; write = 0;
            IntPtr handle = IntPtr.Zero;
            try {
                handle = Kernel32.CreateFile(
                    @"\\.\PhysicalDrive" + Drive,
                    0,                                  // No access needed for the query
                    FileShare.ReadWrite,
                    IntPtr.Zero,
                    FileMode.Open,
                    0,
                    IntPtr.Zero);

                if(handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return false;

                byte[] buffer = new byte[256];
                if(!Kernel32.DeviceIoControl(
                        handle, IOCTL_DISK_PERFORMANCE,
                        null, 0, buffer, (uint)buffer.Length,
                        out uint _, IntPtr.Zero))
                    return false;

                // DISK_PERFORMANCE: BytesRead at offset 0, BytesWritten at offset 8
                read = BitConverter.ToInt64(buffer, 0);
                write = BitConverter.ToInt64(buffer, 8);
                return true;

            } catch {
                return false;
            } finally {
                if(handle != IntPtr.Zero && handle != new IntPtr(-1))
                    Kernel32.CloseHandle(handle);
            }
        }

    }

}
