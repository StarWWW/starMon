// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using StarMon.External;
using StarMon.Library;

namespace StarMon.Ui.Shell {

    // The notification-area icon.
    //
    // WPF has no equivalent of NotifyIcon, so this is it: a window that is
    // never shown, existing only to receive the shell's callbacks, and a
    // NOTIFYICONDATA kept in step with it.
    //
    // Two details are worth knowing about, because both fail silently.
    //
    // The window is a real top-level window rather than a message-only one.
    // A message-only window is cheaper and receives the icon's own callbacks
    // perfectly well — but broadcast messages are only delivered to top-level
    // windows, and "TaskbarCreated" is a broadcast. Without it, restarting
    // Explorer takes the icon away for good.
    //
    // The shell is asked to behave as version 4, which changes the callback
    // protocol: selection arrives as NIN_SELECT and the context menu as
    // WM_CONTEXTMENU, each carrying the anchor point in wParam. That anchor is
    // where the menu belongs — the cursor position is close but wrong for
    // keyboard invocation, and the WinForms build had to clamp a guess into
    // the working area to compensate.
    public sealed class TrayIcon : IDisposable {

        // The message the shell sends back to us. Any value from WM_USER up is
        // ours to choose, since the window receives nothing else.
        private const int CallbackMessage = User32.WM_USER + 1;

        // Raised when the user activates the icon: a left click, or Enter and
        // Space when it has keyboard focus
        public event Action Selected;

        // Raised when the user asks for the context menu, with the anchor
        // point the shell nominated, in physical screen pixels
        public event Action<Point> ContextMenu;

        // Raised when the user clicks the balloon notification itself
        public event Action BalloonClicked;

        // Raised for any other message this window receives, with the message
        // and its two parameters.
        //
        // The window is here for the icon, but it is also the only top-level
        // window a tray application reliably owns — and broadcast messages
        // reach nothing else. That is how a second copy of the application
        // tells the first one to show itself.
        public event Action<int, IntPtr, IntPtr> Message;

        private readonly HwndSource Source;
        private readonly uint TaskbarCreatedMessage;
        private Shell32.NOTIFYICONDATA Data;

        private IntPtr IconHandle;
        private bool IsAdded;
        private bool IsDisposed;

        public TrayIcon(string tooltip) {

            // Never shown, but top-level: see the note above
            HwndSourceParameters parameters =
                new HwndSourceParameters("StarMonTray") {
                    Width = 0,
                    Height = 0,
                    WindowStyle = unchecked((int) 0x80000000)  // WS_POPUP
                };

            this.Source = new HwndSource(parameters);
            this.Source.AddHook(Hook);

            // The shell re-broadcasts this when it restarts, and every icon
            // that wants to survive that has to add itself again
            this.TaskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");

            this.Data = new Shell32.NOTIFYICONDATA {
                cbSize = (uint) Marshal.SizeOf(typeof(Shell32.NOTIFYICONDATA)),
                hWnd = this.Source.Handle,
                uID = 1,
                uFlags = Shell32.NotifyIconFlags.Message
                       | Shell32.NotifyIconFlags.Icon
                       | Shell32.NotifyIconFlags.Tip
                       | Shell32.NotifyIconFlags.ShowTip,
                uCallbackMessage = CallbackMessage,
                hIcon = IntPtr.Zero,
                szTip = Trim(tooltip, 127),
                szInfo = "",
                szInfoTitle = "",
                guidItem = Guid.Empty
            };

        }

        // Adds the icon to the notification area
        // The window the icon belongs to. A tray application owns no other,
        // so it is what anything needing a handle of this process uses.
        public IntPtr Handle {
            get { return this.Source != null ? this.Source.Handle : IntPtr.Zero; }
        }

        public void Show() {

            if(this.IsDisposed || this.IsAdded)
                return;

            if(!Shell32.Shell_NotifyIcon(Shell32.NotifyIconMessage.Add, ref this.Data)) {
                Logger.Error("TrayIcon", "The notification icon could not be added", "");
                return;
            }

            this.IsAdded = true;

            // Opt into the modern callback protocol. This is a separate call
            // because the shell only accepts it for an icon that already
            // exists, and it has to be made every time the icon is added.
            this.Data.uVersion = Shell32.NOTIFYICON_VERSION_4;
            Shell32.Shell_NotifyIcon(Shell32.NotifyIconMessage.SetVersion, ref this.Data);

        }

        // Replaces the icon, taking ownership of the handle
        public void SetIcon(IntPtr icon) {

            if(this.IsDisposed || icon == IntPtr.Zero)
                return;

            IntPtr previous = this.IconHandle;

            this.IconHandle = icon;
            this.Data.hIcon = icon;
            Modify();

            // Only after the shell has been given the new one: destroying the
            // handle it is still drawing leaves a blank space in the tray
            if(previous != IntPtr.Zero)
                User32.DestroyIcon(previous);

        }

        // Replaces the tooltip. Version 4 allows 128 characters, which is why
        // the WinForms build's reflection trick to defeat the old 64-character
        // limit is not needed here.
        public void SetTooltip(string text) {

            if(this.IsDisposed)
                return;

            this.Data.szTip = Trim(text, 127);
            Modify();

        }

        // Shows a balloon notification above the icon
        public void ShowBalloon(string message, string title,
            Shell32.NotifyIconInfoFlags icon) {

            if(this.IsDisposed || !this.IsAdded)
                return;

            this.Data.uFlags |= Shell32.NotifyIconFlags.Info;
            this.Data.szInfo = Trim(message, 255);
            this.Data.szInfoTitle = Trim(title, 63);
            this.Data.dwInfoFlags = icon;

            Modify();

            // Leaving the info field set would repeat the balloon on the next
            // unrelated update, so it is cleared again straight away
            this.Data.uFlags &= ~Shell32.NotifyIconFlags.Info;
            this.Data.szInfo = "";

        }

        private void Modify() {
            if(this.IsAdded)
                Shell32.Shell_NotifyIcon(Shell32.NotifyIconMessage.Modify, ref this.Data);
        }

        // The fields are fixed-size inline buffers, and marshalling a string
        // longer than one of them throws rather than truncating
        private static string Trim(string text, int max) {
            if(string.IsNullOrEmpty(text))
                return "";
            return text.Length <= max ? text : text.Substring(0, max);
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
            ref bool handled) {

            if(msg == this.TaskbarCreatedMessage) {

                // Explorer restarted and took the icon with it
                this.IsAdded = false;
                Show();
                return IntPtr.Zero;

            }

            if(msg != CallbackMessage) {

                Action<int, IntPtr, IntPtr> other = this.Message;
                if(other != null)
                    try { other(msg, wParam, lParam); }
                    catch(Exception e) {
                        Logger.Error("TrayIcon", "A message handler failed", e.Message);
                    }

                return IntPtr.Zero;

            }

            // Under version 4 the low word of lParam is the notification and
            // wParam carries the anchor point, which is the other way round
            // from the protocol the older versions used
            int notification = (int) ((uint) lParam.ToInt64() & 0xFFFF);
            int x = (short) ((uint) wParam.ToInt64() & 0xFFFF);
            int y = (short) (((uint) wParam.ToInt64() >> 16) & 0xFFFF);

            switch(notification) {

                case Shell32.NIN_SELECT:
                case Shell32.NIN_KEYSELECT:
                    Raise(this.Selected);
                    break;

                case User32.WM_CONTEXTMENU:
                    Action<Point> menu = this.ContextMenu;
                    if(menu != null)
                        try { menu(new Point(x, y)); }
                        catch(Exception e) {
                            Logger.Error("TrayIcon", "Showing the menu failed", e.Message);
                        }
                    break;

                case Shell32.NIN_BALLOONUSERCLICK:
                    Raise(this.BalloonClicked);
                    break;

            }

            handled = true;
            return IntPtr.Zero;

        }

        // A handler that throws must not be allowed to escape into the window
        // procedure, where it would take the message loop down with it
        private static void Raise(Action handler) {
            if(handler == null)
                return;
            try {
                handler();
            } catch(Exception e) {
                Logger.Error("TrayIcon", "A notification icon handler failed", e.Message);
            }
        }

        public void Dispose() {

            if(this.IsDisposed)
                return;

            this.IsDisposed = true;

            // The icon has to be removed explicitly: left behind, it lingers
            // in the tray as a ghost until the user waves the cursor over it
            if(this.IsAdded)
                Shell32.Shell_NotifyIcon(Shell32.NotifyIconMessage.Delete, ref this.Data);

            if(this.IconHandle != IntPtr.Zero) {
                User32.DestroyIcon(this.IconHandle);
                this.IconHandle = IntPtr.Zero;
            }

            this.Source.RemoveHook(Hook);
            this.Source.Dispose();

        }

    }

}
