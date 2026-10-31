using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rift.Automation.Windows;

public sealed record VmTelemetrySnapshot(
    string Lab, string Status, string Reason, DateTimeOffset CheckedAtUtc,
    DateTimeOffset? ReceivedAtUtc, double? ReceiptAgeMs,
    long? OuterSequence, long? AddonSequence, long? AddonEpoch,
    int? GameProcessId, double? X, double? Y, double? Z,
    string SourceScope = "none",
    [property: JsonIgnore] string? StreamIdentity = null,
    [property: JsonIgnore] double? AddonClock = null,
    [property: JsonIgnore] string? ReaderSourceAddress = null,
    [property: JsonIgnore] long? GameProcessStartUtcTicks = null,
    [property: JsonIgnore] string? GameExecutablePath = null,
    [property: JsonIgnore] string? GameExecutableSha256 = null,
    [property: JsonIgnore] bool FreshnessOnlyGap = false,
    [property: JsonIgnore] double? GapProducerX = null,
    [property: JsonIgnore] double? GapProducerY = null,
    [property: JsonIgnore] double? GapProducerZ = null)
{
    public bool HasPosition => Status == "fresh_observed" &&
        X.HasValue && Y.HasValue && Z.HasValue;
    // Full sustained qualification plus independent action feedback is a separate milestone.
    public bool CanAutomate => false;
}

/// <summary>
/// Read an existing RR2 host receiver's latest.json using a reviewed source-pin file.
/// Does not start a receiver, perform a memory scan, or touch game/controller input.
/// Fresh observations are diagnostic only until independently qualified.
/// </summary>
public static class VmTelemetryReader
{
    private const int MaxPinBytes = 32768;
    private const int MaxReceiptBytes = 133120;
    private const double MaxAgeMs = 2000;
    private const double ClockSkewMs = 250;

    public static VmTelemetrySnapshot ReadPinned(string pinFile, DateTimeOffset? nowUtc = null)
    {
        DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
        string lab = "unknown";
        try
        {
            byte[] pinBytes = ReadBounded(pinFile, MaxPinBytes);
            using var pin = Parse(pinBytes);
            // Validate the entire pin identity before attributing any later
            // receipt-file failure to its lab. A claimed lab alone is not proof.
            string validatedLab = ValidatedPinLab(pin.RootElement);
            string path = RequiredString(pin.RootElement, "receiptPath");
            if (string.IsNullOrWhiteSpace(path))
                throw new FormatException("Empty receipt path");
            lab = validatedLab;
            byte[] receiptBytes = ReadBounded(path, MaxReceiptBytes);
            return ParsePinned(pinBytes, receiptBytes, now);
        }
        catch (InvalidDataException)
        {
            return Unavailable(lab, "size_limit", now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return Unavailable(lab, "file_unavailable", now);
        }
        catch (Exception ex) when (ex is JsonException or FormatException
            or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            return Unavailable("unknown", "invalid_source_pin", now);
        }
    }

    public static VmTelemetrySnapshot ParsePinned(
        ReadOnlySpan<byte> sourcePin, ReadOnlySpan<byte> receipt, DateTimeOffset nowUtc)
    {
        string lab = "unknown";
        try
        {
            if (sourcePin.Length is <= 0 or > MaxPinBytes)
                return Unavailable(lab, "size_limit", nowUtc);
            using var pin = Parse(sourcePin);
            JsonElement config = pin.RootElement;
            lab = ValidatedPinLab(config);
            if (receipt.Length is <= 0 or > MaxReceiptBytes)
                return Unavailable(lab, "size_limit", nowUtc);
            using var document = Parse(receipt);
            JsonElement root = document.RootElement;
            if (RequiredLong(root, "schemaVersion") != 1)
                return Unavailable(lab, "schema_invalid", nowUtc);

            JsonElement reader = Property(config, "reader");
            JsonElement receiver = Property(config, "receiver");
            JsonElement expectedGame = Property(config, "gameIdentity");
            JsonElement eventData = Property(root, "observation");
            JsonElement target = Property(eventData, "target");
            string expectedNonce = RequiredString(reader, "nonce");
            string observedNonce = RequiredString(eventData, "nonce");
            if (RequiredString(receiver, "instanceId") != RequiredString(root, "receiverInstance")
                || RequiredString(reader, "instanceId") != RequiredString(eventData, "instanceId")
                || expectedNonce != observedNonce
                || expectedNonce != RequiredString(target, "readerSessionNonce")
                || RequiredString(reader, "sourceAddress") != RequiredString(root, "sourceAddress")
                || RequiredLong(expectedGame, "processId") != RequiredLong(target, "processId")
                || RequiredLong(expectedGame, "processStartUtcTicks") != RequiredLong(target, "processStartUtcTicks")
                || !StringEquals(RequiredString(expectedGame, "executablePath"),
                    RequiredString(target, "executablePath"))
                || !StringEquals(RequiredString(expectedGame, "executableSha256"),
                    RequiredString(target, "executableSha256")))
                return Unavailable(lab, "identity_mismatch", nowUtc);

            long outer = RequiredLong(eventData, "sequence");
            if (outer < 1) return Unavailable(lab, "invalid_sequence", nowUtc);
            DateTimeOffset receivedAt = RequiredUtc(root, "receivedAtUtc");
            DateTimeOffset capturedAt = RequiredUtc(eventData, "capturedAtUtc");
            double age = (nowUtc - receivedAt).TotalMilliseconds;
            double delivery = (receivedAt - capturedAt).TotalMilliseconds;
            if (RequiredString(eventData, "readerStatus") != "running")
                return Unavailable(lab, "reader_not_running", nowUtc,
                    receivedAt, age, outer);
            if (age < -ClockSkewMs || age > MaxAgeMs ||
                delivery < -ClockSkewMs || delivery > MaxAgeMs + ClockSkewMs)
                return WithVerifiedFreshnessGap(
                    Unavailable(lab, "stale_or_clock_uncertain", nowUtc, receivedAt, age, outer),
                    age >= -ClockSkewMs && delivery >= -ClockSkewMs &&
                        (age > MaxAgeMs || delivery > MaxAgeMs + ClockSkewMs),
                    lab, receiver, reader, target, eventData);

            JsonElement report = Property(eventData, "riftReader2");
            JsonElement selected = default;
            string scope = "whole-readable-private";
            string rejection = "source_not_fresh";
            string bindingStatus = RequiredString(report, "bindingStatus");
            string freshnessStatus = RequiredString(report, "freshnessStatus");
            string selectionStatus = RequiredString(report, "selectionStatus");
            // Only validated, bound, selected expiration is a recoverable timing gap.
            // Unbound/invalidated sources must never inherit that exemption.
            bool freshnessOnlyGap = bindingStatus == "bound" &&
                selectionStatus == "selected" && freshnessStatus == "expired";
            // Prefer the complete source when it is valid; otherwise validate the
            // fast candidate-region source against its independent full-scan anchor.
            if (bindingStatus == "bound" && freshnessStatus == "fresh" &&
                selectionStatus == "selected")
            {
                JsonElement scan = Property(report, "scan");
                if (RequiredString(scan, "status") != "complete" ||
                    Property(scan, "completed").ValueKind != JsonValueKind.True)
                    rejection = "scan_not_complete";
                else
                {
                    double advanceAge = RequiredNumber(report, "lastAdvanceAgeMilliseconds");
                    double timeout = RequiredNumber(report, "freshnessTimeoutMilliseconds");
                    double captureAge = Math.Max(0, (nowUtc - capturedAt).TotalMilliseconds);
                    if (advanceAge < 0 || timeout <= 0)
                        rejection = "invalid_source_timing";
                    else if (advanceAge + captureAge + ClockSkewMs >= timeout)
                    {
                        rejection = "source_expired";
                        freshnessOnlyGap = true;
                    }
                    else
                        selected = Property(report, "observation");
                }
            }
            bool fastValid = VmFastObservation.TrySelect(
                eventData, capturedAt, nowUtc, out JsonElement fastSelected);
            if (selected.ValueKind != JsonValueKind.Object)
            {
                if (!fastValid)
                    return WithVerifiedFreshnessGap(
                        Unavailable(lab, rejection, nowUtc, receivedAt, age, outer),
                        freshnessOnlyGap, lab, receiver, reader, target, eventData);
                selected = fastSelected;
                scope = "candidate-regions";
            }
            else if (fastValid)
            {
                // When both independent sources pass their own freshness gates,
                // don't silently choose whichever happens to be enumerated first.
                if (RequiredLong(selected, "epoch") != RequiredLong(fastSelected, "epoch") ||
                    RequiredString(Property(selected, "player"), "value") !=
                        RequiredString(Property(fastSelected, "player"), "value"))
                    return Unavailable(lab, "source_conflict", nowUtc, receivedAt, age, outer);
                long fullSequence = RequiredLong(selected, "sequence");
                long fastSequence = RequiredLong(fastSelected, "sequence");
                double fullClock = RequiredNumber(Property(selected, "clock"), "value");
                double fastClock = RequiredNumber(Property(fastSelected, "clock"), "value");
                if (fastSequence > fullSequence && fastClock > fullClock)
                {
                    selected = fastSelected;
                    scope = "candidate-regions";
                }
                else if (fullSequence > fastSequence && fullClock > fastClock)
                {
                    // Preserve full source, which is the newer advancing value.
                }
                else if (fullSequence != fastSequence || fullClock != fastClock ||
                    !JsonElement.DeepEquals(selected, fastSelected))
                    return Unavailable(lab, "source_conflict", nowUtc, receivedAt, age, outer);
            }
            if (RequiredLong(selected, "schemaVersion") != 1 ||
                !RequiredStatus(Property(selected, "player"), "ok") ||
                !RequiredStatus(Property(selected, "clock"), "ok"))
                return Unavailable(lab, "player_unavailable", nowUtc,
                    receivedAt, age, outer);
            JsonElement clock = Property(selected, "clock");
            if (RequiredNumber(clock, "value") < 0 ||
                string.IsNullOrWhiteSpace(RequiredString(Property(selected, "player"), "value")))
                return Unavailable(lab, "source_clock_or_player_invalid", nowUtc,
                    receivedAt, age, outer);
            long sequence = RequiredLong(selected, "sequence");
            long epoch = RequiredLong(selected, "epoch");
            if (sequence < 1 || epoch < 1)
                return Unavailable(lab, "invalid_sequence", nowUtc,
                    receivedAt, age, outer);

            JsonElement fields = Property(selected, "fields");
            double? x = Field(fields, "coordX");
            double? y = Field(fields, "coordY");
            double? z = Field(fields, "coordZ");
            if (x is null || y is null || z is null)
                return Unavailable(lab, "coordinates_unavailable", nowUtc,
                    receivedAt, age, outer, sequence, epoch);
            string fingerprint = StreamFingerprint(lab, receiver, reader, target,
                RequiredString(Property(selected, "player"), "value"));
            return new(lab, "fresh_observed",
                "Current receipt and source passed diagnostic checks; live movement not qualified.",
                nowUtc, receivedAt, age, outer, sequence, epoch,
                checked((int)RequiredLong(target, "processId")), x, y, z, scope,
                fingerprint, RequiredNumber(clock, "value"),
                RequiredString(reader, "sourceAddress"),
                RequiredLong(target, "processStartUtcTicks"),
                RequiredString(target, "executablePath"),
                RequiredString(target, "executableSha256"));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or
            KeyNotFoundException or InvalidOperationException or OverflowException or ArgumentException)
        {
            return Unavailable(lab, "invalid_receipt_or_pin", nowUtc);
        }
    }

    private static string ValidatedPinLab(JsonElement config)
    {
        if (RequiredLong(config, "schemaVersion") != 1)
            throw new FormatException("Unsupported pin schema");
        string lab = RequiredString(config, "lab");
        if (lab is not ("lab1" or "lab2"))
            throw new FormatException("Unsupported lab");
        JsonElement reader = Property(config, "reader");
        JsonElement receiver = Property(config, "receiver");
        JsonElement game = Property(config, "gameIdentity");
        _ = RequiredString(reader, "instanceId");
        _ = RequiredString(reader, "nonce");
        _ = RequiredString(reader, "sourceAddress");
        _ = RequiredString(receiver, "instanceId");
        _ = RequiredLong(game, "processId");
        _ = RequiredLong(game, "processStartUtcTicks");
        _ = RequiredString(game, "executablePath");
        _ = RequiredString(game, "executableSha256");
        return lab;
    }

    /// <summary>
    /// A temporary not-fresh report can be tolerated only when its full identity
    /// and retained producer record remain parseable and match the initial source.
    /// It still carries no usable XYZ and can never advance the watch clock.
    /// </summary>
    private static VmTelemetrySnapshot WithVerifiedFreshnessGap(
        VmTelemetrySnapshot unavailable, bool onlyFreshness,
        string lab, JsonElement receiver, JsonElement reader,
        JsonElement target, JsonElement eventData)
    {
        if (!onlyFreshness) return unavailable;
        try
        {
            JsonElement report = Property(eventData, "riftReader2");
            if (RequiredString(report, "bindingStatus") != "bound" ||
                RequiredString(report, "selectionStatus") != "selected" ||
                RequiredString(report, "freshnessStatus") is not ("fresh" or "expired"))
                return unavailable;
            double reportedAdvanceAge = RequiredNumber(report, "lastAdvanceAgeMilliseconds");
            double reportedTimeout = RequiredNumber(report, "freshnessTimeoutMilliseconds");
            if (reportedAdvanceAge < 0 || reportedTimeout <= 0)
                return unavailable;
            JsonElement scan = Property(report, "scan");
            if (RequiredString(scan, "status") != "complete" ||
                Property(scan, "completed").ValueKind != JsonValueKind.True)
                return unavailable;
            JsonElement selected = Property(report, "observation");
            if (RequiredLong(selected, "schemaVersion") != 1)
                return unavailable;
            JsonElement player = Property(selected, "player");
            JsonElement clock = Property(selected, "clock");
            if (!RequiredStatus(player, "ok") || !RequiredStatus(clock, "ok"))
                return unavailable;
            string playerId = RequiredString(player, "value");
            double producerClock = RequiredNumber(clock, "value");
            long producerSeq = RequiredLong(selected, "sequence");
            long producerEpoch = RequiredLong(selected, "epoch");
            if (string.IsNullOrWhiteSpace(playerId) || producerClock < 0 ||
                producerSeq < 1 || producerEpoch < 1)
                return unavailable;
            JsonElement fields = Property(selected, "fields");
            double? x = Field(fields, "coordX");
            double? y = Field(fields, "coordY");
            double? z = Field(fields, "coordZ");
            if (x is null || y is null || z is null)
                return unavailable;
            int gamePid = checked((int)RequiredLong(target, "processId"));
            long start = RequiredLong(target, "processStartUtcTicks");
            string sourceAddress = RequiredString(reader, "sourceAddress");
            string gamePath = RequiredString(target, "executablePath");
            string gameHash = RequiredString(target, "executableSha256");
            if (gamePid <= 0 || start <= 0 ||
                !VmControllerBindingChecks.IsIPv4(sourceAddress) ||
                !VmControllerBindingChecks.IsSha256(gameHash) ||
                string.IsNullOrWhiteSpace(gamePath))
                return unavailable;
            return unavailable with
            {
                FreshnessOnlyGap = true,
                StreamIdentity = StreamFingerprint(lab, receiver, reader, target, playerId),
                GameProcessId = gamePid,
                AddonEpoch = producerEpoch,
                AddonSequence = producerSeq,
                AddonClock = producerClock,
                GapProducerX = x,
                GapProducerY = y,
                GapProducerZ = z,
                ReaderSourceAddress = sourceAddress,
                GameProcessStartUtcTicks = start,
                GameExecutablePath = gamePath,
                GameExecutableSha256 = gameHash
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or
            KeyNotFoundException or InvalidOperationException or OverflowException or
            ArgumentException)
        {
            return unavailable;
        }
    }

    private static string StreamFingerprint(
        string lab, JsonElement receiver, JsonElement reader,
        JsonElement target, string playerId)
    {
        string sourceId = string.Join("|",
            lab, RequiredString(receiver, "instanceId"),
            RequiredString(reader, "instanceId"),
            RequiredString(reader, "nonce"),
            RequiredLong(target, "processId").ToString(CultureInfo.InvariantCulture),
            RequiredLong(target, "processStartUtcTicks").ToString(CultureInfo.InvariantCulture),
            RequiredString(target, "executableSha256"), playerId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)));
    }

    private static VmTelemetrySnapshot Unavailable(string lab, string reason, DateTimeOffset now,
        DateTimeOffset? received = null, double? age = null, long? outer = null,
        long? sequence = null, long? epoch = null) =>
        new(lab, "unavailable", reason, now, received, age, outer, sequence, epoch,
            null, null, null, null);

    private static byte[] ReadBounded(string path, int max)
    {
        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // One size snapshot bounds allocation even if the source file grows
        // between validation and reading. A changed size then fails closed.
        long length = stream.Length;
        if (length > max) throw new InvalidDataException("Input exceeds limit");
        byte[] data = new byte[checked((int)length)];
        stream.ReadExactly(data);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Input changed size");
        return data;
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> data)
    {
        var doc = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
        try
        {
            ValidateObject(doc.RootElement);
            return doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty member in value.EnumerateObject())
            {
                if (!keys.Add(member.Name)) throw new FormatException("Duplicate key");
                ValidateObject(member.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in value.EnumerateArray()) ValidateObject(entry);
        }
    }

    private static JsonElement Property(JsonElement value, string name) =>
        value.GetProperty(name);
    private static string RequiredString(JsonElement value, string key) =>
        Property(value, key).GetString() ?? throw new FormatException("Null field");
    private static long RequiredLong(JsonElement value, string key) =>
        Property(value, key).GetInt64();
    private static double RequiredNumber(JsonElement value, string key)
    {
        double d = Property(value, key).GetDouble();
        if (!double.IsFinite(d)) throw new FormatException("Non-finite value");
        return d;
    }
    private static DateTimeOffset RequiredUtc(JsonElement value, string key)
    {
        var dt = DateTimeOffset.Parse(RequiredString(value, key),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (dt.Offset != TimeSpan.Zero) throw new FormatException("Expected UTC");
        return dt;
    }
    private static bool RequiredStatus(JsonElement value, string expected) =>
        RequiredString(value, "status") == expected;
    private static double? Field(JsonElement fields, string name)
    {
        JsonElement field = Property(fields, name);
        return RequiredStatus(field, "ok") ? RequiredNumber(field, "value") : null;
    }
    private static bool StringEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
