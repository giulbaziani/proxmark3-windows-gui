using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Proxmark3Gui.Core
{
    /// <summary>
    /// Everything the app needs from WSL: detecting it, installing the Windows feature,
    /// importing its own Ubuntu distro, and running shell commands inside it.
    /// </summary>
    public static class WslManager
    {
        // wsl.exe writes UTF-16 by default; WSL_UTF8=1 makes its own messages UTF-8 so the
        // output we capture is readable.
        private static readonly Dictionary<string, string> Utf8Env =
            new Dictionary<string, string> { { "WSL_UTF8", "1" } };

        public static bool WslExeExists()
        {
            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return File.Exists(Path.Combine(sys, "wsl.exe"));
            }
            catch { return false; }
        }

        public static async Task<RunResult> RunWslAsync(IEnumerable<string> args, Action<string> onLine = null,
            int timeoutMs = 120000, CancellationToken cancel = default)
        {
            return await ProcessRunner.RunAsync("wsl.exe", args, onLine, null, timeoutMs, cancel, Utf8Env)
                .ConfigureAwait(false);
        }

        /// <summary>Distro names currently registered with WSL.</summary>
        public static async Task<List<string>> ListDistrosAsync()
        {
            var list = new List<string>();
            if (!WslExeExists()) return list;

            var r = await RunWslAsync(new[] { "--list", "--quiet" }, null, 30000).ConfigureAwait(false);
            foreach (var raw in r.Output.Split('\n'))
            {
                // Strip NULs in case an old build still answers in UTF-16.
                string line = raw.Replace("\0", "").Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("Windows Subsystem", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.IndexOf("docker-desktop-data", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                list.Add(line);
            }
            return list;
        }

        public static async Task<bool> DistroExistsAsync(string name)
        {
            var all = await ListDistrosAsync().ConfigureAwait(false);
            return all.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when WSL itself is usable (feature enabled, kernel present).</summary>
        public static async Task<bool> IsWslReadyAsync()
        {
            if (!WslExeExists()) return false;
            var r = await RunWslAsync(new[] { "--status" }, null, 30000).ConfigureAwait(false);
            // A broken/absent install answers with a help screen or a non-zero exit code.
            return r.ExitCode == 0 && !r.OutputContains("is not installed");
        }

        /// <summary>
        /// Installs the WSL2 Windows components without a distribution. On Windows 10 this
        /// may require a reboot; the caller reports that to the user.
        /// </summary>
        public static async Task<RunResult> InstallWslAsync(Action<string> onLine, CancellationToken cancel)
        {
            var r = await RunWslAsync(new[] { "--install", "--no-distribution" }, onLine, 15 * 60 * 1000, cancel)
                .ConfigureAwait(false);
            if (!r.Ok)
            {
                // Older builds do not know --no-distribution; fall back to enabling the features directly.
                onLine?.Invoke("[i] Falling back to DISM feature activation...");
                await ProcessRunner.RunAsync("dism.exe",
                    new[] { "/online", "/enable-feature", "/featurename:Microsoft-Windows-Subsystem-Linux", "/all", "/norestart" },
                    onLine, null, 10 * 60 * 1000, cancel).ConfigureAwait(false);
                r = await ProcessRunner.RunAsync("dism.exe",
                    new[] { "/online", "/enable-feature", "/featurename:VirtualMachinePlatform", "/all", "/norestart" },
                    onLine, null, 10 * 60 * 1000, cancel).ConfigureAwait(false);
            }
            return r;
        }

        public static async Task<RunResult> UpdateWslAsync(Action<string> onLine, CancellationToken cancel)
        {
            return await RunWslAsync(new[] { "--update" }, onLine, 15 * 60 * 1000, cancel).ConfigureAwait(false);
        }

        public static bool RebootLikelyRequired(RunResult r)
        {
            if (r == null) return false;
            return r.OutputContains("restart") || r.OutputContains("reboot") || r.ExitCode == 3010;
        }

        /// <summary>
        /// Imports an Ubuntu root filesystem as a dedicated distro. This avoids the Microsoft
        /// Store, avoids the interactive "create a UNIX user" prompt, and leaves any distro the
        /// user already has untouched. The imported distro runs as root, so no sudo password
        /// is ever needed.
        /// </summary>
        public static async Task<RunResult> ImportDistroAsync(string name, string tarPath, string installDir,
            Action<string> onLine, CancellationToken cancel)
        {
            Directory.CreateDirectory(installDir);
            onLine?.Invoke("[i] Importing " + name + " into " + installDir + " (this takes a minute)...");
            return await RunWslAsync(new[] { "--import", name, installDir, tarPath, "--version", "2" },
                onLine, 20 * 60 * 1000, cancel).ConfigureAwait(false);
        }

        public static async Task<RunResult> UnregisterDistroAsync(string name, Action<string> onLine)
        {
            return await RunWslAsync(new[] { "--unregister", name }, onLine, 5 * 60 * 1000).ConfigureAwait(false);
        }

        public static async Task ShutdownAsync()
        {
            await RunWslAsync(new[] { "--shutdown" }, null, 60000).ConfigureAwait(false);
        }

        /// <summary>Runs a bash script inside the distro and streams its output.</summary>
        public static async Task<RunResult> RunInDistroAsync(AppSettings s, string script,
            Action<string> onLine = null, int timeoutMs = 10 * 60 * 1000, CancellationToken cancel = default)
        {
            var args = new List<string> { "-d", s.Distro };
            if (!string.IsNullOrWhiteSpace(s.LinuxUser)) { args.Add("-u"); args.Add(s.LinuxUser); }
            args.Add("--");
            args.Add("bash");
            args.Add("-lc");
            args.Add(script);

            return await ProcessRunner.RunAsync("wsl.exe", args, onLine, null, timeoutMs, cancel, Utf8Env)
                .ConfigureAwait(false);
        }

        /// <summary>Converts a Windows path to its /mnt/... form inside WSL.</summary>
        public static async Task<string> ToWslPathAsync(AppSettings s, string windowsPath)
        {
            var r = await RunInDistroAsync(s, "wslpath -a " + ShellQuote(windowsPath), null, 30000)
                .ConfigureAwait(false);
            string p = r.Output.Trim();
            return r.Ok && p.Length > 0 ? p.Split('\n')[0].Trim() : null;
        }

        /// <summary>Single-quotes a string for bash.</summary>
        public static string ShellQuote(string s)
        {
            if (s == null) return "''";
            return "'" + s.Replace("'", "'\\''") + "'";
        }
    }
}
