// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.Threading;
using StarMon.AppGui;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.AppCli {

    // Implements the main operation loop in the application's CLI mode
    // This part covers fan control program-specific routines
    public static partial class CliOp {

#region Program List
        // Lists all available fan control programs
        private static void ProgList() {

                // Report if no programs available
                if(Config.FanProgram.Count == 0)
                    App.Error("ErrProgNone");

            else

                // Iterate through each program and list it
                foreach(string name in Config.FanProgram.Keys)
                    Cli.PrintProgList(name);

        }
#endregion

#region Program Run
        // Run a specified fan control program
        public static void ProgRun(string name) {

            // Make sure the specified program exists
            string program = name;
            if(!Config.FanProgram.ContainsKey(program)) {

                // Try to substitute spaces for underscores if not
                program = program.Replace('_', ' ');
                if(!Config.FanProgram.ContainsKey(program)) {

                    // Give up if still not found
                    App.Error("ErrProgName");
                    return;

                    }

            }

            // List the program that is about to be run
            Cli.PrintProgList(program, true);

            // Save the console color to be restored later
            ConsoleColor originalColor = Console.ForegroundColor;

            // Initialize the BIOS and the Embedded Controller
            Hw.BiosInit();
            Hw.EcInit();

            // Initialize the fan program with the hardware platform
            FanProgram Program = new FanProgram(new Platform(), Cli.PrintProgMessage);

            // Create an event handler to break out of the perpetual loop
            Console.CancelKeyPress += (sender, eventArgs) => {
                IsStop = true;

                // Terminate the program
                Program.Terminate();

                // Restore the console color to the original
                Console.ForegroundColor = originalColor;

                // Exit the application
                App.Exit();

            };

            // Start the program
            Program.Run(program);

            // Run in a perpetual loop
            int Tick;
            while(!IsStop) {

                // Reset the tick counter
                Tick = -1;

                // Wait until the next iteration
                while(!IsStop && ++Tick < Config.UpdateProgramInterval) {

                    // Print the tick counter
                    Cli.PrintColor((ConsoleColor) Cli.Color.Deemphasis, "# ");
                    Cli.PrintColor((ConsoleColor) Cli.Color.Emphasis, Conv.GetString((uint) Tick, 2, 10));
                    Cli.PrintColor((ConsoleColor) Cli.Color.Deemphasis, " / " + Conv.GetString((uint) Config.UpdateProgramInterval, 2, 10));

                    // Rewinding to overwrite the counter in place needs a
                    // screen buffer; redirected, the counter is simply a line
                    // per tick, which is what a log wants anyway
                    if(!Console.IsOutputRedirected)
                        try {
                            Console.SetCursorPosition(0, Console.CursorTop);
                        } catch {
                        }
                    else
                        Console.WriteLine();

                    // Sleep for each tick
                    Thread.Sleep(Config.GuiTimerInterval);

                }

                // Update the program
                Program.Update();

            }

        }
#endregion

    }

}
