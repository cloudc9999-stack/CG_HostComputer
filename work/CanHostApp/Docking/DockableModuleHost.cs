namespace CanHost.Docking;

public sealed class DockableModuleHost : UserControl
{
    private static Color BorderColor => AppTheme.Border;
    private static Color DragColor => AppTheme.DragBar;
    private static Color TextColor => AppTheme.Muted;

    private readonly Panel _dragBar;
    private readonly TableLayoutPanel _dragLayout;
    private readonly Label _dragLabel;
    private readonly Button _minimizeButton;
    private readonly Button _maximizeButton;
    private readonly Control _content;
    private readonly ToolTip _toolTip = new();
    private DockableWorkspace? _workspace;
    private InteractionMode _interaction;
    private Point _startScreen;
    private Point _startFormLocation;
    private Rectangle _startBounds;
    private Rectangle _normalBounds;
    private Size _restoreMinimumSize;
    private bool _isMinimized;
    private bool _isMaximized;

    public DockableModuleHost(string moduleId, string title, Control content)
    {
        ModuleId = moduleId;
        ModuleTitle = title;
        _content = content;
        BackColor = BorderColor;
        DoubleBuffered = true;
        Padding = new Padding(5, 28, 5, 5);
        MinimumSize = new Size(260, 150);

        content.Dock = DockStyle.Fill;
        _dragLabel = new Label
        {
            Text = "⋮⋮  拖动 / 缩放",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextColor,
            BackColor = DragColor,
            Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold),
            Padding = new Padding(6, 0, 0, 0),
            Cursor = Cursors.SizeAll,
            AutoEllipsis = true
        };
        _dragBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = DragColor,
            Cursor = Cursors.SizeAll
        };
        _minimizeButton = CreateWindowButton("最小化", "最小化当前模块");
        _maximizeButton = CreateWindowButton("最大化", "最大化或还原当前模块");
        _minimizeButton.Click += (_, _) => ToggleMinimize();
        _maximizeButton.Click += (_, _) => ToggleMaximize();
        _dragLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = DragColor,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _dragLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _dragLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118F));
        _dragLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = DragColor,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _maximizeButton.Margin = new Padding(2, 2, 1, 2);
        _minimizeButton.Margin = new Padding(1, 2, 1, 2);
        buttonPanel.Controls.Add(_maximizeButton);
        buttonPanel.Controls.Add(_minimizeButton);
        _dragLayout.Controls.Add(_dragLabel, 0, 0);
        _dragLayout.Controls.Add(buttonPanel, 1, 0);
        _dragBar.Controls.Add(_dragLayout);

        Controls.Add(content);
        Controls.Add(_dragBar);
        _dragBar.MouseDown += StartMove;
        _dragLabel.MouseDown += StartMove;
        _dragBar.MouseMove += ContinueMove;
        _dragLayout.MouseDown += StartMove;
        _dragLayout.MouseMove += ContinueMove;
        _dragLayout.MouseUp += EndInteraction;
        _dragLabel.MouseMove += ContinueMove;
        _dragBar.MouseUp += EndInteraction;
        _dragLabel.MouseUp += EndInteraction;
        _dragLabel.DoubleClick += (_, _) => ToggleMaximize();
        _toolTip.SetToolTip(
            _dragLabel,
            "拖动模块；拖到主工作区外可形成独立窗口");
        MouseDown += StartResize;
        MouseMove += ContinueResize;
        MouseUp += EndInteraction;
        Resize += (_, _) =>
        {
            Invalidate(true);
            Workspace?.Invalidate(true);
        };
        Paint += DrawFrame;
    }

    public string ModuleId { get; }
    public string ModuleTitle { get; }
    public Rectangle PreferredBounds { get; set; }
    public bool PreventDocking { get; set; }

    public DockableWorkspace? Workspace => _workspace ?? Parent as DockableWorkspace;

    public bool IsMinimized => _isMinimized;
    public bool IsMaximized => _isMaximized;
    public bool IsFloating => Workspace?.IsModuleFloating(this) == true;
    internal FloatingModuleForm? FloatingWindow => Parent as FloatingModuleForm;

    public void AttachWorkspace(DockableWorkspace workspace)
    {
        _workspace = workspace;
    }

    public void AttachDockMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        _dragBar.ContextMenuStrip = menu;
        _dragLayout.ContextMenuStrip = menu;
        _dragLabel.ContextMenuStrip = menu;
    }

    public void PrepareForExternalLayout()
    {
        if (_isMinimized)
        {
            MinimumSize = _restoreMinimumSize;
            _content.Visible = true;
            _isMinimized = false;
            _minimizeButton.Text = "最小化";
        }
        _isMaximized = false;
        _maximizeButton.Text = "最大化";
    }

    public void RestoreSavedBounds(Rectangle bounds)
    {
        PrepareForExternalLayout();
        Bounds = bounds;
    }

    private void ToggleMinimize()
    {
        if (FloatingWindow is { } floating)
        {
            floating.WindowState = FormWindowState.Minimized;
            return;
        }

        if (_isMinimized)
        {
            RestoreNormalBounds();
            return;
        }

        if (!_isMaximized)
        {
            _normalBounds = Bounds;
        }
        _restoreMinimumSize = MinimumSize;
        _isMinimized = true;
        _isMaximized = false;
        _content.Visible = false;
        MinimumSize = new Size(MinimumSize.Width, 33);
        Height = 33;
        _minimizeButton.Text = "还原";
        _maximizeButton.Text = "最大化";
        BringToFront();
        Workspace?.Invalidate(true);
        Workspace?.Update();
        Workspace?.SaveLayout();
    }

    private void ToggleMaximize()
    {
        if (FloatingWindow is { } floating)
        {
            floating.WindowState = floating.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            RefreshWindowButtonState();
            return;
        }

        if (Workspace is null)
        {
            return;
        }

        if (_isMaximized)
        {
            RestoreNormalBounds();
            return;
        }

        if (!_isMinimized)
        {
            _normalBounds = Bounds;
        }
        _restoreMinimumSize = MinimumSize;
        _isMinimized = false;
        _content.Visible = true;
        MinimumSize = _restoreMinimumSize;
        Workspace.SuspendLayout();
        Bounds = new Rectangle(0, 0, Workspace.ClientSize.Width, Workspace.ClientSize.Height);
        Workspace.ResumeLayout(true);
        _isMaximized = true;
        _minimizeButton.Text = "最小化";
        _maximizeButton.Text = "还原";
        BringToFront();
        Invalidate(true);
        _content.Invalidate(true);
        Workspace.Invalidate(true);
        Workspace.Update();
        Workspace.SaveLayout();
    }

    private void RestoreNormalBounds()
    {
        if (FloatingWindow is { } floating)
        {
            floating.WindowState = FormWindowState.Normal;
            RefreshWindowButtonState();
            return;
        }

        MinimumSize = _restoreMinimumSize;
        _content.Visible = true;
        _isMinimized = false;
        _isMaximized = false;
        if (_normalBounds.Width > 0 && _normalBounds.Height > 0)
        {
            Bounds = _normalBounds;
        }
        _minimizeButton.Text = "最小化";
        _maximizeButton.Text = "最大化";
        BringToFront();
        Workspace?.Invalidate(true);
        Workspace?.Update();
        Workspace?.SaveLayout();
    }

    internal void RefreshWindowButtonState()
    {
        if (FloatingWindow is not { } floating)
        {
            return;
        }

        _minimizeButton.Text = "最小化";
        _maximizeButton.Text = floating.WindowState == FormWindowState.Maximized
            ? "还原"
            : "最大化";
    }

    private static Button CreateWindowButton(string text, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(54, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.DragBar,
            ForeColor = AppTheme.Text,
            Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
            TabStop = false,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = AppTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        var tip = new ToolTip();
        tip.SetToolTip(button, tooltip);
        button.Disposed += (_, _) => tip.Dispose();
        return button;
    }

    private void StartMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _interaction = InteractionMode.Move;
        _startScreen = Cursor.Position;
        _startBounds = Bounds;
        _startFormLocation = FindForm()?.Location ?? Point.Empty;
        BringToFront();
        if (sender is Control control)
        {
            control.Capture = true;
        }
    }

    private void ContinueMove(object? sender, MouseEventArgs e)
    {
        if (_interaction != InteractionMode.Move ||
            e.Button != MouseButtons.Left ||
            Workspace is null)
        {
            return;
        }

        var delta = new Point(
            Cursor.Position.X - _startScreen.X,
            Cursor.Position.Y - _startScreen.Y);
        if (IsFloating && FindForm() is { } floating)
        {
            floating.Location = new Point(
                _startFormLocation.X + delta.X,
                _startFormLocation.Y + delta.Y);
            return;
        }

        var proposed = new Rectangle(
            _startBounds.X + delta.X,
            _startBounds.Y + delta.Y,
            _startBounds.Width,
            _startBounds.Height);
        Bounds = Workspace.SnapBounds(this, proposed, resizing: false);
        Workspace.Invalidate(true);
    }

    private void StartResize(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _interaction = GetResizeMode(e.Location);
        if (_interaction == InteractionMode.None)
        {
            return;
        }

        _startScreen = Cursor.Position;
        _startBounds = Bounds;
        BringToFront();
        Capture = true;
    }

    private void ContinueResize(object? sender, MouseEventArgs e)
    {
        if (_interaction == InteractionMode.None || e.Button != MouseButtons.Left)
        {
            UpdateResizeCursor(e.Location);
            return;
        }
        if (_interaction == InteractionMode.Move || Workspace is null)
        {
            return;
        }

        var delta = new Point(
            Cursor.Position.X - _startScreen.X,
            Cursor.Position.Y - _startScreen.Y);
        var bounds = _startBounds;
        var minimum = MinimumSize;

        if (_interaction is InteractionMode.ResizeLeft or InteractionMode.ResizeTopLeft or InteractionMode.ResizeBottomLeft)
        {
            var newLeft = Math.Min(bounds.Right - minimum.Width, bounds.Left + delta.X);
            bounds.Width += bounds.Left - newLeft;
            bounds.X = newLeft;
        }
        if (_interaction is InteractionMode.ResizeRight or InteractionMode.ResizeTopRight or InteractionMode.ResizeBottomRight)
        {
            bounds.Width = Math.Max(minimum.Width, bounds.Width + delta.X);
        }
        if (_interaction is InteractionMode.ResizeTop or InteractionMode.ResizeTopLeft or InteractionMode.ResizeTopRight)
        {
            var newTop = Math.Min(bounds.Bottom - minimum.Height, bounds.Top + delta.Y);
            bounds.Height += bounds.Top - newTop;
            bounds.Y = newTop;
        }
        if (_interaction is InteractionMode.ResizeBottom or InteractionMode.ResizeBottomLeft or InteractionMode.ResizeBottomRight)
        {
            bounds.Height = Math.Max(minimum.Height, bounds.Height + delta.Y);
        }

        Bounds = Workspace.SnapBounds(this, bounds, resizing: true);
        Workspace.Invalidate(true);
    }

    private void EndInteraction(object? sender, MouseEventArgs e)
    {
        var completedInteraction = _interaction;
        _interaction = InteractionMode.None;
        if (sender is Control control)
        {
            control.Capture = false;
        }
        Capture = false;
        if (Workspace is { } workspace && completedInteraction == InteractionMode.Move)
        {
            if (IsFloating)
            {
                if (!PreventDocking &&
                    workspace.IsScreenPointInside(Cursor.Position))
                {
                    workspace.DockFloatingModule(this, Cursor.Position);
                }
            }
            else if (workspace.IsScreenPointOutside(Cursor.Position))
            {
                workspace.FloatModule(this, Cursor.Position);
            }
        }
        Workspace?.SaveLayout();
    }

    private void UpdateResizeCursor(Point location)
    {
        Cursor = GetResizeMode(location) switch
        {
            InteractionMode.ResizeLeft or InteractionMode.ResizeRight => Cursors.SizeWE,
            InteractionMode.ResizeTop or InteractionMode.ResizeBottom => Cursors.SizeNS,
            InteractionMode.ResizeTopLeft or InteractionMode.ResizeBottomRight => Cursors.SizeNWSE,
            InteractionMode.ResizeTopRight or InteractionMode.ResizeBottomLeft => Cursors.SizeNESW,
            _ => Cursors.Default
        };
    }

    private InteractionMode GetResizeMode(Point location)
    {
        const int edge = 7;
        var left = location.X <= edge;
        var right = location.X >= Width - edge;
        var top = location.Y <= edge;
        var bottom = location.Y >= Height - edge;
        if (left && top) return InteractionMode.ResizeTopLeft;
        if (right && top) return InteractionMode.ResizeTopRight;
        if (left && bottom) return InteractionMode.ResizeBottomLeft;
        if (right && bottom) return InteractionMode.ResizeBottomRight;
        if (left) return InteractionMode.ResizeLeft;
        if (right) return InteractionMode.ResizeRight;
        if (top) return InteractionMode.ResizeTop;
        if (bottom) return InteractionMode.ResizeBottom;
        return InteractionMode.None;
    }

    private void DrawFrame(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private enum InteractionMode
    {
        None,
        Move,
        ResizeLeft,
        ResizeRight,
        ResizeTop,
        ResizeBottom,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight
    }
}
