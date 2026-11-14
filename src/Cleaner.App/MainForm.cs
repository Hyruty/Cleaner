using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cleaner
{
    /// <summary>Main window: disk overview, cleanup item list and the two actions.</summary>
    internal sealed class MainForm : Form
    {
        private readonly Settings _settings;
        private readonly bool _isAdmin;
        private readonly Options _options;
        private UserContext _user;
        private CleanupEngine _engine;

        private HeaderPanel _header;
        private CardPanel _diskCard;
        private Label _diskTitle;
        private Label _diskFree;
        private Label _diskUsage;
        private Label _diskSelected;
        private UsageBar _usageBar;

        private ListView _list;
        private ImageList _rowImages;
        private Dictionary<string, ListViewItem> _itemsById;

        private CardPanel _bottomCard;
        private CheckBox _autoCheck;
        private Label _autoNote;
        private FlatButton _cleanButton;
        private FlatButton _scanButton;
        private FlatButton _elevateButton;
        private FlatButton _cancelButton;

        private Panel _statusBar;
        private Label _statusLabel;
        private ProgressBar _progress;
        private FlatButton _moreButton;

        private CancellationTokenSource _cancellation;
        private bool _busy;
        private bool _suppressAutoCheck;
        private Dictionary<string, CategoryEstimate> _estimates = new Dictionary<string, CategoryEstimate>(StringComparer.OrdinalIgnoreCase);

        public bool SkipInitialScan { get; set; }

        public MainForm(Settings settings, bool isAdmin, Options options)
        {
            _settings = settings;
            _isAdmin = isAdmin;
            _options = options;

            _user = UserContext.Current(
                isAdmin,
                options == null ? null : options.TargetUserSid,
                options == null ? null : options.TargetLocalAppData,
                options == null ? null : options.TargetTemp);

            _engine = new CleanupEngine(_user, isAdmin);

            BuildUi();
            ApplySettings();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = Loc.IsChinese
                ? AppInfo.DisplayName + "  " + AppInfo.VersionText
                : AppInfo.EnglishName + "  " + AppInfo.VersionText;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.75F, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            MinimumSize = new Size(900, 660);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            Icon = LoadAppIcon();

            _header = new HeaderPanel
            {
                Dock = DockStyle.Top,
                Title = Loc.T("app.title"),
                Subtitle = Loc.T("app.subtitle"),
                VersionText = AppInfo.VersionText,
                BadgeText = _isAdmin ? Loc.T("badge.admin") : Loc.T("badge.user"),
                BadgeIsAdmin = _isAdmin,
                ProductIcon = LoadAppIconBitmap(46)
            };

            _statusBar = BuildStatusBar();

            Panel body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Background,
                Padding = new Padding(22, 16, 22, 6)
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));

            _diskCard = BuildDiskCard();
            _diskCard.Dock = DockStyle.Fill;
            _diskCard.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(_diskCard, 0, 0);

            CardPanel listCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(1),
                Radius = 10
            };
            _list = BuildList();
            listCard.Controls.Add(_list);
            layout.Controls.Add(listCard, 0, 1);

            _bottomCard = BuildBottomCard();
            _bottomCard.Dock = DockStyle.Fill;
            _bottomCard.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(_bottomCard, 0, 2);

            body.Controls.Add(layout);

            // Docking order matters: WinForms lays out the controls in reverse
            // add-order, so the fill panel is added first to keep the leftover
            // space after the header and the status bar take their strips.
            Controls.Add(body);
            Controls.Add(_statusBar);
            Controls.Add(_header);

            ResumeLayout(true);
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(AppInfo.ExePath);
            }
            catch
            {
                try
                {
                    return SystemIcons.Application;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static Image LoadAppIconBitmap(int size)
        {
            try
            {
                using (Icon icon = LoadAppIcon())
                {
                    if (icon == null)
                    {
                        return null;
                    }

                    using (Bitmap source = icon.ToBitmap())
                    {
                        Bitmap scaled = new Bitmap(size, size);
                        using (Graphics g = Graphics.FromImage(scaled))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.DrawImage(source, 0, 0, size, size);
                        }

                        return scaled;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private CardPanel BuildDiskCard()
        {
            CardPanel card = new CardPanel { Padding = new Padding(22, 14, 22, 12), Radius = 10 };

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Theme.Surface
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _diskTitle = new Label
            {
                Text = Loc.T("disk.title"),
                Font = Theme.Font(11.5F, FontStyle.Bold),
                ForeColor = Theme.Text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            _diskFree = new Label
            {
                Text = Loc.T("disk.loading"),
                Font = Theme.Font(11F, FontStyle.Bold),
                ForeColor = Theme.Accent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = false
            };

            _usageBar = new UsageBar
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 6)
            };

            _diskUsage = new Label
            {
                Text = string.Empty,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            _diskSelected = new Label
            {
                Text = Loc.T("disk.selectedNone"),
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = false
            };

            grid.Controls.Add(_diskTitle, 0, 0);
            grid.Controls.Add(_diskFree, 1, 0);

            Panel barHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 6), BackColor = Theme.Surface };
            _usageBar.Dock = DockStyle.Top;
            _usageBar.Height = 8;
            barHost.Controls.Add(_usageBar);
            grid.Controls.Add(barHost, 0, 1);
            grid.SetColumnSpan(barHost, 2);

            grid.Controls.Add(_diskUsage, 0, 2);
            grid.Controls.Add(_diskSelected, 1, 2);

            card.Controls.Add(grid);
            return card;
        }

        private ListView BuildList()
        {
            _rowImages = new ImageList { ImageSize = new Size(1, 28), ColorDepth = ColorDepth.Depth32Bit };

            ListView list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = false,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Font(9.75F, FontStyle.Regular),
                SmallImageList = _rowImages,
                UseCompatibleStateImageBehavior = false
            };

            list.Columns.Add(Loc.T("col.item"), 300, HorizontalAlignment.Left);
            list.Columns.Add(Loc.T("col.size"), 160, HorizontalAlignment.Right);
            list.Columns.Add(Loc.T("col.desc"), 440, HorizontalAlignment.Left);

            _itemsById = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
            foreach (CleanupCategory category in CleanupCatalog.All())
            {
                string name = category.Name;
                if (category.RequiresAdmin && !_isAdmin)
                {
                    name = "[" + Loc.T("badge.admin") + "] " + name;
                }

                ListViewItem item = new ListViewItem(name);
                item.SubItems.Add(Loc.T("list.notScanned"));
                item.SubItems.Add(category.Description);
                item.UseItemStyleForSubItems = false;
                item.SubItems[0].ForeColor = Theme.Text;
                item.SubItems[1].ForeColor = Theme.Text;
                item.SubItems[2].ForeColor = Theme.Text;
                item.Tag = category.Id;
                item.ToolTipText = category.Description + Environment.NewLine + category.Id;
                item.Checked = _settings.IsEnabled(category.Id);
                list.Items.Add(item);
                _itemsById[category.Id] = item;
            }

            list.ItemChecked += (sender, e) => UpdateSelectionSummary();
            list.Resize += (sender, e) => UpdateColumnWidths();

            _list = list;
            UpdateColumnWidths();
            return list;
        }

        /// <summary>
        /// Sizes the columns from the real, DPI scaled font: ListView column
        /// widths are not auto scaled by WinForms, so they are measured here.
        /// </summary>
        private void UpdateColumnWidths()
        {
            if (_list == null || _list.Columns.Count < 3)
            {
                return;
            }

            int itemWidth = TextRenderer.MeasureText(Loc.T("col.item"), _list.Font).Width;
            foreach (ListViewItem item in _list.Items)
            {
                itemWidth = Math.Max(itemWidth, TextRenderer.MeasureText(item.Text, _list.Font).Width);
            }

            itemWidth = Math.Max(200, Math.Min(itemWidth + 54, 470));

            int sizeWidth = TextRenderer.MeasureText("999.99 GB", _list.Font).Width;
            foreach (string key in new[] { "list.needsAdmin", "list.browser", "list.noCache", "list.recycleUnknown", "list.scanning" })
            {
                sizeWidth = Math.Max(sizeWidth, TextRenderer.MeasureText(Loc.T(key), _list.Font).Width);
            }

            sizeWidth = Math.Max(140, Math.Min(sizeWidth + 30, 260));

            _list.Columns[0].Width = itemWidth;
            _list.Columns[1].Width = sizeWidth;

            int description = _list.ClientSize.Width - itemWidth - sizeWidth - 4;
            _list.Columns[2].Width = Math.Max(150, description);
        }

        private CardPanel BuildBottomCard()
        {
            CardPanel card = new CardPanel { Padding = new Padding(22, 12, 18, 10), Radius = 10 };

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Theme.Surface
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 452f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _autoCheck = new CheckBox
            {
                Text = Loc.T("auto.label"),
                Font = Theme.Font(9.75F, FontStyle.Bold),
                ForeColor = Theme.Text,
                AutoSize = true,
                Dock = DockStyle.Left,
                BackColor = Theme.Surface,
                Margin = new Padding(0, 4, 0, 0)
            };

            _autoNote = new Label
            {
                Text = Loc.T("auto.note"),
                ForeColor = Theme.Muted,
                Font = Theme.Font(8.75F, FontStyle.Regular),
                AutoSize = false,
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                Margin = new Padding(2, 2, 8, 0)
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0, 4, 0, 0),
                Padding = new Padding(0)
            };

            _cleanButton = new FlatButton
            {
                Text = Loc.T("btn.clean"),
                Primary = true,
                Size = new Size(132, 42),
                Font = Theme.Font(10F, FontStyle.Bold),
                Margin = new Padding(8, 0, 0, 0)
            };

            _scanButton = new FlatButton
            {
                Text = Loc.T("btn.scan"),
                Primary = false,
                Size = new Size(112, 42),
                Margin = new Padding(8, 0, 0, 0)
            };

            _elevateButton = new FlatButton
            {
                Text = Loc.T("btn.elevate"),
                Primary = false,
                Size = new Size(152, 42),
                Visible = !_isAdmin,
                Margin = new Padding(8, 0, 0, 0)
            };

            _cancelButton = new FlatButton
            {
                Text = Loc.T("btn.cancel"),
                Primary = false,
                Size = new Size(96, 42),
                Visible = false,
                Margin = new Padding(8, 0, 0, 0)
            };

            buttons.Controls.Add(_cleanButton);
            buttons.Controls.Add(_scanButton);
            buttons.Controls.Add(_elevateButton);
            buttons.Controls.Add(_cancelButton);

            grid.Controls.Add(_autoCheck, 0, 0);
            grid.Controls.Add(_autoNote, 0, 1);
            grid.Controls.Add(buttons, 1, 0);
            grid.SetRowSpan(buttons, 2);

            card.Controls.Add(grid);

            _cleanButton.Click += async (sender, e) => await CleanAsync();
            _scanButton.Click += async (sender, e) => await ScanAsync();
            _cancelButton.Click += (sender, e) => CancelWork();
            _elevateButton.Click += (sender, e) => Elevate();
            _autoCheck.CheckedChanged += (sender, e) => ToggleAutoClean();

            return card;
        }

        private Panel BuildStatusBar()
        {
            Panel bar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                BackColor = Theme.Background,
                Padding = new Padding(24, 0, 22, 6)
            };

            _progress = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Height = 10,
                Width = 180,
                Maximum = 100,
                Visible = false
            };

            _moreButton = new FlatButton
            {
                Text = Loc.T("btn.more"),
                Primary = false,
                Size = new Size(78, 26),
                Font = Theme.Font(9F, FontStyle.Regular),
                Radius = 6
            };

            _statusLabel = new Label
            {
                Text = Loc.T("status.ready"),
                ForeColor = Theme.Muted,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0)
            };

            bar.Controls.Add(_statusLabel);
            bar.Controls.Add(_progress);
            bar.Controls.Add(_moreButton);
            bar.Resize += (sender, e) => LayoutStatusBar();
            _moreButton.Click += (sender, e) => ShowMoreMenu();

            return bar;
        }

        private void LayoutStatusBar()
        {
            if (_statusBar == null || _progress == null || _moreButton == null)
            {
                return;
            }

            int right = _statusBar.ClientSize.Width - _statusBar.Padding.Right;
            _moreButton.Location = new Point(right - _moreButton.Width, (_statusBar.ClientSize.Height - _moreButton.Height) / 2 - 3);
            _progress.Location = new Point(_moreButton.Left - _progress.Width - 14, (_statusBar.ClientSize.Height - _progress.Height) / 2 - 3);
            _statusLabel.Width = Math.Max(120, _progress.Left - _statusLabel.Left - 12);
        }

        private void ApplySettings()
        {
            SuspendLayout();

            int width = Math.Max(MinimumSize.Width, _settings.WindowWidth);
            int height = Math.Max(MinimumSize.Height, _settings.WindowHeight);
            ClientSize = new Size(width, height);

            if (_settings.WindowX != int.MinValue && _settings.WindowY != int.MinValue)
            {
                Rectangle wanted = new Rectangle(_settings.WindowX, _settings.WindowY, width, height);
                bool visible = Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(wanted));
                if (visible)
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(_settings.WindowX, _settings.WindowY);
                }
            }

            if (_settings.WindowMaximized)
            {
                WindowState = FormWindowState.Maximized;
            }

            _suppressAutoCheck = true;
            _autoCheck.Checked = _settings.AutoCleanEnabled;
            _suppressAutoCheck = false;

            ResumeLayout(true);
            LayoutStatusBar();
            UpdateColumnWidths();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ClampToWorkingArea();
            LayoutStatusBar();
            UpdateColumnWidths();
        }

        /// <summary>
        /// Keeps the default window inside the screen: with DPI auto scaling a
        /// 1000 px wide window can grow past a small display.
        /// </summary>
        private void ClampToWorkingArea()
        {
            try
            {
                Screen screen = Screen.FromControl(this);
                if (screen == null || WindowState != FormWindowState.Normal)
                {
                    return;
                }

                Rectangle area = screen.WorkingArea;
                int maxWidth = Math.Max(720, area.Width - 40);
                int maxHeight = Math.Max(520, area.Height - 60);

                Size minimum = new Size(Math.Min(MinimumSize.Width, maxWidth), Math.Min(MinimumSize.Height, maxHeight));
                if (MinimumSize != minimum)
                {
                    MinimumSize = minimum;
                }

                if (Width > maxWidth || Height > maxHeight)
                {
                    Size = new Size(Math.Min(Width, maxWidth), Math.Min(Height, maxHeight));
                }

                int left = Left;
                int top = Top;
                if (left + Width > area.Right)
                {
                    left = Math.Max(area.Left, area.Right - Width);
                }

                if (top + Height > area.Bottom)
                {
                    top = Math.Max(area.Top, area.Bottom - Height);
                }

                if (left < area.Left)
                {
                    left = area.Left;
                }

                if (top < area.Top)
                {
                    top = area.Top;
                }

                Location = new Point(left, top);
            }
            catch (Exception ex)
            {
                Log.Warn("Clamping the window to the screen failed: {0}", ex.Message);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Auto scaling is applied when the window is first shown, so clamp
            // once more to keep oversized DPI-scaled windows on screen.
            ClampToWorkingArea();
            LayoutStatusBar();
            UpdateColumnWidths();
            Log.Info("Main window shown: size={0}x{1}; client={2}x{3}; dpi={4}",
                Width, Height, ClientSize.Width, ClientSize.Height, DeviceDpi);

            UpdateDiskStatus();
            UpdateSelectionSummary();

            if (SkipInitialScan)
            {
                return;
            }

            BeginInvoke(new Action(async () =>
            {
                await VerifyAutoCleanStateAsync();
                await ScanAsync();
            }));
        }

        private async Task VerifyAutoCleanStateAsync()
        {
            if (!_settings.AutoCleanEnabled)
            {
                return;
            }

            bool registered = await Task.Run(() => AutoCleanTask.IsRegistered());
            if (registered)
            {
                return;
            }

            _suppressAutoCheck = true;
            _autoCheck.Checked = false;
            _suppressAutoCheck = false;
            _settings.AutoCleanEnabled = false;
            _settings.Save();
            SetStatus(Loc.T("status.ready"));
        }

        // ------------------------------------------------------------- disk
        private void UpdateDiskStatus()
        {
            DiskStatus disk = DiskInfo.SystemDrive();
            if (!disk.Available)
            {
                _diskFree.Text = Loc.T("disk.unavailable");
                _diskUsage.Text = string.Empty;
                _usageBar.Value = 0;
                return;
            }

            _usageBar.Value = disk.UsedPercent;
            _diskFree.Text = Loc.T("disk.free", Fmt.Bytes(disk.Free));
            _diskUsage.Text = Loc.T("disk.usage", Fmt.Bytes(disk.Used), Fmt.Bytes(disk.Total));
        }

        private void UpdateSelectionSummary()
        {
            long bytes = 0;
            int count = 0;

            foreach (ListViewItem item in _list.CheckedItems)
            {
                count++;
                CategoryEstimate estimate;
                if (_estimates.TryGetValue((string)item.Tag, out estimate) && estimate.State == CategoryState.Ok)
                {
                    bytes += estimate.Bytes;
                }
            }

            _diskSelected.Text = count == 0
                ? Loc.T("disk.selectedNone")
                : Loc.T("disk.selected", count, Fmt.Bytes(bytes));
        }

        // ------------------------------------------------------------- scan
        private async Task ScanAsync()
        {
            if (_busy)
            {
                return;
            }

            List<string> ids = CleanupCatalog.All().Select(c => c.Id).ToList();
            SetBusy(true, Loc.T("status.scanning", 0, ids.Count, string.Empty));

            foreach (ListViewItem item in _list.Items)
            {
                item.SubItems[1].Text = Loc.T("list.scanning");
                item.SubItems[1].ForeColor = Theme.Text;
                item.SubItems[0].ForeColor = Theme.Text;
            }

            _cancellation = new CancellationTokenSource();
            Progress<ScanProgress> progress = new Progress<ScanProgress>(report =>
            {
                ApplyEstimate(report.Estimate);
                string name = CleanupCatalog.ById(report.CurrentId) == null ? string.Empty : CleanupCatalog.ById(report.CurrentId).Name;
                SetStatus(Loc.T("status.scanning", report.Completed, report.Total, name));
                SetProgress(report.Total == 0 ? 0 : (int)(report.Completed * 100.0 / report.Total));
            });

            try
            {
                List<CategoryEstimate> estimates = await _engine.ScanAsync(ids, progress, _cancellation.Token);
                long total = 0;
                foreach (CategoryEstimate estimate in estimates)
                {
                    if (estimate.State == CategoryState.Ok)
                    {
                        total += estimate.Bytes;
                    }
                }

                SetStatus(Loc.T("status.scanDone", Fmt.Bytes(total)));
            }
            catch (OperationCanceledException)
            {
                SetStatus(Loc.T("status.ready"));
            }
            catch (Exception ex)
            {
                Log.Exception("Scan failed", ex);
                SetStatus(Loc.T("status.failed"));
                MessageBox.Show(this,
                    Loc.T("error.scan", ex.Message) + Loc.T("error.logHint", Log.CurrentFile ?? string.Empty),
                    Loc.T("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                SetProgress(0);
                UpdateSelectionSummary();
                UpdateDiskStatus();
            }
        }

        private void ApplyEstimate(CategoryEstimate estimate)
        {
            if (estimate == null)
            {
                return;
            }

            _estimates[estimate.Id] = estimate;

            ListViewItem item;
            if (!_itemsById.TryGetValue(estimate.Id, out item))
            {
                return;
            }

            CleanupCategory category = CleanupCatalog.ById(estimate.Id);
            string sizeText;
            Color color = Theme.Text;

            switch (estimate.State)
            {
                case CategoryState.NeedsAdmin:
                    sizeText = Loc.T("list.needsAdmin");
                    color = Theme.Muted;
                    break;
                case CategoryState.BrowserRunning:
                    sizeText = Loc.T("list.browser");
                    color = Theme.Warning;
                    break;
                case CategoryState.NoTargets:
                    sizeText = Loc.T("list.noCache");
                    color = Theme.Muted;
                    break;
                case CategoryState.Error:
                    sizeText = Loc.T("list.noCache");
                    color = Theme.Danger;
                    break;
                default:
                    if (category != null && category.IsRecycleBin)
                    {
                        sizeText = estimate.Items >= 0
                            ? Loc.T("list.recycle", estimate.Items)
                            : Loc.T("list.recycleUnknown");
                    }
                    else
                    {
                        sizeText = estimate.Bytes > 0 ? Fmt.Bytes(estimate.Bytes) : Loc.T("list.noCache");
                    }

                    color = estimate.Bytes > 0 ? Theme.Text : Theme.Muted;
                    break;
            }

            item.SubItems[1].Text = sizeText;
            item.SubItems[1].ForeColor = color;
            item.SubItems[0].ForeColor = Theme.Text;
        }

        // ------------------------------------------------------------ clean
        private async Task CleanAsync()
        {
            if (_busy)
            {
                return;
            }

            List<string> ids = _list.CheckedItems.Cast<ListViewItem>()
                .Select(item => (string)item.Tag)
                .ToList();

            if (ids.Count == 0)
            {
                MessageBox.Show(this, Loc.T("disk.selectedNone"), Loc.T("confirm.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            long estimatedBytes = 0;
            List<string> names = new List<string>();
            foreach (string id in ids)
            {
                CleanupCategory category = CleanupCatalog.ById(id);
                if (category != null)
                {
                    names.Add("· " + category.Name);
                }

                CategoryEstimate estimate;
                if (_estimates.TryGetValue(id, out estimate) && estimate.State == CategoryState.Ok)
                {
                    estimatedBytes += estimate.Bytes;
                }
            }

            string details = string.Join(Environment.NewLine, names);
            if (ids.Contains("recycleBin", StringComparer.OrdinalIgnoreCase))
            {
                details += Environment.NewLine + Environment.NewLine + Loc.T("confirm.recycle");
            }

            DialogResult answer = MessageBox.Show(
                this,
                Loc.T("confirm.body", ids.Count, Fmt.Bytes(estimatedBytes), details),
                Loc.T("confirm.title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true, Loc.T("status.cleaning", string.Empty));
            _cancellation = new CancellationTokenSource();

            Progress<CleanupProgress> progress = new Progress<CleanupProgress>(report =>
            {
                CleanupCategory category = CleanupCatalog.ById(report.CurrentId);
                if (category != null)
                {
                    SetStatus(Loc.T("status.cleaning", category.Name));
                }

                SetProgress(report.Total == 0 ? 0 : (int)(report.Completed * 100.0 / report.Total));
            });

            try
            {
                CleanupResult result = await _engine.CleanAsync(ids, progress, _cancellation.Token);
                SetStatus(Loc.T("status.cleanDone", Fmt.Bytes(result.Bytes)));
                UpdateDiskStatus();
                ShowResult(result);
            }
            catch (OperationCanceledException)
            {
                SetStatus(Loc.T("status.ready"));
            }
            catch (Exception ex)
            {
                Log.Exception("Cleanup failed", ex);
                SetStatus(Loc.T("status.failed"));
                MessageBox.Show(this,
                    Loc.T("result.failed", ex.Message) + Loc.T("error.logHint", Log.CurrentFile ?? string.Empty),
                    Loc.T("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                SetProgress(0);
            }

            await ScanAsync();
        }

        private void ShowResult(CleanupResult result)
        {
            using (ResultDialog dialog = new ResultDialog(result))
            {
                dialog.ShowDialog(this);
            }
        }

        private void CancelWork()
        {
            if (_cancellation != null && !_cancellation.IsCancellationRequested)
            {
                SetStatus(Loc.T("status.cancelling"));
                _cancellation.Cancel();
            }
        }

        private void SetBusy(bool busy, string status = null)
        {
            _busy = busy;
            _cleanButton.Enabled = !busy;
            _scanButton.Enabled = !busy;
            _elevateButton.Enabled = !busy;
            _autoCheck.Enabled = !busy;
            _cancelButton.Visible = busy;
            _progress.Visible = busy;
            UseWaitCursor = busy;
            if (!string.IsNullOrEmpty(status))
            {
                SetStatus(status);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = text ?? string.Empty;
            }
        }

        private void SetProgress(int percent)
        {
            if (_progress == null)
            {
                return;
            }

            _progress.Value = Math.Max(0, Math.Min(100, percent));
        }

        // ------------------------------------------------------- auto clean
        private void ToggleAutoClean()
        {
            if (_suppressAutoCheck)
            {
                return;
            }

            if (_autoCheck.Checked)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    Loc.T("auto.enableConfirm", AppInfo.TaskName),
                    Loc.T("auto.enableTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer != DialogResult.Yes)
                {
                    SetAutoCheckSilently(false);
                    return;
                }

                string error;
                if (!AutoCleanTask.Install(out error))
                {
                    SetAutoCheckSilently(false);
                    MessageBox.Show(this,
                        Loc.T("auto.failed", error ?? string.Empty),
                        Loc.T("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _settings.AutoCleanEnabled = true;
                _settings.Save();
                SetStatus(Loc.T("auto.enabled"));
                return;
            }

            DialogResult disableAnswer = MessageBox.Show(
                this,
                Loc.T("auto.disableConfirm", AppInfo.TaskName),
                Loc.T("auto.enableTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (disableAnswer != DialogResult.Yes)
            {
                SetAutoCheckSilently(true);
                return;
            }

            string uninstallError;
            if (!AutoCleanTask.Uninstall(out uninstallError))
            {
                SetAutoCheckSilently(true);
                MessageBox.Show(this,
                    Loc.T("auto.failed", uninstallError ?? string.Empty),
                    Loc.T("error.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _settings.AutoCleanEnabled = false;
            _settings.Save();
            SetStatus(Loc.T("auto.disabled"));
        }

        private void SetAutoCheckSilently(bool value)
        {
            _suppressAutoCheck = true;
            _autoCheck.Checked = value;
            _suppressAutoCheck = false;
        }

        // --------------------------------------------------------- elevation
        private void Elevate()
        {
            if (_isAdmin)
            {
                MessageBox.Show(this, Loc.T("elevate.running"), Loc.T("badge.admin"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(
                this,
                Loc.T("elevate.confirm"),
                Loc.T("btn.elevate"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            List<string> arguments = new List<string>();
            if (_options != null && _options.OriginalArgs != null)
            {
                arguments.AddRange(_options.OriginalArgs);
            }

            if (_user != null)
            {
                arguments.Add("--target-user-sid");
                arguments.Add(_user.Sid ?? string.Empty);
                arguments.Add("--target-localappdata");
                arguments.Add(_user.LocalAppData ?? string.Empty);
                arguments.Add("--target-temp");
                arguments.Add(_user.Temp ?? string.Empty);
            }

            // Tells the elevated process that it replaces this window instead of
            // being treated as a duplicate launch.
            arguments.Add("--handoff");

            SaveSelection();
            string error;
            if (Elevation.TryStartElevated(arguments, out error))
            {
                Log.Info("Started the elevated instance; closing this window.");
                if (Elevating != null)
                {
                    Elevating(this, EventArgs.Empty);
                }

                Close();
                return;
            }

            MessageBox.Show(this, Loc.T("elevate.failed", error ?? string.Empty), Loc.T("error.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>Raised right before the window closes for an elevated restart.</summary>
        public event EventHandler Elevating;

        // -------------------------------------------------------------- menu
        private void ShowMoreMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip
            {
                Font = Theme.Font(9.5F, FontStyle.Regular),
                ShowImageMargin = false
            };

            ToolStripMenuItem logItem = new ToolStripMenuItem(Loc.T("menu.logs"));
            logItem.Click += (sender, e) => OpenFolder(AppInfo.LogFolder);
            menu.Items.Add(logItem);

            ToolStripMenuItem dataItem = new ToolStripMenuItem(Loc.T("menu.data"));
            dataItem.Click += (sender, e) => OpenFolder(AppInfo.DataRoot);
            menu.Items.Add(dataItem);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem chinese = new ToolStripMenuItem("简体中文") { Checked = Loc.IsChinese };
            chinese.Click += (sender, e) => ChangeLanguage("zh-CN");
            ToolStripMenuItem english = new ToolStripMenuItem("English") { Checked = !Loc.IsChinese };
            english.Click += (sender, e) => ChangeLanguage("en-US");

            ToolStripMenuItem language = new ToolStripMenuItem(Loc.T("menu.language"));
            language.DropDownItems.Add(chinese);
            language.DropDownItems.Add(english);
            menu.Items.Add(language);

            ToolStripMenuItem about = new ToolStripMenuItem(Loc.T("menu.about"));
            about.Click += (sender, e) => ShowAbout();
            menu.Items.Add(about);

            menu.Closed += (sender, e) => menu.Dispose();

            Rectangle screenPoint = _moreButton.RectangleToScreen(_moreButton.ClientRectangle);
            menu.Show(screenPoint.Left, screenPoint.Top - menu.GetPreferredSize(Size.Empty).Height - 6);
        }

        private void ChangeLanguage(string language)
        {
            if (string.Equals(_settings.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.Language = language;
            _settings.Save();
            Application.Restart();
            Close();
        }

        private void OpenFolder(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", "\"" + folder + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Opening folder failed: {0}", ex.Message);
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                this,
                Loc.T("about.text", AppInfo.VersionText, AppInfo.DataRoot),
                Loc.T("about.title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------ closing
        private void SaveSelection()
        {
            foreach (ListViewItem item in _list.Items)
            {
                _settings.SetEnabled((string)item.Tag, item.Checked);
            }

            _settings.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                CancelWork();
                SaveSelection();

                FormWindowState state = WindowState;
                if (state == FormWindowState.Normal)
                {
                    // Store the client size so restoring never grows the window
                    // by the border width on every launch.
                    _settings.WindowWidth = ClientSize.Width;
                    _settings.WindowHeight = ClientSize.Height;
                    _settings.WindowX = Location.X;
                    _settings.WindowY = Location.Y;
                    _settings.WindowMaximized = false;
                }
                else
                {
                    _settings.WindowMaximized = state == FormWindowState.Maximized;
                }

                _settings.Save();
            }
            catch (Exception ex)
            {
                Log.Exception("Saving window state failed", ex);
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }

                if (_rowImages != null)
                {
                    _rowImages.Dispose();
                    _rowImages = null;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>Renders the current window into a bitmap; used by --screenshot.</summary>
        public void CaptureScreenshot(string path)
        {
            using (Bitmap bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine("FORM_SIZE {0}x{1} client={2}x{3} dpi={4}",
                Width, Height, ClientSize.Width, ClientSize.Height, DeviceDpi);
        }
    }
}
