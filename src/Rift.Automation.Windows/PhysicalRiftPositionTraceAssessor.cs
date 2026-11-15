namespace Rift.Automation.Windows;

/// <summary>
/// Historical classification of a bounded, read-only native XYZ trace.
/// Two distinct coordinate plateaus are observed values, NOT a claim of
/// passive game-producer freshness, caused gameplay movement or automation authority.
/// </summary>
public enum PhysicalRiftPositionTraceAssessmentState
{
    InsufficientEvidence,
    OneCoordinatePlateau,
    AwaitingFinalPlateau,
    TwoCoordinatePlateausObserved,
    Interrupted,
    SourceChanged,
    Truncated
}

/// <summary>
/// Detached, diagnostic assessment of native coordinate-value sequences.
/// "Before" is the last sample of the selected earlier stable run; "After" is
/// the final retained sample of the newer run, when that run exists.
/// </summary>
public sealed record PhysicalRiftPositionTraceAssessment(
    PhysicalRiftPositionTraceAssessmentState State,
    string ReasonCode,
    int AcceptedSamples,
    int RejectedSamples,
    float? BeforeX,
    float? BeforeY,
    float? BeforeZ,
    DateTimeOffset? BeforeCapturedAtUtc,
    long? BeforeMonotonicElapsedMilliseconds,
    float? AfterX,
    float? AfterY,
    float? AfterZ,
    DateTimeOffset? AfterCapturedAtUtc,
    long? AfterMonotonicElapsedMilliseconds,
    int BaselineStableRunCount,
    int FinalStableRunCount,
    double? Distance3D)
{
    public bool PassiveFreshnessQualified => false;
    public bool GameplayMovementProven => false;
    public bool CanAutomate => false;
}

/// <summary>
/// Pure memory evaluator for a completed or currently accumulating physical native XYZ
/// trace. No clocks, files, processes, UI or gameplay interfaces are accessed.
/// A complete A/B sequence requires ≥3 consecutive equal XYZ at A, ≥1 Changed
/// acquisition afterward, then ≥3 consecutive equal XYZ at distinct B, all under
/// the same exact identity and acquisition gaps STRICTLY below 4,500ms.
/// </summary>
public static class PhysicalRiftPositionTraceAssessor
{
    public const int MinimumStableRunSamples = 3;
    public const long MaximumExclusiveAcquisitionGapMilliseconds = 4500;

    private sealed record PositionRun(int Start, int End, float X, float Y, float Z)
    {
        internal int Count => End - Start + 1;

        internal bool Matches(PositionRun other) =>
            X == other.X && Y == other.Y && Z == other.Z;
    }

    private sealed record PlateauDescription(
        PhysicalRiftPositionTraceAssessmentState State,
        string ReasonCode,
        PhysicalRiftPositionTraceEntry? Before,
        PhysicalRiftPositionTraceEntry? After,
        int BeforeRunCount,
        int FinalRunCount);

    public static PhysicalRiftPositionTraceAssessment Assess(
        PhysicalRiftPositionTraceReport? report)
    {
        if (report is null)
            return Create(PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence,
                "no_trace", null, null);

        if (report.EvictedSamples > 0)
            return Create(PhysicalRiftPositionTraceAssessmentState.Truncated,
                "trace_fifo_evictions", report, null);

        IReadOnlyList<PhysicalRiftPositionTraceEntry>? entries = report.Samples;
        if (entries is null || entries.Count == 0)
            return Create(PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence,
                "no_accepted_xyz", report, null);

        if (entries.Count > PhysicalRiftPositionTraceRecorder.DefaultMaxEntries ||
            report.MaxEntries is < 1 or > PhysicalRiftPositionTraceRecorder.DefaultMaxEntries ||
            entries.Count > report.MaxEntries ||
            report.TotalSamples < entries.Count ||
            report.RetainedSamples != entries.Count ||
            report.TotalSamples != report.RetainedSamples ||
            report.EvictedSamples != 0)
            return Create(PhysicalRiftPositionTraceAssessmentState.Truncated,
                "trace_summary_incomplete", report, null);

        // A gap anywhere in the recording means an unknown acquisition was omitted.
        // Do not reconstruct a falsely contiguous trace from its remaining subset.
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is null ||
                entries[i].Sequence != i + 1)
                return Create(PhysicalRiftPositionTraceAssessmentState.Truncated,
                    "noncontiguous_trace_sequence", report, null);
        }

        if (report.Schema != PhysicalRiftPositionTraceRecorder.EvidenceSchema ||
            report.ProcessId is null or <= 0 ||
            report.ProcessStartUtcTicks is null or <= 0 ||
            report.CandidateSamples < 0 || report.RejectedSamples < 0 ||
            report.ChangedSamples < 0 || report.UnavailableSamples < 0 ||
            (long)report.CandidateSamples + report.RejectedSamples != report.TotalSamples ||
            !SummaryCountsMatchEntries(report, entries))
            return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                "invalid_trace_summary", report, null);

        var validPrefix = new List<PhysicalRiftPositionTraceEntry>(entries.Count);
        PhysicalRiftPositionTraceEntry? previous = null;
        foreach (PhysicalRiftPositionTraceEntry entry in entries)
        {
            if (SourceChanged(report, entry))
            {
                PlateauDescription prefix = DescribePlateaus(validPrefix);
                return Create(PhysicalRiftPositionTraceAssessmentState.SourceChanged,
                    prefix.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved
                        ? "pinned_source_identity_changed_after_observed_plateaus"
                        : "pinned_source_identity_changed",
                    report, prefix);
            }

            if (!entry.AcceptedForComparison ||
                entry.Disposition != PhysicalRiftPositionTraceDisposition.Accepted ||
                entry.NativeStatus != PhysicalRiftPositionStatus.Candidate)
            {
                PlateauDescription prefix = DescribePlateaus(validPrefix);
                return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                    prefix.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved
                        ? "native_acquisition_interrupted_after_observed_plateaus"
                        : "native_acquisition_interrupted",
                    report, prefix);
            }

            if (entry.X is not float x || !float.IsFinite(x) ||
                entry.Y is not float y || !float.IsFinite(y) ||
                entry.Z is not float z || !float.IsFinite(z) ||
                entry.CapturedAtUtc is not { } utc || utc.Offset != TimeSpan.Zero ||
                entry.MonotonicElapsedMilliseconds < 0)
                return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                    "invalid_accepted_coordinate_or_time", report,
                    DescribePlateaus(validPrefix));

            if (previous is not null)
            {
                long gap = entry.MonotonicElapsedMilliseconds -
                    previous.MonotonicElapsedMilliseconds;
                if (gap <= 0)
                    return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                        "nonadvancing_monotonic_time", report,
                        DescribePlateaus(validPrefix));
                if (gap >= MaximumExclusiveAcquisitionGapMilliseconds)
                    return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                        "acquisition_gap_at_or_above_4500ms", report,
                        DescribePlateaus(validPrefix));
                if (!MatchesCoordinateComparison(previous, entry))
                    return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                        "invalid_coordinate_comparison", report,
                        DescribePlateaus(validPrefix));
            }
            else if (entry.ComparisonStatus != PhysicalRiftPositionChangeStatus.Baseline ||
                entry.AbsoluteDeltaX is not null || entry.AbsoluteDeltaY is not null ||
                entry.AbsoluteDeltaZ is not null || entry.Distance3D is not null)
            {
                return Create(PhysicalRiftPositionTraceAssessmentState.Interrupted,
                    "invalid_initial_baseline", report, null);
            }

            validPrefix.Add(entry);
            previous = entry;
        }

        PlateauDescription all = DescribePlateaus(validPrefix);
        return Create(all.State, all.ReasonCode, report, all);
    }

    private static bool SummaryCountsMatchEntries(
        PhysicalRiftPositionTraceReport report,
        IReadOnlyList<PhysicalRiftPositionTraceEntry> entries)
    {
        int accepted = 0, rejected = 0, changed = 0, unavailable = 0;
        foreach (PhysicalRiftPositionTraceEntry entry in entries)
        {
            if (entry.AcceptedForComparison)
            {
                accepted++;
                if (entry.ComparisonStatus == PhysicalRiftPositionChangeStatus.Changed)
                    changed++;
            }
            else
            {
                rejected++;
                if (entry.Disposition == PhysicalRiftPositionTraceDisposition.Unavailable ||
                    entry.NativeReasonCode == "native_acquisition_not_returned")
                    unavailable++;
            }
        }
        return accepted == report.CandidateSamples &&
            rejected == report.RejectedSamples &&
            changed == report.ChangedSamples &&
            unavailable == report.UnavailableSamples;
    }

    private static bool SourceChanged(
        PhysicalRiftPositionTraceReport report,
        PhysicalRiftPositionTraceEntry entry)
    {
        if (entry.Disposition == PhysicalRiftPositionTraceDisposition.IdentityMismatch ||
            entry.NativeStatus == PhysicalRiftPositionStatus.IdentityMismatch ||
            entry.ComparisonStatus == PhysicalRiftPositionChangeStatus.IdentityChanged ||
            entry.ProcessId is not null && entry.ProcessId != report.ProcessId ||
            entry.ProcessStartUtcTicks is not null &&
                entry.ProcessStartUtcTicks != report.ProcessStartUtcTicks ||
            entry.ExecutableSha256 is not null &&
                !string.Equals(entry.ExecutableSha256, report.ExecutableSha256,
                    StringComparison.OrdinalIgnoreCase) ||
            entry.PlayerUnitKey is not null && entry.PlayerUnitKey != report.PlayerUnitKey ||
            entry.ObservedProcessId is not null && entry.ObservedProcessId != report.ProcessId ||
            entry.ObservedProcessStartUtcTicks is not null &&
                entry.ObservedProcessStartUtcTicks != report.ProcessStartUtcTicks ||
            entry.ObservedPlayerUnitKey is not null &&
                entry.ObservedPlayerUnitKey != report.PlayerUnitKey)
            return true;

        // An accepted sample must carry a complete, exact, trusted current-build
        // identity. Unavailable-only records may legitimately lack build/player.
        return entry.AcceptedForComparison &&
            (entry.ProcessId != report.ProcessId ||
             entry.ProcessStartUtcTicks != report.ProcessStartUtcTicks ||
             !string.Equals(report.ExecutableSha256,
                 PhysicalRiftPositionReader.GameSha256, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(entry.ExecutableSha256, report.ExecutableSha256,
                 StringComparison.OrdinalIgnoreCase) ||
             report.PlayerUnitKey is null or 0 ||
             entry.PlayerUnitKey != report.PlayerUnitKey ||
             entry.ObservedProcessId != report.ProcessId ||
             entry.ObservedProcessStartUtcTicks != report.ProcessStartUtcTicks ||
             entry.ObservedPlayerUnitKey != report.PlayerUnitKey);
    }

    private static bool MatchesCoordinateComparison(
        PhysicalRiftPositionTraceEntry previous, PhysicalRiftPositionTraceEntry current)
    {
        double dx = Math.Abs((double)current.X!.Value - previous.X!.Value);
        double dy = Math.Abs((double)current.Y!.Value - previous.Y!.Value);
        double dz = Math.Abs((double)current.Z!.Value - previous.Z!.Value);
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(distance) ||
            !Near(current.AbsoluteDeltaX, dx) ||
            !Near(current.AbsoluteDeltaY, dy) ||
            !Near(current.AbsoluteDeltaZ, dz) ||
            !Near(current.Distance3D, distance))
            return false;
        bool equal = dx == 0 && dy == 0 && dz == 0;
        return equal
            ? current.ComparisonStatus == PhysicalRiftPositionChangeStatus.Unchanged
            : current.ComparisonStatus == PhysicalRiftPositionChangeStatus.Changed;
    }

    private static bool Near(double? measured, double expected) =>
        measured is double value && double.IsFinite(value) &&
        Math.Abs(value - expected) <= Math.Max(1e-8, expected * 1e-9);

    private static PlateauDescription DescribePlateaus(
        IReadOnlyList<PhysicalRiftPositionTraceEntry> samples)
    {
        if (samples.Count == 0)
            return new(PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence,
                "no_accepted_xyz", null, null, 0, 0);

        var runs = new List<PositionRun>();
        for (int start = 0; start < samples.Count;)
        {
            int end = start;
            float x = samples[start].X!.Value;
            float y = samples[start].Y!.Value;
            float z = samples[start].Z!.Value;
            while (end + 1 < samples.Count &&
                samples[end + 1].X == x &&
                samples[end + 1].Y == y &&
                samples[end + 1].Z == z)
                end++;
            runs.Add(new PositionRun(start, end, x, y, z));
            start = end + 1;
        }

        PositionRun finalRun = runs[^1];
        PositionRun? earlierStable = null;
        PositionRun? earlierDistinctStable = null;
        for (int index = 0; index < runs.Count - 1; index++)
        {
            PositionRun run = runs[index];
            if (run.Count < MinimumStableRunSamples)
                continue;
            earlierStable = run;
            if (!run.Matches(finalRun))
                earlierDistinctStable = run;
        }

        if (finalRun.Count >= MinimumStableRunSamples && earlierDistinctStable is not null)
            return new(PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved,
                "two_distinct_coordinate_plateaus_observed_not_gameplay_movement",
                samples[earlierDistinctStable.End], samples[finalRun.End],
                earlierDistinctStable.Count, finalRun.Count);

        if (finalRun.Count >= MinimumStableRunSamples)
            return new(PhysicalRiftPositionTraceAssessmentState.OneCoordinatePlateau,
                earlierStable is null ? "one_coordinate_plateau_only" :
                    "one_coordinate_plateau_with_transient_different_xyz",
                samples[finalRun.End], null, finalRun.Count, finalRun.Count);

        if (earlierStable is not null && runs.Count > 1)
            return new(PhysicalRiftPositionTraceAssessmentState.AwaitingFinalPlateau,
                "awaiting_distinct_three_sample_final_plateau",
                samples[earlierStable.End], samples[finalRun.End],
                earlierStable.Count, finalRun.Count);

        return new(PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence,
            samples.Count < MinimumStableRunSamples
                ? "not_enough_accepted_xyz"
                : "no_three_sample_plateau",
            null, null, 0, finalRun.Count);
    }

    private static PhysicalRiftPositionTraceAssessment Create(
        PhysicalRiftPositionTraceAssessmentState state,
        string reason,
        PhysicalRiftPositionTraceReport? report,
        PlateauDescription? detail)
    {
        PhysicalRiftPositionTraceEntry? before = detail?.Before;
        PhysicalRiftPositionTraceEntry? after = detail?.After;
        double? distance = null;
        if (before?.X is float bx && before.Y is float by && before.Z is float bz &&
            after?.X is float ax && after.Y is float ay && after.Z is float az)
        {
            double dx = (double)ax - bx;
            double dy = (double)ay - by;
            double dz = (double)az - bz;
            double candidateDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (double.IsFinite(candidateDistance))
                distance = candidateDistance;
        }

        return new PhysicalRiftPositionTraceAssessment(
            state, reason,
            Math.Max(0, report?.CandidateSamples ?? 0),
            Math.Max(0, report?.RejectedSamples ?? 0),
            before?.X, before?.Y, before?.Z,
            before?.CapturedAtUtc, before?.MonotonicElapsedMilliseconds,
            after?.X, after?.Y, after?.Z,
            after?.CapturedAtUtc, after?.MonotonicElapsedMilliseconds,
            detail?.BeforeRunCount ?? 0, detail?.FinalRunCount ?? 0,
            distance);
    }
}
