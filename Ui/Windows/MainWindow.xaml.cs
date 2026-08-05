// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using StarMon.External;
using StarMon.Ui.Views;

namespace StarMon.Ui.Windows {

    public partial class MainWindow : Window {

        private readonly ShellView Shell = new ShellView();

        public MainWindow() {

            InitializeComponent();

            this.ShellHost.Content = this.Shell;

            this.Shell.Minimising += () => this.WindowState = WindowState.Minimized;

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

            FitToScreen();

        }

        // The size the interface is drawn at, before any scaling
        private const double DesignWidth = 1000;
        private const double DesignHeight = 760;

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

        public ShellView View { get { return this.Shell; } }

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
        // which is the earliest any of these can be set
        private void OnSourceInitialized(object sender, EventArgs e) {

            IntPtr handle = new WindowInteropHelper(this).Handle;
            if(handle == IntPtr.Zero)
                return;

            SetDark(handle);
            SetRounded(handle);
            SetBackdrop(handle);

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

        // Asks the desktop manager for Mica behind the window, and only then
        // clears the background so it can be seen.
        //
        // The order matters and the check is not a formality: on a release
        // that does not support the attribute the call fails and the window
        // keeps its solid background, which is exactly right. Clearing the
        // background first and hoping would leave a black hole on Windows 10.
        private void SetBackdrop(IntPtr handle) {

            try {

                int backdrop = DwmApi.DWMSBT_MAINWINDOW;

                if(DwmApi.DwmSetWindowAttribute(handle,
                    DwmApi.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0)
                    this.Background = Brushes.Transparent;

            } catch { }

        }

    }

}
