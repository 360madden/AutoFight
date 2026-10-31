using System.IO;
using System.Text;
using RacTsvConsumer;

namespace Rift.Automation.Windows;

public sealed record ManualNumber(string Status, double? Value, string? Reason);
public sealed record ManualFlag(string Status, bool? Value, string? Reason);

/// <summary>Saved, manually exported collector data. Never a live game-state provider.</summary>
public sealed record ManualUnitSnapshot(
    string Origin, string Subject, string RecordStatus, string? RecordReason,
    string AddonVersion, long Sequence, string? Name,
    ManualNumber X, ManualNumber Y, ManualNumber Z, ManualFlag Combat)
{
    public bool HasCompletePosition => X.Value.HasValue && Y.Value.HasValue && Z.Value.HasValue;
    public bool HasLiveSampleTime => false;
    public bool CanAutomate => false;
}

public sealed record ManualUnitReadResult(bool Success, string ErrorCode, ManualUnitSnapshot? Snapshot);

public static class ManualUnitSnapshotReader
{
    public static ManualUnitReadResult OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new(false, "file_unavailable", null);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.SequentialScan);
            long length = stream.Length;
            if (length > RacTsvParser.MaxWireBytes)
                return new(false, "wire_limit", null);
            byte[] raw = new byte[checked((int)length)];
            stream.ReadExactly(raw);
            if (stream.ReadByte() >= 0)
                return new(false, "wire_limit", null);
            return Parse(raw);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return new(false, "file_unavailable", null);
        }
    }

    public static ManualUnitReadResult Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > RacTsvParser.MaxWireBytes)
            return new(false, "wire_limit", null);
        UnitRecord record;
        try { record = RacTsvParser.Parse(bytes); }
        catch (RacTsvProtocolException) { return new(false, "invalid_tsv", null); }

        ManualNumber Numeric(string name)
        {
            if (!record.Fields.TryGetValue(name, out UnitField? field))
                return new("not_in_record", null, null);
            return new(field.Status.ToString().ToLowerInvariant(),
                field.Status == FieldStatus.Present ? field.NumberValue : null, field.Reason);
        }
        ManualFlag Flag(string name)
        {
            if (!record.Fields.TryGetValue(name, out UnitField? field))
                return new("not_in_record", null, null);
            return new(field.Status.ToString().ToLowerInvariant(),
                field.Status == FieldStatus.Present ? field.BooleanValue : null, field.Reason);
        }
        string? name = record.Fields.TryGetValue("name", out UnitField? nameField)
            && nameField.Status == FieldStatus.Present && nameField.RawBytes is { } rawName
            ? SafeName(rawName) : null;
        string addonVersion = Encoding.ASCII.GetString(record.AddonVersion);
        var snapshot = new ManualUnitSnapshot(
            "ManualSavedFile", record.Subject, record.Status, record.Reason,
            addonVersion, record.Sequence, name,
            Numeric("coordX"), Numeric("coordY"), Numeric("coordZ"), Flag("combat"));
        return new(true, "", snapshot);
    }

    private static string SafeName(byte[] raw)
    {
        try
        {
            string decoded = new UTF8Encoding(false, true).GetString(raw);
            return new string(decoded.Take(128).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        }
        catch (DecoderFallbackException)
        {
            return "[name contains non-UTF8 bytes]";
        }
    }
}
