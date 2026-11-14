using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Cleaner
{
    /// <summary>Palette, drawing helpers and the small custom controls used by the UI.</summary>
    internal static class Theme
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#F2F5F7");
        public static readonly Color Surface = Color.White;
        public static readonly Color Text = ColorTranslator.FromHtml("#17222B");
        public static readonly Color Muted = ColorTranslator.FromHtml("#5E6E7C");
        public static readonly Color Border = ColorTranslator.FromHtml("#DFE6EB");
        public static readonly Color Accent = ColorTranslator.FromHtml("#0F766E");
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#0B5F59");
        public static readonly Color AccentLight = ColorTranslator.FromHtml("#E7F4F1");
        public static readonly Color HeaderStart = ColorTranslator.FromHtml("#17938C");
        public static readonly Color HeaderEnd = ColorTranslator.FromHtml("#10516A");
        public static readonly Color Warning = ColorTranslator.FromHtml("#B45309");
        public static readonly Color Danger = ColorTranslator.FromHtml("#B91C1C");
        public static readonly Color Success = ColorTranslator.FromHtml("#15803D");
        public static readonly Color Track = ColorTranslator.FromHtml("#E8EEF1");

        private static string _family;

        public static string FontFamilyName
        {
            get
            {
                if (_family == null)
                {
                    _family = "Segoe UI";
                    try
                    {
                        using (Font test = new Font("Segoe UI", 9F))
                        {
                            if (!string.Equals(test.FontFamily.Name, "Segoe UI", StringComparison.OrdinalIgnoreCase))
                            {
                                _family = SystemFonts.MessageBoxFont.FontFamily.Name;
                            }
                        }
                    }
                    catch
                    {
                        _family = SystemFonts.MessageBoxFont.FontFamily.Name;
                    }
                }

                return _family;
            }
        }

        public static Font Font(float size, FontStyle style)
        {
            try
            {
                return new Font(FontFamilyName, size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
            }
        }

        public static Color Mix(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }

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

        public static void EnableDoubleBuffer(Control control)
        {
            if (control == null)
            {
                return;
            }

            typeof(Control)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(control, true, null);
        }
    }

    /// <summary>White rounded card, painted on top of the window background.</summary>
    internal class CardPanel : Panel
    {
        public int Radius = 10;
        public bool ShowBorder = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color outer = Parent != null ? Parent.BackColor : Theme.Background;
            e.Graphics.Clear(outer);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundedRect(bounds, Radius))
            {
                using (SolidBrush brush = new SolidBrush(Theme.Surface))
                {
                    e.Graphics.FillPath(brush, path);
                }

                if (ShowBorder)
                {
                    using (Pen pen = new Pen(Theme.Border))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }
    }

    /// <summary>Rounded button that supports a primary (filled) and secondary style.</summary>
    internal sealed class FlatButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public bool Primary { get; set; }
        public int Radius { get; set; }

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.Font(9.75F, FontStyle.Regular);
            Cursor = Cursors.Hand;
            Radius = 8;
            TabStop = true;
            UseVisualStyleBackColor = false;
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
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }

            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color outer = Parent != null ? Parent.BackColor : Theme.Background;
            g.Clear(outer);

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill;
            Color textColor;

            if (Primary)
            {
                fill = !Enabled
                    ? Theme.Mix(Theme.Accent, Color.White, 0.55f)
                    : _pressed ? Theme.AccentDark : _hover ? Theme.Mix(Theme.Accent, Color.Black, 0.06f) : Theme.Accent;
                textColor = Color.White;
            }
            else
            {
                fill = !Enabled
                    ? Theme.Surface
                    : _pressed ? Theme.Mix(Theme.AccentLight, Theme.Accent, 0.12f) : _hover ? Theme.AccentLight : Theme.Surface;
                textColor = !Enabled ? Theme.Mix(Theme.Muted, Color.White, 0.45f) : Theme.Text;
            }

            using (GraphicsPath path = Theme.RoundedRect(bounds, Radius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (!Primary)
                {
                    using (Pen pen = new Pen(Enabled ? Theme.Border : Theme.Mix(Theme.Border, Color.White, 0.4f)))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                Rectangle focus = new Rectangle(4, 4, Width - 9, Height - 9);
                ControlPaint.DrawFocusRectangle(g, focus, textColor, fill);
            }
        }
    }

    /// <summary>Slim progress bar with a rounded track, used for disk usage.</summary>
    internal sealed class UsageBar : Control
    {
        private int _value;

        public UsageBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 8;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (clamped != _value)
                {
                    _value = clamped;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color outer = Parent != null ? Parent.BackColor : Theme.Surface;
            g.Clear(outer);

            Rectangle track = new Rectangle(0, 0, Width, Height);
            using (GraphicsPath path = Theme.RoundedRect(track, Height / 2))
            using (SolidBrush brush = new SolidBrush(Theme.Track))
            {
                g.FillPath(brush, path);
            }

            if (_value <= 0)
            {
                return;
            }

            int width = Math.Max(Height, (int)Math.Round(Width * (_value / 100.0)));
            Rectangle fillRect = new Rectangle(0, 0, Math.Min(width, Width), Height);
            Color from = _value >= 90 ? Theme.Danger : _value >= 75 ? Theme.Warning : Theme.Accent;
            Color to = Theme.Mix(from, Color.White, 0.35f);

            using (GraphicsPath path = Theme.RoundedRect(fillRect, Height / 2))
            using (LinearGradientBrush brush = new LinearGradientBrush(fillRect, from, to, LinearGradientMode.Horizontal))
            {
                g.FillPath(brush, path);
            }
        }
    }

    /// <summary>Header band with gradient, product icon, title and status badges.</summary>
    internal sealed class HeaderPanel : Control
    {
        private Image _icon;

        public string Title = string.Empty;
        public string Subtitle = string.Empty;
        public string BadgeText = string.Empty;
        public bool BadgeIsAdmin;
        public string VersionText = string.Empty;

        public HeaderPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 100;
        }

        public Image ProductIcon
        {
            get { return _icon; }
            set
            {
                _icon = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            // The designer geometry below assumes a 100 px tall band. Scale it
            // with the real control height so high DPI layouts stay aligned.
            float scale = Math.Max(0.6f, Math.Min(3f, Height / 100f));

            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, bounds.Width, bounds.Height),
                Theme.HeaderStart,
                Theme.HeaderEnd,
                LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, bounds);
            }

            // subtle light streak
            using (LinearGradientBrush glow = new LinearGradientBrush(
                new Rectangle(0, -(int)(bounds.Height / 2f), Math.Max(1, bounds.Width), bounds.Height),
                Color.FromArgb(38, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(glow, bounds);
            }

            int iconSize = (int)(46 * scale);
            int left = (int)(26 * scale);
            if (_icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_icon, new Rectangle(left, (bounds.Height - iconSize) / 2, iconSize, iconSize));
                left += iconSize + (int)(14 * scale);
            }

            int textWidth = Math.Max(120, bounds.Width - left - (int)(230 * scale));
            using (Font titleFont = Theme.Font(20F, FontStyle.Bold))
            using (Font subtitleFont = Theme.Font(9.5F, FontStyle.Regular))
            {
                TextRenderer.DrawText(
                    g,
                    Title,
                    titleFont,
                    new Rectangle(left, (int)(18 * scale), textWidth, (int)(38 * scale)),
                    Color.White,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(
                    g,
                    Subtitle,
                    subtitleFont,
                    new Rectangle(left + 1, (int)(60 * scale), textWidth, (int)(24 * scale)),
                    Color.FromArgb(214, 236, 238),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            }

            // badges on the right
            using (Font badgeFont = Theme.Font(8.5F, FontStyle.Bold))
            using (Font versionFont = Theme.Font(8.5F, FontStyle.Regular))
            {
                Size badgeSize = TextRenderer.MeasureText(BadgeText, badgeFont);
                int badgeWidth = badgeSize.Width + (int)(20 * scale);
                int badgeHeight = (int)(24 * scale);
                int badgeX = bounds.Width - badgeWidth - (int)(24 * scale);
                Rectangle badge = new Rectangle(badgeX, (int)(20 * scale), badgeWidth, badgeHeight);

                Color badgeFill = BadgeIsAdmin
                    ? Color.FromArgb(64, 255, 255, 255)
                    : Color.FromArgb(38, 255, 255, 255);

                using (GraphicsPath path = Theme.RoundedRect(badge, badgeHeight / 2))
                {
                    using (SolidBrush brush = new SolidBrush(badgeFill))
                    {
                        g.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                    {
                        g.DrawPath(pen, path);
                    }
                }

                TextRenderer.DrawText(g, BadgeText, badgeFont, badge,
                    Color.FromArgb(240, 250, 250),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                TextRenderer.DrawText(
                    g,
                    VersionText,
                    versionFont,
                    new Rectangle(bounds.Width - (int)(210 * scale), (int)(52 * scale), (int)(186 * scale), (int)(20 * scale)),
                    Color.FromArgb(190, 222, 226),
                    TextFormatFlags.Right | TextFormatFlags.Top);
            }
        }
    }
}
