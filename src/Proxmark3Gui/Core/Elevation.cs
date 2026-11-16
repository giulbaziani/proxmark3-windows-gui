using System;
using System.Diagnostics;
using System.Security.Principal;

namespace Proxmark3Gui.Core
{
    public static class Elevation
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>Restarts the app elevated. Returns false when the user declines the UAC prompt.</summary>
        public static bool RelaunchAsAdmin()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }
    }
}
