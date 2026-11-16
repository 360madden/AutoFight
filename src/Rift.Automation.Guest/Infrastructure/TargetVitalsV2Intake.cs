namespace Rift.Automation.Guest.Infrastructure;

/// <summary>Read-only candidate intake; source transport and physical RIFT identity remain unqualified.</summary>
public sealed class TargetVitalsV2Intake
{
    private readonly TargetVitalsV2Continuity continuity = new();
    private long lastReceiptMs = -1;
    private bool faulted;

    public bool Faulted => faulted || continuity.Faulted;
    public TargetVitalsV2? LastAccepted { get; private set; }

    /// <summary>Read-only freshness check in the caller's monotonic receipt clock domain.</summary>
    public bool TryGetFresh(long nowMs, long maxAgeMs, out TargetVitalsV2? sample)
    {
        sample = null;
        if (Faulted || maxAgeMs < 0 || maxAgeMs > 5000 || lastReceiptMs < 0 ||
            nowMs < lastReceiptMs || nowMs - lastReceiptMs > maxAgeMs)
            return false;
        sample = LastAccepted;
        return sample is not null;
    }

    public bool TryAccept(ReadOnlySpan<byte> frame, long receiptMs, out TargetVitalsV2? accepted)
    {
        accepted = null;
        if (Faulted)
        {
            LastAccepted = null;
            return false;
        }
        if (receiptMs < 0 || (lastReceiptMs >= 0 && receiptMs <= lastReceiptMs))
        {
            faulted = true;
            LastAccepted = null;
            return false;
        }
        TargetVitalsV2 candidate;
        try
        {
            candidate = TargetVitalsV2Decoder.Decode(frame);
        }
        catch (InvalidDataException)
        {
            faulted = true;
            LastAccepted = null;
            return false;
        }
        if (!continuity.Accept(candidate))
        {
            faulted = true;
            LastAccepted = null;
            return false;
        }
        lastReceiptMs = receiptMs;
        LastAccepted = accepted = candidate;
        return true;
    }

    /// <summary>Explicit reader restart only; no automatic recovery after corrupt or reordered frames.</summary>
    public void Reset()
    {
        continuity.Reset();
        lastReceiptMs = -1;
        LastAccepted = null;
        faulted = false;
    }
}
