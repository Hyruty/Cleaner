using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Cleaner
{
    /// <summary>Compact "cleanup finished" dialog with the numbers that matter.</summary>
    internal sealed class ResultDialog : Form
    {
        private readonly CleanupResult _result;

        public ResultDialog(CleanupResult result)
        {
            _result = result ?? new CleanupResult();
            BuildUi();
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = Loc.T("result.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.75F, FontStyle.Regular);

            // Without the design-time DPI the form keeps its pixel size while the
            // fonts grow with the monitor DPI, which squeezes every row together.
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(560, 438);
            MinimumSize = new Size(520, 400);

            HeaderPanel header = new HeaderPanel
            {
                Dock = DockStyle.Top,
                Height = 84,
                Title = Loc.T("result.title"),
                Subtitle = Fmt.Duration(_result.Duration),
                VersionText = string.Empty,
                BadgeText = AppInfo.VersionText,
                BadgeIsAdmin = false
            };

            Panel body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Background,
                Padding = new Padding(26, 16, 26, 12)
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));

            Label freed = new Label
            {
                Text = Fmt.Bytes(_result.Bytes),
                Font = Theme.Font(28F, FontStyle.Bold),
                ForeColor = Theme.Accent,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };

            Label caption = new Label
            {
                Text = Loc.T("result.summary", Fmt.Bytes(_result.Bytes), _result.Files, _result.Folders),
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Margin = new Padding(2, 0, 0, 0)
            };

            List<string> lines = new List<string>();
            if (_result.RecycleBinCleared)
            {
                lines.Add(_result.RecycleBefore > 0
                    ? Loc.T("result.recycle", _result.RecycleBefore)
                    : Loc.T("result.recycleAlready"));
            }
            else if (!string.IsNullOrEmpty(_result.RecycleError))
            {
                lines.Add(Loc.T("result.recycleFailed"));
            }

            if (_result.Skipped > 0)
            {
                lines.Add(Loc.T("result.skipped", _result.Skipped));
            }

            if (_result.Errors > 0)
            {
                lines.Add(Loc.T("result.errors", _result.Errors));
            }

            if (_result.SkippedCategories.Count > 0)
            {
                lines.Add(Loc.T("result.skippedCategories", string.Join("，", _result.SkippedCategories)));
            }

            if (lines.Count == 0)
            {
                lines.Add(Loc.T("result.nothing"));
            }

            Label details = new Label
            {
                Text = string.Join(Environment.NewLine, lines),
                ForeColor = Theme.Text,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Margin = new Padding(2, 6, 2, 0)
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Theme.Background,
                Margin = new Padding(0, 8, 0, 0),
                Padding = new Padding(0)
            };

            FlatButton close = new FlatButton
            {
                Text = Loc.T("btn.ok"),
                Primary = true,
                Size = new Size(104, 38),
                Margin = new Padding(10, 0, 0, 0)
            };
            close.Click += (sender, e) => Close();

            FlatButton log = new FlatButton
            {
                Text = Loc.T("result.openLog"),
                Primary = false,
                Size = new Size(140, 38),
                Margin = new Padding(10, 0, 0, 0)
            };
            log.Click += (sender, e) => OpenLogFolder();

            buttons.Controls.Add(close);
            buttons.Controls.Add(log);

            layout.Controls.Add(freed, 0, 0);
            layout.Controls.Add(caption, 0, 1);
            layout.Controls.Add(details, 0, 2);
            layout.Controls.Add(buttons, 0, 3);

            body.Controls.Add(layout);

            // Docking order: the fill panel goes in first so the header keeps its strip.
            Controls.Add(body);
            Controls.Add(header);

            AcceptButton = close;
            CancelButton = close;

            ResumeLayout(true);
        }

        private void OpenLogFolder()
        {
            try
            {
                string folder = AppInfo.LogFolder;
                if (Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", "\"" + folder + "\"");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Opening the log folder failed: {0}", ex.Message);
            }
        }

        /// <summary>Renders the dialog into a PNG; used by --screenshot-result.</summary>
        public void CaptureScreenshot(string path)
        {
            using (Bitmap bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine("RESULT_DIALOG_SIZE {0}x{1} client={2}x{3} dpi={4}",
                Width, Height, ClientSize.Width, ClientSize.Height, DeviceDpi);
        }
    }
}
