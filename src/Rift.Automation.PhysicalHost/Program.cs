using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Rift.Automation.Windows;

if (args.Length != 2 || args[0] != "--config")
{
    Console.Error.WriteLine("Usage: Rift.Automation.PhysicalHost --config <private-config.json>");
    return 2;
}
try
{
    var configPath = Path.GetFullPath(args[1]);
    var configInfo = new FileInfo(configPath);
    if (configInfo.Length is <= 0 or > 65_536)
        throw new InvalidDataException("Configuration must be 1 through 65536 bytes.");
    using var document = JsonDocument.Parse(File.ReadAllText(configPath));
    var root = document.RootElement;
    if (root.GetProperty("schemaVersion").GetInt32() != 1)
        throw new InvalidDataException("Unsupported configuration schema.");
    int port = root.GetProperty("port").GetInt32();
    int duration = root.GetProperty("durationSeconds").GetInt32();
    if (port is < 1024 or > 65535 || duration is < 1 or > 900)
        throw new InvalidDataException("Port or duration outside permitted bounds.");
    string token = root.GetProperty("deliveryToken").GetString() ?? "";
    var binding = JsonSerializer.Deserialize<PhysicalTelemetryBinding>(
        root.GetProperty("binding").GetRawText(),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Missing binding.");
    using var game = Process.GetProcessById(binding.GameProcessId);
    if (game.HasExited || game.StartTime.ToUniversalTime().Ticks != binding.GameProcessStartUtcTicks)
        throw new InvalidDataException("Pinned RIFT process identity changed.");
    string image = game.MainModule?.FileName ?? throw new InvalidDataException("Executable unavailable.");
    if (!string.Equals(Path.GetFullPath(image), Path.GetFullPath(binding.GameExecutablePath),
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Executable path mismatch.");
    string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image)));
    if (!string.Equals(sha, binding.GameExecutableSha256, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Executable hash mismatch.");
    using var host = new PhysicalTelemetryHostSession(binding, port, token);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(duration));
    Console.WriteLine($"Observation-only receiver listening on 127.0.0.1:{port} for at most {duration}s.");
    var receiver = host.RunAsync(timeout.Token);
    var processExit = game.WaitForExitAsync(timeout.Token);
    var first = await Task.WhenAny(receiver, processExit);
    if (first == processExit && !timeout.IsCancellationRequested)
    {
        timeout.Cancel();
        await receiver;
        throw new InvalidDataException("Pinned RIFT process exited during the receiver session.");
    }
    await receiver;
    var last = host.Latest;
    Console.WriteLine(last is null ? "No observations received." :
        $"Last receipt: seq={last.Sequence} status={last.ReaderStatus} vitals={last.TargetVitalsStatus ?? "unavailable"}");
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or InvalidDataException or
    IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
{
    Console.Error.WriteLine($"Physical host startup failed: {ex.GetType().Name}");
    return 1;
}
