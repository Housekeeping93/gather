using Microsoft.UI.Xaml;
namespace Gather.App;

public partial class App : Application
{
    private MainWindow? _window;
    private FileStream? _instanceLock;
    private FileSystemWatcher? _activationWatcher;
    public App()
    {
        if (Environment.GetCommandLineArgs().Contains("--diagnostics"))
            NativeCrashLog.Install();
        UnhandledException += (_, e) => StartupLog.Write("unhandled", e.Exception);
        StartupLog.Write($"app-initialize version={typeof(App).Assembly.GetName().Version} path={AppContext.BaseDirectory}");
        InitializeComponent();
        StartupLog.Write("app-initialized");
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "--data-dir");
        var directory = Path.GetFullPath(index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gather"));
        Directory.CreateDirectory(directory);
        try
        {
            _instanceLock = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
        catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33)
        {
            StartupLog.Write("activating-existing-instance");
            File.WriteAllText(Path.Combine(directory, "activate.request"), DateTimeOffset.UtcNow.ToString("O"));
            Exit();
            return;
        }
        StartupLog.Write("creating-window");
        _window = new MainWindow();
        StartupLog.Write("window-created");
        _activationWatcher = new FileSystemWatcher(directory, "activate.request") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
        _activationWatcher.Changed += (_, _) => _window.DispatcherQueue.TryEnqueue(() => _window.ShowWindow());
        _activationWatcher.Created += (_, _) => _window.DispatcherQueue.TryEnqueue(() => _window.ShowWindow());
        _window.Closed += (_, _) => { _activationWatcher.Dispose(); _instanceLock.Dispose(); };
        _window.Activate();
        StartupLog.Write("window-activated");
    }
}
