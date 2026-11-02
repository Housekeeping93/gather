using Gather.Signal;
using System.Diagnostics;
using System.Text.Json;

internal static class RuntimeIntegrationChecks
{
    internal static async Task Run(string appDirectory, string reportDirectory)
    {
        string report = Path.GetFullPath(reportDirectory);
        if (Directory.Exists(report)) throw new IOException("Use a new empty runtime-check directory.");
        Directory.CreateDirectory(report);
        var checks = new Dictionary<string, bool>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            string data = Path.Combine(report, "unlinked-data");
            using (var runtime = await SignalRuntime.StartAsync(Path.GetFullPath(appDirectory), data, timeout.Token))
            {
                using var process = Process.GetProcessById(runtime.ProcessId);
                runtime.Connection.Start();
                var client = new SignalAccountClient(runtime.Connection);
                checks["emptyAccountList"] = !await client.RestoreAsync(timeout.Token);
                if (!checks["emptyAccountList"]) throw new IOException("Unexpected account in the isolated runtime check.");
                // Contact Signal's provisioning service, but never approve a link,
                // subscribe to messages, send, or write the linking secret to disk.
                string uri = await client.StartLinkAsync(timeout.Token);
                checks["provisioningUriReceived"] = uri.StartsWith("sgnl://linkdevice?");
                checks["linkFitsQr"] = LinkQrCode.Encode(uri).GetLength(0) == LinkQrCode.Size;
                uri = "";
                runtime.Dispose();
                checks["ownedProcessExited"] = process.WaitForExit(5000);
            }
            using (var restarted = await SignalRuntime.StartAsync(Path.GetFullPath(appDirectory), data, timeout.Token))
            {
                restarted.Connection.Start();
                checks["restartRemainsUnlinked"] = !await new SignalAccountClient(restarted.Connection).RestoreAsync(timeout.Token);
            }
            if (checks.Values.Any(passed => !passed)) throw new IOException("A runtime check failed.");
            File.WriteAllText(Path.Combine(report, "runtime-report.json"), JsonSerializer.Serialize(new { checks, accountsLinked = 0, messagesSent = 0 }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: Real runtime startup, empty account list, provisioning code, QR capacity, owned process shutdown and restart. No account linked or message sent.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(report, "runtime-report.json"), JsonSerializer.Serialize(new { checks, errorType = error.GetType().Name, rpcCode = (error as SignalRpcException)?.Code }));
            Console.Error.WriteLine("FAIL: Real Signal runtime check (" + error.GetType().Name + "). See the redacted report.");
            Environment.ExitCode = 1;
        }
    }
}
