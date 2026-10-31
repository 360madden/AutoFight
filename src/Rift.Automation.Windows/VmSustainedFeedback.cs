using System.Diagnostics;

namespace Rift.Automation.Windows;

/// <summary>
/// Offline-testable, one-lab evidence policy. Reuses the existing continuity
/// tracker; never confers an action permit or communicates with a controller.
/// </summary>
public sealed class VmSustainedFeedbackWindow
{
    public const int RequiredDurationMs = 480_000;
    public const int PollIntervalMs = 500;
    public const int ForbiddenGapFromMs = 4_500;
    public const string PolicyId = "rr2-pinned-480s-gap-strictly-below-4500-v1";

    private readonly string _lab;
    private readonly VmTelemetryWatchTracker _tracker;
    private VmTelemetrySnapshot? _baseline;
    private bool _observed;
    private string? _fault;
    private long? _observedOuter;
    private long _observedProducerSequence;
    private double _observedProducerClock;
    private double _observedX;
    private double _observedY;
    private double _observedZ;

    public VmSustainedFeedbackWindow(string lab)
    {
        if (VmControllerBindingChecks.ExpectedResource(lab) is null)
            throw new ArgumentException("Lab must be lab1 or lab2.", nameof(lab));
        _lab = lab;
        _tracker = new(lab);
    }

    public bool HasTerminalFault => _fault is not null;

    public VmTelemetryWatchReport Observe(VmTelemetrySnapshot sample, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(sample);
        // Let the tracker validate monotonic time before mutating our own state.
        VmTelemetryWatchReport result = _tracker.Observe(sample, elapsedMs);
        if (!_observed)
        {
            _observed = true;
            if (elapsedMs != 0)
                Fail("invalid_initial_clock");
            if (!VmControllerBindingChecks.HasSourceBinding(_lab, sample))
                Fail(sample.Lab == _lab ? SafeReason(sample.Reason, "initial_source_unavailable")
                    : "initial_lab_mismatch");
            else
                _baseline = sample;
        }
        else if (sample.Lab != _lab)
        {
            Fail("lab_mismatch");
        }
        else if (VmControllerBindingChecks.HasSourceBinding(_lab, sample))
        {
            if (_baseline is null ||
                !VmControllerBindingChecks.HasStableSourceBinding(_lab, _baseline, sample) ||
                !string.Equals(_baseline.StreamIdentity, sample.StreamIdentity,
                    StringComparison.Ordinal) ||
                _baseline.AddonEpoch != sample.AddonEpoch)
                Fail("source_identity_changed");
        }
        else if (!VmControllerBindingChecks.HasStableFreshnessGap(_lab, _baseline, sample))
        {
            Fail(sample.FreshnessOnlyGap ? "source_identity_changed" :
                SafeReason(sample.Reason, "invalid_source_interruption"));
        }

        // A source can be expired without losing its validated transport and
        // producer ordering metadata. Preserve that watermark separately:
        // the general tracker deliberately treats expired XYZ as unavailable.
        if (VmControllerBindingChecks.HasSourceBinding(_lab, sample))
            ObserveOrder(sample, sample.X, sample.Y, sample.Z);
        else if (VmControllerBindingChecks.HasStableFreshnessGap(_lab, _baseline, sample))
            ObserveOrder(sample, sample.GapProducerX, sample.GapProducerY, sample.GapProducerZ);

        if (result.SessionChanges != 0) Fail("source_session_changed");
        if (result.Reordered != 0) Fail("source_ordering_conflict");
        if (result.LongestNoAdvanceMs >= ForbiddenGapFromMs)
            Fail("no_advance_limit_exceeded");
        return result;
    }

    public VmSustainedFeedbackReport Finish(bool completed, string? interruption = null)
    {
        VmTelemetryWatchReport result = _tracker.View();
        bool finished = completed && _observed && result.ElapsedMs >= RequiredDurationMs;
        string reason = _fault ??
            (interruption is not null ? SafeReason(interruption, "interrupted") :
            !finished ? "incomplete_window" :
            result.NewSamples < 2 ? "insufficient_progress" :
            result.LongestNoAdvanceMs >= ForbiddenGapFromMs ||
            result.SinceLastAdvanceMs >= ForbiddenGapFromMs ? "no_advance_limit_exceeded" :
            !result.HasCurrentPosition ||
            !VmControllerBindingChecks.HasStableSourceBinding(_lab, _baseline, result.Current) ||
            !string.Equals(_baseline!.StreamIdentity, result.Current!.StreamIdentity,
                StringComparison.Ordinal) ||
            _baseline.AddonEpoch != result.Current.AddonEpoch
                ? "terminal_source_unavailable" : "passed");

        bool passed = finished && reason == "passed";
        return new(
            Mode: "ReadOnlySustainedTelemetryEvidence", PolicyId, _lab, finished, passed,
            reason, RequiredDurationMs, result.ElapsedMs, PollIntervalMs,
            ForbiddenGapFromMs, result.Polls, result.NewSamples, result.Repeated,
            result.Unavailable, result.SessionChanges, result.Reordered, result.Recovered,
            result.LongestNoAdvanceMs, result.SinceLastAdvanceMs);
    }

    private void ObserveOrder(
        VmTelemetrySnapshot sample, double? candidateX, double? candidateY, double? candidateZ)
    {
        if (sample.OuterSequence is not long outer ||
            sample.AddonSequence is not long sequence ||
            sample.AddonClock is not double clock || !double.IsFinite(clock) ||
            candidateX is not double x || !double.IsFinite(x) ||
            candidateY is not double y || !double.IsFinite(y) ||
            candidateZ is not double z || !double.IsFinite(z) ||
            outer < 1 || sequence < 1 || clock < 0)
        {
            Fail("invalid_ordering_evidence");
            return;
        }
        if (_observedOuter is long previousOuter)
        {
            bool repeated = outer >= previousOuter &&
                sequence == _observedProducerSequence && clock == _observedProducerClock &&
                x == _observedX && y == _observedY && z == _observedZ;
            bool advancing = outer > previousOuter &&
                sequence > _observedProducerSequence && clock > _observedProducerClock;
            if (!repeated && !advancing)
                Fail("source_ordering_conflict");
            if (outer < previousOuter)
                Fail("source_ordering_conflict");
        }
        _observedOuter = Math.Max(_observedOuter ?? 0, outer);
        if (sequence > _observedProducerSequence && clock > _observedProducerClock ||
            _observedProducerSequence == 0)
        {
            _observedProducerSequence = sequence;
            _observedProducerClock = clock;
            _observedX = x;
            _observedY = y;
            _observedZ = z;
        }
    }

    private void Fail(string reason) => _fault ??= reason;

    // Never echo an untrusted receipt's free-form reason or an exception message.
    private static string SafeReason(string? reason, string fallback) => reason switch
    {
        "reader_not_running" or "identity_mismatch" or "source_conflict" or
        "file_unavailable" or "invalid_source_pin" or "invalid_receipt_or_pin" or
        "invalid_sequence" or "invalid_source_timing" or "source_clock_or_player_invalid" or
        "source_expired" or "stale_or_clock_uncertain" or "source_not_fresh" or
        "scan_not_complete" or "schema_invalid" or "size_limit" or
        "player_unavailable" or "coordinates_unavailable" or "source_unavailable" or
        "cancelled" or "read_error" or "clock_regression" or "poll_limit_exceeded" =>
            reason,
        _ => fallback
    };
}

public sealed record VmSustainedFeedbackReport(
    string Mode, string PolicyId, string Lab, bool Completed,
    bool SustainedTelemetryEvidencePassed, string Reason,
    int RequestedDurationMs, long ObservedDurationMs,
    int PollIntervalMs, int ForbiddenGapFromMs, int Polls, int NewSamples,
    int Repeated, int Unavailable, int SessionChanges, int Reordered,
    int Recovered, long LongestNoAdvanceMs, long SinceLastAdvanceMs)
{
    public bool InputAttempted => false;
    public bool GameFeedbackConfirmed => false;
    public bool CanAutomate => false;
}

/// <summary>Redacted, immutable policy progress; never contains source paths, identity or XYZ.</summary>
public sealed record VmSustainedFeedbackProgress(
    string Lab, long ElapsedMs, int RequiredDurationMs, int Polls,
    int NewSamples, int Repeated, int Unavailable,
    long LongestNoAdvanceMs, bool TerminalFault);

/// <summary>
/// Fixed-duration, read-only pinned-file runner. The injectable clock/read/delay
/// seam supports deterministic acceptance without an eight-minute test sleep.
/// The real clock is Stopwatch, never receipt wall time.
/// </summary>
public static class VmSustainedFeedbackRunner
{
    public static Task<VmSustainedFeedbackReport> RunAsync(
        string lab, string sourcePin, CancellationToken cancellationToken = default,
        IProgress<VmSustainedFeedbackProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePin);
        var timer = Stopwatch.StartNew();
        return RunObservedAsync(lab,
            () => VmTelemetryReader.ReadPinned(sourcePin),
            () => timer.ElapsedMilliseconds,
            static (ms, ct) => Task.Delay(ms, ct),
            cancellationToken, progress);
    }

    public static async Task<VmSustainedFeedbackReport> RunObservedAsync(
        string lab, Func<VmTelemetrySnapshot> read, Func<long> elapsedMilliseconds,
        Func<int, CancellationToken, Task> delay,
        CancellationToken cancellationToken = default,
        IProgress<VmSustainedFeedbackProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(elapsedMilliseconds);
        ArgumentNullException.ThrowIfNull(delay);
        if (VmControllerBindingChecks.ExpectedResource(lab) is null)
            return Empty(lab, "invalid_lab");

        var window = new VmSustainedFeedbackWindow(lab);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            VmTelemetrySnapshot initial = read();
            cancellationToken.ThrowIfCancellationRequested();
            long start = elapsedMilliseconds();
            VmTelemetryWatchReport first = window.Observe(initial, 0);
            ReportProgress(progress, window, first);
            if (window.HasTerminalFault) return window.Finish(false);
            // A single observation every ~500 ms, no retry of faulty source epochs.
            // The bounded poll count also protects a broken injected clock.
            long lastObservedAt = 0;
            for (int poll = 0; poll < 2048; poll++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long now = elapsedMilliseconds() - start;
                if (now < lastObservedAt) return window.Finish(false, "clock_regression");
                long remaining = VmSustainedFeedbackWindow.RequiredDurationMs - now;
                if (remaining > 0)
                    await delay((int)Math.Min(VmSustainedFeedbackWindow.PollIntervalMs,
                        remaining), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                VmTelemetrySnapshot sample = read();
                cancellationToken.ThrowIfCancellationRequested();
                long observedAt = elapsedMilliseconds() - start;
                if (observedAt < lastObservedAt)
                    return window.Finish(false, "clock_regression");
                lastObservedAt = observedAt;
                VmTelemetryWatchReport current = window.Observe(sample, observedAt); // Includes read IO.
                ReportProgress(progress, window, current);
                if (window.HasTerminalFault) return window.Finish(false);
                if (observedAt >= VmSustainedFeedbackWindow.RequiredDurationMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return window.Finish(true);
                }
            }
            return window.Finish(false, "poll_limit_exceeded");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return window.Finish(false, "cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.Security.SecurityException or OverflowException)
        {
            return window.Finish(false, "read_error");
        }
    }

    private static void ReportProgress(IProgress<VmSustainedFeedbackProgress>? progress,
        VmSustainedFeedbackWindow window, VmTelemetryWatchReport report)
    {
        progress?.Report(new VmSustainedFeedbackProgress(
            report.Lab, report.ElapsedMs, VmSustainedFeedbackWindow.RequiredDurationMs,
            report.Polls, report.NewSamples, report.Repeated, report.Unavailable,
            report.LongestNoAdvanceMs, window.HasTerminalFault));
    }

    private static VmSustainedFeedbackReport Empty(string lab, string reason) =>
        new("ReadOnlySustainedTelemetryEvidence",
            VmSustainedFeedbackWindow.PolicyId, lab, false, false, reason,
            VmSustainedFeedbackWindow.RequiredDurationMs, 0,
            VmSustainedFeedbackWindow.PollIntervalMs,
            VmSustainedFeedbackWindow.ForbiddenGapFromMs,
            0, 0, 0, 0, 0, 0, 0, 0, 0);
}
