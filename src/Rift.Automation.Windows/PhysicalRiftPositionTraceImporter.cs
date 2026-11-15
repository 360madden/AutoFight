using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rift.Automation.Windows;

/// <summary>
/// Strict, read-only recovery of an M4 native XYZ trace produced by AutoFight's
/// own JSON writer. Imported evidence stays historical and never authorizes input.
/// Only the exact canonical UTF-8 format is accepted: duplicates, missing/unknown
/// fields, forged authority flags, BOM, or reformatted copies cannot silently pass.
/// </summary>
public static class PhysicalRiftPositionTraceImporter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    public static PhysicalRiftPositionTraceReport Load(string selectedPath)
    {
        string path = ResolveOrdinaryJsonInput(selectedPath);

        // Never read an unbounded file into memory. Hold a single file handle,
        // enforce the producer's 1 MiB limit, and detect length changes on read.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 8192, options: FileOptions.SequentialScan);
        long length = input.Length;
        if (length is < 1 or > PhysicalRiftPositionTraceExporter.MaximumUtf8JsonBytes)
            throw new InvalidDataException("The physical XYZ trace file size is outside the bounded format.");

        byte[] sourceBytes = new byte[(int)length];
        try
        {
            input.ReadExactly(sourceBytes);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("The physical XYZ trace changed while being read.");
        }
        if (input.ReadByte() != -1)
            throw new InvalidDataException("The physical XYZ trace changed while being read.");

        PhysicalRiftPositionTraceReport report;
        try
        {
            string json = StrictUtf8.GetString(sourceBytes);
            report = JsonSerializer.Deserialize<PhysicalRiftPositionTraceReport>(json, ReadOptions)
                ?? throw new InvalidDataException("The physical XYZ trace has no report object.");

            // JsonSerializer does not populate the report's computed denial flags.
            // A canonical byte match verifies the exact full output of the real
            // serializer, including every false authority flag and evidence note.
            // It also rejects duplicates, missing/unknown fields and noncanonical
            // enum strings without a second schema implementation.
            byte[] canonical = StrictUtf8.GetBytes(report.ToJson());
            if (!sourceBytes.AsSpan().SequenceEqual(canonical))
                throw new InvalidDataException("The physical XYZ trace is not an exact AutoFight export.");
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("The physical XYZ trace contains invalid UTF-8.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The physical XYZ trace contains invalid JSON data.");
        }
        catch (NotSupportedException)
        {
            throw new InvalidDataException("The physical XYZ trace JSON type is unsupported.");
        }

        // Reuse M4's same semantic/redaction validation; M5 then independently
        // classifies interrupted, source-changed or truncated historical traces.
        PhysicalRiftPositionTraceExporter.ValidateReport(report);
        return report;
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            MaxDepth = 16
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
        return options;
    }

    private static string ResolveOrdinaryJsonInput(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || selectedPath.Length > 32_000)
            throw new ArgumentException("An ordinary absolute JSON filename is required.", nameof(selectedPath));

        string slashNormalized = selectedPath.Replace('/', '\\');
        if (slashNormalized.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            slashNormalized.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            slashNormalized.StartsWith(@"\??\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(selectedPath))
            throw new ArgumentException("Only ordinary absolute local JSON file paths are supported.", nameof(selectedPath));

        string path = Path.GetFullPath(selectedPath);
        string canonical = path.Replace('/', '\\');
        string root = Path.GetPathRoot(path) ?? string.Empty;
        // Read-only replay is intentionally local, not UNC shares or Windows devices.
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) ||
            root[1] != ':' || root[2] != '\\' ||
            canonical.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            canonical.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("Only ordinary local drive JSON paths are supported.", nameof(selectedPath));

        for (int index = 0; index < canonical.Length; index++)
        {
            if (canonical[index] == ':' && index != 1)
                throw new ArgumentException("Alternate stream paths are not supported.", nameof(selectedPath));
        }

        string fileName = Path.GetFileName(path);
        string stem = fileName.Split('.')[0].TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(' ') || fileName.EndsWith('.') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            IsReservedDeviceFilename(stem))
            throw new ArgumentException("Choose a normal .json filename.", nameof(selectedPath));
        return path;
    }

    private static bool IsReservedDeviceFilename(string stem)
    {
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
            return true;
        return stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            stem[3] is >= '1' and <= '9' or '¹' or '²' or '³';
    }
}
