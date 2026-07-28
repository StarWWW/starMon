// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows;
using StarMon.Library;

namespace StarMon.Ui.Shell {

    // The two things the application has to be able to say before it has a
    // window, or when it is on its way down.
    //
    // Deliberately the system's own dialogs rather than themed ones. An error
    // reported here is one the application did not expect, and quite possibly
    // one that happened while the interface was being built — so a dialog that
    // depends on the theme, the resource dictionary and a working render pass
    // is a dialog that may not appear at all. The one moment it is needed is
    // the one moment it cannot afford to be clever.
    public static class Dialogs {

        public static void Error(string message, Exception detail = null) {

            string text = message;

            if(detail != null)
                text += Environment.NewLine + Environment.NewLine
                    + detail.Source + ": " + detail.TargetSite
                    + Environment.NewLine + Environment.NewLine
                    + detail.StackTrace;

            Show(text, Config.AppName, MessageBoxImage.Warning);

        }

        public static bool Confirm(string message) {

            return MessageBox.Show(message, Config.AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.Yes) == MessageBoxResult.Yes;

        }

        private static void Show(string text, string caption, MessageBoxImage icon) {

            try {

                MessageBox.Show(text, caption, MessageBoxButton.OK, icon);

            } catch {

                // Even this can fail — during shutdown there may no longer be
                // a dispatcher to show it on. The log is the fallback, and is
                // where anyone diagnosing this afterwards will look anyway.
                Logger.Error("Dialog", "A message could not be shown", text);

            }

        }

    }

}
