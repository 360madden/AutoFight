namespace Rift.Automation.Guest.Infrastructure;

/// <summary>Strict read-only AFT2 selection from one completed scan. No source freshness claim.</summary>
public static class TargetVitalsV2CandidateSelector
{
    public static TargetVitalsV2Envelope? Select(
        IReadOnlyList<ReadOnlyMemory<byte>> candidates, string challenge, bool scanCompleted)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        LiveAddonEnvelopeDecoder.ValidateChallenge(challenge, nameof(challenge));
        if (!scanCompleted || candidates.Count == 0) return null;
        TargetVitalsV2Envelope? selected = null;
        var seen = new Dictionary<long, string>();
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
            var sequence = decoded.Observation.Sequence;
            if (seen.TryGetValue(sequence, out var previousHash) && previousHash != decoded.EnvelopeSha256)
                return null; // conflicting observations, fail closed
            seen[sequence] = decoded.EnvelopeSha256;
            if (selected is null || sequence > selected.Observation.Sequence)
                selected = decoded;
        }
        return selected;
    }
}
