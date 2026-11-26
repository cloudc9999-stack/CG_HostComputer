using CanHost.Controls;

namespace CanHost.Modules;

public sealed class PlotModule : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(20, 30, 48);
    private static Color BorderColor => AppTheme.Border;
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);

    private readonly LineChartControl _chart = new();
    private readonly Label _title = new();
    private readonly Button _configButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _zoomYButton = new();
    private readonly Button _zoomXButton = new();
    private readonly Button _resetZoomButton = new();
    private readonly ToolTip _toolTip = new();
    private long _sampleIndex;
    private DateTime? _firstSampleAt;

    public PlotModule(string title)
    {
        Title = title;
        BackColor = BackgroundColor;
        Dock = DockStyle.Fill;
        Padding = new Padding(18, 14, 18, 16);
        BuildInterface();
    }

    public event EventHandler? PlotCleared;

    public string Title { get; }
    public LineChartControl Chart => _chart;

    public void AppendValue(double value)
    {
        AppendValue(value, DateTime.Now);
    }

    public void AppendValue(double value, DateTime timestamp)
    {
        _firstSampleAt ??= timestamp;
        var elapsedSeconds = Math.Max(
            0,
            (timestamp - _firstSampleAt.Value).TotalSeconds);
        _chart.AddPoint(elapsedSeconds, value);
        _sampleIndex++;
    }

    public void ClearPlot()
    {
        _sampleIndex = 0;
        _firstSampleAt = null;
        _chart.ClearPoints();
        PlotCleared?.Invoke(this, EventArgs.Empty);
    }

    private void BuildInterface()
    {
        Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        _title.Text = Title;
        _title.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _title.ForeColor = TextColor;
        _title.AutoSize = false;
        _title.AutoEllipsis = true;
        _title.TextAlign = ContentAlignment.MiddleLeft;

        StyleSecondaryButton(_configButton);
        _configButton.Text = "配置";
        _configButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _configButton.Bounds = new Rectangle(Width - 140, 8, 60, 30);
        _configButton.Click += (_, _) => OpenConfig();

        StyleSecondaryButton(_clearButton);
        _clearButton.Text = "清空";
        _clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _clearButton.Bounds = new Rectangle(Width - 74, 8, 56, 30);
        _clearButton.Click += (_, _) => ClearPlot();

        StyleToolButton(_zoomYButton, "Y轴放大");
        _zoomYButton.Click += (_, _) => ToggleZoomSelection(ChartZoomAxis.Vertical);
        _toolTip.SetToolTip(_zoomYButton, "点击后在绘图区上下框选纵坐标范围");
        StyleToolButton(_zoomXButton, "X轴放大");
        _zoomXButton.Click += (_, _) => ToggleZoomSelection(ChartZoomAxis.Horizontal);
        _toolTip.SetToolTip(_zoomXButton, "点击后在绘图区左右框选横坐标范围");
        StyleToolButton(_resetZoomButton, "恢复");
        _resetZoomButton.Click += (_, _) =>
        {
            _chart.ResetToConfiguredAxes();
            UpdateZoomButtonState();
        };
        _toolTip.SetToolTip(_resetZoomButton, "恢复配置中的坐标范围");
        _chart.ZoomSelectionCompleted += (_, _) => UpdateZoomButtonState();

        _chart.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Bottom |
            AnchorStyles.Left |
            AnchorStyles.Right;

        Controls.AddRange(
        [
            _title,
            _configButton,
            _clearButton,
            _zoomYButton,
            _zoomXButton,
            _resetZoomButton,
            _chart
        ]);
        Resize += (_, _) => LayoutContent();
    }

    private void LayoutContent()
    {
        var contentWidth = Math.Max(20, ClientSize.Width - 36);
        _title.Bounds = new Rectangle(
            18,
            8,
            Math.Max(40, ClientSize.Width - 166),
            30);
        _chart.Bounds = new Rectangle(
            18,
            84,
            contentWidth,
            Math.Max(40, ClientSize.Height - 100));

        _configButton.Left = ClientSize.Width - 140;
        _clearButton.Left = ClientSize.Width - 74;
        _zoomYButton.Bounds = new Rectangle(18, 48, 72, 28);
        _zoomXButton.Bounds = new Rectangle(94, 48, 72, 28);
        _resetZoomButton.Bounds = new Rectangle(170, 48, 72, 28);
    }

    private void ToggleZoomSelection(ChartZoomAxis axis)
    {
        var next = _chart.ActiveZoomAxis == axis
            ? ChartZoomAxis.None
            : axis;
        _chart.BeginZoomSelection(next);
        UpdateZoomButtonState();
    }

    private void UpdateZoomButtonState()
    {
        UpdateZoomButton(_zoomYButton, _chart.ActiveZoomAxis == ChartZoomAxis.Vertical);
        UpdateZoomButton(_zoomXButton, _chart.ActiveZoomAxis == ChartZoomAxis.Horizontal);
    }

    private static void UpdateZoomButton(Button button, bool active)
    {
        button.BackColor = active ? AppTheme.Accent : Color.FromArgb(25, 38, 59);
        button.ForeColor = active ? Color.FromArgb(7, 27, 43) : TextColor;
        button.Invalidate();
    }

    private void OpenConfig()
    {
        using var dialog = new PlotSettingsForm(
            _chart.ConfiguredYMinimum,
            _chart.ConfiguredYMaximum,
            _chart.ConfiguredTimeWindowSeconds);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _chart.ConfigureAxes(
                dialog.Minimum,
                dialog.Maximum,
                dialog.TimeWindowSeconds);
        }
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.FromArgb(25, 38, 59);
        button.ForeColor = TextColor;
        button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static void StyleToolButton(Button button, string text)
    {
        StyleSecondaryButton(button);
        button.Text = text;
        button.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);
        button.Padding = Padding.Empty;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}
