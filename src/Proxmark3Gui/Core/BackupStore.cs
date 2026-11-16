using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proxmark3Gui.Core
{
    /// <summary>One saved card backup, described in the index.</summary>
    public class BackupEntry
    {
        /// <summary>Unique id, also the filename prefix (e.g. 20261116-145230-123).</summary>
        public string Id { get; set; }

        /// <summary>When the backup was taken (UTC). Shown in local time in the UI.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Stored primary file name (the .bin), relative to the backup folder.</summary>
        public string FileName { get; set; }

        /// <summary>The dump's original name as pm3 produced it.</summary>
        public string OriginalName { get; set; }

        /// <summary>Other stored files that belong to this backup (e.g. the .json).</summary>
        public List<string> ExtraFiles { get; set; } = new List<string>();

        public string Uid { get; set; }
        public string CardType { get; set; }
        public string Note { get; set; }
        public long SizeBytes { get; set; }

        [JsonIgnore]
        public DateTime CreatedLocal { get { return CreatedUtc.ToLocalTime(); } }

        [JsonIgnore]
        public string Label
        {
            get
            {
                string who = !string.IsNullOrWhiteSpace(Uid) ? Uid : (OriginalName ?? FileName);
                return CreatedLocal.ToString("yyyy-MM-dd HH:mm") + "  ·  " + who;
            }
        }
    }

    /// <summary>
    /// Keeps a local, timestamped copy of every card backup under
    /// %APPDATA%\Proxmark3Gui\backups, with an index so the app can list them with dates.
    /// This is independent of the optional "export to Documents" copy.
    /// </summary>
    public static class BackupStore
    {
        public static string Folder
        {
            get { return Path.Combine(AppSettings.Folder, "backups"); }
        }

        private static string IndexPath { get { return Path.Combine(Folder, "index.json"); } }

        public static List<BackupEntry> List()
        {
            var list = LoadIndex();
            // Newest first, and drop entries whose primary file has gone missing.
            return list
                .Where(e => File.Exists(PrimaryPath(e)))
                .OrderByDescending(e => e.CreatedUtc)
                .ToList();
        }

        public static string PrimaryPath(BackupEntry e)
        {
            return Path.Combine(Folder, e.FileName);
        }

        public static IEnumerable<string> AllPaths(BackupEntry e)
        {
            yield return PrimaryPath(e);
            foreach (var x in e.ExtraFiles) yield return Path.Combine(Folder, x);
        }

        /// <summary>
        /// Copies the given Windows files into the store under a timestamped name and records the
        /// metadata. The first path is treated as the primary (restorable) file.
        /// </summary>
        public static BackupEntry Add(IList<string> windowsPaths, string uid, string cardType, string note)
        {
            if (windowsPaths == null || windowsPaths.Count == 0)
                throw new ArgumentException("No files to back up.");

            Directory.CreateDirectory(Folder);

            var now = DateTime.Now;
            string id = now.ToString("yyyyMMdd-HHmmss") + "-" + (now.Millisecond % 1000).ToString("D3");

            var entry = new BackupEntry
            {
                Id = id,
                CreatedUtc = now.ToUniversalTime(),
                Uid = uid,
                CardType = cardType,
                Note = note
            };

            for (int i = 0; i < windowsPaths.Count; i++)
            {
                string src = windowsPaths[i];
                if (!File.Exists(src)) continue;

                string original = Path.GetFileName(src);
                string stored = id + "_" + original;
                string dest = Path.Combine(Folder, stored);
                File.Copy(src, dest, overwrite: true);

                if (i == 0)
                {
                    entry.FileName = stored;
                    entry.OriginalName = original;
                    entry.SizeBytes = new FileInfo(dest).Length;
                }
                else
                {
                    entry.ExtraFiles.Add(stored);
                }
            }

            if (entry.FileName == null)
                throw new IOException("None of the backup files could be copied.");

            var index = LoadIndex();
            index.Add(entry);
            SaveIndex(index);
            return entry;
        }

        public static void Delete(BackupEntry e)
        {
            foreach (var p in AllPaths(e))
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
            var index = LoadIndex();
            index.RemoveAll(x => x.Id == e.Id);
            SaveIndex(index);
        }

        /// <summary>Copies a backup's primary file out to a folder the user chose.</summary>
        public static string ExportCopy(BackupEntry e, string destFolder)
        {
            Directory.CreateDirectory(destFolder);
            string dest = Path.Combine(destFolder, e.OriginalName ?? e.FileName);
            File.Copy(PrimaryPath(e), dest, overwrite: true);
            return dest;
        }

        // ------------------------------------------------------------------ index io

        private static List<BackupEntry> LoadIndex()
        {
            try
            {
                if (File.Exists(IndexPath))
                {
                    var list = JsonSerializer.Deserialize<List<BackupEntry>>(File.ReadAllText(IndexPath));
                    if (list != null) return list;
                }
            }
            catch { /* a corrupt index must not lose access to the files on disk */ }
            return new List<BackupEntry>();
        }

        private static void SaveIndex(List<BackupEntry> list)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(IndexPath,
                    JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
