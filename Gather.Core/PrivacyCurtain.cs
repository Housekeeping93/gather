namespace Gather.Core;

public static class PrivacyCurtain
{
    public static readonly string[] Shortcuts = ["Ctrl+Shift+P", "Ctrl+Shift+L", "Ctrl+Shift+H"];
    public static void Normalize(Settings state)
    {
        if (!Shortcuts.Contains(state.PrivacyShortcut)) state.PrivacyShortcut = Shortcuts[0];
    }
    public static bool Matches(string shortcut, int key, bool control, bool shift, bool alt) =>
        control && shift && !alt && Shortcuts.Contains(shortcut) && key == shortcut[^1];
}
