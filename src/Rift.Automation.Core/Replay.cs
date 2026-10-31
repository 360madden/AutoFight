using System.IO;
using System.Text;
using System.Text.Json;

namespace Rift.Automation.Core;

public abstract record ReplayStep(long AtMs);
public sealed record ResetStep(long AtMs, SessionIdentity Session, long Epoch) : ReplayStep(AtMs);
public sealed record SampleStep(long AtMs, PositionObservation Observation) : ReplayStep(AtMs);
public sealed record GapStep(long AtMs, string Reason) : ReplayStep(AtMs);
public sealed record TickStep(long AtMs) : ReplayStep(AtMs);

public sealed record ReplayScenario
{
    public string Name { get; }
    public IReadOnlyList<ReplayStep> Steps { get; }

    public ReplayScenario(string name, IReadOnlyList<ReplayStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (!ReplayValidation.Text(name, 128) || steps.Count is < 1 or > 256)
            throw new ArgumentException("Invalid bounded replay scenario.");
        Name = name;
        ReplayStep[] copy = steps.Take(257).ToArray();
        if (copy.Length is < 1 or > 256)
            throw new ArgumentException("Invalid bounded replay step count.", nameof(steps));
        Steps = Array.AsReadOnly(copy);
    }
}

public sealed record ReplayReadResult(bool Success, ReplayScenario? Scenario, string ErrorCode);

internal static class ReplayValidation
{
    internal static bool Text(string? text, int maximum) =>
        text is { Length: > 0 } && text.Length <= maximum &&
        !text.Any(char.IsControl) && !string.IsNullOrWhiteSpace(text);

    internal static bool Identity(SessionIdentity? identity) =>
        identity is not null && Text(identity.SessionId, 128) && Text(identity.BuildId, 128);

    internal static bool Valid(ReplayScenario? scenario)
    {
        if (scenario is null || !Text(scenario.Name, 128) ||
            scenario.Steps.Count is < 1 or > 256 || scenario.Steps[0] is not ResetStep)
            return false;

        long priorAt = 0;
        long priorEpoch = 0;
        foreach (ReplayStep? step in scenario.Steps)
        {
            if (step is null || step.AtMs < priorAt || step.AtMs < 0)
                return false;
            priorAt = step.AtMs;
            switch (step)
            {
                case ResetStep reset:
                    if (!Identity(reset.Session) || reset.Epoch <= priorEpoch)
                        return false;
                    priorEpoch = reset.Epoch;
                    break;
                case SampleStep sample:
                    PositionObservation o = sample.Observation;
                    if (o is null || !Identity(o.Session) || o.Epoch <= 0 || o.Sequence <= 0 ||
                        !Enum.IsDefined(o.Status) || !Text(o.SourceId, 128) ||
                        (o.Reason is not null && !Text(o.Reason, 256)) ||
                        o.AcquiredStartMs < 0 || o.AcquiredEndMs < o.AcquiredStartMs ||
                        o.ReceivedMs < o.AcquiredEndMs || o.ReceivedMs > sample.AtMs)
                        return false;
                    if (o.Status == ObservationStatus.Present)
                    {
                        if (o.Value is not { } p || !double.IsFinite(p.X) ||
                            !double.IsFinite(p.Y) || !double.IsFinite(p.Z))
                            return false;
                    }
                    else if (o.Value is not null)
                        return false;
                    break;
                case GapStep gap:
                    if (!Text(gap.Reason, 256))
                        return false;
                    break;
                case TickStep:
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
}

public static class ReplayReader
{
    private const int MaximumBytes = 65536;

    public static ReplayReadResult Read(Stream stream)
    {
        if (stream is null)
            return Failure("invalid_stream");
        try
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            while (true)
            {
                int remaining = MaximumBytes + 1 - (int)buffer.Length;
                int count = stream.Read(chunk, 0, Math.Min(chunk.Length, remaining));
                if (count == 0)
                    break;
                buffer.Write(chunk, 0, count);
                if (buffer.Length > MaximumBytes)
                    return Failure("too_large");
            }

            byte[] bytes = buffer.ToArray();
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Failure("invalid_utf8");
            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Failure("invalid_utf8");
            }

            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            JsonElement root = document.RootElement;
            RejectDuplicateKeys(root);
            Fields(root, "schema", "origin", "name", "steps");
            if (String(root, "schema") != "rift-replay/1" || String(root, "origin") != "synthetic")
                return Failure("invalid_schema");
            JsonElement stepsJson = root.GetProperty("steps");
            if (stepsJson.ValueKind != JsonValueKind.Array || stepsJson.GetArrayLength() is < 1 or > 256)
                return Failure("invalid_scenario");

            var steps = new List<ReplayStep>(stepsJson.GetArrayLength());
            foreach (JsonElement step in stepsJson.EnumerateArray())
                steps.Add(ParseStep(step));
            var scenario = new ReplayScenario(String(root, "name"), steps);
            return ReplayValidation.Valid(scenario)
                ? new ReplayReadResult(true, scenario, "")
                : Failure("invalid_scenario");
        }
        catch (JsonException) { return Failure("invalid_json"); }
        catch (InvalidDataException) { return Failure("invalid_scenario"); }
        catch (KeyNotFoundException) { return Failure("invalid_scenario"); }
        catch (ObjectDisposedException) { return Failure("invalid_stream"); }
        catch (InvalidOperationException) { return Failure("invalid_scenario"); }
        catch (FormatException) { return Failure("invalid_scenario"); }
        catch (OverflowException) { return Failure("invalid_scenario"); }
        catch (IOException) { return Failure("invalid_stream"); }
        catch (NotSupportedException) { return Failure("invalid_stream"); }
        catch (ArgumentException) { return Failure("invalid_scenario"); }
    }

    private static ReplayReadResult Failure(string code) => new(false, null, code);

    private static ReplayStep ParseStep(JsonElement step)
    {
        string kind = String(step, "kind");
        long at = Integer(step, "atMs");
        switch (kind)
        {
            case "reset":
                Fields(step, "kind", "atMs", "sessionId", "buildId", "epoch");
                return new ResetStep(at, Identity(step), Integer(step, "epoch"));
            case "tick":
                Fields(step, "kind", "atMs");
                return new TickStep(at);
            case "gap":
                Fields(step, "kind", "atMs", "reason");
                return new GapStep(at, String(step, "reason"));
            case "sample":
                Fields(step, new[]
                {
                    "kind", "atMs", "sessionId", "buildId", "epoch", "sequence",
                    "status", "value", "acquiredStartMs", "acquiredEndMs", "receivedMs", "sourceId"
                }, "reason");
                ObservationStatus status = String(step, "status") switch
                {
                    "present" => ObservationStatus.Present,
                    "missing" => ObservationStatus.Missing,
                    "unsupported" => ObservationStatus.Unsupported,
                    "invalid" => ObservationStatus.Invalid,
                    "error" => ObservationStatus.Error,
                    _ => throw new InvalidDataException()
                };
                JsonElement valueJson = step.GetProperty("value");
                PlayerPosition? value = null;
                if (valueJson.ValueKind != JsonValueKind.Null)
                {
                    Fields(valueJson, "x", "y", "z");
                    value = new PlayerPosition(Number(valueJson, "x"), Number(valueJson, "y"), Number(valueJson, "z"));
                }
                string? reason = step.TryGetProperty("reason", out var reasonJson)
                    ? reasonJson.ValueKind == JsonValueKind.String
                        ? reasonJson.GetString()
                        : throw new InvalidDataException()
                    : null;
                return new SampleStep(at, new PositionObservation(
                    Identity(step), Integer(step, "epoch"), Integer(step, "sequence"),
                    status, value, Integer(step, "acquiredStartMs"), Integer(step, "acquiredEndMs"),
                    Integer(step, "receivedMs"), String(step, "sourceId"), reason));
            default:
                throw new InvalidDataException();
        }
    }

    private static SessionIdentity Identity(JsonElement value) =>
        new(String(value, "sessionId"), String(value, "buildId"));

    private static string String(JsonElement value, string name)
    {
        JsonElement item = value.GetProperty(name);
        if (item.ValueKind != JsonValueKind.String)
            throw new InvalidDataException();
        return item.GetString() ?? throw new InvalidDataException();
    }

    private static long Integer(JsonElement value, string name)
    {
        JsonElement item = value.GetProperty(name);
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out long result))
            throw new InvalidDataException();
        return result;
    }

    private static double Number(JsonElement value, string name)
    {
        JsonElement item = value.GetProperty(name);
        if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double result) || !double.IsFinite(result))
            throw new InvalidDataException();
        return result;
    }

    private static void Fields(JsonElement value, params string[] required) => Fields(value, required, null);

    private static void Fields(JsonElement value, string[] required, string? optional)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!names.Add(property.Name) ||
                (!required.Contains(property.Name, StringComparer.Ordinal) && property.Name != optional))
                throw new InvalidDataException();
        }
        if (required.Any(name => !names.Contains(name)))
            throw new InvalidDataException();
    }

    private static void RejectDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException();
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
                RejectDuplicateKeys(item);
        }
    }
}

public sealed class ReplayPlayer
{
    private readonly ReplayScenario _scenario;
    private ObservationEngine _engine = new();
    public int Index { get; private set; }
    public int Count => _scenario.Steps.Count;
    public long NowMs { get; private set; }
    public bool CanStep => Index < Count;
    public long MaxAcquisitionAgeMs => _engine.MaxAcquisitionAgeMs;
    public long MaxReceiveAgeMs => _engine.MaxReceiveAgeMs;
    public PositionView View => _engine.Project(NowMs);
    public string LastResultCode { get; private set; } = "ready";

    public ReplayPlayer(ReplayScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        _scenario = new ReplayScenario(scenario.Name, scenario.Steps);
        if (!ReplayValidation.Valid(_scenario))
            throw new ArgumentException("Invalid synthetic replay scenario.", nameof(scenario));
    }

    public bool Step()
    {
        if (!CanStep)
            return false;
        ReplayStep step = _scenario.Steps[Index];
        NowMs = step.AtMs;
        switch (step)
        {
            case ResetStep reset:
                _engine.Reset(reset.Session, reset.Epoch, NowMs);
                LastResultCode = "reset";
                break;
            case SampleStep sample:
                LastResultCode = _engine.Apply(sample.Observation, NowMs).Code;
                break;
            case GapStep gap:
                _engine.MarkGap(NowMs, gap.Reason);
                LastResultCode = "gap";
                break;
            case TickStep:
                LastResultCode = "tick";
                break;
        }
        Index++;
        return true;
    }

    public void Reset()
    {
        _engine = new ObservationEngine();
        Index = 0;
        NowMs = 0;
        LastResultCode = "ready";
    }
}
