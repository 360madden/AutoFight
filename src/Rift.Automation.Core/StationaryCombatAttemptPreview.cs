namespace Rift.Automation.Core;

/// <summary>
/// One hypothetical stationary attack, with no input adapter and no retry.
/// A completed assessment is historical observation, never command attribution.
/// </summary>
public enum CombatPreviewAttemptStage
{
    AwaitingReadiness, Ready, AwaitingAcknowledgment, AwaitingHealth,
    Completed, Failed, Stopped
}

public sealed record CombatPreviewAttemptReport(
    CombatPreviewAttemptStage Stage, string Reason, CombatOutcomeAssessment? Outcome = null)
{
    public bool CanExecute => false;
}

/// <summary>
/// Single-owner synthetic composition of the existing preview planner and
/// outcome assessor. Construct a new instance for another hypothetical attempt.
/// </summary>
public sealed class StationaryCombatAttemptPreview
{
    private readonly StationaryCombatPlanner planner = new();
    private readonly string sessionId;
    private CombatObservation? initialObservation;
    private CombatHealthSnapshot? initialHealth;
    private CombatAttemptWindow? attempt;
    private long lastNowMs = -1;
    private long latestObservedSequence = -1;
    private long latestObservedAtMs = -1;
    private CombatPreviewAttemptReport status =
        new(CombatPreviewAttemptStage.AwaitingReadiness, "awaiting-qualified-synthetic-readiness");

    public StationaryCombatAttemptPreview(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session required.", nameof(sessionId));
        this.sessionId = sessionId;
        planner.Reset(sessionId);
    }

    public CombatPreviewAttemptReport Status => status;

    public CombatPreviewAttemptReport ObserveReadiness(
        CombatObservation observation, CombatHealthSnapshot health, long nowMs)
    {
        if (Terminal) return status;
        if (status.Stage != CombatPreviewAttemptStage.AwaitingReadiness)
            return Fail("wrong-stage");
        if (!AcceptNow(nowMs)) return Fail("clock-regressed");
        if (!PairValid(observation, health, nowMs) || health.CurrentHealth is not > 0 ||
            observation.TargetAlive is not true)
            return Fail("readiness-source-or-health-invalid");
        var decision = planner.Evaluate(observation, nowMs);
        if (decision.Intent != CombatIntent.RequestBasicAttack || decision.CanExecute)
            return Fail("readiness-" + decision.Reason);
        initialObservation = observation;
        initialHealth = health;
        latestObservedSequence = health.Sequence;
        latestObservedAtMs = health.AcquiredAtMs;
        return Set(CombatPreviewAttemptStage.Ready, "synthetic-preview-ready");
    }

    public CombatPreviewAttemptReport BeginPreviewAttack(string commandId, long nowMs)
    {
        if (Terminal) return status;
        if (status.Stage != CombatPreviewAttemptStage.Ready ||
            initialHealth is null || initialObservation is null)
            return Fail("wrong-stage");
        if (!AcceptNow(nowMs)) return Fail("clock-regressed");
        if (nowMs <= initialHealth.AcquiredAtMs ||
            nowMs - initialHealth.AcquiredAtMs > 2000 ||
            string.IsNullOrWhiteSpace(commandId) ||
            !planner.BeginPreviewAttack(commandId, initialObservation.TargetId!, nowMs))
            return Fail("preview-dispatch-invalid");
        attempt = new(sessionId, commandId, initialHealth.TargetId, nowMs, nowMs);
        return Set(CombatPreviewAttemptStage.AwaitingAcknowledgment, "awaiting-hypothetical-acknowledgment");
    }

    /// <summary>Optional pre-ack observation; a lost source fences all later callbacks.</summary>
    public CombatPreviewAttemptReport ObservePending(
        CombatObservation observation, CombatHealthSnapshot health, long nowMs)
    {
        if (Terminal) return status;
        if (status.Stage != CombatPreviewAttemptStage.AwaitingAcknowledgment ||
            attempt is null || initialHealth is null)
            return Fail("wrong-stage");
        if (!AcceptNow(nowMs)) return Fail("clock-regressed");
        if (!PairValid(observation, health, nowMs) ||
            health.Sequence <= latestObservedSequence ||
            health.AcquiredAtMs < latestObservedAtMs ||
            health.TargetId != initialHealth.TargetId ||
            health.MaximumHealth != initialHealth.MaximumHealth ||
            observation.PlayerAlive is not true || observation.RecoveryRequired is not false ||
            observation.TargetAlive is not true || health.CurrentHealth is not > 0)
            return Fail("pending-source-or-target-lost");
        var decision = planner.Evaluate(observation, nowMs);
        if (decision.Phase != CombatPhase.AttackPending || decision.Intent != CombatIntent.None)
            return Fail("pending-" + decision.Reason);
        latestObservedSequence = health.Sequence;
        latestObservedAtMs = health.AcquiredAtMs;
        return status;
    }

    public CombatPreviewAttemptReport CompletePreviewAttack(
        string commandId, string targetId, bool acknowledged, long nowMs)
    {
        if (Terminal) return status;
        if (status.Stage != CombatPreviewAttemptStage.AwaitingAcknowledgment ||
            attempt is null) return Fail("wrong-stage");
        if (!AcceptNow(nowMs)) return Fail("clock-regressed");
        if (commandId != attempt.CommandId || targetId != attempt.TargetId ||
            !acknowledged ||
            !planner.CompletePreviewAttack(commandId, targetId, acknowledged, nowMs))
            return Fail("acknowledgment-rejected");
        attempt = attempt with { AcknowledgedAtMs = nowMs };
        return Set(CombatPreviewAttemptStage.AwaitingHealth, "awaiting-post-ack-health");
    }

    public CombatPreviewAttemptReport ObservePostAckHealth(
        CombatObservation observation, CombatHealthSnapshot health, long nowMs)
    {
        if (Terminal) return status;
        if (status.Stage != CombatPreviewAttemptStage.AwaitingHealth ||
            attempt is null || initialHealth is null)
            return Fail("wrong-stage");
        if (!AcceptNow(nowMs)) return Fail("clock-regressed");
        // The observation window closes by receiver monotonic time, even when
        // a sampled timestamp barely precedes the deadline.
        if (nowMs - attempt.DispatchedAtMs > 10000) return Fail("observation-timeout");
        // A dead target with zero health can be valid after acknowledgment;
        // unlike pre-ack observation, it is not required to remain alive.
        if (!PairValid(observation, health, nowMs) ||
            health.TargetId != attempt.TargetId ||
            health.Sequence <= latestObservedSequence ||
            health.AcquiredAtMs <= latestObservedAtMs ||
            health.AcquiredAtMs <= attempt.AcknowledgedAtMs ||
            observation.PlayerAlive is not true || observation.RecoveryRequired is not false ||
            (observation.TargetAlive is false && health.CurrentHealth != 0))
            return Fail("post-ack-source-or-target-invalid");
        var assessed = CombatOutcomeAssessor.Assess(attempt, initialHealth, health);
        if (assessed.Status == CombatOutcomeStatus.Invalid)
            return Fail("post-ack-" + assessed.Reason);
        return Set(CombatPreviewAttemptStage.Completed, "historical-comparison-complete", assessed);
    }

    /// <summary>Monotonic no-telemetry timeout check; never generates an action.</summary>
    public CombatPreviewAttemptReport AdvanceTime(long nowMs)
    {
        if (Terminal) return status;
        if (!AcceptNow(nowMs)) return Fail("clock-regressed");
        return status.Stage switch
        {
            CombatPreviewAttemptStage.Ready when initialHealth is not null &&
                nowMs - initialHealth.AcquiredAtMs > 2000 => Fail("readiness-expired"),
            CombatPreviewAttemptStage.AwaitingAcknowledgment when attempt is not null &&
                nowMs - attempt.DispatchedAtMs > 3000 => Fail("acknowledgment-timeout"),
            CombatPreviewAttemptStage.AwaitingHealth when attempt is not null &&
                nowMs - attempt.DispatchedAtMs > 10000 => Fail("observation-timeout"),
            _ => status
        };
    }

    public CombatPreviewAttemptReport Stop()
    {
        if (Terminal) return status;
        planner.Stop();
        initialHealth = null;
        initialObservation = null;
        attempt = null;
        return Set(CombatPreviewAttemptStage.Stopped, "stop-latched");
    }

    private bool PairValid(CombatObservation observation, CombatHealthSnapshot health, long nowMs)
    {
        if (observation is null || health is null ||
            observation.SessionId != sessionId || health.SessionId != sessionId ||
            string.IsNullOrWhiteSpace(observation.TargetId) ||
            observation.TargetId != health.TargetId ||
            observation.Sequence < 0 || observation.Sequence != health.Sequence ||
            observation.AcquiredAtMs < 0 ||
            observation.AcquiredAtMs != health.AcquiredAtMs ||
            observation.AcquiredAtMs > nowMs || nowMs - observation.AcquiredAtMs > 2000 ||
            !observation.SourceQualified || !health.SourceQualified ||
            health.CurrentHealth is not double hp || health.MaximumHealth is not double max ||
            !double.IsFinite(hp) || !double.IsFinite(max) ||
            max <= 0 || hp < 0 || hp > max)
            return false;
        return true;
    }

    private bool AcceptNow(long nowMs)
    {
        if (nowMs < 0 || nowMs < lastNowMs) return false;
        lastNowMs = nowMs;
        return true;
    }

    private bool Terminal => status.Stage is
        CombatPreviewAttemptStage.Completed or CombatPreviewAttemptStage.Failed or CombatPreviewAttemptStage.Stopped;

    private CombatPreviewAttemptReport Set(
        CombatPreviewAttemptStage stage, string reason, CombatOutcomeAssessment? outcome = null) =>
        status = new(stage, reason, outcome);

    private CombatPreviewAttemptReport Fail(string reason)
    {
        planner.Stop();
        initialHealth = null;
        initialObservation = null;
        attempt = null;
        return Set(CombatPreviewAttemptStage.Failed, reason);
    }
}
