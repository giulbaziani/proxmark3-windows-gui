using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Proxmark3Gui.Core
{
    /// <summary>
    /// Lightweight file logging for diagnosis. Mirrors the in-app log to session.log and records
    /// full exception stack traces (not just messages) to crash.log, so a problem a user reports
    /// can be pinpointed from their files without reproducing it here.
    /// </summary>
    public static class Diag
    {
        private static readonly object Gate = new object();

        public static string SessionLogPath { get { return Path.Combine(AppSettings.Folder, "session.log"); } }
        public static string CrashLogPath { get { return Path.Combine(AppSettings.Folder, "crash.log"); } }

        public static void Init()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                lock (Gate)
                {
                    // Keep the diagnostic log from growing without bound across many sessions.
                    try
                    {
                        if (File.Exists(SessionLogPath) && new FileInfo(SessionLogPath).Length > 1024 * 1024)
                            File.Delete(SessionLogPath);
                    }
                    catch { }

                    File.AppendAllText(SessionLogPath,
                        Environment.NewLine + "===== session started " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====" + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Line(string text)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(SessionLogPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + Environment.NewLine);
            }
            catch { }
        }

        public static void Crash(string where, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("===== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + where + "] =====");
                sb.AppendLine("Thread: managed id " + Thread.CurrentThread.ManagedThreadId +
                              (Thread.CurrentThread.IsThreadPoolThread ? " (thread-pool)" : " (foreground)"));
                sb.AppendLine(ex != null ? ex.ToString() : "(null exception)");
                lock (Gate)
                {
                    File.AppendAllText(CrashLogPath, sb.ToString());
                    File.AppendAllText(SessionLogPath, sb.ToString());
                }
            }
            catch { }
        }
    }
}
