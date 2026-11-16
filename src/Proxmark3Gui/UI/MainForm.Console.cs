using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Proxmark3Gui.UI
{
    public partial class MainForm
    {
        private ComboBox _cmdBox;

        /// <summary>The commands from the quick-reference table, so nobody has to remember them.</summary>
        private static readonly string[] CommonCommands =
        {
            "hw version",
            "hw tune",
            "hf search",
            "lf search",
            "hf mf info",
            "hf mf autopwn",
            "hf mf rdbl --blk 0 -k FFFFFFFFFFFF",
            "hf mf view -f FILE.bin",
            "hf mf restore -f FILE.bin --force",
            "hf mf cload -f FILE.bin",
            "lf t55xx detect",
            "lf em 410x clone --id 1234567890",
            "hf 14a info",
            "hf mfu info"
        };

        private TabPage BuildConsoleTab()
        {
            var tab = new TabPage("6 · Console") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("Run any pm3 command", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "For anything the tabs do not cover. The command runs as ./pm3 -c \"...\" and the answer " +
                       "appears in the log below.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 20
            });

            _cmdBox = new ComboBox
            {
                Location = new Point(16, 78),
                Width = 700,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 10f),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            _cmdBox.Items.AddRange(CommonCommands);
            _cmdBox.Text = "hw version";
            _cmdBox.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !IsBusy)
                {
                    e.SuppressKeyPress = true;
                    await RunConsoleCommandAsync().ConfigureAwait(true);
                }
            };
            tab.Controls.Add(_cmdBox);

            var btnRunCmd = Btn("Run", 728, 76, 110, RunConsoleCommandAsync, primary: true);
            btnRunCmd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            tab.Controls.Add(btnRunCmd);
            var btnStopCmd = Btn("Stop", 848, 76, 90, async () => { CancelCurrent(); await Task.CompletedTask; });
            btnStopCmd.Tag = "always-on";
            btnStopCmd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            tab.Controls.Add(btnStopCmd);

            tab.Controls.Add(new Label
            {
                Text = "Shell access\n" +
                       "You can also run a plain Linux command inside the managed environment — useful when a " +
                       "build or a permission problem needs a closer look.",
                ForeColor = Muted,
                Location = new Point(16, 124),
                Width = 940,
                Height = 44
            });

            _shellBox = new TextBox
            {
                Location = new Point(16, 174),
                Width = 700,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Consolas", 10f),
                Text = "uname -a"
            };
            tab.Controls.Add(_shellBox);
            var btnRunShell = Btn("Run in Linux", 728, 172, 210, RunShellCommandAsync);
            btnRunShell.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            tab.Controls.Add(btnRunShell);

            tab.Controls.Add(new Label
            {
                Text = "Reference\n" +
                       "hf search — identify a 13.56 MHz card          lf search — identify a 125 kHz tag\n" +
                       "hf mf info — Mifare details and magic type      hf mf autopwn — recover keys and dump\n" +
                       "hf mf rdbl --blk N -k KEY — read one block      hf mf wrbl --blk N -k KEY -d DATA --force\n" +
                       "hf mf restore -f FILE --force — full clone      hf mf cload -f FILE — Gen1a only\n" +
                       "lf em 410x clone --id ID — write a T5577        lf t55xx detect — is the card writable\n" +
                       "hw tune — antenna voltages                      hw version — client and firmware version",
                ForeColor = Muted,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(16, 220),
                Width = 940,
                Height = 150
            });

            return tab;
        }

        private TextBox _shellBox;

        private async Task RunConsoleCommandAsync()
        {
            string cmd = (_cmdBox.Text ?? "").Trim();
            if (cmd.Length == 0) return;

            await WithBusyAsync("Running " + cmd, async ct =>
            {
                Log("");
                Log("===== pm3: " + cmd + " =====");
                var r = await Pm3.RunAsync(cmd, Log, 20 * 60 * 1000, ct).ConfigureAwait(true);
                SetStatus(r.Ok ? "Command finished." : "Command exited with " + r.ExitCode + ".",
                    r.Ok ? Good : Bad);
            }).ConfigureAwait(true);
        }

        private async Task RunShellCommandAsync()
        {
            string cmd = (_shellBox.Text ?? "").Trim();
            if (cmd.Length == 0) return;

            if (!Confirm("Run this inside the '" + S.Distro + "' environment?\n\n" + cmd, "Run a Linux command"))
                return;

            await WithBusyAsync("Running a shell command", async ct =>
            {
                Log("");
                Log("===== shell: " + cmd + " =====");
                var r = await Core.WslManager.RunInDistroAsync(S, cmd, Log, 20 * 60 * 1000, ct)
                    .ConfigureAwait(true);
                SetStatus(r.Ok ? "Command finished." : "Exit code " + r.ExitCode + ".", r.Ok ? Good : Bad);
            }).ConfigureAwait(true);
        }
    }
}
