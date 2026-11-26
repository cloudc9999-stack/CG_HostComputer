using System.Drawing;

namespace CanHost;

public enum AppThemeMode
{
    Dark,
    Light
}

public static class AppTheme
{
    public static event EventHandler? Changed;

    public static string? SettingPathOverride { get; set; }

    private static string SettingsPath => SettingPathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CanHost",
        "theme.txt");

    public static AppThemeMode Mode { get; private set; } = AppThemeMode.Dark;
    public static bool IsLight => Mode == AppThemeMode.Light;
    private static ThemePalette Current => IsLight ? ThemePalette.Light : ThemePalette.Dark;

    public static Color Background => Current.Background;
    public static Color Card => Current.Card;
    public static Color Field => Current.Field;
    public static Color Border => Current.Border;
    public static Color Text => Current.Text;
    public static Color Muted => Current.Muted;
    public static Color Hint => Current.Hint;
    public static Color Accent => Current.Accent;
    public static Color Success => Current.Success;
    public static Color Danger => Current.Danger;
    public static Color Rx => Current.Rx;
    public static Color Tx => Current.Tx;
    public static Color MenuBackground => Current.MenuBackground;
    public static Color HeaderBackground => Current.HeaderBackground;
    public static Color HeaderSurface => Current.HeaderSurface;
    public static Color GridHeader => Current.GridHeader;
    public static Color GridLine => Current.GridLine;
    public static Color Selection => Current.Selection;
    public static Color SecondaryButton => Current.SecondaryButton;
    public static Color DragBar => Current.DragBar;
    public static Color Splitter => Current.Splitter;
    public static Color HighlightSurface => Current.HighlightSurface;
    public static Color StopBackground => Current.StopBackground;
    public static Color StopText => Current.StopText;

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var value = File.ReadAllText(SettingsPath).Trim();
                Mode = string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)
                    ? AppThemeMode.Light
                    : AppThemeMode.Dark;
            }
        }
        catch
        {
            Mode = AppThemeMode.Dark;
        }
    }

    public static void SetTheme(AppThemeMode mode)
    {
        Mode = mode;
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(SettingsPath, mode.ToString());
        }
        catch
        {
            // Theme persistence is optional.
        }

        foreach (Form form in Application.OpenForms)
        {
            ThemeApplicator.Apply(form);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static Color MapColor(Color color)
    {
        if (color.A == 0)
        {
            return color;
        }

        foreach (var pair in ThemePalette.ColorPairs)
        {
            if (color.ToArgb() == pair.Dark.ToArgb() ||
                color.ToArgb() == pair.Light.ToArgb())
            {
                return pair.Select(Current);
            }
        }
        return color;
    }

    private sealed record ThemePalette(
        Color Background,
        Color Card,
        Color Field,
        Color Border,
        Color Text,
        Color Muted,
        Color Hint,
        Color Accent,
        Color Success,
        Color Danger,
        Color Rx,
        Color Tx,
        Color MenuBackground,
        Color HeaderBackground,
        Color HeaderSurface,
        Color GridHeader,
        Color GridLine,
        Color Selection,
        Color SecondaryButton,
        Color DragBar,
        Color Splitter,
        Color HighlightSurface,
        Color StopBackground,
        Color StopText)
    {
        public static ThemePalette Dark { get; } = new(
            Color.FromArgb(12, 18, 32),
            Color.FromArgb(20, 30, 48),
            Color.FromArgb(14, 23, 39),
            Color.FromArgb(48, 64, 88),
            Color.FromArgb(229, 237, 247),
            Color.FromArgb(139, 155, 179),
            Color.FromArgb(111, 133, 162),
            Color.FromArgb(56, 189, 248),
            Color.FromArgb(134, 239, 172),
            Color.FromArgb(248, 113, 113),
            Color.FromArgb(125, 211, 252),
            Color.FromArgb(134, 239, 172),
            Color.FromArgb(10, 16, 28),
            Color.FromArgb(16, 25, 41),
            Color.FromArgb(19, 54, 77),
            Color.FromArgb(24, 37, 57),
            Color.FromArgb(34, 48, 68),
            Color.FromArgb(30, 58, 80),
            Color.FromArgb(25, 38, 59),
            Color.FromArgb(20, 31, 49),
            Color.FromArgb(42, 60, 84),
            Color.FromArgb(20, 47, 70),
            Color.FromArgb(76, 41, 47),
            Color.FromArgb(254, 202, 202));

        public static ThemePalette Light { get; } = new(
            Color.FromArgb(242, 245, 249),
            Color.White,
            Color.FromArgb(248, 250, 252),
            Color.FromArgb(203, 213, 225),
            Color.FromArgb(30, 41, 59),
            Color.FromArgb(100, 116, 139),
            Color.FromArgb(100, 116, 139),
            Color.FromArgb(2, 132, 199),
            Color.FromArgb(21, 128, 61),
            Color.FromArgb(220, 38, 38),
            Color.FromArgb(3, 105, 161),
            Color.FromArgb(21, 128, 61),
            Color.White,
            Color.White,
            Color.FromArgb(224, 242, 254),
            Color.FromArgb(241, 245, 249),
            Color.FromArgb(226, 232, 240),
            Color.FromArgb(219, 234, 254),
            Color.FromArgb(241, 245, 249),
            Color.FromArgb(241, 245, 249),
            Color.FromArgb(203, 213, 225),
            Color.FromArgb(224, 242, 254),
            Color.FromArgb(254, 226, 226),
            Color.FromArgb(153, 27, 27));

        public static IReadOnlyList<(Color Dark, Color Light, Func<ThemePalette, Color> Select)> ColorPairs
        {
            get;
        } =
        [
            (Dark.Background, Light.Background, palette => palette.Background),
            (Dark.Card, Light.Card, palette => palette.Card),
            (Dark.Field, Light.Field, palette => palette.Field),
            (Dark.Border, Light.Border, palette => palette.Border),
            (Dark.Text, Light.Text, palette => palette.Text),
            (Dark.Muted, Light.Muted, palette => palette.Muted),
            (Dark.Hint, Light.Hint, palette => palette.Hint),
            (Dark.Accent, Light.Accent, palette => palette.Accent),
            (Dark.Success, Light.Success, palette => palette.Success),
            (Dark.Danger, Light.Danger, palette => palette.Danger),
            (Dark.Rx, Light.Rx, palette => palette.Rx),
            (Dark.Tx, Light.Tx, palette => palette.Tx),
            (Dark.MenuBackground, Light.MenuBackground, palette => palette.MenuBackground),
            (Dark.HeaderBackground, Light.HeaderBackground, palette => palette.HeaderBackground),
            (Dark.HeaderSurface, Light.HeaderSurface, palette => palette.HeaderSurface),
            (Dark.GridHeader, Light.GridHeader, palette => palette.GridHeader),
            (Dark.GridLine, Light.GridLine, palette => palette.GridLine),
            (Dark.Selection, Light.Selection, palette => palette.Selection),
            (Dark.SecondaryButton, Light.SecondaryButton, palette => palette.SecondaryButton),
            (Dark.DragBar, Light.DragBar, palette => palette.DragBar),
            (Dark.Splitter, Light.Splitter, palette => palette.Splitter),
            (Dark.HighlightSurface, Light.HighlightSurface, palette => palette.HighlightSurface),
            (Dark.StopBackground, Light.StopBackground, palette => palette.StopBackground),
            (Dark.StopText, Light.StopText, palette => palette.StopText)
        ];
    }
}

public static class ThemeApplicator
{
    public static void Apply(Control root)
    {
        if (root is Form form)
        {
            form.BackColor = AppTheme.Background;
            form.ForeColor = AppTheme.Text;
        }
        ApplyControl(root);
        root.Invalidate(true);
    }

    private static void ApplyControl(Control control)
    {
        control.BackColor = AppTheme.MapColor(control.BackColor);
        control.ForeColor = AppTheme.MapColor(control.ForeColor);

        if (control is Button button)
        {
            button.FlatAppearance.BorderColor = AppTheme.MapColor(button.FlatAppearance.BorderColor);
        }
        if (control is DataGridView grid)
        {
            grid.GridColor = AppTheme.GridLine;
            grid.BackgroundColor = AppTheme.Field;
            grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.GridHeader;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = AppTheme.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = AppTheme.GridHeader;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = AppTheme.Muted;
            grid.DefaultCellStyle.BackColor = AppTheme.Field;
            grid.DefaultCellStyle.ForeColor = AppTheme.Text;
            grid.DefaultCellStyle.SelectionBackColor = AppTheme.Selection;
            grid.DefaultCellStyle.SelectionForeColor = AppTheme.Text;
        }
        if (control is ToolStrip strip)
        {
            strip.BackColor = AppTheme.MenuBackground;
            strip.ForeColor = AppTheme.Text;
            strip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
            ApplyToolStripItems(strip.Items);
        }
        if (control.ContextMenuStrip is not null)
        {
            ApplyToolStrip(control.ContextMenuStrip);
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child);
        }
    }

    private static void ApplyToolStrip(ToolStrip strip)
    {
        strip.BackColor = AppTheme.MenuBackground;
        strip.ForeColor = AppTheme.Text;
        strip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());
        ApplyToolStripItems(strip.Items);
    }

    private static void ApplyToolStripItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = AppTheme.MapColor(item.BackColor);
            item.ForeColor = AppTheme.MapColor(item.ForeColor);
            if (item is ToolStripMenuItem menuItem)
            {
                if (menuItem.HasDropDownItems)
                {
                    ApplyToolStrip(menuItem.DropDown);
                }
                ApplyToolStripItems(menuItem.DropDownItems);
            }
        }
    }
}

public sealed class ThemeColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => AppTheme.Selection;
    public override Color MenuItemBorder => AppTheme.Accent;
    public override Color MenuItemSelectedGradientBegin => AppTheme.Selection;
    public override Color MenuItemSelectedGradientEnd => AppTheme.Selection;
    public override Color ToolStripDropDownBackground => AppTheme.MenuBackground;
    public override Color ImageMarginGradientBegin => AppTheme.MenuBackground;
    public override Color ImageMarginGradientMiddle => AppTheme.MenuBackground;
    public override Color ImageMarginGradientEnd => AppTheme.MenuBackground;
    public override Color MenuBorder => AppTheme.Border;
}
