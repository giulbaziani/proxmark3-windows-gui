using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Proxmark3Gui.Core
{
    public class UsbDevice
    {
        public string BusId = "";
        public string VidPid = "";
        public string Description = "";
        public string State = "";

        public bool LooksLikeProxmark
        {
            get
            {
                string hay = (VidPid + " " + Description).ToLowerInvariant();
                return hay.Contains("proxmark")
                    || hay.Contains("9ac4:4b8f")   // PM3 RDV4 / generic CDC
                    || hay.Contains("2d2d:504d")   // PM3 bootloader
                    || hay.Contains("1d50:60")     // OLIMEX / community VIDs
                    || hay.Contains("usb serial device")
                    || hay.Contains("cdc");
            }
        }

        public bool IsAttached
        {
            get { return State.IndexOf("Attached", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        public override string ToString()
        {
            return BusId + "  " + VidPid + "  " + Description + "  [" + State + "]";
        }
    }

    /// <summary>usbipd-win: install it, list devices, and hand the Proxmark to WSL.</summary>
    public static class UsbIpdManager
    {
        public static bool IsInstalled()
        {
            if (ProcessRunner.ExistsOnPath("usbipd.exe")) return true;
            foreach (var p in CandidatePaths())
                if (File.Exists(p)) return true;
            return false;
        }

        private static IEnumerable<string> CandidatePaths()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(pf, "usbipd-win", "usbipd.exe");
            yield return Path.Combine(pf, "usbipd-win", "usbipd", "usbipd.exe");
        }

        private static string Exe()
        {
            foreach (var p in CandidatePaths())
                if (File.Exists(p)) return p;
            return "usbipd.exe";
        }

        /// <summary>Installs usbipd-win silently with winget, which is present on Win10 2004+ / Win11.</summary>
        public static async Task<RunResult> InstallAsync(Action<string> onLine, CancellationToken cancel)
        {
            onLine?.Invoke("[i] Installing usbipd-win via winget...");
            var r = await ProcessRunner.RunAsync("winget.exe", new[]
            {
                "install", "--id", Defaults.UsbIpdWingetId, "-e",
                "--accept-source-agreements", "--accept-package-agreements",
                "--silent", "--disable-interactivity"
            }, onLine, null, 15 * 60 * 1000, cancel).ConfigureAwait(false);

            // winget reports "already installed" with a non-zero code; that is still success for us.
            if (!r.Ok && IsInstalled()) r.ExitCode = 0;
            return r;
        }

        public static async Task<List<UsbDevice>> ListAsync(Action<string> onLine = null)
        {
            var list = new List<UsbDevice>();
            var r = await ProcessRunner.RunAsync(Exe(), new[] { "list" }, onLine, null, 30000)
                .ConfigureAwait(false);
            if (!r.Started) return list;

            bool inConnected = false;
            foreach (var raw in r.Output.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;

                if (line.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)) { inConnected = true; continue; }
                if (line.StartsWith("Persisted", StringComparison.OrdinalIgnoreCase)) { inConnected = false; continue; }
                if (line.TrimStart().StartsWith("BUSID", StringComparison.OrdinalIgnoreCase)) { inConnected = true; continue; }
                if (!inConnected) continue;

                // "2-1    9ac4:4b8f  USB Serial Device (COM5)                      Not shared"
                var m = Regex.Match(line, @"^\s*(\d+-\d+)\s+([0-9a-fA-F]{4}:[0-9a-fA-F]{4})\s+(.*?)\s{2,}(\S.*)$");
                if (!m.Success)
                {
                    m = Regex.Match(line, @"^\s*(\d+-\d+)\s+([0-9a-fA-F]{4}:[0-9a-fA-F]{4})\s+(.*)$");
                    if (!m.Success) continue;
                    list.Add(new UsbDevice
                    {
                        BusId = m.Groups[1].Value,
                        VidPid = m.Groups[2].Value,
                        Description = m.Groups[3].Value.Trim(),
                        State = ""
                    });
                    continue;
                }

                list.Add(new UsbDevice
                {
                    BusId = m.Groups[1].Value,
                    VidPid = m.Groups[2].Value,
                    Description = m.Groups[3].Value.Trim(),
                    State = m.Groups[4].Value.Trim()
                });
            }
            return list;
        }

        public static async Task<UsbDevice> FindProxmarkAsync(Action<string> onLine = null)
        {
            var all = await ListAsync(onLine).ConfigureAwait(false);
            foreach (var d in all)
                if (d.VidPid.Equals("9ac4:4b8f", StringComparison.OrdinalIgnoreCase)) return d;
            foreach (var d in all)
                if (d.LooksLikeProxmark) return d;
            return null;
        }

        /// <summary>
        /// bind (once, persistent) + attach (per session) — both need administrator rights.
        ///
        /// usbipd-win 4.0+ dropped the "--distribution" option: "attach --wsl" attaches the device
        /// to the shared WSL2 virtual machine, so it becomes visible in every WSL2 distribution
        /// (including the managed pm3-ubuntu). The distro argument is kept for API compatibility but
        /// is no longer passed to usbipd.
        /// </summary>
        public static async Task<RunResult> AttachAsync(string busId, string distro, Action<string> onLine,
            CancellationToken cancel = default)
        {
            await ProcessRunner.RunAsync(Exe(), new[] { "bind", "--busid", busId, "--force" }, onLine, null, 30000, cancel)
                .ConfigureAwait(false);

            var r = await ProcessRunner.RunAsync(Exe(),
                new[] { "attach", "--busid", busId, "--wsl" }, onLine, null, 60000, cancel).ConfigureAwait(false);

            if (!r.Ok && r.OutputContains("already attached")) r.ExitCode = 0;
            return r;
        }

        public static async Task<RunResult> DetachAsync(string busId, Action<string> onLine)
        {
            return await ProcessRunner.RunAsync(Exe(), new[] { "detach", "--busid", busId }, onLine, null, 30000)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps the device attached across the re-enumeration that firmware flashing causes.
        /// Runs detached in the background; the caller stops it when done.
        /// </summary>
        public static Process StartAutoAttach(string busId, string distro)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Exe(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("attach");
                psi.ArgumentList.Add("--busid");
                psi.ArgumentList.Add(busId);
                psi.ArgumentList.Add("--wsl");
                // No "--distribution": removed in usbipd 4.0+. The device is shared with the whole
                // WSL2 VM, so pm3-ubuntu sees it regardless.
                psi.ArgumentList.Add("--auto-attach");

                var p = Process.Start(psi);
                return p;
            }
            catch { return null; }
        }

        public static void StopAutoAttach(Process p)
        {
            try { if (p != null && !p.HasExited) p.Kill(true); } catch { }
        }
    }
}
