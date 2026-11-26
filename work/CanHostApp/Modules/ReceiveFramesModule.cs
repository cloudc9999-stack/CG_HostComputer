using System.Text;
using CanHost.Models;

namespace CanHost.Modules;

public sealed class ReceiveFramesModule : UserControl
{
    private static readonly Color BackgroundColor = Color.FromArgb(20, 30, 48);
    private static readonly Color FieldColor = Color.FromArgb(14, 23, 39);
    private static Color BorderColor => AppTheme.Border;
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color MutedColor = Color.FromArgb(139, 155, 179);
    private static readonly Color RxColor = Color.FromArgb(125, 211, 252);

    private readonly DataGridView _grid = new();
    private readonly Label _countLabel = new();
    private readonly CheckBox _pauseCheck = new();
    private readonly Button _exportButton = new();
    private readonly Button _clearButton = new();
    private readonly Panel _gridHost = new();
    private readonly TabControl _filterTabs = new();
    private readonly CheckBox _idFilterEnabled = new();
    private readonly TextBox _idFilterText = new();
    private readonly CheckBox _dlcFilterEnabled = new();
    private readonly NumericUpDown _dlcFilterValue = new();
    private readonly List<CanFrame> _frames = [];
    private long _frameCount;

    public ReceiveFramesModule()
    {
        BackColor = BackgroundColor;
        Dock = DockStyle.Fill;
        Padding = new Padding(18, 14, 18, 16);
        BuildInterface();
    }

    public event Action<CanFrame>? FrameActivated;
    public event EventHandler? FramesCleared;

    public long FrameCount => _frameCount;

    public void AddFrame(CanFrame frame)
    {
        if (frame.Direction != FrameDirection.Rx)
        {
            return;
        }

        _frameCount++;
        _countLabel.Text = $"接收  {_frameCount:N0}";
        if (_pauseCheck.Checked)
        {
            return;
        }

        _frames.Add(frame);
        if (_frames.Count > 5000)
        {
            _frames.RemoveAt(0);
        }
        if (!MatchesFilters(frame))
        {
            return;
        }

        AddGridRow(frame);
        if (_grid.Rows.Count > 5000)
        {
            _grid.Rows.RemoveAt(0);
        }
        ScrollToLatest();
    }

    private void AddGridRow(CanFrame frame)
    {
        var index = _grid.Rows.Add(
            frame.Timestamp.ToString("HH:mm:ss.fff"),
            frame.TypeText,
            frame.IdText,
            frame.Dlc,
            frame.DataText);
        var row = _grid.Rows[index];
        row.Tag = frame;
        row.Cells["id"].Style.ForeColor = RxColor;

        if (_grid.Rows.Count > 0)
        {
            _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1;
        }
    }

    private void ScrollToLatest()
    {
        if (_grid.Rows.Count > 0)
        {
            _grid.FirstDisplayedScrollingRowIndex = _grid.Rows.Count - 1;
        }
    }

    private bool MatchesFilters(CanFrame frame)
    {
        if (_idFilterEnabled.Checked)
        {
            var text = _idFilterText.Text.Trim();
            var normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? text[2..]
                : text;
            if (!uint.TryParse(
                    normalized,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var id) ||
                frame.Id != id)
            {
                return false;
            }
        }
        if (_dlcFilterEnabled.Checked && frame.Dlc != (byte)_dlcFilterValue.Value)
        {
            return false;
        }
        return true;
    }

    private void RebuildGrid()
    {
        _grid.Rows.Clear();
        foreach (var frame in _frames.Where(MatchesFilters))
        {
            AddGridRow(frame);
        }
        ScrollToLatest();
    }

    public void ClearFrames()
    {
        _grid.Rows.Clear();
        _frames.Clear();
        _frameCount = 0;
        _countLabel.Text = "接收  0";
        FramesCleared?.Invoke(this, EventArgs.Empty);
    }

    private void BuildInterface()
    {
        Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        var title = new Label
        {
            Text = "接收帧",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(18, 14)
        };

        _countLabel.Text = "接收  0";
        _countLabel.AutoSize = true;
        _countLabel.ForeColor = RxColor;
        _countLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _countLabel.Location = new Point(90, 16);

        _pauseCheck.Text = "暂停显示";
        _pauseCheck.AutoSize = true;
        _pauseCheck.ForeColor = TextColor;
        _pauseCheck.FlatStyle = FlatStyle.Flat;
        _pauseCheck.Font = new Font("Microsoft YaHei UI", 9F);
        _pauseCheck.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _pauseCheck.Location = new Point(Width - 320, 9);

        StyleSecondaryButton(_exportButton);
        _exportButton.Text = "导出 CSV";
        _exportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _exportButton.Bounds = new Rectangle(Width - 208, 8, 72, 32);
        _exportButton.Click += (_, _) => ExportCsv();

        StyleSecondaryButton(_clearButton);
        _clearButton.Text = "清空";
        _clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _clearButton.Bounds = new Rectangle(Width - 132, 8, 58, 32);
        _clearButton.Click += (_, _) => ClearFrames();

        ConfigureGrid();
        _filterTabs.Location = new Point(18, 48);
        _filterTabs.Size = new Size(Math.Max(300, Width - 36), 64);
        _filterTabs.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
        _filterTabs.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var idPage = new TabPage("过滤 ID")
        {
            BackColor = BackgroundColor,
            ForeColor = TextColor
        };
        _idFilterEnabled.Text = "启用 ID";
        _idFilterEnabled.AutoSize = true;
        _idFilterEnabled.ForeColor = TextColor;
        _idFilterEnabled.Location = new Point(8, 8);
        _idFilterText.Location = new Point(86, 6);
        _idFilterText.Size = new Size(150, 25);
        _idFilterText.BackColor = FieldColor;
        _idFilterText.ForeColor = TextColor;
        _idFilterText.BorderStyle = BorderStyle.FixedSingle;
        _idFilterText.Font = new Font("Cascadia Mono", 9F);
        _idFilterText.PlaceholderText = "例如 05 或 0x05";
        _idFilterEnabled.CheckedChanged += (_, _) => RebuildGrid();
        _idFilterText.TextChanged += (_, _) => RebuildGrid();
        idPage.Controls.AddRange([_idFilterEnabled, _idFilterText]);

        var dlcPage = new TabPage("过滤 DLC")
        {
            BackColor = BackgroundColor,
            ForeColor = TextColor
        };
        _dlcFilterEnabled.Text = "启用 DLC";
        _dlcFilterEnabled.AutoSize = true;
        _dlcFilterEnabled.ForeColor = TextColor;
        _dlcFilterEnabled.Location = new Point(8, 8);
        _dlcFilterValue.Minimum = 0;
        _dlcFilterValue.Maximum = 8;
        _dlcFilterValue.Value = 8;
        _dlcFilterValue.Location = new Point(98, 5);
        _dlcFilterValue.Size = new Size(70, 25);
        _dlcFilterValue.BackColor = FieldColor;
        _dlcFilterValue.ForeColor = TextColor;
        _dlcFilterValue.BorderStyle = BorderStyle.FixedSingle;
        _dlcFilterEnabled.CheckedChanged += (_, _) => RebuildGrid();
        _dlcFilterValue.ValueChanged += (_, _) => RebuildGrid();
        dlcPage.Controls.AddRange([_dlcFilterEnabled, _dlcFilterValue]);
        _filterTabs.TabPages.AddRange([idPage, dlcPage]);

        _grid.Dock = DockStyle.Fill;
        _gridHost.BackColor = FieldColor;
        _gridHost.Padding = new Padding(1);
        _gridHost.Controls.Add(_grid);
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 &&
                e.RowIndex < _grid.Rows.Count &&
                _grid.Rows[e.RowIndex].Tag is CanFrame frame)
            {
                FrameActivated?.Invoke(frame);
            }
        };

        Controls.AddRange(
        [
            _gridHost,
            _filterTabs,
            title,
            _countLabel,
            _pauseCheck,
            _exportButton,
            _clearButton
        ]);
        Resize += (_, _) =>
        {
            var compact = ClientSize.Width < 520;
            _pauseCheck.Location = compact ? new Point(18, 43) : new Point(ClientSize.Width - 320, 9);
            _exportButton.Location = compact
                ? new Point(112, 40)
                : new Point(ClientSize.Width - 208, 8);
            _clearButton.Location = compact
                ? new Point(190, 40)
                : new Point(ClientSize.Width - 132, 8);
            _filterTabs.Bounds = new Rectangle(
                18,
                48,
                Math.Max(280, ClientSize.Width - 36),
                64);
            _gridHost.Bounds = new Rectangle(
                18,
                120,
                Math.Max(20, ClientSize.Width - 36),
                Math.Max(20, ClientSize.Height - 136));
        };
    }

    private void ConfigureGrid()
    {
        _grid.BackgroundColor = FieldColor;
        _grid.BorderStyle = BorderStyle.None;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.GridColor = Color.FromArgb(34, 48, 68);
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(24, 37, 57),
            ForeColor = MutedColor,
            SelectionBackColor = Color.FromArgb(24, 37, 57),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Padding = new Padding(8, 0, 8, 0)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = FieldColor,
            ForeColor = TextColor,
            SelectionBackColor = Color.FromArgb(30, 58, 80),
            SelectionForeColor = TextColor,
            Font = new Font("Cascadia Mono", 9F),
            Padding = new Padding(8, 0, 8, 0)
        };
        _grid.RowTemplate.Height = 31;
        _grid.ColumnHeadersHeight = 36;
        _grid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        AddColumn("time", "时间", 145);
        AddColumn("type", "帧类型", 135);
        AddColumn("id", "CAN ID", 110);
        AddColumn("dlc", "DLC", 70);
        AddColumn("data", "数据", 300);
    }

    private void AddColumn(string name, string title, int width)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = title,
            Width = width,
            MinimumWidth = Math.Min(60, width)
        };
        _grid.Columns.Add(column);
    }

    private void ExportCsv()
    {
        if (_grid.Rows.Count == 0)
        {
            ShowWarning("当前没有可导出的接收帧。");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出接收帧记录",
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"CAN_RX_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("时间,帧类型,CAN ID,DLC,数据");
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var cells = row.Cells.Cast<DataGridViewCell>()
                    .Select(cell => CsvEscape(cell.Value?.ToString() ?? string.Empty));
                builder.AppendLine(string.Join(",", cells));
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            ShowWarning($"导出失败：\n\n{ex.Message}");
        }
    }

    private void ShowWarning(string message)
    {
        MessageBox.Show(
            FindForm(),
            message,
            "接收帧",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static string CsvEscape(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
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
}
