using System.Diagnostics;
using System.Security.Cryptography;

namespace Rift.Automation.Windows;

/// <summary>Owns one physical receiver per active reader nonce; no game input.</summary>
public sealed class PhysicalReceiverRotation : IAsyncDisposable
{
    private readonly PhysicalReaderSessionTracker tracker;
    private readonly int gameProcessId;
    private readonly int port;
    private readonly string token;
    private readonly object gate = new();
    private readonly SemaphoreSlim rotationGate = new(1, 1);
    private CancellationTokenSource? activeStop;
    private Task? activeTask;
    private long activeGeneration;

    public PhysicalReceiverRotation(PhysicalReaderSessionTracker tracker, int gameProcessId,
        int port, string token)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        if (gameProcessId <= 0 || port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(gameProcessId));
        this.tracker = tracker;
        this.gameProcessId = gameProcessId;
        this.port = port;
        this.token = token;
    }

    public async Task RotateAsync(PhysicalReaderSessionSignal signal, long generation,
        CancellationToken cancellationToken)
    {
        await rotationGate.WaitAsync(cancellationToken);
        try
        {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tracker.IsCurrent(signal, generation))
            throw new InvalidOperationException("Stale reader session.");
        await StopCoreAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (!tracker.IsCurrent(signal, generation))
            throw new InvalidOperationException("Reader session changed during receiver rotation.");

        using var game = Process.GetProcessById(gameProcessId);
        if (game.HasExited || !string.Equals(game.ProcessName, "rift_x64",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pinned physical RIFT is unavailable.");
        var path = game.MainModule?.FileName ?? throw new InvalidOperationException("Game image unavailable.");
        var binding = new PhysicalTelemetryBinding(signal.InstanceId, signal.Nonce,
            game.Id, game.StartTime.ToUniversalTime().Ticks, path,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var session = new PhysicalTelemetryHostSession(binding, port, token);
        Task running;
        try { running = session.RunAsync(stop.Token); }
        catch { session.Dispose(); stop.Dispose(); throw; }
        lock (gate)
        {
            activeStop = stop;
            activeGeneration = generation;
            activeTask = ObserveAsync(running, session, stop);
        }
        }
        finally { rotationGate.Release(); }
    }

    private static async Task ObserveAsync(Task running, PhysicalTelemetryHostSession session,
        CancellationTokenSource stop)
    {
        try { await running; }
        finally { session.Dispose(); }
    }

    public async Task StopAsync()
    {
        await rotationGate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { rotationGate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? stop;
        Task? running;
        lock (gate)
        {
            stop = activeStop;
            running = activeTask;
            activeStop = null;
            activeTask = null;
            activeGeneration = 0;
        }
        if (stop is null) return;
        stop.Cancel();
        try { if (running is not null) await running; }
        finally { stop.Dispose(); }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
