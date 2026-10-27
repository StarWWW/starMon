// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace StarMon.Ui.Shell {

    // The colour themes the user can pick from the Settings page: an accent,
    // and a surface the window is drawn on.
    //
    // Only these two things change. The status colours and the chart's series
    // slots are left as they are on purpose: red has to mean "something is
    // wrong" whatever accent is chosen, and the series were picked to stay
    // apart from one another, which a theme has no business undoing.
    //
    // Everything else that carries the accent - the edge of a primary button,
    // the hover of a switch, the fill of a badge - is worked out from the one
    // colour, so a new accent needs one line here and nothing in the markup.
    public static class ThemePalette {

        // The accents, by the name the configuration file stores, in the
        // order the Settings page shows them
        public static readonly string[] Accents = {
            "Violet", "Blue", "Teal", "Green", "Orange", "Red", "Pink"
        };

        private static readonly Dictionary<string, Color> AccentColours =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase) {
                { "Violet", Color.FromRgb(0x8B, 0x5C, 0xF6) },
                { "Blue",   Color.FromRgb(0x3B, 0x82, 0xF6) },
                { "Teal",   Color.FromRgb(0x14, 0xA8, 0x9A) },
                { "Green",  Color.FromRgb(0x22, 0xA5, 0x5A) },
                { "Orange", Color.FromRgb(0xEA, 0x6C, 0x1E) },
                { "Red",    Color.FromRgb(0xE0, 0x3C, 0x48) },
                { "Pink",   Color.FromRgb(0xE0, 0x4A, 0x9A) }
            };

        // The surfaces. Graphite is the window as it has always been; Night is
        // the same layout a step darker, for an OLED panel or a dark room.
        public static readonly string[] Surfaces = { "Graphite", "Night" };

        public const string DefaultAccent = "Violet";
        public const string DefaultSurface = "Graphite";

        // An accent by name, falling back to the default for a name that is
        // not one of them - a configuration file edited by hand, or one from a
        // later version with more choices
        public static Color AccentOf(string name) {
            Color colour;
            return name != null && AccentColours.TryGetValue(name, out colour)
                ? colour : AccentColours[DefaultAccent];
        }

        public static string NormaliseAccent(string name) {
            foreach(string accent in Accents)
                if(string.Equals(accent, name, StringComparison.OrdinalIgnoreCase))
                    return accent;
            return DefaultAccent;
        }

        public static string NormaliseSurface(string name) {
            foreach(string surface in Surfaces)
                if(string.Equals(surface, name, StringComparison.OrdinalIgnoreCase))
                    return surface;
            return DefaultSurface;
        }

        // Puts the chosen accent and surface into a loaded palette dictionary,
        // replacing the entries the rest of the theme looks up by key. Called
        // before anything else is parsed against the palette, so every
        // StaticResource in the theme and the views resolves to these.
        public static void Apply(ResourceDictionary palette, string accentName, string surfaceName) {

            // The default accent keeps the file's own values, which were
            // tuned by hand; the others are worked out
            if(NormaliseAccent(accentName) != DefaultAccent)
                ApplyAccent(palette, AccentOf(accentName));

            if(NormaliseSurface(surfaceName) == "Night")
                ApplyNight(palette);

        }

        private static void ApplyAccent(ResourceDictionary palette, Color accent) {

            palette["AccentColor"] = accent;
            palette["Accent"] = Frozen(accent);
            palette["AccentDim"] = Frozen(WithAlpha(accent, 0x80));
            palette["AccentFaint"] = Frozen(WithAlpha(accent, 0x30));
            palette["AccentLight"] = Frozen(Mix(accent, Colors.White, 0.12));
            palette["Focus"] = Frozen(WithAlpha(accent, 0x66));
            palette["ChipFill"] = Frozen(WithAlpha(accent, 0x28));
            palette["ChipEdge"] = Frozen(WithAlpha(accent, 0x44));

            Color deeper = Mix(accent, Colors.Black, 0.3);
            palette["Accent2Color"] = deeper;
            palette["Accent2"] = Frozen(deeper);
            palette["Accent2Faint"] = Frozen(WithAlpha(deeper, 0x30));

            palette["BrandStroke"] = Gradient(new Point(0, 0), new Point(1, 1),
                accent, 0.0, deeper, 1.0);

            // The edge of a primary button: lighter along the top, deeper
            // along the bottom, so it reads as a raised key
            palette["AccentEdge"] = Gradient(new Point(0, 0), new Point(0, 1),
                Mix(accent, Colors.White, 0.25), 0.0, accent, 0.3, Mix(accent, Colors.Black, 0.2), 1.0);

        }

        // The darker surface: the plane near black, the cards and wells a step
        // above it, and the hairlines brought down with them so they stay a
        // hint rather than turning into outlines
        private static void ApplyNight(ResourceDictionary palette) {

            Color plane = Color.FromRgb(0x0A, 0x0B, 0x0E);
            Color card = Color.FromRgb(0x15, 0x17, 0x1C);
            Color inset = Color.FromRgb(0x0F, 0x11, 0x15);

            palette["PlaneColor"] = plane;
            palette["CardColor"] = card;
            palette["InsetColor"] = inset;

            palette["Plane"] = Frozen(plane);
            palette["PlaneLit"] = Frozen(plane);
            palette["Card"] = Frozen(card);
            palette["CardFill"] = Frozen(card);
            palette["Inset"] = Frozen(inset);

            palette["CardBorder"] = Frozen(Color.FromRgb(0x22, 0x25, 0x2D));
            palette["Divider"] = Frozen(Color.FromRgb(0x25, 0x28, 0x30));

            palette["Acrylic"] = Frozen(Color.FromArgb(0xE6, 0x15, 0x17, 0x1C));
            palette["AcrylicStrong"] = Frozen(Color.FromArgb(0xF2, 0x11, 0x13, 0x17));

        }

        // A colour a fraction of the way towards another
        public static Color Mix(Color from, Color to, double amount) {
            return Color.FromArgb(from.A,
                (byte) Math.Round(from.R + (to.R - from.R) * amount),
                (byte) Math.Round(from.G + (to.G - from.G) * amount),
                (byte) Math.Round(from.B + (to.B - from.B) * amount));
        }

        public static Color WithAlpha(Color colour, byte alpha) {
            return Color.FromArgb(alpha, colour.R, colour.G, colour.B);
        }

        private static SolidColorBrush Frozen(Color colour) {
            SolidColorBrush brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush Gradient(Point start, Point end, params object[] stops) {
            LinearGradientBrush brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            for(int i = 0; i + 1 < stops.Length; i += 2)
                brush.GradientStops.Add(new GradientStop((Color) stops[i], (double) stops[i + 1]));
            brush.Freeze();
            return brush;
        }

    }

}
