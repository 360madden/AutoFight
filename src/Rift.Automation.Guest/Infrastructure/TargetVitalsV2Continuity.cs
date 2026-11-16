namespace Rift.Automation.Guest.Infrastructure;

/// <summary>Single-owner candidate stream guard. Valid ordering does not establish live source qualification.</summary>
public sealed class TargetVitalsV2Continuity
{
    private long epoch = -1;
    private long sequence = -1;
    private double clock = -1;
    private string? playerId;
    private string? targetId;
    private bool faulted;

    public bool Faulted => faulted;

    public bool Accept(TargetVitalsV2 sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (faulted)
            return false;
        if (epoch == -1)
        {
            epoch = sample.Epoch;
            sequence = sample.Sequence;
            clock = sample.Clock;
            playerId = sample.PlayerId;
            targetId = sample.TargetId;
            return true;
        }
        if (sample.Epoch != epoch || sample.Sequence <= sequence ||
            sample.Clock <= clock || sample.PlayerId != playerId || sample.TargetId != targetId)
        {
            faulted = true;
            return false;
        }
        sequence = sample.Sequence;
        clock = sample.Clock;
        return true;
    }

    /// <summary>Explicitly starts a new reader-owned observation stream.</summary>
    public void Reset()
    {
        epoch = -1;
        sequence = -1;
        clock = -1;
        playerId = null;
        targetId = null;
        faulted = false;
    }
}
