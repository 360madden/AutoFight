using System.Diagnostics;

namespace Rift.Automation.Windows;

/// <summary>Observes a reader's stdout until exit or cancellation; never sends game input.</summary>
public static class PhysicalReaderSessionMonitor
{
    public static async Task MonitorAsync(
        TextReader reader,
        PhysicalReaderSessionTracker tracker,
        Func<PhysicalReaderSessionSignal, long, CancellationToken, Task> onSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(onSession);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (!tracker.ObserveLine(line)) continue;
                var snapshot = tracker.Snapshot();
                if (snapshot.Signal is not null)
                    await onSession(snapshot.Signal, snapshot.Generation, cancellationToken);
            }
        }
        finally
        {
            tracker.Invalidate();
        }
    }
}
