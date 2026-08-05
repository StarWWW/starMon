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
    [TestSuite(Order = 60)]
    public static class TestConfig {

        public static void Run() {

            SelfTest.Group("Configuration");

            TestSettingsRoundTrip();
            TestEverySettingIsPersisted();
            TestTemplateDocumentsEverySetting();
            TestMutexTimeoutCoversWorstCase();
            TestThermalThresholdsMakeSense();
            TestFanLevelRange();
            TestTheLogSizeSliderReachesItsOwnMaximum();
            TestASilentFileDoesNotOverrideAShippedDecision();

        }

        // A configuration file that says nothing about a sensor must not be
        // read as saying yes.
        //
        // Found by comparing what a running application wrote back against
        // what this build ships. The four auxiliary probes are kept out of the
        // hottest-reading check here, because an unconnected channel on a
        // given board still answers with a believable temperature that never
        // moves — and every machine with a configuration file already on disk
        // listed those probes without a Use attribute. The loader started the
        // flag at true, a missing attribute threw, the throw was swallowed,
        // and true stood: the decision was undone on load. The writer only
        // emits the attribute when it is false, so it was undone again on
        // save, and nothing in the file showed it had happened.
        //
        // An explicit value in the file is the user's and is kept. Silence is
        // not an answer, and now takes this build's.
        private static void TestASilentFileDoesNotOverrideAShippedDecision() {

            // Asserted against what this build ships rather than against what
            // is in force: a configuration file sitting beside the test host
            // replaces the second, and these are statements about the first.
            foreach(string name in new string[] { "TNT2", "TNT3", "TNT4", "TNT5" }) {

                Config.TemperatureSensorData shipped;

                SelfTest.Check(Config.TemperatureSensorShipped
                        .TryGetValue(name, out shipped),
                    name + " is one of the shipped sensors");

                if(!Config.TemperatureSensorShipped.TryGetValue(name, out shipped))
                    continue;

                SelfTest.Check(!shipped.Use,
                    name + " is shipped kept out of the hottest-reading check");

                // The loader's answer for a file that does not mention it
                SelfTest.Equal(shipped.Use, DefaultUseFor(name),
                    "and a file silent about " + name + " gets that answer");

            }

            // The named probes stay in the check, so the fix did not simply
            // switch everything off
            foreach(string name in new string[] { "CPUT", "GPTM" }) {

                SelfTest.Check(DefaultUseFor(name),
                    name + " is still used for the hottest reading");

            }

            // A probe somebody added by hand is acted on rather than ignored:
            // adding one and having it quietly do nothing would be a stranger
            // answer than using it
            SelfTest.Check(DefaultUseFor("SomethingNobodyShips"),
                "a sensor this build does not know is used");

        }

        // The loader's default for a sensor the file does not have an opinion
        // about. Private to Config, and the point of the test is what it
        // answers, so it is reached the same way the loader reaches it.
        private static bool DefaultUseFor(string name) {

            return (bool) typeof(Config)
                .GetMethod("DefaultUseFor", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { name });

        }

        // The top of a slider has to be a position that sticks.
        //
        // The log size is stored in kilobytes and was read back through a
        // word, so the slider's own maximum of 64 MB — 65536 kilobytes — was
        // one past what a ushort holds. The parse threw, the reader swallowed
        // it and reported failure, and the setting reverted to the compiled
        // 4 MB on the next launch: saved correctly, discarded on load, with
        // nothing in the log to say so.
        private static void TestTheLogSizeSliderReachesItsOwnMaximum() {

            SelfTest.Check(Config.LogFileMaxKilobytes > ushort.MaxValue,
                "the largest log size does not fit in a word ("
                    + Config.LogFileMaxKilobytes + " KB), so it is not read as one");

            // The value the settings page writes at the top of its range
            SelfTest.Equal(64u * 1024u, Config.LogFileMaxKilobytes,
                "and it is the 64 MB the slider offers");

            // Still a sane figure: the rotation holds the file in memory to
            // trim it, so this is a bound and not just a wide type
            SelfTest.Check(Config.LogFileMaxKilobytes <= 1024 * 1024,
                "while staying inside what can be rotated in memory");

            SelfTest.Check(Config.LogFileMaxBytes > 0
                && Config.LogFileMaxBytes <= (int) Config.LogFileMaxKilobytes * 1024,
                "and the compiled default is inside its own bound ("
                    + (Config.LogFileMaxBytes / 1024) + " KB)");

        }

        // Every setting the reader parses has to be one the writer emits.
        // Otherwise a value the user set in the file survives exactly until
        // the application next saves, and then vanishes without a word.
        private static void TestSettingsRoundTrip() {

            string source = ReadConfigSource();

            if(source == null) {
                // The source file is only there in a development tree; there
                // is nothing to check in a deployed copy, and nothing wrong.
                // Recorded as a skip rather than a pass: it used to be a pass,
                // which meant a run that checked nothing here was
                // indistinguishable from one that checked everything.
                SelfTest.Skip("configuration source not present, round-trip check not run");
                return;
            }

            List<string> read = ExtractKeys(source, "XmlPrefix + \"", "Get");
            List<string> written = ExtractKeys(source, "XmlPrefix + \"", "Set");

            // The scan silently matching nothing is the failure this check is
            // least able to notice - an extraction that stops working looks
            // exactly like a file with no problems in it.
            SelfTest.Check(read.Count > 20,
                "the configuration reader was scanned for settings ("
                    + read.Count + " found)");

            SelfTest.Check(written.Count > 20,
                "the configuration writer was scanned for settings ("
                    + written.Count + " found)");

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
                SelfTest.Skip("configuration source not present, persistence check not run");
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

            SelfTest.Check(checked_ > 20,
                "the settings were enumerated by reflection ("
                    + checked_ + " found)");

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
                SelfTest.Skip("configuration sources not present, template check not run");
                return;
            }

            List<string> read = ExtractKeys(source, "XmlPrefix + \"", "Get");
            List<string> undocumented = new List<string>();

            SelfTest.Check(read.Count > 20,
                "the configuration reader was scanned for settings to document ("
                    + read.Count + " found)");

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
