// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.AppCli {

    // Implements CLI-specific functionality
    // This part covers Embedded Controller-specific routines
    public static partial class Cli {

#region Output Methods - Context: Embedded Controller
        // Outputs the result of a specific Embedded Controller operation
        public static void PrintEcResult(bool isActionSet, bool isWord, byte register, ushort value) {

            // Action name
            PrintAction(isActionSet);

            // Register number
            Console.Write(" " + Config.Locale.Get(Config.L_CLI_EC + "Register") + " ");
            PrintValue(register, ValueFlag.Hex | ValueFlag.Prefix);
            Console.Write(" ");

            // Value size
            if(isWord)
                Console.Write(Config.Locale.Get(Config.L_CLI_EC + "Word"));
            else
                Console.Write(Config.Locale.Get(Config.L_CLI_EC + "Byte"));
            Console.Write(": ");

            // Hexadecimal, binary, and decimal values with prefixes
            PrintValue(value,
                ValueFlag.Color | ValueFlag.Hex | ValueFlag.Bin | ValueFlag.Dec | ValueFlag.Prefix,
                isWord ? (byte) 16 : (byte) 8);

            // Endianness reminder if the value is a word
            if(isWord) {
                Console.Write(" ");
                PrintColor((ConsoleColor) Color.Deemphasis, Config.Locale.Get(Config.L_CLI_EC + "WordNote"));
            }

            // Explanatory name
            string registerName = Enum.GetName(typeof(EmbeddedControllerData.Register), register);
            if(registerName != null) {
                Console.Write(" [");
                Console.Write(registerName);
                Console.Write("]");
            }

            Console.WriteLine();
        }

        // Outputs the Embedded Controller monitoring report to the screen
        // This method is called repeatedly at a specified interval
        public static void PrintEcReport(CliOp.EcMonData[] data) {

            // Start with an empty screen. Only where there is a screen: with
            // the output redirected to a file this throws, and each report is
            // meant to be appended rather than to replace the last one anyway.
            if(!Console.IsOutputRedirected)
                try {
                    Console.Clear();
                } catch {
                }

            // Iterate through all the registers
            for(int register = 0; register < data.Length; register++) {

                // Skip if set not to be shown
                if(!data[register].Show)
                    continue;

                // Output the register name, if available
                string registerName = Enum.GetName(typeof(EmbeddedControllerData.Register), register);
                Console.Write(registerName != null ? registerName.PadRight(4) + " " : "     ");

                // Output the register number
                PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString((byte) register, 2) + " ");

                // Show no more latest readouts than what fits on the screen
                int readStart = 0;
                if(3 * (data[register].Values.Count + 1) > Cli.Width) {
                    // Each readout takes 3 chars and the header row takes 10
                    readStart = data[register].Values.Count - ((Cli.Width - 10) / 3);
                    PrintColor((ConsoleColor) Color.Deemphasis, "< ");
                }

                // Used for comparison, since we only output the differences
                byte? lastValue = null;

                // Iterate through the readouts for each register, starting from: readStart
                for(int readNow = readStart; readNow < data[register].Values.Count; readNow++) {
                    // Do not output the value if it is the same as before
                    if(data[register].Values[readNow] == lastValue) {
                        PrintColor((ConsoleColor) Color.ValueEmpty, " ..");
                        continue;
                    }

                    // Update the last value to current value
                    lastValue = data[register].Values[readNow];

                    // Output a separator between the last and the current value
                    if(readNow > readStart)
                        Console.Write(" ");

                    // Print out the current value
                    PrintValueHexColor(data[register].Values[readNow]);
                }
                Console.WriteLine();
            }
        }
#endregion

    }

}
