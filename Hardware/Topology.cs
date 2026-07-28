// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StarMon.External;

namespace StarMon.Hardware.Cpu {

    // Provides processor-core affinity masks
    public static class Topology {

        // RelationProcessorCore == 0 in LOGICAL_PROCESSOR_RELATIONSHIP
        private const int RelationProcessorCore = 0;

        // Size of one SYSTEM_LOGICAL_PROCESSOR_INFORMATION record on x64:
        // ULONG_PTR ProcessorMask (8) + Relationship (4) + padding (4) + union (16)
        private const int RecordSize = 32;

        private static ulong[] Cached;

        // Returns an affinity mask for each physical core (cached after first call)
        public static ulong[] GetPhysicalCoreMasks() {
            if(Cached != null)
                return Cached;

            try {
                uint length = 0;
                // First call obtains the required buffer length
                Kernel32.GetLogicalProcessorInformation(IntPtr.Zero, ref length);
                if(length == 0 || length > 1024 * 1024)
                    return Cached = Fallback();

                IntPtr buffer = Marshal.AllocHGlobal((int)length);
                try {
                    if(!Kernel32.GetLogicalProcessorInformation(buffer, ref length))
                        return Cached = Fallback();

                    var masks = new List<ulong>();
                    int count = (int)(length / RecordSize);
                    for(int i = 0; i < count; i++) {
                        int offset = i * RecordSize;
                        ulong mask = (ulong)Marshal.ReadInt64(buffer, offset);
                        int relationship = Marshal.ReadInt32(buffer, offset + 8);
                        if(relationship == RelationProcessorCore && mask != 0)
                            masks.Add(mask);
                    }

                    return Cached = masks.Count > 0 ? masks.ToArray() : Fallback();
                } finally {
                    Marshal.FreeHGlobal(buffer);
                }
            } catch {
                return Cached = Fallback();
            }
        }

        // One mask per logical processor, used when topology cannot be determined
        private static ulong[] Fallback() {
            int n = Environment.ProcessorCount;
            if(n < 1) n = 1;
            if(n > 64) n = 64;
            ulong[] masks = new ulong[n];
            for(int i = 0; i < n; i++)
                masks[i] = 1UL << i;
            return masks;
        }

    }

}
