using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Proxmark3Gui.Core;

namespace Proxmark3Gui.UI
{
    public partial class MainForm
    {
        private TextBox _txtDistro;
        private TextBox _txtPm3Dir;
        private TextBox _txtRepo;
        private TextBox _txtDumpFolder;
        private CheckBox _chkEasy;

        private TabPage BuildSettingsTab()
        {
            var tab = new TabPage("7 · Settings") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("Settings", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "The defaults are right for a Proxmark3 Easy. Change these only if you know you need to.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 20
            });

            int y = 82;

            tab.Controls.Add(Lbl("Linux environment", 16, y, 170));
            _txtDistro = Txt(S.Distro, 196, y, 260);
            tab.Controls.Add(_txtDistro);
            tab.Controls.Add(new Label
            {
                Text = "WSL distribution the app manages",
                ForeColor = Muted, Location = new Point(470, y + 4), Width = 400
            });

            y += 36;
            tab.Controls.Add(Lbl("Source folder", 16, y, 170));
            _txtPm3Dir = Txt(S.Pm3Dir, 196, y, 260);
            tab.Controls.Add(_txtPm3Dir);
            tab.Controls.Add(new Label
            {
                Text = "Path inside Linux where pm3 is built",
                ForeColor = Muted, Location = new Point(470, y + 4), Width = 400
            });

            y += 36;
            tab.Controls.Add(Lbl("Source repository", 16, y, 170));
            _txtRepo = Txt(S.SourceRepoUrl, 196, y, 500);
            tab.Controls.Add(_txtRepo);

            y += 28;
            tab.Controls.Add(new Label
            {
                Text = "Your own mirror of the Proxmark3 code. If it cannot be reached, the app falls back to " +
                       S.FallbackRepoUrl,
                ForeColor = Muted, Location = new Point(196, y), Width = 740, Height = 32
            });

            y += 40;
            tab.Controls.Add(Lbl("Dump folder", 16, y, 170));
            _txtDumpFolder = Txt(S.DumpFolder, 196, y, 500);
            tab.Controls.Add(_txtDumpFolder);
            tab.Controls.Add(Btn("Browse...", 706, y - 4, 100, async () =>
            {
                using (var d = new FolderBrowserDialog { SelectedPath = _txtDumpFolder.Text })
                {
                    if (d.ShowDialog(this) == DialogResult.OK) _txtDumpFolder.Text = d.SelectedPath;
                }
                await Task.CompletedTask;
            }));

            y += 40;
            _chkEasy = new CheckBox
            {
                Text = "Proxmark3 Easy — build the trimmed 256 KB firmware (leave this on for the common board)",
                Checked = S.Easy256K,
                Location = new Point(196, y),
                Width = 740
            };
            tab.Controls.Add(_chkEasy);

            y += 44;
            tab.Controls.Add(Btn("Save settings", 196, y, 150, SaveSettingsAsync, primary: true));
            tab.Controls.Add(Btn("Open settings file", 356, y, 160, async () =>
            {
                try
                {
                    S.Save();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = AppSettings.FilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { Log("[!] " + ex.Message); }
                await Task.CompletedTask;
            }));

            y += 56;
            tab.Controls.Add(new Label
            {
                Text = "Maintenance",
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(16, y)
            });

            y += 28;
            tab.Controls.Add(Btn("Forget setup progress", 16, y, 190, async () =>
            {
                S.ResetSteps();
                Log("[i] Setup progress cleared; every step will be checked again.");
                await RefreshSetupStatusAsync().ConfigureAwait(true);
            }));

            tab.Controls.Add(Btn("Delete the Linux environment", 216, y, 230, RemoveDistroAsync));

            tab.Controls.Add(Btn("Shut WSL down", 456, y, 150, async () =>
            {
                await WithBusyAsync("Shutting WSL down", async ct =>
                {
                    await WslManager.ShutdownAsync().ConfigureAwait(true);
                    Log("[i] WSL stopped. It restarts on the next command.");
                }).ConfigureAwait(true);
            }));

            y += 40;
            tab.Controls.Add(new Label
            {
                Text = "Deleting the environment removes the Ubuntu instance this app created, including the " +
                       "compiled client and any dumps still inside it. Dumps already saved to your Windows dump " +
                       "folder are untouched.",
                ForeColor = Muted,
                Location = new Point(16, y),
                Width = 920,
                Height = 44
            });

            return tab;
        }

        private async Task SaveSettingsAsync()
        {
            S.Distro = (_txtDistro.Text ?? "").Trim();
            S.Pm3Dir = (_txtPm3Dir.Text ?? "").Trim();
            S.SourceRepoUrl = (_txtRepo.Text ?? "").Trim();
            S.DumpFolder = (_txtDumpFolder.Text ?? "").Trim();
            S.Easy256K = _chkEasy.Checked;
            S.Save();

            Log("[+] Settings saved to " + AppSettings.FilePath);
            SetStatus("Settings saved.", Good);

            await RefreshSetupStatusAsync().ConfigureAwait(true);
        }

        private async Task RemoveDistroAsync()
        {
            if (!Confirm("Delete the '" + S.Distro + "' Linux environment?\n\n" +
                         "The compiled client and any dumps still inside it are lost. " +
                         "This cannot be undone.", "Delete the environment"))
                return;

            if (!Ask("Really delete it? Setup will have to download and compile everything again.",
                    "Are you sure?"))
                return;

            await WithBusyAsync("Deleting the environment", async ct =>
            {
                await WslManager.UnregisterDistroAsync(S.Distro, Log).ConfigureAwait(true);
                S.ResetSteps();
                Log("[i] Environment removed.");
                await RefreshSetupStatusAsync(ct).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
    }
}
