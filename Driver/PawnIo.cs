// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

// PawnIO is copyright © namazso and licensed under the LGPL-2.1-or-later.
// The module blobs this file loads are unmodified builds from the project's
// own signed release; see Resources/README.md for which one.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace StarMon.Driver {

    // The other way into the Embedded Controller.
    //
    // Reaching the controller means reading and writing two I/O ports, and
    // nothing in user mode may do that — it takes a kernel driver. The one
    // this application carries is WinRing0 1.2.0.5, which Microsoft lists as a
    // vulnerable driver, and that list has been enforced by default since the
    // Windows 11 2022 update. On a machine where it is enforced the driver
    // does not load, and no amount of retrying changes that.
    //
    // PawnIO is the answer the rest of this field settled on. It is a signed
    // driver that is not on the list, and it does not expose raw port access:
    // it runs a small verified program, supplied by the caller, inside the
    // kernel. The program for the Embedded Controller — LpcACPIEC — permits
    // exactly two ports, 0x62 and 0x66, and refuses everything else. That is
    // the whole of what this application needs, and it is a far smaller thing
    // to hand a driver than "read any port you like".
    //
    // This class is only the plumbing: finding the library, opening an
    // executor, handing it a module and calling into it. Which backend is
    // actually used, and what for, is LowLevel's decision.
    public static class PawnIo {

        // Where the library was found, empty until looked for
        public static string LibraryPath { get; private set; }

        // The running commentary, for the log and the hardware report. Every
        // step that could fail appends to it, so a machine where this does not
        // work says why rather than only that.
        private static readonly StringBuilder Log = new StringBuilder();

        private static bool Probed;
        private static bool Present;
        private static uint LibraryVersion;

        // Whether PawnIO is installed and its library could be loaded.
        //
        // Loaded by full path rather than left to the search order: the
        // installer does not put its directory on PATH, so a plain
        // DllImport("PawnIOLib") would throw DllNotFoundException on the first
        // call — an exception is a poor way to answer a question that has a
        // perfectly good answer.
        public static bool IsAvailable {
            get {

                if(Probed)
                    return Present;

                Probed = true;

                string path = Find();
                if(path == null) {
                    Log.AppendLine("PawnIOLib.dll was not found; PawnIO does "
                        + "not appear to be installed");
                    return false;
                }

                // Loading by full path registers the module under its base
                // name, so the DllImport declarations below resolve to this
                // very copy rather than searching again
                if(Kernel.LoadLibrary(path) == IntPtr.Zero) {
                    Log.AppendLine("Found \"" + path + "\" but it would not load"
                        + " (error " + Marshal.GetLastWin32Error() + ")");
                    return false;
                }

                LibraryPath = path;

                uint version;
                int result;
                try {
                    result = pawnio_version(out version);
                } catch(Exception e) {
                    Log.AppendLine("Loaded \"" + path
                        + "\" but could not call into it: " + e.Message);
                    return false;
                }

                if(result != 0) {
                    Log.AppendLine("pawnio_version failed (0x"
                        + result.ToString("X8") + ")");
                    return false;
                }

                LibraryVersion = version;
                Present = true;
                return true;

            }
        }

        // The installed library version as text, empty when not available
        public static string Version {
            get {
                if(!IsAvailable)
                    return "";
                return ((LibraryVersion >> 16) & 0xFFFF) + "."
                    + ((LibraryVersion >> 8) & 0xFF) + "."
                    + (LibraryVersion & 0xFF);
            }
        }

        // Everything that went wrong on the way, empty when nothing did
        public static string GetStatus() {
            return Log.ToString();
        }

        // Locates PawnIOLib.dll.
        //
        // The installer records its directory in the uninstall entry, which is
        // the only place it is written down; the two Program Files variables
        // cover a default install where that entry is missing or names a
        // directory that has since been removed.
        private static string Find() {

            foreach(string directory in new string[] {
                FromUninstallEntry(),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramW6432")
            }) {

                if(string.IsNullOrEmpty(directory))
                    continue;

                try {

                    // The uninstall entry names the install directory itself;
                    // the environment variables name its parent
                    foreach(string candidate in new string[] {
                        Path.Combine(directory, "PawnIOLib.dll"),
                        Path.Combine(directory, "PawnIO", "PawnIOLib.dll")
                    })
                        if(File.Exists(candidate))
                            return candidate;

                } catch { }

            }

            return null;

        }

        // Reads the install directory out of the uninstall entry
        private static string FromUninstallEntry() {
            try {

                using(RegistryKey uninstall = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")) {

                    if(uninstall == null)
                        return null;

                    foreach(string name in uninstall.GetSubKeyNames())
                        using(RegistryKey entry = uninstall.OpenSubKey(name)) {

                            if(entry == null)
                                continue;

                            string display = entry.GetValue("DisplayName") as string;
                            if(display == null || display.IndexOf("PawnIO",
                                StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            string location = entry.GetValue("InstallLocation") as string;
                            if(!string.IsNullOrEmpty(location))
                                return location;

                        }

                }

            } catch { }

            return null;
        }

        // Opens an executor and hands it one of the embedded modules.
        //
        // Returns null on any failure, having said why in the status. The
        // usual failure on a machine with PawnIO installed is that the process
        // is not elevated: opening an executor is a privileged operation, and
        // it is refused rather than degraded.
        public static PawnIoModule Load(string module) {

            if(!IsAvailable)
                return null;

            byte[] blob = Read(module);
            if(blob == null) {
                Log.AppendLine("Module \"" + module
                    + "\" is missing from this build");
                return null;
            }

            IntPtr handle;
            int result = pawnio_open(out handle);
            if(result != 0 || handle == IntPtr.Zero) {
                Log.AppendLine("Could not open an executor for \"" + module
                    + "\" (0x" + result.ToString("X8") + ")"
                    + (result == unchecked((int) 0x80070005)
                        ? " — administrator rights are required" : ""));
                return null;
            }

            result = pawnio_load(handle, blob, (IntPtr) blob.Length);
            if(result != 0) {

                // A module whose main() reports STATUS_NOT_SUPPORTED is not a
                // fault: it is the module saying this is not its processor,
                // which is exactly what the vendor modules are asked to decide
                Log.AppendLine("Module \"" + module + "\" was refused (0x"
                    + result.ToString("X8") + ")");

                pawnio_close(handle);
                return null;

            }

            return new PawnIoModule(module, handle);

        }

        // Reads a module out of this assembly.
        //
        // Internal so a test can check that the modules are actually in the
        // build and are byte-for-byte what was published. A dropped or altered
        // resource fails silently otherwise: the driver refuses a module it
        // cannot verify, and the application falls back to WinRing0 on every
        // machine — which looks exactly like PawnIO not being installed.
        internal static byte[] Read(string module) {
            try {

                Assembly assembly = typeof(PawnIo).Assembly;
                using(Stream stream = assembly.GetManifestResourceStream(
                    "StarMon." + module + ".bin")) {

                    if(stream == null)
                        return null;

                    byte[] blob = new byte[stream.Length];
                    int read = 0;
                    while(read < blob.Length) {
                        int step = stream.Read(blob, read, blob.Length - read);
                        if(step <= 0)
                            return null;
                        read += step;
                    }

                    return blob;

                }

            } catch {
                return null;
            }
        }

#region Native Methods
        [DllImport("PawnIOLib.dll", ExactSpelling = true)]
        private static extern int pawnio_version(out uint version);

        [DllImport("PawnIOLib.dll", ExactSpelling = true)]
        private static extern int pawnio_open(out IntPtr handle);

        [DllImport("PawnIOLib.dll", ExactSpelling = true)]
        private static extern int pawnio_load(IntPtr handle, byte[] blob, IntPtr size);

        [DllImport("PawnIOLib.dll", ExactSpelling = true, CharSet = CharSet.Ansi)]
        internal static extern int pawnio_execute(IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ulong[] input, IntPtr inputCount,
            ulong[] output, IntPtr outputCount,
            out IntPtr returned);

        [DllImport("PawnIOLib.dll", ExactSpelling = true)]
        internal static extern int pawnio_close(IntPtr handle);

        // Kept out of External/Kernel.cs deliberately: that file is the
        // application's own P/Invoke surface, and this one entry point exists
        // only so the library above can be loaded from where it actually is
        private static class Kernel {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr LoadLibrary(string fileName);
        }
#endregion

    }

    // One loaded module, and the calls into it.
    //
    // A module is a program running inside the driver, and each one is opened
    // separately: the Embedded Controller module and the processor module have
    // nothing to do with each other and either can be absent without the other
    // being affected.
    public sealed class PawnIoModule : IDisposable {

        private IntPtr Handle;

        public string Name { get; private set; }

        public bool IsOpen {
            get { return this.Handle != IntPtr.Zero; }
        }

        internal PawnIoModule(string name, IntPtr handle) {
            this.Name = name;
            this.Handle = handle;
        }

        // Calls a function in the module.
        //
        // Sizes are counts of 64-bit words, not bytes — which is what the
        // library means by them, and getting it wrong is the kind of mistake
        // that reads a plausible number out of the wrong place.
        public bool Execute(string function, ulong[] input, ulong[] output) {

            if(!this.IsOpen)
                return false;

            if(input == null)
                input = EmptyBuffer;
            if(output == null)
                output = EmptyBuffer;

            try {

                IntPtr returned;
                return PawnIo.pawnio_execute(this.Handle, function,
                    input, (IntPtr) input.Length,
                    output, (IntPtr) output.Length,
                    out returned) == 0;

            } catch {
                return false;
            }

        }

        private static readonly ulong[] EmptyBuffer = new ulong[0];

        public void Dispose() {

            IntPtr handle = this.Handle;
            this.Handle = IntPtr.Zero;

            if(handle == IntPtr.Zero)
                return;

            try {
                PawnIo.pawnio_close(handle);
            } catch { }

        }

    }

}
