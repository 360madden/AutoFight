using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>
/// One point-in-time, read-only diagnostic of the exact selected-lab and
/// source/controller checks used before a supervised pulse. This creates no
/// authority, sends no input and never implies sustained game feedback.
/// </summary>
public sealed record VmBoundPreflightReport(
    string Lab, string SourceStatus, string Reason,
    DateTimeOffset CheckedAtUtc, bool IdentityMatched, bool ActionReadyAtCheckTime)
{
    public string Mode => "ReadOnlyBoundPreflight";
    public bool BindingAndReadinessPassed => IdentityMatched && ActionReadyAtCheckTime;
    public bool InputAttempted => false;
    public bool GameFeedbackConfirmed => false;
    public bool CanAutomate => false;
    public string Qualification => "Point-in-time diagnostic only; never a lasting permit or proof of movement.";
}

public static class VmBoundPreflight
{
    /// <summary>
    /// Production read-only entry: exactly one optional status GET, two bounded
    /// source reads on the successful path and no HTTP action/arm/lease request.
    /// </summary>
    public static async Task<VmBoundPreflightReport> CheckAsync(
        string lab, string sourcePin, string configPath, CancellationToken ct = default)
    {
        VmControllerBridge? bridge = null;
        try
        {
            return await CheckObservedAsync(lab,
                () => VmTelemetryReader.ReadPinned(sourcePin),
                (source, token) =>
                {
                    bridge ??= new VmControllerBridge(configPath);
                    return bridge.VerifyPulseTargetAsync(lab, source, token);
                }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or JsonException
            or NotSupportedException or FormatException
            or System.Security.SecurityException)
        {
            return Result(lab, "unavailable", "config_or_source_unavailable", false, false);
        }
        finally
        {
            bridge?.Dispose();
        }
    }

    /// <summary>
    /// Purely read-only orchestration seam for synthetic tests. The verifier
    /// delegate is expected to perform only the authenticated status GET.
    /// Application callers must use CheckAsync, not substitute a verifier.
    /// </summary>
    public static async Task<VmBoundPreflightReport> CheckObservedAsync(
        string lab, Func<VmTelemetrySnapshot> readSource,
        Func<VmTelemetrySnapshot, CancellationToken, Task<VmControllerResult>> verifyStatus,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        ArgumentNullException.ThrowIfNull(verifyStatus);
        ct.ThrowIfCancellationRequested();
        if (VmControllerBindingChecks.ExpectedResource(lab) is null)
            return Result(lab, "unavailable", "invalid_lab", false, false);

        VmTelemetrySnapshot before = readSource();
        if (!VmControllerBindingChecks.HasSourceBinding(lab, before))
            return Result(lab, before.Status,
                before.Lab is "lab1" or "lab2" && before.Lab != lab
                    ? "lab_mismatch"
                    : before.HasPosition ? "source_binding_unavailable" : before.Reason,
                false, false);

        ct.ThrowIfCancellationRequested();
        VmControllerResult controller = await verifyStatus(before, ct);
        ct.ThrowIfCancellationRequested();
        // A complete, matching identity may be sampled while safely disarmed
        // or busy. Preserve that distinction without granting input authority.
        bool identityMatched = controller.HostAccepted ||
            controller.ErrorCode == "controller_not_ready";
        if (!identityMatched)
            return Result(lab, before.Status,
                controller.ErrorCode ?? "controller_binding_unavailable", false, false);

        VmTelemetrySnapshot latest = readSource();
        ct.ThrowIfCancellationRequested();
        if (!VmControllerBindingChecks.HasStableSourceBinding(lab, before, latest) ||
            !VmPulseObservationChecks.CanRevalidate(lab, before, latest))
            return Result(lab, latest.Status, "source_changed_or_expired_after_status", false, false);

        return Result(lab, latest.Status,
            controller.HostAccepted ? "checks_passed" : "controller_not_ready",
            true, controller.HostAccepted);
    }

    private static VmBoundPreflightReport Result(
        string lab, string sourceStatus, string reason,
        bool identityMatched, bool actionReady) =>
        new(lab, sourceStatus, reason, DateTimeOffset.UtcNow, identityMatched, actionReady);
}
