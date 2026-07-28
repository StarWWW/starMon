// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using StarMon.Library;

namespace StarMon.Ui.ViewModels {

    // One line in the log.
    //
    // A projection of Library.LogEntry rather than the entry itself: the
    // logger is written to from every thread in the application and must stay
    // free of anything that raises events on assignment.
    public sealed class LogLineViewModel {

        public LogLineViewModel(LogEntry entry) {

            this.Time = entry.Timestamp.ToString("HH:mm:ss");
            this.Level = entry.Level;
            this.Source = entry.Source ?? "";
            this.Message = entry.Message ?? "";

            // Identical consecutive entries are stacked by the logger rather
            // than repeated, so the count has to be shown or a message that
            // arrived four hundred times looks like one that arrived once
            this.Repeat = entry.RepeatCount > 1 ? "×" + entry.RepeatCount : "";

            this.Detail = string.IsNullOrEmpty(entry.Details)
                ? "" : entry.Details;

        }

        public string Time { get; private set; }
        public LogLevel Level { get; private set; }
        public string Source { get; private set; }
        public string Message { get; private set; }
        public string Detail { get; private set; }
        public string Repeat { get; private set; }

        // The whole line as it would be exported or copied
        public string Text {
            get {
                return this.Time + "  " + this.Level + "  " + this.Source + "  "
                    + this.Message + (this.Detail.Length > 0 ? "  " + this.Detail : "");
            }
        }

    }

    // The log, with what is shown filtered down from what was recorded.
    public sealed class LogViewModel : Observable {

        // How many lines are kept on show. The logger's own buffer is the
        // record; this is a window onto it, and a list with tens of thousands
        // of rows in it is not one anybody reads.
        public const int MaxLines = 2000;

        private readonly List<LogLineViewModel> All = new List<LogLineViewModel>();

        // The last raw entry taken in, kept so that a stacked repeat can be
        // recognised. The logger collapses identical consecutive entries by
        // raising its event again with the same object and a higher repeat
        // count, so an entry that is the one already at the tail is an update
        // to that line, not a new line — without this a message that arrived
        // four hundred times would be four hundred rows rather than one.
        private LogEntry LastEntry;
        private LogLineViewModel LastLine;

        private string SearchValue = "";
        private bool IsPausedValue;
        private bool ShowHardwareValue = true;
        private bool ShowBiosValue;
        private bool ShowEcValue;
        private bool ShowInterfaceValue = true;
        private bool ShowProblemsValue = true;

        public LogViewModel() {
            this.Lines = new ObservableCollection<LogLineViewModel>();
            this.ClearCommand = new RelayCommand(Clear);
            this.ExportCommand = new RelayCommand(Export);
        }

        public ObservableCollection<LogLineViewModel> Lines { get; private set; }

        public RelayCommand ClearCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }

        // Raised when the user asks for the log to be written out. The view
        // model builds the text; picking a file is the window's business.
        public event Action<string> ExportRequested;

        public string Search {
            get { return this.SearchValue; }
            set { if(Set(ref this.SearchValue, value)) Refilter(); }
        }

        // Paused means new entries are still recorded but not shown. Without
        // it, reading anything on a busy machine means chasing a list that
        // scrolls out from under the cursor. Unpausing rebuilds the view, so
        // everything recorded in the meantime appears rather than being
        // silently dropped until the next filter change.
        public bool IsPaused {
            get { return this.IsPausedValue; }
            set {
                if(Set(ref this.IsPausedValue, value) && !value)
                    Refilter();
            }
        }

        // The filters are grouped by what someone is actually looking for
        // rather than by log level. Eleven checkboxes named after the
        // application's internal levels is a list only its author can use.
        public bool ShowHardware {
            get { return this.ShowHardwareValue; }
            set { if(Set(ref this.ShowHardwareValue, value)) Refilter(); }
        }

        public bool ShowBios {
            get { return this.ShowBiosValue; }
            set { if(Set(ref this.ShowBiosValue, value)) Refilter(); }
        }

        public bool ShowEc {
            get { return this.ShowEcValue; }
            set { if(Set(ref this.ShowEcValue, value)) Refilter(); }
        }

        public bool ShowInterface {
            get { return this.ShowInterfaceValue; }
            set { if(Set(ref this.ShowInterfaceValue, value)) Refilter(); }
        }

        public bool ShowProblems {
            get { return this.ShowProblemsValue; }
            set { if(Set(ref this.ShowProblemsValue, value)) Refilter(); }
        }

        public string Summary {
            get {
                return this.Lines.Count == this.All.Count
                    ? this.All.Count + " " + Config.Locale.Get("GuiWpfEntries")
                    : this.Lines.Count + " " + Config.Locale.Get("GuiWpfEntriesOf")
                        + " " + this.All.Count + " " + Config.Locale.Get("GuiWpfEntries");
            }
        }

        // Records an entry, showing it if it passes the filters
        public void Add(LogEntry entry) {

            LogLineViewModel line = new LogLineViewModel(entry);

            // A stacked repeat of the entry already at the tail: replace that
            // line in place rather than appending a new one, so the repeat
            // count grows on one row the way the logger intends
            if(object.ReferenceEquals(entry, this.LastEntry) && this.LastLine != null) {

                int at = this.All.LastIndexOf(this.LastLine);
                if(at >= 0)
                    this.All[at] = line;

                int shown = this.Lines.IndexOf(this.LastLine);
                if(shown >= 0)
                    this.Lines[shown] = line;

                this.LastLine = line;
                Raise("Summary");
                return;

            }

            this.LastEntry = entry;
            this.LastLine = line;

            this.All.Add(line);
            if(this.All.Count > MaxLines)
                this.All.RemoveRange(0, this.All.Count - MaxLines);

            if(this.IsPaused)
                return;

            if(Matches(line))
                this.Lines.Add(line);

            while(this.Lines.Count > MaxLines)
                this.Lines.RemoveAt(0);

            Raise("Summary");

        }

        public void Clear() {
            this.All.Clear();
            this.Lines.Clear();
            this.LastEntry = null;
            this.LastLine = null;
            Raise("Summary");
        }

        public void Export() {

            Action<string> handler = this.ExportRequested;
            if(handler == null)
                return;

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach(LogLineViewModel line in this.Lines)
                text.AppendLine(line.Text);

            handler(text.ToString());

        }

        // Rebuilds the shown list from scratch. Cheap enough at two thousand
        // lines that a filter change can simply redo the work rather than
        // trying to work out which rows changed state.
        private void Refilter() {

            this.Lines.Clear();

            foreach(LogLineViewModel line in this.All)
                if(Matches(line))
                    this.Lines.Add(line);

            Raise("Summary");

        }

        private bool Matches(LogLineViewModel line) {

            if(!MatchesLevel(line.Level))
                return false;

            if(this.SearchValue.Length == 0)
                return true;

            return line.Message.IndexOf(this.SearchValue,
                       StringComparison.OrdinalIgnoreCase) >= 0
                || line.Source.IndexOf(this.SearchValue,
                       StringComparison.OrdinalIgnoreCase) >= 0
                || line.Detail.IndexOf(this.SearchValue,
                       StringComparison.OrdinalIgnoreCase) >= 0;

        }

        private bool MatchesLevel(LogLevel level) {

            switch(level) {

                case LogLevel.Warning:
                case LogLevel.Error:
                    return this.ShowProblems;

                case LogLevel.BiosCall:
                case LogLevel.BiosResult:
                    return this.ShowBios;

                case LogLevel.EcRead:
                case LogLevel.EcWrite:
                    return this.ShowEc;

                case LogLevel.Hardware:
                case LogLevel.Config:
                    return this.ShowHardware;

                default:
                    return this.ShowInterface;

            }

        }

    }

}
