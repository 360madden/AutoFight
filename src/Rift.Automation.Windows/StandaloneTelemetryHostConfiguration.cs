using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rift.Automation.Windows;

/// <summary>
/// Private, explicitly reviewed configuration for one read-only host receiver.
/// Never print this object: it contains the guest delivery token.
/// </summary>
public sealed record StandaloneTelemetryHostConfiguration(
    StandaloneTelemetryBinding Binding,
    string BindIpv4,
    int Port,
    string DeliveryToken)
{
    public const int MaximumConfigBytes = 16 * 1024;

    // Record-generated ToString otherwise exposes the bearer token.
    public override string ToString() => "StandaloneTelemetryHostConfiguration [redacted]";

    public static StandaloneTelemetryHostConfiguration Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A private configuration file is required.", nameof(path));

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] bytes = new byte[MaximumConfigBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        return Parse(bytes.AsSpan(0, count));
    }

    public static StandaloneTelemetryHostConfiguration Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 0 or > MaximumConfigBytes)
            throw new InvalidDataException("Private telemetry configuration size is invalid.");

        using var document = JsonDocument.Parse(bytes.ToArray(),
            new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !HasUniqueProperties(document.RootElement))
            throw new InvalidDataException("Private telemetry configuration has invalid fields.");

        var settings = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            MaxDepth = 16,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict
        };
        StandaloneTelemetryHostConfiguration? config =
            JsonSerializer.Deserialize<StandaloneTelemetryHostConfiguration>(bytes, settings);
        if (config is null || config.Binding is null ||
            string.IsNullOrWhiteSpace(config.BindIpv4) ||
            string.IsNullOrWhiteSpace(config.DeliveryToken))
            throw new InvalidDataException("Private telemetry configuration is incomplete.");
        return config;
    }

    private static bool HasUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().All(HasUniqueProperties);
        if (element.ValueKind != JsonValueKind.Object)
            return true;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name) || !HasUniqueProperties(property.Value))
                return false;
        }
        return true;
    }
}
