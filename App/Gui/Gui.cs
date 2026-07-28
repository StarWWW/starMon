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

        // Default dialog font name
        public const string DIALOG_FONT = "MS Shell Dlg"; 

        // Message box flags
        public const int MB_SYSTEMMODAL = 0x00001000;  // On top of other topmost windows
        public const int MB_TASKMODAL   = 0x00002000;  // Also prevent interaction with other windows
        public const int MB_TOPMOST     = 0x00004000;  // Stay on top

#region Common Identifiers
        // Type
        public const string T_BAR = "Bar";    // ProgressBar
        public const string T_BTN = "Btn";    // Button
        public const string T_CHK = "Chk";    // CheckBox
        public const string T_CMB = "Cmb";    // ComboBox
        public const string T_FRM = "Form";   // Form
        public const string T_GRP = "Grp";    // GroupBox
        public const string T_LBL = "Lbl";    // Label
        public const string T_LNK = "Lnk";    // LinkLabel
        public const string T_PIC = "Pic";    // PictureBox
        public const string T_RDO = "Rdo";    // RadioButton
        public const string T_RTF = "Rtf";    // RtfText
        public const string T_TRK = "Trk";    // TrackBar
        public const string T_TBL = "Tbl";    // TblLayout
        public const string T_TXT = "Txt";    // TextBox

        // Group
        public const string G_FAN = "Fan";    // Fan (form & menu)
        public const string G_GPU = "Gpu";    // Graphics (menu)
        public const string G_KBD = "Kbd";    // Keyboard (form & menu)
        public const string G_TMP = "Tmp";    // Temperature (form)
        public const string G_SET = "Set";    // Settings (menu)
        public const string G_SYS = "Sys";    // System Status (form)

        // Menu item type
        public const string M_ACT = "Act";    // Action
        public const string M_HDR = "Hdr";    // Header
        public const string M_SUB = "Sub";    // Sub-menu

        // Interfix
        public const string X_UNIT = "Unit";  // Unit

        // Suffix
        public const string S_CAP = "Cap";    // Caption
        public const string S_LVL = "Lvl";    // Level
        public const string S_RTE = "Rte";    // Rate
        public const string S_VAL = "Val";    // Value
#endregion

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

            // Obtain a pointer to the recipient structure
            IntPtr RecipientPtr = Marshal.AllocHGlobal(Marshal.SizeOf(Recipient));
            Marshal.StructureToPtr(Recipient, RecipientPtr, false);

            // Register for power suspend and resume notifications
            return PowrProf.PowerRegisterSuspendResumeNotification(
                PowrProf.DEVICE_NOTIFY_CALLBACK,
                ref Recipient, ref RegistrationHandle) == 0;

        }

        // Removes the callback for power event notifications
        public static bool UnregisterSuspendResumeNotification() {
            return RegistrationHandle != null ?
                PowrProf.PowerUnregisterSuspendResumeNotification(RegistrationHandle) == 0
                : true;
        }
#endregion

#region Scaling
        // Calculate the DPI from the reported device capabilities
        public static float GetDeviceCapsDpi(IntPtr handle) {
            IntPtr hDC = User32.GetDC(handle);
            try {

                // Note: currently seems to always report 96 dpi,
                // which makes this approach no longer very useful
                return 96f * Gdi32.GetDeviceCaps(hDC, Gdi32.DeviceCap.DESKTOPHORZRES)
                    / Gdi32.GetDeviceCaps(hDC, Gdi32.DeviceCap.HORZRES);

            } finally {

                // Release the device context
                User32.ReleaseDC(handle, hDC);

            }
        }

#endregion

#region Visual
        // Shows a dialog window with error information
        public static void ShowError(string message, Exception e = null) {
            StarMon.Ui.Shell.Dialogs.Error(message, e);
        }

        // Describes a hotkey the way it is written on a menu
        public static string HotkeyToString(int mods, int vk) {

            if(vk == 0)
                return Config.Locale.Get(Config.L_GUI + "HotkeyNotAssigned");

            string text = "";
            if((mods & (int) User32.MOD_CONTROL) != 0) text += Config.Locale.Get(Config.L_GUI + "HotkeyModCtrl");
            if((mods & (int) User32.MOD_ALT) != 0) text += Config.Locale.Get(Config.L_GUI + "HotkeyModAlt");
            if((mods & (int) User32.MOD_SHIFT) != 0) text += Config.Locale.Get(Config.L_GUI + "HotkeyModShift");
            if((mods & (int) User32.MOD_WIN) != 0) text += Config.Locale.Get(Config.L_GUI + "HotkeyModWin");

            // The virtual key as WPF names it. The Windows Forms Keys
            // enumeration named the same values and is no longer referenced.
            return text + System.Windows.Input.KeyInterop
                .KeyFromVirtualKey(vk).ToString();

        }

        // Asks before restarting, since some BIOS settings only take effect
        // after one
        public static void ShowPromptReboot() {

            if(StarMon.Ui.Shell.Dialogs.Confirm(
                Config.Locale.Get(Config.L_GUI + "PromptReboot")))
                Os.RestartSystem();

        }

        // Restores a window and brings it to the front
        public static void ShowToFront(IntPtr window) {

            User32.ShowWindow(window, User32.SW_MINIMIZE);
            User32.ShowWindow(window, User32.SW_RESTORE);
            User32.ShowWindow(window, User32.SW_SHOWNORMAL);
            User32.SetForegroundWindow(window);
            User32.SwitchToThisWindow(window, false);

        }

        // Opts a window's title bar into the immersive dark mode where supported.
        // Silently does nothing on Windows releases that lack the attribute.
        public static void SetImmersiveDarkMode(IntPtr window, bool enabled = true) {
            if(window == IntPtr.Zero)
                return;
            try {
                int value = enabled ? 1 : 0;
                // Try the modern attribute first, then fall back to the older one
                if(DwmApi.DwmSetWindowAttribute(
                        window, DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                    DwmApi.DwmSetWindowAttribute(
                        window, DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
            } catch {
                // Attribute unsupported on this Windows version; ignore
            }
        }
#endregion

    }

}
