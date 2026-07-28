// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using StarMon.Library;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // What the machine is, and what it will admit to.
    public partial class SystemView : UserControl {

        private SystemViewModel Model;

        public SystemView() {

            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;

        }

        private void OnDataContextChanged(object sender,
            DependencyPropertyChangedEventArgs e) {

            if(this.Model != null) {
                this.Model.CopyRequested -= OnCopyRequested;
                this.Model.PropertyChanged -= OnModelChanged;
            }

            this.Model = e.NewValue as SystemViewModel;

            if(this.Model != null) {
                this.Model.CopyRequested += OnCopyRequested;
                this.Model.PropertyChanged += OnModelChanged;
            }

            ShowReport();

        }

        private void OnModelChanged(object sender, PropertyChangedEventArgs e) {
            if(e.PropertyName == "Report")
                ShowReport();
        }

        // Lays the report out in columns.
        //
        // It is two hundred lines of fixed-pitch text and it used to sit in a
        // narrow box that wrapped every one of them and scrolled. A flow
        // document set in columns puts the whole thing on screen at once,
        // which is the only way it is actually read — and it stays selectable,
        // which is the only way it is actually useful.
        private void ShowReport() {

            if(this.Model == null) {
                this.ReportHost.Document = null;
                return;
            }

            FlowDocument document = new FlowDocument {
                FontFamily = Find("FontMono") as FontFamily
                    ?? new FontFamily("Consolas"),
                FontSize = 10.5,
                Foreground = Find("TextSecondary") as Brush ?? Brushes.Gray,
                Background = Brushes.Transparent,
                PagePadding = new Thickness(8, 6, 8, 6),
                ColumnWidth = 300,
                ColumnGap = 22,
                IsColumnWidthFlexible = true,
                LineHeight = 13
            };

            // One paragraph, keeping the report's own line breaks: the text is
            // laid out by its author, not by the reader's window
            Paragraph body = new Paragraph { Margin = new Thickness(0) };

            string report = this.Model.Report ?? "";
            string[] lines = report.Split('\n');

            for(int i = 0; i < lines.Length; i++) {

                body.Inlines.Add(new Run(lines[i].TrimEnd('\r')));

                if(i < lines.Length - 1)
                    body.Inlines.Add(new LineBreak());

            }

            document.Blocks.Add(body);
            this.ReportHost.Document = document;

        }

        private static object Find(string key) {
            return Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
        }

        // The clipboard is the view's business, not the view model's — the
        // same division the log panel's export follows.
        //
        // The retry is not superstition: the clipboard is a shared system
        // resource and another process holding it open makes the first attempt
        // throw. One more try a moment later almost always succeeds, and
        // failing quietly into the log beats an exception dialogue over a copy.
        private void OnCopyRequested() {

            if(this.Model == null)
                return;

            try {
                Clipboard.SetText(this.Model.Report);
                return;
            } catch { }

            try {
                System.Threading.Thread.Sleep(120);
                Clipboard.SetText(this.Model.Report);
            } catch(Exception e) {
                Logger.Error("Window", "Copying the hardware report failed", e.Message);
            }

        }

    }

}
