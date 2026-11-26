using System.Text.Json;

namespace CanHost;

public sealed record DisplayState(
    bool ReceiveExpanded,
    bool ReceiveTicker,
    bool MainPressurePlotVisible,
    bool Branch1PressurePlotVisible,
    bool Branch2PressurePlotVisible,
    bool Branch3PressurePlotVisible,
    bool Branch4PressurePlotVisible,
    bool SupplyControlVisible,
    bool SendVisible)
{
    public static DisplayState Empty { get; } = new(
        ReceiveExpanded: false,
        ReceiveTicker: false,
        MainPressurePlotVisible: false,
        Branch1PressurePlotVisible: false,
        Branch2PressurePlotVisible: false,
        Branch3PressurePlotVisible: false,
        Branch4PressurePlotVisible: false,
        SupplyControlVisible: false,
        SendVisible: false);
}

public static class DisplayStateStore
{
    public static string? PathOverride { get; set; }

    private static string StorePath => PathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CanHost",
        "display-v5.3.json");

    public static DisplayState Load()
    {
        try
        {
            return File.Exists(StorePath)
                ? JsonSerializer.Deserialize<DisplayState>(File.ReadAllText(StorePath)) ?? DisplayState.Empty
                : DisplayState.Empty;
        }
        catch
        {
            return DisplayState.Empty;
        }
    }

    public static void Save(DisplayState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(StorePath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Display persistence is optional.
        }
    }
}
