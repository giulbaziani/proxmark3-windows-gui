using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Proxmark3Gui.Core;

namespace Proxmark3Gui.UI
{
    public partial class MainForm : Form
    {
        internal readonly AppSettings S;
        internal readonly SetupManager Setup;
        internal readonly Pm3Runner Pm3;

        private RichTextBox _log;
        private Label _status;
        private Dot _statusDot;
        private TabControl _tabs;
        private CancellationTokenSource _cts;
        private int _busyDepth;

        /// <summary>
        /// The version shown in the title bar. It comes from the VERSION file at the repo root,
        /// which the build stamps into the assembly — edit VERSION, rebuild, and it appears here.
        /// </summary>
        internal static string VersionString
        {
            get
            {
                try
                {
                    var asm = typeof(MainForm).Assembly;
                    var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute
                        .GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                    if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                    {
                        // Strip any build metadata the SDK may append (e.g. "1.0.0+abcdef").
                        string v = info.InformationalVersion;
                        int plus = v.IndexOf('+');
                        return plus >= 0 ? v.Substring(0, plus) : v;
                    }
                    var ver = asm.GetName().Version;
                    return ver != null ? ver.ToString(3) : "1.0.0";
                }
                catch { return "1.0.0"; }
            }
        }

        internal static readonly Color Bg = Color.FromArgb(246, 247, 250);
        internal static readonly Color Card = Color.White;
        internal static readonly Color Ink = Color.FromArgb(24, 28, 35);
        internal static readonly Color Muted = Color.FromArgb(108, 116, 128);
        internal static readonly Color Line = Color.FromArgb(226, 229, 235);
        internal static readonly Color Accent = Color.FromArgb(0, 98, 204);
        internal static readonly Color AccentHover = Color.FromArgb(0, 86, 179);
        internal static readonly Color Good = Color.FromArgb(22, 128, 72);
        internal static readonly Color Bad = Color.FromArgb(197, 48, 48);

        /// <summary>A small filled circle used as a status indicator.</summary>
        internal sealed class Dot : Control
        {
            public Color FillColor { get; set; } = Color.Gray;
            public Dot() { SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int d = Math.Min(Width, Height) - 2;
                using (var b = new SolidBrush(FillColor))
                    e.Graphics.FillEllipse(b, 1, (Height - d) / 2, d, d);
            }
        }

        public MainForm()
        {
            S = AppSettings.Load();
            Setup = new SetupManager(S);
            Pm3 = new Pm3Runner(S);

            // Font-based autoscaling keeps the layout correct across DPI / display-scale settings.
            AutoScaleMode = AutoScaleMode.Font;
            DoubleBuffered = true;

            BuildShell();

            // Force the window handle to exist on the UI thread before any work can log, so that
            // every Log/SetStatus marshals correctly instead of touching a control cross-thread.
            var _ = Handle;

            Shown += async (s, e) =>
            {
                try { await OnShownAsync().ConfigureAwait(true); }
                catch (Exception ex) { Core.Diag.Crash("OnShown", ex); Log("[!] " + ex.Message); }
            };
            FormClosing += (s, e) => { try { _cts?.Cancel(); } catch { } S.Save(); };
        }

        /// <summary>
        /// Runs an action on the UI thread, whatever thread calls it. Every control mutation goes
        /// through here so background work (process output, setup steps) can never touch a control
        /// cross-thread. Never throws.
        /// </summary>
        internal void Ui(Action action)
        {
            if (action == null) return;
            try
            {
                if (IsDisposed) return;
                if (!IsHandleCreated)
                {
                    // Handle not realized yet: run inline only if we are on the thread that owns
                    // the form; otherwise there is nothing safe to do, so drop the update.
                    action();
                    return;
                }
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (Exception ex)
            {
                // Record the full stack so any remaining cross-thread access can be pinpointed.
                Core.Diag.Crash("Ui", ex);
            }
        }

        // ------------------------------------------------------------------ shell

        private void BuildShell()
        {
            Text = "Proxmark3 Windows GUI  v" + VersionString;
            TryLoadIcon();
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 760);
            MinimumSize = new Size(900, 620);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9.5f);

            var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Card };
            var headerLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Line };
            header.Controls.Add(headerLine);

            var title = new Label
            {
                Text = "Proxmark3 Windows GUI",
                Font = new Font("Segoe UI Semibold", 13.5f, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(18, 9)
            };
            var versionBadge = new Label
            {
                Text = "v" + VersionString,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = Accent,
                BackColor = Color.FromArgb(232, 240, 251),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 20,
                Width = 58,
                Location = new Point(title.Right + 8, 13)
            };
            title.LocationChanged += (s, e) => versionBadge.Left = title.Right + 8;

            _statusDot = new Dot { FillColor = Muted, Size = new Size(12, 12), Location = new Point(20, 37) };
            _status = new Label
            {
                Text = "Starting up...",
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(36, 35)
            };

            header.Controls.Add(title);
            header.Controls.Add(versionBadge);
            header.Controls.Add(_statusDot);
            header.Controls.Add(_status);

            var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 230, Padding = new Padding(10, 6, 10, 10) };
            var logHeader = new Panel { Dock = DockStyle.Top, Height = 26 };
            logHeader.Controls.Add(new Label
            {
                Text = "Log",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(2, 4)
            });
            var btnCopy = new Button
            {
                Text = "Copy",
                Width = 70,
                Height = 24,
                FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnCopy.Click += (s, e) =>
            {
                try { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); } catch { }
            };
            var btnClear = new Button
            {
                Text = "Clear",
                Width = 70,
                Height = 24,
                FlatStyle = FlatStyle.System,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClear.Click += (s, e) => _log.Clear();
            logHeader.Resize += (s, e) =>
            {
                btnCopy.Left = logHeader.Width - 150;
                btnClear.Left = logHeader.Width - 76;
            };
            logHeader.Controls.Add(btnCopy);
            logHeader.Controls.Add(btnClear);

            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(24, 26, 31),
                ForeColor = Color.FromArgb(222, 226, 233),
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                DetectUrls = false
            };

            logPanel.Controls.Add(_log);
            logPanel.Controls.Add(logHeader);

            var splitter = new Splitter { Dock = DockStyle.Bottom, Height = 4, BackColor = Color.FromArgb(226, 229, 235) };

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 6),
                SizeMode = TabSizeMode.Normal
            };
            _tabs.TabPages.Add(BuildSetupTab());
            _tabs.TabPages.Add(BuildDeviceTab());
            _tabs.TabPages.Add(BuildHfTab());
            _tabs.TabPages.Add(BuildLfTab());
            _tabs.TabPages.Add(BuildFilesTab());
            _tabs.TabPages.Add(BuildConsoleTab());
            _tabs.TabPages.Add(BuildSettingsTab());

            // Every tab scrolls when the window is too small to show its content, so the app stays
            // usable at any resolution instead of clipping controls.
            foreach (TabPage page in _tabs.TabPages)
            {
                page.AutoScroll = true;
                page.AutoScrollMinSize = new Size(900, 470);
            }

            Controls.Add(_tabs);
            Controls.Add(splitter);
            Controls.Add(logPanel);
            Controls.Add(header);
        }

        private async Task OnShownAsync()
        {
            Log("Proxmark3 Windows GUI  v" + VersionString);
            Log("Settings: " + AppSettings.FilePath);

            if (!Elevation.IsAdministrator())
                Log("[!] Not running as administrator — attaching USB to Linux will fail.");

            try { await RefreshBackupsAsync().ConfigureAwait(true); } catch { }

            await RefreshSetupStatusAsync().ConfigureAwait(true);
        }

        // ------------------------------------------------------------------ helpers

        private void TryLoadIcon()
        {
            try
            {
                // The .ico is embedded in the exe via <ApplicationIcon>; reuse it for the window.
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { /* a missing icon must never stop the app from opening */ }
        }

        internal void Log(string line)
        {
            if (line == null) return;
            Core.Diag.Line(line);
            Ui(() =>
            {
                // Keep the log bounded so a long build cannot grow it without limit.
                if (_log.Lines.Length > 6000)
                {
                    var keep = _log.Lines;
                    _log.Lines = new List<string>(keep).GetRange(keep.Length - 3000, 3000).ToArray();
                }

                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;

                if (line.StartsWith("[!]")) _log.SelectionColor = Color.FromArgb(255, 138, 128);
                else if (line.StartsWith("[+]")) _log.SelectionColor = Color.FromArgb(126, 222, 150);
                else if (line.StartsWith("[i]") || line.StartsWith("=====")) _log.SelectionColor = Color.FromArgb(126, 184, 255);
                else _log.SelectionColor = Color.FromArgb(222, 226, 233);

                _log.AppendText(line + Environment.NewLine);
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            });
        }

        internal void SetStatus(string text, Color? color = null)
        {
            Ui(() =>
            {
                _statusDot.FillColor = color ?? Muted;
                _statusDot.Invalidate();
                _status.Text = text;
                _status.ForeColor = color ?? Muted;
            });
        }

        /// <summary>Disables the UI while an operation runs and gives it a cancellation token.</summary>
        internal async Task WithBusyAsync(string what, Func<CancellationToken, Task> work)
        {
            if (_busyDepth > 0)
            {
                MessageBox.Show("Another operation is still running. Wait for it to finish, or press Stop.",
                    "Busy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _busyDepth++;
            _cts = new CancellationTokenSource();
            SetControlsEnabled(false);
            SetStatus(what + "...", Accent);
            UseWaitCursor = true;

            try
            {
                await work(_cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Log("[!] Cancelled.");
                SetStatus("Cancelled.", Bad);
            }
            catch (Exception ex)
            {
                Core.Diag.Crash("WithBusy:" + what, ex);
                Log("[!] " + ex.Message);
                SetStatus("Error: " + ex.Message, Bad);
            }
            finally
            {
                UseWaitCursor = false;
                _busyDepth--;
                SetControlsEnabled(true);
                try { _cts.Dispose(); } catch { }
                _cts = null;
            }
        }

        internal void CancelCurrent()
        {
            try { _cts?.Cancel(); } catch { }
        }

        internal bool IsBusy { get { return _busyDepth > 0; } }

        /// <summary>
        /// Locks the interactive controls while an operation runs. Everything is disabled except
        /// controls tagged "always-on" (the Stop buttons) and the tab strip, so the user can still
        /// switch tabs to watch the log. Controls come back only when the operation finishes —
        /// whether it succeeded or failed — via the finally block in WithBusyAsync.
        /// </summary>
        private void SetControlsEnabled(bool on)
        {
            Ui(() =>
            {
                foreach (var c in EnumerateInputs(this))
                {
                    if (c.Tag as string == "always-on") continue;
                    c.Enabled = on;
                }
            });
        }

        private static IEnumerable<Control> EnumerateInputs(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button || c is ComboBox || c is TextBox || c is CheckBox ||
                    c is ListView || c is ListBox)
                    yield return c;
                foreach (var inner in EnumerateInputs(c)) yield return inner;
            }
        }

        internal bool Confirm(string text, string caption = "Confirm")
        {
            return MessageBox.Show(text, caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                   == DialogResult.OK;
        }

        internal bool Ask(string text, string caption = "Question")
        {
            return MessageBox.Show(text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                   == DialogResult.Yes;
        }

        internal void SelectTab(int index)
        {
            if (index >= 0 && index < _tabs.TabPages.Count) _tabs.SelectedIndex = index;
        }

        // ---------------------------------------------------------------- small widgets

        internal static Label H1(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(x, y)
            };
        }

        internal static Label Hint(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                ForeColor = Muted,
                Location = new Point(x, y),
                Width = width,
                AutoSize = false,
                Height = 40
            };
        }

        internal static Label Lbl(string text, int x, int y, int width = 150)
        {
            return new Label { Text = text, ForeColor = Ink, Location = new Point(x, y + 4), Width = width, AutoSize = false };
        }

        internal static TextBox Txt(string text, int x, int y, int width)
        {
            return new TextBox { Text = text ?? "", Location = new Point(x, y), Width = width };
        }

        internal Button Btn(string text, int x, int y, int width, Func<Task> onClick, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Width = width,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = true
            };
            b.FlatAppearance.BorderSize = 1;

            if (primary)
            {
                b.BackColor = Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = Accent;
                b.FlatAppearance.MouseOverBackColor = AccentHover;
                b.FlatAppearance.MouseDownBackColor = AccentHover;
                b.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            }
            else
            {
                b.BackColor = Card;
                b.ForeColor = Ink;
                b.FlatAppearance.BorderColor = Line;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 243, 251);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 235, 249);
                b.Font = new Font("Segoe UI", 9.5f);
            }

            // Keep the disabled look readable rather than the greyed-out default on a white button.
            b.EnabledChanged += (s, e) =>
            {
                if (!b.Enabled) b.ForeColor = Color.FromArgb(170, 176, 186);
                else b.ForeColor = primary ? Color.White : Ink;
            };

            b.Click += async (s, e) =>
            {
                try { await onClick().ConfigureAwait(true); }
                catch (Exception ex) { Core.Diag.Crash("Btn:" + text, ex); Log("[!] " + ex.Message); }
            };
            return b;
        }
    }
}
