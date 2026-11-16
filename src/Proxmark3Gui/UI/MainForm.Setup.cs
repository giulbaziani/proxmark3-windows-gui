using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Proxmark3Gui.Core;

namespace Proxmark3Gui.UI
{
    public partial class MainForm
    {
        private ListView _steps;
        private Button _btnInstallAll;
        private Button _btnStop;
        private Label _setupSummary;

        private TabPage BuildSetupTab()
        {
            var tab = new TabPage("1 · Setup") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("One-click setup", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "Everything the Proxmark3 needs is installed for you: WSL2, a private Ubuntu environment, " +
                       "the USB bridge, the build tools, the source code and the firmware. No terminal, no commands.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 36
            });

            _steps = new ListView
            {
                Location = new Point(16, 86),
                Size = new Size(940, 212),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _steps.Columns.Add("", 34);
            _steps.Columns.Add("Step", 210);
            _steps.Columns.Add("What it does", 560);
            _steps.Columns.Add("Status", 120);

            foreach (var st in Setup.Steps)
            {
                var row = new ListViewItem(new[] { "", st.Title, st.Description, "not checked" }) { Tag = st.Id };
                _steps.Items.Add(row);
            }
            tab.Controls.Add(_steps);

            _setupSummary = new Label
            {
                Text = "",
                Location = new Point(16, 306),
                Width = 940,
                Height = 20,
                ForeColor = Muted
            };
            tab.Controls.Add(_setupSummary);

            _btnInstallAll = Btn("Install everything", 16, 334, 200, InstallEverythingAsync, primary: true);
            tab.Controls.Add(_btnInstallAll);

            tab.Controls.Add(Btn("Re-check", 226, 334, 120, async () =>
                await WithBusyAsync("Checking", async ct => await RefreshSetupStatusAsync(ct).ConfigureAwait(true))));

            tab.Controls.Add(Btn("Flash firmware", 356, 334, 150, FlashFirmwareAsync));

            tab.Controls.Add(Btn("Rebuild from source", 516, 334, 170, RebuildAsync));

            _btnStop = Btn("Stop", 696, 334, 90, async () => { CancelCurrent(); await Task.CompletedTask; });
            _btnStop.Tag = "always-on";
            tab.Controls.Add(_btnStop);

            tab.Controls.Add(new Label
            {
                Text = "Notes\n" +
                       "•  The first run downloads about 300 MB and compiles for 5–20 minutes. Leave it running.\n" +
                       "•  If Windows asks to restart, restart and start the app again — setup continues where it stopped.\n" +
                       "•  Flashing the firmware is only needed once, and only for a brand-new or updated device.",
                ForeColor = Muted,
                Location = new Point(16, 380),
                Width = 940,
                Height = 90
            });

            return tab;
        }

        // ------------------------------------------------------------------ status

        internal async Task RefreshSetupStatusAsync(CancellationToken ct = default)
        {
            int done = 0, total = 0;

            foreach (var st in Setup.Steps)
            {
                var row = FindRow(st.Id);
                if (row == null) continue;

                if (st.Manual)
                {
                    SetRow(row, "", "on demand", Muted);
                    continue;
                }

                total++;
                SetRow(row, "", "checking...", Muted);

                bool ok;
                try { ok = await st.Check(ct).ConfigureAwait(true); }
                catch { ok = false; }

                if (ok)
                {
                    done++;
                    S.MarkStepDone(st.Id);
                    SetRow(row, "✓", "ready", Good);
                }
                else
                {
                    SetRow(row, "•", "missing", Bad);
                }
            }

            string summary = done + " of " + total + " setup steps complete.";
            Ui(() => _setupSummary.Text = summary);

            if (done == total)
            {
                SetStatus("Setup complete — go to the Device tab.", Good);
                Log("[+] All setup steps are satisfied.");
            }
            else
            {
                SetStatus(summary + " Press \"Install everything\".", Bad);
            }
        }

        private ListViewItem FindRow(string id)
        {
            foreach (ListViewItem it in _steps.Items)
                if ((it.Tag as string) == id) return it;
            return null;
        }

        private void SetRow(ListViewItem row, string mark, string status, Color color)
        {
            Ui(() =>
            {
                row.SubItems[0].Text = mark;
                row.SubItems[3].Text = status;
                row.ForeColor = color;
            });
        }

        // ------------------------------------------------------------------ actions

        private async Task InstallEverythingAsync()
        {
            if (!Elevation.IsAdministrator())
            {
                MessageBox.Show(
                    "Setup needs administrator rights. Close the app and start it again with " +
                    "\"Run as administrator\".",
                    "Administrator required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Confirm(
                    "This installs WSL2, a private Ubuntu environment, usbipd-win, the build tools and the " +
                    "Proxmark3 client.\n\n" +
                    "It downloads roughly 300 MB and can take 20–40 minutes on the first run.\n\n" +
                    "Start now?", "Install everything"))
                return;

            await WithBusyAsync("Installing", async ct =>
            {
                bool ok = await Setup.RunAllAsync(
                    (step, phase) =>
                    {
                        var row = FindRow(step.Id);
                        if (row != null) SetRow(row, "»", phase, Accent);
                        SetStatus(step.Title + " — " + phase, Accent);
                    },
                    (step, res) =>
                    {
                        var row = FindRow(step.Id);
                        if (row != null)
                            SetRow(row, res.Ok ? "✓" : "✗", res.Ok ? "ready" : "failed", res.Ok ? Good : Bad);

                        if (!string.IsNullOrEmpty(res.Message))
                            Log((res.Ok ? "[+] " : "[!] ") + step.Title + ": " + res.Message);

                        if (res.NeedsReboot)
                        {
                            Log("[!] A Windows restart is required.");
                            MessageBox.Show(res.Message, "Restart needed",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    },
                    Log, ct).ConfigureAwait(true);

                if (ok)
                {
                    Log("[+] Setup finished. The device can now be used from the Device tab.");
                    SetStatus("Setup complete.", Good);

                    if (Ask("Setup finished.\n\nFlash the firmware to the device now? " +
                            "Do this for a new device, or after updating the source.", "Flash firmware?"))
                        await FlashFirmwareAsync().ConfigureAwait(true);
                    else
                        SelectTab(1);
                }
                else
                {
                    SetStatus("Setup stopped. See the log.", Bad);
                }
            }).ConfigureAwait(true);
        }

        private async Task FlashFirmwareAsync()
        {
            if (!Confirm(
                    "Firmware flashing writes new software to the Proxmark3.\n\n" +
                    "• The device must be plugged in and attached (Device tab).\n" +
                    "• Do not unplug it while flashing.\n\n" +
                    "Continue?", "Flash firmware"))
                return;

            await WithBusyAsync("Flashing firmware", async ct =>
            {
                // Keep the device attached across the re-enumeration the flash causes.
                System.Diagnostics.Process auto = null;
                if (!string.IsNullOrWhiteSpace(S.BusId))
                {
                    Log("[i] Holding the USB device on " + S.Distro + " during the flash...");
                    auto = UsbIpdManager.StartAutoAttach(S.BusId, S.Distro);
                    await Task.Delay(2500, ct).ConfigureAwait(true);
                }

                try
                {
                    var step = Setup.Steps.Find(s => s.Id == "flash");
                    var res = await step.Run(Log, ct).ConfigureAwait(true);
                    Log((res.Ok ? "[+] " : "[!] ") + res.Message);
                    SetStatus(res.Ok ? "Firmware written." : "Flashing failed.", res.Ok ? Good : Bad);

                    if (res.Ok)
                    {
                        var row = FindRow("flash");
                        if (row != null) SetRow(row, "✓", "flashed", Good);
                    }
                }
                finally
                {
                    UsbIpdManager.StopAutoAttach(auto);
                }
            }).ConfigureAwait(true);
        }

        private async Task RebuildAsync()
        {
            if (!Confirm("Pull the latest source and rebuild the client and firmware?\n\n" +
                         "This takes several minutes.", "Rebuild"))
                return;

            await WithBusyAsync("Rebuilding", async ct =>
            {
                var source = Setup.Steps.Find(s => s.Id == "source");
                var build = Setup.Steps.Find(s => s.Id == "build");

                Log("===== Source =====");
                var r1 = await source.Run(Log, ct).ConfigureAwait(true);
                Log((r1.Ok ? "[+] " : "[!] ") + r1.Message);
                if (!r1.Ok) { SetStatus("Could not update the source.", Bad); return; }

                Log("===== Build =====");
                var r2 = await build.Run(Log, ct).ConfigureAwait(true);
                Log((r2.Ok ? "[+] " : "[!] ") + r2.Message);
                SetStatus(r2.Ok ? "Rebuilt." : "Build failed.", r2.Ok ? Good : Bad);
            }).ConfigureAwait(true);
        }
    }
}
