using Gather.Signal;
using System.Security.Cryptography;
using System.Text.Json;

internal static class RuntimeChecks
{
    internal static void Run(Action<string, Action> check, string[] args)
    {
        bool export = args.Length == 2 && args[0] == "--runtime-fixtures";
        string fixtures = export ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "Gather-runtime-checks-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(fixtures)) throw new IOException("Use a new fixture directory.");
        Directory.CreateDirectory(fixtures);
        var results = new List<object>();
        void Case(string name, bool valid, Action<string, RuntimeManifest> modify)
        {
            check("Runtime inventory: " + name, () =>
            {
                string app = Path.Combine(fixtures, name), root = Path.Combine(app, "runtimes", "signal");
                var manifest = new RuntimeManifest { SignalCliVersion = "0.14.7", JavaMajorVersion = 25 };
                manifest.Files = RuntimeManifest.RequiredFiles.Append("signal-cli/lib/libsignal-client-test.jar").Select(relative =>
                {
                    string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    // Metadata-only fixtures; these bytes are never executed or linked.
                    File.WriteAllText(path, "Synthetic runtime fixture: " + relative);
                    return new RuntimeManifest.RuntimeFile { File = relative, SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
                }).ToArray();
                modify(root, manifest);
                if (name != "missing-manifest") File.WriteAllText(Path.Combine(root, "runtime-manifest.json"), name == "malformed-manifest" ? "{" : JsonSerializer.Serialize(manifest));
                if (SignalRuntime.IsAvailable(app) != valid) throw new Exception("Unexpected runtime validation result: " + name);
                results.Add(new { name, directory = root, valid });
            });
        }
        Case("complete", true, (_, _) => { });
        Case("missing-manifest", false, (_, _) => { });
        Case("malformed-manifest", false, (_, _) => { });
        Case("empty-inventory", false, (_, m) => m.Files = []);
        Case("changed-file", false, (root, _) => File.AppendAllText(Path.Combine(root, "java", "lib", "modules"), "changed"));
        Case("missing-file", false, (root, _) => File.Delete(Path.Combine(root, "java", "bin", "server", "jvm.dll")));
        Case("missing-required-entry", false, (root, m) =>
        {
            File.Delete(Path.Combine(root, "java", "lib", "modules"));
            m.Files = m.Files!.Where(f => f.File != "java/lib/modules").ToArray();
        });
        Case("missing-native-dependency", false, (root, m) =>
        {
            File.Delete(Path.Combine(root, "signal-cli", "lib", "libsignal-client-test.jar"));
            m.Files = m.Files!.Where(f => !f.File!.Contains("libsignal-client")).ToArray();
        });
        Case("wrong-java", false, (_, m) => m.JavaMajorVersion = 8);
        Case("wrong-cli", false, (_, m) => m.SignalCliVersion = "0.0.0");
        Case("unsafe-path", false, (_, m) => m.Files![0].File = "../outside.exe");
        Case("duplicate-path", false, (_, m) => m.Files = [.. m.Files!, new() { File = "JAVA\\BIN\\JAVA.EXE", SHA256 = m.Files![0].SHA256 }]);
        Case("unlisted-file", false, (root, _) => File.WriteAllText(Path.Combine(root, "extra.dll"), "not inventoried"));
        check("An unavailable or cancelled runtime never creates an account directory", () =>
        {
            string data = Path.Combine(fixtures, "unused-account");
            try { SignalRuntime.StartAsync(Path.Combine(fixtures, "missing-manifest"), data, default).GetAwaiter().GetResult(); throw new Exception("Missing runtime started."); }
            catch (FileNotFoundException) { }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { SignalRuntime.StartAsync(Path.Combine(fixtures, "complete"), data, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Cancelled runtime started."); }
            catch (OperationCanceledException) { }
            if (Directory.Exists(data)) throw new Exception("An account directory was unexpectedly created.");
        });
        if (export) File.WriteAllText(Path.Combine(fixtures, "expectations.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
