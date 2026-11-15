using Rift.Automation.Windows;

internal static class PhysicalRiftPositionChangeSpecs
{
    private static readonly DateTimeOffset Captured =
        new(2026, 11, 15, 21, 0, 0, TimeSpan.Zero);

    private static PhysicalRiftPositionResult Candidate(
        float x = 1, float y = 2, float z = 3, int pid = 6001,
        long processStartTicks = 639303416167439797, ulong playerKey = 42,
        string? sha = null, double readElapsedMs = 50, DateTimeOffset? capturedAt = null) =>
        new(PhysicalRiftPositionStatus.Candidate, "native-current-build-candidate", pid,
            processStartTicks, sha ?? PhysicalRiftPositionReader.GameSha256,
            playerKey, x, y, z, capturedAt ?? Captured, readElapsedMs);

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical XYZ repeat equal candidate establishes stationary observations only", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            var first = tracker.Observe(Candidate(), 100);
            var same = tracker.Observe(Candidate(readElapsedMs: 12, capturedAt: Captured.AddSeconds(1)), 1100);
            check(first.Status == PhysicalRiftPositionChangeStatus.Baseline &&
                first.CandidateSamples == 1 && first.HasBaseline, "first exact-identity candidate establishes baseline");
            check(same.Status == PhysicalRiftPositionChangeStatus.Unchanged &&
                same.CandidateSamples == 2 && same.UnchangedSamples == 1 &&
                same.ChangedSamples == 0 && same.Distance3D == 0, "equal reacquired XYZ is unchanged not stale");
            check(!same.ProducerFreshnessQualified && !same.SustainedQualification && !same.CanAutomate,
                "no producer freshness or input permission invented");
        });

        test("Physical XYZ observed x-y-z deltas have exact independent 3D distance", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            tracker.Observe(Candidate(), 0);
            var movedXY = tracker.Observe(Candidate(x: 4, y: 6, z: 3), 1000);
            var movedZ = tracker.Observe(Candidate(x: 4, y: 6, z: -1), 2000);
            check(movedXY.Status == PhysicalRiftPositionChangeStatus.Changed &&
                movedXY.AbsoluteDeltaX == 3 && movedXY.AbsoluteDeltaY == 4 &&
                movedXY.AbsoluteDeltaZ == 0 && movedXY.Distance3D == 5,
                "3-4-5 observed coordinate value difference");
            check(movedZ.Status == PhysicalRiftPositionChangeStatus.Changed &&
                movedZ.AbsoluteDeltaX == 0 && movedZ.AbsoluteDeltaY == 0 &&
                movedZ.AbsoluteDeltaZ == 4 && movedZ.Distance3D == 4 &&
                movedZ.CandidateSamples == 3 && movedZ.ChangedSamples == 2,
                "z-only delta and per-current-epoch counts");
            check(movedZ.Reason.Contains("not_proven_gameplay_movement", StringComparison.Ordinal) &&
                !movedZ.CanAutomate, "measured coordinate value differences are not credited as movement feedback");
        });

        test("Physical XYZ rejects changed process epoch and resets baseline", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            tracker.Observe(Candidate(), 100);
            var wrongPid = tracker.Observe(Candidate(pid: 6002), 1100);
            check(wrongPid.Status == PhysicalRiftPositionChangeStatus.IdentityChanged &&
                !wrongPid.HasBaseline && wrongPid.CandidateSamples == 0 &&
                wrongPid.PlayerUnitKey is null, "process swap cannot carry baseline or unit key");
            var restart = tracker.Observe(Candidate(pid: 6002), 1200);
            check(restart.Status == PhysicalRiftPositionChangeStatus.Baseline &&
                restart.CandidateSamples == 1, "new process identity requires a fresh first baseline");
            var wrongEpoch = tracker.Observe(Candidate(pid: 6002, processStartTicks: 639303416167439798), 2300);
            check(wrongEpoch.Status == PhysicalRiftPositionChangeStatus.IdentityChanged &&
                !wrongEpoch.HasBaseline, "PID reuse with changed process start rejected");
            tracker.Observe(Candidate(), 2500);
            var differentUnit = tracker.Observe(Candidate(playerKey: 43), 3300);
            check(differentUnit.Status == PhysicalRiftPositionChangeStatus.IdentityChanged &&
                !differentUnit.HasBaseline, "player unit identity swap cannot inherit position state");
        });

        test("Physical XYZ invalid source hash and noncandidate receipt clear prior XYZ", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            tracker.Observe(Candidate(), 0);
            var wrongSha = tracker.Observe(Candidate(sha: new string('F', 64)), 1000);
            check(wrongSha.Status == PhysicalRiftPositionChangeStatus.InvalidCandidate &&
                !wrongSha.HasBaseline && wrongSha.ExecutableSha256 is null,
                "wrong build never becomes current identity");
            tracker.Observe(Candidate(), 2000);
            var unavailable = Candidate() with
            {
                Status = PhysicalRiftPositionStatus.Unavailable, X = null,
                Y = null, Z = null, PlayerUnitKey = null, ExecutableSha256 = null
            };
            var lost = tracker.Observe(unavailable, 3000);
            check(lost.Status == PhysicalRiftPositionChangeStatus.Unavailable &&
                !lost.HasBaseline && lost.CandidateSamples == 0,
                "unavailable reader cannot retain old current position");
            check(tracker.Observe(Candidate(), 4000).Status == PhysicalRiftPositionChangeStatus.Baseline,
                "first recovery sample is baseline not fabricated delta");
        });

        test("Physical XYZ reader identity mismatch stops continuity immediately", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            tracker.Observe(Candidate(), 100);
            var mismatch = Candidate() with { Status = PhysicalRiftPositionStatus.IdentityMismatch,
                ExecutableSha256 = null, PlayerUnitKey = null, X = null, Y = null, Z = null };
            var result = tracker.Observe(mismatch, 1100);
            check(result.Status == PhysicalRiftPositionChangeStatus.IdentityChanged &&
                !result.HasBaseline && result.CandidateSamples == 0,
                "native reader identity mismatch invalidates current sample and exposes a stop signal");
            check(tracker.Observe(Candidate(), 1200).Status == PhysicalRiftPositionChangeStatus.Baseline,
                "an explicit subsequent valid candidate starts a new independent baseline");
        });

        test("Physical XYZ monotonic order and bounded acquisition gaps invalidate baseline", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker(maxGapMilliseconds: 4500);
            tracker.Observe(Candidate(), 1000);
            var replay = tracker.Observe(Candidate(), 1000);
            check(replay.Status == PhysicalRiftPositionChangeStatus.OutOfOrder &&
                !replay.HasBaseline, "equal monotonic elapsed timestamp rejected");
            tracker.Observe(Candidate(), 2000);
            var gap = tracker.Observe(Candidate(), 6501);
            check(gap.Status == PhysicalRiftPositionChangeStatus.StaleAcquisitionGap &&
                !gap.HasBaseline && gap.Reason.Contains("not_game_producer_staleness", StringComparison.Ordinal),
                "4501ms gap is a missing timely read, not an asserted stale game producer");
            var rebaseline = tracker.Observe(Candidate(), 7000);
            var exactly = tracker.Observe(Candidate(), 11500);
            check(rebaseline.Status == PhysicalRiftPositionChangeStatus.Baseline &&
                exactly.Status == PhysicalRiftPositionChangeStatus.Unchanged,
                "4500ms gap remains inside inclusive bound");
        });

        test("Physical XYZ invalid numeric payload and acquisition elapsed fail closed", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            tracker.Observe(Candidate(), 100);
            var badX = tracker.Observe(Candidate(x: float.NaN), 200);
            check(badX.Status == PhysicalRiftPositionChangeStatus.InvalidCandidate &&
                !badX.HasBaseline, "NaN cannot produce distance or valid position");
            var noReadTime = tracker.Observe(Candidate(readElapsedMs: double.PositiveInfinity), 300);
            check(noReadTime.Status == PhysicalRiftPositionChangeStatus.InvalidCandidate,
                "nonfinite per-read duration is invalid");
            var noCapture = tracker.Observe(Candidate(capturedAt: default(DateTimeOffset)), 400);
            check(noCapture.Status == PhysicalRiftPositionChangeStatus.InvalidCandidate,
                "unavailable acquisition timestamp is invalid");
            tracker.Observe(Candidate(), 500);
            var negativeTime = tracker.Observe(Candidate(), -1);
            check(negativeTime.Status == PhysicalRiftPositionChangeStatus.InvalidCandidate &&
                !negativeTime.HasBaseline, "negative injected monotonic time invalidates baseline");
        });

        test("Physical XYZ cancellation requires an explicit fresh reset", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker();
            tracker.Observe(Candidate(), 100);
            var canceled = tracker.Cancel();
            check(canceled.Status == PhysicalRiftPositionChangeStatus.Cancelled &&
                !canceled.HasBaseline, "cancel drops current data");
            var afterCancel = tracker.Observe(Candidate(), 1100);
            check(afterCancel.Status == PhysicalRiftPositionChangeStatus.Cancelled &&
                !afterCancel.HasBaseline, "no automatic rearming after cancellation");
            tracker.Reset();
            var fresh = tracker.Observe(Candidate(), 2000);
            check(fresh.Status == PhysicalRiftPositionChangeStatus.Baseline &&
                fresh.CandidateSamples == 1 && fresh.Invalidations == 0,
                "explicit reset creates a new independent observation epoch");
        });

        test("Physical XYZ tracker bounds observations without carrying stale coordinates", () =>
        {
            var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 2);
            tracker.Observe(Candidate(), 0);
            tracker.Observe(Candidate(), 1000);
            var limit = tracker.Observe(Candidate(), 2000);
            check(limit.Status == PhysicalRiftPositionChangeStatus.LimitReached &&
                limit.Observations == 2 && !limit.HasBaseline,
                "limit invalidates current and refuses unbounded history");
            tracker.Reset();
            check(tracker.Observe(Candidate(), 3000).Status == PhysicalRiftPositionChangeStatus.Baseline,
                "bounded tracker can restart explicitly");
        });
    }
}
