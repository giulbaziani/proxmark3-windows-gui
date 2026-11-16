using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Proxmark3Gui.Core;

namespace Proxmark3Gui.UI
{
    public partial class MainForm
    {
        private ListBox _deviceList;
        private TextBox _txtBusId;
        private Label _deviceState;

        private TabPage BuildDeviceTab()
        {
            var tab = new TabPage("2 · Device") { BackColor = Bg, Padding = new Padding(16) };

            tab.Controls.Add(H1("Connect the Proxmark3", 16, 14));
            tab.Controls.Add(new Label
            {
                Text = "Plug the device in, press Detect, then Connect. Windows hands the USB port to the Linux " +
                       "environment; you only need to do this once per Windows session.",
                ForeColor = Muted,
                Location = new Point(16, 42),
                Width = 940,
                Height = 36
            });

            _deviceList = new ListBox
            {
                Location = new Point(16, 84),
                Size = new Size(640, 150),
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.FixedSingle
            };
            _deviceList.SelectedIndexChanged += (s, e) =>
            {
                var d = _deviceList.SelectedItem as UsbDevice;
                if (d != null) _txtBusId.Text = d.BusId;
            };
            tab.Controls.Add(_deviceList);

            tab.Controls.Add(Btn("Detect", 672, 84, 150, DetectDevicesAsync));
            tab.Controls.Add(Btn("Connect to Linux", 672, 122, 150, AttachAsync, primary: true));
            tab.Controls.Add(Btn("Disconnect", 672, 160, 150, DetachAsync));
            tab.Controls.Add(Btn("Test connection", 672, 198, 150, TestConnectionAsync));

            tab.Controls.Add(Lbl("USB bus id", 16, 248, 90));
            _txtBusId = Txt(S.BusId, 110, 244, 100);
            tab.Controls.Add(_txtBusId);

            _deviceState = new Label
            {
                Text = "Not connected.",
                ForeColor = Muted,
                Location = new Point(230, 248),
                Width = 600,
                AutoSize = false
            };
            tab.Controls.Add(_deviceState);

            tab.Controls.Add(Btn("Antenna check (hw tune)", 16, 284, 200, TuneAsync));
            tab.Controls.Add(Btn("Identify HF card", 226, 284, 170, async () => await QuickSearchAsync("hf search").ConfigureAwait(true)));
            tab.Controls.Add(Btn("Identify LF card", 406, 284, 170, async () => await QuickSearchAsync("lf search").ConfigureAwait(true)));

            tab.Controls.Add(new Label
            {
                Text = "If Connect fails\n" +
                       "•  Make sure the app is running as administrator.\n" +
                       "•  Unplug and replug the device, then press Detect again.\n" +
                       "•  A freshly flashed device gets a new USB id — detect and connect once more.",
                ForeColor = Muted,
                Location = new Point(16, 334),
                Width = 940,
                Height = 90
            });

            return tab;
        }

        private async Task DetectDevicesAsync()
        {
            await WithBusyAsync("Detecting USB devices", async ct =>
            {
                if (!UsbIpdManager.IsInstalled())
                {
                    Log("[!] usbipd-win is not installed. Run setup first.");
                    SetStatus("usbipd-win missing — run setup.", Bad);
                    return;
                }

                var list = await UsbIpdManager.ListAsync(null).ConfigureAwait(true);
                _deviceList.Items.Clear();
                foreach (var d in list) _deviceList.Items.Add(d);

                Log("[i] " + list.Count + " USB device(s) visible to usbipd.");

                var pm3 = await UsbIpdManager.FindProxmarkAsync(null).ConfigureAwait(true);
                if (pm3 != null)
                {
                    _txtBusId.Text = pm3.BusId;
                    S.BusId = pm3.BusId;
                    S.Save();
                    for (int i = 0; i < _deviceList.Items.Count; i++)
                        if (ReferenceEquals(_deviceList.Items[i], pm3)) _deviceList.SelectedIndex = i;

                    Log("[+] Proxmark3 found on bus " + pm3.BusId + " (" + pm3.VidPid + ").");
                    _deviceState.Text = pm3.IsAttached ? "Already connected to Linux." : "Found — press Connect.";
                    _deviceState.ForeColor = pm3.IsAttached ? Good : Accent;
                    SetStatus("Proxmark3 on bus " + pm3.BusId + ".", Good);
                }
                else
                {
                    Log("[!] No Proxmark3 recognised. Pick the right device from the list by hand.");
                    _deviceState.Text = "Not found — select the device manually.";
                    _deviceState.ForeColor = Bad;
                    SetStatus("Device not detected.", Bad);
                }
            }).ConfigureAwait(true);
        }

        private async Task AttachAsync()
        {
            string bus = _txtBusId.Text.Trim();
            if (bus.Length == 0)
            {
                MessageBox.Show("Press Detect first, or type the USB bus id (for example 2-1).",
                    "No device", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Elevation.IsAdministrator())
            {
                MessageBox.Show("Connecting the USB device needs administrator rights. " +
                                "Restart the app with \"Run as administrator\".",
                    "Administrator required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            S.BusId = bus;
            S.Save();

            await WithBusyAsync("Connecting the device", async ct =>
            {
                var r = await UsbIpdManager.AttachAsync(bus, S.Distro, Log, ct).ConfigureAwait(true);
                if (!r.Ok)
                {
                    Log("[!] Attach failed (exit code " + r.ExitCode + ").");
                    _deviceState.Text = "Attach failed.";
                    _deviceState.ForeColor = Bad;
                    SetStatus("Could not connect the device.", Bad);
                    return;
                }

                await Task.Delay(1500, ct).ConfigureAwait(true);
                string port = await Pm3.FindSerialPortAsync(ct).ConfigureAwait(true);

                if (port != null)
                {
                    Log("[+] Device visible inside Linux as " + port + ".");
                    _deviceState.Text = "Connected — " + port;
                    _deviceState.ForeColor = Good;
                    SetStatus("Device connected.", Good);
                }
                else
                {
                    Log("[!] Attached, but no /dev/ttyACM* appeared. Unplug, replug, detect and try again.");
                    _deviceState.Text = "Attached, but Linux sees no serial port.";
                    _deviceState.ForeColor = Bad;
                    SetStatus("Device not reachable inside Linux.", Bad);
                }
            }).ConfigureAwait(true);
        }

        private async Task DetachAsync()
        {
            string bus = _txtBusId.Text.Trim();
            if (bus.Length == 0) return;

            await WithBusyAsync("Disconnecting", async ct =>
            {
                await UsbIpdManager.DetachAsync(bus, Log).ConfigureAwait(true);
                _deviceState.Text = "Disconnected.";
                _deviceState.ForeColor = Muted;
                SetStatus("Device returned to Windows.", Muted);
            }).ConfigureAwait(true);
        }

        private async Task TestConnectionAsync()
        {
            await WithBusyAsync("Testing", async ct =>
            {
                var r = await Pm3.HwVersionAsync(Log, ct).ConfigureAwait(true);

                if (Pm3Parse.PortMissing(r.Output))
                {
                    Log("[!] The client could not open the device. Connect it on this tab first.");
                    SetStatus("No device.", Bad);
                    return;
                }

                if (r.OutputContains("Client:") || r.OutputContains("firmware"))
                {
                    Log("[+] The client talked to the device.");
                    SetStatus("Device is working.", Good);
                    _deviceState.Text = "Working.";
                    _deviceState.ForeColor = Good;
                }
                else
                {
                    SetStatus("Unexpected answer — see the log.", Bad);
                }
            }).ConfigureAwait(true);
        }

        private async Task TuneAsync()
        {
            await WithBusyAsync("Measuring the antennas", async ct =>
            {
                var r = await Pm3.HwTuneAsync(Log, ct).ConfigureAwait(true);
                SetStatus(r.Ok ? "Antenna measurement done." : "Antenna measurement failed.", r.Ok ? Good : Bad);
            }).ConfigureAwait(true);
        }

        private async Task QuickSearchAsync(string command)
        {
            await WithBusyAsync("Reading the card", async ct =>
            {
                var r = await Pm3.RunAsync(command, Log, 120000, ct).ConfigureAwait(true);

                string uid = Pm3Parse.Uid(r.Output);
                string em = Pm3Parse.Em410xId(r.Output);
                string magic = Pm3Parse.MagicCapabilities(r.Output);

                if (uid != null) Log("[+] UID: " + uid);
                if (em != null) Log("[+] EM4100 ID: " + em);
                if (magic != null) Log("[+] Magic: " + magic);

                if (uid == null && em == null)
                    SetStatus("No card recognised — reposition it on the antenna.", Bad);
                else
                    SetStatus("Card read.", Good);
            }).ConfigureAwait(true);
        }
    }
}
