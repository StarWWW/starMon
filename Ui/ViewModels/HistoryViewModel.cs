// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.ObjectModel;
using StarMon.AppService;

namespace StarMon.Ui.ViewModels {

    // One entry in the chart's legend.
    //
    // A wrapper around the series rather than the series itself: the buffer
    // lives in the service layer and knows nothing about change notification,
    // and it should stay that way — it is the part that has to be cheap to
    // write to once a second from a background thread.
    public sealed class SeriesLegendViewModel : Observable {

        private readonly HistorySeries Source;

        public SeriesLegendViewModel(HistorySeries source) {
            this.Source = source;
        }

        public string Label { get { return this.Source.Label; } }

        // Which palette slot draws it, so the legend chip and the trace cannot
        // disagree about which colour belongs to which series
        public int Slot { get { return this.Source.Slot; } }

        public string Value {
            get {
                return this.Source.HasValue
                    ? HistoryBuffer.Format(this.Source.Last) + this.Source.Unit : "";
            }
        }

        public bool HasValue { get { return this.Source.HasValue; } }

        // Clicking the legend hides a series without dropping its history, so
        // it comes back with its past intact rather than starting again
        public bool IsVisible {
            get { return this.Source.IsVisible; }
            set {
                if(this.Source.IsVisible == value)
                    return;
                this.Source.IsVisible = value;
                Raise();
            }
        }

        // Called after a sample arrives: the underlying value changed without
        // going through a setter here, so the change has to be announced
        internal void Refresh() {
            Raise("Value");
            Raise("HasValue");
        }

        // Called after the series has been renamed under this entry
        internal void RefreshLabel() {
            Raise("Label");
        }

    }

    // The history chart, its legend and the window it covers
    public sealed class HistoryViewModel : Observable {

        // The windows the user can pick between, in samples. At one sample a
        // second these are two, five and ten minutes.
        public static readonly int[] Windows = { 120, 300, 600 };

        private string SummaryValue = "";
        private int WindowValue = Windows[0];

        public HistoryViewModel() {

            this.Buffer = new HistoryBuffer();
            this.Legend = new ObservableCollection<SeriesLegendViewModel>();

            this.ExportCommand = new RelayCommand(RaiseExport, () => this.Buffer.Count > 0);

        }

        // Raised when the user asks for the history as a file. The view model
        // will not open a save dialog: that is the view's business, and the
        // log panel already settled this shape.
        public event System.Action<string> ExportRequested;

        public RelayCommand ExportCommand { get; private set; }

        // The buffer has been able to produce a CSV since it was written, and
        // it has been called by nothing but its own test. A window's worth of
        // every sensor the machine reports is the one thing a user actually
        // wants to take away from a monitoring application, and there was no
        // way to.
        private void RaiseExport() {

            System.Action<string> handler = this.ExportRequested;
            if(handler != null)
                handler(this.Buffer.BuildCsv());

        }

        // How much history the chart covers, in samples. Windows has offered
        // three since it was written and only the first was ever used:
        // SetCapacity had no caller at all, so the chart was fixed at two
        // minutes with no way to say otherwise.
        public int Window {
            get { return this.WindowValue; }
            set {

                if(!Set(ref this.WindowValue, value))
                    return;

                // Keeps the samples it already has. Growing the window shows
                // the past filling in from the left rather than starting the
                // history again, which is what a user changing the window is
                // asking to see.
                this.Buffer.SetCapacity(value);

                Raise("IsWindowShort");
                Raise("IsWindowMedium");
                Raise("IsWindowLong");

                this.Summary = this.Buffer.BuildSummary();

            }
        }

        // The paired booleans the segmented selector needs, for the reason
        // documented on the fan modes: a converter on a two-way ToggleButton
        // flickers the group through a state where nothing is selected.
        public bool IsWindowShort {
            get { return this.WindowValue == Windows[0]; }
            set { if(value) this.Window = Windows[0]; }
        }

        public bool IsWindowMedium {
            get { return this.WindowValue == Windows[1]; }
            set { if(value) this.Window = Windows[1]; }
        }

        public bool IsWindowLong {
            get { return this.WindowValue == Windows[2]; }
            set { if(value) this.Window = Windows[2]; }
        }

        public HistoryBuffer Buffer { get; private set; }

        public ObservableCollection<SeriesLegendViewModel> Legend { get; private set; }

        // Per-series minimum, mean and maximum over the window, shown on hover
        public string Summary {
            get { return this.SummaryValue; }
            private set { Set(ref this.SummaryValue, value); }
        }

        // Configures the series. The order they are added in is the order they
        // take their palette slots in, which is the mechanism that keeps
        // neighbouring ones apart for colour-blind readers.
        public void Begin(int capacity, params SeriesSpec[] specs) {

            this.Buffer.Begin(capacity);
            this.WindowValue = capacity;
            this.Legend.Clear();

            foreach(SeriesSpec spec in specs)
                this.Legend.Add(new SeriesLegendViewModel(
                    this.Buffer.Add(spec.Label, spec.Minimum, spec.Maximum, spec.Unit)));

        }

        // Renames the series in place, for a language change: the buffer and
        // its samples stay, only the labels move
        public void Relabel(params string[] labels) {

            for(int i = 0; i < this.Legend.Count && i < labels.Length; i++) {
                this.Buffer.Series[i].Label = labels[i];
                this.Legend[i].RefreshLabel();
            }

        }

        // Records a tick and tells the legend to catch up
        public void Push(params double[] values) {

            this.Buffer.Push(values);

            foreach(SeriesLegendViewModel entry in this.Legend)
                entry.Refresh();

            this.Summary = this.Buffer.BuildSummary();

        }

    }

    // How a series is described when the chart is configured
    public struct SeriesSpec {

        public string Label;
        public string Unit;
        public double Minimum;
        public double Maximum;

        public SeriesSpec(string label, double minimum, double maximum, string unit) {
            this.Label = label;
            this.Minimum = minimum;
            this.Maximum = maximum;
            this.Unit = unit;
        }

    }

}
