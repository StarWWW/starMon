// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Reflection;
using StarMon.Library;

namespace StarMon.Test {

    // Checks the configuration for the mistakes that do not announce
    // themselves: a setting the reader knows about but the writer does not
    // (so it silently reverts on the next save), and values that contradict
    // one another badly enough to break the hardware protocol.
    public static class TestConfig {

        public static void Run() {

            SelfTest.Group("Configuration");

            TestSettingsRoundTrip();
            TestEverySettingIsPersisted();
            TestTemplateDocumentsEverySetting();
            TestMutexTimeoutCoversWorstCase();
            TestThermalThresholdsMakeSense();
            TestFanLevelRange();

        }

        // Every setting the reader parses has to be one the writer emits.
        // Otherwise a value the user set in the file survives exactly until
        // the application next saves, and then vanishes without a word.
        private static void TestSettingsRoundTrip() {

            string source = ReadConfigSource();

            if(source == null) {
                // The source file is only there in a development tree; there
                // is nothing to check in a deployed copy, and nothing wrong
                SelfTest.Check(true,
                    "configuration source not present, round-trip check skipped");
                return;
            }

            List<string> read = ExtractKeys(source, "XmlPrefix + \"", "Get");
            List<string> written = ExtractKeys(source, "XmlPrefix + \"", "Set");

            List<string> orphans = new List<string>();
            foreach(string key in read)
                if(!written.Contains(key))
                    orphans.Add(key);

            SelfTest.Check(orphans.Count == 0,
                orphans.Count == 0
                    ? "every setting that is read is also written back ("
                        + read.Count + " settings)"
                    : "read but never written back: "
                        + string.Join(", ", orphans.ToArray()));

        }

        // Settings that live in the class but are not read or written by name.
        // Everything here is either a runtime fact about this process rather
        // than a preference, or a collection with a block of its own.
        private static readonly string[] NotSettings = {
            "AppFile", "AppName", "AppVersion", "AppProcessId",
            "EnvVarSelfName", "FilePath", "LockNameMux", "PathTemp",
            "OnlyOnceFileExt", "OnlyOncePath", "TaskRunPath", "Task",

            // The loaded dictionary and the list of languages that can be
            // chosen; what is saved is the choice, under Language
            "Locale", "LanguageNames"
        };

        // The gap the round-trip check above cannot see.
        //
        // That one compares the reader's keys against the writer's, both taken
        // from calls already present in Config.cs — so a setting that appears
        // in neither produces no key on either side and looks like agreement.
        // UpdateRecordInterval shipped exactly that way: settable from the
        // interface, used on every tick, and written to the configuration file
        // by nothing, so a changed value lasted until the application closed.
        //
        // This one starts from the fields instead, which is the list a new
        // setting is actually added to.
        private static void TestEverySettingIsPersisted() {

            string source = ReadConfigSource();

            if(source == null) {
                SelfTest.Check(true,
                    "configuration source not present, persistence check skipped");
                return;
            }

            string loading = Region(source, "#region Configuration Retrieval");
            string saving = Region(source, "#region Configuration Saving");

            if(loading == null || saving == null) {
                SelfTest.Check(false,
                    "the loading and saving regions could not be found in "
                        + "Config.cs; this check needs them to tell the two apart");
                return;
            }

            List<string> unpersisted = new List<string>();
            int checked_ = 0;

            foreach(FieldInfo field in typeof(Config).GetFields(
                BindingFlags.Public | BindingFlags.Static)) {

                // Constants and read-only fields are not preferences
                if(field.IsLiteral || field.IsInitOnly)
                    continue;

                if(Array.IndexOf(NotSettings, field.Name) >= 0)
                    continue;

                checked_++;

                if(!Mentions(loading, field.Name) || !Mentions(saving, field.Name))
                    unpersisted.Add(field.Name);

            }

            SelfTest.Check(unpersisted.Count == 0,
                unpersisted.Count == 0
                    ? "every setting is both loaded and saved ("
                        + checked_ + " settings)"
                    : "settable but not persisted: "
                        + string.Join(", ", unpersisted.ToArray()));

        }

        // Whether a region of source refers to an identifier, as a whole word
        private static bool Mentions(string source, string name) {

            int at = 0;

            while((at = source.IndexOf(name, at, StringComparison.Ordinal)) >= 0) {

                bool leftClear = at == 0
                    || !(char.IsLetterOrDigit(source[at - 1]) || source[at - 1] == '_');

                int after = at + name.Length;
                bool rightClear = after >= source.Length
                    || !(char.IsLetterOrDigit(source[after]) || source[after] == '_');

                if(leftClear && rightClear)
                    return true;

                at = after;

            }

            return false;

        }

        // The text of one #region, up to its matching #endregion
        private static string Region(string source, string marker) {

            int start = source.IndexOf(marker, StringComparison.Ordinal);
            if(start < 0)
                return null;

            int end = source.IndexOf("#endregion", start, StringComparison.Ordinal);
            return end < 0 ? source.Substring(start)
                : source.Substring(start, end - start);

        }

        // The shipped template is where a setting is discoverable and where it
        // is explained. One the code reads but the template never mentions is
        // effectively undocumented: it exists, but nobody editing the file by
        // hand would know to write it.
        private static void TestTemplateDocumentsEverySetting() {

            string source = ReadConfigSource();
            string template = ReadTemplate();

            if(source == null || template == null) {
                SelfTest.Check(true,
                    "configuration sources not present, template check skipped");
                return;
            }

            List<string> read = ExtractKeys(source, "XmlPrefix + \"", "Get");
            List<string> undocumented = new List<string>();

            foreach(string key in read)
                if(template.IndexOf("<" + key + ">", StringComparison.Ordinal) < 0)
                    undocumented.Add(key);

            SelfTest.Check(undocumented.Count == 0,
                undocumented.Count == 0
                    ? "every setting is documented in the template ("
                        + read.Count + " settings)"
                    : "missing from StarMon.xml: "
                        + string.Join(", ", undocumented.ToArray()));

        }

        // The mutex timeout races against the time a single transaction can
        // hold the lock. Setting it below that worst case does not make
        // anything faster: it makes concurrent callers give up on a controller
        // that is merely busy, which is what "failed to acquire embedded
        // controller exclusive lock" means when it appears out of nowhere.
        private static void TestMutexTimeoutCoversWorstCase() {

            // Four waits per byte, two bytes for a word, and the whole
            // exchange retried, each wait bounded by its own budget
            int worstCase = Config.EcWaitTimeoutMs * 4 * 2 * Config.EcRetryLimit;

            SelfTest.Check(Config.EcMutexTimeout >= worstCase,
                "the EC mutex timeout (" + Config.EcMutexTimeout
                    + " ms) covers the worst-case transaction ("
                    + worstCase + " ms)");

            SelfTest.Check(Config.EcWaitTimeoutMs > 0,
                "the wait time budget is positive");
            SelfTest.Check(Config.EcWaitLimit > 0,
                "the spin count is positive");
            SelfTest.Check(Config.EcRetryLimit > 0,
                "the retry budget is positive");

        }

        // Hysteresis only works if the release threshold is genuinely below
        // the engage threshold; equal values make the protection chatter
        private static void TestThermalThresholdsMakeSense() {

            SelfTest.Check(Config.ThermalProtectionLowC < Config.ThermalProtectionHighC,
                "the thermal release threshold (" + Config.ThermalProtectionLowC
                    + "°C) is below the engage threshold ("
                    + Config.ThermalProtectionHighC + "°C)");

            SelfTest.Check(Config.ThermalProtectionHighC <= Config.MaxBelievableTemperature,
                "the thermal engage threshold is a temperature the sensors "
                    + "can actually report (at most "
                    + Config.MaxBelievableTemperature + "°C)");

            SelfTest.Check(Config.FanProgramHysteresisC >= 0,
                "the fan curve hysteresis margin is not negative");

        }

        private static void TestFanLevelRange() {

            SelfTest.Check(Config.FanLevelMin < Config.FanLevelMax,
                "the minimum fan level is below the maximum");
            SelfTest.Check(Config.FanLevelMax > 0 && Config.FanLevelMax <= 255,
                "the maximum fan level fits in the byte the hardware takes");

        }

#region Source Inspection
        // Locates and reads Config.cs from a development tree, or returns null
        private static string ReadConfigSource() {
            return ReadFromRepository("Library", "Config.cs");
        }

        // Reads the shipped configuration template, or returns null
        private static string ReadTemplate() {
            return ReadFromRepository("StarMon.xml");
        }

        // The repository root, found by walking up from the executable looking
        // for a file that only exists there. Anchoring on Library\Config.cs
        // rather than on whichever file is being read matters: a built copy of
        // StarMon.xml sits next to the executable, and searching for that one
        // directly would find the deployed configuration instead of the
        // template the repository ships.
        private static string RepositoryRoot() {
            try {
                string dir = System.IO.Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);

                for(int i = 0; i < 4 && dir != null; i++) {

                    if(System.IO.File.Exists(
                        System.IO.Path.Combine(dir, "Library", "Config.cs")))
                        return dir;

                    dir = System.IO.Path.GetDirectoryName(dir);

                }
            } catch { }

            return null;
        }

        private static string ReadFromRepository(params string[] parts) {
            try {
                string path = RepositoryRoot();
                if(path == null)
                    return null;

                foreach(string part in parts)
                    path = System.IO.Path.Combine(path, part);

                return System.IO.File.Exists(path)
                    ? System.IO.File.ReadAllText(path) : null;

            } catch {
                return null;
            }
        }

        // Pulls the setting names out of the reader or writer calls. Both take
        // the form Get*(xml, XmlPrefix + "Name", ...) or Set*(xml, XmlPrefix +
        // "Name", ...), so the prefix that precedes the name says which.
        private static List<string> ExtractKeys(
            string source, string marker, string callPrefix) {

            List<string> keys = new List<string>();
            int at = 0;

            while((at = source.IndexOf(marker, at)) >= 0) {

                int nameStart = at + marker.Length;
                int nameEnd = source.IndexOf('"', nameStart);
                if(nameEnd < 0)
                    break;

                // Look back for the call this argument belongs to
                int lineStart = source.LastIndexOf('\n', at) + 1;
                string lead = source.Substring(lineStart, at - lineStart);

                if(lead.TrimStart().StartsWith(callPrefix)
                    || lead.Contains("(" + callPrefix)
                    || lead.Contains(" " + callPrefix)) {

                    string name = source.Substring(nameStart, nameEnd - nameStart);
                    if(name.Length > 0 && !keys.Contains(name))
                        keys.Add(name);

                }

                at = nameEnd;

            }

            return keys;
        }
#endregion

    }

}
