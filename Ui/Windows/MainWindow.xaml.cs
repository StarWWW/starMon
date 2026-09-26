// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Interop;
using StarMon.External;
using StarMon.Ui.Views;

namespace StarMon.Ui.Windows {

    public partial class MainWindow : Window {

        private readonly ShellView Shell = new ShellView();

        public MainWindow() {

            InitializeComponent();

            this.ShellHost.Content = this.Shell;

            this.Shell.Minimising += () => this.WindowState = WindowState.Minimized;

            // The maximise button, and the double-click on the caption that
            // WindowChrome already gives, do the same thing
            this.Shell.MaximiseToggled += () => this.WindowState =
                this.WindowState == WindowState.Maximized
                    ? WindowState.Normal : WindowState.Maximized;

            // Closing the window hides it rather than ending the application.
            // This is a tray application: the fan program it is running has to
            // carry on, and someone who has finished looking at the readings
            // has not asked for their fan curve to stop.
            //
            // GuiCloseWindowExit turns that round for anyone who would rather
            // the close button meant close. The setting has been in the
            // configuration file, and documented, since before this interface
            // existed; until now nothing read it.
            this.Shell.Closing += OnCloseRequested;

            // The caption is the whole title bar, and the title bar now holds
            // the navigation tabs as well as the window buttons, so anything
            // in it that can be clicked has to be exempted or the click drags
            // the window instead. The shell marks its own controls, because it
            // is the only thing that knows which of its children are meant to
            // be clickable and which run of empty space is meant to be a
            // handle.
            this.SourceInitialized += OnSourceInitialized;

            this.Client.SizeChanged += (s, e) => FitShell();
            this.StateChanged += OnStateChanged;

            FitToScreen();

        }

        // The size the interface is laid out for, and never below
        internal const double DesignWidth = 1000;
        internal const double DesignHeight = 760;

        // Keeps the window inside the desktop it is opening on.
        //
        // The design size is in device-independent units, so on a display at
        // 150 % it asks for 1500x1140 physical pixels — and a 1920x1080
        // desktop has around 1040 of usable height once the taskbar has its
        // share. The window used to be pinned to exactly the design size in
        // both directions, so the bottom of every page was simply off the
        // screen with no way to reach it.
        //
        // SystemParameters gives the work area in the same units the window is
        // sized in, so no conversion is needed here: the scaling is already
        // accounted for on both sides.
        private void FitToScreen() {

            try {

                Size size = FitTo(
                    SystemParameters.WorkArea.Width,
                    SystemParameters.WorkArea.Height,
                    this.MinWidth, this.MinHeight);

                if(size.IsEmpty)
                    return;

                this.Width = size.Width;
                this.Height = size.Height;

            } catch { }

        }

        // The size to open at, given the room available.
        //
        // Separated from the window so the arithmetic can be checked against
        // the displays that broke it — a 1366x768 panel, and a 1080p one at
        // 150 % — without opening a window on each of them.
        internal static Size FitTo(double workWidth, double workHeight,
            double minWidth, double minHeight) {

            if(workWidth <= 0 || workHeight <= 0)
                return Size.Empty;

            // A margin, so the window does not sit corner to corner against
            // the edges of the work area
            const double Margin = 16;

            // Never larger than the design size, never larger than the room
            // there is, and never below the minimum the window declares —
            // in that order, because a window smaller than its own minimum is
            // resized back up by WPF and would overflow again.
            return new Size(
                Math.Min(DesignWidth, Math.Max(minWidth, workWidth - Margin)),
                Math.Min(DesignHeight, Math.Max(minHeight, workHeight - Margin)));

        }

        // Sizes the shell to the room the window has.
        private void FitShell() {

            Size size = ShellSize(this.Client.ActualWidth, this.Client.ActualHeight);

            if(size.IsEmpty)
                return;

            this.ShellHost.Width = size.Width;
            this.ShellHost.Height = size.Height;

        }

        // The size to lay the shell out at, for a client area of the given
        // size, such that the Viewbox around it — which only ever scales down —
        // brings it back to exactly the client area.
        //
        // The scale is whatever it takes to keep the shell at least the design
        // size in both directions, and never more than one: a window larger
        // than the design gets its pages laid out larger, not magnified; a
        // smaller one gets the whole design, shrunk evenly. Dividing by the
        // same scale the Viewbox will then apply is what makes the two cancel
        // and leaves no band of empty plane on either side.
        internal static Size ShellSize(double width, double height) {

            if(width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height))
                return Size.Empty;

            double scale = Math.Min(1.0,
                Math.Min(width / DesignWidth, height / DesignHeight));

            return new Size(width / scale, height / scale);

        }

        public ShellView View { get { return this.Shell; } }

        // What a minimised window comes back as: maximised if that is what it
        // was when it went down. Showing it from the tray set it to Normal
        // regardless, so a window the user kept maximised came back small.
        public WindowState RestoreState { get; private set; } = WindowState.Normal;

        private void OnStateChanged(object sender, EventArgs e) {

            if(this.WindowState != WindowState.Minimized)
                this.RestoreState = this.WindowState;

            // A maximised WindowChrome window is placed with its resize border
            // hanging off every edge of the monitor, so everything drawn in
            // that border is off screen: the caption buttons lose their outer
            // half and the pages their outermost pixels. Insetting by the same
            // amount is the standard answer; the frame is the system's own,
            // plus the padding Windows adds around it.
            this.Frame.Padding = this.WindowState == WindowState.Maximized
                ? MaximisedInset() : new Thickness(0);

            this.Shell.IsMaximised = this.WindowState == WindowState.Maximized;

        }

        private Thickness MaximisedInset() {

            Thickness frame = SystemParameters.WindowResizeBorderThickness;

            double padded = 0;
            try {
                // In physical pixels, so it is brought into the window's units
                PresentationSource source = PresentationSource.FromVisual(this);
                double scale = source != null && source.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice.M11 : 1.0;
                padded = User32.GetSystemMetrics(User32.SM_CXPADDEDBORDER) * scale;
            } catch { }

            return new Thickness(
                frame.Left + padded, frame.Top + padded,
                frame.Right + padded, frame.Bottom + padded);

        }

        // What the close button does. Hiding is the default, because a tray
        // application that stops running its fan program when its window is
        // dismissed is not doing what it was left to do.
        private void OnCloseRequested() {

            if(!Library.Config.GuiCloseWindowExit) {
                Hide();
                return;
            }

            // Ending it properly rather than closing the window: the tray
            // icon, the hardware session and the fan program all have to be
            // let go, and only the application knows how
            Library.Logger.Gui("Window", "Close ends the application",
                "GuiCloseWindowExit is set");
            StarMon.App.Exit();

        }

        // Applies the window's system-level appearance once it has a handle,
        // which is the earliest any of these can be set.
        //
        // There used to be a Mica backdrop requested here as well, after which
        // the window's background was cleared to let it through. The shell
        // paints its own opaque plane, deliberately — the charts are validated
        // against a solid surface — so the Mica was never visible inside it;
        // what the cleared background did show was black, wherever the window
        // was larger than the shell, because a WPF window with a transparent
        // background and no glass frame is drawn onto black.
        private void OnSourceInitialized(object sender, EventArgs e) {

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if(handle == IntPtr.Zero)
                return;

            SetDark(handle);
            SetRounded(handle);

        }

        private static void SetDark(IntPtr handle) {

            try {

                int on = 1;

                if(DwmApi.DwmSetWindowAttribute(handle,
                    DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmApi.DwmSetWindowAttribute(handle,
                        DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1,
                        ref on, sizeof(int));

            } catch { }

        }

        private static void SetRounded(IntPtr handle) {

            try {
                int preference = DwmApi.DWMWCP_ROUND;
                DwmApi.DwmSetWindowAttribute(handle,
                    DwmApi.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            } catch { }

        }

    }

}
