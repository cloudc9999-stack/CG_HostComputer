using CanHost.Controls;

namespace CanHost;

public sealed class PlotSettingsForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(12, 18, 32);
    private static readonly Color FieldColor = Color.FromArgb(14, 23, 39);
    private static readonly Color BorderColor = Color.FromArgb(48, 64, 88);
    private static readonly Color TextColor = Color.FromArgb(229, 237, 247);
    private static readonly Color MutedColor = Color.FromArgb(139, 155, 179);
    private static readonly Color AccentColor = Color.FromArgb(56, 189, 248);
    private static readonly Color DangerColor = Color.FromArgb(248, 113, 113);

    private readonly NumericUpDown _minimumInput = new();
    private readonly NumericUpDown _maximumInput = new();
    private readonly NumericUpDown _timeWindowInput = new();
    private readonly Label _errorLabel = new();

    public PlotSettingsForm(double minimum, double maximum, int timeWindowSeconds)
    {
        Text = "气压绘图配置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 350);
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9F);

        var title = new Label
        {
            Text = "气压绘图配置",
            Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(26, 20)
        };
        var subtitle = new Label
        {
            Text = "固定坐标范围与横轴显示窗口",
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = MutedColor,
            AutoSize = true,
            Location = new Point(28, 55)
        };

        var minimumLabel = FieldLabel("纵坐标下限");
        minimumLabel.Location = new Point(28, 92);
        ConfigureNumeric(_minimumInput, minimum);
        _minimumInput.Location = new Point(28, 114);

        var maximumLabel = FieldLabel("纵坐标上限");
        maximumLabel.Location = new Point(240, 92);
        ConfigureNumeric(_maximumInput, maximum);
        _maximumInput.Location = new Point(240, 114);

        var timeLabel = FieldLabel("横坐标显示最长时间（秒）");
        timeLabel.Location = new Point(28, 164);
        _timeWindowInput.Minimum = 1;
        _timeWindowInput.Maximum = 86_400;
        _timeWindowInput.Value = Math.Clamp(timeWindowSeconds, 1, 86_400);
        _timeWindowInput.Location = new Point(28, 186);
        _timeWindowInput.Size = new Size(180, 31);
        _timeWindowInput.BackColor = FieldColor;
        _timeWindowInput.ForeColor = TextColor;
        _timeWindowInput.BorderStyle = BorderStyle.FixedSingle;
        _timeWindowInput.Font = new Font("Cascadia Mono", 10F);

        _errorLabel.AutoSize = false;
        _errorLabel.Size = new Size(404, 42);
        _errorLabel.Location = new Point(28, 229);
        _errorLabel.ForeColor = DangerColor;
        _errorLabel.Font = new Font("Microsoft YaHei UI", 8.7F);

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel
        };
        StyleSecondaryButton(cancelButton);
        cancelButton.Location = new Point(242, 286);
        cancelButton.Size = new Size(88, 38);

        var applyButton = new Button { Text = "应用" };
        StylePrimaryButton(applyButton);
        applyButton.Location = new Point(342, 286);
        applyButton.Size = new Size(90, 38);
        applyButton.Click += (_, _) => Apply();

        Controls.AddRange(
        [
            title,
            subtitle,
            minimumLabel,
            _minimumInput,
            maximumLabel,
            _maximumInput,
            timeLabel,
            _timeWindowInput,
            _errorLabel,
            cancelButton,
            applyButton
        ]);
        CancelButton = cancelButton;
        AcceptButton = applyButton;
        ThemeApplicator.Apply(this);
    }

    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public int TimeWindowSeconds => (int)_timeWindowInput.Value;

    private void Apply()
    {
        var minimum = (double)_minimumInput.Value;
        var maximum = (double)_maximumInput.Value;
        if (minimum >= maximum)
        {
            _errorLabel.Text = "纵坐标上限必须大于纵坐标下限。";
            return;
        }

        Minimum = minimum;
        Maximum = maximum;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = MutedColor,
        Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold)
    };

    private static void ConfigureNumeric(NumericUpDown input, double value)
    {
        input.Minimum = -1_000_000_000;
        input.Maximum = 1_000_000_000;
        input.DecimalPlaces = 0;
        input.ThousandsSeparator = true;
        input.Value = Math.Clamp((decimal)value, input.Minimum, input.Maximum);
        input.Location = new Point(0, 0);
        input.Size = new Size(182, 31);
        input.BackColor = FieldColor;
        input.ForeColor = TextColor;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = new Font("Cascadia Mono", 10F);
    }

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = AccentColor;
        button.ForeColor = Color.FromArgb(7, 27, 43);
        button.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
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
