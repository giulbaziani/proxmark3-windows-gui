using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Proxmark3Gui.Core
{
    /// <summary>Result of one external process run.</summary>
    public class RunResult
    {
        public string Output = "";
        public int ExitCode = -1;
        public bool TimedOut;
        public bool Started = true;
        public bool Ok { get { return Started && !TimedOut && ExitCode == 0; } }

        public bool OutputContains(string needle)
        {
            return Output.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Runs external programs (wsl.exe, usbipd.exe, winget.exe, ...) and streams their
    /// output back line by line so the UI log can update while a long build runs.
    /// </summary>
    public static class ProcessRunner
    {
        public const int NoTimeout = -1;

        public static async Task<RunResult> RunAsync(
            string fileName,
            IEnumerable<string> args,
            Action<string> onLine = null,
            string stdin = null,
            int timeoutMs = 120000,
            CancellationToken cancel = default,
            IDictionary<string, string> env = null)
        {
            var res = new RunResult();
            var sb = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (args != null)
                foreach (var a in args) psi.ArgumentList.Add(a);
            if (env != null)
                foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

            using (var p = new Process())
            {
                p.StartInfo = psi;
                p.EnableRaisingEvents = true;

                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                p.Exited += (s, e) => done.TrySetResult(true);

                DataReceivedEventHandler sink = (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (sb) sb.AppendLine(e.Data);
                    if (onLine != null)
                    {
                        try { onLine(e.Data); } catch { /* UI marshalling errors must not kill the run */ }
                    }
                };
                p.OutputDataReceived += sink;
                p.ErrorDataReceived += sink;

                try
                {
                    p.Start();
                }
                catch (Exception ex)
                {
                    res.Started = false;
                    res.Output = "Could not start '" + fileName + "': " + ex.Message;
                    if (onLine != null) onLine(res.Output);
                    return res;
                }

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (stdin != null)
                {
                    try
                    {
                        await p.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
                        await p.StandardInput.FlushAsync().ConfigureAwait(false);
                        p.StandardInput.Close();
                    }
                    catch { /* the process may already have exited */ }
                }

                var waits = new List<Task> { done.Task };
                if (timeoutMs > 0) waits.Add(Task.Delay(timeoutMs, cancel));
                else if (cancel.CanBeCanceled) waits.Add(Task.Delay(Timeout.Infinite, cancel));

                Task finished;
                try { finished = await Task.WhenAny(waits).ConfigureAwait(false); }
                catch (OperationCanceledException) { finished = null; }

                if (finished != done.Task)
                {
                    res.TimedOut = !cancel.IsCancellationRequested;
                    TryKill(p);
                    lock (sb)
                    {
                        sb.AppendLine(res.TimedOut
                            ? "[!] Timed out after " + (timeoutMs / 1000) + "s."
                            : "[!] Cancelled.");
                        res.Output = sb.ToString();
                    }
                    return res;
                }

                // Let the async readers drain before reading ExitCode.
                p.WaitForExit();
                res.ExitCode = p.ExitCode;
                lock (sb) res.Output = sb.ToString();
            }

            return res;
        }

        private static void TryKill(Process p)
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        }

        /// <summary>True when the executable can be found on PATH.</summary>
        public static bool ExistsOnPath(string exe)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        string full = System.IO.Path.Combine(dir.Trim(), exe);
                        if (System.IO.File.Exists(full)) return true;
                        if (!exe.Contains(".") && System.IO.File.Exists(full + ".exe")) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }
    }
}
