using Rift.Automation.Windows;

internal static class PhysicalRiftPositionTraceAssessmentSpecs
{
    private static readonly DateTimeOffset Captured =
        new(2026, 11, 15, 22, 10, 0, TimeSpan.Zero);
    private const int Pid = 6001;
    private const long StartTicks = 639303416167439797;

    private static PhysicalRiftPositionResult Candidate(
        float x, long elapsed, int processId = Pid, ulong playerKey = 42) =>
        new(PhysicalRiftPositionStatus.Candidate, "native-current-build-candidate",
            processId, StartTicks, PhysicalRiftPositionReader.GameSha256,
            playerKey, x, 2, 3, Captured.AddMilliseconds(elapsed), 12);

    private static PhysicalRiftPositionTraceReport Trace(
        IReadOnlyList<float> xs, IReadOnlyList<long>? instants = null)
    {
        var recorder = new PhysicalRiftPositionTraceRecorder();
        var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 1024);
        recorder.BeginSession(Pid, StartTicks);
        for (int index = 0; index < xs.Count; index++)
        {
            long elapsed = instants is null ? index * 1000L : instants[index];
            var sample = Candidate(xs[index], elapsed);
            recorder.Record(sample, tracker.Observe(sample, elapsed), elapsed,
                sample.CapturedAtUtc.AddMilliseconds(100));
        }
        return recorder.Snapshot();
    }

    private static bool NoAuthority(PhysicalRiftPositionTraceAssessment result) =>
        !result.PassiveFreshnessQualified &&
        !result.GameplayMovementProven && !result.CanAutomate;

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical XYZ assessment recognizes complete distinct A3 B3 coordinate plateaus", () =>
        {
            var report = Trace([1, 1, 1, 4, 4, 4]);
            var result = PhysicalRiftPositionTraceAssessor.Assess(report);
            check(result.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved &&
                result.ReasonCode == "two_distinct_coordinate_plateaus_observed_not_gameplay_movement" &&
                result.AcceptedSamples == 6 && result.RejectedSamples == 0,
                "two distinct 3-sample stationary coordinate-value runs observed");
            check(result.BaselineStableRunCount == 3 && result.FinalStableRunCount == 3 &&
                result.BeforeX == 1 && result.AfterX == 4 &&
                result.BeforeY == 2 && result.AfterY == 2 &&
                result.BeforeZ == 3 && result.AfterZ == 3 &&
                result.Distance3D == 3, "before and after exact XYZ and coordinate distance");
            check(result.BeforeMonotonicElapsedMilliseconds == 2000 &&
                result.AfterMonotonicElapsedMilliseconds == 5000 &&
                result.BeforeCapturedAtUtc == Captured.AddSeconds(2) &&
                result.AfterCapturedAtUtc == Captured.AddSeconds(5),
                "before = last initial plateau and after = last final plateau acquisition");
            check(NoAuthority(result), "even complete A/B does not prove producer freshness or gameplay cause");
        });

        test("Physical XYZ assessment tolerates intermediate changed positions before B plateau", () =>
        {
            var result = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 2, 4, 7, 7, 7]));
            check(result.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved &&
                result.BeforeX == 1 && result.AfterX == 7 && result.Distance3D == 6 &&
                result.BaselineStableRunCount == 3 && result.FinalStableRunCount == 3,
                "one or more intermediate changed XYZ values do not disqualify two stable endpoint runs");
            var returnTrace = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 4, 4, 4, 1, 1, 1]));
            check(returnTrace.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved &&
                returnTrace.BeforeX == 4 && returnTrace.AfterX == 1,
                "if coordinates return to old value, use the most recent distinct stable before run");
            var transientAndReturn = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 4, 1, 1, 1]));
            check(transientAndReturn.State == PhysicalRiftPositionTraceAssessmentState.OneCoordinatePlateau &&
                transientAndReturn.ReasonCode == "one_coordinate_plateau_with_transient_different_xyz" &&
                transientAndReturn.BeforeX == 1 && transientAndReturn.AfterX is null &&
                transientAndReturn.Distance3D is null,
                "brief different XYZ returning to A is not a second distinct stable endpoint");
        });

        test("Physical XYZ assessment distinguishes one plateau from incomplete evidence", () =>
        {
            var noTrace = PhysicalRiftPositionTraceAssessor.Assess(null);
            var empty = new PhysicalRiftPositionTraceRecorder().Snapshot();
            var one = PhysicalRiftPositionTraceAssessor.Assess(Trace([1, 1, 1, 1]));
            var noInitial = PhysicalRiftPositionTraceAssessor.Assess(Trace([1, 1, 2, 2, 2]));
            var noneStable = PhysicalRiftPositionTraceAssessor.Assess(Trace([1, 2, 3, 4]));
            check(noTrace.State == PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence &&
                noTrace.ReasonCode == "no_trace" &&
                PhysicalRiftPositionTraceAssessor.Assess(empty).State ==
                    PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence,
                "null and empty report do not invent a source or baseline");
            check(one.State == PhysicalRiftPositionTraceAssessmentState.OneCoordinatePlateau &&
                one.BaselineStableRunCount == 4 && one.AfterX is null &&
                one.Distance3D is null && NoAuthority(one),
                "only one stationary XYZ coordinate plateau exists");
            check(noInitial.State == PhysicalRiftPositionTraceAssessmentState.OneCoordinatePlateau &&
                noInitial.BaselineStableRunCount == 3 &&
                noneStable.State == PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence,
                "2+3 qualifies only one plateau, and changing positions without triples qualify none");
        });

        test("Physical XYZ assessment requires three final consecutive equal observations", () =>
        {
            var partial = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 4, 4]));
            var tiny = PhysicalRiftPositionTraceAssessor.Assess(Trace([1, 1]));
            check(partial.State == PhysicalRiftPositionTraceAssessmentState.AwaitingFinalPlateau &&
                partial.BaselineStableRunCount == 3 &&
                partial.FinalStableRunCount == 2 &&
                partial.BeforeX == 1 && partial.AfterX == 4 &&
                partial.Distance3D == 3 && NoAuthority(partial),
                "3+2 remains awaiting final triple, even though changed XYZ was sampled");
            check(tiny.State == PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence &&
                tiny.ReasonCode == "not_enough_accepted_xyz",
                "two stationary acquisitions cannot form even the initial plateau");
        });

        test("Physical XYZ assessment interrupts after native failure while retaining earlier A/B diagnostics", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            for (int index = 0; index < 6; index++)
            {
                long elapsed = index * 1000L;
                var sample = Candidate(index < 3 ? 1 : 4, elapsed);
                recorder.Record(sample, tracker.Observe(sample, elapsed), elapsed,
                    sample.CapturedAtUtc.AddMilliseconds(100));
            }
            recorder.RecordUnavailable("native_read_timeout", 6000, Captured.AddSeconds(6));
            var result = PhysicalRiftPositionTraceAssessor.Assess(recorder.Snapshot());
            check(result.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                result.ReasonCode == "native_acquisition_interrupted_after_observed_plateaus" &&
                result.AcceptedSamples == 6 && result.RejectedSamples == 1 &&
                result.BaselineStableRunCount == 3 && result.FinalStableRunCount == 3,
                "later read failure invalidates uninterrupted sequence classification");
            check(result.BeforeX == 1 && result.AfterX == 4 &&
                result.Distance3D == 3 && NoAuthority(result),
                "previously observed A/B values remain diagnostic, not qualified after interruption");

            var interrupted = new PhysicalRiftPositionTraceRecorder();
            var interruptedTracker = new PhysicalRiftPositionChangeTracker();
            interrupted.BeginSession(Pid, StartTicks);
            for (int index = 0; index < 3; index++)
            {
                long elapsed = index * 1000L;
                var sample = Candidate(1, elapsed);
                interrupted.Record(sample, interruptedTracker.Observe(sample, elapsed), elapsed,
                    sample.CapturedAtUtc.AddMilliseconds(100));
            }
            interrupted.RecordUnavailable("timeout", 3000, Captured.AddSeconds(3));
            interruptedTracker.Reset();
            for (int index = 0; index < 3; index++)
            {
                long elapsed = 4000 + index * 1000L;
                var sample = Candidate(4, elapsed);
                interrupted.Record(sample, interruptedTracker.Observe(sample, elapsed), elapsed,
                    sample.CapturedAtUtc.AddMilliseconds(100));
            }
            var noStitch = PhysicalRiftPositionTraceAssessor.Assess(interrupted.Snapshot());
            check(noStitch.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                noStitch.AfterX is null && noStitch.Distance3D is null,
                "never stitch A before a fault to B after it");
        });

        test("Physical XYZ assessment latches changed PID/player identity", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            for (int index = 0; index < 3; index++)
            {
                long elapsed = index * 1000L;
                var sample = Candidate(1, elapsed);
                recorder.Record(sample, tracker.Observe(sample, elapsed), elapsed,
                    sample.CapturedAtUtc.AddMilliseconds(100));
            }
            var swapped = Candidate(999, 3000, playerKey: 43);
            recorder.Record(swapped, tracker.Observe(swapped, 3000), 3000,
                swapped.CapturedAtUtc.AddMilliseconds(100));
            var result = PhysicalRiftPositionTraceAssessor.Assess(recorder.Snapshot());
            check(result.State == PhysicalRiftPositionTraceAssessmentState.SourceChanged &&
                result.ReasonCode == "pinned_source_identity_changed" &&
                result.AfterX is null && NoAuthority(result),
                "identity swap cannot promote untrusted XYZ as a second plateau");

            var accepted = Trace([1, 1, 1, 4, 4, 4]);
            var altered = accepted with
            {
                Samples = accepted.Samples.Select((entry, index) =>
                    index == 5 ? entry with { ObservedProcessId = Pid + 1 } : entry).ToArray()
            };
            var forged = PhysicalRiftPositionTraceAssessor.Assess(altered);
            check(forged.State == PhysicalRiftPositionTraceAssessmentState.SourceChanged &&
                forged.BeforeX == 1 && forged.AfterX == 4 &&
                forged.AfterMonotonicElapsedMilliseconds == 4000,
                "forged source is rejected while earlier partial B evidence remains diagnostic");
        });

        test("Physical XYZ assessment enforces strictly below 4500ms between acquisitions", () =>
        {
            var below = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 4, 4, 4], [0, 100, 200, 4699, 4799, 4899]));
            var atLimit = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 4, 4, 4], [0, 100, 200, 4700, 4800, 4900]));
            var over = PhysicalRiftPositionTraceAssessor.Assess(
                Trace([1, 1, 1, 4, 4, 4], [0, 100, 200, 4701, 4801, 4901]));
            check(below.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved,
                "4499ms accepted as a strictly sub-4500ms acquisition gap");
            check(atLimit.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                atLimit.ReasonCode == "acquisition_gap_at_or_above_4500ms" &&
                over.State == PhysicalRiftPositionTraceAssessmentState.Interrupted,
                "4500ms and larger are rejected even if legacy comparison tracker allows exact 4500");
        });

        test("Physical XYZ assessment rejects nonadvancing timestamps or malformed coordinate deltas", () =>
        {
            var report = Trace([1, 1, 1, 4, 4, 4]);
            var repeated = report with
            {
                Samples = report.Samples.Select((entry, index) =>
                    index == 4 ? entry with { MonotonicElapsedMilliseconds = 3000 } : entry).ToArray()
            };
            var badDelta = report with
            {
                Samples = report.Samples.Select((entry, index) =>
                    index == 3 ? entry with { Distance3D = 9999 } : entry).ToArray()
            };
            var repeatResult = PhysicalRiftPositionTraceAssessor.Assess(repeated);
            var badDeltaResult = PhysicalRiftPositionTraceAssessor.Assess(badDelta);
            check(repeatResult.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                repeatResult.ReasonCode == "nonadvancing_monotonic_time",
                "duplicate monotonic stopwatch reading invalidates continuity");
            check(badDeltaResult.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                badDeltaResult.ReasonCode == "invalid_coordinate_comparison" &&
                NoAuthority(badDeltaResult), "forged delta cannot be accepted as observed coordinate change");
        });

        test("Physical XYZ assessment rejects public DTO forged counters and retention cap", () =>
        {
            var report = Trace([1, 1, 1, 4, 4, 4]);
            var forgedCounters = report with
            {
                CandidateSamples = 1,
                RejectedSamples = 5
            };
            var forgedChanged = report with { ChangedSamples = 0 };
            var forgedRetention = report with { MaxEntries = 5 };
            var counterResult = PhysicalRiftPositionTraceAssessor.Assess(forgedCounters);
            check(counterResult.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                counterResult.ReasonCode == "invalid_trace_summary" &&
                PhysicalRiftPositionTraceAssessor.Assess(forgedChanged).State ==
                    PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                PhysicalRiftPositionTraceAssessor.Assess(forgedRetention).State ==
                    PhysicalRiftPositionTraceAssessmentState.Truncated,
                "misleading public report counters and smaller claimed cap cannot qualify an A/B trace");
        });

        test("Physical XYZ assessment rejects sequence gaps and FIFO evidence loss", () =>
        {
            var report = Trace([1, 1, 1, 4, 4, 4]);
            var missing = report with
            {
                Samples = report.Samples.Select((entry, index) =>
                    index == 3 ? entry with { Sequence = 99 } : entry).ToArray()
            };
            var fragmented = PhysicalRiftPositionTraceAssessor.Assess(missing);
            check(fragmented.State == PhysicalRiftPositionTraceAssessmentState.Truncated &&
                fragmented.ReasonCode == "noncontiguous_trace_sequence" &&
                NoAuthority(fragmented), "missing sequence cannot be stitched into a complete capture");

            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 1024);
            recorder.BeginSession(Pid, StartTicks);
            for (int index = 0; index < 605; index++)
            {
                long elapsed = index * 100L;
                var sample = Candidate(index < 302 ? 1 : 4, elapsed);
                recorder.Record(sample, tracker.Observe(sample, elapsed), elapsed,
                    sample.CapturedAtUtc.AddMilliseconds(100));
            }
            var report605 = recorder.Snapshot();
            var evicted = PhysicalRiftPositionTraceAssessor.Assess(report605);
            check(report605.RetainedSamples == 600 && report605.EvictedSamples == 5 &&
                evicted.State == PhysicalRiftPositionTraceAssessmentState.Truncated &&
                evicted.ReasonCode == "trace_fifo_evictions" &&
                NoAuthority(evicted), "FIFO drops disable complete-sequence assessment despite retained A/B");
        });
    }
}
