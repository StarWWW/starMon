// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using StarMon.Ui.ViewModels;

namespace StarMon.Ui.Views {

    // Which part of the application is on show.
    //
    // Cooling was Curve: the page is no longer only the curve editor, it is
    // everything about how the machine cools itself — the curve, the fan
    // programs the tray menu used to be the only way to reach, the level
    // ceiling and where it came from. System is new, and is where the
    // hardware report that nothing has ever called now lives.
    public enum Section {
        Dashboard,
        Sensors,
        Cooling,
        Keyboard,
        System,
        Log,
        Settings,
        About
    }

    // The window's frame: its own title bar with the tabs in it, the summary
    // strip, and the section currently on show.
    //
    // A UserControl rather than the Window itself, so it can be laid out and
    // rendered without one — which is how it is looked at during development.
    public partial class ShellView : UserControl {

        private readonly Dictionary<ToggleButton, Section> Buttons =
            new Dictionary<ToggleButton, Section>();

        private readonly Sparkline TrendCpu = new Sparkline();
        private readonly Sparkline TrendGpu = new Sparkline();

        private SummaryViewModel Summary;

        // Raised when the user picks a section. The shell does not build the
        // sections: whatever owns it decides what each one is and hands it
        // back through SetSection, so the frame stays a frame.
        public event Action<Section> SectionSelected;

        public event Action Minimising;
        public event Action Closing;

        public ShellView() {

            InitializeComponent();

            Register(this.NavDashboard, Section.Dashboard);
            Register(this.NavSensors, Section.Sensors);
            Register(this.NavCooling, Section.Cooling);
            Register(this.NavKeyboard, Section.Keyboard);
            Register(this.NavSystem, Section.System);
            Register(this.NavLog, Section.Log);
            Register(this.NavSettings, Section.Settings);
            Register(this.NavAbout, Section.About);

            this.ButtonMinimise.Click += (s, e) => Raise(this.Minimising);
            this.ButtonClose.Click += (s, e) => Raise(this.Closing);

            // The window buttons sit in the caption region too, so they need
            // the same exemption the tabs do or the chrome swallows their
            // clicks and the window drags instead
            WindowChrome.SetIsHitTestVisibleInChrome(this.ButtonMinimise, true);
            WindowChrome.SetIsHitTestVisibleInChrome(this.ButtonClose, true);

            // The strip's trends are drawn controls, so they are placed from
            // here rather than named in markup
            this.SparkCpu.Content = this.TrendCpu;
            this.SparkGpu.Content = this.TrendGpu;

            this.DataContextChanged += OnDataContextChanged;

        }

        private void Register(ToggleButton button, Section section) {

            this.Buttons[button] = section;

            // Each tab individually, not the panel holding them: marking the
            // panel would make the whole run of the title bar undraggable,
            // including the empty space the user reaches for to move the
            // window
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);

            button.Checked += (s, e) => {
                Action<Section> handler = this.SectionSelected;
                if(handler != null)
                    handler(section);
            };

        }

        // The summary strip's trends. They are pushed into the drawn controls
        // rather than bound, for the reason every drawn control here is
        // pushed to: a FrameworkElement that renders its own geometry has no
        // dependency property for a binding to target, and giving it one would
        // mean naming this type in markup.
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {

            SummaryViewModel old = e.OldValue as SummaryViewModel;
            if(old != null)
                old.PropertyChanged -= OnSummaryChanged;

            this.Summary = e.NewValue as SummaryViewModel;
            if(this.Summary == null)
                return;

            this.Summary.PropertyChanged += OnSummaryChanged;

            this.TrendCpu.Stroke = Find("Series1");
            this.TrendGpu.Stroke = Find("Series2");

            RefreshTrends();

        }

        private void OnSummaryChanged(object sender, PropertyChangedEventArgs e) {

            if(e.PropertyName == "CpuTrend" || e.PropertyName == "GpuTrend")
                RefreshTrends();

        }

        private void RefreshTrends() {

            if(this.Summary == null)
                return;

            this.TrendCpu.Values = this.Summary.CpuTrend;
            this.TrendGpu.Values = this.Summary.GpuTrend;

        }

        private static Brush Find(string key) {
            object brush = Application.Current != null
                ? Application.Current.TryFindResource(key) : null;
            return brush as Brush;
        }

        // Puts a section on show. The tabs are brought into line too, so
        // selecting a section from the tray menu leaves the window looking the
        // way it would if the tab had been clicked — the two must never
        // disagree about where the user is. The caption still arrives because
        // callers name every section; the tabs carry the names now, so it is
        // not displayed separately.
        public void SetSection(Section section, string caption, object content) {

            this.SectionHost.Content = content;

            foreach(KeyValuePair<ToggleButton, Section> entry in this.Buttons)
                if(entry.Value == section && entry.Key.IsChecked != true)
                    entry.Key.IsChecked = true;

            AnimateSection();

        }

        // A short fade-and-rise as a section comes in, so switching reads as a
        // move rather than a jump. The whole host is animated, not each
        // section, so every one arrives the same way for free — and the
        // sections themselves stay ignorant of it.
        private void AnimateSection() {

            TranslateTransform slide = new TranslateTransform(0, 12);
            this.SectionHost.RenderTransform = slide;

            Duration duration = new Duration(TimeSpan.FromMilliseconds(220));
            IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            this.SectionHost.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, duration) { EasingFunction = ease });

        }

        private static void Raise(Action handler) {
            if(handler != null)
                handler();
        }

    }

}
