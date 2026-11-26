using CanHost.Models;

namespace CanHost.Controls;

public sealed class FrameTickerControl : Control
{
    private static Color BackgroundColor => AppTheme.Field;
    private static Color BorderColor => AppTheme.Border;
    private static Color TextColor => AppTheme.Rx;

    private readonly Font _font;
    private readonly Font _labelFont;
    private string _message = "等待接收 CAN 帧…";

    public FrameTickerControl()
    {
        DoubleBuffered = true;
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 8.8F);
        _font = new Font("Cascadia Mono", 9F, FontStyle.Bold);
        _labelFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    public void ShowFrame(CanFrame frame)
    {
        var data = frame.Remote ? "(远程帧)" : frame.DataText;
        _message =
            $"{frame.Timestamp:HH:mm:ss.fff}   {frame.TypeText}   ID {frame.IdText}   " +
            $"DLC {frame.Dlc}   {data}";
        Invalidate();
    }

    public void ClearMessage()
    {
        _message = "等待接收 CAN 帧…";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.Clear(BackgroundColor);

        using var borderPen = new Pen(BorderColor);
        graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        const string label = "最新接收";
        var labelSize = TextRenderer.MeasureText(label, _labelFont);
        var labelRect = new Rectangle(8, 0, labelSize.Width + 14, Height);
        using var labelBackground = new SolidBrush(AppTheme.HighlightSurface);
        graphics.FillRectangle(labelBackground, labelRect);
        TextRenderer.DrawText(
            graphics,
            label,
            _labelFont,
            labelRect,
            AppTheme.Success,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);

        var textLeft = labelRect.Right + 8;
        graphics.SetClip(new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height));
        TextRenderer.DrawText(
            graphics,
            _message,
            _font,
            new Rectangle(textLeft + 8, 0, Math.Max(0, Width - textLeft - 16), Height),
            TextColor,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
        graphics.ResetClip();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
