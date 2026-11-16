using System.Diagnostics;
using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Owns only the explicitly launched reader process and its stdout session events.</summary>
public static class PhysicalReaderProcessSupervisor
{
    public static async Task<int> RunAsync(
        string readerExecutable,
        PhysicalReaderSessionTracker tracker,
        Func<PhysicalReaderSessionSignal, long, CancellationToken, Task> onSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(onSession);
        if (string.IsNullOrWhiteSpace(readerExecutable) ||
            !Path.IsPathFullyQualified(readerExecutable) ||
            !string.Equals(Path.GetFileName(readerExecutable), "Rift.Automation.GuestReader.exe",
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(readerExecutable))
            throw new ArgumentException("An existing absolute GuestReader executable path is required.",
                nameof(readerExecutable));
        cancellationToken.ThrowIfCancellationRequested();
        // Fail before starting a memory-scanning process if its delivery cannot reach
        // the local physical receiver. Do not mutate the user's existing configuration.
        var configPath = Path.Combine(Path.GetDirectoryName(readerExecutable)!, "autofight-guest-reader.json");
        var configFile = new FileInfo(configPath);
        if (!configFile.Exists || configFile.Length is <= 0 or > 65_536)
            throw new InvalidDataException("A bounded reader configuration is required beside the executable.");
        using (var config = JsonDocument.Parse(File.ReadAllText(configPath)))
        {
            var root = config.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("processName").GetString() != "rift_x64")
                throw new InvalidDataException("Reader configuration must target physical rift_x64.");
            var delivery = root.GetProperty("delivery");
            var endpoint = delivery.GetProperty("endpoint").GetString();
            if (!delivery.GetProperty("allowLoopback").GetBoolean() ||
                !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" ||
                uri.AbsolutePath != "/observation" || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) || uri.Port is < 1024 or > 65535)
                throw new InvalidDataException("Reader must explicitly deliver to the local physical receiver.");
            var token = delivery.GetProperty("token").GetString();
            if (token is null || token.Length is < 32 or > 512 || token.Any(c => c is < '!' or > '~'))
                throw new InvalidDataException("Reader delivery token is invalid.");
        }
        var start = new ProcessStartInfo(readerExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(readerExecutable)!
        };
        start.ArgumentList.Add("--live");
        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Reader process did not start.");
        try
        {
            await PhysicalReaderSessionMonitor.MonitorAsync(
                process.StandardOutput, tracker, onSession, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        finally
        {
            tracker.Invalidate();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
