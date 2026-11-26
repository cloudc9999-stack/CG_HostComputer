using System.Drawing.Drawing2D;

namespace CanHost.Controls;

public enum ChartZoomAxis
{
    None,
    Vertical,
    Horizontal
}

public sealed class LineChartControl : Control
{
    private static Color BackgroundColor => AppTheme.Field;
    private static Color GridColor => AppTheme.GridLine;
    private static Color AxisColor => AppTheme.Border;
    private static Color TextColor => AppTheme.Muted;
    private static Color MutedColor => AppTheme.Hint;
    private static Color LineColor => AppTheme.Accent;
    private static Color PointColor => AppTheme.Success;

    private readonly List<PlotPoint> _points = [];
    private readonly Font _emptyFont;
    private readonly Font _axisFont;
    private readonly Font _valueFont;
    private RectangleF _plotBounds;
    private Point? _selectionStart;
    private Point _selectionCurrent;
    private double? _customViewStartTime;
    private double? _customViewEndTime;
    private double _configuredYMinimum;
    private double _configuredYMaximum = 4_095;
    private int _configuredTimeWindowSeconds = 30;

    public LineChartControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 8.5F);
        _emptyFont = new Font(Font.FontFamily, 10F);
        _axisFont = new Font(Font.FontFamily, 8F);
        _valueFont = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        MinimumSize = new Size(120, 40);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    public event EventHandler? ChartChanged;
    public event EventHandler? ZoomSelectionCompleted;

    public int PointCount => _points.Count;
    public double YMinimum { get; private set; }
    public double YMaximum { get; private set; } = 4_095;
    public int TimeWindowSeconds { get; private set; } = 30;
    public double ConfiguredYMinimum => _configuredYMinimum;
    public double ConfiguredYMaximum => _configuredYMaximum;
    public int ConfiguredTimeWindowSeconds => _configuredTimeWindowSeconds;
    public ChartZoomAxis ActiveZoomAxis { get; private set; }

    public void AddPoint(double timeSeconds, double value)
    {
        _points.Add(new PlotPoint(timeSeconds, value));
        if (_points.Count > 5000)
        {
            _points.RemoveRange(0, _points.Count - 5000);
        }

        Invalidate();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearPoints()
    {
        _points.Clear();
        _customViewStartTime = null;
        _customViewEndTime = null;
        SetZoomSelection(ChartZoomAxis.None);
        Invalidate();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ConfigureAxes(double minimum, double maximum, int timeWindowSeconds)
    {
        if (minimum >= maximum)
        {
            throw new ArgumentException("纵坐标上限必须大于下限。");
        }
        if (timeWindowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeWindowSeconds));
        }

        YMinimum = minimum;
        YMaximum = maximum;
        TimeWindowSeconds = timeWindowSeconds;
        _configuredYMinimum = minimum;
        _configuredYMaximum = maximum;
        _configuredTimeWindowSeconds = timeWindowSeconds;
        _customViewStartTime = null;
        _customViewEndTime = null;
        SetZoomSelection(ChartZoomAxis.None);
        Invalidate();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomYIn()
    {
        SetZoomSelection(ChartZoomAxis.None);
        var center = (YMinimum + YMaximum) / 2;
        var halfSpan = (YMaximum - YMinimum) * 0.25;
        if (halfSpan < 0.5)
        {
            return;
        }

        YMinimum = center - halfSpan;
        YMaximum = center + halfSpan;
        Invalidate();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomXIn()
    {
        SetZoomSelection(ChartZoomAxis.None);
        var nextWindow = Math.Max(5, (int)Math.Round(TimeWindowSeconds * 0.5));
        if (nextWindow == TimeWindowSeconds)
        {
            return;
        }

        TimeWindowSeconds = nextWindow;
        _customViewStartTime = null;
        _customViewEndTime = null;
        Invalidate();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToConfiguredAxes()
    {
        YMinimum = _configuredYMinimum;
        YMaximum = _configuredYMaximum;
        TimeWindowSeconds = _configuredTimeWindowSeconds;
        _customViewStartTime = null;
        _customViewEndTime = null;
        SetZoomSelection(ChartZoomAxis.None);
        Invalidate();
        ChartChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginZoomSelection(ChartZoomAxis axis)
    {
        ActiveZoomAxis = axis;
        _selectionStart = null;
        Cursor = axis == ChartZoomAxis.None ? Cursors.Default : Cursors.Cross;
        Invalidate();
    }

    public void CancelZoomSelection()
    {
        ActiveZoomAxis = ChartZoomAxis.None;
        _selectionStart = null;
        Capture = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    public PlotStatistics? GetVisibleStatistics()
    {
        var window = GetVisibleWindow();
        if (window.Points.Length == 0)
        {
            return null;
        }

        return new PlotStatistics(
            window.Points.Length,
            window.Points.Average(point => point.Value),
            window.Points.Min(point => point.Value),
            window.Points.Max(point => point.Value),
            window.StartTime,
            window.EndTime);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackgroundColor);

        using var borderPen = new Pen(AppTheme.Border);
        graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        var plot = new RectangleF(78, 28, Math.Max(40, Width - 102), Math.Max(40, Height - 76));
        _plotBounds = plot;
        var yDivisions = GetAxisDivisionCount(plot.Height, 36, 4, 12);
        var window = GetVisibleWindow();
        var windowSpan = Math.Max(1, window.EndTime - window.StartTime);
        var xDivisions = GetAxisDivisionCount(plot.Width, 64, 4, 12);
        var maximumLabeledDivisions = Math.Max(1, (int)Math.Floor(windowSpan / 5.0));
        xDivisions = Math.Min(xDivisions, maximumLabeledDivisions);
        DrawGrid(graphics, plot, yDivisions, xDivisions);
        DrawAxisLabels(
            graphics,
            plot,
            windowSpan,
            0,
            YMinimum,
            YMaximum,
            xDivisions,
            yDivisions);

        if (_points.Count == 0)
        {
            TextRenderer.DrawText(
                graphics,
                "等待协议数据…",
                _emptyFont,
                Rectangle.Round(plot),
                MutedColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
            DrawZoomSelection(graphics);
            return;
        }

        var visible = window.Points;
        if (visible.Length == 0)
        {
            TextRenderer.DrawText(
                graphics,
                "当前时间范围内无数据",
                _emptyFont,
                Rectangle.Round(plot),
                MutedColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
            DrawZoomSelection(graphics);
            return;
        }

        var screenPoints = visible
            .Select(point => new PointF(
                ToScreenX(plot, point.Seconds, window.StartTime, window.EndTime),
                ToScreenY(plot, point.Value, YMinimum, YMaximum)))
            .ToArray();

        var oldClip = graphics.Clip;
        graphics.SetClip(plot);
        if (screenPoints.Length > 1)
        {
            using var linePen = new Pen(LineColor, 2.2f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLines(linePen, screenPoints);
        }

        var latest = screenPoints[^1];
        using var pointBrush = new SolidBrush(PointColor);
        graphics.FillEllipse(pointBrush, latest.X - 4.5f, latest.Y - 4.5f, 9f, 9f);
        graphics.Clip = oldClip;

        var latestValue = _points[^1].Value;
        var valueText = $"当前  {latestValue:N0}";
        var valueSize = TextRenderer.MeasureText(valueText, _valueFont);
        var valueRect = new Rectangle(
            Math.Max((int)plot.Left, Width - valueSize.Width - 28),
            8,
            valueSize.Width + 14,
            22);
        using var valueBackground = new SolidBrush(AppTheme.HighlightSurface);
        graphics.FillRectangle(valueBackground, valueRect);
        TextRenderer.DrawText(
            graphics,
            valueText,
            _valueFont,
            valueRect,
            PointColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding);
        DrawZoomSelection(graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left ||
            ActiveZoomAxis == ChartZoomAxis.None ||
            !_plotBounds.Contains(e.Location))
        {
            return;
        }

        Focus();
        _selectionStart = e.Location;
        _selectionCurrent = e.Location;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_selectionStart is null || e.Button != MouseButtons.Left)
        {
            return;
        }

        _selectionCurrent = new Point(
            Math.Clamp(e.X, (int)_plotBounds.Left, (int)_plotBounds.Right),
            Math.Clamp(e.Y, (int)_plotBounds.Top, (int)_plotBounds.Bottom));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selectionStart is not { } start || e.Button != MouseButtons.Left)
        {
            return;
        }

        Capture = false;
        var selection = GetSelectionRectangle(start);
        _selectionStart = null;
        var axis = ActiveZoomAxis;
        var changed = ApplyZoomSelection(selection, axis);
        SetZoomSelection(ChartZoomAxis.None);
        if (changed)
        {
            ChartChanged?.Invoke(this, EventArgs.Empty);
        }
        ZoomSelectionCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && ActiveZoomAxis != ChartZoomAxis.None)
        {
            CancelZoomSelection();
            ZoomSelectionCompleted?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
    }

    private Rectangle GetSelectionRectangle(Point start)
    {
        var left = Math.Min(start.X, _selectionCurrent.X);
        var top = Math.Min(start.Y, _selectionCurrent.Y);
        var right = Math.Max(start.X, _selectionCurrent.X);
        var bottom = Math.Max(start.Y, _selectionCurrent.Y);
        return Rectangle.Intersect(
            Rectangle.Round(_plotBounds),
            Rectangle.FromLTRB(left, top, right, bottom));
    }

    private bool ApplyZoomSelection(Rectangle selection, ChartZoomAxis axis)
    {
        if (axis == ChartZoomAxis.None ||
            (axis == ChartZoomAxis.Vertical && selection.Height < 4) ||
            (axis == ChartZoomAxis.Horizontal && selection.Width < 4))
        {
            return false;
        }

        if (axis == ChartZoomAxis.Vertical)
        {
            var first = ScreenYToValue(selection.Top);
            var second = ScreenYToValue(selection.Bottom);
            var minimum = Math.Min(first, second);
            var maximum = Math.Max(first, second);
            if (maximum - minimum < 0.001)
            {
                return false;
            }

            YMinimum = minimum;
            YMaximum = maximum;
            Invalidate();
            return true;
        }

        var window = GetVisibleWindow();
        var span = Math.Max(double.Epsilon, window.EndTime - window.StartTime);
        var leftRatio = Math.Clamp(
            (selection.Left - _plotBounds.Left) / Math.Max(1f, _plotBounds.Width),
            0f,
            1f);
        var rightRatio = Math.Clamp(
            (selection.Right - _plotBounds.Left) / Math.Max(1f, _plotBounds.Width),
            0f,
            1f);
        var firstTime = window.StartTime + span * leftRatio;
        var secondTime = window.StartTime + span * rightRatio;
        var startTime = Math.Min(firstTime, secondTime);
        var endTime = Math.Max(firstTime, secondTime);
        if (endTime - startTime < 0.01)
        {
            return false;
        }

        _customViewStartTime = startTime;
        _customViewEndTime = endTime;
        Invalidate();
        return true;
    }

    private double ScreenYToValue(int y)
    {
        var ratio = Math.Clamp(
            (_plotBounds.Bottom - y) / Math.Max(1f, _plotBounds.Height),
            0f,
            1f);
        return YMinimum + (YMaximum - YMinimum) * ratio;
    }

    private void DrawZoomSelection(Graphics graphics)
    {
        if (ActiveZoomAxis == ChartZoomAxis.None)
        {
            return;
        }

        var hint = ActiveZoomAxis == ChartZoomAxis.Vertical
            ? "拖动框选纵坐标范围"
            : "拖动框选横坐标范围";
        TextRenderer.DrawText(
            graphics,
            hint,
            _axisFont,
            new Point((int)_plotBounds.Left + 6, (int)_plotBounds.Top + 4),
            AppTheme.Accent);

        if (_selectionStart is null)
        {
            return;
        }

        var selection = GetSelectionRectangle(_selectionStart.Value);
        if (selection.Width <= 0 || selection.Height <= 0)
        {
            return;
        }

        var highlight = ActiveZoomAxis == ChartZoomAxis.Vertical
            ? Rectangle.Round(new RectangleF(
                _plotBounds.Left,
                selection.Top,
                _plotBounds.Width,
                selection.Height))
            : Rectangle.Round(new RectangleF(
                selection.Left,
                _plotBounds.Top,
                selection.Width,
                _plotBounds.Height));
        using var fill = new SolidBrush(Color.FromArgb(
            AppTheme.IsLight ? 38 : 55,
            AppTheme.Accent));
        using var pen = new Pen(AppTheme.Accent, 1.5f)
        {
            DashStyle = DashStyle.Dash
        };
        graphics.FillRectangle(fill, highlight);
        graphics.DrawRectangle(pen, highlight);
    }

    private void SetZoomSelection(ChartZoomAxis axis)
    {
        ActiveZoomAxis = axis;
        _selectionStart = null;
        Cursor = axis == ChartZoomAxis.None ? Cursors.Default : Cursors.Cross;
        Invalidate();
    }

    private static int GetAxisDivisionCount(
        float availablePixels,
        float pixelsPerDivision,
        int minimum,
        int maximum)
    {
        return Math.Clamp(
            (int)Math.Round(availablePixels / pixelsPerDivision),
            minimum,
            maximum);
    }

    private static void DrawGrid(
        Graphics graphics,
        RectangleF plot,
        int yDivisions,
        int xDivisions)
    {
        using var gridPen = new Pen(GridColor, 1);
        using var axisPen = new Pen(AxisColor, 1.2f);

        for (var index = 0; index <= yDivisions; index++)
        {
            var y = plot.Top + plot.Height * index / yDivisions;
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
        }
        for (var index = 0; index <= xDivisions; index++)
        {
            var x = plot.Left + plot.Width * index / xDivisions;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
        }

        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }

    private void DrawAxisLabels(
        Graphics graphics,
        RectangleF plot,
        double xMin,
        double xMax,
        double yMin,
        double yMax,
        int xDivisions,
        int yDivisions)
    {
        for (var index = 0; index <= yDivisions; index++)
        {
            var value = yMax - (yMax - yMin) * index / yDivisions;
            var y = plot.Top + plot.Height * index / yDivisions;
            var label = value.ToString("N0");
            var size = TextRenderer.MeasureText(label, _axisFont);
            TextRenderer.DrawText(
                graphics,
                label,
                _axisFont,
                new Point((int)plot.Left - size.Width - 9, (int)y - size.Height / 2),
                TextColor);
        }

        for (var index = 0; index <= xDivisions; index++)
        {
            var value = xMin + (xMax - xMin) * index / xDivisions;
            var x = plot.Left + plot.Width * index / xDivisions;
            var label = $"{value:0}s";
            var size = TextRenderer.MeasureText(label, _axisFont);
            TextRenderer.DrawText(
                graphics,
                label,
                _axisFont,
                new Point((int)x - size.Width / 2, (int)plot.Bottom + 7),
                TextColor);
        }

        TextRenderer.DrawText(
            graphics,
            "时间 (s)",
            _axisFont,
            new Point((int)(plot.Left + plot.Width / 2) - 12, Height - 24),
            MutedColor);
        TextRenderer.DrawText(
            graphics,
            "数字量",
            _axisFont,
            new Point(7, 7),
            MutedColor);
    }

    private static float ToScreenX(RectangleF plot, double value, double min, double max)
    {
        return plot.Left + (float)((value - min) / Math.Max(double.Epsilon, max - min)) * plot.Width;
    }

    private static float ToScreenY(RectangleF plot, double value, double min, double max)
    {
        return plot.Bottom - (float)((value - min) / Math.Max(double.Epsilon, max - min)) * plot.Height;
    }

    private VisibleWindow GetVisibleWindow()
    {
        if (_customViewStartTime.HasValue && _customViewEndTime.HasValue)
        {
            var customStartTime = _customViewStartTime.Value;
            var customEndTime = _customViewEndTime.Value;
            var customPoints = _points
                .Where(point =>
                    point.Seconds >= customStartTime &&
                    point.Seconds <= customEndTime)
                .ToArray();
            return new VisibleWindow(customStartTime, customEndTime, customPoints);
        }

        if (_points.Count == 0)
        {
            return new VisibleWindow(0, TimeWindowSeconds, []);
        }

        var lastTime = _points[^1].Seconds;
        var endTime = Math.Max(TimeWindowSeconds, lastTime);
        var startTime = Math.Max(0, endTime - TimeWindowSeconds);
        var points = _points
            .Where(point => point.Seconds >= startTime && point.Seconds <= endTime)
            .ToArray();
        return new VisibleWindow(startTime, endTime, points);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _emptyFont.Dispose();
            _axisFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record PlotPoint(double Seconds, double Value);
    private sealed record VisibleWindow(double StartTime, double EndTime, PlotPoint[] Points);
}

public sealed record PlotStatistics(
    int Count,
    double Average,
    double Minimum,
    double Maximum,
    double StartTime,
    double EndTime);
