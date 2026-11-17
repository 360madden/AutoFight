using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Read-only physical RIFT receiver. Separate from VMware lab ingest.</summary>
public sealed record PhysicalTelemetryBinding(
    Guid ReaderInstanceId, string ReaderNonce, int GameProcessId,
    long GameProcessStartUtcTicks, string GameExecutablePath, string GameExecutableSha256);

/// <summary>Identity-bound, unqualified memory candidate. Not a combat observation.</summary>
public sealed record PhysicalTargetVitalsCandidate(
    string PlayerId, string TargetId, long Epoch, long Sequence,
    double ProducerClock, double Health, double HealthMax,
    string EnvelopeSha256, string FrameSha256)
{
    public bool SourceQualified => false;
}

public sealed record PhysicalTelemetryReceipt(
    long Sequence, string ReaderStatus, DateTimeOffset ReceivedAtUtc,
    string? TargetVitalsStatus, long? TargetVitalsSequence)
{
    // This remains diagnostic only: receipt UTC does not validate producer time.
    public PhysicalTargetVitalsCandidate? TargetVitals { get; init; }
}

public sealed class PhysicalTelemetryIngest
{
    private readonly PhysicalTelemetryBinding binding;
    private readonly byte[] tokenDigest;
    private readonly object gate = new();
    private long lastSequence;
    private bool terminated;
    private PhysicalTelemetryReceipt? latest;
    public PhysicalTelemetryReceipt? Latest
    {
        get { lock (gate) return latest; }
    }

    /// <summary>Only returns a receipt within an explicit freshness window.</summary>
    public PhysicalTelemetryReceipt? ReadFresh(DateTimeOffset nowUtc, TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero || maxAge > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        lock (gate)
        {
            if (latest is null) return null;
            var age = nowUtc.ToUniversalTime() - latest.ReceivedAtUtc;
            return age >= TimeSpan.Zero && age <= maxAge ? latest : null;
        }
    }

    public PhysicalTelemetryIngest(PhysicalTelemetryBinding binding, string deliveryToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.ReaderInstanceId == Guid.Empty ||
            binding.ReaderNonce is null || binding.ReaderNonce.Length != 32 ||
            !binding.ReaderNonce.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            binding.GameProcessId < 1 || binding.GameProcessStartUtcTicks < 1 ||
            string.IsNullOrWhiteSpace(binding.GameExecutablePath) ||
            binding.GameExecutableSha256 is null || binding.GameExecutableSha256.Length != 64 ||
            !binding.GameExecutableSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid physical telemetry binding.", nameof(binding));
        if (deliveryToken is null || deliveryToken.Length is < 32 or > 512 ||
            deliveryToken.Any(c => c is < '!' or > '~'))
            throw new ArgumentException("Invalid delivery token.", nameof(deliveryToken));
        this.binding = binding;
        tokenDigest = SHA256.HashData(Encoding.ASCII.GetBytes(deliveryToken));
    }

    public bool Accept(ReadOnlyMemory<byte> body, string token, string peer, DateTimeOffset receivedAtUtc)
    {
        if (peer != "127.0.0.1" || token is null ||
            token.Length is < 32 or > 512 || token.Any(c => c is < '!' or > '~') ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.ASCII.GetBytes(token)), tokenDigest) ||
            body.Length is <= 0 or > 131072)
            return false;
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Unique(root) ||
                root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("instanceId").GetGuid() != binding.ReaderInstanceId ||
                root.GetProperty("nonce").GetString() != binding.ReaderNonce)
                return false;
            var sequence = root.GetProperty("sequence").GetInt64();
            var status = root.GetProperty("readerStatus").GetString();
            if (sequence < 1 || status is not ("running" or "unavailable" or "terminated")) return false;
            var target = root.GetProperty("target");
            if (target.ValueKind == JsonValueKind.Object)
            {
                if (!Unique(target) ||
                    target.GetProperty("processId").GetInt32() != binding.GameProcessId ||
                    target.GetProperty("processStartUtcTicks").GetInt64() != binding.GameProcessStartUtcTicks ||
                    !string.Equals(target.GetProperty("executablePath").GetString(),
                        binding.GameExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(target.GetProperty("executableSha256").GetString(),
                        binding.GameExecutableSha256, StringComparison.OrdinalIgnoreCase) ||
                    target.GetProperty("readerSessionNonce").GetString() != binding.ReaderNonce)
                    return false;
            }
            else if (target.ValueKind != JsonValueKind.Null) return false;

            string? vitalsStatus = null;
            long? vitalsSequence = null;
            PhysicalTargetVitalsCandidate? parsedVitals = null;
            if (root.TryGetProperty("targetVitalsCandidate", out var vitals) &&
                vitals.ValueKind == JsonValueKind.Object)
            {
                if (!Unique(vitals) || vitals.EnumerateObject().Any(p =>
                    p.Name is not ("status" or "candidateCount" or "sequence" or
                        "targetId" or "health" or "healthMax" or "playerId" or
                        "epoch" or "producerClock" or "envelopeSha256" or "frameSha256")))
                    return false;
                if (vitals.TryGetProperty("candidateCount", out var count) &&
                    (count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var n) ||
                        n < 0 || n > 4096))
                    return false;
                vitalsStatus = vitals.GetProperty("status").GetString();
                if (vitalsStatus is not ("unavailable" or "not-found" or "conflict" or "candidate-unqualified"))
                    return false;
                if (vitals.TryGetProperty("sequence", out var seq))
                {
                    if (seq.ValueKind == JsonValueKind.Number)
                    {
                        vitalsSequence = seq.GetInt64();
                        if (vitalsSequence is < 1 or > 9007199254740991 ||
                            vitalsStatus != "candidate-unqualified") return false;
                    }
                    else if (seq.ValueKind != JsonValueKind.Null) return false;
                }
                if (vitalsStatus == "candidate-unqualified")
                {
                    if (vitalsSequence is not long candidateSequence) return false;
                    if (vitals.TryGetProperty("candidateCount", out var candidateCount) &&
                        (candidateCount.GetInt32() is < 1 or > 256)) return false;
                    parsedVitals = ParseTargetVitalsCandidate(vitals, candidateSequence);
                }
                // No health candidate survives missing identity or a stopped reader.
                if (target.ValueKind == JsonValueKind.Null || status != "running")
                {
                    vitalsStatus = null;
                    vitalsSequence = null;
                    parsedVitals = null;
                }
            }
            lock (gate)
            {
                if (terminated || sequence <= lastSequence) return false;
                lastSequence = sequence;
                if (status == "terminated") terminated = true;
                latest = new(sequence, status!, receivedAtUtc.ToUniversalTime(), vitalsStatus, vitalsSequence)
                {
                    TargetVitals = parsedVitals
                };
            }
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static PhysicalTargetVitalsCandidate? ParseTargetVitalsCandidate(
        JsonElement candidate, long sequence)
    {
        // Older diagnostic senders reported only status + sequence. Preserve
        // compatibility, but never synthesize absent health/provenance values.
        string[] fields = ["playerId", "targetId", "epoch", "producerClock",
            "health", "healthMax", "envelopeSha256", "frameSha256"];
        if (fields.All(name => !candidate.TryGetProperty(name, out _))) return null;
        if (fields.Any(name => !candidate.TryGetProperty(name, out _)))
            throw new FormatException("Partial candidate provenance.");
        var playerId = candidate.GetProperty("playerId").GetString();
        var targetId = candidate.GetProperty("targetId").GetString();
        var epoch = candidate.GetProperty("epoch").GetInt64();
        var clock = candidate.GetProperty("producerClock").GetDouble();
        var health = candidate.GetProperty("health").GetDouble();
        var max = candidate.GetProperty("healthMax").GetDouble();
        var envelopeHash = candidate.GetProperty("envelopeSha256").GetString();
        var frameHash = candidate.GetProperty("frameSha256").GetString();
        if (!ValidId(playerId) || !ValidId(targetId) || epoch is < 1 or > 9007199254740991 ||
            !double.IsFinite(clock) || clock < 0 || !double.IsFinite(health) ||
            !double.IsFinite(max) || max <= 0 || health < 0 || health > max ||
            !ValidHash(envelopeHash) || !ValidHash(frameHash))
            throw new FormatException("Invalid candidate provenance.");
        return new(playerId!, targetId!, epoch, sequence, clock, health, max,
            envelopeHash!, frameHash!);
    }

    private static bool ValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 128 &&
        id.All(c => c is >= ' ' and <= '~');

    private static bool ValidHash(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool Unique(JsonElement obj)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
            if (!keys.Add(p.Name)) return false;
        return true;
    }
}
