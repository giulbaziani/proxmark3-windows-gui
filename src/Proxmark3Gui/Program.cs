using System;
using System.Windows.Forms;
using Proxmark3Gui.Core;
using Proxmark3Gui.UI;

namespace Proxmark3Gui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Diag.Init();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // The app needs administrator rights (usbipd and WSL setup require elevation). The exe
            // manifest already asks Windows to elevate, but if it is ever launched without elevation
            // (e.g. via `dotnet run`), relaunch ourselves elevated so the user just sees a UAC prompt.
            if (!Elevation.IsAdministrator())
            {
                Diag.Line("Not elevated — requesting administrator rights.");
                if (!Elevation.RelaunchAsAdmin())
                {
                    MessageBox.Show(
                        "Proxmark3 Windows GUI must run as administrator, because it hands the USB device " +
                        "to Linux and installs Windows features.\n\n" +
                        "Right-click the app and choose \"Run as administrator\".",
                        "Administrator required",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                // Either the elevated instance has taken over, or the user declined the prompt.
                return;
            }

            Application.ThreadException += (s, e) => { Diag.Crash("ThreadException", e.Exception); ShowCrash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Diag.Crash("UnhandledException", e.ExceptionObject as Exception);
                ShowCrash(e.ExceptionObject as Exception);
            };

            Application.Run(new MainForm());
        }

        private static void ShowCrash(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    "Something went wrong:\n\n" + (ex != null ? ex.ToString() : "unknown error") +
                    "\n\nDetails were written to:\n" + Diag.CrashLogPath,
                    "Proxmark3 Windows GUI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
