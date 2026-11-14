using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cleaner.Setup
{
    /// <summary>Three page wizard used for install and uninstall.</summary>
    internal sealed class SetupForm : Form
    {
        private enum PageKind
        {
            Options,
            Progress,
            Done,
            UninstallConfirm
        }

        private readonly SetupOptions _options;
        private readonly bool _isAdmin;
        private readonly bool _machineScope;

        private Panel _banner;
        private Panel _content;
        private Panel _welcomePage;
        private Panel _progressPage;
        private Panel _donePage;
        private Panel _uninstallPage;

        private FlatPrimary _primary;
        private Button _cancel;

        private TextBox _pathBox;
        private CheckBox _desktopCheck;
        private CheckBox _startMenuCheck;
        private CheckBox _launchCheck;
        private CheckBox _deleteDataCheck;
        private CheckBox _finishLaunchCheck;
        private Label _scopeLabel;
        private Label _upgradeLabel;

        private ProgressBar _progressBar;
        private Label _statusLabel;
        private TextBox _detailsBox;

        private Label _doneHeading;
        private Label _doneBody;

        private Label _uninstallHeading;
        private Label _uninstallBody;
        private Label _uninstallPath;
        private Label _uninstallNote;

        private PageKind _page = PageKind.Options;
        private bool _working;
        private bool _notInstalled;
        private string _installedDirectory;

        /// <summary>Set by --screenshot so no modal dialog can block automation.</summary>
        public bool SuppressDialogs { get; set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_notInstalled && !SuppressDialogs)
            {
                MessageBox.Show(this, S.T("uninstall.notInstalled"), S.T("error.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public SetupForm(SetupOptions options, bool isAdmin, bool machineScope)
        {
            _options = options;
            _isAdmin = isAdmin;
            _machineScope = machineScope;

            BuildUi();
            LoadState();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = _options.Uninstall ? S.T("uninstall.title") : S.T("setup.title");
            BackColor = SetupTheme.Background;
            ForeColor = SetupTheme.Text;
            Font = SetupTheme.Font(9.5F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(660, 472);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            DoubleBuffered = true;
            Icon = LoadIcon();

            _banner = new Panel { Dock = DockStyle.Top, Height = 80 };
            _banner.Paint += PaintBanner;

            Panel buttons = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = SetupTheme.Background
            };

            _primary = new FlatPrimary
            {
                Text = _options.Uninstall ? S.T("btn.uninstall") : S.T("btn.install"),
                Size = new Size(118, 38)
            };
            _primary.Click += async (sender, e) => await OnPrimaryAsync();

            _cancel = new Button
            {
                Text = S.T("btn.cancel"),
                Size = new Size(104, 38),
                FlatStyle = FlatStyle.System
            };
            _cancel.Click += (sender, e) => OnCancel();

            buttons.Controls.Add(_primary);
            buttons.Controls.Add(_cancel);
            buttons.Layout += (sender, e) => LayoutButtons(buttons);

            _content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SetupTheme.Background,
                Padding = new Padding(26, 18, 26, 8)
            };

            _welcomePage = BuildWelcomePage();
            _progressPage = BuildProgressPage();
            _donePage = BuildDonePage();
            _uninstallPage = BuildUninstallPage();

            _content.Controls.Add(_welcomePage);
            _content.Controls.Add(_progressPage);
            _content.Controls.Add(_donePage);
            _content.Controls.Add(_uninstallPage);

            // Docking order: the fill panel is added first so it keeps the space
            // left by the banner and the button bar.
            Controls.Add(_content);
            Controls.Add(buttons);
            Controls.Add(_banner);

            ResumeLayout(true);
        }

        private void LayoutButtons(Panel host)
        {
            if (_primary == null || _cancel == null)
            {
                return;
            }

            int right = host.ClientSize.Width - 26;
            int top = Math.Max(0, (host.ClientSize.Height - _primary.Height) / 2);
            _primary.Location = new Point(right - _primary.Width, top);
            _cancel.Location = new Point(_primary.Left - 10 - _cancel.Width, top + 1);
        }

        private static Icon LoadIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(SetupInfo.ExePath);
            }
            catch
            {
                return null;
            }
        }

        private Panel BuildWelcomePage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = SetupTheme.Background, Visible = false };

            Label heading = MakeLabel(S.T("welcome.heading"), 15F, FontStyle.Bold, new Point(0, 0), new Size(600, 30));
            page.Controls.Add(heading);

            Label body = MakeLabel(S.T("welcome.body"), 9F, FontStyle.Regular, new Point(1, 34), new Size(600, 66));
            body.ForeColor = SetupTheme.Muted;
            page.Controls.Add(body);

            Label location = MakeLabel(S.T("welcome.location"), 9.5F, FontStyle.Bold, new Point(0, 104), new Size(200, 22));
            page.Controls.Add(location);

            _pathBox = new TextBox
            {
                Location = new Point(0, 128),
                Size = new Size(500, 26),
                BorderStyle = BorderStyle.FixedSingle
            };
            page.Controls.Add(_pathBox);

            Button browse = new Button
            {
                Text = S.T("welcome.browse"),
                Location = new Point(510, 127),
                Size = new Size(90, 28),
                FlatStyle = FlatStyle.System
            };
            browse.Click += (sender, e) => BrowseFolder();
            page.Controls.Add(browse);

            _scopeLabel = MakeLabel(string.Empty, 8.75F, FontStyle.Regular, new Point(1, 158), new Size(600, 20));
            _scopeLabel.ForeColor = SetupTheme.Muted;
            page.Controls.Add(_scopeLabel);

            _upgradeLabel = MakeLabel(string.Empty, 8.75F, FontStyle.Regular, new Point(1, 178), new Size(600, 20));
            _upgradeLabel.ForeColor = SetupTheme.AccentDark;
            page.Controls.Add(_upgradeLabel);

            _desktopCheck = new CheckBox
            {
                Text = S.T("welcome.desktop"),
                Location = new Point(0, 208),
                Size = new Size(300, 24),
                Checked = _options.DesktopShortcut ?? true,
                BackColor = SetupTheme.Background
            };
            page.Controls.Add(_desktopCheck);

            _startMenuCheck = new CheckBox
            {
                Text = S.T("welcome.startmenu"),
                Location = new Point(0, 234),
                Size = new Size(300, 24),
                Checked = _options.StartMenuShortcut ?? true,
                BackColor = SetupTheme.Background
            };
            page.Controls.Add(_startMenuCheck);

            _launchCheck = new CheckBox
            {
                Text = S.T("welcome.launch"),
                Location = new Point(0, 260),
                Size = new Size(300, 24),
                Checked = _options.LaunchAfter ?? true,
                BackColor = SetupTheme.Background
            };
            page.Controls.Add(_launchCheck);

            Label hint = MakeLabel(S.T("welcome.hint"), 8.5F, FontStyle.Regular, new Point(1, 300), new Size(590, 34));
            hint.ForeColor = SetupTheme.Muted;
            page.Controls.Add(hint);

            return page;
        }

        private Panel BuildProgressPage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = SetupTheme.Background, Visible = false };

            Label heading = MakeLabel(S.T("progress.preparing"), 13F, FontStyle.Bold, new Point(0, 4), new Size(600, 28));
            heading.Name = "progressHeading";
            page.Controls.Add(heading);

            _progressBar = new ProgressBar
            {
                Location = new Point(0, 44),
                Size = new Size(600, 16),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            page.Controls.Add(_progressBar);

            _statusLabel = MakeLabel(string.Empty, 9F, FontStyle.Regular, new Point(0, 68), new Size(600, 22));
            _statusLabel.ForeColor = SetupTheme.Muted;
            page.Controls.Add(_statusLabel);

            _detailsBox = new TextBox
            {
                Location = new Point(0, 96),
                Size = new Size(600, 168),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SetupTheme.Surface,
                Font = SetupTheme.Font(8.75F, FontStyle.Regular)
            };
            page.Controls.Add(_detailsBox);

            return page;
        }

        private Panel BuildDonePage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = SetupTheme.Background, Visible = false };

            _doneHeading = MakeLabel(string.Empty, 15F, FontStyle.Bold, new Point(0, 0), new Size(600, 30));
            page.Controls.Add(_doneHeading);

            _doneBody = MakeLabel(string.Empty, 9F, FontStyle.Regular, new Point(1, 38), new Size(600, 110));
            _doneBody.ForeColor = SetupTheme.Muted;
            page.Controls.Add(_doneBody);

            _finishLaunchCheck = new CheckBox
            {
                Text = S.T("done.launch"),
                Location = new Point(0, 156),
                Size = new Size(320, 24),
                Checked = true,
                BackColor = SetupTheme.Background,
                Visible = false
            };
            page.Controls.Add(_finishLaunchCheck);

            return page;
        }

        private Panel BuildUninstallPage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = SetupTheme.Background, Visible = false };

            _uninstallHeading = MakeLabel(S.T("uninstall.heading"), 15F, FontStyle.Bold, new Point(0, 0), new Size(600, 30));
            page.Controls.Add(_uninstallHeading);

            _uninstallBody = MakeLabel(S.T("uninstall.body"), 9F, FontStyle.Regular, new Point(1, 34), new Size(600, 60));
            _uninstallBody.ForeColor = SetupTheme.Muted;
            page.Controls.Add(_uninstallBody);

            _uninstallPath = MakeLabel(string.Empty, 9.5F, FontStyle.Bold, new Point(1, 102), new Size(600, 44));
            page.Controls.Add(_uninstallPath);

            _deleteDataCheck = new CheckBox
            {
                Text = string.Empty,
                Location = new Point(0, 156),
                Size = new Size(560, 24),
                Checked = _options.DeleteUserData ?? false,
                BackColor = SetupTheme.Background
            };
            page.Controls.Add(_deleteDataCheck);

            _uninstallNote = MakeLabel(S.T("uninstall.taskNote"), 8.75F, FontStyle.Regular, new Point(1, 190), new Size(600, 20));
            _uninstallNote.ForeColor = SetupTheme.Muted;
            page.Controls.Add(_uninstallNote);

            return page;
        }

        private static Label MakeLabel(string text, float fontSize, FontStyle style, Point location, Size size)
        {
            return new Label
            {
                Text = text,
                Font = SetupTheme.Font(fontSize, style),
                Location = location,
                Size = size,
                AutoSize = false,
                ForeColor = SetupTheme.Text,
                BackColor = Color.Transparent
            };
        }

        private void PaintBanner(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = _banner.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, SetupTheme.HeaderStart, SetupTheme.HeaderEnd, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, bounds);
            }

            using (LinearGradientBrush glow = new LinearGradientBrush(
                new Rectangle(0, -bounds.Height / 2, Math.Max(1, bounds.Width), bounds.Height),
                Color.FromArgb(36, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(glow, bounds);
            }

            float scale = Math.Max(0.6f, bounds.Height / 80f);
            try
            {
                Icon icon = Icon;
                if (icon != null)
                {
                    int size = (int)(42 * scale);
                    g.DrawIcon(icon, new Rectangle((int)(26 * scale), (bounds.Height - size) / 2, size, size));
                }
            }
            catch
            {
            }

            using (Font titleFont = SetupTheme.Font(17F, FontStyle.Bold))
            using (Font subtitleFont = SetupTheme.Font(9F, FontStyle.Regular))
            {
                TextRenderer.DrawText(
                    g,
                    _options.Uninstall ? S.T("uninstall.title") : S.T("setup.title"),
                    titleFont,
                    new Rectangle((int)(84 * scale), (int)(14 * scale), bounds.Width - (int)(120 * scale), (int)(32 * scale)),
                    Color.White,
                    TextFormatFlags.Left | TextFormatFlags.Bottom);

                TextRenderer.DrawText(
                    g,
                    _options.Uninstall ? S.T("uninstall.subtitle", SetupInfo.Version) : S.T("setup.subtitle", SetupInfo.Version),
                    subtitleFont,
                    new Rectangle((int)(86 * scale), (int)(46 * scale), bounds.Width - (int)(120 * scale), (int)(20 * scale)),
                    Color.FromArgb(214, 236, 238),
                    TextFormatFlags.Left | TextFormatFlags.Top);
            }
        }

        private void LoadState()
        {
            InstallState state = Installer.Query();
            string defaultDir = _options.InstallDir;

            if (string.IsNullOrEmpty(defaultDir))
            {
                defaultDir = state.Installed && !string.IsNullOrEmpty(state.InstallDir)
                    ? state.InstallDir
                    : Installer.DefaultInstallDir(_machineScope);
            }

            _pathBox.Text = defaultDir;

            if (_options.Uninstall)
            {
                _uninstallPath.Text = state.Installed ? state.InstallDir : defaultDir;
                _deleteDataCheck.Text = S.T("uninstall.keepData", UserDataFolder());
                ShowPage(PageKind.UninstallConfirm);

                if (!state.Installed)
                {
                    _primary.Enabled = false;
                    _notInstalled = true;
                }

                return;
            }

            _scopeLabel.Text = _machineScope ? S.T("welcome.scopeMachine") : S.T("welcome.scopeUser");
            _upgradeLabel.Text = state.Installed
                ? S.T("welcome.upgrade", state.Version ?? SetupInfo.Version)
                : S.T("welcome.freeSpace", FormatSize(Payload.Size + 400 * 1024));

            ShowPage(PageKind.Options);
        }

        private static string UserDataFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SetupInfo.Product);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "—";
            }

            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return string.Format(System.Globalization.CultureInfo.CurrentCulture,
                unit == 0 ? "{0:N0} {1}" : "{0:N1} {1}", value, units[unit]);
        }

        private void ShowPage(PageKind page)
        {
            _page = page;
            _welcomePage.Visible = page == PageKind.Options;
            _progressPage.Visible = page == PageKind.Progress;
            _donePage.Visible = page == PageKind.Done;
            _uninstallPage.Visible = page == PageKind.UninstallConfirm;

            _cancel.Enabled = !_working;
            _primary.Enabled = !_working;

            switch (page)
            {
                case PageKind.Options:
                    _primary.Text = S.T("btn.install");
                    break;
                case PageKind.UninstallConfirm:
                    _primary.Text = S.T("btn.uninstall");
                    break;
                case PageKind.Progress:
                    _primary.Text = S.T("progress.preparing");
                    break;
                case PageKind.Done:
                    _primary.Text = S.T("btn.finish");
                    _finishLaunchCheck.Visible = !_options.Uninstall;
                    break;
            }
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = S.T("welcome.location");
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(_pathBox.Text))
                {
                    dialog.SelectedPath = _pathBox.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                {
                    _pathBox.Text = dialog.SelectedPath;
                }
            }
        }

        private async Task OnPrimaryAsync()
        {
            if (_working)
            {
                return;
            }

            if (_page == PageKind.Done)
            {
                Finish();
                return;
            }

            if (_options.Uninstall || _page == PageKind.UninstallConfirm)
            {
                await RunUninstallAsync();
                return;
            }

            await RunInstallAsync();
        }

        private async Task RunInstallAsync()
        {
            string directory = (_pathBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
            {
                MessageBox.Show(this, S.T("error.dir"), S.T("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            InstallRequest request = new InstallRequest
            {
                InstallDir = directory,
                MachineScope = _machineScope,
                DesktopShortcut = _desktopCheck.Checked,
                StartMenuShortcut = _startMenuCheck.Checked,
                LaunchAfter = _launchCheck.Checked
            };

            SetWorking(true);
            ShowPage(PageKind.Progress);
            AppendDetails(S.T("welcome.location") + ": " + directory + Environment.NewLine);
            _progressBar.Value = 0;
            SetProgressHeading(S.T("progress.preparing"));

            IProgress<InstallProgress> progress = new Progress<InstallProgress>(report =>
            {
                _progressBar.Value = Math.Max(0, Math.Min(100, report.Percent));
                _statusLabel.Text = report.Message;
                if (!string.IsNullOrEmpty(report.Message))
                {
                    AppendDetails(report.Message + Environment.NewLine);
                }
            });

            WorkResult result = await Task.Run(() =>
            {
                string error;
                bool ok = Installer.Install(request, progress, out error);
                return new WorkResult { Ok = ok, Error = error };
            });

            SetWorking(false);

            if (!result.Ok)
            {
                MessageBox.Show(this, S.T("error.failed", result.Error ?? string.Empty), S.T("error.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(PageKind.Options);
                return;
            }

            _installedDirectory = directory;
            _progressBar.Value = 100;

            _doneHeading.Text = S.T("done.heading");
            _doneBody.Text = S.T("done.body", SetupInfo.Version, directory);
            _finishLaunchCheck.Checked = request.LaunchAfter;
            ShowPage(PageKind.Done);
        }

        private async Task RunUninstallAsync()
        {
            if (!_isAdmin && _machineScope)
            {
                MessageBox.Show(this, S.T("error.admin"), S.T("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                S.T("uninstall.body"),
                S.T("uninstall.heading"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            InstallRequest request = new InstallRequest
            {
                InstallDir = _uninstallPath.Text,
                MachineScope = _machineScope,
                DeleteUserData = _deleteDataCheck.Checked
            };

            SetWorking(true);
            ShowPage(PageKind.Progress);
            _progressBar.Value = 0;
            SetProgressHeading(S.T("progress.files"));

            IProgress<InstallProgress> progress = new Progress<InstallProgress>(report =>
            {
                _progressBar.Value = Math.Max(0, Math.Min(100, report.Percent));
                _statusLabel.Text = report.Message;
                if (!string.IsNullOrEmpty(report.Message))
                {
                    AppendDetails(report.Message + Environment.NewLine);
                }
            });

            WorkResult result = await Task.Run(() =>
            {
                string error;
                bool ok = Installer.Uninstall(request, progress, out error);
                return new WorkResult { Ok = ok, Error = error };
            });

            SetWorking(false);

            if (!result.Ok)
            {
                MessageBox.Show(this, S.T("error.failed", result.Error ?? string.Empty), S.T("error.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(PageKind.UninstallConfirm);
                return;
            }

            _progressBar.Value = 100;
            _doneHeading.Text = S.T("uninstallDone.heading");
            _doneBody.Text = S.T("uninstallDone.body");
            ShowPage(PageKind.Done);
        }

        private void SetProgressHeading(string text)
        {
            foreach (Control control in _progressPage.Controls)
            {
                if (control.Name == "progressHeading")
                {
                    control.Text = text;
                    break;
                }
            }
        }

        private void AppendDetails(string text)
        {
            if (_detailsBox == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            _detailsBox.AppendText(text);
            _detailsBox.SelectionStart = _detailsBox.TextLength;
            _detailsBox.ScrollToCaret();
        }

        private void SetWorking(bool working)
        {
            _working = working;
            _cancel.Enabled = !working;
            _primary.Enabled = !working;
            UseWaitCursor = working;
        }

        private void Finish()
        {
            try
            {
                if (!_options.Uninstall && _finishLaunchCheck.Visible && _finishLaunchCheck.Checked)
                {
                    string exe = Path.Combine(_installedDirectory ?? SetupInfo.ProgramFilesCleaner, SetupInfo.ExeName);
                    if (File.Exists(exe))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exe,
                            WorkingDirectory = Path.GetDirectoryName(exe),
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Launching the application failed: {0}", ex.Message);
            }

            Close();
        }

        private void OnCancel()
        {
            if (_working)
            {
                return;
            }

            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_working)
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        private sealed class WorkResult
        {
            public bool Ok;
            public string Error;
        }

        /// <summary>Renders the wizard into a PNG; used by --screenshot.</summary>
        public void CaptureScreenshot(string path)
        {
            using (Bitmap bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine("SETUP_FORM_SIZE {0}x{1}", Width, Height);
        }
    }

    /// <summary>Filled accent button used for the primary wizard action.</summary>
    internal sealed class FlatPrimary : Button
    {
        private bool _hover;

        public FlatPrimary()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = SetupTheme.Font(9.75F, FontStyle.Bold);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : SetupTheme.Background);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled
                ? Color.FromArgb(160, SetupTheme.Accent)
                : _hover ? SetupTheme.Mix(SetupTheme.Accent, Color.Black, 0.08f) : SetupTheme.Accent;

            using (GraphicsPath path = Rounded(bounds, 8))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(g, Text, Font, bounds, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
