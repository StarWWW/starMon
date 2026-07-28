// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StarMon.Library;

namespace StarMon.Ui.Shell {

    // The notification icon, drawn at runtime.
    //
    // It carries the current temperature, so it is redrawn rather than loaded.
    // The WinForms version did this with GDI+ into a Bitmap and GetHicon; this
    // draws with WPF and converts once, through IconRender.
    //
    // The diamond is not decoration. A tray icon is sixteen pixels of a row of
    // sixteen-pixel icons, and a shape that is not a square is what makes this
    // one findable at a glance. Its colour says which fan mode is in force,
    // which is the one piece of state worth carrying without opening anything.
    public sealed class DynamicIcon {

        public enum Backdrop {
            None,     // The figures alone
            Outline,  // A diamond outline
            Cool,     // Filled, cool gradient — the ordinary fan modes
            Warm,     // Filled, warm gradient — performance
            Mark      // The application's own colours, carrying no reading
        }

        private readonly TrayIcon Icon;

        private Backdrop BackdropValue = Backdrop.Cool;
        private string LastText;
        private bool LastDynamic;
        private Backdrop LastBackdrop;

        // Whether anything has been drawn yet.
        //
        // Tracked separately rather than inferred from the fields above,
        // because "nothing has been drawn" and "the last thing drawn was the
        // static icon" have the same values in all of them — and reading the
        // first as the second is how the icon came to be added to the tray
        // with no picture in it at all.
        private bool HasDrawn;

        public DynamicIcon(TrayIcon icon) {

            this.Icon = icon;
            this.IsDynamic = Config.GuiDynamicIcon;
            this.HasBackdrop = Config.GuiDynamicIconHasBackground;

            // Draw before the icon is ever shown, so the shell is handed a
            // picture when it is asked to add one rather than a null handle
            Update("");

        }

        public bool IsDynamic { get; set; }
        public bool HasBackdrop { get; set; }

        public Backdrop Background {
            get { return this.BackdropValue; }
            set { this.BackdropValue = value; }
        }

        // The icon is drawn larger than the shell's nominal small-icon size
        // and handed over at that size, which is what the notification area
        // has wanted since it started being asked to scale: it downsamples a
        // larger icon far better than it upsamples a smaller one.
        private static int Size {
            get {
                int size = (int) SystemParameters.SmallIconWidth;
                if(size <= 0)
                    size = 16;
                return size * Config.GuiDynamicIconUpscaleRatio;
            }
        }

        // Redraws the icon if anything about it has changed.
        //
        // The check is not an optimisation. Handing the shell a new icon
        // handle makes it redraw the notification area, so an icon replaced
        // once a second whether or not it differs is a tray that flickers.
        public void Update(string text) {

            if(!this.IsDynamic) {

                if(this.HasDrawn && !this.LastDynamic)
                    return;

                this.HasDrawn = true;
                this.LastDynamic = false;
                this.LastText = null;

                SetStatic();
                return;

            }

            if(this.HasDrawn
                && this.LastDynamic
                && this.LastText == text
                && this.LastBackdrop == this.BackdropValue)
                return;

            this.HasDrawn = true;
            this.LastDynamic = true;
            this.LastText = text;
            this.LastBackdrop = this.BackdropValue;

            try {
                this.Icon.SetIcon(Draw(text ?? ""));
            } catch(Exception e) {
                Logger.Error("Icon", "The notification icon could not be drawn",
                    e.Message);
            }

        }

        // Forces the next Update to redraw, whatever it is asked for
        public void Invalidate() {
            this.HasDrawn = false;
        }

        // The icon when it is not carrying a reading: the application's mark,
        // which is the same diamond in the accent rather than the two-colour
        // gradient the live one uses. A resting icon should say which
        // application it is, not imply a state it is not reporting.
        private void SetStatic() {
            try {
                this.Icon.SetIcon(IconRender.FromVisual(
                    Compose("", Backdrop.Mark, true, Size), Size));
            } catch(Exception e) {
                Logger.Error("Icon", "The notification icon could not be drawn",
                    e.Message);
            }
        }

        // The icon as a bitmap rather than a handle, so it can be looked at
        // during development. A tray icon is sixteen pixels and is the one
        // surface a mistake in is easiest to ship: it is too small to notice
        // being wrong and too far away to check.
        public static System.Windows.Media.Imaging.BitmapSource Preview(
            string text, Backdrop backdrop, bool hasBackdrop, int size) {

            System.Windows.Media.Imaging.RenderTargetBitmap bitmap =
                new System.Windows.Media.Imaging.RenderTargetBitmap(
                    size, size, size * 3, size * 3, PixelFormats.Pbgra32);

            bitmap.Render(Compose(text, backdrop, hasBackdrop, size));
            return bitmap;

        }

        private IntPtr Draw(string text) {

            int size = Size;

            return IconRender.FromVisual(
                Compose(text, this.BackdropValue, this.HasBackdrop, size),
                size);

        }

        private static DrawingVisual Compose(string text, Backdrop backdrop,
            bool hasBackdrop, int size) {

            DrawingVisual visual = new DrawingVisual();

            using(DrawingContext context = visual.RenderOpen()) {

                double edge = size;
                Point top = new Point(edge / 2, 0.5);
                Point right = new Point(edge - 0.5, edge / 2);
                Point bottom = new Point(edge / 2, edge - 0.5);
                Point left = new Point(0.5, edge / 2);

                StreamGeometry diamond = new StreamGeometry();
                using(StreamGeometryContext draw = diamond.Open()) {
                    draw.BeginFigure(top, true, true);
                    draw.LineTo(right, true, true);
                    draw.LineTo(bottom, true, true);
                    draw.LineTo(left, true, true);
                }
                diamond.Freeze();

                if(backdrop == Backdrop.Mark) {

                    // The brand's violet stroke, matching the mark in the
                    // window's title bar
                    LinearGradientBrush mark = new LinearGradientBrush(
                        Color.FromRgb(0x8B, 0x5C, 0xF6),
                        Color.FromRgb(0x5B, 0x3B, 0xD6),
                        new Point(0, 0), new Point(1, 1));
                    mark.Freeze();

                    context.DrawGeometry(mark, null, diamond);

                } else if(hasBackdrop
                    && (backdrop == Backdrop.Cool || backdrop == Backdrop.Warm)) {

                    // Cool sweeps top to bottom and warm left to right, which
                    // is inherited: at this size the direction is not read as
                    // direction, but it does make the two shades distinct
                    // enough to tell apart out of the corner of an eye.
                    bool cool = backdrop == Backdrop.Cool;

                    LinearGradientBrush fill = new LinearGradientBrush(
                        FromArgb(cool ? Config.GuiColorCoolDark : Config.GuiColorWarmDark),
                        FromArgb(cool ? Config.GuiColorCoolLite : Config.GuiColorWarmLite),
                        new Point(0, 0),
                        cool ? new Point(0, 1) : new Point(1, 0));
                    fill.Freeze();

                    context.DrawGeometry(fill, null, diamond);

                } else if(backdrop == Backdrop.Outline) {

                    Pen pen = new Pen(Brushes.White, 1);
                    pen.Freeze();
                    context.DrawGeometry(null, pen, diamond);

                }

                if(text.Length > 0)
                    DrawText(context, text, size);

            }

            return visual;

        }

        // The figures, fitted to the width rather than set at a fixed size.
        //
        // The icon shows one, two or three characters depending on the
        // temperature and the unit, and a size chosen for two leaves three
        // spilling over the edge — which on a tray icon means an unreadable
        // smear rather than a slightly tight fit.
        private static void DrawText(DrawingContext context, string text, int size) {

            Typeface face = new Typeface(
                new FontFamily("Segoe UI Variable Display, Segoe UI Semibold, Segoe UI"),
                FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            double height = size * Config.GuiDynamicIconFontSizeRatio;

            FormattedText formatted = new FormattedText(text,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                face, height, Brushes.White, 3.0);

            // The bare number fills more of the tile now the unit lives in the
            // tooltip, so it is fitted to a wider box before being shrunk
            double available = size * 0.92;
            if(formatted.Width > available && formatted.Width > 0) {
                height *= available / formatted.Width;
                formatted = new FormattedText(text,
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    face, height, Brushes.White, 3.0);
            }

            Point origin = new Point(
                (size - formatted.Width) / 2,
                (size - formatted.Height) / 2);

            // A dark halo under white figures, so the reading stays legible on
            // the warm gradient, the cool one, or none at all. Drawn as the
            // glyph outline stroked first and the white fill laid over it,
            // rather than a fill-plus-stroke that would eat into the figures.
            Geometry glyphs = formatted.BuildGeometry(origin);

            Pen halo = new Pen(
                new SolidColorBrush(Color.FromArgb(0xD0, 0, 0, 0)),
                Math.Max(2.0, size * 0.09)) {
                LineJoin = PenLineJoin.Round
            };
            halo.Freeze();

            context.DrawGeometry(null, halo, glyphs);
            context.DrawGeometry(Brushes.White, null, glyphs);

        }

        private static Color FromArgb(int value) {
            return Color.FromArgb(
                (byte) ((value >> 24) & 0xFF),
                (byte) ((value >> 16) & 0xFF),
                (byte) ((value >> 8) & 0xFF),
                (byte) (value & 0xFF));
        }

    }

}
