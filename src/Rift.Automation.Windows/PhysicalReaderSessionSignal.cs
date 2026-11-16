using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Strict, token-free reader startup event for a bounded coordinator.</summary>
public sealed record PhysicalReaderSessionSignal(Guid InstanceId, string Nonce)
{
    public static bool TryParse(string? line, out PhysicalReaderSessionSignal? signal)
    {
        signal = null;
        if (string.IsNullOrEmpty(line) || line.Length > 1024) return false;
        try
        {
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!seen.Add(property.Name) || property.Name is not
                    ("eventType" or "instanceId" or "nonce")) return false;
            if (seen.Count != 3 ||
                root.GetProperty("eventType").GetString() != "autofight-reader-session-v1") return false;
            var id = root.GetProperty("instanceId").GetGuid();
            var nonce = root.GetProperty("nonce").GetString();
            if (id == Guid.Empty || nonce is null || nonce.Length != 32 ||
                !nonce.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
            signal = new(id, nonce);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or ArgumentException)
        {
            return false;
        }
    }
}
