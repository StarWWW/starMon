// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using StarMon.Library;

namespace StarMon.Hardware.Ec {

    // Implements an exclusive lock mechanism for accessing the Embedded Controller
    public static class EmbeddedControllerMutex {

        private static Mutex m;

        // Closes the lock
        public static void Close() {
            m?.Close(); }

        // Sets up a new lock
        public static void Open() {
            m = CreateOrOpenExistingMutex(Config.LockPathEc);

            static Mutex CreateOrOpenExistingMutex(string name) {
                try {
                    MutexSecurity security = new MutexSecurity();
                    security.AddAccessRule(
                        new MutexAccessRule(
                            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                            MutexRights.FullControl, AccessControlType.Allow));
                    return new Mutex(false, name, out _, security);
                } catch(UnauthorizedAccessException) {
                    try {
                        return Mutex.OpenExisting(name, MutexRights.Synchronize);
                    } catch {
                    }
                }
                return null;
            }
        }

        // Tries to release the lock
        // Will throw an exception if no lock was set
        public static void Release() {
            try {
                m?.ReleaseMutex();
            } catch {
            }
        }

        // Waits until the lock is released
        public static bool Wait(int timeout) {

            // Open() leaves the mutex null when it can neither create nor open
            // one, which a locked-down security context can cause. Without
            // this guard every EC access then dies on a null dereference that
            // none of the catches below would have caught.
            if(m == null)
                return false;

            try {
                return m.WaitOne(timeout, false);
            } catch(AbandonedMutexException) {
                return true;
            } catch(InvalidOperationException) {
                return false;
            }
        }

    }

}
