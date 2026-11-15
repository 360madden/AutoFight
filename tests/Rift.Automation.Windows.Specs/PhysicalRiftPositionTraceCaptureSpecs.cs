using Rift.Automation.Windows;

internal static class PhysicalRiftPositionTraceCaptureSpecs
{
    private const int Pid = 6001;
    private const long StartTicks = 639303416167439797;
    private static readonly DateTimeOffset Epoch =
        new(2026, 11, 15, 22, 30, 0, TimeSpan.Zero);

    private sealed class VirtualCapture
    {
        public long Milliseconds;
        public long ReadCostMilliseconds = 40;
        public int Reads;
        public long BiggestDelay;
        public CancellationTokenSource? CancelOn;
        public Func<int, PhysicalRiftPositionResult>? Return;
        public Func<CancellationToken, Task<PhysicalRiftPositionResult>>? OverrideRead;

        public PhysicalRiftPositionResult Candidate(float x = 1, ulong player = 42,
            int pid = Pid, long startTicks = StartTicks) =>
            new(PhysicalRiftPositionStatus.Candidate, "native-current-build-candidate",
                pid, startTicks, PhysicalRiftPositionReader.GameSha256,
                player, x, 2, 3, Epoch.AddMilliseconds(Milliseconds - 20), 12);

        public Task<PhysicalRiftPositionTraceCaptureResult> Run(
            int duration = 10, CancellationToken cancellationToken = default) =>
            PhysicalRiftPositionTraceCaptureRunner.RunAsync(Pid, StartTicks, duration,
                async (selectedPid, selectedStartTicks, token) =>
                {
                    if (selectedPid != Pid || selectedStartTicks != StartTicks)
                        throw new InvalidOperationException("Fake was called with an unpinned source.");
                    Reads++;
                    Milliseconds += ReadCostMilliseconds;
                    if (OverrideRead is not null)
                        return await OverrideRead(token);
                    var result = Return?.Invoke(Reads) ?? Candidate();
                    if (CancelOn is not null && Reads == 4)
                        CancelOn.Cancel();
                    return result;
                },
                () => Milliseconds,
                () => Epoch.AddMilliseconds(Milliseconds),
                (duration, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    long delta = checked((long)duration.TotalMilliseconds);
                    if (delta <= 0)
                        throw new InvalidOperationException("Nonpositive synthetic wait.");
                    BiggestDelay = Math.Max(BiggestDelay, delta);
                    Milliseconds += delta;
                    return Task.CompletedTask;
                }, cancellationToken);
    }

    private static PhysicalRiftPositionTraceCaptureResult Capture(
        VirtualCapture fake, int duration = 10, CancellationToken token = default) =>
        fake.Run(duration, token).GetAwaiter().GetResult();

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }

    private static void WithPrivateDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "AutoFight-M7TraceCaptureSpecs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("M7 virtual native capture A3 B3 yields honest plateau evidence", () =>
        {
            var fake = new VirtualCapture();
            fake.Return = count => fake.Candidate(count <= 3 ? 1 : 4);
            var capture = Capture(fake);
            var assessment = PhysicalRiftPositionTraceAssessor.Assess(capture.Report);
            check(capture.CompletedWindow && !capture.Cancelled &&
                  capture.TerminalReasonCode == "capture_window_complete" &&
                  capture.Attempts == fake.Reads && fake.Reads >= 6 && fake.Reads <= 11 &&
                  capture.Report.TotalSamples == fake.Reads &&
                  capture.Report.CandidateSamples == fake.Reads &&
                  assessment.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved &&
                  assessment.BeforeX == 1 && assessment.AfterX == 4 &&
                  assessment.Distance3D == 3 &&
                  !capture.CanAutomate && !capture.GameplayMovementProven &&
                  !capture.PassiveFreshnessQualified &&
                  !capture.Report.CanAutomate && !assessment.GameplayMovementProven,
                "A3/B3 proves only observed coordinate plateaus, never caused movement or authority");
        });

        test("M7 instant virtual reads preserve first baseline and no-catch-up cadence", () =>
        {
            var fake = new VirtualCapture { ReadCostMilliseconds = 0 };
            var capture = Capture(fake);
            check(capture.CompletedWindow && !capture.Cancelled &&
                  capture.Attempts == 10 && fake.Milliseconds == 10_000 &&
                  capture.Report.CandidateSamples == 10 &&
                  capture.Report.Samples[0].MonotonicElapsedMilliseconds == 0 &&
                  capture.Report.Samples[1].MonotonicElapsedMilliseconds == 1000,
                "sub-millisecond synthetic read at t=0 is valid; next read waits 1 second");
        });

        test("M7 completed synthetic trace exports and imports unmodified", () =>
            WithPrivateDirectory(directory =>
            {
                var fake = new VirtualCapture();
                fake.Return = count => fake.Candidate(count <= 3 ? 1 : 4);
                var capture = Capture(fake);
                string destination = Path.Combine(directory, "capture.json");
                PhysicalRiftPositionTraceExporter.CheckNewDestination(destination);
                PhysicalRiftPositionTraceExporter.SaveNew(destination, capture.Report);
                var imported = PhysicalRiftPositionTraceImporter.Load(destination);
                var assessed = PhysicalRiftPositionTraceAssessor.Assess(imported);
                check(imported.TotalSamples == capture.Attempts &&
                      assessed.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved &&
                      !imported.PassiveFreshnessQualified && !assessed.CanAutomate,
                    "runner produces canonical M4 JSON and M6 importable A/B evidence");
            }));

        test("M7 stationary 10-second window stays one plateau", () =>
        {
            var fake = new VirtualCapture();
            var capture = Capture(fake);
            var assessment = PhysicalRiftPositionTraceAssessor.Assess(capture.Report);
            check(capture.CompletedWindow && capture.Report.ChangedSamples == 0 &&
                  assessment.State == PhysicalRiftPositionTraceAssessmentState.OneCoordinatePlateau &&
                  fake.Reads >= 3 && fake.Milliseconds == 10_000,
                "equal coordinates remain stationary evidence, no gameplay movement proof");
        });

        test("M7 stale and future candidate XYZ are redacted before comparison", () =>
        {
            foreach (bool future in new[] { false, true })
            {
                var fake = new VirtualCapture();
                fake.Return = count => count == 2
                    ? fake.Candidate(99) with
                    {
                        CapturedAtUtc = future ? Epoch.AddMinutes(2) : Epoch.AddMinutes(-2)
                    }
                    : fake.Candidate();
                var capture = Capture(fake);
                var entry = capture.Report.Samples[1];
                check(!capture.CompletedWindow &&
                      capture.Report.CandidateSamples == 1 &&
                      capture.Report.RejectedSamples == 1 &&
                      entry.X is null && entry.Y is null && entry.Z is null &&
                      !entry.AcceptedForComparison &&
                      PhysicalRiftPositionTraceAssessor.Assess(capture.Report).State ==
                      PhysicalRiftPositionTraceAssessmentState.Interrupted,
                    "out-of-age candidate cannot establish or extend XYZ baseline");
            }
        });

        test("M7 wrong PID and changed player fail closed", () =>
        {
            foreach (bool wrongPid in new[] { true, false })
            {
                var fake = new VirtualCapture();
                fake.Return = count => count == 2
                    ? wrongPid ? fake.Candidate(99, pid: Pid + 1) :
                        fake.Candidate(99, player: 43)
                    : fake.Candidate();
                var capture = Capture(fake);
                var assessment = PhysicalRiftPositionTraceAssessor.Assess(capture.Report);
                check(!capture.CompletedWindow && !capture.Cancelled &&
                      capture.TerminalReasonCode == "source_identity_changed" &&
                      capture.Report.CandidateSamples == 1 &&
                      capture.Report.RejectedSamples == 1 &&
                      capture.Report.Samples[1].X is null &&
                      assessment.State == PhysicalRiftPositionTraceAssessmentState.SourceChanged,
                    "source identity discontinuity never silently rebaselines");
            }
        });

        test("M7 reader exception and deadline append unavailable terminal evidence", () =>
        {
            foreach (bool timeout in new[] { true, false })
            {
                var fake = new VirtualCapture();
                fake.OverrideRead = token =>
                {
                    if (fake.Reads == 2)
                    {
                        if (timeout) throw new OperationCanceledException(token);
                        throw new InvalidOperationException("should not leak arbitrary error message");
                    }
                    return Task.FromResult(fake.Candidate());
                };
                var capture = Capture(fake);
                var assessment = PhysicalRiftPositionTraceAssessor.Assess(capture.Report);
                check(!capture.CompletedWindow && !capture.Cancelled &&
                      capture.Attempts == 2 &&
                      capture.TerminalReasonCode ==
                      (timeout ? "native_read_deadline" : "native_read_exception") &&
                      capture.Report.Samples[^1].Disposition ==
                          PhysicalRiftPositionTraceDisposition.Unavailable &&
                      capture.Report.Samples[^1].X is null &&
                      !capture.Report.ToJson().Contains("should not leak", StringComparison.Ordinal) &&
                      assessment.State == PhysicalRiftPositionTraceAssessmentState.Interrupted,
                    "error/deadline ends capture, redacts error text and disallows success");
            }
        });

        test("M7 cancellation stops quickly and invalidates partial trace", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var fake = new VirtualCapture { CancelOn = cancellation };
            var capture = Capture(fake, token: cancellation.Token);
            check(capture.Cancelled && !capture.CompletedWindow &&
                  capture.TerminalReasonCode == "user_cancelled" &&
                  capture.Attempts == 4 && capture.Report.TotalSamples == 4 &&
                  capture.Report.Samples[^1].Disposition ==
                      PhysicalRiftPositionTraceDisposition.Unavailable &&
                  PhysicalRiftPositionTraceAssessor.Assess(capture.Report).State ==
                      PhysicalRiftPositionTraceAssessmentState.Interrupted,
                "cancel does not present partial trace as a completed capture");
        });

        test("M7 duration and sampling bounds prevent catch-up or overrun", () =>
        {
            var fake = new VirtualCapture();
            var capture = Capture(fake, duration: 120);
            check(capture.CompletedWindow && capture.Attempts == fake.Reads &&
                  fake.Reads >= 100 && fake.Reads <= 121 &&
                  fake.Milliseconds == 120_000 &&
                  fake.BiggestDelay <= 1_000 &&
                  capture.Report.TotalSamples <= 600,
                "120s capture yields ~1Hz bounded observations, not busy catch-up reads");
            check(Throws<ArgumentOutOfRangeException>(() => Capture(new VirtualCapture(), 9)) &&
                  Throws<ArgumentOutOfRangeException>(() => Capture(new VirtualCapture(), 121)),
                "duration outside 10..120 seconds is rejected before native read");
        });

        test("M7 explicit export destination preflight preserves existing bytes", () =>
            WithPrivateDirectory(directory =>
            {
                string fresh = Path.Combine(directory, "new-capture.json");
                string used = Path.Combine(directory, "used.json");
                byte[] unchanged = { 0, 1, 2, 3, 255 };
                File.WriteAllBytes(used, unchanged);
                PhysicalRiftPositionTraceExporter.CheckNewDestination(fresh);
                bool rejectsUsed = Throws<IOException>(() =>
                    PhysicalRiftPositionTraceExporter.CheckNewDestination(used));
                bool rejectsMissingParent = Throws<DirectoryNotFoundException>(() =>
                    PhysicalRiftPositionTraceExporter.CheckNewDestination(
                        Path.Combine(directory, "no-such-folder", "trace.json")));
                bool rejectsNonJson = Throws<ArgumentException>(() =>
                    PhysicalRiftPositionTraceExporter.CheckNewDestination(
                        Path.Combine(directory, "not-json.txt")));
                bool rejectsUnc = Throws<ArgumentException>(() =>
                    PhysicalRiftPositionTraceExporter.CheckNewDestination(
                        @"\\some-server\some-share\trace.json"));
                check(rejectsUsed && rejectsMissingParent && rejectsNonJson &&
                      rejectsUnc && File.ReadAllBytes(used).SequenceEqual(unchanged) &&
                      !File.Exists(fresh),
                    "preflight rejects existing/malformed/UNC destination without clobbering");
            }));
    }
}
