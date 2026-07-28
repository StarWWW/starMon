// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StarMon.Ui.Views;

namespace StarMon.Ui.Design {

    public partial class Gallery : UserControl {

        public Gallery() {

            InitializeComponent();

            // The drawn controls are placed here rather than in the markup:
            // naming a type from this assembly in XAML forces a second markup
            // compilation pass the build cannot run. Every view in the
            // application does the same, so the gallery shows the components
            // the way they are actually used.
            this.SparkLead.Content = Spark(Rising(48), "Serious", true);
            this.SparkRowA.Content = Spark(Noisy(40, 6), "Series6", false);
            this.SparkRowB.Content = Spark(Rising(40), "Series2", false);
            this.SparkRowC.Content = Spark(Flat(40), "Series5", false);

            this.BarsHealth.Content = new BarStrip {
                Integers = new int[] { 62, 64, 61, 78, 90, 88, 91, 74, 63, 60, 66, 71 },
                Floor = 30, Ceiling = 100
            };

            // Clocks are not a health scale, so the bars are one colour: a
            // core running slowly is not a core in trouble
            this.BarsFlat.Content = new BarStrip {
                Integers = new int[] { 3900, 3940, 3880, 4100, 2200, 2210, 4090, 3870,
                                       3910, 3860, 3930, 3990 },
                Floor = 800, Ceiling = 4400,
                UsesHealthBands = false,
                Fill = Brush("Series1")
            };

        }

        private static Sparkline Spark(double[] values, string key, bool area) {
            return new Sparkline {
                Values = values,
                Stroke = Brush(key),
                HasArea = area
            };
        }

        private static Brush Brush(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        // Sample shapes. Deterministic rather than random, so a difference
        // between two renders is a change to the control and never to the data.

        private static double[] Rising(int count) {
            double[] values = new double[count];
            for(int i = 0; i < count; i++)
                values[i] = 30 + i * 0.9 + Math.Sin(i / 3.0) * 4;
            return values;
        }

        private static double[] Noisy(int count, double amplitude) {
            double[] values = new double[count];
            for(int i = 0; i < count; i++)
                values[i] = 50 + Math.Sin(i / 2.3) * amplitude + Math.Cos(i / 5.7) * amplitude;
            return values;
        }

        // A run that barely moves. The control must draw this as the flat line
        // it is rather than scaling the last of the sensor noise into a
        // mountain range, which is what auto-scaling does if it is not guarded.
        private static double[] Flat(int count) {
            double[] values = new double[count];
            for(int i = 0; i < count; i++)
                values[i] = 62;
            return values;
        }

    }

}
