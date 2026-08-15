// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.IO;
using StarMon.External;
using StarMon.Library;

namespace StarMon.Hardware {

    // Provides the temperature of the system NVMe drive, in degrees Celsius
    public static class DiskTemperature {

        // IOCTL_STORAGE_QUERY_PROPERTY = CTL_CODE(0x2D, 0x500, METHOD_BUFFERED, FILE_ANY_ACCESS)
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        // STORAGE_PROPERTY_ID / STORAGE_QUERY_TYPE values
        private const int StorageDeviceProtocolSpecificProperty = 50;
        private const int PropertyStandardQuery = 0;

        // STORAGE_PROTOCOL_TYPE / data type values
        private const int ProtocolTypeNvme = 3;
        private const uint NVMeDataTypeLogPage = 2;
        private const uint NVMeLogPageHealthInfo = 0x02;

        // Buffer geometry
        private const int HeaderSize = 8;          // STORAGE_PROPERTY_QUERY header
        private const int ProtocolDataSize = 40;   // STORAGE_PROTOCOL_SPECIFIC_DATA
        private const int LogSize = 512;           // NVMe log page length
        private const int BufferSize = HeaderSize + ProtocolDataSize + LogSize;

        // Offset of the NVMe log within the buffer
        private const int DataOffset = HeaderSize + ProtocolDataSize; // 48

        private const uint GENERIC_ZERO = 0;

        // Cached index of the drive that answered, to avoid re-probing every time
        private static int KnownDrive = -2; // -2 = not probed yet, -1 = none

        // How many probes in a row have to come back with nothing before this
        // machine is taken to have no NVMe drive to ask.
        //
        // One was enough, and one is the wrong number. A drive momentarily
        // busy — which is a thing drives are — failed the probe, and a single
        // failure wrote the feature off for the life of the process: the
        // reading vanished from the interface and the log said "disk
        // temperature disabled" with no way back short of restarting.
        //
        // Three runs is the same rule the published sensors and the thermal
        // zones already use for the same question, and for the same reason.
        private const int GiveUpAfter = 3;
        private static int EmptyRuns;

        // Returns the NVMe drive temperature in °C, or -1 if unavailable
        public static int GetTemperature() {
            try {
                if(KnownDrive >= 0) {
                    int t = Read(KnownDrive);
                    if(t > 0) {
                        EmptyRuns = 0;
                        return t;
                    }
                    KnownDrive = -2; // Lost it; re-probe next time
                }

                if(KnownDrive == -1)
                    return -1;

                // Probe the first few physical drives once to find an NVMe SSD
                for(int i = 0; i < 4; i++) {
                    int t = Read(i);
                    if(t > 0) {
                        KnownDrive = i;
                        EmptyRuns = 0;
                        Logger.Info("DiskTemp", "NVMe health log found on physical drive " + i);
                        return t;
                    }
                }

                if(++EmptyRuns >= GiveUpAfter) {
                    KnownDrive = -1;
                    Logger.Info("DiskTemp",
                        "No NVMe drive answered the health-log query, disk temperature disabled",
                        "after " + EmptyRuns + " attempts");
                }

                return -1;
            } catch {
                return -1;
            }
        }

        // Queries a single physical drive, returning its temperature or -1
        private static int Read(int driveIndex) {
            IntPtr handle = IntPtr.Zero;
            try {
                handle = Kernel32.CreateFile(
                    @"\\.\PhysicalDrive" + driveIndex,
                    GENERIC_ZERO,                                  // No access needed for a query
                    FileShare.ReadWrite,
                    IntPtr.Zero,
                    FileMode.Open,                                 // OPEN_EXISTING
                    0,
                    IntPtr.Zero);

                // INVALID_HANDLE_VALUE == -1
                if(handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return -1;

                byte[] buffer = new byte[BufferSize];

                // STORAGE_PROPERTY_QUERY
                WriteInt(buffer, 0, StorageDeviceProtocolSpecificProperty);
                WriteInt(buffer, 4, PropertyStandardQuery);

                // STORAGE_PROTOCOL_SPECIFIC_DATA (starts at offset 8)
                WriteInt(buffer, 8, ProtocolTypeNvme);              // ProtocolType
                WriteUInt(buffer, 12, NVMeDataTypeLogPage);         // DataType
                WriteUInt(buffer, 16, NVMeLogPageHealthInfo);       // ProtocolDataRequestValue
                WriteUInt(buffer, 20, 0);                           // ProtocolDataRequestSubValue
                WriteUInt(buffer, 24, ProtocolDataSize);            // ProtocolDataOffset (rel. to this struct)
                WriteUInt(buffer, 28, LogSize);                     // ProtocolDataLength

                if(!Kernel32.DeviceIoControl(
                        handle, IOCTL_STORAGE_QUERY_PROPERTY,
                        buffer, BufferSize, buffer, BufferSize,
                        out uint _, IntPtr.Zero))
                    return -1;

                // NVMe Health log: bytes 1-2 hold the composite temperature in Kelvin
                int kelvin = buffer[DataOffset + 1] | (buffer[DataOffset + 2] << 8);
                if(kelvin <= 0)
                    return -1;

                int celsius = kelvin - 273;
                return (celsius > 0 && celsius < 120) ? celsius : -1;

            } catch {
                return -1;
            } finally {
                if(handle != IntPtr.Zero && handle != new IntPtr(-1))
                    Kernel32.CloseHandle(handle);
            }
        }

        // Little-endian writers for building the request buffer
        private static void WriteInt(byte[] b, int offset, int value) {
            b[offset] = (byte)value;
            b[offset + 1] = (byte)(value >> 8);
            b[offset + 2] = (byte)(value >> 16);
            b[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt(byte[] b, int offset, uint value) {
            b[offset] = (byte)value;
            b[offset + 1] = (byte)(value >> 8);
            b[offset + 2] = (byte)(value >> 16);
            b[offset + 3] = (byte)(value >> 24);
        }

    }

}
