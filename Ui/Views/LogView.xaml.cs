// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    public partial class LogView : UserControl {

        private INotifyCollectionChanged Watched;
        private LogViewModel Model;

        public LogView() {
            InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;

            // Ctrl+C and the context menu both land here. The clipboard gets
            // the full line — time, level, source, message and the detail the
            // row only shows as a tooltip — because a pasted log line is read
            // somewhere the tooltip cannot follow.
            this.Rows.CommandBindings.Add(new System.Windows.Input.CommandBinding(
                System.Windows.Input.ApplicationCommands.Copy,
                OnCopy, OnCanCopy));
        }

        private void OnCanCopy(object sender,
            System.Windows.Input.CanExecuteRoutedEventArgs e) {
            e.CanExecute = this.Rows.SelectedItems.Count > 0;
        }

        private void OnCopy(object sender,
            System.Windows.Input.ExecutedRoutedEventArgs e) {

            System.Text.StringBuilder text = new System.Text.StringBuilder();

            foreach(object item in this.Rows.SelectedItems) {
                LogLineViewModel line = item as LogLineViewModel;
                if(line != null)
                    text.AppendLine(line.Text);
            }

            SetClipboard(text.ToString());

        }

        private void OnCopyAll(object sender, RoutedEventArgs e) {

            if(this.Model == null)
                return;

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach(LogLineViewModel line in this.Model.Lines)
                text.AppendLine(line.Text);

            SetClipboard(text.ToString());

        }

        // The clipboard can be held open by another process, and WPF answers
        // that with an exception; retrying once covers nearly every real case
        // and failing quietly covers the rest — a copy that did not take is
        // recoverable, a crash is not
        private static void SetClipboard(string text) {
            if(text.Length == 0)
                return;
            try {
                Clipboard.SetText(text);
            } catch {
                try {
                    System.Threading.Thread.Sleep(50);
                    Clipboard.SetText(text);
                } catch { }
            }
        }

        private void OnDataContextChanged(object sender,
            DependencyPropertyChangedEventArgs e) {

            if(this.Watched != null)
                this.Watched.CollectionChanged -= OnLinesChanged;

            if(this.Model != null)
                this.Model.ExportRequested -= OnExportRequested;

            this.Model = e.NewValue as LogViewModel;
            this.Watched = this.Model != null ? this.Model.Lines : null;

            if(this.Watched != null)
                this.Watched.CollectionChanged += OnLinesChanged;

            if(this.Model != null)
                this.Model.ExportRequested += OnExportRequested;

        }

        // The view model built the text; picking where it goes is a window's
        // business, which is why the request arrives here as an event
        private void OnExportRequested(string text) {

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog {
                FileName = "StarMon-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                DefaultExt = ".log",
                Filter = "Log (*.log)|*.log|Text (*.txt)|*.txt"
            };

            if(dialog.ShowDialog() != true)
                return;

            try {
                System.IO.File.WriteAllText(dialog.FileName, text,
                    System.Text.Encoding.UTF8);
            } catch(System.Exception error) {
                StarMon.Library.Logger.Error("Log",
                    "Writing the exported log failed", error.Message);
            }

        }

        // Follows the tail of the log, but only while the user is already at
        // it. Scrolling back to read something and then being yanked to the
        // bottom by the next entry is the single most irritating thing a log
        // viewer can do, and it happens on exactly the machines where the log
        // is worth reading.
        private void OnLinesChanged(object sender, NotifyCollectionChangedEventArgs e) {

            if(e.Action != NotifyCollectionChangedAction.Add)
                return;

            ScrollViewer scroller = FindScroller(this.Rows);
            if(scroller == null)
                return;

            // Within one row of the end counts as being at the end
            bool atEnd = scroller.VerticalOffset
                >= scroller.ScrollableHeight - 1.0;

            if(atEnd)
                scroller.ScrollToEnd();

        }

        private static ScrollViewer FindScroller(DependencyObject from) {

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(from);

            for(int i = 0; i < count; i++) {

                DependencyObject child =
                    System.Windows.Media.VisualTreeHelper.GetChild(from, i);

                ScrollViewer found = child as ScrollViewer ?? FindScroller(child);
                if(found != null)
                    return found;

            }

            return null;

        }

    }

}
