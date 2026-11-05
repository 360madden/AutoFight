using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rift.Automation.Windows;

internal static class StandaloneTelemetryHostConfigurationSpecs
{
    // Test-only sentinel: do not include its value in test names or diagnostics.
    private const string Secret = "private-test-delivery-token-0123456789-abcdef";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string ValidJson() => JsonSerializer.Serialize(new
    {
        binding = new
        {
            lab = "lab1",
            guestIpv4 = "192.168.56.128",
            readerInstanceId = "reader-test-01",
            readerNonce = "0123456789abcdef0123456789abcdef",
            gameProcessId = 1001,
            gameProcessStartUtcTicks = 1234567891011L,
            gameExecutablePath = @"C:\RIFT\rift_x64.exe",
            gameExecutableSha256 = new string('a', 64)
        },
        bindIpv4 = "192.168.56.1",
        port = 49123,
        deliveryToken = Secret
    });

    private static void Reject(
        string json, Action<bool, string> check, string reason) =>
        Reject(Utf8(json), check, reason);

    private static void Reject(
        byte[] bytes, Action<bool, string> check, string reason)
    {
        bool rejected = false;
        try
        {
            _ = StandaloneTelemetryHostConfiguration.Parse(bytes);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or ArgumentException)
        {
            rejected = true;
            check(!exception.Message.Contains(Secret, StringComparison.Ordinal),
                "invalid configuration must not disclose the delivery token");
        }

        check(rejected, reason);
    }

    private static string Changed(
        Action<JsonObject> mutate)
    {
        JsonObject json = JsonNode.Parse(ValidJson())!.AsObject();
        mutate(json);
        return json.ToJsonString();
    }

    internal static void Run(
        Action<string, Action> test, Action<bool, string> check)
    {
        test("Standalone host configuration accepts one complete pinned reader", () =>
        {
            StandaloneTelemetryHostConfiguration config =
                StandaloneTelemetryHostConfiguration.Parse(Utf8(ValidJson()));

            check(config.Binding.Lab == "lab1" &&
                config.Binding.GuestIpv4 == "192.168.56.128" &&
                config.Binding.ReaderInstanceId == "reader-test-01" &&
                config.Binding.ReaderNonce == "0123456789abcdef0123456789abcdef" &&
                config.Binding.GameProcessId == 1001 &&
                config.Binding.GameProcessStartUtcTicks == 1234567891011L &&
                config.Binding.GameExecutablePath == @"C:\RIFT\rift_x64.exe" &&
                config.Binding.GameExecutableSha256 == new string('a', 64) &&
                config.BindIpv4 == "192.168.56.1" &&
                config.Port == 49123 &&
                config.DeliveryToken == Secret,
                "all explicit host and guest identity fields roundtrip");
        });

        test("Standalone host configuration rejects wrong JSON types", () =>
        {
            Reject(Changed(json => json["port"] = "49123"), check,
                "numeric port cannot be a JSON string");
            Reject(Changed(json => json["binding"]!["gameProcessId"] = "1001"),
                check, "game PID cannot be a JSON string");
            Reject(Changed(json => json["bindIpv4"] = true), check,
                "bind address cannot be boolean");
            Reject("[]", check, "top-level array cannot be configuration");
            Reject("null", check, "null cannot be configuration");
        });

        test("Standalone host configuration rejects unknown fields", () =>
        {
            Reject(Changed(json => json["unexpected"] = true), check,
                "unknown top-level setting");
            Reject(Changed(json => json["binding"]!["unexpected"] = "value"),
                check, "unknown nested binding field");
        });

        test("Standalone host configuration rejects duplicate fields", () =>
        {
            string json = ValidJson();
            Reject(json.Replace("\"port\":49123",
                    "\"port\":49123,\"port\":49124",
                    StringComparison.Ordinal),
                check, "duplicate exact root property");
            Reject(json.Replace("\"port\":49123",
                    "\"port\":49123,\"Port\":49124",
                    StringComparison.Ordinal),
                check, "case-only duplicate root property");
            Reject(json.Replace("\"gameProcessId\":1001",
                    "\"gameProcessId\":1001,\"gameProcessId\":1002",
                    StringComparison.Ordinal),
                check, "duplicate nested binding property");
        });

        test("Standalone host configuration rejects missing identity or credentials", () =>
        {
            Reject(Changed(json => json.Remove("binding")), check,
                "missing binding");
            Reject(Changed(json => json["binding"] = null), check,
                "null binding");
            Reject(Changed(json => json.Remove("bindIpv4")), check,
                "missing host IPv4");
            Reject(Changed(json => json.Remove("deliveryToken")), check,
                "missing private token");
        });

        test("Standalone host configuration enforces bounded input size", () =>
        {
            Reject([], check, "empty configuration");
            Reject(new byte[StandaloneTelemetryHostConfiguration.MaximumConfigBytes + 1],
                check, "oversized configuration before parsing");
        });

        test("Standalone host configuration protects token in diagnostics", () =>
        {
            StandaloneTelemetryHostConfiguration config =
                StandaloneTelemetryHostConfiguration.Parse(Utf8(ValidJson()));
            check(!config.ToString().Contains(Secret, StringComparison.Ordinal),
                "formatted configuration must redact private token");

            Reject(Changed(json =>
                json["unexpected"] = Secret), check,
                "parser error must never contain token");
        });
    }
}
