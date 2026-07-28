// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StarMon.AppService;

namespace StarMon.Ui.Views {

    // Draws the history: one line per series, oldest sample on the left.
    //
    // A drawn element rather than a tree of Polylines. The plot is redrawn
    // once a second with up to three hundred points per series, and rebuilding
    // that many framework elements each time — each with its own layout,
    // hit-testing and property storage — costs far more than issuing the
    // geometry directly. The legend is the other way round and is built from
    // real controls, because it is a handful of elements that want hover
    // states, tooltips and keyboard focus.
    public sealed class HistoryChart : FrameworkElement {

        // Inside edge of the plot. Enough to keep a two-pixel line and its
        // head dot off the surrounding hairline.
        private const double Pad = 8;

        // The gutter on the right the current-value labels are drawn into, so
        // each trace says where it currently stands against a tick on its own
        // head rather than only in the legend above the plot
        private const double LabelGutter = 52;

        // The gutter on the left the scale is written into.
        //
        // The plot had gridlines and no labels, so a trace sitting a third of
        // the way up the card meant nothing at all: the legend gave the value
        // now, and the shape of the past was unreadable in any unit. Three
        // numbers up the left edge is the whole fix.
        private const double ScaleGutter = 38;

        private HistoryBuffer BufferValue;

        // Where the pointer is over the plot, in element coordinates, or NaN
        // when it is not. Reading a chart by eye gives the shape; this gives
        // the reading at a moment, which is what "when did it spike?" needs.
        private double HoverX = double.NaN;

        // The categorical palette, resolved once from the theme. Slots are
        // assigned in order and never cycled: the ordering is what keeps
        // neighbouring series apart under colour-vision deficiency, so a
        // ninth series is drawn in the neutral rather than reusing the first.
        private readonly Brush[] SeriesBrush = new Brush[8];
        private Brush MutedBrush = Brushes.Gray;
        private Pen GridPen;

        // The value labels are set in the same tabular monospace the readouts
        // use, so a column of them lines its digits up
        private readonly Typeface LabelFace = new Typeface(
            new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // One trace's head, for laying the right-edge value labels out together
        private sealed class Head {
            public double Y;
            public Brush Brush;
            public string Text;
            private double LabelYValue = double.NaN;
            public double LabelY {
                get { return double.IsNaN(this.LabelYValue) ? this.Y : this.LabelYValue; }
                set { this.LabelYValue = value; }
            }
        }

        public HistoryChart() {

            this.Loaded += (s, e) => ResolvePalette();

            // A drawn element has no background, and an element with nothing
            // painted under the pointer is not hit-tested at all, so the plot
            // would never see the mouse. A transparent brush is the usual way
            // to say "I am here" without painting anything.
            this.Background = Brushes.Transparent;

        }

        // Painted first, under everything, purely so the element is hit-tested
        private Brush Background;

        public HistoryBuffer Buffer {
            get { return this.BufferValue; }
            set { this.BufferValue = value; InvalidateVisual(); }
        }

        // Redraws from the current contents of the buffer
        public void Refresh() {
            InvalidateVisual();
        }

        private void ResolvePalette() {

            for(int i = 0; i < this.SeriesBrush.Length; i++)
                this.SeriesBrush[i] = Find("Series" + (i + 1)) ?? Brushes.White;

            this.MutedBrush = Find("SeriesMuted") ?? Brushes.Gray;

            // The grid is a reading aid, not a mark: barely there on purpose,
            // so it never competes with the traces drawn over it
            Brush grid = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            grid.Freeze();
            this.GridPen = new Pen(grid, 1);
            this.GridPen.Freeze();

        }

        private Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        private Brush BrushFor(int slot) {
            return slot >= 0 && slot < this.SeriesBrush.Length && this.SeriesBrush[slot] != null
                ? this.SeriesBrush[slot] : this.MutedBrush;
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e) {

            base.OnMouseMove(e);

            double x = e.GetPosition(this).X;
            if(x == this.HoverX)
                return;

            this.HoverX = x;
            InvalidateVisual();

        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e) {

            base.OnMouseLeave(e);

            if(double.IsNaN(this.HoverX))
                return;

            this.HoverX = double.NaN;
            InvalidateVisual();

        }

        protected override void OnRender(DrawingContext context) {

            if(this.GridPen == null)
                ResolvePalette();

            // The plot leaves a gutter on the right for the value labels, so a
            // trace runs up to its own tick rather than into the card edge,
            // and one on the left for the scale
            double left = ScaleGutter;
            double width = this.ActualWidth - left - Pad - LabelGutter;
            double height = this.ActualHeight - 2 * Pad;

            if(width <= 4 || height <= 4)
                return;

            context.DrawRectangle(this.Background, null,
                new Rect(0, 0, this.ActualWidth, this.ActualHeight));

            double plotRight = left + width;

            HistoryBuffer buffer = this.BufferValue;

            DrawScale(context, buffer, left, height, plotRight);

            if(buffer == null || buffer.Count < 2 || buffer.Capacity < 2)
                return;

            // The newest sample sits against the right edge and the history
            // trails off to the left, so a window that has not filled yet
            // grows leftwards instead of stretching to fit
            double step = width / (buffer.Capacity - 1);
            double baseline = Pad + height;
            double plotLeft = left;

            // Whether to wash the area under each trace down to the baseline.
            //
            // Only when one series is on show. A filled area says "this much
            // of something", which is true of a single trace and false of six:
            // six translucent washes laid over one another stop being six
            // readings and become one brown smear with lines on top. So the
            // fill comes back as the user clicks the legend down to a single
            // series, and stays out of the way while they are comparing.
            int shown = 0;
            foreach(HistorySeries s in buffer.Series)
                if(s.HasValue && s.IsVisible)
                    shown++;

            bool fill = shown == 1;

            // The head of each visible trace, collected as it is drawn so the
            // value labels can be laid out together and nudged apart afterwards
            List<Head> heads = new List<Head>();

            foreach(HistorySeries series in buffer.Series) {

                if(!series.HasValue || !series.IsVisible)
                    continue;

                Brush brush = BrushFor(series.Slot);
                List<Point> run = new List<Point>(buffer.Count);
                int index = 0;
                Point head = new Point(double.NaN, double.NaN);

                foreach(double value in buffer.Samples(series)) {

                    double x = plotLeft + width - (buffer.Count - 1 - index) * step;
                    index++;

                    // A gap breaks the line rather than being interpolated
                    // across: a sensor that said nothing for a minute should
                    // leave a hole, not a straight line implying it was steady
                    if(double.IsNaN(value)) {
                        DrawRun(context, brush, run, baseline, fill);
                        run.Clear();
                        continue;
                    }

                    head = new Point(x, MapY(value, series, height));
                    run.Add(head);

                }

                DrawRun(context, brush, run, baseline, fill);

                if(!double.IsNaN(head.Y))
                    heads.Add(new Head {
                        Y = head.Y,
                        Brush = brush,
                        Text = HistoryBuffer.Format(series.Last) + series.Unit
                    });

            }

            DrawHeadLabels(context, heads, plotRight, height);
            DrawCrosshair(context, buffer, plotLeft, plotRight, width, height, step);

        }

        // The scale up the left edge.
        //
        // A chart whose series each have their own range cannot carry one set
        // of units, so it labels what is actually true of every trace: how far
        // up its own full scale it is sitting. When the user has clicked the
        // legend down to a single series that ambiguity is gone, so the axis
        // switches to that series' real values and units — which is the case
        // where a scale is worth the most anyway.
        private void DrawScale(DrawingContext context, HistoryBuffer buffer,
            double left, double height, double plotRight) {

            HistorySeries only = SoleVisible(buffer);

            for(int k = 0; k <= 2; k++) {

                double y = Snap(Pad + height * k / 2.0);

                context.DrawLine(this.GridPen,
                    new Point(left, y), new Point(plotRight, y));

                // Top to bottom: full, half, none
                double portion = 1 - k / 2.0;

                string label = only != null
                    ? HistoryBuffer.Format(
                        only.Minimum + (only.Maximum - only.Minimum) * portion) + only.Unit
                    : ((int) (portion * 100)) + "%";

                FormattedText text = new FormattedText(label,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    this.LabelFace, 10, this.MutedBrush, 1.25) {
                    TextAlignment = TextAlignment.Right,
                    MaxTextWidth = ScaleGutter - 8
                };

                // The origin of a right-aligned FormattedText is the LEFT edge
                // of its box, and the text is right-aligned inside MaxTextWidth
                // from there — so the box is placed at zero and ends at the
                // gutter, rather than starting at the gutter and running out
                // over the plot. Nudged up by half a line so the number reads
                // as marking the gridline rather than sitting under it.
                context.DrawText(text, new Point(0, y - 6));

            }

        }

        // The one series on show, or null when there are none or several
        private static HistorySeries SoleVisible(HistoryBuffer buffer) {

            if(buffer == null)
                return null;

            HistorySeries found = null;

            foreach(HistorySeries series in buffer.Series) {

                if(!series.HasValue || !series.IsVisible)
                    continue;

                if(found != null)
                    return null;

                found = series;

            }

            return found;

        }

        // Where the pointer is, as a sample.
        //
        // The plot answers "what shape was it" on its own; this answers "what
        // was it at that moment", which is the question anyone looking at a
        // spike actually has. Every visible series is read at the same sample,
        // so the values under the line are comparable.
        private void DrawCrosshair(DrawingContext context, HistoryBuffer buffer,
            double plotLeft, double plotRight, double width, double height, double step) {

            if(double.IsNaN(this.HoverX)
                || this.HoverX < plotLeft || this.HoverX > plotRight)
                return;

            // The newest sample is against the right edge, so the offset is
            // counted back from there
            int back = (int) Math.Round((plotRight - this.HoverX) / step);
            if(back < 0) back = 0;
            if(back > buffer.Count - 1) back = buffer.Count - 1;

            int wanted = buffer.Count - 1 - back;
            double x = Snap(plotRight - back * step);

            Pen line = new Pen(this.MutedBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            line.Freeze();
            context.DrawLine(line, new Point(x, Pad), new Point(x, Pad + height));

            // Read every visible series at that one sample
            List<Head> readings = new List<Head>();

            foreach(HistorySeries series in buffer.Series) {

                if(!series.HasValue || !series.IsVisible)
                    continue;

                int index = 0;
                double found = double.NaN;

                foreach(double value in buffer.Samples(series)) {
                    if(index++ != wanted)
                        continue;
                    found = value;
                    break;
                }

                if(double.IsNaN(found))
                    continue;

                Brush brush = BrushFor(series.Slot);
                double y = MapY(found, series, height);

                context.DrawEllipse(null, new Pen(brush, 2), new Point(x, y), 3, 3);

                readings.Add(new Head {
                    Y = y, Brush = brush,
                    Text = HistoryBuffer.Format(found) + series.Unit
                });

            }

            if(readings.Count == 0)
                return;

            // The stack goes on whichever side of the line has room, so it
            // never runs off the plot when the pointer is near an edge
            const double boxWidth = 62, lineHeight = 14;
            double boxHeight = readings.Count * lineHeight + 8;

            double boxX = x + 10;
            if(boxX + boxWidth > plotRight)
                boxX = x - 10 - boxWidth;

            double boxY = Pad + 4;
            if(boxY + boxHeight > Pad + height)
                boxY = Pad + height - boxHeight;

            // Nearly opaque: the readings have to be legible over whatever
            // trace they happen to land on
            Brush plate = new SolidColorBrush(Color.FromArgb(0xEE, 0x1A, 0x1D, 0x23));
            plate.Freeze();

            context.DrawRoundedRectangle(plate, this.GridPen,
                new Rect(boxX, boxY, boxWidth, boxHeight), 4, 4);

            for(int i = 0; i < readings.Count; i++) {

                FormattedText text = new FormattedText(readings[i].Text,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    this.LabelFace, 10.5, readings[i].Brush, 1.25) {
                    TextAlignment = TextAlignment.Right,
                    MaxTextWidth = boxWidth - 10
                };

                // Same rule as the scale: the origin is the box's left edge and
                // the text right-aligns within MaxTextWidth from it. Drawing
                // at the right edge put every reading outside the plate, which
                // is why the plate looked empty with the numbers beside it.
                context.DrawText(text, new Point(boxX + 5,
                    boxY + 4 + i * lineHeight));

            }

        }

        // The current value of each trace, ticked and labelled at the right
        // edge. The labels are pushed apart vertically where two traces sit on
        // top of one another, so a crossing does not stack their numbers into
        // an unreadable pile.
        private void DrawHeadLabels(DrawingContext context, List<Head> heads,
            double plotRight, double height) {

            if(heads.Count == 0)
                return;

            heads.Sort((a, b) => a.Y.CompareTo(b.Y));

            // Keep a line's worth of space between labels, clamped into the plot
            const double lineHeight = 13;
            for(int i = 1; i < heads.Count; i++)
                if(heads[i].LabelY - heads[i - 1].LabelY < lineHeight)
                    heads[i].LabelY = heads[i - 1].LabelY + lineHeight;

            // Leave a full label's room at the bottom so the lowest one is not
            // clipped against the plot's edge
            double top = Pad, bottom = Pad + height - 16;
            for(int i = heads.Count - 1; i >= 0; i--) {
                if(heads[i].LabelY > bottom) heads[i].LabelY = bottom;
                if(i < heads.Count - 1 && heads[i + 1].LabelY - heads[i].LabelY < lineHeight)
                    heads[i].LabelY = heads[i + 1].LabelY - lineHeight;
            }

            // Clamping the topmost label back into the plot can undo the
            // spacing just made, so the push-down runs once more from the top
            if(heads[0].LabelY < top) heads[0].LabelY = top;
            for(int i = 1; i < heads.Count; i++)
                if(heads[i].LabelY - heads[i - 1].LabelY < lineHeight)
                    heads[i].LabelY = heads[i - 1].LabelY + lineHeight;

            foreach(Head head in heads) {

                // The head dot on the trace already marks the point; the label
                // gets a short tick of its own colour beside it, so a nudged
                // label still reads as belonging to a trace rather than
                // floating. A leader back to the dot would draw a long diagonal
                // that reads as the trace itself plunging, so it is left out.
                Pen tick = new Pen(head.Brush, 2) { StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round };
                tick.Freeze();
                context.DrawLine(tick,
                    new Point(plotRight + 4, head.LabelY + 7),
                    new Point(plotRight + 9, head.LabelY + 7));

                FormattedText text = new FormattedText(head.Text,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    this.LabelFace, 11, head.Brush, 1.25) {
                    TextAlignment = TextAlignment.Left
                };

                context.DrawText(text, new Point(plotRight + 14, head.LabelY));

            }

        }

        // Maps a value to a vertical position using its own series' range
        private double MapY(double value, HistorySeries series, double height) {

            double t = (value - series.Minimum) / (series.Maximum - series.Minimum);
            if(t < 0) t = 0; else if(t > 1) t = 1;

            return Pad + (1 - t) * height;

        }

        // Draws one unbroken stretch of a series
        private void DrawRun(DrawingContext context, Brush brush,
            List<Point> points, double baseline, bool fill) {

            if(points.Count == 0)
                return;

            Color colour = (brush as SolidColorBrush) != null
                ? ((SolidColorBrush) brush).Color : Colors.White;

            if(points.Count == 1) {
                context.DrawEllipse(brush, null, points[0], 2, 2);
                return;
            }

            StreamGeometry line = new StreamGeometry();
            using(StreamGeometryContext draw = line.Open()) {
                draw.BeginFigure(points[0], false, false);
                draw.PolyLineTo(points.GetRange(1, points.Count - 1), true, true);
            }
            line.Freeze();

            if(fill) {

                // A soft wash down to the baseline rather than a flat tint,
                // so the trace is anchored without the fill competing with
                // the line that carries the reading
                StreamGeometry area = new StreamGeometry();
                using(StreamGeometryContext draw = area.Open()) {
                    draw.BeginFigure(new Point(points[0].X, baseline), true, true);
                    draw.PolyLineTo(points, true, true);
                    draw.LineTo(new Point(points[points.Count - 1].X, baseline), true, true);
                }
                area.Freeze();

                LinearGradientBrush wash = new LinearGradientBrush(
                    Color.FromArgb(0x44, colour.R, colour.G, colour.B),
                    Color.FromArgb(0x00, colour.R, colour.G, colour.B),
                    new Point(0, 0), new Point(0, 1));
                wash.Freeze();

                context.DrawGeometry(wash, null, area);

            }

            Pen pen = new Pen(brush, 2) {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();

            context.DrawGeometry(null, pen, line);

            // The newest sample carries a dot, so the right-hand edge shows
            // where each series currently stands
            context.DrawEllipse(brush, null, points[points.Count - 1], 2.5, 2.5);

        }

        // Puts a hairline on a whole pixel so it comes out one pixel wide
        // rather than two half-lit ones
        private static double Snap(double value) {
            return Math.Round(value) + 0.5;
        }

    }

}
