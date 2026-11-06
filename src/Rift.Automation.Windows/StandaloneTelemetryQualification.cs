using System.Diagnostics;

namespace Rift.Automation.Windows;

/// <summary>
/// An AutoFight-owned receiver qualifies one continuous, exact-identity source.
/// The established sustained feedback policy operates on the same typed snapshot
/// contract, without reading an RR2 receiver file or starting an RR2 service.
/// Qualification is observation evidence only, never an input capability.
/// </summary>
public static class StandaloneTelemetryQualification
{
    public const string PolicyId = "autofight-owned-480s-gap-strictly-below-4500-v1";
    public const string Mode = "AutoFightOwnedSustainedTelemetryEvidence";
    public const int SourceWarmupTimeoutMs = 30_000;
    public const int SourceWarmupPollMs = 500;

    /// <summary>
    /// Wait for an already-running receiver to obtain the first fully pinned
    /// usable XYZ sample. The required 480-second window starts only afterwards.
    /// A dead injected clock or missing samples cannot wait indefinitely.
    /// </summary>
    public static async Task<bool> WaitForInitialSourceAsync(
        string lab,
        Func<VmTelemetrySnapshot> read,
        Func<long> elapsedMilliseconds,
        Func<int, CancellationToken, Task> delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(elapsedMilliseconds);
        ArgumentNullException.ThrowIfNull(delay);
        if (VmControllerBindingChecks.ExpectedResource(lab) is null)
            return false;

        long started = elapsedMilliseconds();
        long last = started;
        for (int poll = 0; poll <= SourceWarmupTimeoutMs / SourceWarmupPollMs; poll++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VmTelemetrySnapshot sample = read();
            cancellationToken.ThrowIfCancellationRequested();
            long now = elapsedMilliseconds();
            if (now < last || now < started)
                return false;
            last = now;

            if (VmControllerBindingChecks.HasSourceBinding(lab, sample))
                return true;
            // A source for a different lab cannot satisfy this host binding.
            if (sample.Lab is "lab1" or "lab2" && sample.Lab != lab)
                return false;

            long remaining = SourceWarmupTimeoutMs - (now - started);
            if (remaining <= 0 || poll == SourceWarmupTimeoutMs / SourceWarmupPollMs)
                break;
            await delay((int)Math.Min(SourceWarmupPollMs, remaining), cancellationToken);
        }
        return false;
    }

    /// <summary>Deterministic, input-free 480-second policy over an owned receiver.</summary>
    public static async Task<VmSustainedFeedbackReport> RunObservedAsync(
        string lab,
        Func<VmTelemetrySnapshot> read,
        Func<long> elapsedMilliseconds,
        Func<int, CancellationToken, Task> delay,
        CancellationToken cancellationToken = default,
        IProgress<VmSustainedFeedbackProgress>? progress = null)
    {
        VmSustainedFeedbackReport report =
            await VmSustainedFeedbackRunner.RunObservedAsync(
                lab, read, elapsedMilliseconds, delay, cancellationToken, progress);
        return report with { Mode = Mode, PolicyId = PolicyId };
    }

    /// <summary>Production invocation; host session/listener lifetime is caller-owned.</summary>
    public static Task<VmSustainedFeedbackReport> RunAsync(
        string lab, StandaloneTelemetryHostSession session,
        CancellationToken cancellationToken = default,
        IProgress<VmSustainedFeedbackProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var stopwatch = Stopwatch.StartNew();
        return RunObservedAsync(lab, session.ReadLatest,
            () => stopwatch.ElapsedMilliseconds,
            static (ms, token) => Task.Delay(ms, token),
            cancellationToken, progress);
    }
}
