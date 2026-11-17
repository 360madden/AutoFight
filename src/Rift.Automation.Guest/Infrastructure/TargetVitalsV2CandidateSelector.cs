namespace Rift.Automation.Guest.Infrastructure;

/// <summary>Strict read-only AFT2 selection from one completed scan. No source freshness claim.</summary>
public static class TargetVitalsV2CandidateSelector
{
    public static TargetVitalsV2Envelope? Select(
        IReadOnlyList<ReadOnlyMemory<byte>> candidates, string challenge, bool scanCompleted)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        LiveAddonEnvelopeDecoder.ValidateChallenge(challenge, nameof(challenge));
        if (!scanCompleted || candidates.Count is 0 or > 256) return null;
        TargetVitalsV2Envelope? selected = null;
        long? epoch = null;
        string? playerId = null;
        string? targetId = null;
        var seen = new SortedDictionary<long, (string Hash, double Clock)>();
        foreach (var bytes in candidates)
        {
            TargetVitalsV2Envelope decoded;
            try
            {
                decoded = TargetVitalsV2EnvelopeDecoder.Decode(bytes.Span, challenge);
            }
            catch (InvalidDataException)
            {
                return null; // A malformed candidate must not crash the live reader.
            }
            var sample = decoded.Observation;
            if (epoch is not null &&
                (sample.Epoch != epoch || sample.PlayerId != playerId || sample.TargetId != targetId))
                return null; // Mixed producer generations or actors cannot form one source.
            epoch ??= sample.Epoch;
            playerId ??= sample.PlayerId;
            targetId ??= sample.TargetId;
            if (seen.TryGetValue(sample.Sequence, out var previous) &&
                previous.Hash != decoded.EnvelopeSha256)
                return null; // Same-sequence observations conflict.
            seen[sample.Sequence] = (decoded.EnvelopeSha256, sample.Clock);
            if (selected is null || sample.Sequence > selected.Observation.Sequence)
                selected = decoded;
        }
        // Memory enumeration order is arbitrary; producer time must strictly
        // advance in sequence order, not merely with the last observed copy.
        var previousClock = double.NegativeInfinity;
        foreach (var candidate in seen.Values)
        {
            if (candidate.Clock <= previousClock) return null;
            previousClock = candidate.Clock;
        }
        return selected;
    }
}
