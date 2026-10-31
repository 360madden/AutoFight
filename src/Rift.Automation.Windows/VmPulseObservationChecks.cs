namespace Rift.Automation.Windows;

/// <summary>
/// Diagnostic-only source continuity checks for a single, explicitly supervised VM pulse.
/// These checks do not authorize input or establish action causality.
/// </summary>
public static class VmPulseObservationChecks
{
    public static bool IsUsable(string expectedLab, VmTelemetrySnapshot? sample) =>
        expectedLab is "lab1" or "lab2" &&
        sample is { HasPosition: true } &&
        sample.Lab == expectedLab &&
        !string.IsNullOrWhiteSpace(sample.StreamIdentity) &&
        sample.GameProcessId is > 0 &&
        sample.AddonEpoch is > 0 &&
        sample.OuterSequence is > 0 &&
        sample.AddonSequence is > 0 &&
        sample.AddonClock is double clock && double.IsFinite(clock) && clock >= 0 &&
        sample.X is double x && double.IsFinite(x) &&
        sample.Y is double y && double.IsFinite(y) &&
        sample.Z is double z && double.IsFinite(z);

    public static bool CanRevalidate(
        string expectedLab, VmTelemetrySnapshot? reviewed, VmTelemetrySnapshot? refreshed)
    {
        if (!SameIdentity(expectedLab, reviewed, refreshed))
            return false;

        // A new producer observation must advance both counters and its clock.
        // A receiver redelivery may advance only the outer transport counter,
        // but the producer frame and coordinates must be identical.
        bool newer = refreshed!.OuterSequence > reviewed!.OuterSequence &&
            refreshed.AddonSequence > reviewed.AddonSequence &&
            refreshed.AddonClock > reviewed.AddonClock;
        bool repeated = refreshed.OuterSequence >= reviewed.OuterSequence &&
            refreshed.AddonSequence == reviewed.AddonSequence &&
            refreshed.AddonClock == reviewed.AddonClock &&
            refreshed.X == reviewed.X && refreshed.Y == reviewed.Y &&
            refreshed.Z == reviewed.Z;
        return newer || repeated;
    }

    public static bool IsNewerMatching(
        string expectedLab, VmTelemetrySnapshot? beforeSend, VmTelemetrySnapshot? candidate) =>
        SameIdentity(expectedLab, beforeSend, candidate) &&
        candidate!.OuterSequence > beforeSend!.OuterSequence &&
        candidate.AddonSequence > beforeSend.AddonSequence &&
        candidate.AddonClock > beforeSend.AddonClock;

    /// <summary>
    /// A per-request continuity tracker must first accept the frame. A pairwise
    /// comparison alone cannot detect a rollback after receiver redelivery.
    /// Any observed session replacement disqualifies this action's feedback.
    /// </summary>
    public static bool IsAcceptedFeedback(
        string expectedLab, VmTelemetrySnapshot? beforeSend, VmTelemetryWatchReport? report) =>
        report is { SessionChanges: 0, State: "advancing" or "recovered" } &&
        report.Lab == expectedLab && report.HasCurrentPosition &&
        IsNewerMatching(expectedLab, beforeSend, report.Current);

    private static bool SameIdentity(
        string expectedLab, VmTelemetrySnapshot? initial, VmTelemetrySnapshot? later) =>
        IsUsable(expectedLab, initial) && IsUsable(expectedLab, later) &&
        string.Equals(initial!.StreamIdentity, later!.StreamIdentity, StringComparison.Ordinal) &&
        initial.GameProcessId == later.GameProcessId &&
        initial.AddonEpoch == later.AddonEpoch;

    /// <summary>
    /// Reads once before a user-supplied confirmation callback, and again only
    /// after a positive confirmation. No controller action is performed here.
    /// </summary>
    public static VmPulseSourceReview CheckAcrossConfirmation(
        string expectedLab, Func<VmTelemetrySnapshot> readPinned,
        Func<bool> confirm, Func<bool> isClosing)
    {
        ArgumentNullException.ThrowIfNull(readPinned);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(isClosing);

        VmTelemetrySnapshot initial = readPinned();
        if (!IsUsable(expectedLab, initial))
            return new(VmPulseReviewDisposition.PrecheckFailed, initial, null);
        if (isClosing() || !confirm() || isClosing())
            return new(VmPulseReviewDisposition.Cancelled, initial, null);

        VmTelemetrySnapshot refreshed = readPinned();
        if (isClosing())
            return new(VmPulseReviewDisposition.Cancelled, initial, null);
        return CanRevalidate(expectedLab, initial, refreshed)
            ? new(VmPulseReviewDisposition.SourceRechecked, initial, refreshed)
            : new(VmPulseReviewDisposition.SourceChanged, initial, null);
    }
}

public enum VmPulseReviewDisposition
{
    PrecheckFailed,
    Cancelled,
    SourceChanged,
    SourceRechecked
}

public sealed record VmPulseSourceReview(
    VmPulseReviewDisposition Disposition,
    VmTelemetrySnapshot Initial,
    VmTelemetrySnapshot? BeforeSend);
