// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;
using StarMon.Library;

namespace StarMon.Hardware {

    // Provides NVIDIA GPU monitoring data
    public static class GpuNvidia {

        // Consolidated snapshot of GPU metrics (each field -1 when unavailable)
        public struct GpuInfo {
            public bool Present;
            public int Load;          // GPU utilization [%]
            public int TempC;         // Core temperature [°C]
            public int CoreMhz;       // Graphics clock [MHz]
            public int MemMhz;        // Memory clock [MHz]
            public int VramUsedMB;    // Used dedicated video memory [MB]
            public int VramTotalMB;   // Total dedicated video memory [MB]
            public int PowerW;        // Board power draw [W] (via NVML), -1 if unknown
            public int PowerLimitW;   // Enforced board power limit [W], -1 if unknown
        }

        #region NVAPI interop
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NvAPI_QueryInterface(uint id);

        // Function ids
        private const uint ID_Initialize               = 0x0150E828;
        private const uint ID_EnumPhysicalGPUs         = 0xE5AC921F;
        private const uint ID_GPU_GetThermalSettings   = 0xE3640A56;
        private const uint ID_GPU_GetDynamicPstatesEx  = 0x60DED2ED;
        private const uint ID_GPU_GetAllClockFreqs     = 0xDCB616C3;
        private const uint ID_GPU_GetMemoryInfo        = 0x07F9B368;

        private const int MAX_PHYSICAL_GPUS = 64;
        private const int THERMAL_TARGET_ALL = 15;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int EnumPhysicalGPUsDelegate([Out] IntPtr[] handles, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetThermalSettingsDelegate(IntPtr gpu, int sensorIndex, ref NvThermalSettings settings);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDynamicPstatesDelegate(IntPtr gpu, ref NvDynamicPstates states);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetClockFrequenciesDelegate(IntPtr gpu, ref NvClockFrequencies freqs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetMemoryInfoDelegate(IntPtr gpu, ref NvMemoryInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvThermalSensor {
            public int Controller;
            public int DefaultMinTemp;
            public int DefaultMaxTemp;
            public int CurrentTemp;
            public int Target;
        }

        // NV_GPU_THERMAL_SETTINGS_V2: the official layout holds exactly
        // 3 sensors; NVAPI validates the version field (size | version)
        // strictly, so a padded struct would be rejected outright
        [StructLayout(LayoutKind.Sequential)]
        private struct NvThermalSettings {
            public uint Version;
            public uint Count;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public NvThermalSensor[] Sensor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvUtilization {
            public uint IsPresent;
            public uint Percentage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvDynamicPstates {
            public uint Version;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public NvUtilization[] Utilization;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvClockDomain {
            public uint IsPresent;
            public uint Frequency; // kHz
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvClockFrequencies {
            public uint Version;
            public uint ClockType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public NvClockDomain[] Domain;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvMemoryInfo {
            public uint Version;
            public uint DedicatedVideoMemory;            // KB
            public uint AvailableDedicatedVideoMemory;   // KB
            public uint SystemVideoMemory;               // KB
            public uint SharedSystemMemory;              // KB
            public uint CurrentAvailableDedicatedVideoMemory; // KB
        }

        // Builds an NVAPI struct version (struct size OR'ed with the version number)
        private static uint Version(int size, int ver) {
            return (uint)size | ((uint)ver << 16);
        }
        #endregion

        // Bound delegates and state
        private static readonly object Lock = new object();
        private static volatile bool Initialized;
        private static bool Available;
        private static IntPtr Gpu;

        private static GetThermalSettingsDelegate FnThermal;
        private static GetDynamicPstatesDelegate FnPstates;
        private static GetClockFrequenciesDelegate FnClocks;
        private static GetMemoryInfoDelegate FnMemory;

        // How many readings apart the video-memory figures are refreshed, and
        // what they held in between. See the comment where they are asked for.
        private const int MemoryEvery = 5;
        private static int MemoryCallsSince = int.MaxValue;
        private static int CachedVramTotalMB;
        private static int CachedVramUsedMB;

        // Whether an NVIDIA GPU was found and NVAPI is usable
        public static bool IsAvailable {
            get { EnsureInitialized(); return Available; }
        }

        // Binds a function id to a managed delegate, or returns null on failure
        private static T GetDelegate<T>(uint id) where T : class {
            try {
                IntPtr ptr = NvAPI_QueryInterface(id);
                if(ptr == IntPtr.Zero)
                    return null;
                return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
            } catch {
                return null;
            }
        }

        // Performs one-time NVAPI initialization and GPU discovery
        private static void EnsureInitialized() {
            if(Initialized)
                return;

            lock(Lock) {
                if(Initialized)
                    return;

                // Switched off by hand, and then not asked again.
                //
                // NVAPI is a hand-bound entry point into the display driver,
                // reached through function ids and structs whose layouts this
                // file declares for itself. Everything it is used for here is
                // a reading — nothing is written to the card — but it is still
                // the one part of this application that calls into a kernel
                // driver's user-mode half on a timer, and a display driver
                // that mishandles a request is a machine that stops.
                //
                // So there is a switch. Anyone whose machine misbehaves can
                // take this out of the picture and keep the rest of the
                // application, which is a far better answer than uninstalling
                // it to find out.
                if(!Config.GpuNvidiaEnabled) {
                    Available = false;
                    Initialized = true;
                    Logger.Info("GpuNvidia",
                        "Not asked: GpuNvidiaEnabled is off in the configuration");
                    return;
                }

                // Note: the flag is only raised once everything below is done
                // (in the finally block), so that a caller passing the
                // unsynchronized fast-path check above never observes a
                // half-initialized state
                try {
                    InitializeDelegate init = GetDelegate<InitializeDelegate>(ID_Initialize);
                    EnumPhysicalGPUsDelegate enumGpus = GetDelegate<EnumPhysicalGPUsDelegate>(ID_EnumPhysicalGPUs);
                    if(init == null || enumGpus == null)
                        return;

                    if(init() != 0)
                        return;

                    IntPtr[] handles = new IntPtr[MAX_PHYSICAL_GPUS];
                    if(enumGpus(handles, out int count) != 0 || count <= 0)
                        return;

                    Gpu = handles[0];

                    FnThermal = GetDelegate<GetThermalSettingsDelegate>(ID_GPU_GetThermalSettings);
                    FnPstates = GetDelegate<GetDynamicPstatesDelegate>(ID_GPU_GetDynamicPstatesEx);
                    FnClocks = GetDelegate<GetClockFrequenciesDelegate>(ID_GPU_GetAllClockFreqs);
                    FnMemory = GetDelegate<GetMemoryInfoDelegate>(ID_GPU_GetMemoryInfo);

                    Available = Gpu != IntPtr.Zero;
                } catch {
                    Available = false;
                } finally {
                    Initialized = true;

                    // A single availability entry, so the log always tells
                    // why the GPU metrics are (or are not) being shown
                    Logger.Info("GpuNvidia", Available ?
                        "NVAPI initialized, GPU metrics available"
                        : "NVAPI unavailable, GPU metrics disabled");
                }
            }
        }

        #region NVML interop (board power draw)
        // NVML (the NVIDIA Management Library, nvml.dll) reliably exposes the GPU
        // board power draw, which NVAPI does not. Bound and used defensively: if
        // nvml.dll is missing or any call fails, the power simply stays unavailable.
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NvmlGetHandle(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NvmlGetPowerUsage(IntPtr device, out uint milliwatts);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit", CallingConvention = CallingConvention.Cdecl)]
        private static extern int NvmlGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);

        private static volatile bool NvmlInitialized;
        private static bool NvmlAvailable;
        private static IntPtr NvmlDevice;

        // Returns the GPU board power draw in watts, or -1 if unavailable
        private static int GetNvmlPowerWatts() {
            try {
                if(!NvmlInitialized)
                    lock(Lock) {
                        if(!NvmlInitialized) {
                            try {
                                if(NvmlInit() == 0 && NvmlGetHandle(0, out NvmlDevice) == 0)
                                    NvmlAvailable = true;
                            } finally {
                                NvmlInitialized = true;
                                Logger.Info("GpuNvidia", NvmlAvailable ?
                                    "NVML initialized, board power available"
                                    : "NVML unavailable, board power disabled");
                            }
                        }
                    }
                if(!NvmlAvailable)
                    return -1;
                if(NvmlGetPowerUsage(NvmlDevice, out uint mw) != 0)
                    return -1;
                int watts = (int)Math.Round(mw / 1000.0);
                return (watts >= 0 && watts < 1000) ? watts : -1;
            } catch {
                NvmlAvailable = false;
                return -1;
            }
        }

        // The power limit the driver is currently enforcing, in watts — the
        // live TGP cap, which is what moves when the firmware's performance
        // profile releases or withholds the card's headroom. -1 if unknown.
        private static int GetNvmlPowerLimitWatts() {
            try {
                if(!NvmlAvailable)
                    return -1;
                if(NvmlGetEnforcedPowerLimit(NvmlDevice, out uint mw) != 0)
                    return -1;
                int watts = (int)Math.Round(mw / 1000.0);
                return (watts > 0 && watts < 1000) ? watts : -1;
            } catch {
                return -1;
            }
        }
        #endregion

        // The last reading taken, so a caller that has decided not to disturb
        // a sleeping card can still say what it was doing when it was awake
        private static GpuInfo LastKnown;
        private static bool HasLastKnown;

        // Whether the card is present, without asking the card anything
        public static bool WasPresent {
            get { return HasLastKnown && LastKnown.Present; }
        }

        // The most recent reading, without taking a new one.
        //
        // Querying the driver wakes a discrete card that has powered itself
        // down, so a caller that only wants to keep a panel populated while
        // the machine is on battery asks for this instead. The readings it
        // returns are stale by definition; the alternative is spending
        // battery to refresh numbers nobody is looking at.
        public static GpuInfo GetLastKnown() {

            if(HasLastKnown) {
                GpuInfo stale = LastKnown;

                // The live figures are not carried forward: a load and a power
                // draw from ten minutes ago shown as current is worse than an
                // honest blank. What survives is the fact of the card and its
                // fixed properties.
                stale.Load = -1;
                stale.TempC = -1;
                stale.CoreMhz = -1;
                stale.MemMhz = -1;
                stale.PowerW = -1;
                stale.VramUsedMB = -1;
                return stale;
            }

            return new GpuInfo {
                Load = -1, TempC = -1, CoreMhz = -1, MemMhz = -1,
                VramUsedMB = -1, VramTotalMB = -1, PowerW = -1
            };

        }

        // Reads the current GPU metrics; missing values are returned as -1
        public static GpuInfo Get() {
            GpuInfo info = new GpuInfo {
                Load = -1, TempC = -1, CoreMhz = -1, MemMhz = -1,
                VramUsedMB = -1, VramTotalMB = -1, PowerW = -1
            };

            EnsureInitialized();
            if(!Available)
                return info;

            info.Present = true;

            // Temperature
            try {
                if(FnThermal != null) {
                    NvThermalSettings t = new NvThermalSettings {
                        Sensor = new NvThermalSensor[3],
                        Version = Version(8 + 3 * 20, 2)
                    };
                    if(FnThermal(Gpu, THERMAL_TARGET_ALL, ref t) == 0 && t.Count > 0)
                        info.TempC = t.Sensor[0].CurrentTemp;
                }
            } catch { }

            // Utilization
            try {
                if(FnPstates != null) {
                    NvDynamicPstates p = new NvDynamicPstates {
                        Utilization = new NvUtilization[8],
                        Version = Version(8 + 8 * 8, 1)
                    };
                    if(FnPstates(Gpu, ref p) == 0 && (p.Utilization[0].IsPresent & 1) != 0)
                        info.Load = (int)p.Utilization[0].Percentage;
                }
            } catch { }

            // Clocks
            try {
                if(FnClocks != null) {
                    NvClockFrequencies c = new NvClockFrequencies {
                        Domain = new NvClockDomain[32],
                        Version = Version(8 + 32 * 8, 3), // V3: same layout, accepted by modern drivers
                        ClockType = 0 // Current frequency
                    };
                    if(FnClocks(Gpu, ref c) == 0) {
                        if((c.Domain[0].IsPresent & 1) != 0)  // Graphics
                            info.CoreMhz = (int)(c.Domain[0].Frequency / 1000);
                        if((c.Domain[4].IsPresent & 1) != 0)  // Memory
                            info.MemMhz = (int)(c.Domain[4].Frequency / 1000);
                    }
                }
            } catch { }

            // Dedicated video memory.
            //
            // Asked for a fraction as often as the rest, and held in between.
            //
            // Temperature and load are why anyone watches a graphics card and
            // they are worth a reading a second. How much video memory is in
            // use is a figure on a details row; asking for it five times as
            // often as anybody reads it buys nothing.
            //
            // What it costs is not nothing. This is a call into the display
            // driver, through a function id and a struct layout declared by
            // hand in this file, on a timer, for the life of the process — and
            // the deprecated form of it at that, since newer drivers publish
            // GetMemoryInfoEx instead. A machine here stopped with a black
            // screen naming video memory, with crash dumps switched off and so
            // nothing kept to say what did it. That is not evidence against
            // this call. It is a reason not to make it eight hundred times an
            // hour for a number nobody is looking at.
            if(MemoryCallsSince++ >= MemoryEvery) {

                MemoryCallsSince = 0;

                try {
                    if(FnMemory != null) {
                        NvMemoryInfo m = new NvMemoryInfo {
                            Version = Version(4 + 5 * 4, 2)
                        };
                        if(FnMemory(Gpu, ref m) == 0 && m.DedicatedVideoMemory > 0) {
                            CachedVramTotalMB = (int)(m.DedicatedVideoMemory / 1024);
                            long used = (long)m.DedicatedVideoMemory
                                - m.CurrentAvailableDedicatedVideoMemory;
                            if(used < 0) used = 0;
                            CachedVramUsedMB = (int)(used / 1024);
                        }
                    }
                } catch { }

            }

            info.VramTotalMB = CachedVramTotalMB;
            info.VramUsedMB = CachedVramUsedMB;

            // Board power draw and the enforced limit (NVML)
            info.PowerW = GetNvmlPowerWatts();
            info.PowerLimitW = GetNvmlPowerLimitWatts();

            LastKnown = info;
            HasLastKnown = true;

            return info;
        }

    }

}
