using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Gather.App;
using Microsoft.Win32;

internal static class StartupChecks
{
    [SupportedOSPlatform("windows")]
    public static void Run(Action<string, Action> check, bool skipRegistry = false)
    {
        void RegistryCheck(string name, Action test)
        {
            if (skipRegistry) Console.WriteLine("SKIP " + name + " (--skip-registry-checks)");
            else check(name, test);
        }
        RegistryCheck("Startup registration enables, reloads, updates its command and disables without touching other values", () => WithRegistry((registration, path) =>
        {
            Expect(registration.ReadCommand() == null);
            using (var key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue("AnotherApp", "unchanged");
            string executable = Environment.ProcessPath!;
            registration.SetEnabled(true, executable);
            Expect(new StartupRegistration(path).ReadCommand() == $"\"{executable}\"");
            registration.SetEnabled(true, executable, @"C:\Gather profiles\Personal");
            Expect(registration.ReadCommand() == $"\"{executable}\" --data-dir \"" + @"C:\Gather profiles\Personal" + "\"");
            registration.SetEnabled(false, "");
            registration.SetEnabled(false, "");
            Expect(new StartupRegistration(path).ReadCommand() == null);
            using var read = Registry.CurrentUser.OpenSubKey(path);
            Expect((string?)read?.GetValue("AnotherApp") == "unchanged");
        }));
        check("Startup command preserves spaces, Unicode, shell characters and trailing slashes as arguments", () =>
        {
            string executable = @"C:\Gather & friends\Gather.exe";
            foreach (string data in new[] { @"C:\Users\Zoë\Gather data", @"C:\Gather data\", @"C:\" })
            {
                var args = Parse(StartupRegistration.CommandLine(executable, data));
                Expect(args.SequenceEqual(new[] { executable, "--data-dir", data }));
            }
        });
        RegistryCheck("Invalid startup paths and missing application keep the existing registration", () => WithRegistry((registration, _) =>
        {
            registration.SetEnabled(true, Environment.ProcessPath!);
            string? original = registration.ReadCommand();
            foreach (string executable in new[] { "Gather.exe", "C:\\bad\"path\\Gather.exe", "C:\\bad\npath\\Gather.exe", @"C:\Gather.cmd", "C:\\" + new string('a', 260) + "\\Gather.exe", @"C:\Gather-missing-" + Guid.NewGuid().ToString("N") + @"\Gather.exe" })
            {
                bool rejected = false;
                try { registration.SetEnabled(true, executable); }
                catch (InvalidOperationException) { rejected = true; }
                Expect(rejected && registration.ReadCommand() == original);
            }
        }));
    }

    [SupportedOSPlatform("windows")]
    private static void WithRegistry(Action<StartupRegistration, string> test)
    {
        // A private, non-startup key: the tests never register anything to run at sign-in.
        string path = @"Software\Gather-Startup-Tests-" + Guid.NewGuid().ToString("N");
        try { test(new StartupRegistration(path), path); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    private static void Expect(bool condition) { if (!condition) throw new Exception("Startup check failed."); }
    private static string[] Parse(string command)
    {
        nint argv = CommandLineToArgvW(command, out int count);
        if (argv == 0) throw new Exception("Windows could not parse the startup command.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(argv); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
