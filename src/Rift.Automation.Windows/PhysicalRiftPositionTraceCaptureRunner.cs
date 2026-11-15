namespace Rift.Automation.Windows;

/// <summary>
/// Result of an explicitly requested, bounded read-only native XYZ capture.
/// A completed recording is NOT proof of producer freshness, gameplay movement or input authority.
/// </summary>
public sealed record PhysicalRiftPositionTraceCaptureResult(
    PhysicalRiftPositionTraceReport Report,
    bool CompletedWindow,
    bool Cancelled,
    string TerminalReasonCode,
    int Attempts)
{
    public bool PassiveFreshnessQualified => false;
    public bool GameplayMovementProven => false;
    public bool CanAutomate => false;
}

/// <summary>
/// Single-session capture orchestration over injected clocks/readers. Production callers
/// supply the pinned native read and Stopwatch; tests use virtual time and synthetic XYZ.
/// No process lookup, filesystem I/O, game input or background work lives in this class.
/// </summary>
public static class PhysicalRiftPositionTraceCaptureRunner
{
    public const int MinimumDurationSeconds = 10;
    public const int MaximumDurationSeconds = 120;
    public const int SamplingIntervalMilliseconds = 1000;
    private const int MaximumReadMilliseconds = 15_000;

    public static async Task<PhysicalRiftPositionTraceCaptureResult> RunAsync(
        int pid,
        long startTicks,
        int durationSeconds,
        Func<int, long, CancellationToken, Task<PhysicalRiftPositionResult>> readPinned,
        Func<long> elapsedMilliseconds,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken = default)
    {
        if (pid <= 0)
            throw new ArgumentOutOfRangeException(nameof(pid));
        if (startTicks <= 0 || startTicks > DateTime.MaxValue.Ticks)
            throw new ArgumentOutOfRangeException(nameof(startTicks));
        if (durationSeconds is < MinimumDurationSeconds or > MaximumDurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        ArgumentNullException.ThrowIfNull(readPinned);
        ArgumentNullException.ThrowIfNull(elapsedMilliseconds);
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentNullException.ThrowIfNull(delay);

        cancellationToken.ThrowIfCancellationRequested();

        var trace = new PhysicalRiftPositionTraceRecorder();
        trace.BeginSession(pid, startTicks);
        var tracker = new PhysicalRiftPositionChangeTracker(maxGapMilliseconds: 4500,
            maxObservations: PhysicalRiftPositionTraceRecorder.DefaultMaxEntries);
        long windowMilliseconds = durationSeconds * 1000L;
        long nextDueMilliseconds = 0;
        long lastRecordedMilliseconds = -1;
        int attempts = 0;
        bool cancelled = false;
        bool completed = false;
        string terminal = "capture_window_complete";

        void AppendUnavailable(string reason)
        {
            long now;
            try { now = elapsedMilliseconds(); }
            catch (Exception) { now = -1; }
            // A synthetic or failing clock cannot silently turn a discontinuity into a
            // clean acquisition: an OutOfOrder unavailable entry is still a terminal fault.
            long marker = now > lastRecordedMilliseconds
                ? now : lastRecordedMilliseconds + 1;
            trace.RecordUnavailable(reason, marker, utcNow());
            lastRecordedMilliseconds = marker;
            tracker.Reset();
        }

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                terminal = "user_cancelled";
                break;
            }

            long nowMs;
            try { nowMs = elapsedMilliseconds(); }
            catch (Exception)
            {
                terminal = "monotonic_clock_error";
                AppendUnavailable(terminal);
                break;
            }
            if (nowMs < 0 || nowMs < lastRecordedMilliseconds)
            {
                terminal = "nonadvancing_monotonic_clock";
                AppendUnavailable(terminal);
                break;
            }
            if (nowMs >= windowMilliseconds)
            {
                completed = true;
                break;
            }
            if (attempts >= durationSeconds + 1)
            {
                terminal = "bounded_attempt_limit";
                AppendUnavailable(terminal);
                break;
            }

            if (nowMs < nextDueMilliseconds)
            {
                long waitingMs = Math.Min(nextDueMilliseconds, windowMilliseconds) - nowMs;
                try
                {
                    await delay(TimeSpan.FromMilliseconds(waitingMs), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    terminal = "user_cancelled";
                    break;
                }
                catch (Exception)
                {
                    terminal = "sampling_delay_failed";
                    AppendUnavailable(terminal);
                    break;
                }
                long afterWait;
                try { afterWait = elapsedMilliseconds(); }
                catch (Exception)
                {
                    terminal = "monotonic_clock_error";
                    AppendUnavailable(terminal);
                    break;
                }
                if (afterWait <= nowMs && !cancellationToken.IsCancellationRequested)
                {
                    terminal = "nonadvancing_monotonic_clock";
                    AppendUnavailable(terminal);
                    break;
                }
                continue;
            }

            attempts++;
            long remainingMs = windowMilliseconds - nowMs;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(MaximumReadMilliseconds, remainingMs)));

            PhysicalRiftPositionResult? sample;
            try
            {
                sample = await readPinned(pid, startTicks, deadline.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                terminal = "user_cancelled";
                break;
            }
            catch (OperationCanceledException)
            {
                terminal = "native_read_deadline";
                AppendUnavailable(terminal);
                break;
            }
            catch (Exception)
            {
                terminal = "native_read_exception";
                AppendUnavailable(terminal);
                break;
            }

            long observedMs;
            DateTimeOffset observedUtc;
            try
            {
                observedMs = elapsedMilliseconds();
                observedUtc = utcNow();
            }
            catch (Exception)
            {
                terminal = "observation_clock_error";
                AppendUnavailable(terminal);
                break;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                terminal = "user_cancelled";
                break;
            }
            if (sample is null)
            {
                terminal = "native_read_missing";
                AppendUnavailable(terminal);
                break;
            }

            // The comparison baseline must never accept a stale/future candidate,
            // wrong current-build hash, invalid player key, nonfinite XYZ or timed-out read.
            bool candidateUsable = sample.Status == PhysicalRiftPositionStatus.Candidate
                && sample.ProcessId == pid && sample.ProcessStartUtcTicks == startTicks
                && string.Equals(sample.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
                    StringComparison.OrdinalIgnoreCase)
                && sample.PlayerUnitKey is > 0
                && sample.X is float x && float.IsFinite(x)
                && sample.Y is float y && float.IsFinite(y)
                && sample.Z is float z && float.IsFinite(z)
                && observedUtc >= sample.CapturedAtUtc
                && observedUtc - sample.CapturedAtUtc < TimeSpan.FromSeconds(2.5)
                && sample.ElapsedMilliseconds is >= 0 and <= MaximumReadMilliseconds
                && double.IsFinite(sample.ElapsedMilliseconds);
            PhysicalRiftPositionResult comparedSample =
                sample.Status == PhysicalRiftPositionStatus.Candidate && !candidateUsable
                    ? sample with { X = null, Y = null, Z = null }
                    : sample;
            PhysicalRiftPositionChangeSnapshot comparison = tracker.Observe(
                comparedSample, observedMs);
            trace.Record(sample, comparison, observedMs, observedUtc);
            lastRecordedMilliseconds = observedMs;

            // Exact process/player/build continuity must hold for the ENTIRE recording.
            // A negative/invalid outcome already entered the trace, so no extra
            // rebaseline is permitted and the M5 assessor will mark it incomplete.
            if (!candidateUsable ||
                comparison.Status is not (PhysicalRiftPositionChangeStatus.Baseline or
                    PhysicalRiftPositionChangeStatus.Changed or
                    PhysicalRiftPositionChangeStatus.Unchanged))
            {
                terminal = sample.Status == PhysicalRiftPositionStatus.IdentityMismatch ||
                    sample.ProcessId != pid || sample.ProcessStartUtcTicks != startTicks ||
                    comparison.Status == PhysicalRiftPositionChangeStatus.IdentityChanged
                        ? "source_identity_changed" : "native_acquisition_interrupted";
                break;
            }
            if (observedMs < nowMs || observedMs >= windowMilliseconds)
            {
                // A sample that finishes at or beyond the requested deadline, or
                // experiences a clock regression, cannot complete capture cleanly.
                // A sub-millisecond read may legitimately observe unchanged elapsed ms.
                terminal = "acquisition_outside_window";
                AppendUnavailable(terminal);
                break;
            }

            // Schedule next observation no sooner than one second after this read;
            // never catch up in bursts after slow acquisition.
            nextDueMilliseconds = observedMs + SamplingIntervalMilliseconds;
        }

        if (cancelled && trace.Snapshot().TotalSamples > 0)
            AppendUnavailable("user_cancelled");

        PhysicalRiftPositionTraceReport report = trace.Snapshot();
        return new PhysicalRiftPositionTraceCaptureResult(
            report, completed && !cancelled, cancelled, terminal, attempts);
    }
}
