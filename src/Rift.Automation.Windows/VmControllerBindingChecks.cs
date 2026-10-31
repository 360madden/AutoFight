using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>
/// A nonsecret, captured host-side expectation. Not a credential or permission.
/// The two resource names are the independently reviewed 2026 RR2 VM fleet map.
/// </summary>
public sealed record VmHostBinding(
    string ExpectedGuestAddress, string ExpectedMachineName, string ExpectedVmx)
{
    public static VmHostBinding? FromConfig(JsonElement root, string configPath)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryString(root, "guestAddress", out string address) ||
            !TryString(root, "expectedMachineName", out string machine) ||
            !TryString(root, "vmx", out string vmx) ||
            !VmControllerBindingChecks.IsIPv4(address))
            return null;
        string rootDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        string resolved = Path.GetFullPath(Path.IsPathRooted(vmx)
            ? vmx : Path.Combine(rootDirectory, vmx));
        return new(address, machine, resolved);
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out JsonElement part) ||
            part.ValueKind != JsonValueKind.String)
            return false;
        value = part.GetString() ?? "";
        return value.Length is > 0 and <= 32768 && !string.IsNullOrWhiteSpace(value);
    }
}

/// <summary>
/// Pure, strict RR2 controller-to-pinned-telemetry comparison. Requires an
/// independently selected lab resource, not an editable config filename,
/// shared Windows machine name or arbitrary host port.
/// </summary>
public static class VmControllerBindingChecks
{
    public static string? ExpectedResource(string lab) => lab switch
    {
        "lab1" => "vm:rift-lab-1",
        "lab2" => "vm:rift-lab-2",
        _ => null
    };

    public static bool HasSourceBinding(string lab, VmTelemetrySnapshot? observation) =>
        ExpectedResource(lab) is not null &&
        VmPulseObservationChecks.IsUsable(lab, observation) &&
        observation!.ReaderSourceAddress is string source && IsIPv4(source) &&
        observation.GameProcessStartUtcTicks is > 0 &&
        !string.IsNullOrWhiteSpace(observation.GameExecutablePath) &&
        observation.GameExecutableSha256 is string hash && IsSha256(hash);

    /// <summary>
    /// The final pinned receipt must still describe the exact guest and game
    /// executable whose controller status was just authenticated. A matching
    /// stream nonce alone does not bind its source IP or executable path.
    /// </summary>
    public static bool HasStableSourceBinding(
        string lab, VmTelemetrySnapshot? reviewed, VmTelemetrySnapshot? latest) =>
        HasSourceBinding(lab, reviewed) &&
        HasSourceBinding(lab, latest) &&
        EqualIp(reviewed!.ReaderSourceAddress!, latest!.ReaderSourceAddress!) &&
        reviewed.GameProcessId == latest.GameProcessId &&
        reviewed.GameProcessStartUtcTicks == latest.GameProcessStartUtcTicks &&
        EqualPath(reviewed.GameExecutablePath!, latest.GameExecutablePath!) &&
        Equal(reviewed.GameExecutableSha256!, latest.GameExecutableSha256!);

    /// <summary>
    /// Freshness-only observations have no current XYZ. They may preserve
    /// a validated private source/producer identity for gap accounting only.
    /// This does not make stale data usable for input.
    /// </summary>
    public static bool HasStableFreshnessGap(
        string lab, VmTelemetrySnapshot? reviewed, VmTelemetrySnapshot? gap) =>
        HasSourceBinding(lab, reviewed) &&
        gap is { Status: "unavailable", FreshnessOnlyGap: true } &&
        gap.Lab == lab &&
        !string.IsNullOrEmpty(gap.StreamIdentity) &&
        string.Equals(reviewed!.StreamIdentity, gap.StreamIdentity, StringComparison.Ordinal) &&
        reviewed.AddonEpoch == gap.AddonEpoch &&
        reviewed.GameProcessId == gap.GameProcessId &&
        reviewed.GameProcessStartUtcTicks == gap.GameProcessStartUtcTicks &&
        gap.ReaderSourceAddress is string ip &&
        EqualIp(reviewed.ReaderSourceAddress!, ip) &&
        gap.GameExecutablePath is string path &&
        EqualPath(reviewed.GameExecutablePath!, path) &&
        gap.GameExecutableSha256 is string hash &&
        Equal(reviewed.GameExecutableSha256!, hash);

    public static string Classify(
        string lab, VmTelemetrySnapshot? observed, VmHostBinding? configured,
        JsonElement status)
    {
        if (!HasSourceBinding(lab, observed) || configured is null ||
            !IsIPv4(configured.ExpectedGuestAddress) ||
            string.IsNullOrWhiteSpace(configured.ExpectedMachineName) ||
            string.IsNullOrWhiteSpace(configured.ExpectedVmx))
            return "source_or_controller_binding_unavailable";

        try
        {
            if (status.ValueKind != JsonValueKind.Object || HasDuplicateProperties(status))
                return "invalid_controller_identity";
            if (status.TryGetProperty("error", out JsonElement statusError) &&
                statusError.ValueKind != JsonValueKind.Null)
                return "invalid_controller_identity";
            JsonElement host = Object(status, "host");
            JsonElement guest = Object(status, "guest");
            JsonElement target = Object(guest, "target");
            JsonElement coordinator = Object(host, "coordination");
            JsonElement supervisor = Object(coordinator, "supervisor");
            JsonElement console = Object(host, "consoleReservation");
            JsonElement vm = Object(host, "vmIdentity");

            // Parse required identity before checking readiness, rejecting
            // ambiguous, incorrectly typed and partial HTTP 200 status objects.
            string hostInstance = String(host, "instanceId");
            string guestInstance = String(guest, "instanceId");
            string hostMachine = String(host, "machineName");
            string targetMachine = String(target, "machineName");
            string sourceAddress = String(host, "sourceAddress");
            string resource = String(coordinator, "resource");
            string vmx = String(vm, "vmx");
            int vmPort = PositiveInt(vm, "port");
            int vmPid = PositiveInt(vm, "pid");
            int listenerPid = PositiveInt(vm, "listenerPid");
            long vmStart = PositiveLong(vm, "startTicks");
            string vmImage = String(vm, "image");
            string logEpoch = String(vm, "logEpoch");
            if (vmPort > ushort.MaxValue || vmPid != listenerPid ||
                !Path.IsPathFullyQualified(vmImage) ||
                !Path.GetFileName(vmImage).Equals("vmware-vmx.exe", StringComparison.OrdinalIgnoreCase) ||
                !DateTimeOffset.TryParse(logEpoch, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTimeOffset parsedEpoch) ||
                parsedEpoch.Year < 2000 || vmStart <= 0)
                return "invalid_controller_identity";
            int pid = PositiveInt(target, "processId");
            long start = PositiveLong(target, "processStartUtcTicks");
            string gamePath = String(target, "executablePath");
            string gameHash = String(target, "executableSha256");
            if (!IsIPv4(sourceAddress) || !IsSha256(gameHash))
                return "invalid_controller_identity";

            bool connected = Boolean(host, "connected");
            bool held = Boolean(console, "held");
            bool closing = Boolean(console, "closing");
            bool faulted = Boolean(console, "faulted");
            bool shuttingDown = Boolean(coordinator, "shutdownRequested");
            bool threadAlive = Boolean(supervisor, "threadAlive");
            bool armed = Boolean(guest, "armed");
            bool busy = Boolean(guest, "busy");
            bool recovery = Boolean(guest, "recoveryRequired");
            bool foreground = Boolean(target, "isForeground");
            double expires = Number(guest, "expiresInSeconds");
            JsonElement down = Array(guest, "desktopInputsDown");

            if (resource != ExpectedResource(lab) ||
                !EqualIp(sourceAddress, configured.ExpectedGuestAddress) ||
                !EqualIp(sourceAddress, observed!.ReaderSourceAddress!) ||
                !Equal(hostMachine, configured.ExpectedMachineName) ||
                !Equal(targetMachine, configured.ExpectedMachineName) ||
                hostInstance != guestInstance ||
                pid != observed.GameProcessId ||
                start != observed.GameProcessStartUtcTicks ||
                !EqualPath(gamePath, observed.GameExecutablePath!) ||
                !Equal(gameHash, observed.GameExecutableSha256!) ||
                !EqualPath(vmx, configured.ExpectedVmx))
                return "controller_identity_mismatch";

            if (!connected || !held || closing || faulted || shuttingDown ||
                String(coordinator, "state") != "ready" ||
                String(supervisor, "state") != "running" || !threadAlive ||
                !armed || busy || recovery || !foreground ||
                expires <= 0 || down.GetArrayLength() != 0)
                return "controller_not_ready";
            return "clean";
        }
        catch (Exception ex) when (ex is JsonException or FormatException or
            KeyNotFoundException or InvalidOperationException or ArgumentException or
            NotSupportedException or OverflowException)
        {
            return "invalid_controller_identity";
        }
    }

    public static bool IsIPv4(string value) =>
        IPAddress.TryParse(value, out IPAddress? ip) &&
        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private static bool EqualIp(string a, string b) =>
        IPAddress.TryParse(a, out IPAddress? x) &&
        IPAddress.TryParse(b, out IPAddress? y) &&
        x!.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
        x.Equals(y);

    internal static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool Equal(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool EqualPath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static JsonElement Object(JsonElement parent, string name)
    {
        JsonElement found = parent.GetProperty(name);
        return found.ValueKind == JsonValueKind.Object
            ? found : throw new FormatException("Expected status object");
    }

    private static JsonElement Array(JsonElement parent, string name)
    {
        JsonElement found = parent.GetProperty(name);
        return found.ValueKind == JsonValueKind.Array
            ? found : throw new FormatException("Expected status array");
    }

    private static string String(JsonElement parent, string name)
    {
        JsonElement value = parent.GetProperty(name);
        string text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : throw new FormatException("Expected status string");
        return text.Length is > 0 and <= 32768 && !string.IsNullOrWhiteSpace(text)
            ? text : throw new FormatException("Empty or oversized status identity");
    }

    private static bool Boolean(JsonElement parent, string name)
    {
        JsonElement value = parent.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException("Expected status boolean")
        };
    }

    private static int PositiveInt(JsonElement parent, string name)
    {
        int result = parent.GetProperty(name).GetInt32();
        return result > 0 ? result : throw new FormatException("Invalid process ID");
    }

    private static long PositiveLong(JsonElement parent, string name)
    {
        long result = parent.GetProperty(name).GetInt64();
        return result > 0 ? result : throw new FormatException("Invalid process start");
    }

    private static double Number(JsonElement parent, string name)
    {
        double number = parent.GetProperty(name).GetDouble();
        return double.IsFinite(number) ? number : throw new FormatException("Invalid status number");
    }

    private static bool HasDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
                if (!seen.Add(property.Name) || HasDuplicateProperties(property.Value))
                    return true;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
                if (HasDuplicateProperties(item)) return true;
        }
        return false;
    }
}
