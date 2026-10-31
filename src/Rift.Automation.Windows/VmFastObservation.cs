using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>
/// Validate a RiftReader2 candidate-region observation against its full-scan anchor.
/// This is a diagnostic scope, not a movement-readiness or authority grant.
/// </summary>
internal static class VmFastObservation
{
    private const string Provenance = "recent-full-scan-anchored-candidate-regions/1";
    private static readonly string[] FastKeys =
    [
        "schemaVersion", "provenance", "bindingStatus", "freshnessStatus",
        "reason", "anchor", "selectionStatus", "envelopeSha256",
        "addonFrameSha256", "candidateSequence", "candidateEpoch",
        "observation", "scan", "publishedAtTimestamp",
        "lastAdvanceAtTimestamp", "lastAdvanceAgeMilliseconds",
        "freshnessTimeoutMilliseconds"
    ];
    private static readonly string[] AnchorKeys =
    [
        "fullScanStartedAtTimestamp", "fullScanCompletedAtTimestamp",
        "timestampFrequency", "envelopeSha256", "addonFrameSha256",
        "candidateSequence", "candidateEpoch", "candidateClock", "playerId",
        "regionCount", "totalRegionBytes", "maxAgeMilliseconds"
    ];
    private static readonly string[] ScanKeys =
    [
        "scope", "status", "completed", "startedAtTimestamp",
        "completedAtTimestamp", "plannedBytes", "scannedBytes", "regionCount",
        "elapsedMilliseconds", "endpointCandidateCount",
        "malformedCandidateCount", "matchingCandidateCount",
        "identicalCopyCount", "reason"
    ];
    private static readonly string[] SelectedKeys =
    [
        "schemaVersion", "addonVersion", "sequence", "epoch",
        "clock", "player", "fields"
    ];

    public static bool TrySelect(
        JsonElement eventData, DateTimeOffset capturedAtUtc,
        DateTimeOffset checkedAtUtc, out JsonElement selected)
    {
        selected = default;
        try
        {
            if (!eventData.TryGetProperty("fastObservation", out var fast) ||
                !Exact(fast, FastKeys) ||
                L(fast, "schemaVersion") != 1 ||
                S(fast, "provenance") != Provenance ||
                S(fast, "bindingStatus") != "bound-scoped" ||
                S(fast, "freshnessStatus") != "fresh-scoped" ||
                S(fast, "selectionStatus") != "selected-scoped" ||
                !IsNull(fast, "reason"))
                return false;

            var anchor = P(fast, "anchor");
            if (!Exact(anchor, AnchorKeys)) return false;
            long started = L(anchor, "fullScanStartedAtTimestamp");
            long anchored = L(anchor, "fullScanCompletedAtTimestamp");
            long frequency = L(anchor, "timestampFrequency");
            long anchorSequence = L(anchor, "candidateSequence");
            long anchorEpoch = L(anchor, "candidateEpoch");
            long regionCount = L(anchor, "regionCount");
            long regionBytes = L(anchor, "totalRegionBytes");
            double anchorClock = D(anchor, "candidateClock");
            string playerId = S(anchor, "playerId");
            string anchorEnvelope = Sha(anchor, "envelopeSha256");
            string anchorFrame = Sha(anchor, "addonFrameSha256");

            if (started <= 0 || anchored < started || frequency <= 0 ||
                anchorSequence < 0 || anchorEpoch < 0 || anchorClock < 0 ||
                string.IsNullOrEmpty(playerId) || regionCount < 1 ||
                regionBytes is < 1 or > 1073741824 ||
                L(anchor, "maxAgeMilliseconds") != 10000)
                return false;

            var scan = P(fast, "scan");
            if (!Exact(scan, ScanKeys) ||
                S(scan, "scope") != "candidate-regions" ||
                S(scan, "status") != "complete" ||
                P(scan, "completed").ValueKind != JsonValueKind.True ||
                !IsNull(scan, "reason"))
                return false;
            long scanStart = L(scan, "startedAtTimestamp");
            long scanEnd = L(scan, "completedAtTimestamp");
            if (scanStart < anchored || scanEnd < scanStart ||
                L(scan, "plannedBytes") != regionBytes ||
                L(scan, "scannedBytes") != regionBytes ||
                L(scan, "regionCount") != regionCount ||
                D(scan, "elapsedMilliseconds") < 0 ||
                L(scan, "endpointCandidateCount") < 0 ||
                L(scan, "malformedCandidateCount") < 0 ||
                L(scan, "matchingCandidateCount") < 1 ||
                L(scan, "identicalCopyCount") < 1)
                return false;

            var source = P(fast, "observation");
            if (!Exact(source, SelectedKeys) ||
                L(source, "schemaVersion") != 1 ||
                P(source, "addonVersion").ValueKind != JsonValueKind.String ||
                P(source, "fields").ValueKind != JsonValueKind.Object)
                return false;
            long sequence = L(source, "sequence");
            long epoch = L(source, "epoch");
            var clock = P(source, "clock");
            var player = P(source, "player");
            if (!Exact(clock, ["status", "value"]) ||
                !Exact(player, ["status", "value"]) ||
                S(clock, "status") != "ok" ||
                S(player, "status") != "ok" ||
                S(player, "value") != playerId ||
                sequence <= anchorSequence || epoch != anchorEpoch ||
                D(clock, "value") <= anchorClock ||
                sequence != L(fast, "candidateSequence") ||
                epoch != L(fast, "candidateEpoch") ||
                Sha(fast, "envelopeSha256") == anchorEnvelope ||
                Sha(fast, "addonFrameSha256") == anchorFrame)
                return false;

            long lastAdvance = L(fast, "lastAdvanceAtTimestamp");
            long published = L(fast, "publishedAtTimestamp");
            double statedAge = D(fast, "lastAdvanceAgeMilliseconds");
            double timeout = D(fast, "freshnessTimeoutMilliseconds");
            if (lastAdvance < scanStart || lastAdvance > scanEnd ||
                published < scanEnd || statedAge < 0 || timeout <= 0)
                return false;
            double timestampAge = (published - lastAdvance) * 1000d / frequency;
            if (!double.IsFinite(timestampAge) ||
                Math.Abs(statedAge - timestampAge) >
                    Math.Max(0.001, timestampAge * 1e-9))
                return false;

            // Same explicitly bounded UTC clock-skew assumption as the outer reader.
            double elapsedMs = Math.Max(0, (checkedAtUtc - capturedAtUtc).TotalMilliseconds);
            double innerAge = statedAge + elapsedMs + 250;
            double anchorAge = (published - anchored) * 1000d / frequency + elapsedMs + 250;
            if (!double.IsFinite(anchorAge) ||
                !double.IsFinite(innerAge) ||
                innerAge >= timeout || anchorAge is < 0 or > 10000)
                return false;

            selected = source;
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or
            KeyNotFoundException or OverflowException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Exact(JsonElement value, IReadOnlyList<string> expected)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        int count = 0;
        foreach (JsonProperty prop in value.EnumerateObject())
        {
            if (!expected.Contains(prop.Name, StringComparer.Ordinal))
                return false;
            count++;
        }
        return count == expected.Count;
    }

    private static JsonElement P(JsonElement item, string name) => item.GetProperty(name);
    private static string S(JsonElement item, string name) =>
        P(item, name).GetString() ?? throw new FormatException();
    private static long L(JsonElement item, string name) => P(item, name).GetInt64();
    private static double D(JsonElement item, string name)
    {
        double d = P(item, name).GetDouble();
        if (!double.IsFinite(d)) throw new FormatException();
        return d;
    }
    private static bool IsNull(JsonElement item, string name) =>
        P(item, name).ValueKind == JsonValueKind.Null;
    private static string Sha(JsonElement item, string name)
    {
        string s = S(item, name);
        if (s.Length != 64 || !s.All(Uri.IsHexDigit)) throw new FormatException();
        return s.ToLowerInvariant();
    }
}
