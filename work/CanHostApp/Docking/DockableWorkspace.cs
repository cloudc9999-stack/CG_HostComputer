using System.Text.Json;

namespace CanHost.Docking;

public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
    Maximize
}

public sealed record ModuleVisibilityChangedEventArgs(string ModuleId, bool Visible);

public sealed class DockableWorkspace : Panel
{
    private readonly List<DockableModuleHost> _hosts = [];
    private readonly Dictionary<string, FloatingModuleForm> _floatingWindows = [];
    private readonly Dictionary<string, Rectangle> _lastFloatingBounds = [];
    private bool _initialized;
    private bool _isClosing;
    public static string? LayoutPathOverride { get; set; }

    private string LayoutPath => LayoutPathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CanHost",
        "dock-layout-v5.5.json");

    public DockableWorkspace()
    {
        DoubleBuffered = true;
        BackColor = AppTheme.Background;
        Dock = DockStyle.Fill;
        Padding = new Padding(4);
        Resize += (_, _) => InitializeLayout();
    }

    public IReadOnlyList<DockableModuleHost> Modules => _hosts;
    public event EventHandler<ModuleVisibilityChangedEventArgs>? ModuleVisibilityChanged;

    public void AddModule(DockableModuleHost host, Rectangle defaultBounds)
    {
        host.PreferredBounds = defaultBounds;
        host.AttachWorkspace(this);
        host.AttachDockMenu(BuildDockMenu(host));
        _hosts.Add(host);
        Controls.Add(host);
    }

    public DockableModuleHost? FindModule(string moduleId)
    {
        return _hosts.FirstOrDefault(host => host.ModuleId == moduleId);
    }

    public bool IsModuleFloating(DockableModuleHost host)
    {
        return _floatingWindows.ContainsKey(host.ModuleId);
    }

    public bool IsModuleVisible(DockableModuleHost host)
    {
        return _floatingWindows.TryGetValue(host.ModuleId, out var floating)
            ? floating.Visible
            : host.Visible;
    }

    public void SetModuleVisible(DockableModuleHost host, bool visible)
    {
        if (_floatingWindows.TryGetValue(host.ModuleId, out var floating))
        {
            host.Visible = true;
            floating.Visible = visible;
        }
        else
        {
            host.Visible = visible;
        }
        SaveLayout();
    }

    public bool IsScreenPointInside(Point screenPoint)
    {
        return ClientRectangle.Contains(PointToClient(screenPoint));
    }

    public bool IsScreenPointOutside(Point screenPoint)
    {
        return !IsScreenPointInside(screenPoint);
    }

    public bool FloatModule(DockableModuleHost host, Point screenPoint)
    {
        if (_isClosing)
        {
            return false;
        }
        if (_floatingWindows.TryGetValue(host.ModuleId, out var existing))
        {
            existing.Show();
            existing.Activate();
            return true;
        }

        host.PrepareForExternalLayout();
        var hostSize = host.Bounds.Width >= host.MinimumSize.Width &&
                       host.Bounds.Height >= host.MinimumSize.Height
            ? host.Bounds.Size
            : host.MinimumSize;

        var screen = Screen.FromPoint(screenPoint).WorkingArea;
        var maximumWidth = Math.Max(host.MinimumSize.Width, screen.Width - 40);
        var maximumHeight = Math.Max(host.MinimumSize.Height, screen.Height - 80);
        var desiredSize = new Size(
            Math.Clamp(hostSize.Width, host.MinimumSize.Width, maximumWidth),
            Math.Clamp(hostSize.Height, host.MinimumSize.Height, maximumHeight));
        var desiredLocation = new Point(screenPoint.X - 80, screenPoint.Y - 16);
        desiredLocation.X = Math.Clamp(
            desiredLocation.X,
            screen.Left,
            Math.Max(screen.Left, screen.Right - desiredSize.Width - 20));
        desiredLocation.Y = Math.Clamp(
            desiredLocation.Y,
            screen.Top,
            Math.Max(screen.Top, screen.Bottom - desiredSize.Height - 60));

        Controls.Remove(host);
        host.Dock = DockStyle.Fill;
        host.Visible = true;
        var floating = new FloatingModuleForm(this, host)
        {
            ClientSize = desiredSize,
            Location = desiredLocation
        };
        _floatingWindows[host.ModuleId] = floating;
        floating.Show();
        floating.Activate();
        SaveLayout();
        return true;
    }

    public void DockFloatingModule(DockableModuleHost host, Point? screenPoint = null)
    {
        if (host.PreventDocking)
        {
            return;
        }
        if (!_floatingWindows.Remove(host.ModuleId, out var floating))
        {
            return;
        }

        floating.AllowClose = true;
        floating.WindowState = FormWindowState.Normal;
        floating.Controls.Remove(host);
        host.Dock = DockStyle.None;
        host.PrepareForExternalLayout();
        Controls.Add(host);

        var clientPoint = screenPoint.HasValue
            ? PointToClient(screenPoint.Value)
            : new Point(
                Math.Max(20, (ClientSize.Width - host.Width) / 2),
                Math.Max(20, (ClientSize.Height - host.Height) / 2));
        var proposed = new Rectangle(
            clientPoint.X - Math.Min(80, host.Width / 2),
            clientPoint.Y - 16,
            host.Width,
            host.Height);
        host.Bounds = SnapBounds(host, proposed, false);
        host.Visible = true;
        host.BringToFront();
        floating.Close();
        floating.Dispose();
        SaveLayout();
    }

    public void DockAllFloatingModules()
    {
        foreach (var host in _hosts.Where(IsModuleFloating).ToArray())
        {
            if (!host.PreventDocking)
            {
                DockFloatingModule(host);
            }
        }
    }

    public void CloseAllFloatingWindows()
    {
        _isClosing = true;
        foreach (var floating in _floatingWindows.Values.ToArray())
        {
            floating.AllowClose = true;
            floating.Close();
        }
        _floatingWindows.Clear();
    }

    internal FloatingModuleForm? GetFloatingWindow(DockableModuleHost host)
    {
        return _floatingWindows.GetValueOrDefault(host.ModuleId);
    }

    internal void NotifyModuleVisibilityChanged(DockableModuleHost host, bool visible)
    {
        ModuleVisibilityChanged?.Invoke(
            this,
            new ModuleVisibilityChangedEventArgs(host.ModuleId, visible));
    }

    public void InitializeLayout()
    {
        if (_initialized || ClientSize.Width < 400 || ClientSize.Height < 300)
        {
            return;
        }

        RestoreDefaultLayout();
        LoadLayout();
        _initialized = true;
    }

    public void RestoreDefaultLayout()
    {
        DockAllFloatingModules();
        var width = Math.Max(400, ClientSize.Width - 8);
        var height = Math.Max(300, ClientSize.Height - 8);
        var sendHeight = Math.Clamp((int)(height * 0.29), 245, 300);
        var upperHeight = Math.Max(190, height - sendHeight - 8);
        var receiveWidth = Math.Clamp((int)(width * 0.20), 250, 300);
        var supplyWidth = Math.Clamp((int)(width * 0.34), 400, 540);
        var supplyX = 4 + receiveWidth + 4;
        var plotX = supplyX + supplyWidth + 4;
        var plotAreaWidth = Math.Max(250, width - plotX - 4);
        var plotColumns = plotAreaWidth >= 500 ? 2 : 1;
        var plotRows = (int)Math.Ceiling(5 / (double)plotColumns);
        var plotGap = 4;
        var plotWidth = Math.Max(
            250,
            (plotAreaWidth - plotGap * (plotColumns - 1)) / plotColumns);
        var plotHeight = Math.Max(
            150,
            (upperHeight - plotGap * (plotRows - 1)) / plotRows);
        var pressurePlotIds = new[]
        {
            "plot-main-pressure",
            "plot-branch1-pressure",
            "plot-branch2-pressure",
            "plot-branch3-pressure",
            "plot-branch4-pressure"
        };
        var entries = new List<(string Id, Rectangle Bounds)>
        {
            ("receive", new Rectangle(4, 4, receiveWidth, upperHeight)),
            ("supply-control", new Rectangle(supplyX, 4, supplyWidth, upperHeight)),
            ("send", new Rectangle(4, upperHeight + 8, width - 4, sendHeight)),
            ("ticker", new Rectangle(4, 4, receiveWidth, 80))
        };
        for (var index = 0; index < pressurePlotIds.Length; index++)
        {
            var column = index % plotColumns;
            var row = index / plotColumns;
            entries.Add((
                pressurePlotIds[index],
                new Rectangle(
                    plotX + column * (plotWidth + plotGap),
                    4 + row * (plotHeight + plotGap),
                    plotWidth,
                    plotHeight)));
        }

        SetModules(entries.ToArray());
        SaveLayout();
    }

    public void RestoreModule(DockableModuleHost host)
    {
        if (host.PreventDocking)
        {
            return;
        }
        if (IsModuleFloating(host))
        {
            DockFloatingModule(host);
        }

        host.PrepareForExternalLayout();
        if (_lastFloatingBounds.TryGetValue(host.ModuleId, out var bounds))
        {
            host.Bounds = Constrain(host, bounds);
        }
        else
        {
            host.Bounds = Constrain(host, host.PreferredBounds);
        }
        host.Visible = true;
        host.BringToFront();
        SaveLayout();
    }

    public void DockModule(DockableModuleHost host, DockSide side)
    {
        if (host.PreventDocking)
        {
            return;
        }
        if (IsModuleFloating(host))
        {
            DockFloatingModule(host);
        }

        host.PrepareForExternalLayout();
        _lastFloatingBounds[host.ModuleId] = host.Bounds;
        var width = Math.Max(host.MinimumSize.Width, ClientSize.Width - 8);
        var height = Math.Max(host.MinimumSize.Height, ClientSize.Height - 8);
        host.Bounds = side switch
        {
            DockSide.Left => new Rectangle(4, 4, width / 2 - 4, height),
            DockSide.Right => new Rectangle(width / 2 + 4, 4, width / 2 - 4, height),
            DockSide.Top => new Rectangle(4, 4, width, height / 2 - 4),
            DockSide.Bottom => new Rectangle(4, height / 2 + 4, width, height / 2 - 4),
            DockSide.Maximize => new Rectangle(4, 4, width, height),
            _ => host.Bounds
        };
        host.Visible = true;
        host.BringToFront();
        SaveLayout();
    }

    public Rectangle SnapBounds(DockableModuleHost source, Rectangle proposed, bool resizing)
    {
        const int threshold = 12;
        proposed.Width = Math.Max(source.MinimumSize.Width, proposed.Width);
        proposed.Height = Math.Max(source.MinimumSize.Height, proposed.Height);

        var maxX = Math.Max(0, ClientSize.Width - proposed.Width);
        var maxY = Math.Max(0, ClientSize.Height - proposed.Height);
        proposed.X = Math.Clamp(proposed.X, 0, maxX);
        proposed.Y = Math.Clamp(proposed.Y, 0, maxY);

        if (Math.Abs(proposed.Left) <= threshold) proposed.X = 0;
        if (Math.Abs(proposed.Top) <= threshold) proposed.Y = 0;
        if (Math.Abs(ClientSize.Width - proposed.Right) <= threshold)
        {
            proposed.X = ClientSize.Width - proposed.Width;
        }
        if (Math.Abs(ClientSize.Height - proposed.Bottom) <= threshold)
        {
            proposed.Y = ClientSize.Height - proposed.Height;
        }

        foreach (var other in _hosts.Where(host =>
                     host != source &&
                     host.Visible &&
                     host.Parent == this))
        {
            if (!resizing)
            {
                if (Math.Abs(proposed.Left - other.Left) <= threshold) proposed.X = other.Left;
                if (Math.Abs(proposed.Left - other.Right) <= threshold) proposed.X = other.Right;
                if (Math.Abs(proposed.Right - other.Left) <= threshold) proposed.X = other.Left - proposed.Width;
                if (Math.Abs(proposed.Right - other.Right) <= threshold) proposed.X = other.Right - proposed.Width;
                if (Math.Abs(proposed.Top - other.Top) <= threshold) proposed.Y = other.Top;
                if (Math.Abs(proposed.Top - other.Bottom) <= threshold) proposed.Y = other.Bottom;
                if (Math.Abs(proposed.Bottom - other.Top) <= threshold) proposed.Y = other.Top - proposed.Height;
                if (Math.Abs(proposed.Bottom - other.Bottom) <= threshold) proposed.Y = other.Bottom - proposed.Height;
            }
            else
            {
                if (Math.Abs(proposed.Left - other.Left) <= threshold) proposed.X = other.Left;
                if (Math.Abs(proposed.Left - other.Right) <= threshold) proposed.X = other.Right;
                if (Math.Abs(proposed.Right - other.Left) <= threshold)
                {
                    proposed.Width = other.Left - proposed.Left;
                }
                if (Math.Abs(proposed.Right - other.Right) <= threshold)
                {
                    proposed.Width = other.Right - proposed.Left;
                }
                if (Math.Abs(proposed.Top - other.Top) <= threshold) proposed.Y = other.Top;
                if (Math.Abs(proposed.Top - other.Bottom) <= threshold) proposed.Y = other.Bottom;
                if (Math.Abs(proposed.Bottom - other.Top) <= threshold)
                {
                    proposed.Height = other.Top - proposed.Top;
                }
                if (Math.Abs(proposed.Bottom - other.Bottom) <= threshold)
                {
                    proposed.Height = other.Bottom - proposed.Top;
                }
            }
        }

        proposed.X = Math.Clamp(proposed.X, 0, maxX);
        proposed.Y = Math.Clamp(proposed.Y, 0, maxY);
        proposed.Width = Math.Clamp(
            proposed.Width,
            source.MinimumSize.Width,
            Math.Max(source.MinimumSize.Width, ClientSize.Width));
        proposed.Height = Math.Clamp(
            proposed.Height,
            source.MinimumSize.Height,
            Math.Max(source.MinimumSize.Height, ClientSize.Height));
        return proposed;
    }

    public void SaveLayout()
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            var layout = _hosts.ToDictionary(
                host => host.ModuleId,
                host =>
                {
                    if (_floatingWindows.TryGetValue(host.ModuleId, out var floating))
                    {
                        return new LayoutEntry(
                            host.Bounds.X,
                            host.Bounds.Y,
                            host.Bounds.Width,
                            host.Bounds.Height,
                            floating.Visible,
                            true,
                            floating.Location.X,
                            floating.Location.Y,
                            floating.Width,
                            floating.Height);
                    }

                    return new LayoutEntry(
                        host.Bounds.X,
                        host.Bounds.Y,
                        host.Bounds.Width,
                        host.Bounds.Height,
                        host.Visible,
                        false,
                        0,
                        0,
                        0,
                        0);
                });
            var directory = Path.GetDirectoryName(LayoutPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(
                LayoutPath,
                JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Layout persistence is optional; dragging must not fail if disk access is denied.
        }
    }

    private void LoadLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath))
            {
                return;
            }

            var layout = JsonSerializer.Deserialize<Dictionary<string, LayoutEntry>>(
                File.ReadAllText(LayoutPath));
            if (layout is null)
            {
                return;
            }

            foreach (var host in _hosts)
            {
                if (layout.TryGetValue(host.ModuleId, out var entry))
                {
                    if (entry.Floating)
                    {
                        FloatModule(
                            host,
                            new Point(entry.FloatingX + 80, entry.FloatingY + 16));
                        if (GetFloatingWindow(host) is { } floating)
                        {
                            var requested = new Rectangle(
                                entry.FloatingX,
                                entry.FloatingY,
                                Math.Max(floating.MinimumSize.Width, entry.FloatingWidth),
                                Math.Max(floating.MinimumSize.Height, entry.FloatingHeight));
                            var screen = Screen.FromRectangle(requested).WorkingArea;
                            requested.Width = Math.Min(requested.Width, screen.Width);
                            requested.Height = Math.Min(requested.Height, screen.Height);
                            requested.X = Math.Clamp(
                                requested.X,
                                screen.Left,
                                Math.Max(screen.Left, screen.Right - requested.Width));
                            requested.Y = Math.Clamp(
                                requested.Y,
                                screen.Top,
                                Math.Max(screen.Top, screen.Bottom - requested.Height));
                            floating.Bounds = requested;
                            floating.Visible = entry.Visible;
                        }
                        continue;
                    }

                    host.Bounds = Constrain(host, new Rectangle(
                        entry.X,
                        entry.Y,
                        Math.Max(host.MinimumSize.Width, entry.Width),
                        Math.Max(host.MinimumSize.Height, entry.Height)));
                    host.Visible = entry.Visible;
                }
            }
        }
        catch
        {
            // Invalid saved layouts fall back to default positions.
        }
    }

    private Rectangle Constrain(DockableModuleHost host, Rectangle bounds)
    {
        bounds.Width = Math.Clamp(
            bounds.Width,
            host.MinimumSize.Width,
            Math.Max(host.MinimumSize.Width, ClientSize.Width));
        bounds.Height = Math.Clamp(
            bounds.Height,
            host.MinimumSize.Height,
            Math.Max(host.MinimumSize.Height, ClientSize.Height));
        bounds.X = Math.Clamp(bounds.X, 0, Math.Max(0, ClientSize.Width - bounds.Width));
        bounds.Y = Math.Clamp(bounds.Y, 0, Math.Max(0, ClientSize.Height - bounds.Height));
        return bounds;
    }

    private void SetModules((string Id, Rectangle Bounds)[] entries)
    {
        foreach (var entry in entries)
        {
            var host = FindModule(entry.Id);
            if (host is not null)
            {
                host.PrepareForExternalLayout();
                host.Bounds = Constrain(host, entry.Bounds);
            }
        }
    }

    private ContextMenuStrip BuildDockMenu(DockableModuleHost host)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = AppTheme.MenuBackground,
            ForeColor = AppTheme.Text,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        AddDockItem(menu, "停靠左侧", host, DockSide.Left);
        AddDockItem(menu, "停靠右侧", host, DockSide.Right);
        AddDockItem(menu, "停靠顶部", host, DockSide.Top);
        AddDockItem(menu, "停靠底部", host, DockSide.Bottom);
        AddDockItem(menu, "最大化", host, DockSide.Maximize);
        menu.Items.Add(new ToolStripSeparator());
        var floatItem = new ToolStripMenuItem("浮动为独立窗口")
        {
            BackColor = AppTheme.MenuBackground,
            ForeColor = AppTheme.Text
        };
        floatItem.Click += (_, _) => FloatModule(host, Cursor.Position);
        menu.Items.Add(floatItem);
        var dockFloatingItem = new ToolStripMenuItem("停靠回主窗口")
        {
            BackColor = AppTheme.MenuBackground,
            ForeColor = AppTheme.Text
        };
        dockFloatingItem.Click += (_, _) => DockFloatingModule(host);
        menu.Items.Add(dockFloatingItem);
        menu.Opening += (_, _) =>
        {
            var floating = IsModuleFloating(host);
            floatItem.Enabled = !floating;
            dockFloatingItem.Enabled = floating && !host.PreventDocking;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem dockItem &&
                    dockItem.Text?.StartsWith("停靠", StringComparison.Ordinal) == true)
                {
                    dockItem.Enabled = !host.PreventDocking;
                }
            }
        };
        menu.Items.Add(new ToolStripSeparator());
        var restore = new ToolStripMenuItem("恢复拖动位置")
        {
            BackColor = AppTheme.MenuBackground,
            ForeColor = AppTheme.Text
        };
        restore.Click += (_, _) => RestoreModule(host);
        menu.Items.Add(restore);
        return menu;
    }

    private void AddDockItem(
        ContextMenuStrip menu,
        string text,
        DockableModuleHost host,
        DockSide side)
    {
        var item = new ToolStripMenuItem(text)
        {
            BackColor = AppTheme.MenuBackground,
            ForeColor = AppTheme.Text
        };
        item.Click += (_, _) => DockModule(host, side);
        menu.Items.Add(item);
    }

    private sealed record LayoutEntry(
        int X,
        int Y,
        int Width,
        int Height,
        bool Visible,
        bool Floating,
        int FloatingX,
        int FloatingY,
        int FloatingWidth,
        int FloatingHeight);
}
