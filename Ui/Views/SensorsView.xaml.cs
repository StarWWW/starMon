// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // Every reading the machine gives, laid out in columns.
    //
    // These used to be a narrow column beside the chart on the dashboard,
    // where there was room for neither them nor the chart. They share the
    // dashboard's model — the same groups, written to by the same poller — but
    // here they have a page of their own to lay out on.
    //
    // The layout is the whole problem this page has. The groups are wildly
    // different lengths — two rows for memory, nine for the fans and board
    // probes — and neither panel WPF offers for this handles that: a WrapPanel
    // leaves a ragged strip of empty page beside the last row, and a
    // UniformGrid gives every card in a row the height of the tallest one in
    // it, so the two-row group sits in a nine-row hole.
    //
    // So the groups are dealt out here instead, each to whichever column is
    // currently shortest. That is the arrangement with no gaps in it, and it
    // costs a few lines because the collection changes only when the language
    // does or when the first reading discovers which probes this board has.
    public partial class SensorsView : UserControl {

        // How many columns at which width. A card narrower than this cannot
        // hold "Victus by HP Gaming Laptop 15-fa2xxx" beside its label without
        // trimming it away to nothing.
        private const double WideEnoughForFour = 1120;
        private const double WideEnoughForThree = 860;

        private readonly ObservableCollection<DetailGroupViewModel>[] Buckets;
        private readonly ItemsControl[] Panels;

        private DashboardViewModel Model;
        private int ColumnCount = 4;

        public SensorsView() {

            InitializeComponent();

            this.Buckets = new[] {
                new ObservableCollection<DetailGroupViewModel>(),
                new ObservableCollection<DetailGroupViewModel>(),
                new ObservableCollection<DetailGroupViewModel>(),
                new ObservableCollection<DetailGroupViewModel>()
            };

            this.Panels = new[] { this.ColumnA, this.ColumnB, this.ColumnC, this.ColumnD };

            for(int i = 0; i < this.Panels.Length; i++)
                this.Panels[i].ItemsSource = this.Buckets[i];

            this.DataContextChanged += OnDataContextChanged;
            this.SizeChanged += OnSizeChanged;

        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {

            if(this.Model != null)
                this.Model.Details.CollectionChanged -= OnDetailsChanged;

            this.Model = e.NewValue as DashboardViewModel;

            if(this.Model != null)
                this.Model.Details.CollectionChanged += OnDetailsChanged;

            Deal();

        }

        // The group list grows once, when the first reading reveals which
        // board probes exist, and is emptied and rebuilt on a language change
        private void OnDetailsChanged(object sender, NotifyCollectionChangedEventArgs e) {
            Deal();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) {

            if(!e.WidthChanged)
                return;

            int wanted = this.ActualWidth >= WideEnoughForFour ? 4
                : this.ActualWidth >= WideEnoughForThree ? 3 : 2;

            if(wanted == this.ColumnCount)
                return;

            this.ColumnCount = wanted;
            Deal();

        }

        // Deals the groups out, each to the column that is currently shortest.
        //
        // Height is counted in rows rather than measured: a card's height is
        // its rows plus a fixed heading, layout has not happened yet when this
        // runs, and asking for an ActualHeight here would return zero for
        // every card and pile them all into the first column.
        private void Deal() {

            foreach(ObservableCollection<DetailGroupViewModel> bucket in this.Buckets)
                bucket.Clear();

            // A column past the visible count is given zero width rather than
            // just left empty: an empty ItemsControl in a star-width column
            // still takes its share, so the visible cards would come out
            // narrower than the count says they should be.
            for(int i = 0; i < this.Panels.Length; i++)
                this.Columns.ColumnDefinitions[i].Width = i < this.ColumnCount
                    ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            if(this.Model == null)
                return;

            // A card's fixed overhead — its heading, its rule, its padding —
            // in the same units the rows are counted in. Without it a column
            // holding four short groups would look shorter to this than one
            // holding a single long group, and the short cards would bunch.
            const int Overhead = 3;

            int[] height = new int[this.ColumnCount];

            foreach(DetailGroupViewModel group in this.Model.Details) {

                int shortest = 0;
                for(int i = 1; i < this.ColumnCount; i++)
                    if(height[i] < height[shortest])
                        shortest = i;

                this.Buckets[shortest].Add(group);
                height[shortest] += group.Rows.Count + Overhead;

            }

        }

    }

}
