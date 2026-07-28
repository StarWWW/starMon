// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using StarMon.External;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.AppCli {

    // Implements CLI-specific functionality
    // Note: partially defined in Cli*.cs for each specific context
    public static partial class Cli {

        public static bool IsInitialized { get; private set; }
        public static bool IsPowerShell { get; private set; }

        private static ConsoleColor OriginalBackgroundColor;

        // Whether there is a console screen buffer to manipulate.
        //
        // Console.Clear, the cursor properties and BufferWidth all read the
        // screen-buffer info, which does not exist when the output has been
        // redirected to a file or a pipe. Every one of them throws there —
        // and because this is a WinExe, the exception surfaces as a message
        // box in front of the user rather than as text on a console nobody is
        // watching, and the process then sits waiting to be dismissed.
        //
        // Redirecting the output is the ordinary way to script a command-line
        // tool. `StarMon.exe -Bios > out.txt` crashed on the first statement
        // of Initialize, which meant none of the command-line interface could
        // be used from a script at all. The cursor tidying below is cosmetic;
        // when there is nowhere to be cosmetic, it is skipped.
        private static bool HasScreenBuffer {
            get {
                try {
                    return !Console.IsOutputRedirected && Console.BufferWidth > 0;
                } catch {
                    return false;
                }
            }
        }

        // The console width, or a sensible column count when the output is
        // going somewhere that has no width
        public static int Width {
            get {
                try {
                    return Console.IsOutputRedirected ? DefaultWidth : Console.BufferWidth;
                } catch {
                    return DefaultWidth;
                }
            }
        }

        // Wide enough for the register table's rows without wrapping in a
        // file, narrow enough to stay readable when it is read back in one
        private const int DefaultWidth = 120;

        // Portable Executable (PE) Common Object File Format (COFF) header constants
        private const UInt16 IMAGE_OPTIONAL_HEADER_SUBSYSTEM = 0x00DC;
        private const byte IMAGE_SUBSYSTEM_WINDOWS_GUI = 0x02;
        private const byte IMAGE_SUBSYSTEM_WINDOWS_CUI = 0x03;

        // Setting flags for printing numerical values
        // Note: not all flag combinations are implemented for all data types
        [Flags]
        public enum ValueFlag : byte {
            Bin = 0x01,     // Show the binary value
            Dec = 0x02,     // Show the decimal value
            Hex = 0x04,     // Show the hexadecimal value
            Color = 0x08,   // Color the hexadecimal or binary value
            Prefix = 0x10   // Show numerical base prefixes (ignored for decimal)
        }

#region Color Settings
        // Console output color values
        public enum Color : int {

            ActionGet      = (int) ConsoleColor.DarkMagenta,  // Retrieval action (read)
            ActionSet      = (int) ConsoleColor.Magenta,      // Assignment action (write)
            Context        = (int) ConsoleColor.DarkCyan,     // Top-level argument header
            Deemphasis     = (int) ConsoleColor.DarkGray,     // Less important portion
            Emphasis       = (int) ConsoleColor.White,        // More important portion
            Error          = (int) ConsoleColor.Red,          // Error message
            HeaderCaption  = (int) ConsoleColor.DarkMagenta,  // Header application summary
            HeaderTitle    = (int) ConsoleColor.Blue,         // Header application name
            HeaderVersion  = (int) ConsoleColor.Magenta,      // Header application version 
            StateOff       = (int) ConsoleColor.Red,          // Disabled state
            StateOn        = (int) ConsoleColor.Green,        // Enabled state
            TableHeader    = (int) ConsoleColor.DarkMagenta,  // Table column and row headers
            Value          = (int) ConsoleColor.Blue,         // Any value except those below
            ValueBinUnset  = (int) ConsoleColor.DarkCyan,     // Unset bit in a binary value
            ValueEmpty     = (int) ConsoleColor.DarkGray,     // Value == 0x00
            ValueFull      = (int) ConsoleColor.DarkYellow,   // Value == 0xFF
            ValueSingleBit = (int) ConsoleColor.DarkRed       // PopCount(Value) == 1

        }
#endregion

#region Initialization & Termination Methods
        // Enables a Windows Forms (GUI) app
        // to work with console (with some caveats)
        public static void Initialize() {

            // The redirected writers, taken before the console is attached.
            //
            // AttachConsole rebinds the process's standard handles to the
            // console it attaches to. .NET creates Console.Out lazily, and
            // this call is the first thing that happens in command-line mode,
            // so the writer is created afterwards and bound to the console —
            // and everything the tool prints goes to the console window
            // instead of to the file it was redirected into. That is why
            // `StarMon.exe -Bios > out.txt` produced an empty file while the
            // output scrolled past in the terminal.
            //
            // Reading Console.Out here pins it to the redirected handle; the
            // writers are put back below, after the attach.
            bool outRedirected = false, errorRedirected = false;
            TextWriter redirectedOut = null, redirectedError = null;

            try {

                outRedirected = Console.IsOutputRedirected;
                errorRedirected = Console.IsErrorRedirected;

                // A file gets UTF-8; a console keeps its own code page.
                //
                // Redirected output is written in Console.OutputEncoding,
                // which is the OEM code page — 857 on a Turkish system — so a
                // report saved to a file came out with every accented
                // character mangled, which is both unreadable and lossy.
                //
                // Setting Console.OutputEncoding cannot fix it here: that
                // setter calls SetConsoleOutputCP, and at this point the
                // process is a windowed application with no console attached
                // yet, so it fails. Writing over the raw stream instead needs
                // no console at all — and the stream holds its own handle, so
                // the AttachConsole below cannot take it away.
                if(outRedirected) {
                    redirectedOut = new StreamWriter(Console.OpenStandardOutput(),
                        new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                    Console.SetOut(redirectedOut);
                }

                if(errorRedirected) {
                    redirectedError = new StreamWriter(Console.OpenStandardError(),
                        new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                    Console.SetError(redirectedError);
                }

            } catch { }

            // Attach to console window, which may modify the standard handles
            if(!Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS))
                Kernel32.AllocConsole(); // Using an attached console
            else { // Using an existing console

                IsPowerShell = Os.IsConsolePowerShell();

                // Everything below rearranges a screen buffer. With the output
                // redirected there is no buffer to rearrange and each call
                // throws, so it is all skipped — the text still goes where it
                // was sent, which is the whole point of redirecting it.
                if(HasScreenBuffer)
                    try {

                        // Save the original background color and set it to black
                        OriginalBackgroundColor = Console.BackgroundColor;
                        Console.BackgroundColor = ConsoleColor.Black;

                        if(IsPowerShell) {

                            // Basic workaround only
                            Console.Clear();

                        } else {

                            // Clear the last two rows, and make sure we end up
                            // at the first column of the row before the last one
                            Console.Error.Write("\r" + new string(' ', Console.BufferWidth));
                            Console.SetCursorPosition(0,
                                Console.CursorTop == 0 ? 0 : Console.CursorTop - 1);
                            Console.Write("\r" + new string(' ', Console.BufferWidth) + "\r");

                        }

                    } catch {

                        // A console that will not be tidied is still a console
                        // that can be written to. Nothing here is worth losing
                        // the output over.

                    }

            }

            // Put the redirected writers back. Whatever the attach did to the
            // standard handles, output the caller asked to be sent to a file
            // or a pipe goes there.
            try {
                if(redirectedOut != null) Console.SetOut(redirectedOut);
                if(redirectedError != null) Console.SetError(redirectedError);
            } catch { }

            IsInitialized = true;
       }

        // Releases the console when no longer needed
        public static void Close() {

            // Try to move the cursor to the bottom of the window,
            // which does not happen automatically in a PowerShell session
            if(IsPowerShell && HasScreenBuffer)
                try {
                    Console.SetCursorPosition(0,
                        Console.WindowHeight >= Console.BufferHeight ?
                            Console.BufferHeight - 1 : Console.WindowHeight);
                } catch {
                }

            // Restore the original background color. Only where one was taken:
            // with the output redirected Initialize never read it, and setting
            // it from the default would repaint a console it never touched.
            if(HasScreenBuffer)
                try {
                    Console.BackgroundColor = OriginalBackgroundColor;
                } catch {
                }

            IsInitialized = false;
            Kernel32.FreeConsole();

        }

        // Relaunches the process as a console application
        public static void Relaunch(string[] args) {
            byte[] data;

            // Read the image of our own process into an array
            using(FileStream dataIn = new FileStream(
                Config.AppFile,
                FileMode.Open, FileAccess.Read)) {

                data = new byte[dataIn.Length];
                dataIn.Read(data, 0, data.Length);

            }

            // Modify the PE header to run as a console application
            data[IMAGE_OPTIONAL_HEADER_SUBSYSTEM] = IMAGE_SUBSYSTEM_WINDOWS_CUI;

            // Launch ourselves again
            Assembly ass = Assembly.Load(data);
            MethodInfo m = ass.EntryPoint;
            m.Invoke(null, new[] { args });

            // Note: this is still not enough to run as a proper console application
            // Would need to launch a separate process or perhaps Assembly.LoadFile()

        }

        // Makes the command prompt reappear when the application is done in CLI mode
        public static void RestorePrompt() {

            // Skip if a PowerShell session, or if there is no screen buffer to
            // put a prompt back on: with the output redirected there is no
            // console window to send the keystroke to either
            if(!Os.IsConsolePowerShell() && HasScreenBuffer) {

                try {

                    // Make the command prompt appear again
                    // by simulating a keystroke (an ugly hack)
                    Console.CursorTop -= 1; // Go back one row to avoid leaving blank space
                    User32.SendMessage(
                        Kernel32.GetConsoleWindow(),
                        User32.WM_CHAR,
                        (IntPtr) User32.VK_ENTER,
                        IntPtr.Zero);

                } catch {
                }

            }

        }
#endregion

#region Output Methods - General
        // Outputs a string in a given color, then reverts back to the original color
        public static void PrintColor(ConsoleColor color, string text) {
            ConsoleColor originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = originalColor;
        }

        // Outputs a formatted byte array
        public static void PrintValue(byte[] value, ValueFlag flags = ValueFlag.Hex | ValueFlag.Color, byte bits = 8) {

            // Output a hexadecimal representation
            if(flags.HasFlag(ValueFlag.Hex))
            for(int i = 1; i <= value.Length; i++) {
                if(flags.HasFlag(ValueFlag.Color))
                    PrintValueHexColor(value[i - 1], 8);
                else
                    Console.Write(Conv.GetString(value[i - 1]));
                if(i * 8 % bits == 0 && i != value.Length)
                    Console.Write(" ");
                if(i % 16 == 0 && i != value.Length)
                    Console.WriteLine();

            // Output a binary representation
            } else if(flags.HasFlag(ValueFlag.Bin))
            for(int i = 1; i <= value.Length; i++) {
                if(flags.HasFlag(ValueFlag.Color))
                    PrintValueBinColor(value[i - 1], 8);
                else
                    Console.Write(Conv.GetString(value[i - 1], 8, 2));
                if(i * 8 % bits == 0 && i != value.Length)
                    Console.Write(" ");
                if(i % 8 == 0 && i != value.Length)
                    Console.WriteLine();
            }
        }

        // Outputs a formatted hexadecimal, decimal and/or binary value
        public static void PrintValue(uint value, ValueFlag flags = ValueFlag.Hex, byte bits = 0) {

            // Set the number of bits to use when padding values
            int bytes = bits == 0 ? value > byte.MaxValue ? value > ushort.MaxValue ? 4 : 2 : 1 : bits / 8;

            // Used to separate entries in different numerical bases
            bool needSeparator = false;

            // Check if asked to output the hexadecimal value
            if(flags.HasFlag(ValueFlag.Hex)) {

                // Output the hexadecimal prefix if requested
                if(flags.HasFlag(ValueFlag.Prefix))
                    PrintColor((ConsoleColor) Color.Deemphasis, "0x");

                // Output the hexadecimal value
                if(flags.HasFlag(ValueFlag.Color))
                    PrintValueHexColor(value, bytes * 8);
                else
                    PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString(value, bytes * 2, 16));

                // Request the separator before the next output (if any)
                needSeparator = true;
            }

            // Check if asked to output the binary value
            if(flags.HasFlag(ValueFlag.Bin)) {

                // Output the separator first if necessary
                PrintValueSeparator(ref needSeparator);

                // Output the binary prefix if requested
                if(flags.HasFlag(ValueFlag.Prefix))
                    PrintColor((ConsoleColor) Color.Deemphasis, "0b");

                // Output the binary value
                if(flags.HasFlag(ValueFlag.Color))
                    PrintValueBinColor(value, bytes * 8);
                else
                    PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString(value, bytes * 8, 2));

                // Request the separator before the next output (if any)
                needSeparator = true;
            }

            // Check if asked to output the decimal value
            if(flags.HasFlag(ValueFlag.Dec)) {

                // Output the separator first if necessary
                PrintValueSeparator(ref needSeparator);

                // Output the decimal value
                PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString(value, 0, 10));
            }

            // Prints out the separator and resets the request
            void PrintValueSeparator(ref bool needSeparator) {
                if(needSeparator) {
                    Console.Write(" = ");
                    needSeparator = false;
                }
            }

        }

        // Outputs a color-formatted binary value (without prefix)
        public static void PrintValueBinColor(uint value, int bits = 8) {

            // The color for set bits depends on whether only a single bit is set
            ConsoleColor setColor = Conv.GetBitCount(value) == 1 ?
                (ConsoleColor) Color.ValueSingleBit : (ConsoleColor) Color.Value;

            // Iterate through the string and output the color for each bit
            foreach(char c in Conv.GetString(value, bits, 2)) {
                PrintColor(c == '1' ? setColor : (ConsoleColor) Color.ValueBinUnset, c.ToString());
            }

        }
        // Outputs a color-formatted hexadecimal value (without prefix)
        public static void PrintValueHexColor(uint value, int bits = 8) {

            // Pick the color
            ConsoleColor valueColor =
                value != 0 ? // Special color for empty and full values
                (value != byte.MaxValue && value != ushort.MaxValue && value != uint.MaxValue) ? 
                Conv.GetBitCount(value) == 1 ? // Terribly inefficient
                (ConsoleColor) Color.ValueSingleBit : (ConsoleColor) Color.Value : (ConsoleColor) Color.ValueFull : (ConsoleColor) Color.ValueEmpty;

            // Output the value in color
            PrintColor(valueColor, Conv.GetString(value, bits / 4, 16));

        }
#endregion

#region Output Methods - Application-Specific
        // Outputs an action keyword, depending on the action
        public static void PrintAction(bool isSet = false) {
            if(isSet)
                PrintColor((ConsoleColor) Color.ActionSet, Config.Locale.Get(Config.L_CLI + "ActionSet"));
            else
                PrintColor((ConsoleColor) Color.ActionGet, Config.Locale.Get(Config.L_CLI + "ActionGet"));
        }

        // Outputs the context (top-level command-line argument)
        public static void PrintContext(string header, string argument = null) {
            PrintColor((ConsoleColor) Color.Context, header);
            if(argument != null) {
                Console.Write(" (" + argument + ")");
            }
            Console.WriteLine();
        }

        // Outputs an error message
        public static void PrintError(string message, Exception e = null) {
            PrintColor((ConsoleColor) Color.Error, message + Environment.NewLine);
            if(e != null) {
                Console.Error.WriteLine(
                    Environment.NewLine +
                    "{0}: {1}" + Environment.NewLine +
                    "{2}", e.Source, e.TargetSite, e.StackTrace);

            }

        }

        // Outputs explanatory information for the value when present
        public static void PrintExplanation(string explanation = null) {
            if(explanation != null) {
                Console.Write(" [");
                Console.Write(explanation);
                Console.Write("]");
            }
        }

        // Prints a formatted row of a fan table entry
        public static void PrintFanTableEntry(int number, byte temperature, byte[] level) {

            // Level number
            PrintColor((ConsoleColor) Color.Deemphasis, "# ");
            PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString((uint) number, 2, 10));

            // Fan #1 speed level value
            Console.Write(": " + Enum.GetName(typeof(BiosData.FanType), 1) + " ");
            PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString((uint) level[0] * 100, 4, 10));
            PrintColor((ConsoleColor) Color.Deemphasis, " " + Config.Locale.Get(Config.L_UNIT + "RotationRate"));

            // Fan #2 speed level value
            Console.Write(" " + Enum.GetName(typeof(BiosData.FanType), 2) + " ");
            PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString((uint) level[1] * 100, 4, 10));
            PrintColor((ConsoleColor) Color.Deemphasis, " " + Config.Locale.Get(Config.L_UNIT + "RotationRate"));

            // Temperature value
            Console.Write(" @ ");
            PrintColor((ConsoleColor) Color.Emphasis, Conv.GetString(temperature, 2, 10));
            PrintColor((ConsoleColor) Color.Deemphasis, " " + Config.Locale.Get(Config.L_UNIT + "Temperature"));
	    
            Console.WriteLine("");

        }


        // Prints out the header in command-line mode
        public static void PrintHeader() {
            PrintColor((ConsoleColor) Color.HeaderTitle, Config.AppName);
            PrintColor((ConsoleColor) Color.HeaderCaption, " " + Config.Locale.Get(Config.L_CLI + "Header") + " ");
            PrintColor((ConsoleColor) Color.HeaderVersion, Config.Locale.Get(Config.L_CLI + "HeaderVersion") + " " + Config.AppVersion);

            // Output translation credit
            string translationCredit = Config.Locale.Get(Config.L_CLI + "Translated");
            if(translationCredit != "")
                Console.Write(Environment.NewLine + Config.Locale.Get(Config.L_CLI + "Translated"));

            Console.WriteLine();
        }

        // Outputs an action keyword, depending on the action
        public static void PrintState(bool isSet = false) {
            if(isSet)
                PrintColor((ConsoleColor) Color.StateOn, Config.Locale.Get(Config.L_CLI + "StateOn"));
            else
                PrintColor((ConsoleColor) Color.StateOff, Config.Locale.Get(Config.L_CLI + "StateOff"));
        }

        // Prints out the usage information in command-line mode
        public static void PrintUsage() {

            string data =
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "Byte") + ", " + Config.Locale.Get(Config.L_DATATYPE_NAME + "Reg") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "Byte") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "ByteArray") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "ByteArray") + ", " +
                Config.Locale.Get(Config.L_DATATYPE_NAME + "Bool") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "Bool") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "Color4") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "Color4") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "FanLevel") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "FanLevel") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "FanMode") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "FanMode") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "FanTable") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "FanTable") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "GpuMode") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "GpuMode") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "GpuPowerLevel") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "GpuPowerLevel") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "TName") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "TName") + Environment.NewLine +
                "  " + Config.Locale.Get(Config.L_DATATYPE_NAME + "Word") + ": " + Config.Locale.Get(Config.L_DATATYPE_SYNTAX + "Word") + Environment.NewLine;

            Console.WriteLine(Config.Locale.Get(Config.L_CLI + "UsageText"), Config.AppName, data);

        }
#endregion

    }

}
