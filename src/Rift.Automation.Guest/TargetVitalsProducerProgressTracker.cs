namespace Rift.Automation.Guest.Live;

/// <summary>
/// Read-only cross-scan AFT2 evidence. These labels never establish producer
/// freshness, physical-game provenance or combat authority.
/// </summary>
public sealed record TargetVitalsProducerProgressDiagnostic(
    string Status, long? Sequence, long? PreviousSequence,
    double? ProducerClock, double? PreviousProducerClock);

/// <summary>
/// Owned by one attached GuestReader session. An unavailable/conflicting scan
/// breaks continuity; a producer identity change starts a new baseline.
/// </summary>
public sealed class TargetVitalsProducerProgressTracker
{
    private TargetVitalsCandidateDiagnostic? previous;

    public TargetVitalsProducerProgressDiagnostic Observe(TargetVitalsCandidateDiagnostic? current)
    {
        if (current is null || current.Status != "candidate-unqualified")
        {
            previous = null;
            return new(current?.Status ?? "unavailable", null, null, null, null);
        }

        // Invalid floating-point input must never escape into strict JSON output.
        double? finiteClock = current.ProducerClock is double candidateClock &&
            double.IsFinite(candidateClock) && candidateClock >= 0 ? candidateClock : null;
        if (current.Sequence is not > 0 || current.Epoch is not > 0 ||
            finiteClock is not double clock ||
            string.IsNullOrEmpty(current.PlayerId) || string.IsNullOrEmpty(current.TargetId) ||
            string.IsNullOrEmpty(current.FrameSha256) || string.IsNullOrEmpty(current.EnvelopeSha256))
        {
            previous = null;
            return new("invalid-progression", current.Sequence, null, finiteClock, null);
        }

        var prior = previous;
        if (prior is null)
        {
            previous = current;
            return new("baseline-unqualified", current.Sequence, null, clock, null);
        }

        if (current.Epoch != prior.Epoch || current.PlayerId != prior.PlayerId ||
            current.TargetId != prior.TargetId)
        {
            previous = current;
            return new("source-changed-unqualified", current.Sequence,
                prior.Sequence, clock, prior.ProducerClock);
        }

        if (current.Sequence == prior.Sequence && clock == prior.ProducerClock &&
            current.FrameSha256 == prior.FrameSha256 &&
            current.EnvelopeSha256 == prior.EnvelopeSha256)
            return new("repeated-unqualified", current.Sequence, prior.Sequence,
                clock, prior.ProducerClock);

        if (current.Sequence > prior.Sequence && clock > prior.ProducerClock &&
            current.FrameSha256 != prior.FrameSha256 &&
            current.EnvelopeSha256 != prior.EnvelopeSha256)
        {
            previous = current;
            return new("advanced-unqualified", current.Sequence,
                prior.Sequence, clock, prior.ProducerClock);
        }

        // Regressed producer sequence/clock, or contradictory same-sequence
        // bytes: drop the baseline, rather than continuing a false stream.
        previous = null;
        return new("invalid-progression", current.Sequence,
            prior.Sequence, clock, prior.ProducerClock);
    }

    public void Reset() => previous = null;
}
