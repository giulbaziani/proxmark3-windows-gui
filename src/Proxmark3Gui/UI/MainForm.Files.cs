using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Proxmark3Gui.Core;

namespace Proxmark3Gui.UI
{
    public partial class MainForm
    {
        private ListView _backupList;
        private TextBox _txtRestoreFile;

        private TabPage BuildFilesTab()
        {
            var tab = new TabPage("5 · Backups") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("Local card backups", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "Every backup is saved on this computer under your AppData folder and listed here with " +
                       "the date it was taken. You can restore any of them to a card, export a copy, or delete it.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 36
            });

            tab.Controls.Add(Btn("Back up the card on the antenna", 16, 82, 280, BackupAsync, primary: true));
            tab.Controls.Add(Btn("Refresh", 306, 82, 110, async () => { await RefreshBackupsAsync().ConfigureAwait(true); }));
            tab.Controls.Add(Btn("Open backups folder", 426, 82, 180, async () =>
            {
                OpenInExplorer(BackupStore.Folder);
                await Task.CompletedTask;
            }));

            _backupList = new ListView
            {
                Location = new Point(16, 124),
                Size = new Size(760, 240),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f)
            };
            _backupList.Columns.Add("Date taken", 150);
            _backupList.Columns.Add("UID", 130);
            _backupList.Columns.Add("Type", 150);
            _backupList.Columns.Add("Size", 70);
            _backupList.Columns.Add("File", 250);
            _backupList.DoubleClick += async (s, e) => await RestoreSelectedBackupAsync().ConfigureAwait(true);
            tab.Controls.Add(_backupList);

            var bRestore = Btn("Restore to card", 788, 124, 170, RestoreSelectedBackupAsync, primary: true);
            var bExport = Btn("Export a copy...", 788, 164, 170, ExportSelectedBackupAsync);
            var bView = Btn("View contents", 788, 204, 170, ViewSelectedBackupAsync);
            var bReveal = Btn("Reveal in Explorer", 788, 244, 170, async () =>
            {
                var e = SelectedBackup();
                if (e != null) RevealInExplorer(BackupStore.PrimaryPath(e));
                await Task.CompletedTask;
            });
            var bDelete = Btn("Delete", 788, 284, 170, DeleteSelectedBackupAsync);
            foreach (var rb in new[] { bRestore, bExport, bView, bReveal, bDelete })
            {
                rb.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                tab.Controls.Add(rb);
            }

            // Restore from any file on disk (e.g. a dump someone sent you).
            var lblFromDisk = Lbl("Write a dump file from disk", 16, 384, 220);
            lblFromDisk.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            tab.Controls.Add(lblFromDisk);

            _txtRestoreFile = Txt("", 16, 410, 600);
            _txtRestoreFile.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            tab.Controls.Add(_txtRestoreFile);

            var bBrowse = Btn("Browse...", 632, 408, 100, async () =>
            {
                using (var d = new OpenFileDialog
                {
                    Title = "Pick a dump file",
                    Filter = "Dump files (*.bin;*.json;*.eml)|*.bin;*.json;*.eml|All files (*.*)|*.*",
                    InitialDirectory = Directory.Exists(BackupStore.Folder) ? BackupStore.Folder : null
                })
                {
                    if (d.ShowDialog(this) == DialogResult.OK) _txtRestoreFile.Text = d.FileName;
                }
                await Task.CompletedTask;
            });
            bBrowse.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            tab.Controls.Add(bBrowse);

            var bWriteFile = Btn("Write this file", 742, 408, 160, RestoreFromWindowsAsync);
            bWriteFile.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            tab.Controls.Add(bWriteFile);

            return tab;
        }

        // ------------------------------------------------------------------ list

        internal async Task RefreshBackupsAsync()
        {
            // Reading the index touches the disk; do it off the UI thread, then fill the list on it.
            var items = await Task.Run(() => BackupStore.List()).ConfigureAwait(true);

            Ui(() =>
            {
                _backupList.BeginUpdate();
                _backupList.Items.Clear();
                foreach (var e in items)
                {
                    var row = new ListViewItem(new[]
                    {
                        e.CreatedLocal.ToString("yyyy-MM-dd HH:mm"),
                        string.IsNullOrWhiteSpace(e.Uid) ? "—" : e.Uid,
                        string.IsNullOrWhiteSpace(e.CardType) ? "—" : e.CardType,
                        FormatSize(e.SizeBytes),
                        e.OriginalName ?? e.FileName
                    })
                    { Tag = e };
                    _backupList.Items.Add(row);
                }
                _backupList.EndUpdate();
            });

            SetStatus(items.Count + " local backup(s).", items.Count > 0 ? Good : Muted);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
        }

        private BackupEntry SelectedBackup()
        {
            if (_backupList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Pick a backup from the list first.", "Nothing selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            return _backupList.SelectedItems[0].Tag as BackupEntry;
        }

        // ------------------------------------------------------------------ backup

        private async Task BackupAsync()
        {
            if (!Confirm("Place the card to back up on the HF antenna.\n\n" +
                         "The keys are recovered first, which can take a while.", "Back up a card"))
                return;

            await WithBusyAsync("Backing up", async ct =>
            {
                var r = await Pm3.RunAsync("hf mf autopwn", Log, 15 * 60 * 1000, ct).ConfigureAwait(true);

                if (Pm3Parse.PortMissing(r.Output))
                {
                    SetStatus("No device — connect it on the Device tab.", Bad);
                    return;
                }

                string dump = Pm3Parse.DumpFile(r.Output);
                if (dump == null)
                {
                    Log("[!] No dump was produced. Reposition the card and try again.");
                    SetStatus("Backup failed.", Bad);
                    return;
                }

                string uid = Pm3Parse.Uid(r.Output);
                string type = Pm3Parse.CardType(r.Output);
                Log("[+] Dump: " + dump + (uid != null ? "  (UID " + uid + ")" : ""));

                // Pull the dump (and its siblings) out of Linux, then store it locally with a timestamp.
                var copied = await Pm3.ExportDumpToAsync(dump, BackupStore.Folder, Log, ct).ConfigureAwait(true);
                if (copied.Count == 0)
                {
                    Log("[!] The dump could not be copied out of Linux.");
                    SetStatus("Backup failed to copy.", Bad);
                    return;
                }

                try
                {
                    var entry = BackupStore.Add(copied, uid, type, null);
                    // The staging copies in the backup folder were the source; remove them so only the
                    // timestamped stored files remain.
                    foreach (var p in copied)
                    {
                        try { if (File.Exists(p)) File.Delete(p); } catch { }
                    }

                    Log("[+] Saved backup from " + entry.CreatedLocal.ToString("yyyy-MM-dd HH:mm") +
                        " to " + BackupStore.Folder);
                    SetStatus("Backup saved (" + entry.CreatedLocal.ToString("yyyy-MM-dd HH:mm") + ").", Good);
                }
                catch (Exception ex)
                {
                    Log("[!] Could not store the backup: " + ex.Message);
                    SetStatus("Backup not stored.", Bad);
                }

                await RefreshBackupsAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        // ------------------------------------------------------------------ actions

        private async Task RestoreSelectedBackupAsync()
        {
            var e = SelectedBackup();
            if (e == null) return;

            await WithBusyAsync("Writing the backup to a card", async ct =>
            {
                string winPath = BackupStore.PrimaryPath(e);
                string name = await Pm3.ImportFileAsync(winPath, Log, ct).ConfigureAwait(true);
                if (name == null) { SetStatus("Could not copy the backup into Linux.", Bad); return; }
                await RestoreCoreAsync(name, ct).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async Task ExportSelectedBackupAsync()
        {
            var e = SelectedBackup();
            if (e == null) return;

            using (var d = new FolderBrowserDialog
            {
                Description = "Where should the copy go?",
                SelectedPath = Directory.Exists(S.DumpFolder) ? S.DumpFolder : null
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string dest = BackupStore.ExportCopy(e, d.SelectedPath);
                    Log("[+] Exported a copy to " + dest);
                    SetStatus("Copy exported.", Good);
                }
                catch (Exception ex)
                {
                    Log("[!] Export failed: " + ex.Message);
                    SetStatus("Export failed.", Bad);
                }
            }
            await Task.CompletedTask;
        }

        private async Task ViewSelectedBackupAsync()
        {
            var e = SelectedBackup();
            if (e == null) return;

            await WithBusyAsync("Reading the backup", async ct =>
            {
                string name = await Pm3.ImportFileAsync(BackupStore.PrimaryPath(e), Log, ct).ConfigureAwait(true);
                if (name == null) { SetStatus("Could not read the backup.", Bad); return; }
                await Pm3.RunAsync("hf mf view -f " + name, Log, 120000, ct).ConfigureAwait(true);
                SetStatus("Backup contents are in the log.", Muted);
            }).ConfigureAwait(true);
        }

        private async Task DeleteSelectedBackupAsync()
        {
            var e = SelectedBackup();
            if (e == null) return;

            if (!Confirm("Delete the backup from " + e.CreatedLocal.ToString("yyyy-MM-dd HH:mm") +
                         (e.Uid != null ? " (UID " + e.Uid + ")" : "") + "?\n\n" +
                         "This removes the local file. It cannot be undone.", "Delete backup"))
                return;

            try
            {
                BackupStore.Delete(e);
                Log("[i] Backup deleted.");
            }
            catch (Exception ex) { Log("[!] " + ex.Message); }

            await RefreshBackupsAsync().ConfigureAwait(true);
        }

        private async Task RestoreFromWindowsAsync()
        {
            string path = (_txtRestoreFile.Text ?? "").Trim();
            if (path.Length == 0 || !File.Exists(path))
            {
                MessageBox.Show("Pick an existing file first.", "No file", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            await WithBusyAsync("Copying the file into Linux", async ct =>
            {
                string name = await Pm3.ImportFileAsync(path, Log, ct).ConfigureAwait(true);
                if (name == null) { SetStatus("Could not copy the file.", Bad); return; }
                Log("[+] Copied in as " + name);
                await RestoreCoreAsync(name, ct).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async Task RestoreCoreAsync(string fileName, CancellationToken ct)
        {
            if (!Confirm("Write " + fileName + " onto the card on the antenna?\n\n" +
                         "Everything currently on that card is overwritten.", "Write the card"))
                return;

            var r = await Pm3.RunAsync("hf mf restore -f " + fileName + " --force", Log, 15 * 60 * 1000, ct)
                .ConfigureAwait(true);

            bool ok = r.OutputContains("( ok )") || (r.OutputContains("Restore") && !r.OutputContains("failed"));
            Log(ok ? "[+] Write finished — verify on the clone tab." : "[!] The write reported problems.");
            SetStatus(ok ? "Card written." : "Write failed — see the log.", ok ? Good : Bad);
        }

        // ------------------------------------------------------------------ explorer helpers

        private void OpenInExplorer(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log("[!] " + ex.Message); }
        }

        private void RevealInExplorer(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                else
                    OpenInExplorer(Path.GetDirectoryName(filePath));
            }
            catch (Exception ex) { Log("[!] " + ex.Message); }
        }
    }
}
