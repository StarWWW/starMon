// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using StarMon.Library;

namespace StarMon.Ui.Shell {

    // Puts the theme where every window and control can find it.
    //
    // A StaticResource is looked up as its dictionary is parsed, walking out
    // through the logical tree and finally to the application's own resources.
    // That last step is the one that matters here: attributes on a control's
    // root element — its Background, most obviously — are set before that
    // control's own resources exist, so a dictionary merged locally is already
    // too late for them. The theme has to be above, in Application.Resources.
    //
    // An Application instance is also what makes "pack://application:,,,/"
    // resolve, which is how the compiled markup is addressed.
    public static class Theme {

        private const string Uri = "pack://application:,,,/Ui/Theme/Theme.xaml";
        private const string PaletteUri = "pack://application:,,,/Ui/Theme/Palette.xaml";

        public static bool IsInitialized { get; private set; }

        // The two dictionaries this class put into the application's
        // resources, so that a change of colours can take them out again
        private static ResourceDictionary Palette;
        private static ResourceDictionary Styles;

        public static void Initialize() {

            if(IsInitialized)
                return;

            // The application object may already exist, or may not: the
            // interface creates one, and the design renderer needs one purely
            // so that pack URIs resolve
            if(Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            Load(Config.GuiThemeAccent, Config.GuiThemeSurface);

            // The binding converters are registered here rather than declared
            // in markup, because markup that names a type from this same
            // assembly forces a second markup-compilation pass, and that pass
            // builds a throwaway project which trips over StarMon.resx. The
            // reasoning is written out in full at the top of Ui/Views/Cards.xaml.
            //
            // This has to happen before any view is constructed: a
            // StaticResource is resolved as its dictionary is parsed, and the
            // application's own resources are the last place it looks.
            Application.Current.Resources["HealthBrush"] =
                new Views.HealthBrushConverter();
            Application.Current.Resources["HealthColour"] =
                new Views.HealthColourConverter();
            Application.Current.Resources["HideWhenEmpty"] =
                new Views.EmptyVisibilityConverter();
            Application.Current.Resources["ShowWhen"] =
                new Views.BoolVisibilityConverter();
            Application.Current.Resources["SlotBrush"] =
                new Views.SlotBrushConverter();
            Application.Current.Resources["LogBrush"] =
                new Views.LogLevelBrushConverter();

            // Every {loc:Str} in the application is a binding onto one object,
            // so telling that object the language moved is enough to redraw
            // the lot
            Config.LocaleChangedHandler += Loc.Strings.Current.Refresh;

            IsInitialized = true;

        }

        // Puts in the palette with the chosen colours, then the styles parsed
        // against it. The palette goes first, and on its own: every
        // StaticResource in the styles is resolved as they are parsed, and
        // the application's resources are where that lookup ends up.
        private static void Load(string accent, string surface) {

            Palette = new ResourceDictionary { Source = new Uri(PaletteUri) };
            ThemePalette.Apply(Palette, accent, surface);
            Application.Current.Resources.MergedDictionaries.Insert(0, Palette);

            Styles = new ResourceDictionary { Source = new Uri(Theme.Uri) };
            Application.Current.Resources.MergedDictionaries.Insert(1, Styles);

        }

        // Changes the colours of everything created from here on.
        //
        // What is already on screen keeps the colours it was built with - a
        // StaticResource is looked up once - so the caller rebuilds its window
        // afterwards. Both dictionaries are reloaded, not just the palette:
        // the styles hold the brushes they resolved when they were parsed.
        public static void Reapply(string accent, string surface) {

            if(!IsInitialized)
                return;

            Application.Current.Resources.MergedDictionaries.Remove(Styles);
            Application.Current.Resources.MergedDictionaries.Remove(Palette);
            Load(accent, surface);

        }

    }

}
