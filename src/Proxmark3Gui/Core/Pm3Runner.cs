using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Proxmark3Gui.Core
{
    /// <summary>Runs pm3 client commands inside the managed Linux environment.</summary>
    public class Pm3Runner
    {
        private readonly AppSettings _s;

        public Pm3Runner(AppSettings s) { _s = s; }

        /// <summary>Runs one pm3 command: ./pm3 -c "command".</summary>
        public async Task<RunResult> RunAsync(string pm3Command, Action<string> onLine = null,
            int timeoutMs = 120000, CancellationToken ct = default)
        {
            string script = "cd " + WslManager.ShellQuote(_s.Pm3Dir) +
                            " && ./pm3 -c " + WslManager.ShellQuote(pm3Command) + " 2>&1";
            return await WslManager.RunInDistroAsync(_s, script, onLine, timeoutMs, ct).ConfigureAwait(false);
        }

        /// <summary>Runs several pm3 commands in one client session (one connection, faster).</summary>
        public async Task<RunResult> RunManyAsync(IEnumerable<string> commands, Action<string> onLine = null,
            int timeoutMs = 300000, CancellationToken ct = default)
        {
            var parts = new List<string>();
            foreach (var c in commands) parts.Add("-c " + WslManager.ShellQuote(c));
            string script = "cd " + WslManager.ShellQuote(_s.Pm3Dir) +
                            " && ./pm3 " + string.Join(" ", parts) + " 2>&1";
            return await WslManager.RunInDistroAsync(_s, script, onLine, timeoutMs, ct).ConfigureAwait(false);
        }

        /// <summary>True when the device's serial port is visible inside Linux.</summary>
        public async Task<string> FindSerialPortAsync(CancellationToken ct = default)
        {
            var r = await WslManager.RunInDistroAsync(_s,
                "ls /dev/ttyACM* 2>/dev/null | head -1", null, 30000, ct).ConfigureAwait(false);
            string port = r.Output.Trim();
            return port.StartsWith("/dev/") ? port.Split('\n')[0].Trim() : null;
        }

        public async Task<RunResult> HwVersionAsync(Action<string> onLine = null, CancellationToken ct = default)
        {
            return await RunAsync("hw version", onLine, 90000, ct).ConfigureAwait(false);
        }

        public async Task<RunResult> HwTuneAsync(Action<string> onLine = null, CancellationToken ct = default)
        {
            return await RunAsync("hw tune", onLine, 120000, ct).ConfigureAwait(false);
        }

        /// <summary>Lists dump files the client has written, newest first.</summary>
        public async Task<List<string>> ListDumpsAsync(CancellationToken ct = default)
        {
            var list = new List<string>();
            var r = await WslManager.RunInDistroAsync(_s,
                "cd " + WslManager.ShellQuote(_s.Pm3Dir) +
                " && ls -1t *.bin *.json 2>/dev/null | head -40", null, 30000, ct).ConfigureAwait(false);
            foreach (var line in r.Output.Split('\n'))
            {
                string f = line.Trim();
                if (f.Length > 0 && !f.Contains(":")) list.Add(f);
            }
            return list;
        }

        /// <summary>Copies a dump out of Linux into the user's Windows dump folder.</summary>
        public async Task<string> ExportDumpAsync(string fileName, Action<string> onLine = null,
            CancellationToken ct = default)
        {
            var copied = await ExportDumpToAsync(fileName, _s.DumpFolder, onLine, ct).ConfigureAwait(false);
            return copied.Count > 0 ? copied[0] : null;
        }

        /// <summary>
        /// Copies a dump and its sibling files (the matching .json and key file, when present) from
        /// Linux into a Windows folder. Returns the copied Windows paths, the primary dump first.
        /// </summary>
        public async Task<System.Collections.Generic.List<string>> ExportDumpToAsync(
            string fileName, string destFolderWindows, Action<string> onLine = null,
            CancellationToken ct = default)
        {
            var result = new System.Collections.Generic.List<string>();
            try { System.IO.Directory.CreateDirectory(destFolderWindows); } catch { }

            string wslDest = await WslManager.ToWslPathAsync(_s, destFolderWindows).ConfigureAwait(false);
            if (string.IsNullOrEmpty(wslDest)) return result;

            // The primary dump, plus any sibling with the same stem (e.g. the .json/.eml variant).
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string script =
                "cd " + WslManager.ShellQuote(_s.Pm3Dir) + " && " +
                "cp -f " + WslManager.ShellQuote(fileName) + " " + WslManager.ShellQuote(wslDest) + "/ && " +
                "for ext in json eml bin; do " +
                "  f=" + WslManager.ShellQuote(stem) + ".$ext; " +
                "  [ -f \"$f\" ] && cp -f \"$f\" " + WslManager.ShellQuote(wslDest) + "/ ; " +
                "done; echo COPY_OK";

            var r = await WslManager.RunInDistroAsync(_s, script, onLine, 120000, ct).ConfigureAwait(false);
            if (!r.OutputContains("COPY_OK")) return result;

            // Report the primary first, then any siblings that actually landed.
            string primary = System.IO.Path.Combine(destFolderWindows, fileName);
            if (System.IO.File.Exists(primary)) result.Add(primary);

            foreach (var ext in new[] { "json", "eml" })
            {
                string sib = System.IO.Path.Combine(destFolderWindows, stem + "." + ext);
                if (System.IO.File.Exists(sib) &&
                    !sib.Equals(primary, StringComparison.OrdinalIgnoreCase))
                    result.Add(sib);
            }
            return result;
        }

        /// <summary>Copies a Windows file into the Linux working directory so pm3 can read it.</summary>
        public async Task<string> ImportFileAsync(string windowsPath, Action<string> onLine = null,
            CancellationToken ct = default)
        {
            string src = await WslManager.ToWslPathAsync(_s, windowsPath).ConfigureAwait(false);
            if (string.IsNullOrEmpty(src)) return null;

            string name = System.IO.Path.GetFileName(windowsPath);
            string script = "cp -f " + WslManager.ShellQuote(src) + " " +
                            WslManager.ShellQuote(_s.Pm3Dir + "/" + name) + " && echo COPY_OK";
            var r = await WslManager.RunInDistroAsync(_s, script, onLine, 120000, ct).ConfigureAwait(false);
            return r.OutputContains("COPY_OK") ? name : null;
        }
    }
}
