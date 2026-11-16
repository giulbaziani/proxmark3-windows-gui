using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Proxmark3Gui.Core;

namespace Proxmark3Gui.UI
{
    public partial class MainForm
    {
        // source card state, filled by step 1 of each flow
        private string _hfUid;
        private string _hfDump;
        private readonly List<SectorKey> _hfSpecialKeys = new List<SectorKey>();
        private Label _hfSource;

        private string _lfId;
        private Label _lfSource;

        // ------------------------------------------------------------------ HF 13.56

        private TabPage BuildHfTab()
        {
            var tab = new TabPage("3 · Clone 13.56 MHz") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("Mifare Classic → magic card", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "Step 1 reads the original and recovers its keys. Step 2 writes everything onto a magic " +
                       "(CUID / Gen1a) card, verifies the UID and restores any non-default sector keys automatically.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 36
            });

            tab.Controls.Add(Btn("1 · Read the original", 16, 90, 240, HfReadOriginalAsync, primary: true));
            tab.Controls.Add(Btn("2 · Write the clone", 272, 90, 240, HfWriteCloneAsync, primary: true));
            tab.Controls.Add(Btn("Verify the clone", 528, 90, 170, HfVerifyAsync));

            _hfSource = new Label
            {
                Text = "No original read yet.",
                ForeColor = Muted,
                Location = new Point(16, 136),
                Width = 940,
                Height = 40
            };
            tab.Controls.Add(_hfSource);

            tab.Controls.Add(new Label
            {
                Text = "How to place the cards\n" +
                       "•  HF antenna is the flat spiral drawn on the board.\n" +
                       "•  Only one card on the antenna at a time — two cards is the most common failure.\n" +
                       "•  Buy Gen2 / CUID (written with restore --force) or Gen1a. Avoid FUID: it can only be written once.\n" +
                       "•  If a real reader rejects the clone, the system checks a special key; the app restores those " +
                       "automatically in step 2, but check the log.",
                ForeColor = Muted,
                Location = new Point(16, 190),
                Width = 940,
                Height = 120
            });

            return tab;
        }

        private async Task HfReadOriginalAsync()
        {
            if (!Confirm("Place the ORIGINAL card on the HF antenna.", "Step 1 — read the original")) return;

            await WithBusyAsync("Reading the original", async ct =>
            {
                Log("===== Reading the original (hf mf autopwn) =====");
                var r = await Pm3.RunAsync("hf mf autopwn", Log, 15 * 60 * 1000, ct).ConfigureAwait(true);

                if (Pm3Parse.PortMissing(r.Output))
                {
                    SetStatus("No device — connect it on the Device tab.", Bad);
                    return;
                }

                _hfUid = Pm3Parse.Uid(r.Output);
                _hfDump = Pm3Parse.DumpFile(r.Output);

                _hfSpecialKeys.Clear();
                foreach (var k in Pm3Parse.SectorKeys(r.Output))
                    if (!k.IsDefault) _hfSpecialKeys.Add(k);

                if (_hfDump == null)
                {
                    Log("[!] No dump file was produced. Reposition the card and try again.");
                    SetStatus("Reading failed.", Bad);
                    _hfSource.Text = "No original read yet.";
                    return;
                }

                if (Pm3Parse.KeysIncomplete(r.Output))
                    Log("[!] Warning: some keys were not recovered, so the clone may be incomplete.");

                Log("[+] UID " + _hfUid + ", dump " + _hfDump + ", non-default sectors " + _hfSpecialKeys.Count);
                _hfSource.Text = "Original: UID " + _hfUid + "   ·   dump " + _hfDump +
                                 "   ·   " + _hfSpecialKeys.Count + " non-default sector key(s)";
                _hfSource.ForeColor = Good;
                SetStatus("Original read. Put the magic card on the antenna and run step 2.", Good);
            }).ConfigureAwait(true);
        }

        private async Task HfWriteCloneAsync()
        {
            if (_hfDump == null)
            {
                MessageBox.Show("Run step 1 first so there is a dump to write.", "Nothing to write",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Confirm("Place the MAGIC (CUID / Gen1a) card on the antenna.\n\n" +
                         "Everything on it will be overwritten.", "Step 2 — write the clone"))
                return;

            await WithBusyAsync("Writing the clone", async ct =>
            {
                Log("===== Checking the target card =====");
                var rs = await Pm3.RunAsync("hf mf info", Log, 120000, ct).ConfigureAwait(true);
                string magic = Pm3Parse.MagicCapabilities(rs.Output);

                if (magic == null)
                {
                    if (!Ask("This card does not report magic capabilities, so its UID probably cannot be " +
                             "changed.\n\nWrite the data anyway?", "Not a magic card"))
                    {
                        SetStatus("Stopped.", Muted);
                        return;
                    }
                }
                else
                {
                    Log("[+] Magic: " + magic);
                }

                // Recover the target's own keys so restore can authenticate on every sector.
                Log("===== Recovering the target's keys =====");
                var rk = await Pm3.RunAsync("hf mf autopwn", Log, 15 * 60 * 1000, ct).ConfigureAwait(true);
                string targetKeyFile = Pm3Parse.KeyFile(rk.Output);
                if (targetKeyFile != null) Log("[i] Using key file " + targetKeyFile + " for the target.");

                Log("===== Writing the dump =====");
                string cmd = "hf mf restore -f " + _hfDump +
                             (targetKeyFile != null ? " -k " + targetKeyFile : "") + " --force";
                var rr = await Pm3.RunAsync(cmd, Log, 15 * 60 * 1000, ct).ConfigureAwait(true);

                bool uidOk = await HfCheckUidAsync(ct).ConfigureAwait(true);

                int restored = await HfRestoreSpecialKeysAsync(ct).ConfigureAwait(true);

                if (uidOk)
                    SetStatus("Clone finished" + (restored > 0 ? " (" + restored + " sector key(s) restored)" : "") + ".", Good);
                else
                    SetStatus("Data written, but the UID did not change — the card is not magic.", Bad);
            }).ConfigureAwait(true);
        }

        private async Task<bool> HfCheckUidAsync(CancellationToken ct)
        {
            var rv = await Pm3.RunAsync("hf mf rdbl --blk 0 -k FFFFFFFFFFFF", Log, 120000, ct).ConfigureAwait(true);
            string wrote = Pm3Parse.BlockZeroUid(rv.Output);

            bool ok = _hfUid != null && wrote != null &&
                      wrote.StartsWith(_hfUid.Substring(0, Math.Min(8, _hfUid.Length)),
                          StringComparison.OrdinalIgnoreCase);

            Log(ok ? "[+] UID written: " + _hfUid
                   : "[!] UID mismatch (read back: " + (wrote ?? "nothing") + ").");
            return ok;
        }

        /// <summary>
        /// Re-writes sector trailers whose keys were not the default FFFFFFFFFFFF. Some readers
        /// check one specific sector key, and a plain restore can leave it at the default.
        /// </summary>
        private async Task<int> HfRestoreSpecialKeysAsync(CancellationToken ct)
        {
            if (_hfSpecialKeys.Count == 0) return 0;

            Log("===== Restoring " + _hfSpecialKeys.Count + " non-default sector key(s) =====");
            int fixedCount = 0;

            foreach (var k in _hfSpecialKeys)
            {
                if (ct.IsCancellationRequested) break;

                var chk = await Pm3.RunAsync("hf mf rdbl --blk " + k.FirstBlock + " -k " + k.KeyA,
                    null, 90000, ct).ConfigureAwait(true);

                bool keyWorks = chk.Output.IndexOf("auth error", StringComparison.OrdinalIgnoreCase) < 0
                                && Regex.IsMatch(chk.Output, @"\|\s*\d+\s*\|", RegexOptions.IgnoreCase);

                if (keyWorks)
                {
                    Log("[i] Sector " + k.Sector + " already has its original key.");
                    continue;
                }

                // Key A + standard access bits (FF078069) + key B
                string trailer = k.KeyA + "FF078069" + k.KeyB;
                var fix = await Pm3.RunAsync(
                    "hf mf wrbl --blk " + k.TrailerBlock + " -k FFFFFFFFFFFF -d " + trailer + " --force",
                    Log, 90000, ct).ConfigureAwait(true);

                bool ok = fix.Output.IndexOf("( ok )", StringComparison.OrdinalIgnoreCase) >= 0
                          || fix.Output.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0;

                Log((ok ? "[+] " : "[!] ") + "Sector " + k.Sector + " key restore: " + (ok ? "ok" : "see log"));
                if (ok) fixedCount++;
            }

            return fixedCount;
        }

        private async Task HfVerifyAsync()
        {
            await WithBusyAsync("Verifying", async ct =>
            {
                var r = await Pm3.RunAsync("hf mf info", Log, 120000, ct).ConfigureAwait(true);
                string uid = Pm3Parse.Uid(r.Output);
                Log("[i] Card on the antenna: UID " + (uid ?? "unknown"));

                if (_hfUid != null && uid != null)
                {
                    bool same = string.Equals(uid, _hfUid, StringComparison.OrdinalIgnoreCase);
                    Log(same ? "[+] Matches the original." : "[!] Different from the original (" + _hfUid + ").");
                    SetStatus(same ? "UID matches the original." : "UID does not match.", same ? Good : Bad);
                }
                else
                {
                    SetStatus(uid != null ? "Card read." : "No card found.", uid != null ? Good : Bad);
                }
            }).ConfigureAwait(true);
        }

        // ------------------------------------------------------------------ LF 125 kHz

        private TabPage BuildLfTab()
        {
            var tab = new TabPage("4 · Clone 125 kHz") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("EM4100 → T5577", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "Reads the ID of a 125 kHz tag and writes it onto a rewritable T5577 (or EM4305) card.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 20
            });

            tab.Controls.Add(Btn("1 · Read the original", 16, 80, 240, LfReadOriginalAsync, primary: true));
            tab.Controls.Add(Btn("2 · Write to T5577", 272, 80, 240, LfWriteCloneAsync, primary: true));
            tab.Controls.Add(Btn("Detect T5577", 528, 80, 170, LfDetectAsync));

            _lfSource = new Label
            {
                Text = "No original read yet.",
                ForeColor = Muted,
                Location = new Point(16, 126),
                Width = 940,
                Height = 24
            };
            tab.Controls.Add(_lfSource);

            tab.Controls.Add(Lbl("Write this ID", 16, 164, 100));
            _txtLfId = Txt("", 120, 160, 160);
            tab.Controls.Add(_txtLfId);
            tab.Controls.Add(new Label
            {
                Text = "10 hex digits — filled in by step 1, or type one yourself.",
                ForeColor = Muted,
                Location = new Point(292, 164),
                Width = 500
            });

            tab.Controls.Add(new Label
            {
                Text = "Notes\n" +
                       "•  LF antenna is the wire coil.\n" +
                       "•  Only T5577 / EM4305 cards are writable. A read-only EM4100 cannot be cloned onto.\n" +
                       "•  \"Detect T5577\" answering nothing usually means bad card placement, not a bad card.",
                ForeColor = Muted,
                Location = new Point(16, 206),
                Width = 940,
                Height = 90
            });

            return tab;
        }

        private TextBox _txtLfId;

        private async Task LfReadOriginalAsync()
        {
            if (!Confirm("Place the ORIGINAL 125 kHz tag on the LF antenna.", "Step 1 — read the original")) return;

            await WithBusyAsync("Reading the tag", async ct =>
            {
                var r = await Pm3.RunAsync("lf search", Log, 120000, ct).ConfigureAwait(true);

                if (Pm3Parse.PortMissing(r.Output))
                {
                    SetStatus("No device — connect it on the Device tab.", Bad);
                    return;
                }

                _lfId = Pm3Parse.Em410xId(r.Output);
                if (_lfId == null)
                {
                    Log("[!] No EM410x ID found. Is the tag on the LF coil?");
                    _lfSource.Text = "No original read yet.";
                    _lfSource.ForeColor = Muted;
                    SetStatus("Could not read the tag.", Bad);
                    return;
                }

                _txtLfId.Text = _lfId;
                Log("[+] EM410x ID: " + _lfId);
                _lfSource.Text = "Original ID: " + _lfId;
                _lfSource.ForeColor = Good;
                SetStatus("ID read. Put the T5577 card on the antenna and run step 2.", Good);
            }).ConfigureAwait(true);
        }

        private async Task LfWriteCloneAsync()
        {
            string id = (_txtLfId.Text ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(id, "^[0-9A-F]{10}$"))
            {
                MessageBox.Show("The ID must be exactly 10 hex digits. Run step 1, or type a valid ID.",
                    "Invalid ID", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Confirm("Place the writable T5577 card on the antenna — only one card.\n\n" +
                         "It will be overwritten with ID " + id + ".", "Step 2 — write"))
                return;

            await WithBusyAsync("Writing the tag", async ct =>
            {
                var w = await Pm3.RunAsync("lf em 410x clone --id " + id, Log, 120000, ct).ConfigureAwait(true);
                var v = await Pm3.RunAsync("lf search", Log, 120000, ct).ConfigureAwait(true);

                string now = Pm3Parse.Em410xId(v.Output);
                bool ok = string.Equals(now, id, StringComparison.OrdinalIgnoreCase);

                Log(ok ? "[+] Written: " + id
                       : "[!] The ID did not change (read back: " + (now ?? "nothing") + ").");
                SetStatus(ok
                    ? "Clone written: " + id
                    : "Not written — the card is probably a read-only EM4100, not a T5577.", ok ? Good : Bad);
            }).ConfigureAwait(true);
        }

        private async Task LfDetectAsync()
        {
            await WithBusyAsync("Detecting the chip", async ct =>
            {
                var r = await Pm3.RunAsync("lf t55xx detect", Log, 120000, ct).ConfigureAwait(true);
                bool ok = r.OutputContains("Chip Type") || r.OutputContains("Modulation");
                SetStatus(ok ? "T55xx detected — the card is writable."
                             : "No T55xx answer — reposition the card, or it is read-only.", ok ? Good : Bad);
            }).ConfigureAwait(true);
        }
    }
}
