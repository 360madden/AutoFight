using System.Text.Json;

namespace Rift.Automation.Guest.Infrastructure;

/// <summary>Isolated candidate decoder for target vitals. Does not alter RR2A1 or authorize gameplay.</summary>
public sealed record TargetVitalsV2(
    long Sequence, long Epoch, double Clock, string PlayerId,
    string TargetId, double Health, double HealthMax);

public static class TargetVitalsV2Decoder
{
    public const int MaxFrameBytes = 1024;
    private static readonly byte[] Prefix = "AFT2|"u8.ToArray();
    private static readonly byte[] Suffix = "|ENDAFT2"u8.ToArray();

    public static TargetVitalsV2 Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length is < 20 or > MaxFrameBytes ||
            !frame.StartsWith(Prefix) || !frame.EndsWith(Suffix))
            throw new InvalidDataException("Invalid target-vitals v2 framing.");
        foreach (var value in frame)
            if (value > 126 || value < 32)
                throw new InvalidDataException("Target-vitals frame must be printable ASCII.");
        try
        {
            using var doc = JsonDocument.Parse(frame[Prefix.Length..^Suffix.Length].ToArray(),
                new JsonDocumentOptions { MaxDepth = 3, AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Target-vitals root must be an object.");
            var expected = new HashSet<string>(StringComparer.Ordinal)
            { "schemaVersion", "sequence", "epoch", "clock", "playerId", "targetId", "health", "healthMax" };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in root.EnumerateObject())
                if (!expected.Contains(prop.Name) || !seen.Add(prop.Name))
                    throw new InvalidDataException("Unexpected or duplicate target-vitals field.");
            if (seen.Count != expected.Count || root.GetProperty("schemaVersion").GetInt32() != 2)
                throw new InvalidDataException("Target-vitals schema mismatch.");
            var sequence = root.GetProperty("sequence").GetInt64();
            var epoch = root.GetProperty("epoch").GetInt64();
            var clock = root.GetProperty("clock").GetDouble();
            var player = root.GetProperty("playerId").GetString();
            var target = root.GetProperty("targetId").GetString();
            var health = root.GetProperty("health").GetDouble();
            var max = root.GetProperty("healthMax").GetDouble();
            if (sequence is < 1 or > 9007199254740991 || epoch is < 1 or > 9007199254740991 ||
                !double.IsFinite(clock) || clock < 0 ||
                !ValidId(player) || !ValidId(target) ||
                !double.IsFinite(health) || !double.IsFinite(max) ||
                max <= 0 || health < 0 || health > max)
                throw new InvalidDataException("Invalid target-vitals values.");
            return new(sequence, epoch, clock, player!, target!, health, max);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException("Malformed target-vitals frame.", ex);
        }
    }

    private static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(c => c is >= ' ' and <= '~');
}
