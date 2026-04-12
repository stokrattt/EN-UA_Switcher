using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Switcher.App;

/// <summary>Creates a tray icon programmatically — no .ico file dependency.</summary>
internal static class TrayIconHelper
{
    public static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Background circle
        g.FillEllipse(new SolidBrush(Color.FromArgb(0, 120, 212)), 1, 1, 14, 14);

        // Letter "S" in white
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        g.DrawString("S", font, brush, 2f, 1f);

        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>Dark context menu renderer.</summary>
internal class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color BackColor = Color.FromArgb(45, 45, 48);
    private static readonly Color ForeColor = Color.FromArgb(224, 224, 224);
    private static readonly Color HoverColor = Color.FromArgb(63, 63, 70);

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        e.Graphics.FillRectangle(
            new SolidBrush(e.Item.Selected ? HoverColor : BackColor),
            e.Item.ContentRectangle);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.FillRectangle(new SolidBrush(BackColor), e.AffectedBounds);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ForeColor;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(new Pen(Color.FromArgb(85, 85, 88)),
            0, y, e.Item.Width, y);
    }
}
