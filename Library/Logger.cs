// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StarMon.Library
{

    // Log severity levels
    public enum LogLevel
    {
        Debug,      // Detailed debugging information
        Info,       // General information
        Warning,    // Warning messages
        Error,      // Error messages
        BiosCall,   // BIOS WMI calls
        BiosResult, // BIOS call results
        EcRead,     // EC register reads
        EcWrite,    // EC register writes
        Hardware,   // Hardware state changes
        Config,     // Configuration changes
        Gui         // GUI events
    }

    // Represents a single log entry
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string Description { get; set; }
        public int RepeatCount { get; set; }

        public LogEntry(LogLevel level, string source, string message, string details = null, string description = null)
        {
            Timestamp = DateTime.Now;
            Level = level;
            Source = source;
            Message = message;
            Details = details;
            Description = description;
            RepeatCount = 1;
        }

        // Gets a unique key for stacking identical entries
        public string GetStackKey()
        {
            return $"{Level}|{Source}|{Message}|{Details}";
        }

        public override string ToString()
        {
            string result = $"[{Timestamp:HH:mm:ss.fff}] [{Level,-10}] [{Source,-12}] {Message}";
            if (!string.IsNullOrEmpty(Details))
                result += $" | {Details}";
            if (RepeatCount > 1)
                result += $" (x{RepeatCount})";
            return result;
        }
    }

    // Provides human-readable descriptions for operations
    public static class LogDescriptions
    {

        // BIOS commandType descriptions (from BiosCtl.cs)
        private static readonly Dictionary<int, string> BiosCommands = new Dictionary<int, string> {
            // Keyboard commands (Cmd.Keyboard = 0x20009)
            { 0x01, "Check keyboard backlight support" },
            { 0x02, "Get keyboard color table" },
            { 0x03, "Set keyboard color table" },
            { 0x04, "Get keyboard backlight state" },
            { 0x05, "Set keyboard backlight" },
            { 0x06, "Get LED animation table" },
            { 0x07, "Set LED animation table" },

            // Legacy commands (Cmd.Legacy = 0x00001)
            { 0x0F, "Get power adapter status" },
            { 0x10, "Get born-on date" },
            { 0x52, "Get/set GPU mode" },

            // Default commands (Cmd.Default = 0x20008)
            { 0x18, "Check memory overclock support" },
            { 0x19, "Set memory XMP profile" },
            { 0x1A, "Set fan performance mode" },
            { 0x21, "Get GPU power settings" },
            { 0x22, "Change GPU power settings" },
            { 0x23, "Read temperature sensor" },
            { 0x26, "Get max fan speed state" },
            { 0x27, "Set max fan speed mode" },
            { 0x28, "Get system design data" },
            { 0x29, "Set CPU power limits" },
            { 0x2B, "Get keyboard type" },
            { 0x2C, "Query fan type" },
            { 0x2D, "Get fan levels" },
            { 0x2E, "Set fan levels" },
            { 0x2F, "Get fan speed table" },
            { 0x31, "Set idle mode" },
            { 0x32, "Set fan speed table" },
            { 0x35, "Check overclock/undervolt support" }
        };

        // EC register descriptions (from EcData.cs)
        private static readonly Dictionary<byte, string> EcRegisters = new Dictionary<byte, string> {
            // Fan speed control
            { 0x2C, "Left fan target speed [%]" },
            { 0x2D, "Right fan target speed [%]" },
            { 0x2E, "Left fan current speed [%]" },
            { 0x2F, "Right fan current speed [%]" },
            { 0x34, "Left fan target [krpm]" },
            { 0x35, "Right fan target [krpm]" },

            // Temperature sensors
            { 0x47, "Temperature sensor 2 [°C]" },
            { 0x48, "Temperature sensor 3 [°C]" },
            { 0x49, "Temperature sensor 4 [°C]" },
            { 0x4A, "IR temperature sensor [°C]" },
            { 0x4B, "Temperature sensor 5 [°C]" },
            { 0x57, "CPU temperature [°C]" },
            { 0x58, "General temperature [°C]" },
            { 0x59, "Temperature sensor 1 [°C]" },
            { 0xB7, "GPU temperature [°C]" },

            // Fan RPM readings
            { 0xB0, "Left fan RPM (low byte)" },
            { 0xB1, "Left fan RPM (high byte)" },
            { 0xB2, "Right fan RPM (low byte)" },
            { 0xB3, "Right fan RPM (high byte)" },

            // Fan control
            { 0x5F, "HID disable" },
            { 0x62, "Manual fan control" },
            { 0x63, "Fan auto countdown [s]" },
            { 0x95, "Performance mode (OMEN)" },
            { 0xEC, "Max fan speed toggle" },
            { 0xF4, "Fan toggle" },
            { 0xF9, "Thermal threshold state" },

            // Battery and power
            { 0x96, "Battery charge level" },
            { 0xBA, "Minimum power state" },
            { 0xBB, "Maximum power state" },

            // Keyboard
            { 0xA0, "Last hotkey pressed" },
            { 0xA2, "HID related" }
        };

        // Gets human-readable description for BIOS command
        public static string GetBiosDescription(int command)
        {
            if (BiosCommands.TryGetValue(command, out string desc))
                return desc;
            return $"Unknown BIOS command (0x{command:X2})";
        }

        // Gets human-readable description for EC register
        public static string GetEcDescription(byte register)
        {
            if (EcRegisters.TryGetValue(register, out string desc))
                return desc;
            return $"Unknown EC register (0x{register:X2})";
        }
    }

    // Event args for log events
    public class LogEventArgs : EventArgs
    {
        public LogEntry Entry { get; }
        public LogEventArgs(LogEntry entry)
        {
            Entry = entry;
        }
    }

    // Central logging system for StarMon
    // Thread-safe, event-based architecture
    public static class Logger
    {

        #region Fields
        private static readonly object _lock = new object();
        private static readonly List<LogEntry> _entries = new List<LogEntry>();
        private static readonly int _maxEntries = 5000;
        private static bool _isEnabled = true;
        private static bool _fileLoggingEnabled = false;
        private static string _logFilePath;
        private static StreamWriter _fileWriter;

        // Bytes written to the current log file, tracked so the file can be
        // rolled over without asking the filesystem for its size every line
        private static long _fileBytes;
        #endregion

        #region Events
        // Fired when a new log entry is added
        public static event EventHandler<LogEventArgs> LogAdded;

        // Fired when logs are cleared
        public static event EventHandler LogsCleared;
        #endregion

        #region Properties
        // Gets or sets whether logging is enabled
        public static bool IsEnabled
        {
            get { lock (_lock) { return _isEnabled; } }
            set { lock (_lock) { _isEnabled = value; } }
        }

        // Gets the current log entry count
        public static int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }
        #endregion

        #region Logging Methods
        // Adds a log entry with optional stacking for identical entries
        public static void Log(LogLevel level, string source, string message, string details = null, string description = null)
        {
            if (!_isEnabled) return;

            var entry = new LogEntry(level, source, message, details, description);
            bool isStacked = false;
            // The entry that will be reported to listeners (determined inside the lock
            // so the event is never raised with an index into a list another thread may
            // have mutated in the meantime)
            LogEntry reportEntry = entry;

            lock (_lock)
            {
                // Check if last entry is identical for stacking
                if (_entries.Count > 0)
                {
                    var lastEntry = _entries[_entries.Count - 1];
                    if (lastEntry.GetStackKey() == entry.GetStackKey())
                    {
                        // Stack the entry
                        lastEntry.RepeatCount++;
                        lastEntry.Timestamp = DateTime.Now;
                        isStacked = true;
                        reportEntry = lastEntry;
                    }
                }

                if (!isStacked)
                {
                    // Remove oldest entries if at capacity
                    if (_entries.Count >= _maxEntries)
                    {
                        int removeCount = _entries.Count - _maxEntries + 1;
                        if (removeCount > 0)
                            _entries.RemoveRange(0, removeCount);
                    }
                    _entries.Add(entry);
                }

                // Write to file if enabled
                if (_fileLoggingEnabled && _fileWriter != null)
                {
                    try
                    {
                        string line = entry.ToString();
                        _fileWriter.WriteLine(line);
                        _fileBytes += line.Length + Environment.NewLine.Length;

                        // Roll the file over once it grows past the limit, so
                        // an application left running for weeks cannot quietly
                        // fill the disk
                        if (_fileBytes >= Config.LogFileMaxBytes)
                            RotateFile();
                    }
                    catch { }
                }
            }

            // Fire event (outside lock to prevent deadlocks)
            LogAdded?.Invoke(null, new LogEventArgs(reportEntry));
        }

        // Convenience methods
        public static void Debug(string source, string message, string details = null)
            => Log(LogLevel.Debug, source, message, details);

        public static void Info(string source, string message, string details = null)
            => Log(LogLevel.Info, source, message, details);

        public static void Warning(string source, string message, string details = null)
            => Log(LogLevel.Warning, source, message, details);

        public static void Error(string source, string message, string details = null)
            => Log(LogLevel.Error, source, message, details);

        // Deduplication state: the last value seen for a key, plus how many
        // identical entries have been dropped since one was last emitted
        private class DedupState
        {
            public string Value;
            public int Suppressed;
        }

        private static readonly Dictionary<string, DedupState> _dedupCache =
            new Dictionary<string, DedupState>();

        // Decides whether an entry should be emitted, suppressing runs of
        // identical values. The number of entries dropped since the last
        // emitted one is reported back, so the log can say how long a value
        // held steady rather than silently pretending nothing happened.
        private static bool ShouldLog(string key, string value, out int suppressed)
        {
            lock (_lock)
            {
                if (_dedupCache.TryGetValue(key, out DedupState state))
                {
                    if (state.Value == value)
                    {
                        state.Suppressed++;
                        suppressed = 0;
                        return false;
                    }

                    suppressed = state.Suppressed;
                    state.Value = value;
                    state.Suppressed = 0;
                    return true;
                }

                _dedupCache[key] = new DedupState { Value = value, Suppressed = 0 };
                suppressed = 0;
                return true;
            }
        }

        // Appends a note about how many identical entries preceded this one,
        // so a steady value does not read as if it were only sampled once
        private static string WithSuppressed(string details, int suppressed)
        {
            return suppressed > 0
                ? (details ?? "") + $" (unchanged for {suppressed} prior reads)"
                : details;
        }

        public static void BiosCall(int command, string parameters = null)
        {
            if (!Config.LogVerbose) return;

            string desc = LogDescriptions.GetBiosDescription(command);
            // Deduplicate based on command parameters
            if (!ShouldLog($"BIOS_C_{command}", parameters ?? string.Empty, out int n)) return;
            Log(LogLevel.BiosCall, "BIOS", $"Cmd=0x{command:X2}", WithSuppressed(parameters, n), desc);
        }

        public static void BiosResult(int command, string result)
        {
            if (!Config.LogVerbose) return;

            string desc = LogDescriptions.GetBiosDescription(command);
            // Deduplicate based on result data
            if (!ShouldLog($"BIOS_R_{command}", result ?? string.Empty, out int n)) return;
            Log(LogLevel.BiosResult, "BIOS", $"Cmd=0x{command:X2}", WithSuppressed(result, n), desc);
        }

        public static void EcRead(byte register, byte value)
        {
            if (!Config.LogVerbose) return;
            if (!ShouldLog($"EC_R_{register}", value.ToString(), out int n)) return;

            string desc = LogDescriptions.GetEcDescription(register);
            string message = $"Read 0x{register:X2}";
            string details = $"Value: 0x{value:X2} ({value})";
            Log(LogLevel.EcRead, "EC", message, WithSuppressed(details, n), desc);
        }

        public static void EcReadWord(byte register, ushort value)
        {
            if (!Config.LogVerbose) return;
            if (!ShouldLog($"EC_RW_{register}", value.ToString(), out int n)) return;

            string desc = LogDescriptions.GetEcDescription(register);
            string message = $"Read word 0x{register:X2}";
            string details = $"Value: 0x{value:X4} ({value})";
            Log(LogLevel.EcRead, "EC", message, WithSuppressed(details, n), desc);
        }

        public static void EcWrite(byte register, byte value)
        {
            if (!Config.LogVerbose) return;

            // Writing the same value repeatedly (a fan control loop re-applying
            // its setting, say) says nothing new, so runs are collapsed the
            // same way reads are
            if (!ShouldLog($"EC_W_{register}", value.ToString(), out int n)) return;

            string desc = LogDescriptions.GetEcDescription(register);
            string message = $"Write 0x{register:X2}";
            string details = $"Value: 0x{value:X2} ({value})";
            Log(LogLevel.EcWrite, "EC", message, WithSuppressed(details, n), desc);
        }

        public static void EcWriteWord(byte register, ushort value)
        {
            if (!Config.LogVerbose) return;
            if (!ShouldLog($"EC_WW_{register}", value.ToString(), out int n)) return;

            string desc = LogDescriptions.GetEcDescription(register);
            string message = $"Write word 0x{register:X2}";
            string details = $"Value: 0x{value:X4} ({value})";
            Log(LogLevel.EcWrite, "EC", message, WithSuppressed(details, n), desc);
        }

        // A register exchange that failed every retry, and has kept failing:
        // a controller that has genuinely stopped answering is exactly what
        // the log exists to reveal. Never gated behind the verbose setting.
        public static void EcFail(byte register, bool isWrite, int attempts)
        {
            string desc = LogDescriptions.GetEcDescription(register);
            string message = $"{(isWrite ? "Write" : "Read")} 0x{register:X2} failing";
            string details = $"Gave up after {attempts} attempt{(attempts == 1 ? "" : "s")}, repeatedly";
            Log(LogLevel.Warning, "EC", message, details, desc);
        }

        // A single failed exchange that has not yet become a pattern. The
        // Embedded Controller is shared with the firmware and momentarily busy
        // often — a lone miss on a register that answers again next tick is
        // noise, not a fault, so it is logged at the quiet EC-traffic level
        // (only visible with the EC filter on) rather than as a warning.
        public static void EcTransient(byte register, bool isWrite)
        {
            if (!Config.LogVerbose) return;
            if (!ShouldLog($"EC_TF_{register}", isWrite.ToString(), out int n)) return;

            string desc = LogDescriptions.GetEcDescription(register);
            string message = $"{(isWrite ? "Write" : "Read")} 0x{register:X2} retried";
            Log(LogLevel.EcRead, "EC", message, WithSuppressed("Transient, recovered", n), desc);
        }

        public static void Hardware(string component, string message, string details = null)
            => Log(LogLevel.Hardware, component, message, details);

        public static void Gui(string component, string message, string details = null)
            => Log(LogLevel.Gui, component, message, details);

        public static void ConfigChange(string setting, string oldValue, string newValue)
            => Log(LogLevel.Config, "Config", $"{setting} changed", $"{oldValue} → {newValue}");
        #endregion

        #region Query Methods
        // Gets all log entries
        public static List<LogEntry> GetAll()
        {
            lock (_lock)
            {
                return new List<LogEntry>(_entries);
            }
        }

        // Gets log entries filtered by level
        public static List<LogEntry> GetByLevel(params LogLevel[] levels)
        {
            var levelSet = new HashSet<LogLevel>(levels);
            lock (_lock)
            {
                return _entries.FindAll(e => levelSet.Contains(e.Level));
            }
        }

        // Gets log entries containing search text
        public static List<LogEntry> Search(string text)
        {
            if (string.IsNullOrEmpty(text)) return GetAll();
            lock (_lock)
            {
                return _entries.FindAll(e =>
                    e.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (e.Details != null && e.Details.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    e.Source.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        // Clears all log entries
        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
            LogsCleared?.Invoke(null, EventArgs.Empty);
        }
        #endregion

        #region Export Methods
        // Exports logs to a string
        public static string Export(List<LogEntry> entries = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"StarMon Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            var toExport = entries ?? GetAll();
            foreach (var entry in toExport)
            {
                sb.AppendLine(entry.ToString());
            }

            return sb.ToString();
        }

        // Exports logs to a file
        public static bool ExportToFile(string filePath, List<LogEntry> entries = null)
        {
            try
            {
                File.WriteAllText(filePath, Export(entries), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region File Logging
        // Enables file logging
        public static void EnableFileLogging(string filePath)
        {
            bool opened = false;
            string failure = null;

            lock (_lock)
            {
                try
                {
                    _logFilePath = filePath;
                    _fileWriter = new StreamWriter(filePath, true, Encoding.UTF8);
                    _fileWriter.AutoFlush = true;
                    _fileLoggingEnabled = true;

                    try { _fileBytes = new FileInfo(filePath).Length; } catch { _fileBytes = 0; }
                    if (_fileBytes >= Config.LogFileMaxBytes)
                        RotateFile();

                    opened = true;
                }
                catch (Exception ex)
                {
                    _fileLoggingEnabled = false;
                    failure = ex.Message;
                }
            }

            // Reported outside the lock: Log() raises an event whose listeners
            // are none of this class's business, and holding the log lock
            // across arbitrary handler code is how deadlocks are built
            if (opened)
                Log(LogLevel.Info, "Logger", "File logging enabled", filePath);
            else
                Log(LogLevel.Error, "Logger", "Failed to enable file logging", failure);
        }

        // Rolls the current log file over to a single ".1" backup, replacing
        // any previous one. Caller must hold the lock.
        private static void RotateFile()
        {
            try
            {
                _fileWriter.Flush();
                _fileWriter.Close();
                _fileWriter.Dispose();
            }
            catch { }
            _fileWriter = null;

            try
            {
                string backup = _logFilePath + ".1";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(_logFilePath, backup);
            }
            catch { }

            try
            {
                _fileWriter = new StreamWriter(_logFilePath, false, Encoding.UTF8);
                _fileWriter.AutoFlush = true;
                _fileBytes = 0;
            }
            catch
            {
                _fileLoggingEnabled = false;
            }
        }

        // Disables file logging
        public static void DisableFileLogging()
        {
            lock (_lock)
            {
                _fileLoggingEnabled = false;
                if (_fileWriter != null)
                {
                    try
                    {
                        _fileWriter.Close();
                        _fileWriter.Dispose();
                    }
                    catch { }
                    _fileWriter = null;
                }
            }
        }
        #endregion

    }

}
