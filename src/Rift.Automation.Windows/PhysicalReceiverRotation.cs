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
        var gameStartTicks = game.StartTime.ToUniversalTime().Ticks;
        await using var imageStream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var imageHash = Convert.ToHexString(await SHA256.HashDataAsync(imageStream, cancellationToken));
        game.Refresh();
        if (game.HasExited || game.StartTime.ToUniversalTime().Ticks != gameStartTicks ||
            !string.Equals(game.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase) ||
            !tracker.IsCurrent(signal, generation))
            throw new InvalidOperationException("Game or reader identity changed during receiver preparation.");
        var binding = new PhysicalTelemetryBinding(signal.InstanceId, signal.Nonce,
            game.Id, gameStartTicks, path, imageHash);
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var session = new PhysicalTelemetryHostSession(binding, port, token);
        Task running;
        try { running = session.RunAsync(stop.Token); }
        catch { session.Dispose(); stop.Dispose(); throw; }
        lock (gate)
        {
            activeStop = stop;
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
        }
        if (stop is null) return;
        stop.Cancel();
        try
        {
            if (running is not null) await running;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The listener was deliberately stopped during rotation or shutdown.
        }
        finally { stop.Dispose(); }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
