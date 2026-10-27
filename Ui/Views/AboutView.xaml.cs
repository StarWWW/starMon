// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using StarMon.Library;

namespace StarMon.Ui.Views {

    // The application's own page: what it is, what it is running on, where
    // it keeps its files, and whose licence it is under. Shares
    // SystemViewModel with the System section because the machine facts are
    // the same facts.
    public partial class AboutView : UserControl {

        public AboutView() {

            InitializeComponent();

            this.ShowConfig.Click += (s, e) => Reveal(Config.FilePath);
            this.ShowLog.Click += (s, e) => Reveal(LogFile());
            this.OpenHomepage.Click += (s, e) => Launch(Config.AppHomepageLink);

            this.ConfigPath.Text = Config.FilePath ?? "";

        }

        // The log file if one is being written, or the folder it would be
        // written to — which is the configuration's folder either way
        private static string LogFile() {
            try {
                string path = Config.LogFilePath;
                return !string.IsNullOrEmpty(path) && File.Exists(path)
                    ? path : Path.GetDirectoryName(Config.FilePath);
            } catch {
                return Path.GetDirectoryName(Config.FilePath ?? "");
            }
        }

        // Shows a file selected in its folder, or a folder, in Explorer.
        //
        // Through explorer.exe rather than by starting the file: this process
        // runs elevated, and anything it starts directly runs elevated with
        // it. Explorer hands the request to the shell the user is already
        // running, so the window that opens is an ordinary one.
        private static void Reveal(string path) {

            if(string.IsNullOrEmpty(path))
                return;

            try {
                if(File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if(Directory.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
            } catch(Exception e) {
                Logger.Error("Window", "Could not show " + path, e.Message);
            }

        }

        // Opens a link in the user's own browser, the same way and for the
        // same reason: a browser started straight from here would be an
        // elevated one
        private static void Launch(string url) {

            if(string.IsNullOrEmpty(url))
                return;

            try {
                Process.Start("explorer.exe", "\"" + url + "\"");
            } catch(Exception e) {
                Logger.Error("Window", "Could not open " + url, e.Message);
            }

        }

    }

}
