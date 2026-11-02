using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Gather.App;

// Read Windows' registration instead of keeping a second, potentially stale setting.
[SupportedOSPlatform("windows")]
internal sealed class StartupRegistration(string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    internal const string ValueName = "Gather";

    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(registryPath);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetEnabled(bool enabled, string executable, string? dataDirectory = null)
    {
        if (enabled)
        {
            string command = CommandLine(executable, dataDirectory);
            if (!File.Exists(executable))
                throw new InvalidOperationException("Gather's application file could not be found. Reopen Gather from its saved folder and try again.");
            if (ReadCommand() == command) return;
            using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
            if (ReadCommand() != command)
                throw new IOException("Windows did not keep the startup setting. Try again.");
        }
        else
        {
            if (ReadCommand() == null) return;
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            if (ReadCommand() != null)
                throw new IOException("Windows did not remove the startup setting. Try again.");
        }
    }

    internal static string CommandLine(string executable, string? dataDirectory)
    {
        string command = QuotePath(executable);
        if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Open Gather from its application file before changing startup.");
        if (dataDirectory != null)
            command += " --data-dir " + QuotePath(dataDirectory);
        if (command.Length > 260)
            throw new InvalidOperationException("This folder path is too long for Windows startup. Move Gather to a shorter folder path and try again.");
        return command;
    }

    private static string QuotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.IndexOfAny(['"', '\0', '\r', '\n']) >= 0)
            throw new InvalidOperationException("Gather needs a valid, full folder path to start with Windows.");
        // Windows doubles a trailing backslash before a closing argument quote.
        int trailing = path.Length - path.TrimEnd('\\').Length;
        return "\"" + path + new string('\\', trailing) + "\"";
    }
}
