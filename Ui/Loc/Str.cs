// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using StarMon.Library;

namespace StarMon.Ui.Loc {

    // The strings, as something markup can bind to.
    //
    // An indexer rather than a property per string, because there are 436 of
    // them and a class with 436 properties is a class nobody adds a string to.
    // Raising a change for the indexer itself — "Item[]" — tells every binding
    // in the application at once that its value may have moved, which is what
    // makes switching language redraw the interface instead of recreating it.
    // The Windows Forms build had to close every window and build it again.
    public sealed class Strings : INotifyPropertyChanged {

        public static readonly Strings Current = new Strings();

        private Strings() { }

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] {
            get {

                if(string.IsNullOrEmpty(key))
                    return "";

                try {
                    return Config.Locale.Get(key);
                } catch {

                    // Before the locale is initialised, and if a lookup ever
                    // throws, the key itself is a far better thing to show
                    // than an empty control: it names what is missing
                    return key;

                }

            }
        }

        // Called after the language has changed
        public void Refresh() {

            PropertyChangedEventHandler handler = this.PropertyChanged;
            if(handler != null)
                handler(this, new PropertyChangedEventArgs("Item[]"));

        }

    }

    // {loc:Str GuiMainFan} in markup.
    //
    // Returns a binding rather than a string, which is the whole point: a
    // string is read once when the window is built, and a binding is asked
    // again every time the language changes.
    //
    // This class being named in markup is what forces the second
    // markup-compilation pass. That pass could not run while StarMon.resx
    // existed, which is why the resx had to go first — see the note in the
    // project file.
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class StrExtension : MarkupExtension {

        public StrExtension() { }

        public StrExtension(string key) {
            this.Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) {

            Binding binding = new Binding("[" + this.Key + "]") {
                Source = Strings.Current,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);

        }

    }

}
