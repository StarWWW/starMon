// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Reflection;
using StarMon.Library;
using StarMon.Library.Locale;

namespace StarMon.Test {

    // Checks that the built-in translations stay in step with the English
    // strings. A missing key is not fatal at runtime (it falls back to
    // English), which is exactly why it needs catching here: the symptom is a
    // window with half its captions in the wrong language, and nothing in the
    // log to say so.
    [TestSuite(Order = 20)]
    public static class TestLocale {

        // The one key a translation is not expected to define: it generates a
        // configuration file whose comments are the same in every language
        private static readonly string[] Untranslated = { "_ConfigXmlTemplate" };

        public static void Run() {

            SelfTest.Group("Locale");

            Dictionary<string, string> english = GetDictionary("msgFallback");
            Dictionary<string, string> turkish = GetDictionary("msgTurkish");

            if(english == null || turkish == null) {
                SelfTest.Check(false,
                    "the message dictionaries could not be read");
                return;
            }

            SelfTest.Check(english.Count > 200,
                "the English dictionary is populated ("
                    + english.Count + " keys)");

            TestNoMissingKeys(english, turkish);
            TestNoStrayKeys(english, turkish);
            TestNoEmptyTranslations(turkish);
            TestPlaceholdersMatch(english, turkish);
            TestKeysUsedBySourceExist();
            TestEnumeratedNamesTranslated(english, turkish);
            TestLanguageResolution();
            TestSwitchingLanguageLeavesTheTranslationIntact();

        }

        // Switching language must not damage a translation.
        //
        // The configuration file's Messages section is an override, and its
        // entries used to be written into the dictionary of whichever language
        // was being selected. So an English override in the file was written
        // over the built-in Turkish strings the moment somebody switched to
        // Turkish — permanently, for the rest of the session, leaving a
        // half-translated interface with no way back but editing the file.
        //
        // Checked by switching back and forth and asking whether the built-in
        // strings are still what this build ships. The dictionaries are read
        // through reflection because they are the shipped ones rather than
        // anything a test is meant to reach.
        private static void TestSwitchingLanguageLeavesTheTranslationIntact() {

            Dictionary<string, string> turkish = GetDictionary("msgTurkish");
            Dictionary<string, string> english = GetDictionary("msgFallback");

            if(turkish == null || english == null) {
                SelfTest.Skip("the message dictionaries could not be read");
                return;
            }

            // A key that exists in both, with different text, so a leak from
            // one into the other is visible
            string key = null;
            foreach(KeyValuePair<string, string> entry in turkish)
                if(english.ContainsKey(entry.Key)
                    && english[entry.Key] != entry.Value
                    && entry.Value.Length > 0) {
                    key = entry.Key;
                    break;
                }

            if(key == null) {
                SelfTest.Skip("no key differs between the two translations");
                return;
            }

            string turkishText = turkish[key];
            string englishText = english[key];

            LocaleData.Language saved = Config.Locale.GetLanguage();

            try {

                Config.Locale.SetLanguage(LocaleData.Language.Turkish);
                SelfTest.Equal(turkishText, Config.Locale.Get(key),
                    "the Turkish string is the Turkish one");

                Config.Locale.SetLanguage(LocaleData.Language.Override);
                Config.Locale.SetLanguage(LocaleData.Language.Turkish);

                SelfTest.Equal(turkishText, Config.Locale.Get(key),
                    "and still is after switching away and back");

                // The shipped dictionaries themselves are untouched, which is
                // what makes the rebuild honest rather than merely idempotent
                SelfTest.Equal(turkishText, GetDictionary("msgTurkish")[key],
                    "the built-in Turkish dictionary is not written over");

                SelfTest.Equal(englishText, GetDictionary("msgFallback")[key],
                    "nor is the English one");

            } finally {
                Config.Locale.SetLanguage(saved);
            }

        }

        // Values the firmware names, which the interface shows as words.
        //
        // These are the leak nobody sees in a parity check: the key exists in
        // both dictionaries, so every test above passes, while the screen
        // shows the bare enumeration name because the code never looked the
        // key up. That is how a Turkish panel came to read "Mod - Performance"
        // directly above a selector offering "Performans".
        //
        // Checking that the key exists for every value the enumeration
        // declares is the half that can be tested mechanically; it is also the
        // half that breaks when a firmware profile is added later.
        private static void TestEnumeratedNamesTranslated(
            Dictionary<string, string> english,
            Dictionary<string, string> turkish) {

            List<string> missing = new List<string>();

            foreach(string name in Enum.GetNames(
                typeof(Hardware.Bios.BiosData.FanMode))) {

                string key = Config.L_PROG + "Mode" + name;

                if(!english.ContainsKey(key)) missing.Add(key + " (English)");
                if(!turkish.ContainsKey(key)) missing.Add(key + " (Turkish)");

            }

            SelfTest.Check(missing.Count == 0,
                "every firmware fan mode has a name in both languages"
                    + (missing.Count == 0 ? ""
                        : " - missing " + string.Join(", ", missing.ToArray())));

        }

        // A key the interface asks for but no dictionary defines is not an
        // error at runtime: Locale.Get hands back the key itself, so the
        // window shows "GuiMainDetVram" where a caption belongs. Nothing
        // reports it, which is why it needs catching here.
        private static void TestKeysUsedBySourceExist() {

            // Every way the interface names a message, mapped to the prefix
            // that call form prepends. The first three are the short helpers
            // the details panel and the stat cards use; the rest are the
            // direct lookups, including the ones written as a prefix constant
            // concatenated with a literal suffix.
            var helpers = new Dictionary<string, string> {
                ["Det("] = "GuiMainDet",
                ["Card("] = "GuiMainCard",
                ["BatTip("] = "GuiMainBatTip",
                ["Locale.Get("] = "",
                ["Config.L_CLI + "] = "Cli",
                ["Config.L_GUI + "] = "Gui",
                ["Config.L_GUI_ABOUT + "] = "GuiAbout",
                ["Config.L_GUI_MAIN + "] = "GuiMain",
                ["Config.L_GUI_MENU + "] = "GuiMenu",
                ["Config.L_GUI_TIP + "] = "GuiTip",
                ["Config.L_PROG + "] = "Prog",
                ["Config.L_UNIT + "] = "Unit"
            };

            // The markup form. {loc:Str Key} is not quoted the way the C#
            // calls are, so it is scanned separately below.
            const string Markup = "{loc:Str ";

            string source = ReadGuiSource();

            if(source == null) {
                SelfTest.Skip("interface source not present, key-usage check not run");
                return;
            }

            List<string> missing = new List<string>();
            int checked_ = 0;

            foreach(KeyValuePair<string, string> helper in helpers) {

                int at = 0;
                while((at = source.IndexOf(helper.Key + "\"", at)) >= 0) {

                    int start = at + helper.Key.Length + 1;
                    int end = source.IndexOf('"', start);
                    if(end < 0)
                        break;

                    string suffix = source.Substring(start, end - start);
                    at = end;

                    // Only whole, statically-known names can be checked. A
                    // lookup assembled at run time (a preset name, a fan mode)
                    // is skipped rather than reported as missing.
                    if(suffix.Length == 0 || suffix.IndexOfAny(
                        new[] { ' ', '+', '.', '(', ')', '\\', '"' }) >= 0)
                        continue;

                    // A literal that is itself concatenated with something
                    // further along is only part of a name, not a whole one:
                    // Get(L_GUI_MAIN + "Det" + key) yields "Det" here, which
                    // no dictionary defines and none should
                    int after = end + 1;
                    while(after < source.Length && char.IsWhiteSpace(source[after]))
                        after++;
                    if(after < source.Length && source[after] == '+')
                        continue;

                    string key = helper.Value + suffix;
                    checked_++;

                    // Get returns the identifier unchanged when it knows no
                    // such message, which is exactly the symptom being hunted
                    if(Locale.Instance.Get(key) == key && !missing.Contains(key))
                        missing.Add(key);

                }

            }

            // The markup form: {loc:Str Key}, ending at the closing brace
            int mark = 0;
            int inMarkup = 0;

            while((mark = source.IndexOf(Markup, mark)) >= 0) {

                int start = mark + Markup.Length;
                int end = source.IndexOf('}', start);
                if(end < 0)
                    break;

                string key = source.Substring(start, end - start).Trim();
                mark = end;

                if(key.Length == 0 || key.IndexOfAny(new[] { ' ', '"', ',' }) >= 0)
                    continue;

                checked_++;
                inMarkup++;

                if(Locale.Instance.Get(key) == key && !missing.Contains(key))
                    missing.Add(key);

            }

            // The scan silently covering nothing is the failure this test is
            // least able to notice, so the count is asserted as well as the
            // keys: the interface is markup now, and a run that found no
            // {loc:Str} at all has stopped testing rather than started passing
            SelfTest.Check(inMarkup > 20,
                "the markup was scanned for message keys (" + inMarkup + " found)");

            SelfTest.Check(missing.Count == 0,
                missing.Count == 0
                    ? "every message the interface asks for is defined ("
                        + checked_ + " uses)"
                    : "used but undefined: " + string.Join(", ", missing.ToArray()));

        }

        // A format placeholder that exists in one language and not the other
        // either throws when the string is formatted, or silently drops the
        // value it was meant to carry
        private static void TestPlaceholdersMatch(
            Dictionary<string, string> english,
            Dictionary<string, string> turkish) {

            List<string> mismatched = new List<string>();

            foreach(KeyValuePair<string, string> entry in english) {

                string other;
                if(!turkish.TryGetValue(entry.Key, out other))
                    continue;

                for(int i = 0; i < 3; i++) {
                    string token = "{" + i + "}";
                    if(entry.Value.Contains(token) != other.Contains(token)) {
                        mismatched.Add(entry.Key + " (" + token + ")");
                        break;
                    }
                }

            }

            SelfTest.Check(mismatched.Count == 0,
                mismatched.Count == 0
                    ? "format placeholders agree between languages"
                    : "placeholder mismatch: "
                        + string.Join(", ", mismatched.ToArray()));

        }

        // Concatenates the interface sources so their keys can be checked
        // against the dictionaries.
        //
        // Both the code and the markup: most of the interface's strings are
        // now {loc:Str Key} in XAML rather than Locale.Get in C#, and a scan
        // that only looked at the code would go on passing while covering
        // almost nothing — which is a worse state than not having it.
        private static string ReadGuiSource() {
            try {
                string dir = System.IO.Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);

                for(int i = 0; i < 4 && dir != null; i++) {

                    string app = System.IO.Path.Combine(dir, "App");
                    string ui = System.IO.Path.Combine(dir, "Ui");

                    if(System.IO.Directory.Exists(app) && System.IO.Directory.Exists(ui)) {

                        System.Text.StringBuilder sb = new System.Text.StringBuilder();

                        Append(sb, app, "*.cs");
                        Append(sb, ui, "*.cs");
                        Append(sb, ui, "*.xaml");

                        return sb.ToString();

                    }

                    dir = System.IO.Path.GetDirectoryName(dir);

                }
            } catch { }

            return null;
        }

        private static void Append(System.Text.StringBuilder sb,
            string directory, string pattern) {

            foreach(string file in System.IO.Directory.GetFiles(
                directory, pattern, System.IO.SearchOption.AllDirectories))
                sb.Append(System.IO.File.ReadAllText(file)).Append('\n');

        }

        // Reads one of the protected message dictionaries off a locale
        // instance. Reflection is the price of leaving them protected, which
        // is where they belong: nothing outside the locale system should be
        // reaching into them at runtime.
        private static Dictionary<string, string> GetDictionary(string name) {
            try {
                FieldInfo field = typeof(LocaleData).GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return field == null ? null
                    : field.GetValue(Locale.Instance) as Dictionary<string, string>;
            } catch {
                return null;
            }
        }

        private static void TestNoMissingKeys(
            Dictionary<string, string> english,
            Dictionary<string, string> turkish) {

            List<string> missing = new List<string>();

            foreach(string key in english.Keys)
                if(!turkish.ContainsKey(key)
                    && Array.IndexOf(Untranslated, key) < 0)
                    missing.Add(key);

            SelfTest.Check(missing.Count == 0,
                missing.Count == 0
                    ? "every English key has a Turkish counterpart"
                    : "Turkish is missing " + missing.Count + " key(s): "
                        + string.Join(", ", missing.ToArray()));

        }

        // A key on one side only is usually a rename that was applied to one
        // file and not the other, which leaves dead weight behind
        private static void TestNoStrayKeys(
            Dictionary<string, string> english,
            Dictionary<string, string> turkish) {

            List<string> stray = new List<string>();

            foreach(string key in turkish.Keys)
                if(!english.ContainsKey(key))
                    stray.Add(key);

            SelfTest.Check(stray.Count == 0,
                stray.Count == 0
                    ? "Turkish defines no keys English does not"
                    : "Turkish has " + stray.Count + " stray key(s): "
                        + string.Join(", ", stray.ToArray()));

        }

        // An empty translation shows as a blank caption rather than falling
        // back, so it is worse than no translation at all
        private static void TestNoEmptyTranslations(
            Dictionary<string, string> turkish) {

            List<string> empty = new List<string>();

            foreach(KeyValuePair<string, string> entry in turkish)

                // Two keys are legitimately empty in English too: they are
                // placeholders only a translation fills in
                if(string.IsNullOrEmpty(entry.Value)
                    && entry.Key != "CliTranslated"
                    && entry.Key != "GuiTranslated"
                    && !entry.Key.EndsWith("ThrottlingUnknown"))
                    empty.Add(entry.Key);

            SelfTest.Check(empty.Count == 0,
                empty.Count == 0
                    ? "no Turkish string is empty"
                    : "empty Turkish string(s): "
                        + string.Join(", ", empty.ToArray()));

        }

        // The language name in the configuration has to land on the right slot,
        // and an unrecognized one must not take the application down
        private static void TestLanguageResolution() {

            string saved = Config.Language;

            try {

                Config.Language = "Turkish";
                SelfTest.Equal(LocaleData.Language.Turkish, Config.ResolveLanguage(),
                    "\"Turkish\" resolves to the Turkish slot");

                Config.Language = "English";
                SelfTest.Equal(LocaleData.Language.Override, Config.ResolveLanguage(),
                    "\"English\" resolves to the override slot, so that "
                        + "file-supplied strings keep taking effect");

                Config.Language = "turkish";
                SelfTest.Equal(LocaleData.Language.Turkish, Config.ResolveLanguage(),
                    "language names are matched without regard to case");

                Config.Language = "Klingon";
                SelfTest.Equal(LocaleData.Language.Override, Config.ResolveLanguage(),
                    "an unknown language falls back rather than throwing");

                Config.Language = "";
                SelfTest.Equal(LocaleData.Language.Override, Config.ResolveLanguage(),
                    "an empty language falls back rather than throwing");

                Config.Language = "Auto";
                LocaleData.Language auto = Config.ResolveLanguage();
                SelfTest.Check(
                    auto == LocaleData.Language.Override
                        || auto == LocaleData.Language.Turkish,
                    "\"Auto\" resolves to one of the available languages");

            } finally {
                Config.Language = saved;
            }

        }

    }

}
