// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.Threading;
using StarMon.AppCli;
using StarMon.AppGui;
using StarMon.Library;

namespace StarMon {

    // The application's main class
    public class App {

        // Application entry point
        [STAThread]
        public static void Main(string[] args) {

            // Set up an event handler to run on exit
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            // Initialize the configuration class
            Config.Initialize();

            // Initialize the hardware class
            Hw.Initialize();

            try {

                // If no arguments were given,
                // run in GUI (Windows Forms) mode
                if(args.Length == 0) {

#region Interface mode
                    // Set up the interface
                    Gui.Initialize();

                    // Last-resort handlers: log and survive unexpected
                    // interface errors, and at least log unhandled background
                    // ones. Marking a dispatcher exception handled is what
                    // keeps a fault in one panel from taking the whole
                    // application down with it — a tray application that
                    // vanishes leaves the fans wherever it last set them.
                    System.Windows.Application.Current.DispatcherUnhandledException +=
                        (sender, e) => {
                            Logger.Error("App", "Unhandled interface exception",
                                e.Exception != null ? e.Exception.ToString() : "");
                            e.Handled = true;
                        };

                    AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                        Logger.Error("App", "Unhandled exception",
                            e.ExceptionObject != null ? e.ExceptionObject.ToString() : "");

                    // Only allow a single GUI instance at any given time
                    bool isFirstInstance;
                    using(Mutex mutex = new Mutex(
                        true,
                        Config.LockPathGui,
                        out isFirstInstance)) {

                        if(isFirstInstance) {

                            // Start with just a notification icon and no
                            // window. The application object was made by
                            // Gui.Initialize, before anything that needs a
                            // theme or a pack:// URI was built.
                            GuiTray tray = new GuiTray();

                            System.Windows.Application.Current.Run();

                            tray.Shutdown();

                            // Release the lock when done
                            mutex.ReleaseMutex();

                        } else {

                            // Unless the application was run automatically
                            if(Environment.GetEnvironmentVariable(Config.EnvVarSelfName) == null
                                || Environment.GetEnvironmentVariable(Config.EnvVarSelfName).Contains(Config.EnvVarSelfValueGui))

                                // Send a message to the running instance
                                // to bring itself to the user's attention
                                Gui.BroadcastMessage(
                                    Gui.MessageId,
                                    Gui.MessageParam.AnotherInstance);

                        }

                    }
#endregion

                // In this special argument case,
                // launch a task in headless mode
                //
                // Invariant lowercasing throughout, here and in CliOp: on a
                // Turkish system ToLower() maps 'I' to the dotless 'ı', so
                // "-RENDERUI" arrived as "-renderuı" and matched nothing. The
                // arguments are ASCII keywords, not prose, and have to be
                // folded by the same rule everywhere the application runs.
                } else if(args[0].ToLowerInvariant() == "-run") {

                    CliOp.TaskRun(args);

                // Run the built-in tests. Handled ahead of the ordinary
                // command-line path because it needs a console but must not
                // take the single-instance lock the other CLI operations do:
                // the tests touch no hardware, so nothing needs serializing.
                } else if(args[0].ToLowerInvariant() == "-selftest") {

                    Cli.Initialize();

                    // An optional second argument narrows the run to the
                    // suites whose name contains it, so that a change to one
                    // area can be checked without waiting for the rest:
                    //
                    //     StarMon.exe -SelfTest service
                    Environment.ExitCode = StarMon.Test.SelfTest.Run(
                        args.Length > 1 ? args[1] : null);

                    Cli.RestorePrompt();

                // Render a piece of the interface to a PNG and exit. Like the
                // self-test, this touches no hardware and takes no lock; see
                // Ui/Design/DesignRender.cs for what it is for.
                } else if(args[0].ToLowerInvariant() == "-renderui") {

                    Cli.Initialize();
                    Environment.ExitCode = StarMon.Ui.Design.DesignRender.Run(args);
                    Cli.RestorePrompt();

                // As for any other command-line arguments,
                // process them in CLI (Console) mode
                } else {

#region CLI (Console) Mode
                    // If this is the first CLI instance,
                    // relaunch as a console application
                    bool isFirstInstance;
                    using(Mutex mutex = new Mutex(
                        true,
                        Config.LockPathCli,
                        out isFirstInstance)) {

                        if(isFirstInstance) {

                            // Relaunch the process as a console application
                            Cli.Relaunch(args);

                            // Release the lock when done
                            mutex.ReleaseMutex();

                            // Make the command prompt reappear
                            Cli.RestorePrompt();

                        } else {

                            // Attach the console (which is detached by default)
                            Cli.Initialize();

                            // Output the header
                            Cli.PrintHeader();

                            // Process all command-line arguments
                            // and perform the operations as requested
                            CliOp.Loop(args);

                        }

                    }

                }
#endregion

            } catch(Exception e) {

                // Any unhandled errors will result in a pop-up dialog
                // or be output to the console if it is initialized

                Error("ErrUnexpected|EXCEPTION", e);

            }

        }

#region Error & Exit Handlers
        // Handles an error depending on whether the application is running in CLI or GUI mode
        public static void Error(string messageIds, Exception e = null) {

            if(Cli.IsInitialized) {

                // Error out to the console
                Cli.PrintError(Config.GetError(messageIds, e), e);

                // And make the process say so on the way out. Reporting a
                // failure on standard output and then exiting zero tells a
                // script the opposite of what the text says; the first code
                // set is kept, since it is the more specific one.
                if(Environment.ExitCode == (int) Config.ExitStatus.NoError)
                    Environment.ExitCode = (int) Config.ExitStatus.ErrorOperation;

            } else

                // Pop up a window
                Gui.ShowError(Config.GetError(messageIds, e), e);

        }

        // Terminates the application
        public static void Exit(Config.ExitStatus code = Config.ExitStatus.NoError) {

            // Running as a console (CLI) application
            if(Cli.IsInitialized)

                // Make the command prompt reappear
                // since that's the end of it
                Cli.RestorePrompt();

            // Running as a Windows Forms (GUI) application
            if(GuiTray.Context != null && GuiTray.Context.Tray != null)
                GuiTray.Context.Tray.Dispose();

            System.Environment.Exit((int) code);

        }

        // Handler that gets called when the application is about to exit
        private static void OnExit(object sender, EventArgs e) {

                // Close the hardware, if opened
                if(Hw.IsInitialized)
                    Hw.Close();

                // Free the console, if running as a CLI app
                if(Cli.IsInitialized)
                    Cli.Close();

                // Close the forms, if running as a GUI app
                if(Gui.IsInitialized)
                    Gui.Close();

        }
#endregion

    }

}
