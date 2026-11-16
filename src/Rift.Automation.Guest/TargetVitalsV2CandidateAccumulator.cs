using System.Text;
using Rift.Automation.Guest.Infrastructure;

namespace Rift.Automation.Guest.Live;

/// <summary>Bounded AFT2 memory candidate discovery; read-only and not wired to a live scanner yet.</summary>
public sealed class TargetVitalsV2CandidateAccumulator
{
    private const int RetainedTailBytes = TargetVitalsV2EnvelopeDecoder.MaxEnvelopeBytes - 1;
    public const int MaxEndpointCandidates = 4096;
    public const int MaxValidCandidates = 256;
    private static readonly byte[] Suffix = "|ENDRR2L1"u8.ToArray();
    private readonly string challenge;
    private readonly byte[] prefix;
    private byte[] tail = new byte[RetainedTailBytes];
    private int tailLength;
    private readonly List<ReadOnlyMemory<byte>> candidates = [];

    public TargetVitalsV2CandidateAccumulator(string challenge)
    {
        LiveAddonEnvelopeDecoder.ValidateChallenge(challenge, nameof(challenge));
        this.challenge = challenge;
        prefix = Encoding.ASCII.GetBytes($"RR2L1|{challenge}|AFT2|");
    }

    public IReadOnlyList<ReadOnlyMemory<byte>> Candidates => candidates;
    public bool BudgetExceeded { get; private set; }
    public int MalformedCount { get; private set; }
    public void ResetMemoryBoundary() => tailLength = 0;

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (BudgetExceeded || bytes.IsEmpty) return;
        var window = new byte[checked(tailLength + bytes.Length)];
        tail.AsSpan(0, tailLength).CopyTo(window);
        bytes.CopyTo(window.AsSpan(tailLength));
        var search = 0;
        while (search <= window.Length - prefix.Length)
        {
            var relative = window.AsSpan(search).IndexOf(prefix);
            if (relative < 0) break;
            var start = search + relative;
            var suffixStart = start + prefix.Length;
            if (suffixStart < window.Length)
            {
                var endRelative = window.AsSpan(suffixStart).IndexOf(Suffix);
                if (endRelative >= 0)
                {
                    var end = suffixStart + endRelative + Suffix.Length;
                    if (end - start <= TargetVitalsV2EnvelopeDecoder.MaxEnvelopeBytes && end > tailLength)
                    {
                        if (candidates.Count + MalformedCount >= MaxEndpointCandidates)
                        { BudgetExceeded = true; return; }
                        var candidate = window.AsSpan(start, end - start).ToArray();
                        try
                        {
                            _ = TargetVitalsV2EnvelopeDecoder.Decode(candidate, challenge);
                            if (candidates.Count >= MaxValidCandidates)
                            { BudgetExceeded = true; return; }
                            candidates.Add(candidate);
                        }
                        catch (InvalidDataException) { MalformedCount++; }
                    }
                }
            }
            search = start + 1;
        }
        tailLength = Math.Min(RetainedTailBytes, window.Length);
        window.AsSpan(window.Length - tailLength, tailLength).CopyTo(tail);
    }
}
