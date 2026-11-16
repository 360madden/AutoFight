using System.Text.RegularExpressions;

namespace Rift.Automation.Windows;

/// <summary>Observes a reader's stdout until exit or cancellation; never sends game input.</summary>
public static class PhysicalReaderSessionMonitor
{
    public static async Task MonitorAsync(
        TextReader reader,
        PhysicalReaderSessionTracker tracker,
        Func<PhysicalReaderSessionSignal, long, CancellationToken, Task> onSession,
        CancellationToken cancellationToken,
        Action<string>? onStatus = null)
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
                if (!tracker.ObserveLine(line))
                {
                    // Never forward arbitrary reader output, bearer tokens, or game identifiers.
                    // Only the bounded, structured status fields emitted by PrintStatus are allowed.
                    if (onStatus is not null && line.Length <= 512 &&
                        Regex.IsMatch(line, @"^\S+ scan=[a-z-]+ selection=[A-Za-z-]+ freshness=[A-Za-z-]+ ageMs=[0-9.-]+(?: fastScan=[a-z-]+ fastFreshness=[A-Za-z-]+ fastAgeMs=[0-9.-]+)? delivery=[a-z0-9-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
                        onStatus(line);
                    continue;
                }
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
