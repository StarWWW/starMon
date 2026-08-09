// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.External;

namespace StarMon.Hardware {

    // Provides cheap, driver-independent system metrics
    public static class SystemMetrics {

        // CPU-load sampling state (the counters are cumulative, so we keep the
        // previous reading and report the busy fraction over the interval)
        private static long LastIdle, LastKernel, LastUser;
        private static bool HasCpuSample;

        // Clears the CPU-load baseline so the next call starts a fresh interval
        // (call when the window becomes visible again to avoid a stale delta)
        public static void ResetCpuLoad() {
            HasCpuSample = false;
        }

        // Returns the overall CPU load since the previous call, 0-100, or -1 if
        // unavailable or this is the first (baseline) sample
        public static int GetCpuLoadPercent() {
            try {
                if(!Kernel32.GetSystemTimes(out long idle, out long kernel, out long user))
                    return -1;

                if(!HasCpuSample) {
                    LastIdle = idle; LastKernel = kernel; LastUser = user;
                    HasCpuSample = true;
                    return -1;
                }

                // Deltas (the FILETIME values increase monotonically)
                long dIdle = idle - LastIdle;
                long dKernel = kernel - LastKernel;
                long dUser = user - LastUser;
                LastIdle = idle; LastKernel = kernel; LastUser = user;

                long total = dKernel + dUser; // kernel time already includes idle
                if(total <= 0)
                    return -1;

                long busy = total - dIdle;
                if(busy < 0) busy = 0;

                int pct = (int)Math.Round((double)busy / total * 100.0);
                return pct < 0 ? 0 : (pct > 100 ? 100 : pct);

            } catch {
                return -1;
            }
        }

        // Reads physical memory usage. Returns false if unavailable.
        public static bool GetMemory(out double usedGB, out double totalGB, out int percent) {
            usedGB = 0; totalGB = 0; percent = -1;
            try {
                Kernel32.MEMORYSTATUSEX m = new Kernel32.MEMORYSTATUSEX();
                m.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Kernel32.MEMORYSTATUSEX));
                if(!Kernel32.GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0)
                    return false;

                const double giga = 1024.0 * 1024.0 * 1024.0;
                totalGB = m.ullTotalPhys / giga;
                usedGB = (m.ullTotalPhys - m.ullAvailPhys) / giga;
                percent = (int)m.dwMemoryLoad;
                return true;
            } catch {
                return false;
            }
        }

        // What this application itself is costing, in megabytes.
        //
        // Reported because it was claimed and never measured: the README said
        // StarMon "uses a few megabytes", which was an aspiration rather than
        // a figure. It is a WPF application, and once its window has been
        // opened the visual tree and the render surfaces stay resident — on
        // the machine this was developed on that settles at around 250 MB.
        //
        // Two numbers, because they answer different questions. The working
        // set is what the task manager calls memory: the pages actually
        // resident, which Windows will trim under pressure. The private bytes
        // are what has been committed and cannot be shared with another
        // process — the figure that grows if something is genuinely leaking.
        // Watching only the first is how a leak hides behind a trim.
        public static bool GetProcessMemory(out double workingSetMB,
            out double privateMB) {

            workingSetMB = -1;
            privateMB = -1;

            try {

                using(System.Diagnostics.Process self =
                    System.Diagnostics.Process.GetCurrentProcess()) {

                    self.Refresh();

                    const double mega = 1024.0 * 1024.0;
                    workingSetMB = self.WorkingSet64 / mega;
                    privateMB = self.PrivateMemorySize64 / mega;

                    return true;

                }

            } catch {
                return false;
            }

        }

        // Modern power-mode overlay GUIDs set by the Windows power slider
        private static readonly Guid OverlayHighPerf  = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        private static readonly Guid OverlayPowerSaver = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");

        // The selectable Windows power modes (the quick-settings "Power mode")
        public enum PowerMode { Unknown = -1, Balanced = 0, HighPerformance = 1, PowerSaver = 2 }

        // Returns the currently effective Windows power mode
        public static PowerMode GetPowerMode() {
            try {
                if(PowrProf.PowerGetEffectiveOverlayScheme(out Guid overlay) == 0) {
                    if(overlay == OverlayHighPerf) return PowerMode.HighPerformance;
                    if(overlay == OverlayPowerSaver) return PowerMode.PowerSaver;
                    return PowerMode.Balanced;
                }
            } catch { }
            return PowerMode.Unknown;
        }

        // Switches the Windows power mode, exactly like the power slider does.
        // Returns false when the call is unavailable or rejected.
        public static bool SetPowerMode(PowerMode mode) {
            try {
                Guid overlay =
                    mode == PowerMode.HighPerformance ? OverlayHighPerf
                    : mode == PowerMode.PowerSaver ? OverlayPowerSaver
                    : Guid.Empty;
                return PowrProf.PowerSetActiveOverlayScheme(overlay) == 0;
            } catch {
                return false;
            }
        }

        // Returns the friendly name of the active Windows power plan, or null.
        // Checks the modern power-mode overlay first (what the taskbar slider
        // changes), falling back to the classic active scheme's name.
        public static string GetPowerPlanName() {
            try {
                if(PowrProf.PowerGetEffectiveOverlayScheme(out Guid overlay) == 0
                    && overlay != Guid.Empty) {
                    if(overlay == OverlayHighPerf)
                        return "Performance";
                    if(overlay == OverlayPowerSaver)
                        return "Power Saver";
                }
            } catch { }

            IntPtr guidPtr = IntPtr.Zero;
            try {
                if(PowrProf.PowerGetActiveScheme(IntPtr.Zero, out guidPtr) != 0 || guidPtr == IntPtr.Zero)
                    return null;

                uint size = 0;
                PowrProf.PowerReadFriendlyName(IntPtr.Zero, guidPtr, IntPtr.Zero, IntPtr.Zero, null, ref size);
                if(size == 0 || size > 4096)
                    return null;

                byte[] buffer = new byte[size];
                if(PowrProf.PowerReadFriendlyName(IntPtr.Zero, guidPtr, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0)
                    return null;

                string name = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();
                return string.IsNullOrEmpty(name) ? null : name;
            } catch {
                return null;
            } finally {
                if(guidPtr != IntPtr.Zero)
                    Kernel32.LocalFree(guidPtr);
            }
        }

        // Returns how long the system has been running since the last boot
        public static TimeSpan GetUptime() {
            try {
                return TimeSpan.FromMilliseconds(Kernel32.GetTickCount64());
            } catch {
                return TimeSpan.Zero;
            }
        }

        // Formats an uptime span compactly, e.g. "3d 7h 4m"
        public static string FormatUptime(TimeSpan up) {
            if(up.TotalDays >= 1)
                return (int)up.TotalDays + "d " + up.Hours + "h";
            if(up.TotalHours >= 1)
                return up.Hours + "h " + up.Minutes + "m";
            return up.Minutes + "m";
        }

    }

}
