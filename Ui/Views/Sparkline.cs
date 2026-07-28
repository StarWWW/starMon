// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Media;

namespace StarMon.Ui.Views {

    // A trend small enough to sit on a line of text.
    //
    // The instrument panel's carrying element. A figure on its own says where
    // a sensor is; it never says which way it is going, and "78 °C" reads the
    // same whether the machine is settling or running away. Sixty pixels of
    // line beside the figure answers that without costing a second reading.
    //
    // Deliberately not a chart: no axes, no gridlines, no labels. Anything
    // that needs those belongs in HistoryChart, and the two are not variants
    // of each other — this one is read at a glance, in peripheral vision,
    // beside the number it belongs to.
    //
    // Drawn rather than assembled, for the reason every drawn control here is:
    // markup naming a type from this assembly forces a second markup
    // compilation pass the build cannot run, so it is placed into a named
    // ContentControl from code-behind instead.
    public sealed class Sparkline : FrameworkElement {

        private double[] ValuesField = new double[0];
        private Brush StrokeField;
        private Brush AreaField;
        private Brush AreaFor;

        // Half the stroke, so the trace at the very top or bottom of the band
        // is not clipped in half by the element's own edge
        private const double Pad = 1.5;

        private const double Thickness = 1.5;

        // Below this many samples there is no trend to show, and two points
        // joined by a line is a shape the eye over-reads
        private const int Fewest = 3;

        private Brush Accent, Muted;

        public Sparkline() {
            this.Loaded += (s, e) => ResolveTheme();
            this.IsHitTestVisible = false;
        }

        // Oldest first. NaN is a gap rather than a zero — the history buffer
        // records a missing reading that way, and drawing it as zero invents a
        // cliff that never happened.
        public double[] Values {
            get { return this.ValuesField; }
            set {
                this.ValuesField = value ?? new double[0];
                InvalidateVisual();
            }
        }

        // The trace's colour. Left null it follows the accent; the callers
        // that care hand it the health band or the series slot the figure
        // beside it already uses, so the two never disagree.
        public Brush Stroke {
            get { return this.StrokeField; }
            set {
                this.StrokeField = value;
                InvalidateVisual();
            }
        }

        // A wash under the trace. Off by default: at this size it is welcome
        // on a lone sparkline and muddy on a row of them.
        public bool HasArea { get; set; }

        private void ResolveTheme() {
            this.Accent = Find("Accent") ?? Brushes.MediumPurple;
            this.Muted = Find("TextMuted") ?? Brushes.Gray;
        }

        private Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        // The wash is the trace's own colour at a tenth of its weight, built
        // once per colour rather than per render: this is redrawn every second
        // on every visible sparkline, and a fresh brush each time is churn for
        // a value that almost never changes.
        private Brush Area(Brush stroke) {

            if(ReferenceEquals(this.AreaFor, stroke) && this.AreaField != null)
                return this.AreaField;

            SolidColorBrush solid = stroke as SolidColorBrush;
            if(solid == null)
                return null;

            Color colour = solid.Color;
            SolidColorBrush wash = new SolidColorBrush(
                Color.FromArgb(0x24, colour.R, colour.G, colour.B));
            wash.Freeze();

            this.AreaFor = stroke;
            this.AreaField = wash;
            return wash;

        }

        protected override void OnRender(DrawingContext context) {

            if(this.Accent == null)
                ResolveTheme();

            double[] values = this.ValuesField;
            double width = this.ActualWidth, height = this.ActualHeight;

            if(values == null || values.Length < Fewest || width <= 4 || height <= 4)
                return;

            // The band. Taken from the data rather than fixed, because a
            // sparkline's whole job is the shape of the change, not its
            // absolute size — the figure beside it carries that.
            double low = double.MaxValue, high = double.MinValue;
            int seen = 0;

            for(int i = 0; i < values.Length; i++) {
                double value = values[i];
                if(double.IsNaN(value))
                    continue;
                if(value < low) low = value;
                if(value > high) high = value;
                seen++;
            }

            if(seen < Fewest)
                return;

            // A flat run has no band to scale against. Giving it one would
            // magnify the last bit of sensor noise into a mountain range, so
            // it is drawn as the flat line it is.
            double span = high - low;
            bool flat = span < 1e-9;

            double top = Pad, bottom = height - Pad;
            double plot = bottom - top;
            if(plot <= 0)
                return;

            double step = width / (values.Length - 1);

            Brush stroke = this.StrokeField ?? this.Accent;
            Pen pen = new Pen(stroke, Thickness) {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            StreamGeometry trace = new StreamGeometry();
            StreamGeometry wash = this.HasArea ? new StreamGeometry() : null;

            double lastX = 0, lastY = 0;
            bool any = false;

            using(StreamGeometryContext line = trace.Open())
            using(StreamGeometryContext fill = wash != null ? wash.Open() : null) {

                bool drawing = false;
                double runStartX = 0;

                for(int i = 0; i < values.Length; i++) {

                    double value = values[i];

                    if(double.IsNaN(value)) {
                        // A gap ends the run: the next reading starts a new
                        // segment rather than being joined across the hole
                        if(drawing && fill != null)
                            CloseWash(fill, lastX, bottom, runStartX);
                        drawing = false;
                        continue;
                    }

                    double x = i * step;
                    double y = flat
                        ? top + plot / 2
                        : bottom - (value - low) / span * plot;

                    if(!drawing) {
                        line.BeginFigure(new Point(x, y), false, false);
                        if(fill != null) {
                            fill.BeginFigure(new Point(x, bottom), true, true);
                            fill.LineTo(new Point(x, y), false, false);
                        }
                        runStartX = x;
                        drawing = true;
                    } else {
                        line.LineTo(new Point(x, y), true, false);
                        if(fill != null)
                            fill.LineTo(new Point(x, y), false, false);
                    }

                    lastX = x;
                    lastY = y;
                    any = true;

                }

                if(drawing && fill != null)
                    CloseWash(fill, lastX, bottom, runStartX);

            }

            if(!any)
                return;

            trace.Freeze();

            if(wash != null) {
                wash.Freeze();
                Brush area = Area(stroke);
                if(area != null)
                    context.DrawGeometry(area, null, wash);
            }

            context.DrawGeometry(null, pen, trace);

            // The head. Without it the eye has to work out which end is now,
            // and on a line this short that is a real cost.
            context.DrawEllipse(stroke, null, new Point(lastX, lastY), 1.9, 1.9);

        }

        // Drops the wash to the baseline and back, closing the run into a
        // shape rather than leaving it an open line
        private static void CloseWash(StreamGeometryContext fill,
            double lastX, double bottom, double startX) {

            fill.LineTo(new Point(lastX, bottom), false, false);
            fill.LineTo(new Point(startX, bottom), false, false);

        }

    }

}
