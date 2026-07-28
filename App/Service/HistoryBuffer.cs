// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StarMon.AppService {

    // One plotted series and its rolling window.
    //
    // Each series carries its own value range, because the graph shows metrics
    // that share nothing but a time axis: a temperature, a fan speed, a clock
    // in gigahertz and a load percentage cannot be plotted against one scale
    // without three of them being flat lines.
    public sealed class HistorySeries {

        internal double[] Data;

        internal HistorySeries(string label, double min, double max,
            string unit, int slot, int capacity) {

            this.Label = label;
            this.Unit = unit ?? "";
            this.Minimum = min;
            this.Maximum = max > min ? max : min + 1;
            this.Slot = slot;
            this.Data = new double[capacity];
            this.IsVisible = true;

        }

        // Settable after construction so a language change can rename the
        // series without rebuilding the buffer, which would drop the history
        public string Label { get; internal set; }
        public string Unit { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }

        // Which categorical palette slot draws this series. An index rather
        // than a colour: the palette's order is what keeps neighbouring series
        // apart for colour-blind readers, so a series takes the next slot and
        // never picks one.
        public int Slot { get; private set; }

        // Whether any real sample has arrived. A series that has never
        // reported is left out of the legend rather than drawn as a flat line
        // along the bottom, which would read as a reading of zero.
        public bool HasValue { get; internal set; }

        public double Last { get; internal set; }

        // Toggled by clicking the legend
        public bool IsVisible { get; set; }

    }

    // The history the graph draws, with no graph attached.
    //
    // This used to live inside the WinForms control, which meant the ring
    // buffer arithmetic and the CSV export could only be tested by
    // constructing a window. The arithmetic is the part most worth testing:
    // once the buffer has wrapped, the oldest sample is no longer at index
    // zero, and an off-by-one there shows up as an export that silently starts
    // in the middle of the history.
    public sealed class HistoryBuffer {

        // The shortest window worth keeping. A handful of samples cannot show
        // a trend, and a capacity of one or two makes the wrap arithmetic
        // degenerate rather than merely short.
        public const int MinimumCapacity = 8;

        private readonly List<HistorySeries> SeriesList = new List<HistorySeries>();

        // Where the next sample goes, and how many the buffer holds
        private int Head;
        private int Filled;

        public HistoryBuffer() {
            this.Capacity = 120;
        }

        public int Capacity { get; private set; }

        // How many samples are held, which is the capacity once it has wrapped
        public int Count { get { return this.Filled; } }

        public IList<HistorySeries> Series { get { return this.SeriesList; } }

        // Begins configuration: drops every series and sets the window length
        public void Begin(int capacity) {
            this.Capacity = Math.Max(MinimumCapacity, capacity);
            this.SeriesList.Clear();
            this.Head = 0;
            this.Filled = 0;
        }

        // Adds a series, which takes the next palette slot
        public HistorySeries Add(string label, double min, double max, string unit) {

            HistorySeries series = new HistorySeries(
                label, min, max, unit, this.SeriesList.Count, this.Capacity);

            this.SeriesList.Add(series);
            return series;

        }

        // Records one sample per series.
        //
        // A value of zero or less is a gap rather than a reading. Every metric
        // here is one where zero means the sensor said nothing: a fan that is
        // genuinely stopped still reports its state through a different path,
        // and a processor at 0 °C has not been built.
        public void Push(params double[] values) {

            if(this.SeriesList.Count == 0)
                return;

            for(int i = 0; i < this.SeriesList.Count; i++) {

                double value = i < values.Length ? values[i] : 0;
                HistorySeries series = this.SeriesList[i];

                if(value <= 0) {
                    series.Data[this.Head] = double.NaN;
                } else {
                    series.Data[this.Head] = value;
                    series.HasValue = true;
                    series.Last = value;
                }

            }

            this.Head = (this.Head + 1) % this.Capacity;

            if(this.Filled < this.Capacity)
                this.Filled++;

        }

        // Changes the window length, carrying the samples over.
        //
        // This used to drop them, on the argument that a longer window cannot
        // be filled from a shorter one and so keeping half of it made the two
        // directions behave differently. They are not different: the buffer
        // holds what it holds and the plot draws what it has, which is already
        // true for the first two minutes after the application starts. What
        // the old behaviour actually did was punish the user for asking a
        // question — switch to ten minutes to look at a trend and the two
        // minutes of trend on the screen vanished.
        //
        // Widening keeps everything and fills in from the left as new samples
        // arrive; narrowing keeps the newest that fit.
        public void SetCapacity(int capacity) {

            int target = Math.Max(MinimumCapacity, capacity);
            if(target == this.Capacity)
                return;

            int keep = Math.Min(this.Filled, target);

            foreach(HistorySeries series in this.SeriesList) {

                double[] moved = new double[target];

                for(int i = 0; i < target; i++)
                    moved[i] = double.NaN;

                // The newest samples the new window has room for, oldest
                // first, laid out from the start so the ring unwraps
                for(int i = 0; i < keep; i++) {
                    int from = (this.Head - keep + i + this.Capacity) % this.Capacity;
                    moved[i] = series.Data[from];
                }

                series.Data = moved;

            }

            this.Capacity = target;
            this.Filled = keep;

            // Not zero: the next sample goes after the ones just kept. A full
            // buffer wraps back to the start, which is what the modulus is for.
            this.Head = keep % target;

        }

        // The samples held for a series, oldest first, with gaps as NaN.
        //
        // Every consumer wants them in this order — the plot draws left to
        // right, the export reads top to bottom — and every consumer that
        // walks the raw ring itself is a chance to get the wrap wrong.
        public IEnumerable<double> Samples(HistorySeries series) {

            for(int i = 0; i < this.Filled; i++) {
                int index = (this.Head - this.Filled + i + this.Capacity) % this.Capacity;
                yield return series.Data[index];
            }

        }

        // Per-series current value with its minimum, mean and maximum over
        // the window
        public string BuildSummary() {

            StringBuilder text = new StringBuilder();

            foreach(HistorySeries series in this.SeriesList) {

                if(!series.HasValue)
                    continue;

                double min = double.MaxValue, max = double.MinValue, sum = 0;
                int count = 0;

                foreach(double value in series.Data) {
                    if(double.IsNaN(value))
                        continue;
                    if(value < min) min = value;
                    if(value > max) max = value;
                    sum += value;
                    count++;
                }

                if(count == 0)
                    continue;

                if(text.Length > 0)
                    text.Append('\n');

                text.Append(series.Label).Append(": ")
                    .Append(Format(series.Last)).Append(series.Unit)
                    .Append("   (min ").Append(Format(min))
                    .Append("  avg ").Append(Format(sum / count))
                    .Append("  max ").Append(Format(max)).Append(")");

            }

            return text.ToString();

        }

        // One row per sample, oldest first, one column per series.
        //
        // Gaps are written as empty cells rather than zeroes, so a sensor that
        // was unavailable is not mistaken for one reading zero — which is the
        // whole reason the buffer distinguishes them.
        public string BuildCsv() {

            CultureInfo invariant = CultureInfo.InvariantCulture;
            StringBuilder text = new StringBuilder(4096);

            text.Append("Sample");
            foreach(HistorySeries series in this.SeriesList)
                text.Append(',').Append(Quote(series.Unit.Length > 0
                    ? series.Label + " (" + series.Unit.Trim() + ")" : series.Label));
            text.Append(Environment.NewLine);

            for(int i = 0; i < this.Filled; i++) {

                int index = (this.Head - this.Filled + i + this.Capacity) % this.Capacity;

                text.Append((i + 1).ToString(invariant));

                foreach(HistorySeries series in this.SeriesList) {
                    text.Append(',');
                    double value = series.Data[index];
                    if(!double.IsNaN(value))
                        text.Append(value.ToString("0.###", invariant));
                }

                text.Append(Environment.NewLine);

            }

            return text.ToString();

        }

        // One decimal below ten, where the range is small enough for it to
        // matter (a clock in gigahertz); a whole number above
        public static string Format(double value) {
            return value < 10
                ? value.ToString("0.0", CultureInfo.InvariantCulture)
                : ((int) Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }

        // Quotes a field only when it needs it
        private static string Quote(string text) {
            if(text == null)
                return "";
            return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? text : "\"" + text.Replace("\"", "\"\"") + "\"";
        }

    }

}
