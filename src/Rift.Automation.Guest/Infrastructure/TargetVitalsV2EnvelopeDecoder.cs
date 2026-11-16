using System.Security.Cryptography;

namespace Rift.Automation.Guest.Infrastructure;

/// <summary>Strict bound-reader envelope for AFT2. No physical-source qualification or game input.</summary>
public sealed record TargetVitalsV2Envelope(
    string Challenge, TargetVitalsV2 Observation, string EnvelopeSha256, string FrameSha256);

public static class TargetVitalsV2EnvelopeDecoder
{
    public const int MaxEnvelopeBytes = TargetVitalsV2Decoder.MaxFrameBytes + 48;
    private static readonly byte[] Prefix = "RR2L1|"u8.ToArray();
    private static readonly byte[] Suffix = "|ENDRR2L1"u8.ToArray();

    public static TargetVitalsV2Envelope Decode(ReadOnlySpan<byte> envelope, string expectedChallenge)
    {
        LiveAddonEnvelopeDecoder.ValidateChallenge(expectedChallenge, nameof(expectedChallenge));
        var minimum = Prefix.Length + 32 + 1 + 1 + Suffix.Length;
        if (envelope.Length < minimum || envelope.Length > MaxEnvelopeBytes ||
            !envelope.StartsWith(Prefix) || !envelope.EndsWith(Suffix))
            throw new InvalidDataException("Invalid AFT2 envelope framing.");
        var challengeBytes = envelope.Slice(Prefix.Length, 32);
        for (var i = 0; i < challengeBytes.Length; i++)
            if (challengeBytes[i] != (byte)expectedChallenge[i])
                throw new InvalidDataException("AFT2 challenge mismatch.");
        var separator = Prefix.Length + 32;
        if (envelope[separator] != (byte)'|')
            throw new InvalidDataException("Missing AFT2 challenge separator.");
        var payload = envelope[(separator + 1)..^Suffix.Length];
        var observation = TargetVitalsV2Decoder.Decode(payload);
        return new(expectedChallenge, observation,
            Convert.ToHexString(SHA256.HashData(envelope)),
            Convert.ToHexString(SHA256.HashData(payload)));
    }
}
