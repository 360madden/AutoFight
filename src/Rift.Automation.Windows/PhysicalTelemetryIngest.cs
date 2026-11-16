using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Read-only physical RIFT receiver. Separate from VMware lab ingest.</summary>
public sealed record PhysicalTelemetryBinding(
    Guid ReaderInstanceId, string ReaderNonce, int GameProcessId,
    long GameProcessStartUtcTicks, string GameExecutablePath, string GameExecutableSha256);

public sealed record PhysicalTelemetryReceipt(
    long Sequence, string ReaderStatus, DateTimeOffset ReceivedAtUtc,
    string? TargetVitalsStatus, long? TargetVitalsSequence);

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
            if (root.TryGetProperty("targetVitalsCandidate", out var vitals) &&
                vitals.ValueKind == JsonValueKind.Object)
            {
                if (!Unique(vitals)) return false;
                vitalsStatus = vitals.GetProperty("status").GetString();
                if (vitalsStatus is not ("unavailable" or "not-found" or "conflict" or "candidate-unqualified"))
                    return false;
                if (vitals.TryGetProperty("sequence", out var seq))
                {
                    if (seq.ValueKind == JsonValueKind.Number)
                    {
                        vitalsSequence = seq.GetInt64();
                        if (vitalsSequence < 1 || vitalsStatus != "candidate-unqualified") return false;
                    }
                    else if (seq.ValueKind != JsonValueKind.Null) return false;
                }
                // Never promote candidate data without the independently pinned game identity.
                if (target.ValueKind == JsonValueKind.Null || status != "running")
                {
                    vitalsStatus = null;
                    vitalsSequence = null;
                }
            }
            lock (gate)
            {
                if (terminated || sequence <= lastSequence) return false;
                lastSequence = sequence;
                if (status == "terminated") terminated = true;
                latest = new(sequence, status!, receivedAtUtc.ToUniversalTime(), vitalsStatus, vitalsSequence);
            }
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Unique(JsonElement obj)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
            if (!keys.Add(p.Name)) return false;
        return true;
    }
}
