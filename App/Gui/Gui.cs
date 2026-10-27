// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;
using StarMon.External;
using StarMon.Library;

namespace StarMon.AppGui {

    // Implements general GUI-specific functionality
    public static class Gui {

        // Registration handle
        private static IntPtr RegistrationHandle;

        // Registration callback
        private static PowrProf.DeviceNotifyCallbackRoutine RegistrationCallback;

        // State flags
        public static bool IsInitialized { get; private set; }

        // Unique custom message identifier used to
        // tell the GUI to bring itself to the user's attention
        public static uint MessageId;

        // Custom message parameters
        public enum MessageParam : int {
            Default         =   0,  // No parameter specified
            AnotherInstance =   1,  // Another instance has been launched
            Gui             =   2,  // Autorun task has been launched
            Key             =   3,  // Omen Key event has been registered
            NoLastParam     = 255,  // Launched not as a message response
        }


#region Initialization & Termination
        // Initializes a Windows Forms (GUI) application
        public static void Initialize() {

            // Only do it once
            if(!IsInitialized) {

               // Register a custom message to communicate between application instances
               // The identifier obtained this way remains unique until user logout
               MessageId = RegisterMessage(Config.GuiMessageId);

               // Bring up the interface. An Application has to exist before
               // any view is built, both so that pack:// URIs resolve and so
               // that the theme is in scope when a control's own root
               // attributes are set — which happens before that control's own
               // resources do.
               StarMon.Ui.Shell.Theme.Initialize();

               // Set the state flag
               IsInitialized = true;

               }

        }

        // Closes the Windows Forms (GUI) application
        public static void Close() {

            // Set the state flag
            IsInitialized = false;

        }
#endregion

#region Messaging
        // Broadcasts a specific message
        public static bool BroadcastMessage(uint msg, MessageParam param = MessageParam.Default) {
            IntPtr lParam = (IntPtr) param;
            return User32.PostMessage(
                (IntPtr) User32.HWND_BROADCAST,  // Send to all top-most windows
                msg,                             // The message identifier registered beforehand
                (IntPtr) Config.AppProcessId,    // Add a semi-unique identifier to sieve out duplicates
                lParam);                         // Used to distinguish message types
        }

        // Registers a specific message
        public static uint RegisterMessage(string msg) {
            return User32.RegisterWindowMessage(msg);
        }

        // Registers a callback for suspend
        // and resume power event notifications
        public static bool RegisterSuspendResumeNotification(
            Func<IntPtr, uint, IntPtr, uint> Callback) {

            // Retain the registration handle
            RegistrationHandle = new IntPtr();

            // Set up the structure for the received data
            PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS Recipient
                = new PowrProf.DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS();

            // Populate the data structure with the callback function delegate
            RegistrationCallback = new PowrProf.DeviceNotifyCallbackRoutine(Callback);
            Recipient.Callback = RegistrationCallback;
            Recipient.Context = IntPtr.Zero;

            // The structure is passed by reference, so the marshaller owns the
            // copy the API sees and there is nothing to allocate.
            //
            // There used to be an AllocHGlobal and a StructureToPtr here whose
            // result was then not passed to anything and never freed: an
            // allocation made, orphaned and leaked on the same three lines.
            bool registered = PowrProf.PowerRegisterSuspendResumeNotification(
                PowrProf.DEVICE_NOTIFY_CALLBACK,
                ref Recipient, ref RegistrationHandle) == 0;

            if(!registered)
                RegistrationHandle = IntPtr.Zero;

            return registered;

        }

        // Removes the callback for power event notifications.
        //
        // Tested against Zero rather than null: RegistrationHandle is an
        // IntPtr, which is a value type, so the null check this used to make
        // could never be false — and the unregister was attempted even when
        // the registration had failed and there was nothing to unregister.
        public static bool UnregisterSuspendResumeNotification() {

            if(RegistrationHandle == IntPtr.Zero)
                return true;

            bool ok = PowrProf.PowerUnregisterSuspendResumeNotification(
                RegistrationHandle) == 0;

            RegistrationHandle = IntPtr.Zero;
            return ok;

        }
#endregion

#region Visual
        // Shows a dialog window with error information
        public static void ShowError(string message, Exception e = null) {
            StarMon.Ui.Shell.Dialogs.Error(message, e);
        }

#endregion

    }

}
