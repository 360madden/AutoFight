namespace Rift.Automation.Core;

public sealed record ReplayLatencySummary(
    int Count, long? MinMs, long? P50Ms, long? P95Ms, long? P99Ms, long? MaxMs);
public sealed record ReplayResultCount(string Code, int Count);
public sealed record ReplayStatusCount(string Status, int Count);
public sealed record ReplayStatusDuration(string Status, long DurationMs);

public sealed record ReplayAnalysisReport(
    string Scenario, int StepCount, long WindowStartMs, long WindowEndMs,
    long MaxAcquisitionAgeMs, long MaxReceiveAgeMs,
    int ResetCount, int GapCount, int TickCount,
    int SampleCount, int AcceptedSampleCount, int RejectedSampleCount,
    int AcceptedPresentSampleCount, int FreshPresentOnArrivalCount,
    long UsableDurationMs, long UnavailableDurationMs, long LongestUnavailableIntervalMs,
    long TrailingUnavailableDurationMs, bool TrailingUnavailableIsRightCensored,
    string FinalStatus, IReadOnlyList<ReplayStatusCount> InputStatusCounts,
    IReadOnlyList<ReplayStatusDuration> StatusDurations,
    IReadOnlyList<ReplayResultCount> SampleResults,
    ReplayLatencySummary AcquisitionSpan, ReplayLatencySummary ReceiptDelay,
    ReplayLatencySummary ApplicationQueueWait, ReplayLatencySummary AcquisitionToApply)
{
    public string Schema => "rift-replay-analysis/1";
    public string Origin => "SyntheticReplay";
    public string CoverageModel => "integer_ms_slots_first_reset_to_final_step_exclusive";
    public string LatencyPopulation => "all_structurally_valid_sample_attempts_including_rejected";
    public string PercentileMethod => "nearest_rank";
    public bool IsLiveQualification => false;
    public bool CanAutomate => false;
    public long DurationMs => WindowEndMs - WindowStartMs;
    public double? UsablePercent => DurationMs == 0 ? null : 100.0 * UsableDurationMs / DurationMs;
}

/// <summary>
/// Bounded, pure analysis of a fully validated synthetic scenario. Coverage is
/// measured in integer-ms slots, not real wall time or producer liveness.
/// No intervals beyond the final step are inferred.
/// </summary>
public static class ReplayAnalysis
{
    public static ReplayAnalysisReport Analyze(ReplayScenario scenario)
    {
        // The player copies and validates the entire scenario before analysis.
        var player = new ReplayPlayer(scenario);
        IReadOnlyList<ReplayStep> steps = scenario.Steps;
        var acquisition = new List<long>();
        var receipt = new List<long>();
        var queue = new List<long>();
        var total = new List<long>();
        var results = new Dictionary<string, int>(StringComparer.Ordinal);
        ObservationStatus[] inputStatuses = Enum.GetValues<ObservationStatus>();
        var inputCounts = inputStatuses.ToDictionary(status => status, _ => 0);
        ViewStatus[] statuses = Enum.GetValues<ViewStatus>();
        var durations = statuses.ToDictionary(status => status, _ => 0L);
        int resets = 0, gaps = 0, ticks = 0, samples = 0;
        int accepted = 0, acceptedPresent = 0, freshPresent = 0;
        long unavailableRun = 0, longestUnavailable = 0;

        void Charge(ViewStatus status, long duration)
        {
            if (duration == 0)
                return;
            durations[status] = checked(durations[status] + duration);
            if (status == ViewStatus.Present)
                unavailableRun = 0;
            else
            {
                unavailableRun = checked(unavailableRun + duration);
                longestUnavailable = Math.Max(longestUnavailable, unavailableRun);
            }
        }

        for (int index = 0; index < steps.Count; index++)
        {
            ReplayStep step = steps[index];
            player.Step();
            PositionView view = player.View;
            switch (step)
            {
                case ResetStep:
                    resets++;
                    break;
                case GapStep:
                    gaps++;
                    break;
                case TickStep:
                    ticks++;
                    break;
                case SampleStep sample:
                    samples++;
                    PositionObservation observation = sample.Observation;
                    inputCounts[observation.Status]++;
                    acquisition.Add(observation.AcquiredEndMs - observation.AcquiredStartMs);
                    receipt.Add(observation.ReceivedMs - observation.AcquiredEndMs);
                    queue.Add(step.AtMs - observation.ReceivedMs);
                    total.Add(step.AtMs - observation.AcquiredStartMs);
                    string code = player.LastResultCode;
                    results[code] = results.GetValueOrDefault(code) + 1;
                    if (code == "accepted")
                    {
                        accepted++;
                        if (observation.Status == ObservationStatus.Present)
                        {
                            acceptedPresent++;
                            if (view.CanObservePosition)
                                freshPresent++;
                        }
                    }
                    break;
            }

            long end = index + 1 < steps.Count ? steps[index + 1].AtMs : step.AtMs;
            long duration = end - step.AtMs;
            if (duration == 0)
                continue; // Same-time source order, final state owns next slots.
            if (view.Status != ViewStatus.Present)
            {
                Charge(view.Status, duration);
                continue;
            }

            if (view.AcquisitionAgeMs is not long acquisitionAge ||
                view.ReceiveAgeMs is not long receiveAge ||
                acquisitionAge < 0 || acquisitionAge > player.MaxAcquisitionAgeMs ||
                receiveAge < 0 || receiveAge > player.MaxReceiveAgeMs)
                throw new InvalidOperationException("Present view has inconsistent age bounds.");

            long remaining = Math.Min(player.MaxAcquisitionAgeMs - acquisitionAge,
                player.MaxReceiveAgeMs - receiveAge);
            // Age equal to budget remains valid. Compare remaining allowance
            // instead of adding expiry to an absolute near-Int64.Max timestamp.
            long freshDuration = remaining >= duration - 1 ? duration : remaining + 1;
            Charge(ViewStatus.Present, freshDuration);
            Charge(ViewStatus.Stale, duration - freshDuration);
        }

        long start = steps[0].AtMs;
        long finish = steps[^1].AtMs;
        long usable = durations[ViewStatus.Present];
        PositionView finalView = player.View;
        bool endsUnavailable = !finalView.CanObservePosition;
        return new ReplayAnalysisReport(
            scenario.Name, steps.Count, start, finish,
            player.MaxAcquisitionAgeMs, player.MaxReceiveAgeMs,
            resets, gaps, ticks, samples, accepted, samples - accepted,
            acceptedPresent, freshPresent, usable, finish - start - usable,
            longestUnavailable, endsUnavailable ? unavailableRun : 0, endsUnavailable,
            finalView.Status.ToString(),
            Array.AsReadOnly(inputStatuses.Select(status =>
                new ReplayStatusCount(status.ToString(), inputCounts[status])).ToArray()),
            Array.AsReadOnly(statuses.Select(status =>
                new ReplayStatusDuration(status.ToString(), durations[status])).ToArray()),
            Array.AsReadOnly(results.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ReplayResultCount(pair.Key, pair.Value)).ToArray()),
            Summarize(acquisition), Summarize(receipt), Summarize(queue), Summarize(total));
    }

    private static ReplayLatencySummary Summarize(List<long> values)
    {
        if (values.Count == 0)
            return new(0, null, null, null, null, null);
        values.Sort();
        long Quantile(int percentile) => values[(values.Count * percentile + 99) / 100 - 1];
        return new(values.Count, values[0], Quantile(50), Quantile(95), Quantile(99), values[^1]);
    }
}
