// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Media;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // A row of bars in slots: one bar per core, per zone, per anything the
    // machine has several of.
    //
    // This is CoreStrip generalised. That control existed because a single
    // figure — the hottest sensor in the machine — says less the more cores
    // there are: one core pinned and every core hot read identically through
    // it. The same argument applies to per-core clocks, which the processor
    // has reported all along and nothing has ever shown, so the band and the
    // colouring are parameters now rather than constants.
    //
    // Drawn rather than assembled: twenty rectangles redrawn each reading are
    // far cheaper as geometry than as twenty framework elements, and markup
    // cannot name a type from this assembly.
    public sealed class BarStrip : FrameworkElement {

        private double[] ValuesField = new double[0];
        private Brush FillField;

        // The band the bars are scaled against. The defaults are the
        // temperature band CoreStrip used; a caller showing clocks or load
        // sets its own.
        private double FloorField = 30, CeilingField = 100;

        // Below the floor a bar is drawn as a stub rather than as nothing, so
        // an idle processor still reads as a row of cores rather than as an
        // empty card
        private const double Stub = 0.06;

        private Brush Track, Good, Warning, Serious, Critical, Muted;

        public BarStrip() {
            this.UsesHealthBands = true;
            this.Loaded += (s, e) => ResolveTheme();
        }

        public double[] Values {
            get { return this.ValuesField; }
            set {
                this.ValuesField = value ?? new double[0];
                InvalidateVisual();
            }
        }

        // Convenience for the callers that already hold integers — the
        // per-core temperatures arrive from the hardware layer that way
        public int[] Integers {
            set {
                if(value == null) {
                    this.Values = null;
                    return;
                }
                double[] converted = new double[value.Length];
                for(int i = 0; i < value.Length; i++)
                    converted[i] = value[i];
                this.Values = converted;
            }
        }

        public double Floor {
            get { return this.FloorField; }
            set { this.FloorField = value; InvalidateVisual(); }
        }

        public double Ceiling {
            get { return this.CeilingField; }
            set { this.CeilingField = value; InvalidateVisual(); }
        }

        // True: each bar takes its colour from the health band its value falls
        // in, which is what a temperature wants. False: every bar is Fill,
        // which is what a clock or a load wants — there is no such thing as a
        // dangerous clock speed, and colouring one red says there is.
        public bool UsesHealthBands { get; set; }

        public Brush Fill {
            get { return this.FillField; }
            set { this.FillField = value; InvalidateVisual(); }
        }

        private void ResolveTheme() {
            this.Track = Find("Inset") ?? Brushes.Black;
            this.Good = Find("Good") ?? Brushes.Green;
            this.Warning = Find("Warning") ?? Brushes.Yellow;
            this.Serious = Find("Serious") ?? Brushes.Orange;
            this.Critical = Find("Critical") ?? Brushes.Red;
            this.Muted = Find("TextMuted") ?? Brushes.Gray;
        }

        private Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        private Brush BrushFor(double value) {

            if(!this.UsesHealthBands)
                return value <= 0 ? this.Muted : (this.FillField ?? this.Good);

            if(value <= 0) return this.Muted;
            if(value < HealthScale.WarningC) return this.Good;
            if(value < HealthScale.SeriousC) return this.Warning;
            if(value < HealthScale.CriticalC) return this.Serious;
            return this.Critical;

        }

        protected override void OnRender(DrawingContext context) {

            if(this.Good == null)
                ResolveTheme();

            double[] values = this.ValuesField;
            if(values == null || values.Length == 0)
                return;

            double width = this.ActualWidth, height = this.ActualHeight;
            if(width <= 8 || height <= 8)
                return;

            double span = this.CeilingField - this.FloorField;
            if(span <= 0)
                return;

            int count = values.Length;

            // A gap proportional to the bar, so a sixteen-core strip and a
            // four-core one both look deliberate rather than one looking padded
            double gap = count > 24 ? 2 : count > 12 ? 3 : 5;
            double barWidth = (width - gap * (count - 1)) / count;
            if(barWidth < 1)
                barWidth = 1;

            double radius = Math.Min(barWidth / 2, 3);

            for(int i = 0; i < count; i++) {

                double x = i * (barWidth + gap);

                // The full-height track the bar rises within, so a cool core
                // reads as a low bar in a slot rather than as empty space
                Rect slot = new Rect(x, 0, barWidth, height);
                context.DrawRoundedRectangle(this.Track, null, slot, radius, radius);

                double value = values[i];
                if(double.IsNaN(value))
                    continue;

                double portion = (value - this.FloorField) / span;
                if(portion < Stub) portion = Stub; else if(portion > 1) portion = 1;

                double barHeight = height * portion;
                Rect bar = new Rect(x, height - barHeight, barWidth, barHeight);

                context.DrawRoundedRectangle(BrushFor(value), null, bar, radius, radius);

            }

        }

    }

}
