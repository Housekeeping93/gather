namespace Gather.Core;

public sealed record ShellMetrics(double TitleHeight, double ToolbarHeight, double RailWidth,
    double ButtonHeight, double TileSize, double PaneHeaderHeight, double DividerWidth, double OuterSpacing);

public static class ShellAppearance
{
    public static void Normalize(Settings state)
    {
        if (state.ShellDensity is not ("Compact" or "Comfortable")) state.ShellDensity = "Compact";
    }

    // DIPs: Windows applies display scaling once, independently of provider zoom.
    public static ShellMetrics For(string density, bool focusView)
    {
        var metrics = density == "Comfortable"
            ? new ShellMetrics(40, 56, 88, 40, 64, 52, 16, 12)
            : new ShellMetrics(32, 44, 72, 32, 52, 40, 12, 8);
        return focusView ? metrics with { ToolbarHeight = metrics.ButtonHeight + 8, RailWidth = 0, PaneHeaderHeight = 0, OuterSpacing = 4 } : metrics;
    }
}

public enum ShellShortcut { None, Previous, Next, QuickSwitch, FocusView }
public static class ShellShortcuts
{
    public static ShellShortcut Resolve(int key, bool control, bool shift, bool alt) => !control || alt ? ShellShortcut.None : key switch
    {
        9 => shift ? ShellShortcut.Previous : ShellShortcut.Next,
        0x4B when !shift => ShellShortcut.QuickSwitch,
        0x46 when shift => ShellShortcut.FocusView,
        _ => ShellShortcut.None
    };
}
