namespace Gather.App;

internal static class StartupLog
{
    internal static void Write(string stage, Exception? error = null)
    {
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "--data-dir");
            var directory = Path.GetFullPath(index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gather"));
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.UtcNow:O} {stage}" + (error == null ? "" : $" {error.GetType().Name} 0x{error.HResult:X8} {error.StackTrace}") + "\n");
        }
        catch { }
    }
}
