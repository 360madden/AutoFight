using System.Text.Json;
using Rift.Automation.Windows;

internal static class PhysicalRiftPositionTraceSpecs
{
    private static readonly DateTimeOffset Captured =
        new(2026, 11, 15, 21, 45, 0, TimeSpan.Zero);
    private const int Pid = 6001;
    private const long StartTicks = 639303416167439797;

    private static PhysicalRiftPositionResult Candidate(
        float x = 1, float y = 2, float z = 3, int pid = Pid,
        long processStartTicks = StartTicks, ulong playerKey = 42,
        string? sha = null, DateTimeOffset? capturedAtUtc = null,
        double nativeReadMilliseconds = 12) =>
        new(PhysicalRiftPositionStatus.Candidate, "native-current-build-candidate",
            pid, processStartTicks, sha ?? PhysicalRiftPositionReader.GameSha256,
            playerKey, x, y, z, capturedAtUtc ?? Captured, nativeReadMilliseconds);

    private static void Record(
        PhysicalRiftPositionTraceRecorder recorder,
        PhysicalRiftPositionResult sample,
        PhysicalRiftPositionChangeSnapshot comparison,
        long monotonicElapsedMilliseconds) =>
        recorder.Record(sample, comparison, monotonicElapsedMilliseconds,
            sample.CapturedAtUtc.AddMilliseconds(100));

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical XYZ trace records accepted baseline changed unchanged without movement proof", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);

            var first = Candidate();
            var changed = Candidate(x: 4, y: 6, z: 3, capturedAtUtc: Captured.AddSeconds(1));
            var unchanged = Candidate(x: 4, y: 6, z: 3, capturedAtUtc: Captured.AddSeconds(2));
            Record(recorder, first, tracker.Observe(first, 100), 100);
            Record(recorder, changed, tracker.Observe(changed, 1100), 1100);
            Record(recorder, unchanged, tracker.Observe(unchanged, 2100), 2100);

            var report = recorder.Snapshot();
            check(report.TotalSamples == 3 && report.RetainedSamples == 3 &&
                report.CandidateSamples == 3 && report.ChangedSamples == 1 &&
                report.RejectedSamples == 0, "counts classify all independently acquired positions");
            check(report.ProcessId == Pid && report.ProcessStartUtcTicks == StartTicks &&
                report.ExecutableSha256 == PhysicalRiftPositionReader.GameSha256 &&
                report.PlayerUnitKey == 42, "process/build/player identity is pinned");
            check(report.Samples[0].ComparisonStatus == PhysicalRiftPositionChangeStatus.Baseline &&
                report.Samples[0].AcceptedForComparison &&
                report.Samples[0].Distance3D is null, "first candidate is a baseline only");
            check(report.Samples[1].ComparisonStatus == PhysicalRiftPositionChangeStatus.Changed &&
                report.Samples[1].X == 4 && report.Samples[1].Y == 6 &&
                report.Samples[1].AbsoluteDeltaX == 3 &&
                report.Samples[1].AbsoluteDeltaY == 4 &&
                report.Samples[1].Distance3D == 5 &&
                report.Samples[1].MonotonicElapsedMilliseconds == 1100 &&
                report.Samples[1].CapturedAtUtc == Captured.AddSeconds(1), "changed XYZ and 3D distance are exact");
            check(report.Samples[2].ComparisonStatus == PhysicalRiftPositionChangeStatus.Unchanged &&
                report.Samples[2].AcceptedForComparison && report.Samples[2].Distance3D == 0,
                "same position is a distinct unchanged acquisition");
            check(!report.PassiveFreshnessQualified && !report.GameplayMovementProven &&
                !report.CanAutomate, "coordinate changes do not establish producer freshness or input authority");
        });

        test("Physical XYZ trace preserves unavailable read without falsely carrying coordinates", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            var first = Candidate();
            Record(recorder, first, tracker.Observe(first, 100), 100);
            var unavailable = first with
            {
                Status = PhysicalRiftPositionStatus.Unavailable,
                Reason = "credential=trouble C:\\private\\game.exe",
                X = 123, Y = 456, Z = 789, ExecutableSha256 = null,
                PlayerUnitKey = null, CapturedAtUtc = Captured.AddSeconds(1)
            };
            Record(recorder, unavailable, tracker.Observe(unavailable, 1100), 1100);
            var report = recorder.Snapshot();
            var last = report.Samples[^1];
            check(report.TotalSamples == 2 && report.UnavailableSamples == 1 &&
                report.RejectedSamples == 1, "unavailable event counted with acquisition context");
            check(!last.AcceptedForComparison && last.X is null && last.Y is null &&
                last.Z is null && last.Distance3D is null &&
                last.Disposition == PhysicalRiftPositionTraceDisposition.Unavailable,
                "malformed unavailable payload cannot advertise an XYZ");
            check(report.ProcessId == Pid && report.PlayerUnitKey == 42,
                "failed read does not silently repin the known source");
            check(!report.ToJson().Contains("credential", StringComparison.OrdinalIgnoreCase) &&
                !report.ToJson().Contains("private", StringComparison.OrdinalIgnoreCase),
                "untrusted native reason is not exported");
        });

        test("Physical XYZ trace rejects PID and player switch until explicit reset", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            var first = Candidate();
            Record(recorder, first, tracker.Observe(first, 100), 100);

            var wrongPid = Candidate(pid: 7001, x: 777, capturedAtUtc: Captured.AddSeconds(1));
            Record(recorder, wrongPid, tracker.Observe(wrongPid, 1100), 1100);
            var wrongPidBaseline = Candidate(pid: 7001, x: 999, capturedAtUtc: Captured.AddSeconds(2));
            Record(recorder, wrongPidBaseline, tracker.Observe(wrongPidBaseline, 2100), 2100);
            var report = recorder.Snapshot();
            check(report.ProcessId == Pid && report.PlayerUnitKey == 42 &&
                report.CandidateSamples == 1 && report.RejectedSamples == 2,
                "source drift never repins trace or increments candidate count");
            check(report.Samples[1].Disposition == PhysicalRiftPositionTraceDisposition.IdentityMismatch &&
                report.Samples[2].Disposition == PhysicalRiftPositionTraceDisposition.IdentityMismatch &&
                report.Samples[1].X is null && report.Samples[2].X is null,
                "both mismatch and subsequent raw baseline are withheld");
            recorder.Reset();
            recorder.BeginSession(7001, StartTicks);
            tracker.Reset();
            Record(recorder, wrongPid, tracker.Observe(wrongPid, 100), 100);
            check(recorder.Snapshot().ProcessId == 7001 &&
                recorder.Snapshot().CandidateSamples == 1 &&
                recorder.Snapshot().TotalSamples == 1, "explicit reset begins a new verified source epoch");

            recorder.Reset();
            tracker.Reset();
            recorder.BeginSession(Pid, StartTicks);
            Record(recorder, first, tracker.Observe(first, 100), 100);
            var wrongUnit = Candidate(playerKey: 43, x: 123, capturedAtUtc: Captured.AddSeconds(1));
            Record(recorder, wrongUnit, tracker.Observe(wrongUnit, 1000), 1000);
            check(recorder.Snapshot().Samples[^1].X is null &&
                recorder.Snapshot().PlayerUnitKey == 42, "player-unit switch also fails closed");
        });

        test("Physical XYZ trace drops wrong-build missing and nonfinite candidate XYZ", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            var badBuild = Candidate(sha: new string('F', 64));
            Record(recorder, badBuild, tracker.Observe(badBuild, 100), 100);
            var noCoordinate = Candidate(x: float.NaN, capturedAtUtc: Captured.AddSeconds(1));
            Record(recorder, noCoordinate, tracker.Observe(noCoordinate, 200), 200);
            var wrongBuildReport = recorder.Snapshot();
            check(wrongBuildReport.CandidateSamples == 0 &&
                wrongBuildReport.PlayerUnitKey is null &&
                wrongBuildReport.ExecutableSha256 is null &&
                wrongBuildReport.Samples.All(e => e.X is null && e.Y is null && e.Z is null),
                "invalid source never seeds build/key or exposes nonfinite coordinates");
            check(!wrongBuildReport.ToJson().Contains(new string('F', 64), StringComparison.Ordinal),
                "arbitrary unrecognized executable fingerprint is redacted");

            var valid = Candidate(capturedAtUtc: Captured.AddSeconds(2));
            Record(recorder, valid, tracker.Observe(valid, 300), 300);
            check(recorder.Snapshot().CandidateSamples == 1 &&
                recorder.Snapshot().Samples[^1].ComparisonStatus == PhysicalRiftPositionChangeStatus.Baseline,
                "first valid acquisition after invalid readings can pin exact source");
        });

        test("Physical XYZ trace rejects stale gap and duplicate monotonic comparison", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            var first = Candidate();
            Record(recorder, first, tracker.Observe(first, 100), 100);
            var repeatedTime = Candidate(x: 20);
            Record(recorder, repeatedTime, tracker.Observe(repeatedTime, 100), 100);
            check(recorder.Snapshot().Samples[^1].Disposition ==
                PhysicalRiftPositionTraceDisposition.OutOfOrder &&
                recorder.Snapshot().Samples[^1].X is null, "same elapsed instant cannot be reused");
            var newBaseline = Candidate(x: 20);
            Record(recorder, newBaseline, tracker.Observe(newBaseline, 500), 500);
            var afterGap = Candidate(x: 30);
            Record(recorder, afterGap, tracker.Observe(afterGap, 5001), 5001);
            check(recorder.Snapshot().Samples[^1].ComparisonStatus ==
                PhysicalRiftPositionChangeStatus.StaleAcquisitionGap &&
                !recorder.Snapshot().Samples[^1].AcceptedForComparison &&
                recorder.Snapshot().Samples[^1].X is null, "acquisition gaps are rejected, not producer-staleness proof");
        });

        test("Physical XYZ trace rejects fabricated comparison deltas", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            var first = Candidate();
            Record(recorder, first, tracker.Observe(first, 100), 100);
            var changed = Candidate(x: 4, y: 6, capturedAtUtc: Captured.AddSeconds(1));
            var actual = tracker.Observe(changed, 1100);
            Record(recorder, changed, actual with { Distance3D = 9999 }, 1100);
            var last = recorder.Snapshot().Samples[^1];
            check(!last.AcceptedForComparison &&
                last.Disposition == PhysicalRiftPositionTraceDisposition.ComparisonRejected &&
                last.X is null && last.Distance3D is null, "tracker-output mismatch does not leak candidate as accepted XYZ");
        });

        test("Physical XYZ trace 600-entry FIFO cap evicts old evidence with copied snapshot", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder(maxEntries: 600);
            var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 1024);
            var oldSnapshot = recorder.Snapshot();
            for (int index = 0; index < 605; index++)
            {
                long elapsed = index * 100L;
                var sample = Candidate(capturedAtUtc: Captured.AddMilliseconds(elapsed));
                Record(recorder, sample, tracker.Observe(sample, elapsed), elapsed);
            }
            var report = recorder.Snapshot();
            check(report.MaxEntries == 600 && report.TotalSamples == 605 &&
                report.RetainedSamples == 600 && report.EvictedSamples == 5 &&
                report.CandidateSamples == 605 &&
                report.Samples[0].Sequence == 6 &&
                report.Samples[^1].Sequence == 605,
                "FIFO evicts exactly five oldest observations while retaining historical counts");
            check(oldSnapshot.TotalSamples == 0 && oldSnapshot.Samples.Count == 0,
                "prior snapshots are detached from subsequent writes");
            recorder.Reset();
            check(report.RetainedSamples == 600 && report.ProcessId == Pid &&
                recorder.Snapshot().TotalSamples == 0 &&
                recorder.Snapshot().ProcessId is null, "reset does not mutate an earlier immutable report");
            bool exposedMutableList = false;
            if (report.Samples is IList<PhysicalRiftPositionTraceEntry> list)
            {
                try { list.Clear(); exposedMutableList = true; }
                catch (NotSupportedException) { }
            }
            check(!exposedMutableList && report.Samples.Count == 600,
                "snapshot list cannot be mutated through an IList cast");
        });

        test("Physical XYZ trace error-only session preserves selected pin and redacts JSON", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            recorder.BeginSession(Pid, StartTicks);
            recorder.RecordUnavailable("pwd=my-secret C:\\private\\rift_x64.exe", 100, Captured);
            var report = recorder.Snapshot();
            string json = report.ToJson();
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            check(report.ProcessId == Pid && report.ProcessStartUtcTicks == StartTicks &&
                report.ExecutableSha256 is null && report.PlayerUnitKey is null &&
                report.UnavailableSamples == 1 && report.CandidateSamples == 0,
                "read failure before candidate retains selected PID/start but invents no build/player");
            check(root.GetProperty("schema").GetString() == PhysicalRiftPositionTraceRecorder.EvidenceSchema &&
                !root.GetProperty("passiveFreshnessQualified").GetBoolean() &&
                !root.GetProperty("gameplayMovementProven").GetBoolean() &&
                !root.GetProperty("canAutomate").GetBoolean(),
                "one versioned evidence object has all three explicit false gates");
            check(!json.Contains("pwd=", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("my-secret", StringComparison.OrdinalIgnoreCase) &&
                !json.Contains("C:\\", StringComparison.OrdinalIgnoreCase) &&
                json.Length < 4096, "exception/path/secret is redacted and empty-session evidence remains compact");

            bool rebindRejected = false;
            try { recorder.BeginSession(Pid + 1, StartTicks); }
            catch (InvalidOperationException) { rebindRejected = true; }
            check(rebindRejected, "source cannot be silently repinned after recording");
        });

        test("Physical XYZ trace age gate rejects stale and future captures despite tracker acceptance", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker();
            recorder.BeginSession(Pid, StartTicks);
            var stale = Candidate(x: 101);
            var staleComparison = tracker.Observe(stale, 100);
            recorder.Record(stale, staleComparison, 100, Captured.AddMilliseconds(2500));
            check(staleComparison.Status == PhysicalRiftPositionChangeStatus.Baseline &&
                recorder.Snapshot().Samples[^1].Disposition ==
                    PhysicalRiftPositionTraceDisposition.AcquisitionAgeRejected &&
                !recorder.Snapshot().Samples[^1].AcceptedForComparison &&
                recorder.Snapshot().Samples[^1].X is null, "2.5-second-old candidate has no usable XYZ");

            tracker.Reset();
            var future = Candidate(x: 202);
            recorder.Record(future, tracker.Observe(future, 200), 200, Captured.AddMilliseconds(-1));
            check(recorder.Snapshot().Samples[^1].Disposition ==
                    PhysicalRiftPositionTraceDisposition.AcquisitionAgeRejected &&
                recorder.Snapshot().Samples[^1].X is null &&
                recorder.Snapshot().PlayerUnitKey is null, "future timestamp cannot seed a source identity");

            tracker.Reset();
            var fresh = Candidate(x: 3);
            recorder.Record(fresh, tracker.Observe(fresh, 300), 300,
                Captured.AddMilliseconds(2499));
            check(recorder.Snapshot().Samples[^1].AcceptedForComparison &&
                recorder.Snapshot().CandidateSamples == 1 &&
                recorder.Snapshot().Samples[^1].X == 3, "fresh acquisition strictly below 2.5 seconds is retained");
        });

        test("Physical XYZ trace bounds output size and rejects excess retention", () =>
        {
            var recorder = new PhysicalRiftPositionTraceRecorder();
            var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 1024);
            for (int index = 0; index < 605; index++)
            {
                long elapsed = index * 100L;
                var sample = Candidate(capturedAtUtc: Captured.AddMilliseconds(elapsed));
                Record(recorder, sample, tracker.Observe(sample, elapsed), elapsed);
            }
            string json = recorder.Snapshot().ToJson();
            check(json.Length < 800_000 && json.Contains("\"retainedSamples\":600", StringComparison.Ordinal) &&
                !json.Contains("\"canAutomate\":true", StringComparison.Ordinal),
                "maximum trace remains a bounded single JSON object with denied automation gate");
            bool largeRejected = false;
            try { _ = new PhysicalRiftPositionTraceRecorder(maxEntries: 601); }
            catch (ArgumentOutOfRangeException) { largeRejected = true; }
            bool zeroRejected = false;
            try { _ = new PhysicalRiftPositionTraceRecorder(maxEntries: 0); }
            catch (ArgumentOutOfRangeException) { zeroRejected = true; }
            check(largeRejected && zeroRejected, "retention cannot be unbounded or empty");
        });
    }
}
