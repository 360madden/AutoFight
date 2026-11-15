using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rift.Automation.Windows;

/// <summary>
/// Whether a single native XYZ acquisition may appear as a position observation in the trace.
/// An accepted value is only a validated coordinate candidate, not producer freshness or movement proof.
/// </summary>
public enum PhysicalRiftPositionTraceDisposition
{
    Accepted,
    Unavailable,
    Invalid,
    IdentityMismatch,
    ComparisonRejected,
    OutOfOrder,
    AcquisitionAgeRejected
}

/// <summary>
/// Immutable, bounded evidence entry. The source identity fields are diagnostic metadata;
/// rejected candidates never expose their raw XYZ or untrusted exception/reason strings.
/// </summary>
public sealed record PhysicalRiftPositionTraceEntry(
    int Sequence,
    DateTimeOffset? CapturedAtUtc,
    long MonotonicElapsedMilliseconds,
    PhysicalRiftPositionStatus NativeStatus,
    PhysicalRiftPositionChangeStatus ComparisonStatus,
    PhysicalRiftPositionTraceDisposition Disposition,
    string NativeReasonCode,
    string ComparisonReasonCode,
    bool AcceptedForComparison,
    int? ProcessId,
    long? ProcessStartUtcTicks,
    string? ExecutableSha256,
    ulong? PlayerUnitKey,
    int? ObservedProcessId,
    long? ObservedProcessStartUtcTicks,
    ulong? ObservedPlayerUnitKey,
    float? X,
    float? Y,
    float? Z,
    double? AbsoluteDeltaX,
    double? AbsoluteDeltaY,
    double? AbsoluteDeltaZ,
    double? Distance3D,
    double? NativeReadElapsedMilliseconds);

/// <summary>
/// Copy of a single-session passive native acquisition trace. This is historical evidence:
/// neither acquisition recency nor coordinate changes prove producer freshness or gameplay movement.
/// </summary>
public sealed record PhysicalRiftPositionTraceReport(
    string Schema,
    int MaxEntries,
    int TotalSamples,
    int RetainedSamples,
    int EvictedSamples,
    int CandidateSamples,
    int ChangedSamples,
    int UnavailableSamples,
    int RejectedSamples,
    int? ProcessId,
    long? ProcessStartUtcTicks,
    string? ExecutableSha256,
    ulong? PlayerUnitKey,
    IReadOnlyList<PhysicalRiftPositionTraceEntry> Samples)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public bool PassiveFreshnessQualified => false;
    public bool GameplayMovementProven => false;
    public bool CanAutomate => false;
    public string EvidenceNote =>
        "Native acquisitions and observed XYZ differences have no game producer clock; " +
        "they do not prove passive producer freshness, gameplay movement or input authority.";

    /// <summary>Serializes exactly one bounded, versioned, redacted JSON evidence object.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>
/// Thread-safe, pure-memory, single-session ring recorder for current-build physical native XYZ.
/// Caller owns all process sampling, comparisons and the Stopwatch used for monotonic elapsed time.
/// No background work, process access, filesystem writes or gameplay input occurs here.
/// </summary>
public sealed class PhysicalRiftPositionTraceRecorder
{
    public const int DefaultMaxEntries = 600;
    public const string EvidenceSchema = "autofight-physical-xyz-trace/1";

    private readonly object gate = new();
    private readonly Queue<PhysicalRiftPositionTraceEntry> entries;
    private readonly int maxEntries;

    private int totalSamples;
    private int evictedSamples;
    private int candidateSamples;
    private int changedSamples;
    private int unavailableSamples;
    private int rejectedSamples;
    private long? previousMonotonicMilliseconds;
    private (float X, float Y, float Z)? previousAcceptedPosition;
    private int? processId;
    private long? processStartUtcTicks;
    private string? executableSha256;
    private ulong? playerUnitKey;

    public PhysicalRiftPositionTraceRecorder(int maxEntries = DefaultMaxEntries)
    {
        if (maxEntries is < 1 or > DefaultMaxEntries)
            throw new ArgumentOutOfRangeException(nameof(maxEntries),
                "Native XYZ evidence retention must contain 1 through 600 entries.");
        this.maxEntries = maxEntries;
        entries = new Queue<PhysicalRiftPositionTraceEntry>(maxEntries);
    }

    /// <summary>
    /// Pins a verified selection before acquisition so unavailable-only evidence retains PID/start.
    /// This must occur before recording entries; Reset first to begin a different process session.
    /// The build and player key are pinned only by an accepted exact-build native baseline.
    /// </summary>
    public void BeginSession(int selectedProcessId, long selectedProcessStartUtcTicks)
    {
        if (selectedProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(selectedProcessId));
        if (selectedProcessStartUtcTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(selectedProcessStartUtcTicks));
        lock (gate)
        {
            if (totalSamples != 0 || processId is not null)
                throw new InvalidOperationException("Reset the trace before changing the pinned session.");
            processId = selectedProcessId;
            processStartUtcTicks = selectedProcessStartUtcTicks;
        }
    }

    /// <summary>
    /// Records each native acquisition outcome and matching tracker comparison. Rejected raw
    /// candidates retain their native/comparison statuses but never export raw position values.
    /// No caller-owned records or collections are retained.
    /// </summary>
    public void Record(
        PhysicalRiftPositionResult sample,
        PhysicalRiftPositionChangeSnapshot comparison,
        long monotonicElapsedMilliseconds,
        DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(comparison);

        lock (gate)
        {
            bool monotonicValid = monotonicElapsedMilliseconds >= 0 &&
                (previousMonotonicMilliseconds is null ||
                 monotonicElapsedMilliseconds > previousMonotonicMilliseconds);
            if (monotonicElapsedMilliseconds >= 0 &&
                (previousMonotonicMilliseconds is null ||
                 monotonicElapsedMilliseconds > previousMonotonicMilliseconds))
                previousMonotonicMilliseconds = monotonicElapsedMilliseconds;

            bool sourceMatchesSelection =
                (processId is null || sample.ProcessId == processId) &&
                (processStartUtcTicks is null || sample.ProcessStartUtcTicks == processStartUtcTicks);
            bool sourceMatchesEstablishedIdentity =
                (executableSha256 is null || sample.ExecutableSha256 is null ||
                 string.Equals(sample.ExecutableSha256, executableSha256, StringComparison.OrdinalIgnoreCase)) &&
                (playerUnitKey is null || sample.PlayerUnitKey is null ||
                 sample.PlayerUnitKey == playerUnitKey);
            bool nativeIdentityValid = sample.ProcessId > 0 &&
                sample.ProcessStartUtcTicks > 0 &&
                string.Equals(sample.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                sample.PlayerUnitKey is > 0;
            bool positionValid = sample.X is float x && sample.Y is float y && sample.Z is float z &&
                float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z);
            bool timingValid = sample.CapturedAtUtc != default &&
                double.IsFinite(sample.ElapsedMilliseconds) &&
                sample.ElapsedMilliseconds is >= 0 and <= 15_000;
            // Native capture age is verified independently of the comparison result.
            // Live callers use UtcNow; explicit UTC is for deterministic synthetic replay.
            TimeSpan captureAge = (observedAtUtc ?? DateTimeOffset.UtcNow) - sample.CapturedAtUtc;
            bool acquisitionFresh = captureAge >= TimeSpan.Zero &&
                captureAge < TimeSpan.FromSeconds(2.5);
            bool trackerIdentityValid = comparison.HasBaseline &&
                comparison.ProcessId == sample.ProcessId &&
                comparison.ProcessStartUtcTicks == sample.ProcessStartUtcTicks &&
                string.Equals(comparison.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                comparison.PlayerUnitKey == sample.PlayerUnitKey &&
                comparison.MonotonicElapsedMilliseconds == monotonicElapsedMilliseconds;

            bool acceptedStatus = comparison.Status is
                PhysicalRiftPositionChangeStatus.Baseline or
                PhysicalRiftPositionChangeStatus.Changed or
                PhysicalRiftPositionChangeStatus.Unchanged;
            bool accepted = sample.Status == PhysicalRiftPositionStatus.Candidate &&
                monotonicValid && sourceMatchesSelection && sourceMatchesEstablishedIdentity &&
                nativeIdentityValid && positionValid && timingValid && acquisitionFresh &&
                trackerIdentityValid && acceptedStatus &&
                ValidComparisonDelta(comparison, sample, previousAcceptedPosition);

            PhysicalRiftPositionTraceDisposition disposition;
            if (accepted)
            {
                processId ??= sample.ProcessId;
                processStartUtcTicks ??= sample.ProcessStartUtcTicks;
                executableSha256 ??= PhysicalRiftPositionReader.GameSha256;
                playerUnitKey ??= sample.PlayerUnitKey;
                previousAcceptedPosition = (sample.X!.Value, sample.Y!.Value, sample.Z!.Value);
                disposition = PhysicalRiftPositionTraceDisposition.Accepted;
                candidateSamples = SaturatingIncrement(candidateSamples);
                if (comparison.Status == PhysicalRiftPositionChangeStatus.Changed)
                    changedSamples = SaturatingIncrement(changedSamples);
            }
            else
            {
                previousAcceptedPosition = null;
                disposition =
                    !monotonicValid ? PhysicalRiftPositionTraceDisposition.OutOfOrder :
                    !sourceMatchesSelection || !sourceMatchesEstablishedIdentity ||
                    sample.Status == PhysicalRiftPositionStatus.IdentityMismatch ||
                    comparison.Status == PhysicalRiftPositionChangeStatus.IdentityChanged
                        ? PhysicalRiftPositionTraceDisposition.IdentityMismatch :
                    sample.Status is PhysicalRiftPositionStatus.Unavailable or PhysicalRiftPositionStatus.Error ||
                    comparison.Status == PhysicalRiftPositionChangeStatus.Unavailable
                        ? PhysicalRiftPositionTraceDisposition.Unavailable :
                    !acquisitionFresh && sample.Status == PhysicalRiftPositionStatus.Candidate &&
                    nativeIdentityValid && positionValid && timingValid
                        ? PhysicalRiftPositionTraceDisposition.AcquisitionAgeRejected :
                    sample.Status == PhysicalRiftPositionStatus.Candidate && nativeIdentityValid &&
                    positionValid && timingValid
                        ? PhysicalRiftPositionTraceDisposition.ComparisonRejected
                        : PhysicalRiftPositionTraceDisposition.Invalid;
                rejectedSamples = SaturatingIncrement(rejectedSamples);
                if (disposition == PhysicalRiftPositionTraceDisposition.Unavailable)
                    unavailableSamples = SaturatingIncrement(unavailableSamples);
            }

            // Only a known, verified build fingerprint is copied. Arbitrary error text, hashes,
            // private process image paths and credentials never enter the evidence object.
            var entry = new PhysicalRiftPositionTraceEntry(
                NextSequence(),
                sample.CapturedAtUtc == default ? null : sample.CapturedAtUtc.ToUniversalTime(),
                monotonicElapsedMilliseconds,
                sample.Status,
                comparison.Status,
                disposition,
                NativeReasonCode(sample.Status),
                KnownComparisonReasonCode(comparison.Reason),
                accepted,
                processId,
                processStartUtcTicks,
                executableSha256,
                playerUnitKey,
                sample.ProcessId > 0 ? sample.ProcessId : null,
                sample.ProcessStartUtcTicks > 0 ? sample.ProcessStartUtcTicks : null,
                sample.PlayerUnitKey is > 0 ? sample.PlayerUnitKey : null,
                accepted ? sample.X : null,
                accepted ? sample.Y : null,
                accepted ? sample.Z : null,
                accepted ? comparison.AbsoluteDeltaX : null,
                accepted ? comparison.AbsoluteDeltaY : null,
                accepted ? comparison.AbsoluteDeltaZ : null,
                accepted ? comparison.Distance3D : null,
                double.IsFinite(sample.ElapsedMilliseconds) &&
                sample.ElapsedMilliseconds is >= 0 and <= 15_000
                    ? sample.ElapsedMilliseconds : null);
            Append(entry);
        }
    }

    /// <summary>
    /// Records a timeout, cancellation or other no-result acquisition. The reason argument
    /// is deliberately not retained because it may contain OS paths, credentials or exceptions.
    /// </summary>
    public void RecordUnavailable(
        string reason,
        long monotonicElapsedMilliseconds,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(reason);
        lock (gate)
        {
            bool monotonicValid = monotonicElapsedMilliseconds >= 0 &&
                (previousMonotonicMilliseconds is null ||
                 monotonicElapsedMilliseconds > previousMonotonicMilliseconds);
            if (monotonicValid)
                previousMonotonicMilliseconds = monotonicElapsedMilliseconds;
            previousAcceptedPosition = null;
            unavailableSamples = SaturatingIncrement(unavailableSamples);
            rejectedSamples = SaturatingIncrement(rejectedSamples);

            Append(new PhysicalRiftPositionTraceEntry(
                NextSequence(),
                capturedAtUtc == default ? null : capturedAtUtc.ToUniversalTime(),
                monotonicElapsedMilliseconds,
                PhysicalRiftPositionStatus.Unavailable,
                PhysicalRiftPositionChangeStatus.Unavailable,
                monotonicValid ? PhysicalRiftPositionTraceDisposition.Unavailable :
                    PhysicalRiftPositionTraceDisposition.OutOfOrder,
                "native_acquisition_not_returned",
                "no_comparison_result",
                false,
                processId, processStartUtcTicks, executableSha256, playerUnitKey,
                null, null, null,
                null, null, null, null, null, null, null, null));
        }
    }

    /// <summary>
    /// Returns a detached read-only array. Subsequent Record/Reset calls cannot mutate this report.
    /// </summary>
    public PhysicalRiftPositionTraceReport Snapshot()
    {
        lock (gate)
        {
            var copy = entries.ToArray();
            ReadOnlyCollection<PhysicalRiftPositionTraceEntry> immutable = Array.AsReadOnly(copy);
            return new PhysicalRiftPositionTraceReport(
                EvidenceSchema, maxEntries, totalSamples, copy.Length, evictedSamples,
                candidateSamples, changedSamples, unavailableSamples, rejectedSamples,
                processId, processStartUtcTicks, executableSha256, playerUnitKey, immutable);
        }
    }

    /// <summary>Explicitly drops prior evidence and all source identities for a new session.</summary>
    public void Reset()
    {
        lock (gate)
        {
            entries.Clear();
            totalSamples = evictedSamples = candidateSamples =
                changedSamples = unavailableSamples = rejectedSamples = 0;
            previousMonotonicMilliseconds = null;
            previousAcceptedPosition = null;
            processId = null;
            processStartUtcTicks = null;
            executableSha256 = null;
            playerUnitKey = null;
        }
    }

    private int NextSequence() => totalSamples == int.MaxValue
        ? int.MaxValue : totalSamples + 1;

    private void Append(PhysicalRiftPositionTraceEntry entry)
    {
        totalSamples = SaturatingIncrement(totalSamples);
        if (entries.Count == maxEntries)
        {
            entries.Dequeue();
            evictedSamples = SaturatingIncrement(evictedSamples);
        }
        entries.Enqueue(entry);
    }

    private static int SaturatingIncrement(int value) =>
        value == int.MaxValue ? value : value + 1;

    private static bool ValidComparisonDelta(
        PhysicalRiftPositionChangeSnapshot comparison,
        PhysicalRiftPositionResult sample,
        (float X, float Y, float Z)? previous)
    {
        if (comparison.Status == PhysicalRiftPositionChangeStatus.Baseline)
            return comparison.AbsoluteDeltaX is null && comparison.AbsoluteDeltaY is null &&
                comparison.AbsoluteDeltaZ is null && comparison.Distance3D is null;

        if (previous is null)
            return false;
        double dx = Math.Abs((double)sample.X!.Value - previous.Value.X);
        double dy = Math.Abs((double)sample.Y!.Value - previous.Value.Y);
        double dz = Math.Abs((double)sample.Z!.Value - previous.Value.Z);
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(distance) ||
            !Near(comparison.AbsoluteDeltaX, dx) ||
            !Near(comparison.AbsoluteDeltaY, dy) ||
            !Near(comparison.AbsoluteDeltaZ, dz) ||
            !Near(comparison.Distance3D, distance))
            return false;

        return comparison.Status == PhysicalRiftPositionChangeStatus.Unchanged
            ? dx == 0 && dy == 0 && dz == 0
            : dx != 0 || dy != 0 || dz != 0;
    }

    private static bool Near(double? reported, double expected) =>
        reported is double actual && double.IsFinite(actual) &&
        Math.Abs(actual - expected) <= Math.Max(1e-8, expected * 1e-9);

    private static string NativeReasonCode(PhysicalRiftPositionStatus status) => status switch
    {
        PhysicalRiftPositionStatus.Candidate => "native_candidate",
        PhysicalRiftPositionStatus.IdentityMismatch => "native_identity_mismatch",
        PhysicalRiftPositionStatus.Unavailable => "native_unavailable",
        _ => "native_error"
    };

    // Strict allowlist prevents incidental native exception text, paths, credentials
    // or arbitrary strings from reaching copied evidence/JSON.
    private static string KnownComparisonReasonCode(string? reason) => reason switch
    {
        "first_exact_identity_native_xyz_acquisition" or
        "new_acquisition_equal_xyz_no_producer_freshness_claim" or
        "observed_xyz_value_change_not_proven_gameplay_movement" or
        "no_acquisition" or "reset_no_baseline" or "cancelled_reset_required" or
        "bounded_observation_limit_reset_required" or
        "negative_monotonic_acquisition_elapsed" or
        "native_reader_process_or_build_identity_mismatch" or
        "source_status_unavailable" or "source_status_identitymismatch" or
        "source_status_error" or
        "native_candidate_missing_invalid_or_unpinned" or
        "native_player_or_process_identity_changed" or
        "nonadvancing_monotonic_acquisition_elapsed" or
        "native_acquisition_gap_exceeded_limit_not_game_producer_staleness" or
        "native_xyz_difference_nonfinite" => reason,
        _ => "comparison_reason_redacted"
    };
}
