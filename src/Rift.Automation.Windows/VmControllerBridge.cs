using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Loopback-only adapter for a separately supervised VMware controller.
/// A request acknowledgment is not proof that a RIFT action succeeded.</summary>
public sealed class VmControllerBridge : IDisposable
{
    private const int MaxResponseBytes = 65536;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly int _port;
    private readonly string _bearer;
    private readonly VmHostBinding? _binding;

    public VmControllerBridge(string configPath, HttpMessageHandler? transport = null,
        TimeProvider? timeProvider = null)
    {
        using var config = ParseFile(configPath, MaxResponseBytes);
        int port = config.RootElement.GetProperty("uiPort").GetInt32();
        string token = config.RootElement.GetProperty("operatorToken").GetString() ?? "";
        if (port is < 1 or > 65535 || token.Length is < 16 or > 512)
            throw new ArgumentException("Invalid controller configuration.");
        _port = port;
        _bearer = token;
        // Capture the controller's nonsecret VM binding once; a later path
        // edit must not retarget an already constructed bridge.
        _binding = VmHostBinding.FromConfig(config.RootElement, configPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _http = transport is null
            ? new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            : new HttpClient(transport, disposeHandler: false);
        _http.BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        // ResponseHeadersRead only applies HttpClient.Timeout through headers.
        // One explicit deadline below must also cover the bounded body read.
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<VmControllerResult> StatusAsync(CancellationToken ct = default)
    {
        VmControllerResult result = await RequestAsync("GET", "api/status", null, ct);
        if (!result.HostAccepted || result.Body is null)
            return result;
        using var document = JsonDocument.Parse(result.Body);
        var root = document.RootElement;
        if (!root.TryGetProperty("host", out var host) ||
            !host.TryGetProperty("connected", out var conn) ||
            conn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return result with { HostAccepted = false, ErrorCode = "invalid_status", Body = null };
        return result with
        {
            Connected = conn.GetBoolean(),
            GuestResponded = root.TryGetProperty("guest", out _),
            Body = null
        };
    }

    /// <summary>
    /// Read-only, authenticated binding preflight on the same endpoint later used
    /// for a single pulse. Its selected-lab resource is independent of the
    /// editable host config, and its target must match pinned telemetry.
    /// This is not an atomic guarantee that the target stays fixed until POST.
    /// </summary>
    public async Task<VmControllerResult> VerifyPulseTargetAsync(
        string selectedLab, VmTelemetrySnapshot? observation, CancellationToken ct = default)
    {
        if (_binding is null || !VmControllerBindingChecks.HasSourceBinding(selectedLab, observation))
            return new(false, false, false, false, "source_or_controller_binding_unavailable");
        VmControllerResult fetched = await RequestAsync("GET", "api/status", null, ct);
        if (!fetched.HostAccepted || fetched.Body is null)
            return fetched with { HostAccepted = false, Body = null, GameFeedbackConfirmed = false };
        try
        {
            using var json = JsonDocument.Parse(fetched.Body);
            string check = VmControllerBindingChecks.Classify(
                selectedLab, observation, _binding, json.RootElement);
            return new(check == "clean", check == "clean", check == "clean", false,
                check == "clean" ? null : check, fetched.HttpStatus);
        }
        catch (JsonException)
        {
            return new(false, false, false, false, "invalid_controller_identity", fetched.HttpStatus);
        }
    }

    /// <summary>Requires an existing, independently reviewed lease and 15s arm artifact.
    /// Does not acquire authority or silently retry a possibly delivered input.</summary>
    public async Task<VmControllerResult> PulseAsync(
        string leaseFile, string armFile, string key, int holdMilliseconds,
        CancellationToken ct = default)
    {
        if (key is not ("w" or "a" or "s" or "d" or "q" or "e") ||
            holdMilliseconds is < 20 or > 250)
            throw new ArgumentException("Only one bounded w/a/s/d/q/e pulse is supported.");
        using var lease = ParseFile(leaseFile, 8192);
        using var arm = ParseFile(armFile, 8192);
        JsonElement grant = lease.RootElement;
        JsonElement armData = arm.RootElement;
        // Private lease files may contain additional ownership metadata.
        string requestId = grant.GetProperty("request_id").GetString() ?? "";
        string accessToken = grant.GetProperty("token").GetString() ?? "";
        string armToken = armData.GetProperty("armToken").GetString() ?? "";
        if (requestId.Length is < 1 or > 128 ||
            !requestId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-') ||
            accessToken.Length != 64 || !accessToken.All(Uri.IsHexDigit) ||
            armToken.Length is < 16 or > 512)
            throw new ArgumentException("Missing or malformed private authorization.");
        var body = new
        {
            access = new { request_id = requestId, token = accessToken },
            payload = new
            {
                armToken,
                action = new
                {
                    keys = new[] { key }, holdMilliseconds,
                    mouseButton = (string?)null, deltaX = 0, deltaY = 0, wheelNotches = 0
                }
            }
        };
        // One explicit send only. HTTP 200 may still describe guest executor
        // failure, cancellation, uncertain release or required recovery.
        VmControllerResult sent = await RequestAsync("POST", "api/action", body, ct);
        if (!sent.HostAccepted)
            return sent with { Body = null, GameFeedbackConfirmed = false };
        string outcome = ClassifyActionAcknowledgment(sent.Body);
        return sent with
        {
            HostAccepted = outcome == "clean",
            ErrorCode = outcome == "clean" ? null : outcome,
            Body = null,
            GameFeedbackConfirmed = false
        };
    }

    private static string ClassifyActionAcknowledgment(string? body)
    {
        if (body is null) return "invalid_action_ack";
        try
        {
            using var json = JsonDocument.Parse(body);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root) ||
                !root.TryGetProperty("result", out JsonElement execution) ||
                execution.ValueKind != JsonValueKind.Object || HasDuplicateProperties(execution) ||
                !TryBoolean(root, "recoveryRequired", out bool recoveryRequired) ||
                !TryBoolean(execution, "deliverySucceeded", out bool deliverySucceeded) ||
                !TryBoolean(execution, "releaseSucceeded", out bool releaseSucceeded) ||
                !TryBoolean(execution, "cancelled", out bool cancelled) ||
                !TryBoolean(execution, "targetLost", out bool targetLost) ||
                !TryBoolean(execution, "stateUncertain", out bool stateUncertain) ||
                !TryBoolean(execution, "requiresCallerDisarm", out bool requiresCallerDisarm) ||
                !execution.TryGetProperty("error", out JsonElement error) ||
                error.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                return "invalid_action_ack";
            // A guest exception is a separate top-level error envelope, never
            // a clean executor result; reject contradictory host/proxy output.
            if (root.TryGetProperty("error", out JsonElement topError))
            {
                if (topError.ValueKind == JsonValueKind.String)
                    return "action_outcome_unconfirmed";
                if (topError.ValueKind != JsonValueKind.Null)
                    return "invalid_action_ack";
            }
            return deliverySucceeded && releaseSucceeded && !cancelled && !targetLost &&
                !stateUncertain && !requiresCallerDisarm && !recoveryRequired &&
                error.ValueKind == JsonValueKind.Null
                ? "clean" : "action_outcome_unconfirmed";
        }
        catch (JsonException)
        {
            return "invalid_action_ack";
        }
    }

    private static bool TryBoolean(JsonElement value, string property, out bool result)
    {
        result = false;
        if (!value.TryGetProperty(property, out JsonElement member) ||
            member.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        result = member.GetBoolean();
        return true;
    }

    private static bool HasDuplicateProperties(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!names.Add(property.Name)) return true;
        return false;
    }

    private async Task<VmControllerResult> RequestAsync(string method, string path, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
        req.Headers.TryAddWithoutValidation("Origin", $"http://127.0.0.1:{_port}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        // Bound the whole one-send request: headers and every response-body read.
        // A timeout leaves delivery ambiguous and must never trigger a retry.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        CancellationToken requestToken = linked.Token;
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, requestToken);
            await using Stream stream = await response.Content.ReadAsStreamAsync(requestToken);
            byte[] bytes = new byte[MaxResponseBytes + 1];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(count), requestToken);
                if (read == 0) break;
                count += read;
            }
            if (count > MaxResponseBytes)
                return new(false, false, false, false, "response_too_large", (int)response.StatusCode);
            using var json = JsonDocument.Parse(bytes.AsMemory(0, count));
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                return new(false, false, false, false, "invalid_response", (int)response.StatusCode);
            // Response data may include sensitive controller metadata: never return or log it.
            string? error = response.IsSuccessStatusCode ? null : "controller_rejected";
            string? safe = response.IsSuccessStatusCode ? Encoding.UTF8.GetString(bytes, 0, count) : null;
            return new(response.IsSuccessStatusCode, false, false, false, error,
                (int)response.StatusCode, safe);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, false, false, false,
                deadline.IsCancellationRequested ? "transport_timeout" : "transport_interrupted");
        }
        catch (HttpRequestException)
        {
            return new(false, false, false, false, "controller_unreachable");
        }
        catch (JsonException)
        {
            return new(false, false, false, false, "invalid_response");
        }
    }

    private static JsonDocument ParseFile(string path, int limit)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > limit) throw new ArgumentException("Authorization file too large.");
        byte[] bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        using var check = JsonDocument.Parse(bytes);
        if (check.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected one JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in check.RootElement.EnumerateObject())
            if (!names.Add(property.Name))
                throw new ArgumentException("Duplicate JSON key.");
        return JsonDocument.Parse(bytes);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record VmControllerResult(
    bool HostAccepted, bool Connected, bool GuestResponded, bool GameFeedbackConfirmed,
    string? ErrorCode, int? HttpStatus = null, string? Body = null);
