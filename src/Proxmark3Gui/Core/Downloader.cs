using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Proxmark3Gui.Core
{
    /// <summary>Streams a file to disk with progress, resuming nothing but reporting clearly.</summary>
    public static class Downloader
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            c.Timeout = Timeout.InfiniteTimeSpan; // progress reporting handles stalls
            c.DefaultRequestHeaders.UserAgent.ParseAdd("proxmark3-windows-gui");
            return c;
        }

        public static async Task<bool> DownloadAsync(string url, string destPath,
            Action<string> onLine, CancellationToken cancel)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                onLine?.Invoke("[i] Downloading " + url);

                using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel)
                           .ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        onLine?.Invoke("[!] HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                        return false;
                    }

                    long total = resp.Content.Headers.ContentLength ?? -1;
                    string tmp = destPath + ".part";

                    using (var src = await resp.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
                    using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                    {
                        var buf = new byte[1 << 16];
                        long got = 0;
                        int lastPct = -1;
                        DateTime lastReport = DateTime.MinValue;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, cancel).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, cancel).ConfigureAwait(false);
                            got += n;

                            if ((DateTime.UtcNow - lastReport).TotalMilliseconds < 700) continue;
                            lastReport = DateTime.UtcNow;

                            if (total > 0)
                            {
                                int pct = (int)(got * 100 / total);
                                if (pct != lastPct)
                                {
                                    lastPct = pct;
                                    onLine?.Invoke("    " + pct + "%  (" + Mb(got) + " / " + Mb(total) + " MB)");
                                }
                            }
                            else
                            {
                                onLine?.Invoke("    " + Mb(got) + " MB");
                            }
                        }
                    }

                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(tmp, destPath);
                    onLine?.Invoke("[+] Saved to " + destPath);
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                onLine?.Invoke("[!] Download cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                onLine?.Invoke("[!] Download failed: " + ex.Message);
                return false;
            }
        }

        private static string Mb(long bytes)
        {
            return (bytes / 1024.0 / 1024.0).ToString("F1");
        }
    }
}
