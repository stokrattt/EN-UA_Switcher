using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Switcher.App;

internal static class TrayIconHelper
{
    public static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var backgroundPath = CreateRoundedRectangle(new RectangleF(4, 4, 24, 24), 8f);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(20, 28, 39));
        using var borderPen = new Pen(Color.FromArgb(73, 95, 127), 1.2f);

        g.FillPath(backgroundBrush, backgroundPath);
        g.DrawPath(borderPen, backgroundPath);

        using var dividerPen = new Pen(Color.FromArgb(54, 68, 92), 1f);
        g.DrawLine(dividerPen, 16, 8, 16, 24);

        using var leftBrush = new SolidBrush(Color.FromArgb(110, 159, 255));
        using var rightBrush = new SolidBrush(Color.FromArgb(113, 209, 132));
        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var stringFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString("A", font, leftBrush, new RectangleF(5.5f, 7f, 10f, 14f), stringFormat);
        g.DrawString("Я", font, rightBrush, new RectangleF(16.5f, 7f, 10f, 14f), stringFormat);

        using var arrowPen = new Pen(Color.FromArgb(236, 241, 248), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        g.DrawLine(arrowPen, 11, 22, 21, 22);
        g.DrawLine(arrowPen, 18, 19, 21, 22);
        g.DrawLine(arrowPen, 18, 25, 21, 22);

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF rect, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

internal sealed class MaterialMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color BackColor = Color.FromArgb(36, 33, 33);
    private static readonly Color BorderColor = Color.FromArgb(56, 52, 52);
    private static readonly Color HoverColor = Color.FromArgb(48, 44, 44);
    private static readonly Color AccentColor = Color.FromArgb(91, 140, 255);
    private static readonly Color ForeColor = Color.FromArgb(244, 247, 251);
    private static readonly Color MutedColor = Color.FromArgb(158, 173, 197);

    public MaterialMenuRenderer() : base(new MaterialMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = CreateRoundedRectangle(bounds, 12);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(BorderColor);
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var path = CreateRoundedRectangle(bounds, 12);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        Rectangle bounds = new(6, 2, e.Item.Width - 12, e.Item.Height - 4);
        using var brush = new SolidBrush(e.Item.Selected ? HoverColor : BackColor);
        using var path = CreateRoundedRectangle(bounds, 8);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        string text = e.Text ?? string.Empty;
        Font font = e.TextFont ?? Control.DefaultFont!;
        using var brush = new SolidBrush(e.Item.Enabled ? ForeColor : MutedColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        Rectangle textBounds = new(18, 0, e.Item.Width - 42, e.Item.Height);
        e.Graphics.DrawString(text, font!, brush, textBounds, format);

        if (string.Equals(e.Item.Tag as string, "chevron", StringComparison.Ordinal))
        {
            using var chevronPen = new Pen(MutedColor, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            int midY = e.Item.Height / 2;
            int x = e.Item.Width - 18;
            e.Graphics.DrawLine(chevronPen, x - 4, midY - 4, x, midY);
            e.Graphics.DrawLine(chevronPen, x, midY, x - 4, midY + 4);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        Rectangle badgeRect = new(14, (e.Item.Height - 16) / 2, 16, 16);
        using var badgeBrush = new SolidBrush(AccentColor);
        using var badgePath = CreateRoundedRectangle(badgeRect, 6);
        using var pen = new Pen(Color.White, 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        e.Graphics.FillPath(badgeBrush, badgePath);
        e.Graphics.DrawLine(pen, badgeRect.Left + 4, badgeRect.Top + 8, badgeRect.Left + 7, badgeRect.Top + 11);
        e.Graphics.DrawLine(pen, badgeRect.Left + 7, badgeRect.Top + 11, badgeRect.Right - 4, badgeRect.Top + 5);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Color.FromArgb(46, 43, 43));
        int y = e.Item.Bounds.Top + (e.Item.Height / 2);
        e.Graphics.DrawLine(pen, 16, y, e.Item.Width - 16, y);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private sealed class MaterialMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => BackColor;
        public override Color ImageMarginGradientBegin => BackColor;
        public override Color ImageMarginGradientMiddle => BackColor;
        public override Color ImageMarginGradientEnd => BackColor;
        public override Color MenuBorder => BorderColor;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => HoverColor;
        public override Color MenuItemSelectedGradientBegin => HoverColor;
        public override Color MenuItemSelectedGradientEnd => HoverColor;
        public override Color MenuItemPressedGradientBegin => BackColor;
        public override Color MenuItemPressedGradientMiddle => BackColor;
        public override Color MenuItemPressedGradientEnd => BackColor;
        public override Color CheckBackground => AccentColor;
        public override Color CheckSelectedBackground => AccentColor;
        public override Color CheckPressedBackground => AccentColor;
        public override Color SeparatorDark => BorderColor;
        public override Color SeparatorLight => BorderColor;
    }
}
