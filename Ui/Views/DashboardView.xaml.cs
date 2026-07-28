// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StarMon.AppService;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    public partial class DashboardView : UserControl {

        private readonly HistoryChart Chart = new HistoryChart();

        // Temperature is banded by health; clock is not. There is no such
        // thing as a dangerous clock speed, and colouring one red says there
        // is — so the second strip is one flat colour and reads as "how fast",
        // not "how worried".
        private readonly BarStrip CoreTemps = new BarStrip();
        private readonly BarStrip CoreClocks = new BarStrip();

        private readonly Sparkline TrendCpu = new Sparkline();
        private readonly Sparkline TrendGpu = new Sparkline();
        private readonly Sparkline TrendFan = new Sparkline();

        private DashboardViewModel Model;

        public DashboardView() {

            InitializeComponent();

            // The drawn controls are placed here rather than named in the
            // markup: XAML that names a type from this same assembly forces
            // a second markup-compilation pass, which this project cannot run.
            // The reasoning is written out at the top of Ui/Views/Cards.xaml.
            this.ChartHost.Content = this.Chart;
            this.CoreTempHost.Content = this.CoreTemps;
            this.CoreClockHost.Content = this.CoreClocks;
            this.SparkCpuHost.Content = this.TrendCpu;
            this.SparkGpuHost.Content = this.TrendGpu;
            this.SparkFanHost.Content = this.TrendFan;

            this.CoreClocks.UsesHealthBands = false;
            this.CoreClocks.Fill = Find("Series1");

            // Each block's trend takes the colour of the series it is drawn
            // from, so the sparkline in a block and the trace in the chart
            // below cannot disagree about which line is which
            this.TrendCpu.Stroke = Find("Series1");
            this.TrendCpu.HasArea = true;
            this.TrendGpu.Stroke = Find("Series2");
            this.TrendGpu.HasArea = true;
            this.TrendFan.Stroke = Find("Series3");
            this.TrendFan.HasArea = true;

            this.DataContextChanged += OnDataContextChanged;

        }

        private static Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {

            if(this.Model != null)
                this.Model.PropertyChanged -= OnModelChanged;

            this.Model = e.NewValue as DashboardViewModel;
            this.Chart.Buffer = this.Model != null ? this.Model.History.Buffer : null;

            if(this.Model == null)
                return;

            this.Model.PropertyChanged += OnModelChanged;

            ApplyCores();
            RefreshChart();

        }

        // The strips are drawn, not bound, so they are told when their source
        // moves — the same way the chart is
        private void OnModelChanged(object sender,
            System.ComponentModel.PropertyChangedEventArgs e) {

            if(e.PropertyName == "CoreTemperatures" || e.PropertyName == "CoreClocks")
                ApplyCores();

        }

        private void ApplyCores() {

            if(this.Model == null)
                return;

            this.CoreTemps.Integers = this.Model.CoreTemperatures;
            this.CoreClocks.Integers = this.Model.CoreClocks;

            // The clock strip is scaled against what this machine's cores
            // actually reach rather than a made-up ceiling: a fixed 5 GHz top
            // would draw every core on a mobile part as a stub. The floor is
            // held well under the lowest so a parked core is short rather than
            // invisible.
            int fastest = 0;
            foreach(int clock in this.Model.CoreClocks)
                if(clock > fastest)
                    fastest = clock;

            this.CoreClocks.Ceiling = fastest > 0 ? fastest : 1;
            this.CoreClocks.Floor = 0;

        }

        // Called after the poller has recorded a tick. The bindings look after
        // everything with a value behind it; the plot and the trends are drawn
        // rather than bound, so they have to be told.
        public void RefreshChart() {

            this.Chart.Refresh();

            if(this.Model == null)
                return;

            // The trends are read straight out of the chart's own buffer
            // rather than kept a second time. One history, drawn twice.
            HistoryBuffer buffer = this.Model.History.Buffer;

            this.TrendCpu.Values = Series(buffer, 0);
            this.TrendGpu.Values = Series(buffer, 1);
            this.TrendFan.Values = Series(buffer, 2);

        }

        // The last stretch of one series, oldest first. Shorter than the
        // chart's window on purpose: a sparkline the width of a word cannot
        // show ten minutes without turning every feature into one pixel, so it
        // shows the recent past and the chart below shows the rest.
        private static double[] Series(HistoryBuffer buffer, int slot) {

            const int Recent = 60;

            if(buffer == null || slot >= buffer.Series.Count)
                return new double[0];

            List<double> samples = new List<double>(buffer.Count);
            foreach(double value in buffer.Samples(buffer.Series[slot]))
                samples.Add(value);

            if(samples.Count <= Recent)
                return samples.ToArray();

            return samples.GetRange(samples.Count - Recent, Recent).ToArray();

        }

    }

}
