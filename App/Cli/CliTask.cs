// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Library;

namespace StarMon.AppCli {

    // Implements CLI-specific functionality
    // This part covers task scheduling-specific routines
    public static partial class Cli {

#region Output Methods - Context: Task Scheduling
        // Outputs the result of a specific task scheduling operation
        public static void PrintTaskResult(bool isActionSet, Config.TaskId task, bool state, string extra = "") {

            // Action name
            PrintAction(isActionSet);
            Console.Write(" ");

            // Task identifier
            string taskName = Enum.GetName(typeof(Config.TaskId), task);
            PrintColor((ConsoleColor) Color.Emphasis, taskName);
            Console.Write(": ");

            // Task description
            Console.Write(Config.Locale.Get(Config.L_CLI_TASK + "" + taskName));
            Console.Write(": ");

            // State
            PrintState(state);

            // Extra information
            if(extra != "")
                Console.Write(" [" + extra + "]");

            Console.WriteLine();
        }
#endregion

    }

}
