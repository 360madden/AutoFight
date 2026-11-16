namespace Rift.Automation.Windows;

/// <summary>
/// Bounded coordinator state: a reader nonce rotation invalidates the previous host binding.
/// This component never starts a game or sends input.
/// </summary>
public sealed class PhysicalReaderSessionTracker
{
    private readonly object gate = new();
    private PhysicalReaderSessionSignal? current;
    private long generation;

    public (PhysicalReaderSessionSignal? Signal, long Generation) Snapshot()
    {
        lock (gate) return (current, generation);
    }

    /// <summary>Returns true only when a valid event changes the active reader session.</summary>
    public bool ObserveLine(string? line)
    {
        if (!PhysicalReaderSessionSignal.TryParse(line, out var signal) || signal is null)
            return false;
        lock (gate)
        {
            if (current == signal) return false;
            current = signal;
            generation++;
            return true;
        }
    }

    public bool IsCurrent(PhysicalReaderSessionSignal signal, long expectedGeneration)
    {
        lock (gate) return current == signal && generation == expectedGeneration;
    }

    public void Invalidate()
    {
        lock (gate)
        {
            current = null;
            generation++;
        }
    }
}
