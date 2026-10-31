using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>Reviewed identity for one VM guest telemetry stream.</summary>
public sealed record StandaloneTelemetryBinding(
    string Lab, string GuestIpv4, string ReaderInstanceId, string ReaderNonce,
    int GameProcessId, long GameProcessStartUtcTicks,
    string GameExecutablePath, string GameExecutableSha256);

/// <summary>
/// A recorded transport delivery is distinct from a fresh game observation.
/// An accepted delivery may have no target or usable player position.
/// </summary>
public sealed record StandaloneTelemetryDeliveryResult(
    bool Accepted, string Reason, VmTelemetrySnapshot Snapshot);

/// <summary>
/// Bounded, authenticated, in-memory ingest for the RiftReader2-compatible
/// guest LiveReaderSnapshot wire format. No service or input is started.
/// </summary>
public sealed class StandaloneTelemetryIngest
{
    public const int MaximumObservationBytes = 128 * 1024;

    private readonly object _gate = new();
    private readonly StandaloneTelemetryBinding _binding;
    private readonly string _guestIpv4;
    private readonly byte[] _token;
    private readonly byte[] _sourcePin;
    private byte[]? _lastReceipt;
    private VmTelemetrySnapshot? _failure;
    private long? _outerSequence;
    private long? _addonSequence;
    private long? _addonEpoch;
    private DateTimeOffset? _receivedAt;
    private string? _streamIdentity;
    private bool _lastTargetUnavailable;

    public StandaloneTelemetryIngest(
        StandaloneTelemetryBinding binding, string deliveryToken)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Lab is not ("lab1" or "lab2"))
            throw new ArgumentException("Invalid lab.", nameof(binding));

        _guestIpv4 = PrivateIpv4(binding.GuestIpv4);

        if (string.IsNullOrWhiteSpace(binding.ReaderInstanceId) ||
            binding.ReaderInstanceId.Length > 128 ||
            string.IsNullOrWhiteSpace(binding.ReaderNonce) ||
            binding.ReaderNonce.Length != 32 ||
            !binding.ReaderNonce.All(Uri.IsHexDigit) ||
            binding.GameProcessId < 1 ||
            binding.GameProcessStartUtcTicks < 1 ||
            string.IsNullOrWhiteSpace(binding.GameExecutablePath) ||
            binding.GameExecutablePath.Length > 1024 ||
            string.IsNullOrWhiteSpace(binding.GameExecutableSha256) ||
            binding.GameExecutableSha256.Length != 64 ||
            !binding.GameExecutableSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Invalid telemetry binding.", nameof(binding));
        }

        if (deliveryToken is null ||
            deliveryToken.Length is < 32 or > 512 ||
            deliveryToken.Any(c => c < '!' || c > '~'))
        {
            throw new ArgumentException(
                "A 32..512-character printable delivery token is required.",
                nameof(deliveryToken));
        }

        _binding = binding;
        _token = Encoding.ASCII.GetBytes(deliveryToken);
        ReceiverInstanceId = Guid.NewGuid().ToString("D");

        _sourcePin = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            lab = binding.Lab,
            reader = new
            {
                instanceId = binding.ReaderInstanceId,
                nonce = binding.ReaderNonce,
                sourceAddress = _guestIpv4
            },
            receiver = new
            {
                instanceId = ReceiverInstanceId
            },
            gameIdentity = new
            {
                processId = binding.GameProcessId,
                processStartUtcTicks = binding.GameProcessStartUtcTicks,
                executablePath = binding.GameExecutablePath,
                executableSha256 = binding.GameExecutableSha256
            }
        });
    }

    public string ReceiverInstanceId { get; }

    /// <summary>
    /// The HTTP listener must pass the actual peer socket address.
    /// Invalid authenticated traffic invalidates prior game observations.
    /// Unauthorized or wrong-peer traffic cannot mutate the receiver state.
    /// </summary>
    public StandaloneTelemetryDeliveryResult AcceptDelivery(
        ReadOnlyMemory<byte> body,
        string suppliedToken,
        string remoteIpv4,
        DateTimeOffset receivedAtUtc)
    {
        DateTimeOffset now = receivedAtUtc.ToUniversalTime();

        if (!Authenticated(suppliedToken))
            return Rejected("delivery_unauthorized", now);

        if (!IPAddress.TryParse(remoteIpv4, out IPAddress? remote) ||
            remote.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(remote.ToString(), _guestIpv4,
                StringComparison.Ordinal))
        {
            return Rejected("delivery_wrong_guest", now);
        }

        lock (_gate)
        {
            if (body.Length is <= 0 or > MaximumObservationBytes)
                return InvalidDelivery("delivery_size_limit", now);

            try
            {
                using JsonDocument observation = JsonDocument.Parse(
                    body, new JsonDocumentOptions { MaxDepth = 64 });

                JsonElement root = observation.RootElement;

                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("schemaVersion", out JsonElement version) ||
                    version.ValueKind != JsonValueKind.Number ||
                    !version.TryGetInt32(out int schema) || schema != 1)
                {
                    return InvalidDelivery("delivery_schema_invalid", now);
                }

                if (!HasUniqueProperties(root))
                    return InvalidDelivery("delivery_duplicate_field", now);

                string instanceId = RequiredString(root, "instanceId");
                long outerSequence = RequiredLong(root, "sequence");
                string nonce = RequiredString(root, "nonce");

                if (instanceId.Length > 128 ||
                    instanceId != _binding.ReaderInstanceId ||
                    nonce != _binding.ReaderNonce)
                {
                    return InvalidDelivery("delivery_identity_changed", now);
                }

                if (outerSequence < 1)
                    return InvalidDelivery("delivery_invalid_sequence", now);

                if (_outerSequence is long previous &&
                    outerSequence <= previous)
                {
                    return InvalidDelivery("delivery_replayed", now);
                }

                // RR2's guest producer sends target:null when the game is
                // unavailable. That is a valid delivery, never proof of XYZ.
                if (!root.TryGetProperty("target", out JsonElement target) ||
                    target.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                {
                    return InvalidDelivery("delivery_invalid_target", now);
                }

                bool targetUnavailable = target.ValueKind == JsonValueKind.Null;

                if (!targetUnavailable &&
                    (RequiredLong(target, "processId") != _binding.GameProcessId ||
                     RequiredLong(target, "processStartUtcTicks") !=
                         _binding.GameProcessStartUtcTicks ||
                     !string.Equals(RequiredString(target, "executablePath"),
                         _binding.GameExecutablePath,
                         StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(RequiredString(target, "executableSha256"),
                         _binding.GameExecutableSha256,
                         StringComparison.OrdinalIgnoreCase) ||
                     RequiredString(target, "readerSessionNonce") !=
                         _binding.ReaderNonce))
                {
                    return InvalidDelivery("identity_mismatch", now);
                }

                byte[] receipt = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1,
                    receiverInstance = ReceiverInstanceId,
                    receivedAtUtc = now.ToString("O"),
                    sourceAddress = _guestIpv4,
                    observation = root,
                    gameReady = false
                });

                VmTelemetrySnapshot result = targetUnavailable
                    ? Unavailable("game_target_unavailable", now, now, outerSequence)
                    : VmTelemetryReader.ParsePinned(_sourcePin, receipt, now);

                // A usable game sample requires continuity of its independent
                // addon clock and producer epoch; the transport sequence alone
                // does not establish this.
                if (result.HasPosition)
                {
                    if (_addonEpoch is long epoch && result.AddonEpoch != epoch)
                        return InvalidDelivery("delivery_epoch_changed", now);

                    if (_addonSequence is long sequence &&
                        result.AddonSequence is long nextSequence &&
                        nextSequence < sequence)
                    {
                        return InvalidDelivery("delivery_sequence_regressed", now);
                    }

                    if (_streamIdentity is not null &&
                        !string.Equals(_streamIdentity, result.StreamIdentity,
                            StringComparison.Ordinal))
                    {
                        return InvalidDelivery("delivery_identity_changed", now);
                    }

                    _addonSequence = result.AddonSequence;
                    _addonEpoch = result.AddonEpoch;
                    _streamIdentity = result.StreamIdentity;
                }

                // Critically, this advances even when the player is absent,
                // addon freshness expired, or the reader terminated.
                _lastReceipt = receipt;
                _lastTargetUnavailable = targetUnavailable;
                _failure = null;
                _receivedAt = now;
                _outerSequence = outerSequence;

                return new(true, "recorded", result);
            }
            catch (Exception ex) when (
                ex is JsonException or FormatException or KeyNotFoundException or
                InvalidOperationException or OverflowException)
            {
                return InvalidDelivery("delivery_invalid_json", now);
            }
        }
    }

    /// <summary>Compatibility API for diagnostic callers; not the HTTP success gate.</summary>
    public VmTelemetrySnapshot Accept(
        ReadOnlyMemory<byte> body,
        string suppliedToken,
        string remoteIpv4,
        DateTimeOffset receivedAtUtc) =>
        AcceptDelivery(body, suppliedToken, remoteIpv4, receivedAtUtc).Snapshot;

    /// <summary>
    /// Rechecks original receipt age. Polling never renews previously received XYZ.
    /// </summary>
    public VmTelemetrySnapshot ReadLatest(DateTimeOffset nowUtc)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();

        lock (_gate)
        {
            if (_lastReceipt is not null)
            {
                return _lastTargetUnavailable
                    ? Unavailable("game_target_unavailable", now,
                        _receivedAt, _outerSequence)
                    : VmTelemetryReader.ParsePinned(_sourcePin, _lastReceipt, now);
            }

            return _failure is null
                ? Unavailable("delivery_not_received", now)
                : _failure with { CheckedAtUtc = now };
        }
    }

    private bool Authenticated(string? suppliedToken)
    {
        if (suppliedToken is null || suppliedToken.Length > 512 ||
            suppliedToken.Any(c => c < '!' || c > '~'))
        {
            return false;
        }

        byte[] actual = Encoding.ASCII.GetBytes(suppliedToken);

        return actual.Length == _token.Length &&
            CryptographicOperations.FixedTimeEquals(actual, _token);
    }

    private StandaloneTelemetryDeliveryResult InvalidDelivery(
        string reason, DateTimeOffset now)
    {
        _lastReceipt = null;
        _lastTargetUnavailable = false;
        _failure = Unavailable(reason, now);
        return new(false, reason, _failure);
    }

    private StandaloneTelemetryDeliveryResult Rejected(
        string reason, DateTimeOffset now) =>
        new(false, reason, Unavailable(reason, now));

    private VmTelemetrySnapshot Unavailable(
        string reason, DateTimeOffset now,
        DateTimeOffset? received = null, long? outer = null) =>
        new(
            _binding.Lab, "unavailable", reason, now, received,
            received.HasValue ? (now - received.Value).TotalMilliseconds : null,
            outer, null, null, null, null, null, null);

    private static string RequiredString(JsonElement root, string property)
    {
        JsonElement value = root.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String)
            throw new FormatException("Invalid required string.");
        string? result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
            throw new FormatException("Empty required string.");
        return result;
    }

    private static long RequiredLong(JsonElement root, string property)
    {
        JsonElement value = root.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out long result))
        {
            throw new FormatException("Invalid required number.");
        }

        return result;
    }

    private static bool HasUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty field in value.EnumerateObject())
            {
                if (!names.Add(field.Name) ||
                    !HasUniqueProperties(field.Value))
                    return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (!HasUniqueProperties(item))
                    return false;
            }
        }

        return true;
    }

    private static string PrivateIpv4(string raw)
    {
        if (!IPAddress.TryParse(raw, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "Expected private guest IPv4.", nameof(raw));
        }

        byte[] octets = address.GetAddressBytes();

        if (!(octets[0] == 10 ||
              octets[0] == 172 && octets[1] is >= 16 and <= 31 ||
              octets[0] == 192 && octets[1] == 168))
        {
            throw new ArgumentException(
                "Expected private guest IPv4.", nameof(raw));
        }

        return address.ToString();
    }
}
