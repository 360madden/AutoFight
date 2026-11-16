using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Runs an explicitly selected reader and replaces its localhost receiver on nonce rotation.</summary>
public static class PhysicalTelemetryCoordinator
{
    public static async Task<int> RunAsync(string readerExecutable, int gameProcessId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(readerExecutable) ||
            !Path.IsPathFullyQualified(readerExecutable) ||
            !string.Equals(Path.GetFileName(readerExecutable), "Rift.Automation.GuestReader.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Absolute GuestReader path required.", nameof(readerExecutable));
        var configPath = Path.Combine(Path.GetDirectoryName(readerExecutable)!,
            "autofight-guest-reader.json");
        var info = new FileInfo(configPath);
        if (!info.Exists || info.Length is <= 0 or > 65_536)
            throw new InvalidDataException("Reader configuration missing or oversized.");
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!config.RootElement.TryGetProperty("targetProcessId", out var pin) ||
            pin.ValueKind != JsonValueKind.Number || pin.GetInt32() != gameProcessId)
            throw new InvalidDataException("Reader targetProcessId must match the coordinator RIFT PID.");
        var delivery = config.RootElement.GetProperty("delivery");
        if (!delivery.GetProperty("allowLoopback").GetBoolean() ||
            !Uri.TryCreate(delivery.GetProperty("endpoint").GetString(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttp || endpoint.Host != "127.0.0.1" ||
            endpoint.AbsolutePath != "/observation" || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.Port is < 1024 or > 65535)
            throw new InvalidDataException("Local receiver delivery endpoint required.");
        var token = delivery.GetProperty("token").GetString() ??
            throw new InvalidDataException("Delivery token missing.");
        var tracker = new PhysicalReaderSessionTracker();
        await using var receivers = new PhysicalReceiverRotation(tracker, gameProcessId, endpoint.Port, token);
        try
        {
            return await PhysicalReaderProcessSupervisor.RunAsync(readerExecutable, tracker,
                async (signal, generation, ct) =>
                {
                    await receivers.RotateAsync(signal, generation, ct);
                    if (tracker.IsCurrent(signal, generation))
                        Console.WriteLine($"AutoFight PID {gameProcessId}: enter /afbind {signal.Nonce} in the matching RIFT client.");
                },
                cancellationToken);
        }
        finally
        {
            await receivers.StopAsync();
            tracker.Invalidate();
        }
    }
}
