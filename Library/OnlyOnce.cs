// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.IO;
using StarMon.Library;

namespace StarMon.Library {

    // Sets a state that persists until reboot only
    public class OnlyOnce {

        private readonly string FileName;
        private readonly bool IsFirstTime;

        // Initializes the class
        public OnlyOnce(string name) {

            // Determine the lock file name
            FileName = Config.OnlyOncePath + "\\" + name + Config.OnlyOnceFileExt;

            // Claim the lock; whether the claim succeeded is the answer
            IsFirstTime = Claim();

        }

        // Checks the state
        public bool Check() {

            // The claim happened in the constructor, which is the only place
            // it can happen without racing itself
            return IsFirstTime;

        }

        // Creates the marker file, and says whether this call is the one that
        // created it.
        //
        // The creation is the test. Asking File.Exists first and creating
        // afterwards leaves a window between the two, and the caller that this
        // exists for — the graphics-mux fix, spawned from a registry-change
        // event that can fire twice in quick succession during a mode switch —
        // is exactly the kind that arrives twice at once. Both processes saw no
        // file, both believed they were first, and both went on to restart the
        // shell and the display service. FileMode.CreateNew is atomic: the
        // second one is refused by the file system.
        private bool Claim() {

            try {

                using(new FileStream(FileName, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None)) { }

            } catch(IOException) {

                // CreateNew reports "it is already there" and "the directory
                // does not exist" through the same exception type, so the two
                // have to be told apart: only the first means somebody else
                // was here. Anything else is a marker that cannot be written,
                // and refusing to run at all is worse than running twice —
                // which is also how this behaved before.
                try { return !File.Exists(FileName); } catch { return true; }

            } catch {

                return true;

            }

            // Reset the state on reboot
            try { Os.RemoveOnReboot(FileName); } catch { }

            return true;

        }

    }

}
