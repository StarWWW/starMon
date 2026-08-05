// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // The draggable temperature-to-fan-speed curve.
    //
    // Drawn rather than assembled from elements, because every part of it —
    // the grid, the trace, the six handles, the marker showing where the
    // machine currently sits — moves together as one thing, and dragging a
    // handle changes all of them.
    public sealed class FanCurveEditor : FrameworkElement {

        // The visible span of the temperature axis, a little wider than the
        // columns so the first and last handles are not against the edge
        private const int TempMin = 30, TempMax = 95;

        private const double PadLeft = 34, PadTop = 22, PadRight = 12, PadBottom = 26;

        // How near the cursor has to be, horizontally, to take hold of a
        // column. Generous: the handles are small and the columns are far
        // apart, so there is no ambiguity to protect against.
        private const double GrabDistance = 26;

        private FanCurveViewModel ModelValue;
        private int Dragging = -1;

        private Typeface Face;
        private Brush InkMuted, InkPrimary, Accent, AccentWash;
        private Pen GridPen, CurvePen, MarkerPen;

        public FanCurveEditor() {
            this.Focusable = true;
            this.Cursor = Cursors.SizeNS;
            this.Loaded += (s, e) => ResolveTheme();

            // The selection ring above only exists while focused, so gaining
            // and losing focus are both redraws
            this.GotKeyboardFocus += (s, e) => InvalidateVisual();
            this.LostKeyboardFocus += (s, e) => InvalidateVisual();
        }

        public FanCurveViewModel Model {
            get { return this.ModelValue; }
            set {

                if(this.ModelValue != null)
                    this.ModelValue.PropertyChanged -= OnModelChanged;

                this.ModelValue = value;

                if(this.ModelValue != null)
                    this.ModelValue.PropertyChanged += OnModelChanged;

                InvalidateVisual();

            }
        }

        private void OnModelChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {
            InvalidateVisual();
        }

        private void ResolveTheme() {

            this.InkMuted = Find("TextMuted") ?? Brushes.Gray;
            this.InkPrimary = Find("TextPrimary") ?? Brushes.White;
            this.Accent = Find("Accent") ?? Brushes.DodgerBlue;

            Color accent = (this.Accent as SolidColorBrush) != null
                ? ((SolidColorBrush) this.Accent).Color : Colors.DodgerBlue;

            LinearGradientBrush wash = new LinearGradientBrush(
                Color.FromArgb(0x40, accent.R, accent.G, accent.B),
                Color.FromArgb(0x00, accent.R, accent.G, accent.B),
                new Point(0, 0), new Point(0, 1));
            wash.Freeze();
            this.AccentWash = wash;

            Brush grid = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            grid.Freeze();
            this.GridPen = new Pen(grid, 1);
            this.GridPen.Freeze();

            this.CurvePen = new Pen(this.Accent, 2) {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            this.CurvePen.Freeze();

            // The marker is dashed so it reads as an annotation over the plot
            // rather than as a seventh thing being plotted on it
            Pen marker = new Pen(this.InkMuted, 1) {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
            };
            marker.Freeze();
            this.MarkerPen = marker;

            this.Face = new Typeface(
                (FontFamily) (Application.Current != null
                    ? Application.Current.TryFindResource("FontSmall") : null)
                    ?? new FontFamily("Segoe UI"),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        }

        private Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        private Rect Plot() {
            return new Rect(PadLeft, PadTop,
                Math.Max(0, this.ActualWidth - PadLeft - PadRight),
                Math.Max(0, this.ActualHeight - PadTop - PadBottom));
        }

        private double TempToX(double temperature) {
            Rect plot = Plot();
            return plot.Left
                + (temperature - TempMin) / (double) (TempMax - TempMin) * plot.Width;
        }

        private double PercentToY(double percent) {
            Rect plot = Plot();
            return plot.Bottom - percent / 100.0 * plot.Height;
        }

        private int YToPercent(double y) {
            Rect plot = Plot();
            if(plot.Height <= 0)
                return 0;
            int percent = (int) Math.Round((plot.Bottom - y) / plot.Height * 100.0);
            return percent < 0 ? 0 : percent > 100 ? 100 : percent;
        }

        protected override void OnRender(DrawingContext context) {

            if(this.GridPen == null)
                ResolveTheme();

            Rect plot = Plot();
            if(plot.Width <= 8 || plot.Height <= 8)
                return;

            FanCurveViewModel model = this.ModelValue;
            if(model == null)
                return;

            // Horizontal grid, labelled at the quarters
            for(int percent = 0; percent <= 100; percent += 25) {

                double y = Math.Round(PercentToY(percent)) + 0.5;
                context.DrawLine(this.GridPen,
                    new Point(plot.Left, y), new Point(plot.Right, y));

                if(percent % 50 == 0)
                    Draw(context, percent + "%", this.InkMuted, 10,
                        new Point(4, y - 8));

            }

            int[] columns = FanCurveViewModel.Columns;
            int[] percents = model.Percent;

            // Where the machine currently is. Drawn under the curve so the
            // trace stays the thing in front.
            double now = model.CurrentTemperature;
            if(now > 0) {
                double x = Math.Round(TempToX(Math.Min(Math.Max(now, TempMin), TempMax))) + 0.5;
                context.DrawLine(this.MarkerPen,
                    new Point(x, plot.Top), new Point(x, plot.Bottom));
                Draw(context, ((int) Math.Round(now)) + "°", this.InkMuted, 10,
                    new Point(x + 4, plot.Top - 16));
            }

            // The curve, and the wash under it. One series, so the wash is
            // saying "this much fan", which is exactly what it should say.
            List<Point> points = new List<Point>(columns.Length);
            for(int i = 0; i < columns.Length; i++)
                points.Add(new Point(TempToX(columns[i]), PercentToY(percents[i])));

            // The trace runs flat out to both edges rather than stopping at
            // the first and last columns. That is not decoration: it is what
            // the fan program does. Below the first column it holds the first
            // level, and above the last it holds the last — so a curve drawn
            // ending at ninety degrees looks like a cliff back to zero, which
            // is the opposite of what happens.
            List<Point> trace = new List<Point>(points.Count + 2);
            trace.Add(new Point(plot.Left, points[0].Y));
            trace.AddRange(points);
            trace.Add(new Point(plot.Right, points[points.Count - 1].Y));

            StreamGeometry area = new StreamGeometry();
            using(StreamGeometryContext draw = area.Open()) {
                draw.BeginFigure(new Point(trace[0].X, plot.Bottom), true, true);
                draw.PolyLineTo(trace, true, true);
                draw.LineTo(new Point(trace[trace.Count - 1].X, plot.Bottom), true, true);
            }
            area.Freeze();
            context.DrawGeometry(this.AccentWash, null, area);

            StreamGeometry line = new StreamGeometry();
            using(StreamGeometryContext draw = line.Open()) {
                draw.BeginFigure(trace[0], false, false);
                draw.PolyLineTo(trace.GetRange(1, trace.Count - 1), true, true);
            }
            line.Freeze();
            context.DrawGeometry(null, this.CurvePen, line);

            // The handles, each in the colour of the band its column sits in,
            // so the curve says at a glance which part of it is the part that
            // runs while the machine is hot
            for(int i = 0; i < points.Count; i++) {

                Brush band = BandBrush(columns[i]);

                // The handle the arrow keys move, ringed while the editor has
                // keyboard focus — without it the keys adjust an invisible
                // selection, which reads as the curve changing on its own
                if(this.IsKeyboardFocused && i == this.Selected) {
                    Pen ring = new Pen(this.Accent, 1.5);
                    ring.Freeze();
                    context.DrawEllipse(null, ring, points[i], 9.5, 9.5);
                }

                context.DrawEllipse(band, new Pen(this.InkPrimary, 1.5),
                    points[i], 5.5, 5.5);

                Draw(context, percents[i] + "%", this.InkPrimary, 10,
                    new Point(points[i].X - 11, points[i].Y - 21));

                Draw(context, columns[i].ToString(), this.InkMuted, 10,
                    new Point(points[i].X - 8, plot.Bottom + 5));

            }

        }

        private Brush BandBrush(int temperature) {

            string key;
            switch(HealthScale.FromTemperature(temperature)) {
                case Health.Good:     key = "Good";     break;
                case Health.Warning:  key = "Warning";  break;
                case Health.Serious:  key = "Serious";  break;
                case Health.Critical: key = "Critical"; break;
                default:              key = "TextMuted"; break;
            }

            return Find(key) ?? Brushes.White;

        }

        private void Draw(DrawingContext context, string text, Brush ink,
            double size, Point at) {

            FormattedText formatted = new FormattedText(text,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                this.Face, size, ink, Dpi.For(this));

            context.DrawText(formatted, at);

        }

        // The column nearest a horizontal position, or -1
        private int HitColumn(double x) {

            int best = -1;
            double nearest = GrabDistance;

            int[] columns = FanCurveViewModel.Columns;
            for(int i = 0; i < columns.Length; i++) {
                double distance = Math.Abs(TempToX(columns[i]) - x);
                if(distance < nearest) { nearest = distance; best = i; }
            }

            return best;

        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {

            base.OnMouseLeftButtonDown(e);

            if(this.ModelValue == null)
                return;

            Point at = e.GetPosition(this);
            this.Dragging = HitColumn(at.X);

            if(this.Dragging < 0)
                return;

            // The pointer is captured for the whole drag, so a handle dragged
            // past the edge of the plot keeps following the cursor instead of
            // being dropped the moment it leaves. The grabbed column also
            // becomes the keyboard selection, so arrows carry on from it.
            CaptureMouse();
            Focus();
            this.Selected = this.Dragging;
            this.ModelValue[this.Dragging] = YToPercent(at.Y);

        }

        protected override void OnMouseMove(MouseEventArgs e) {

            base.OnMouseMove(e);

            if(this.Dragging < 0 || this.ModelValue == null)
                return;

            this.ModelValue[this.Dragging] = YToPercent(e.GetPosition(this).Y);

        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonUp(e);
            this.Dragging = -1;
            ReleaseMouseCapture();
        }

        // Keyboard adjustment, because a curve that can only be set by
        // dragging cannot be set at all without a pointer
        protected override void OnKeyDown(KeyEventArgs e) {

            base.OnKeyDown(e);

            if(this.ModelValue == null)
                return;

            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;

            if(e.Key == Key.Left || e.Key == Key.Right) {
                int columns = FanCurveViewModel.Columns.Length;
                this.Selected = e.Key == Key.Left
                    ? Math.Max(0, this.Selected - 1)
                    : Math.Min(columns - 1, this.Selected + 1);
                InvalidateVisual();
                e.Handled = true;
            } else if(e.Key == Key.Up) {
                this.ModelValue[this.Selected] = this.ModelValue[this.Selected] + step;
                e.Handled = true;
            } else if(e.Key == Key.Down) {
                this.ModelValue[this.Selected] = this.ModelValue[this.Selected] - step;
                e.Handled = true;
            }

        }

        private int Selected;

    }

}
