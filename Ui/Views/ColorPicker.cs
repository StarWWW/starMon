// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace StarMon.Ui.Views {

    // A hue/saturation/value colour picker, drawn.
    //
    // The keyboard used to be recoloured by typing six hex digits into a box —
    // which is a way to enter a colour you already know, not a way to choose
    // one. This is the way to choose one: a saturation-value field for the
    // current hue, a hue bar under it, and a row of presets for the colours
    // people actually reach for on a keyboard.
    //
    // Hand-drawn rather than assembled from controls for the same reason the
    // keyboard diagram is: naming a type from this assembly in markup forces a
    // markup-compilation pass the project cannot run, and a picker is mostly
    // two draggable fields and a hit test anyway.
    public sealed class ColorPicker : FrameworkElement {

        // The saturation-value field, the hue bar under it and the preset row,
        // as fractions and fixed heights measured from the top
        private const double FieldHeight = 150;
        private const double Gap = 14;
        private const double HueHeight = 16;
        private const double PresetSize = 24;
        private const double PresetGap = 8;

        private static readonly Color[] Presets = {
            Color.FromRgb(0xFF, 0x3B, 0x30), // Red
            Color.FromRgb(0xFF, 0x6A, 0x00), // Orange
            Color.FromRgb(0xFF, 0xCC, 0x00), // Yellow
            Color.FromRgb(0x34, 0xC7, 0x59), // Green
            Color.FromRgb(0x00, 0xC7, 0xBE), // Teal
            Color.FromRgb(0x0A, 0x84, 0xFF), // Blue
            Color.FromRgb(0x5E, 0x5C, 0xE6), // Indigo
            Color.FromRgb(0xBF, 0x5A, 0xF2), // Purple
            Color.FromRgb(0xFF, 0x2D, 0x55), // Pink
            Color.FromRgb(0xFF, 0xFF, 0xFF)  // White
        };

        private double Hue;   // 0..360
        private double Sat;   // 0..1
        private double Val;   // 0..1

        private bool DraggingField;
        private bool DraggingHue;

        private Rect FieldRect;
        private Rect HueRect;

        public ColorPicker() {
            this.Focusable = false;
            this.Val = 1;
        }

        // Raised as the colour changes, once per interaction step, so the zone
        // it drives is written to as the field is dragged rather than only when
        // it is let go
        public event Action<Color> ColorChanged;

        public Color Color {
            get { return FromHsv(this.Hue, this.Sat, this.Val); }
            set {
                ToHsv(value, out this.Hue, out this.Sat, out this.Val);
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext context) {

            double width = this.ActualWidth;
            if(width <= 20)
                return;

            this.FieldRect = new Rect(0, 0, width, FieldHeight);
            this.HueRect = new Rect(0, FieldHeight + Gap, width, HueHeight);

            DrawField(context);
            DrawHue(context);
            DrawPresets(context);

        }

        // The saturation-value field: white to the hue across, clear to black
        // down, with a ring marking the chosen point
        private void DrawField(DrawingContext context) {

            Color pure = FromHsv(this.Hue, 1, 1);

            LinearGradientBrush across = new LinearGradientBrush(
                Colors.White, pure, new Point(0, 0), new Point(1, 0));
            across.Freeze();

            LinearGradientBrush down = new LinearGradientBrush(
                Color.FromArgb(0x00, 0, 0, 0), Colors.Black,
                new Point(0, 0), new Point(0, 1));
            down.Freeze();

            context.DrawRoundedRectangle(across, null, this.FieldRect, 8, 8);
            context.DrawRoundedRectangle(down, null, this.FieldRect, 8, 8);

            double x = this.FieldRect.Left + this.Sat * this.FieldRect.Width;
            double y = this.FieldRect.Top + (1 - this.Val) * this.FieldRect.Height;

            DrawThumb(context, new Point(x, y), 8);

        }

        // The hue bar: the spectrum, with a thumb at the current hue
        private void DrawHue(DrawingContext context) {

            LinearGradientBrush spectrum = new LinearGradientBrush {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };

            for(int i = 0; i <= 6; i++)
                spectrum.GradientStops.Add(new GradientStop(
                    FromHsv(i * 60, 1, 1), i / 6.0));

            spectrum.Freeze();

            context.DrawRoundedRectangle(spectrum, null, this.HueRect,
                HueHeight / 2, HueHeight / 2);

            double x = this.HueRect.Left + (this.Hue / 360.0) * this.HueRect.Width;
            DrawThumb(context, new Point(x, this.HueRect.Top + HueHeight / 2),
                HueHeight / 2 + 2);

        }

        private void DrawPresets(DrawingContext context) {

            double top = this.HueRect.Bottom + Gap + 4;

            for(int i = 0; i < Presets.Length; i++) {

                Rect rect = new Rect(
                    i * (PresetSize + PresetGap), top, PresetSize, PresetSize);

                if(rect.Right > this.ActualWidth)
                    break;

                SolidColorBrush fill = new SolidColorBrush(Presets[i]);
                fill.Freeze();

                Pen edge = new Pen(new SolidColorBrush(
                    Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
                edge.Freeze();

                context.DrawRoundedRectangle(fill, edge, rect, 6, 6);

            }

        }

        // A thumb that stays visible over any colour: a white ring with a dark
        // hairline inside it
        private static void DrawThumb(DrawingContext context, Point at, double radius) {

            Pen halo = new Pen(new SolidColorBrush(
                Color.FromArgb(0x80, 0, 0, 0)), 3);
            halo.Freeze();

            Pen ring = new Pen(Brushes.White, 2);
            ring.Freeze();

            context.DrawEllipse(null, halo, at, radius, radius);
            context.DrawEllipse(null, ring, at, radius, radius);

        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {

            base.OnMouseLeftButtonDown(e);
            Point at = e.GetPosition(this);

            if(this.FieldRect.Contains(at)) {
                this.DraggingField = true;
                CaptureMouse();
                UpdateField(at);
            } else if(this.HueRect.Contains(at)) {
                this.DraggingHue = true;
                CaptureMouse();
                UpdateHue(at);
            } else {
                PickPreset(at);
            }

        }

        protected override void OnMouseMove(MouseEventArgs e) {

            base.OnMouseMove(e);

            if(!this.DraggingField && !this.DraggingHue)
                return;

            Point at = e.GetPosition(this);
            if(this.DraggingField)
                UpdateField(at);
            else
                UpdateHue(at);

        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
            base.OnMouseLeftButtonUp(e);
            this.DraggingField = false;
            this.DraggingHue = false;
            ReleaseMouseCapture();
        }

        private void UpdateField(Point at) {
            this.Sat = Clamp((at.X - this.FieldRect.Left) / this.FieldRect.Width);
            this.Val = 1 - Clamp((at.Y - this.FieldRect.Top) / this.FieldRect.Height);
            Changed();
        }

        private void UpdateHue(Point at) {
            this.Hue = Clamp((at.X - this.HueRect.Left) / this.HueRect.Width) * 360;
            Changed();
        }

        private void PickPreset(Point at) {

            double top = this.HueRect.Bottom + Gap + 4;
            if(at.Y < top || at.Y > top + PresetSize)
                return;

            int i = (int) (at.X / (PresetSize + PresetGap));
            if(i < 0 || i >= Presets.Length)
                return;

            Rect rect = new Rect(i * (PresetSize + PresetGap), top, PresetSize, PresetSize);
            if(!rect.Contains(at))
                return;

            Color = Presets[i];
            Raise();

        }

        private void Changed() {
            InvalidateVisual();
            Raise();
        }

        private void Raise() {
            Action<Color> handler = this.ColorChanged;
            if(handler != null)
                handler(Color);
        }

        private static double Clamp(double value) {
            return value < 0 ? 0 : value > 1 ? 1 : value;
        }

        // HSV → RGB. Hue in degrees, saturation and value in 0..1.
        private static Color FromHsv(double hue, double sat, double val) {

            double c = val * sat;
            double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            double m = val - c;

            double r = 0, g = 0, b = 0;

            if(hue < 60) { r = c; g = x; }
            else if(hue < 120) { r = x; g = c; }
            else if(hue < 180) { g = c; b = x; }
            else if(hue < 240) { g = x; b = c; }
            else if(hue < 300) { r = x; b = c; }
            else { r = c; b = x; }

            return Color.FromRgb(
                (byte) Math.Round((r + m) * 255),
                (byte) Math.Round((g + m) * 255),
                (byte) Math.Round((b + m) * 255));

        }

        // RGB → HSV, so a colour set from outside places the thumbs correctly
        private static void ToHsv(Color colour, out double hue,
            out double sat, out double val) {

            double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            val = max;
            sat = max <= 0 ? 0 : delta / max;

            if(delta <= 0) {
                hue = 0;
                return;
            }

            if(max == r)
                hue = 60 * (((g - b) / delta) % 6);
            else if(max == g)
                hue = 60 * ((b - r) / delta + 2);
            else
                hue = 60 * ((r - g) / delta + 4);

            if(hue < 0)
                hue += 360;

        }

    }

}
