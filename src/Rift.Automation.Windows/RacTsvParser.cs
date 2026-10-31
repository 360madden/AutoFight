using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RacTsvConsumer;

/// <summary>
/// Exact unit-detail/1 wire parser. Input is bytes, not clipboard text.
/// No lenient newline repair, Unicode replacement, game APIs, or file I/O.
/// </summary>
public static class RacTsvParser
{
    public const int MaxWireBytes = 8192;
    public const long MaxSequence = 9007199254740991L;

    private static readonly (string Key, string Type)[] Metadata =
    [
        ("schemaVersion", "number"),
        ("addonVersion", "string"),
        ("sequence", "number"),
        ("source", "string"),
        ("subject", "string"),
        ("mode", "string"),
        ("clockKind", "string"),
        ("sampledAt", "number"),
        ("recordStatus", "string"),
        ("recordReason", "string"),
    ];

    private static readonly (string Key, FieldType Type)[] Fields =
    [
        ("id", FieldType.String),
        ("name", FieldType.String),
        ("level", FieldType.Number),
        ("coordX", FieldType.Number),
        ("coordY", FieldType.Number),
        ("coordZ", FieldType.Number),
        ("combat", FieldType.Boolean),
    ];

    private static readonly Regex NumberPattern = new(
        @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?(?:e-?[1-9][0-9]*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Dictionary<string, string> ReasonStatus =
        new(StringComparer.Ordinal)
        {
            ["api_exception"] = "error",
            ["wrong_return_type"] = "invalid",
            ["field_access"] = "invalid",
            ["target_unresolved"] = "unavailable",
            ["lookup_id_invalid"] = "invalid",
            ["lookup_exception"] = "error",
            ["detail_exception"] = "error",
            ["detail_unavailable"] = "unavailable",
            ["target_lost"] = "unavailable",
            ["target_changed"] = "unavailable",
            ["detail_id_invalid"] = "invalid",
            ["identity_unverified"] = "unavailable",
            ["detail_id_mismatch"] = "invalid",
        };

    private static readonly HashSet<string> TargetReasons =
        new(StringComparer.Ordinal)
        {
            "target_unresolved", "lookup_id_invalid", "lookup_exception",
            "detail_exception", "detail_unavailable", "target_lost",
            "target_changed", "detail_id_invalid", "identity_unverified",
            "detail_id_mismatch",
        };

    private static readonly HashSet<string> LookupReasons =
        new(StringComparer.Ordinal)
        {
            "target_unresolved", "lookup_id_invalid", "lookup_exception",
        };

    private static readonly HashSet<string> InvalidFieldReasons =
        new(StringComparer.Ordinal) { "wrong_type", "nonfinite", "too_long", "blank" };

    private readonly record struct Row(byte[] Status, byte[] Value, byte[] Code);

    public static UnitRecord Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxWireBytes)
            Fail("wire_limit");
        if (payload.Length == 0 || payload[^1] != (byte)'\n' || payload.Contains((byte)'\r'))
            Fail("line_endings");

        // Bounded copy keeps all slices independent of caller lifetime/mutation.
        byte[] raw = payload.ToArray();
        List<byte[]> lines = [];
        int from = 0;
        for (int index = 0; index < raw.Length; index++)
        {
            if (raw[index] != (byte)'\n')
                continue;
            lines.Add(raw.AsSpan(from, index - from).ToArray());
            from = index + 1;
        }
        if (from != raw.Length || lines.Count == 0 || !AsciiEquals(lines[0], "RAC-TSV\t1"))
            Fail("magic");
        if (lines.Count < 12)
            Fail("row_count");

        Row[] metadata = new Row[Metadata.Length];
        for (int index = 0; index < Metadata.Length; index++)
        {
            var spec = Metadata[index];
            metadata[index] = ParseRow(lines[index + 1], "M", spec.Key, spec.Type);
        }

        RequireLiteral(metadata[0], "available", "1", "");
        byte[] addonVersion = RequireAvailable(metadata[1], maxDecoded: 32);
        if (!ValidVersion(addonVersion))
            Fail("addon_version");

        byte[] sequenceBytes = RequireAvailable(metadata[2], maxDecoded: 16);
        long sequence = ParseSequence(sequenceBytes);

        byte[] sourceBytes = RequireAvailable(metadata[3], maxDecoded: 32);
        if (!AsciiEquals(sourceBytes, "Inspect.Unit.Detail")
            && !AsciiEquals(sourceBytes, "Inspect.Unit.Lookup"))
            Fail("source");
        string source = Encoding.ASCII.GetString(sourceBytes);

        byte[] subjectBytes = RequireAvailable(metadata[4], maxDecoded: 8);
        if (!AsciiEquals(subjectBytes, "player") && !AsciiEquals(subjectBytes, "target"))
            Fail("subject");
        string subject = Encoding.ASCII.GetString(subjectBytes);

        RequireLiteral(metadata[5], "available", "manual", "");
        RequireLiteral(metadata[6], "available", "none", "");
        RequireLiteral(metadata[7], "unavailable", "", "not_sampled");

        byte[] statusBytes = RequireAvailable(metadata[8], maxDecoded: 16);
        if (!AsciiEquals(statusBytes, "available")
            && !AsciiEquals(statusBytes, "unavailable")
            && !AsciiEquals(statusBytes, "invalid")
            && !AsciiEquals(statusBytes, "error"))
            Fail("record_status");
        string status = Encoding.ASCII.GetString(statusBytes);

        string? reason = ParseReason(metadata[9], status);
        ValidateSourceAndReason(subject, source, status, reason);

        int expectedDataRows = status == "available" ? 17 : 10;
        int expectedTotalLines = expectedDataRows + 2; // header + records + trailer
        if (lines.Count != expectedTotalLines
            || !AsciiEquals(lines[^1], expectedDataRows == 17 ? "END\t17" : "END\t10"))
            Fail(status == "available" ? "success_shape" : "failure_shape");

        var fields = new Dictionary<string, UnitField>(StringComparer.Ordinal);
        if (status == "available")
        {
            for (int index = 0; index < Fields.Length; index++)
            {
                var spec = Fields[index];
                string typeName = spec.Type switch
                {
                    FieldType.String => "string",
                    FieldType.Number => "number",
                    FieldType.Boolean => "boolean",
                    _ => throw new InvalidOperationException("Unexpected schema type"),
                };
                Row row = ParseRow(lines[Metadata.Length + 1 + index],
                    "F", spec.Key, typeName);
                fields.Add(spec.Key, ParseField(row, spec.Key, spec.Type));
            }
            if (subject == "target")
            {
                UnitField targetId = fields["id"];
                if (targetId.Status != FieldStatus.Present)
                    Fail("target_id_required");
                byte[]? bytes = targetId.RawBytes;
                if (bytes is null || bytes.Length == 0 || bytes.Length > 128
                    || bytes.Any(value => value < 32 || value == 127))
                    Fail("target_id_invalid");
            }
        }

        return new UnitRecord(addonVersion, sequence, source, subject, status, reason, fields);
    }

    private static string? ParseReason(Row row, string status)
    {
        if (status == "available")
        {
            RequireLiteral(row, "unavailable", "", "not_applicable");
            return null;
        }
        if (AsciiEquals(row.Status, "unavailable")
            && row.Value.Length == 0
            && AsciiEquals(row.Code, "not_reported"))
            return null;

        if (!AsciiEquals(row.Status, "available") || row.Code.Length != 0)
            Fail("reason_status");
        byte[] raw = DecodeString(row.Value, maxDecoded: 64);
        string reason = Encoding.ASCII.GetString(raw);
        if (!ReasonStatus.TryGetValue(reason, out string? requiredStatus))
            Fail("failure_reason");
        if (requiredStatus != status)
            Fail("failure_reason_status");
        return reason;
    }

    private static void ValidateSourceAndReason(
        string subject, string source, string status, string? reason)
    {
        if (subject == "player" && source != "Inspect.Unit.Detail")
            Fail("player_source");
        if (subject == "target" && status == "available"
            && source != "Inspect.Unit.Detail")
            Fail("target_source");

        if (reason is not null && TargetReasons.Contains(reason) && subject != "target")
            Fail("target_reason_for_player");
        if (reason == "api_exception" && subject != "player")
            Fail("player_reason_for_target");

        if (source == "Inspect.Unit.Lookup")
        {
            // No Detail has been reached. Only pre-Detail lookup failures fit.
            if (subject != "target" || reason is null || !LookupReasons.Contains(reason))
                Fail("illegal_lookup_reason");
        }
        else if (reason is not null && LookupReasons.Contains(reason)
                 && reason != "lookup_exception" && reason != "lookup_id_invalid")
        {
            // Second lookup can throw or return malformed data after Detail.
            // A target-unresolved initial lookup cannot follow Detail.
            Fail("lookup_reason_after_detail");
        }

        if (reason is not null && TargetReasons.Contains(reason)
            && source == "Inspect.Unit.Lookup" && !LookupReasons.Contains(reason))
            Fail("detail_reason_without_detail");
    }

    private static UnitField ParseField(Row row, string name, FieldType type)
    {
        if (AsciiEquals(row.Status, "available"))
        {
            if (row.Code.Length != 0)
                Fail("available_field_detail");
            return type switch
            {
                FieldType.String => AvailableString(row.Value),
                FieldType.Number => new UnitField(type, FieldStatus.Present,
                    NumberValue: ParseCanonicalNumber(row.Value)),
                FieldType.Boolean => AvailableBoolean(row.Value),
                _ => throw new InvalidOperationException("Unexpected schema type"),
            };
        }

        if (row.Value.Length != 0)
            Fail("nonavailable_field_value");
        if (AsciiEquals(row.Status, "unavailable"))
        {
            if (AsciiEquals(row.Code, "masked_level"))
            {
                if (name != "level")
                    Fail("masked_nonlevel");
                return new UnitField(type, FieldStatus.Missing, Reason: "masked_level");
            }
            if (!AsciiEquals(row.Code, "api_nil"))
                Fail("missing_reason");
            return new UnitField(type, FieldStatus.Missing);
        }
        if (AsciiEquals(row.Status, "invalid"))
        {
            string code = StrictAscii(row.Code, "invalid_field_reason");
            if (!InvalidFieldReasons.Contains(code))
                Fail("invalid_field_reason");
            if ((code == "too_long" || code == "blank") && type != FieldType.String)
                Fail("invalid_reason_type");
            if (code == "nonfinite" && type != FieldType.Number)
                Fail("invalid_reason_type");
            return new UnitField(type, FieldStatus.Invalid, Reason: code);
        }
        Fail("field_status");
        throw new UnreachableException();
    }

    private static UnitField AvailableString(byte[] encoded)
    {
        byte[] raw = DecodeString(encoded, maxDecoded: 256);
        if (raw.Length == 0 || raw.All(value =>
                value is (byte)' ' or (byte)'\t' or (byte)'\n'
                    or (byte)'\r' or (byte)'\v' or (byte)'\f'))
            Fail("blank_available_string");
        return new UnitField(FieldType.String, FieldStatus.Present, RawBytes: raw);
    }

    private static UnitField AvailableBoolean(byte[] value)
    {
        if (AsciiEquals(value, "true"))
            return new UnitField(FieldType.Boolean, FieldStatus.Present, BooleanValue: true);
        if (AsciiEquals(value, "false"))
            return new UnitField(FieldType.Boolean, FieldStatus.Present, BooleanValue: false);
        Fail("boolean_value");
        throw new UnreachableException();
    }

    private static double ParseCanonicalNumber(ReadOnlySpan<byte> input)
    {
        if (input.Length == 0 || input.Length > 64)
            Fail("number_lexeme");
        string token = StrictAscii(input, "number_lexeme");
        if (!NumberPattern.IsMatch(token))
            Fail("number_lexeme");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value))
            Fail("number_parse");
        if (!double.IsFinite(value))
            Fail("number_nonfinite");

        string canonical;
        if (value == 0)
            canonical = BitConverter.DoubleToInt64Bits(value) < 0 ? "-0" : "0";
        else
        {
            canonical = value.ToString("G17", CultureInfo.InvariantCulture).ToLowerInvariant();
            int exponentMarker = canonical.IndexOf('e');
            if (exponentMarker >= 0)
            {
                string exponentText = canonical[(exponentMarker + 1)..];
                if (!int.TryParse(exponentText, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out int exponent))
                    Fail("number_parse");
                canonical = canonical[..exponentMarker]
                    + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            }
        }
        if (token != canonical)
            Fail("number_not_canonical");
        return value;
    }

    private static long ParseSequence(ReadOnlySpan<byte> input)
    {
        if (input.Length == 0 || input.Length > 16 || input[0] < (byte)'1'
            || input[0] > (byte)'9')
            Fail("sequence_lexeme");
        foreach (byte character in input)
        {
            if (character < (byte)'0' || character > (byte)'9')
                Fail("sequence_lexeme");
        }
        if (!long.TryParse(StrictAscii(input, "sequence_lexeme"),
                NumberStyles.None, CultureInfo.InvariantCulture, out long result))
            Fail("sequence_range");
        if (result < 1 || result > MaxSequence)
            Fail("sequence_range");
        return result;
    }

    private static bool ValidVersion(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0 || value.Length > 32)
            return false;
        int dots = 0;
        int digitsInPart = 0;
        foreach (byte current in value)
        {
            if (current >= (byte)'0' && current <= (byte)'9')
            {
                digitsInPart++;
            }
            else if (current == (byte)'.' && digitsInPart > 0 && dots < 2)
            {
                dots++;
                digitsInPart = 0;
            }
            else
            {
                return false;
            }
        }
        return dots == 2 && digitsInPart > 0;
    }

    private static Row ParseRow(ReadOnlySpan<byte> line,
        string expectedKind, string expectedKey, string expectedType)
    {
        byte[][] columns = new byte[6][];
        int count = 0;
        int from = 0;
        for (int index = 0; index < line.Length; index++)
        {
            if (line[index] != (byte)'\t')
                continue;
            if (count >= 5)
                Fail("column_count");
            columns[count++] = line[from..index].ToArray();
            from = index + 1;
        }
        if (count != 5)
            Fail("column_count");
        columns[5] = line[from..].ToArray();

        if (!AsciiEquals(columns[0], expectedKind)
            || !AsciiEquals(columns[1], expectedKey)
            || !AsciiEquals(columns[2], expectedType))
            Fail("row_order_or_type");
        return new Row(columns[3], columns[4], columns[5]);
    }

    private static void RequireLiteral(Row row, string status, string value, string code)
    {
        if (!AsciiEquals(row.Status, status)
            || !AsciiEquals(row.Value, value)
            || !AsciiEquals(row.Code, code))
            Fail("metadata_contract");
    }

    private static byte[] RequireAvailable(Row row, int maxDecoded)
    {
        if (!AsciiEquals(row.Status, "available") || row.Code.Length != 0)
            Fail("metadata_status");
        return DecodeString(row.Value, maxDecoded);
    }

    private static byte[] DecodeString(ReadOnlySpan<byte> encoded, int maxDecoded)
    {
        if (encoded.Length > maxDecoded * 3)
            Fail("encoded_string_limit");
        List<byte> decoded = new(Math.Min(encoded.Length, maxDecoded));
        for (int index = 0; index < encoded.Length;)
        {
            byte value = encoded[index];
            if (Unreserved(value))
            {
                decoded.Add(value);
                index++;
            }
            else if (value == (byte)'%')
            {
                if (index + 2 >= encoded.Length)
                    Fail("incomplete_percent_escape");
                int high = UpperHex(encoded[index + 1]);
                int low = UpperHex(encoded[index + 2]);
                if (high < 0 || low < 0)
                    Fail("noncanonical_percent_escape");
                byte original = (byte)((high << 4) | low);
                if (Unreserved(original))
                    Fail("overescaped_unreserved");
                decoded.Add(original);
                index += 3;
            }
            else
            {
                Fail("unescaped_byte");
            }
            if (decoded.Count > maxDecoded)
                Fail("decoded_string_limit");
        }
        return [.. decoded];
    }

    private static bool Unreserved(byte value) =>
        (value >= (byte)'A' && value <= (byte)'Z')
        || (value >= (byte)'a' && value <= (byte)'z')
        || (value >= (byte)'0' && value <= (byte)'9')
        || value is (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~';

    private static int UpperHex(byte value) =>
        value >= (byte)'0' && value <= (byte)'9' ? value - (byte)'0'
        : value >= (byte)'A' && value <= (byte)'F' ? value - (byte)'A' + 10
        : -1;

    private static bool AsciiEquals(ReadOnlySpan<byte> value, string expected)
    {
        if (value.Length != expected.Length)
            return false;
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] > 127 || value[index] != expected[index])
                return false;
        }
        return true;
    }

    private static string StrictAscii(ReadOnlySpan<byte> value, string failure)
    {
        foreach (byte letter in value)
        {
            if (letter > 127)
                Fail(failure);
        }
        return Encoding.ASCII.GetString(value);
    }

    [DoesNotReturn]
    private static void Fail(string code) => throw new RacTsvProtocolException(code);
}

public enum FieldType { String, Number, Boolean }
public enum FieldStatus { Present, Missing, Invalid }

public sealed record UnitField(
    FieldType Type,
    FieldStatus Status,
    byte[]? RawBytes = null,
    double? NumberValue = null,
    bool? BooleanValue = null,
    string? Reason = null);

public sealed record UnitRecord(
    byte[] AddonVersion,
    long Sequence,
    string Source,
    string Subject,
    string Status,
    string? Reason,
    IReadOnlyDictionary<string, UnitField> Fields)
{
    public string Contract => "unit-detail/1";
    public int SchemaVersion => 1;
    public string Mode => "manual";
    public string ClockKind => "none";
    public string SampledAtStatus => "unavailable";
}

public sealed class RacTsvProtocolException(string code) : FormatException(code)
{
    public string Code { get; } = code;
}
