using System.Security.Cryptography;
using System.Text.Json;

namespace Gather.Signal;

public sealed class RuntimeManifest
{
    public string? SignalCliVersion { get; set; }
    public int JavaMajorVersion { get; set; }
    public RuntimeFile[]? Files { get; set; }
    public sealed class RuntimeFile
    {
        public string? File { get; set; }
        public string? SHA256 { get; set; }
    }
    public static readonly string[] RequiredFiles = [
        "java/bin/java.exe", "java/bin/server/jvm.dll", "java/lib/modules", "java/release",
        $"signal-cli/lib/signal-cli-{SignalRuntime.TestedProtocolVersion}.jar"
    ];

    // A completed import must inventory the entire distribution. Merely copying
    // java.exe and the CLI jar cannot make an incomplete installation available.
    public static bool IsValid(string directory)
    {
        try
        {
            string root = Path.GetFullPath(directory);
            if (!Directory.Exists(root) || IsLink(root)) return false;
            string manifestPath = Path.Combine(root, "runtime-manifest.json");
            if (!File.Exists(manifestPath) || IsLink(manifestPath) || new FileInfo(manifestPath).Length > 4 * 1024 * 1024) return false;
            var manifest = JsonSerializer.Deserialize<RuntimeManifest>(File.ReadAllBytes(manifestPath));
            if (manifest?.SignalCliVersion != SignalRuntime.TestedProtocolVersion || manifest.JavaMajorVersion != 25 || manifest.Files is not { Length: > 0 }) return false;
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.File) || entry.SHA256 is not { Length: 64 } || !entry.SHA256.All(Uri.IsHexDigit)) return false;
                string relative = entry.File.Replace('\\', '/');
                if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/').Any(part => part is "" or "." or "..")) return false;
                if (!listed.Add(relative) || relative.Equals("runtime-manifest.json", StringComparison.OrdinalIgnoreCase)) return false;
                string current = root;
                foreach (var part in relative.Split('/'))
                {
                    current = Path.Combine(current, part);
                    if (IsLink(current)) return false;
                }
                if (!File.Exists(current)) return false;
                using var stream = File.OpenRead(current);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.SHA256, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (RequiredFiles.Any(required => !listed.Contains(required))) return false;
            if (!listed.Any(path => path.StartsWith("signal-cli/lib/libsignal-client-", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))) return false;
            var directories = new Stack<string>(); directories.Push(root);
            while (directories.TryPop(out var parent))
                foreach (string path in Directory.EnumerateFileSystemEntries(parent))
                {
                    if (IsLink(path)) return false;
                    if (Directory.Exists(path)) directories.Push(path);
                    else if (!path.Equals(manifestPath, StringComparison.OrdinalIgnoreCase) && !listed.Contains(Path.GetRelativePath(root, path).Replace('\\', '/'))) return false;
                }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or System.Security.SecurityException or NotSupportedException)
        { return false; }
    }
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
