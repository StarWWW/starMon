// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarMon.Ui.Design {

    // Renders a piece of the interface to a PNG, without showing a window.
    //
    // This exists because the shipping executable asks for administrator
    // rights: it cannot be started to be looked at without a consent prompt,
    // and a run that does get elevated is awkward to stop again. So the
    // interface is looked at the other way round — WPF will render any visual
    // into a bitmap on a thread with no window at all, and the same
    // unelevated test host that runs the self-test can do it.
    //
    //     StarMonTest.exe -RenderUi <surface> <output.png> [scale]
    //
    // It renders the real theme out of the real build, so what comes back is
    // what the application will draw, not an approximation of it built
    // somewhere else. The design surfaces themselves cost a few kilobytes of
    // compiled markup in the shipping binary, which is a fair price for the
    // design loop being authoritative rather than a copy that can drift.
    public static class DesignRender {

        // Every surface that can be rendered, by the name given on the
        // command line. Adding one is a line here and a XAML file beside it.
        private static readonly Dictionary<string, Func<FrameworkElement>> Surfaces =
            new Dictionary<string, Func<FrameworkElement>>(StringComparer.OrdinalIgnoreCase) {
                { "gallery",   () => new Gallery() },
                { "dashboard", Dashboard },
                { "window",    Window },
                { "window-one", WindowOneSeries },
                { "curve",     Curve },
                { "keyboard",  Keyboard },
                { "keyboard-off", KeyboardOff },
                { "keyboard-tkl", KeyboardTkl },
                { "log",       Log },
                { "system",    SystemSection },
                { "sensors",   SensorsSection },
                { "settings",  SettingsSection },
                { "picker",    Picker },
                { "menu",      Menu },
                { "trayicon",  TrayIcon },
                { "window-tr", WindowTurkish },
                { "window-en", WindowEnglish }
            };

        // A section inside the window frame.
        //
        // Every surface below is one, and each used to build the frame for
        // itself. That was three lines of ceremony per surface, and — once the
        // frame grew a summary strip — three lines that would each have had to
        // remember to feed it. A surface that forgets renders a strip of
        // dashes, which looks like a bug in the strip rather than in the
        // sample data, so there is one place to forget it and it does not.
        private static FrameworkElement Frame(Views.Section section,
            string caption, FrameworkElement content) {

            Views.ShellView shell = new Views.ShellView {
                Width = 1000, Height = 760,
                DataContext = DesignData.Summary()
            };

            shell.SetSection(section, caption, content);
            return shell;

        }

        // The window in Turkish.
        //
        // Worth a surface of its own rather than a spot check: the strings are
        // longer in nearly every case, and the places a layout gives way under
        // that — a segmented selector, a caption beside a value — are not the
        // places anyone looks when they were written in English.
        private static FrameworkElement WindowTurkish() {

            Library.Config.LocaleInit("Turkish");

            return Frame(Views.Section.Dashboard,
                Library.Config.Locale.Get("GuiWpfDashboard"),
                new Views.DashboardView { DataContext = DesignData.Dashboard() });

        }

        // The window in English.
        //
        // The surface above pins Turkish; this one has to pin English, because
        // "window" pins nothing and renders in whatever language the machine
        // running the design loop happens to be in. On a Turkish machine that
        // means the English layout is never actually looked at — the one
        // surface everyone assumes is being checked is the one that was not.
        //
        // The sample rows are Turkish either way: they are literals in
        // DesignData, and the shipped application never reads them. What this
        // surface is for is the chrome the locale does drive — tabs, section
        // captions, the segmented selectors and the chips — which is exactly
        // where the two languages differ in width.
        private static FrameworkElement WindowEnglish() {

            // Not LocaleInit("English"): that parses the name straight into a
            // locale slot, and English has no slot of its own — it is the
            // Override slot, so that a translation supplied through the
            // configuration file still takes effect. Going through the
            // application's own resolution is both the thing that works and
            // the thing that would notice if that mapping ever changed.
            Library.Config.LocaleInit();
            Library.Config.Language = "English";
            Library.Config.Locale.SetLanguage(Library.Config.ResolveLanguage());

            return Frame(Views.Section.Dashboard,
                Library.Config.Locale.Get("GuiWpfDashboard"),
                new Views.DashboardView { DataContext = DesignData.Dashboard() });

        }

        // The notification icon at the sizes the shell asks for, in every
        // state it has. A tray icon is the surface a mistake is easiest to
        // ship in: too small to notice being wrong, too far away to check.
        private static FrameworkElement TrayIcon() {

            System.Windows.Controls.StackPanel rows =
                new System.Windows.Controls.StackPanel { Margin = new Thickness(28) };

            AddRow(rows, "Cool, with backdrop", Shell.DynamicIcon.Backdrop.Cool, true);
            AddRow(rows, "Warm, with backdrop", Shell.DynamicIcon.Backdrop.Warm, true);
            AddRow(rows, "No backdrop", Shell.DynamicIcon.Backdrop.None, false);
            AddRow(rows, "Outline", Shell.DynamicIcon.Backdrop.Outline, false);

            return new System.Windows.Controls.Border {
                Background = Brush("Plane"),
                Child = rows
            };

        }

        private static void AddRow(System.Windows.Controls.Panel into,
            string caption, Shell.DynamicIcon.Backdrop backdrop, bool hasBackdrop) {

            System.Windows.Controls.StackPanel row =
                new System.Windows.Controls.StackPanel {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 18)
                };

            row.Children.Add(new System.Windows.Controls.TextBlock {
                Text = caption,
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("TextSecondary"),
                FontFamily = Application.Current.TryFindResource("FontText") as FontFamily,
                FontSize = 13
            });

            // The three-character case matters: two digits and a degree sign
            // is what the icon shows most of the time, and a size chosen for
            // two leaves the third spilling over the edge
            foreach(string text in new[] { "61", "104", "8" })
                foreach(int size in new[] { 16, 24, 32 })
                    row.Children.Add(new System.Windows.Controls.Image {
                        Source = Shell.DynamicIcon.Preview(text, backdrop, hasBackdrop, size),
                        Width = size, Height = size,
                        Margin = new Thickness(0, 0, 14, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });

            into.Children.Add(row);

        }

        // The tray menu's contents on the surface the popup gives them.
        //
        // A ContextMenu has no visual until it opens, and opening one needs a
        // window, so the items are put on a border with the same fill, edge
        // and radius the popup template uses. What is being checked here is
        // the item template — the tick, the sub-menu arrow, the spacing — and
        // that is the same either way.
        private static FrameworkElement Menu() {

            System.Windows.Controls.ItemsControl items =
                new System.Windows.Controls.ItemsControl();

            Shell.MenuModel.Fill(items, DesignData.Menu());

            System.Windows.Controls.Border border =
                new System.Windows.Controls.Border {
                    Background = Brush("Card"),
                    BorderBrush = Brush("CardBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4),
                    Width = 268,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = items
                };

            return new System.Windows.Controls.Border {
                Background = Brush("Plane"),
                Padding = new Thickness(28),
                Child = border
            };

        }

        private static Brush Brush(string key) {
            return Application.Current.TryFindResource(key) as Brush;
        }

        private static FrameworkElement SystemSection() {

            return Frame(Views.Section.System, "Sistem",
                new Views.SystemView { DataContext = DesignData.SystemInfo() });

        }

        // The colour picker on its flyout surface, so the one control that only
        // ever exists inside a popup can still be looked at during development
        private static FrameworkElement Picker() {

            Views.ColorPicker picker = new Views.ColorPicker {
                Width = 240, Height = 230,
                Color = System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF)
            };

            System.Windows.Controls.Border card =
                new System.Windows.Controls.Border {
                    Background = Brush("Card"),
                    BorderBrush = Brush("CardBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16),
                    Width = 272,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = picker
                };

            return new System.Windows.Controls.Border {
                Background = Brush("Plane"),
                Padding = new Thickness(28),
                Child = card
            };

        }

        private static FrameworkElement SettingsSection() {

            return Frame(Views.Section.Settings, "Ayarlar",
                new Views.SettingsView { DataContext = DesignData.Settings() });

        }

        private static FrameworkElement SensorsSection() {

            return Frame(Views.Section.Sensors, "Sensörler",
                new Views.SensorsView { DataContext = DesignData.Dashboard() });

        }

        private static FrameworkElement Log() {

            return Frame(Views.Section.Log, "Günlük",
                new Views.LogView { DataContext = DesignData.Log() });

        }

        private static FrameworkElement Keyboard() {

            return Frame(Views.Section.Keyboard, "Klavye",
                new Views.KeyboardView { DataContext = DesignData.Keyboard() });

        }

        // The keyboard with its backlight switched off, which is the state the
        // diagram most has to hold up in: no colour to carry it, so the caps
        // themselves have to read as a real keyboard
        private static FrameworkElement KeyboardOff() {

            ViewModels.KeyboardViewModel model = DesignData.Keyboard();
            model.IsBacklightOn = false;

            return Frame(Views.Section.Keyboard, "Klavye",
                new Views.KeyboardView { DataContext = model });

        }

        // A ten-key-less deck with a backlight that takes no colour from here,
        // which is what the 16-inch per-key-RGB machines look like. Both of
        // those used to be drawn as though they were this machine's keyboard.
        private static FrameworkElement KeyboardTkl() {

            ViewModels.KeyboardViewModel model = new ViewModels.KeyboardViewModel(0);
            model.HasNumPad = false;
            model.Brand = "OMEN";
            model.IsBacklightOn = true;

            return Frame(Views.Section.Keyboard, "Klavye",
                new Views.KeyboardView { DataContext = model });

        }

        private static FrameworkElement Curve() {

            return Frame(Views.Section.Cooling, "Soğutma",
                new Views.CoolingView { DataContext = DesignData.Cooling() });

        }

        // The same window with the legend clicked down to a single series,
        // which is the other half of the chart's behaviour: the area wash
        // comes back once there is only one reading to wash under
        private static FrameworkElement WindowOneSeries() {

            ViewModels.DashboardViewModel model = DesignData.Dashboard();

            for(int i = 1; i < model.History.Legend.Count; i++)
                model.History.Legend[i].IsVisible = false;

            return Frame(Views.Section.Dashboard, "Panel",
                new Views.DashboardView { DataContext = model });

        }

        // The dashboard on its own, at the width it has inside the window
        private static FrameworkElement Dashboard() {
            Views.DashboardView view = new Views.DashboardView {
                DataContext = DesignData.Dashboard(),
                Width = 952, Height = 678
            };
            return view;
        }

        // The whole window: chrome, rail and section together, at the size it
        // opens at. Looking at a panel on its own hides the things that only
        // go wrong in company — a rail that crowds the content, a title bar
        // that does not line up with the first row beneath it.
        private static FrameworkElement Window() {
            return Frame(Views.Section.Dashboard, "Panel",
                new Views.DashboardView { DataContext = DesignData.Dashboard() });
        }

        public static int Run(string[] args) {

            if(args.Length < 3) {
                Console.WriteLine("Usage: -RenderUi <surface> <output.png> [scale]");
                Console.WriteLine("Surfaces: " + string.Join(", ", Names()));
                return 2;
            }

            string name = args[1];
            string output = args[2];

            double scale = 2.0;
            if(args.Length > 3 && !double.TryParse(args[3],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out scale)) {
                Console.WriteLine("The scale must be a number");
                return 2;
            }

            Func<FrameworkElement> build;
            if(!Surfaces.TryGetValue(name, out build)) {
                Console.WriteLine("Unknown surface: " + name);
                Console.WriteLine("Surfaces: " + string.Join(", ", Names()));
                return 2;
            }

            try {

                Ui.Shell.Theme.Initialize();

                FrameworkElement element = build();
                Save(element, output, scale);

                Console.WriteLine("Rendered {0} to {1}", name, Path.GetFullPath(output));
                return 0;

            } catch(Exception e) {

                Console.WriteLine("Rendering failed:");
                Console.WriteLine(e.ToString());
                return 1;

            }

        }

        private static IEnumerable<string> Names() {
            return Surfaces.Keys;
        }

        // A stand-in for the Mica the live window sits on: a quiet diagonal
        // wash in the dark accent, dark enough to read light content against
        // and textured enough that a translucent card shows it has something
        // behind it
        private static Brush DesktopBackdrop() {

            LinearGradientBrush brush = new LinearGradientBrush {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };

            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb(0x10, 0x14, 0x1C), 0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb(0x0A, 0x0C, 0x12), 0.55));
            brush.GradientStops.Add(new GradientStop(
                Color.FromRgb(0x14, 0x11, 0x1E), 1));

            brush.Freeze();
            return brush;

        }

        // Lays an element out at its natural size and writes it to a PNG.
        //
        // The measure and arrange passes have to be driven by hand: an element
        // that was never put in a window has no layout pass of its own, and
        // rendering one that has not been arranged produces an empty bitmap
        // rather than an error.
        public static void Save(FrameworkElement element, string path, double scale) {

            Size size = new Size(
                double.IsNaN(element.Width) ? element.MaxWidth : element.Width,
                double.IsNaN(element.Height) ? element.MaxHeight : element.Height);

            // No explicit size: let the element ask for one
            if(double.IsInfinity(size.Width) || double.IsInfinity(size.Height)) {
                element.Measure(new Size(
                    double.IsInfinity(size.Width) ? double.PositiveInfinity : size.Width,
                    double.IsInfinity(size.Height) ? double.PositiveInfinity : size.Height));
                size = element.DesiredSize;
            }

            // The live window sits on the desktop manager's Mica, so its own
            // background is cleared to let that through. A render has no desktop
            // behind it, so a stand-in backdrop is put there — otherwise every
            // surface that is transparent for Mica would render as a black hole.
            System.Windows.Controls.Border host = new System.Windows.Controls.Border {
                Width = size.Width,
                Height = size.Height,
                Background = DesktopBackdrop(),
                Child = element
            };

            host.Measure(size);
            host.Arrange(new Rect(new Point(0, 0), size));
            host.UpdateLayout();

            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int) Math.Round(size.Width * scale),
                (int) Math.Round(size.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(host);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using(FileStream file = File.Create(path))
                encoder.Save(file);

        }

    }

}
