// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using StarMon.Library;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // Turns a reading's health band into the brush that paints it.
    //
    // The alternative is a DataTrigger per band per template, which is five
    // times the markup saying the same thing, and which would have to be
    // copied every time a new surface wants to show a status. The colours
    // still come from the theme dictionary rather than from here: this
    // resolves them by key, so changing the palette changes them.
    public sealed class HealthBrushConverter : IValueConverter {

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) {

            string key;

            switch(value is Health ? (Health) value : Health.Neutral) {
                case ViewModels.Health.Good:     key = "Good";     break;
                case ViewModels.Health.Warning:  key = "Warning";  break;
                case ViewModels.Health.Serious:  key = "Serious";  break;
                case ViewModels.Health.Critical: key = "Critical"; break;

                // A reading with no judgement attached — a fan speed, a clock
                // — is drawn in ink rather than colour. Where that ink lands
                // on a small mark rather than a figure, the parameter asks for
                // the quiet weight: full-strength white on a two-pixel rule
                // shouts louder than the status colours it sits beside, which
                // inverts exactly the emphasis the rule exists to give.
                default:
                    key = "Muted".Equals(parameter as string)
                        ? "TextMuted" : "TextPrimary";
                    break;
            }

            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;

            return brush ?? Brushes.White;

        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }

    }

    // The same mapping as HealthBrushConverter, but handing back the colour
    // rather than a brush. A gradient stop takes a Color, and binding one to
    // a SolidColorBrush's own colour would mean a second binding hop through
    // a property path that cannot be written in markup.
    public sealed class HealthColourConverter : IValueConverter {

        private static readonly HealthBrushConverter Brushes =
            new HealthBrushConverter();

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) {

            SolidColorBrush brush =
                Brushes.Convert(value, targetType, parameter, culture)
                    as SolidColorBrush;

            return brush != null ? brush.Color : Colors.White;

        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }

    }

    // Turns a categorical palette slot into its brush.
    //
    // Slots are assigned in order and never cycled, so a ninth series would
    // have to be an invented hue — which is exactly what the palette forbids,
    // because the ordering is what keeps neighbouring series apart under
    // colour-vision deficiency. Anything past the eight is drawn in the
    // neutral instead, which reads as a reference trace rather than as a peer.
    public sealed class SlotBrushConverter : IValueConverter {

        public const int Slots = 8;

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) {

            int slot = value is int ? (int) value : -1;

            string key = slot >= 0 && slot < Slots
                ? "Series" + (slot + 1) : "SeriesMuted";

            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;

            return brush ?? Brushes.White;

        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }

    }

    // Turns a log level into the ink it is written in.
    //
    // Grouped, not one colour per level. Eleven distinct hues in a scrolling
    // list is not a legend anybody learns; it is a rainbow that makes the
    // whole panel harder to scan. So the levels share four inks: problems in
    // the status colours, everything else in the two text weights, and the
    // hardware conversation — which is what the log mostly is — quiet.
    public sealed class LogLevelBrushConverter : IValueConverter {

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) {

            string key;

            switch(value is LogLevel ? (LogLevel) value : LogLevel.Info) {

                case LogLevel.Error:   key = "Critical"; break;
                case LogLevel.Warning: key = "Warning";  break;

                case LogLevel.Hardware:
                case LogLevel.Config:
                    key = "TextPrimary";
                    break;

                case LogLevel.BiosCall:
                case LogLevel.BiosResult:
                case LogLevel.EcRead:
                case LogLevel.EcWrite:
                case LogLevel.Debug:
                    key = "TextMuted";
                    break;

                default:
                    key = "TextSecondary";
                    break;

            }

            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;

            return brush ?? Brushes.White;

        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }

    }

    // True becomes Visible, false becomes Collapsed. The parameter may name
    // either or both of two variations: "Hidden" keeps the space instead of
    // reclaiming it, and "Inverse" flips the sense so a false value is what
    // shows the element.
    public sealed class BoolVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) {

            bool flag = value is bool && (bool) value;

            string option = parameter as string ?? "";
            if(option.IndexOf("Inverse", StringComparison.Ordinal) >= 0)
                flag = !flag;

            if(flag)
                return Visibility.Visible;

            return option.IndexOf("Hidden", StringComparison.Ordinal) >= 0
                ? Visibility.Hidden : Visibility.Collapsed;

        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) {
            return value is Visibility && (Visibility) value == Visibility.Visible;
        }

    }

    // Collapses an element whose text is empty, so a card with nothing to say
    // in its supporting line does not reserve a blank row for it
    public sealed class EmptyVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture) {
            return string.IsNullOrEmpty(value as string)
                ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }

    }

}
