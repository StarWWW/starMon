// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using StarMon.Library;

namespace StarMon.Library.Locale {

#region Interface
    // Defines an interface for the retrieval
    // of localization-dependent natural language messages
    public interface ILocale : IDisposable {

        // Language retrieval methods
        public LocaleData.Language GetLanguage();
        public LocaleData.Language GetLanguage(string languageName);
        public string GetLanguageName(LocaleData.Language language);
        public string[] GetLanguages();

        // Language setting methods
        public void SetLanguage(LocaleData.Language language);
        public void SetLanguage(string languageName);

        // Localized message string methods
        public string Get(string messageId);
        public string GetDefault(string messageId);

    }
#endregion

    // Implements the general functionality for message localization
    // including the default text for all localizable messages
    public abstract class LocaleAbstract : LocaleData, ILocale {

        // The identifier of the currently-selected language
        protected Language lang;

        // Localizable message dictionary.
        //
        // Each entry is replaced wholesale rather than edited in place — see
        // the note in Load() about the thread reading these while the language
        // changes.
        protected Dictionary<string, string>[] msg;

        // What this build ships, before anything in the configuration file is
        // layered over it
        protected Dictionary<string, string>[] builtIn;

#region Initialization & Disposal
        // Constructs an instance
        public LocaleAbstract() {

            // Initialize the dictionary array
            msg = new Dictionary<string, string>[GetLanguages().Length];

            // Initialize the per-locale dictionaries
            foreach(string language in GetLanguages()) {
                msg[(int) GetLanguage(language)] =
                    new Dictionary<string, string>();
            }

            // Define the default fallback messages
            msg[(int) Language.Fallback] = msgFallback;

            // Define the built-in translations
            msg[(int) Language.Turkish] = msgTurkish;

            // Kept apart from the live dictionaries above, so a language can
            // be rebuilt from what this build ships rather than from whatever
            // the last load left behind. See Load().
            builtIn = new Dictionary<string, string>[msg.Length];
            builtIn[(int) Language.Fallback] = msgFallback;
            builtIn[(int) Language.Turkish] = msgTurkish;

            // Set the language to the default fallback
            SetLanguage(Language.Fallback);

            // Note: This also loads the messages, so has to run
            // only when the dictionaries are already initalized

        }

        // Frees up the resources
        public void Dispose() {
        }
#endregion

#region Language Retrieval & Setting Methods
        // Retrieves the currently-set language given its enumerated identifier
        public virtual Language GetLanguage() {
            return this.lang;
        }

        // Retrieves the currently-set language given its identifier as a string
        public virtual Language GetLanguage(string languageName) {
            return (Language) Enum.Parse(typeof(Language), languageName);
        }

        // Retrieves the identifiers of all languages as a string array
        public virtual string[] GetLanguages() {
            return Enum.GetNames(typeof(Language));
        }

        // Retrieves the descriptive name of a language
        public virtual string GetLanguageName(Language language) {
            return Enum.GetName(typeof(Language), language);
        }

        // Sets the current language given its identifier as a string
        // and loads the messages for it
        public virtual void SetLanguage(Language language) {
            this.lang = language;

            if(language != Language.Fallback)
                Load(language);
        }

        // Sets the current language given its enumerated identifier
        public virtual void SetLanguage(string languageName) {
            SetLanguage(GetLanguage(languageName));
        }
#endregion

#region Localization Methods
        // Retrieves the default fallback localized message given its identifier
        public virtual string Get(string messageId) {
            return GetDefault(messageId);

        }

        // Retrieves the default fallback localized message given its identifier
        public virtual string GetDefault(string messageId) {
            string message;

            // Try to get the message for the default fallback language
            if(msg[(int) Language.Fallback].TryGetValue(messageId, out message))
                return message;

            // If no message can be retrieved
            else // Just return the identifier
                return messageId;

        }

        // Loads messages for a given language silently ignoring any errors
        protected virtual void Load(Language language) {
            Load(language, false);
        }

        // Loads messages for a given language and optionally report an error
        protected abstract void Load(Language language, bool showError);

    }
#endregion

    // Implements the language-specific functionality for message localization
    public sealed class Locale : LocaleAbstract, ILocale {

        // The following three statements ensure the class can be instantiated only once
        private static readonly Locale instance = new Locale();

        private Locale() { }

        public static Locale Instance {
            get { return instance; }
        }

        // Implements loading messages for a given language.
        //
        // Two things here are deliberate and were not before.
        //
        // The dictionary is rebuilt and swapped in, rather than edited where
        // it stands. This runs on the interface thread when the language
        // changes, and the poller reads the same dictionary from its own
        // thread every tick — for sensor labels, zone names and throttle
        // descriptions. A Dictionary being written while it is read can throw,
        // and mid-resize can spin. Assigning the reference is atomic, so a
        // reader sees either the whole of the old one or the whole of the new.
        //
        // And it is rebuilt from what this build ships rather than from
        // whatever is currently loaded. The file's overrides used to be
        // written into the slot of whichever language was being selected, so
        // an English override in the configuration file was written over the
        // built-in Turkish strings the moment somebody switched to Turkish —
        // permanently, for the rest of the session, giving a half-translated
        // interface with no way back but editing the file.
        protected override void Load(Language language, bool showError = false) {

            if(Config.FilePath == "" || !File.Exists(Config.FilePath))
                return;

            try {

                XmlDocument xml = new XmlDocument();
                xml.Load(Config.FilePath);

                XmlNodeList messages = xml.SelectNodes("StarMon/Messages/String");
                if(messages == null || messages.Count == 0)
                    return;

                // From the shipped strings for this language, where there are
                // any — the Override slot has none of its own and layers onto
                // the fallback, which is what makes it an override
                Dictionary<string, string> source =
                    builtIn[(int) language] ?? builtIn[(int) Language.Fallback];

                Dictionary<string, string> rebuilt =
                    new Dictionary<string, string>(source);

                // Assigned rather than added, so a key a built-in translation
                // already defines is overridden by the file instead of
                // throwing and abandoning the rest of the load
                foreach(XmlNode node in messages)
                    if(node.Attributes["Key"] != null)
                        rebuilt[node.Attributes["Key"].Value] = node.InnerText;

                msg[(int) language] = rebuilt;

            } catch {

                // Show an error message if the file is present but malformed
                if(File.Exists(Config.FilePath) && showError)
                    App.Error("ErrLocaleLoad");

            }

        }

        // Retrieves the localized natural-language message given its identifier
        // Or the default fallback if the message could not be found
        public override string Get(string messageId) {
            string message;

            // Try to get the value for the currently-selected language first
            if(msg[(int) lang].TryGetValue(messageId, out message))
                return message;

            // If no message can be retrieved
            else // Fallback to the default implementation
                return base.Get(messageId);
        }

    }

}
