namespace Rift.Automation.Core;

public enum CombatPhase { Idle, Acquire, AttackReady, AttackPending, Recover, Stopped }
public enum CombatIntent { None, RequestTarget, RequestBasicAttack, WaitForRecovery }

public sealed record CombatObservation(
    string SessionId, long Sequence, long AcquiredAtMs,
    bool SourceQualified, bool? PlayerAlive, bool? RecoveryRequired,
    string? TargetId, bool? TargetHostile, bool? TargetAlive,
    bool? TargetInRange, bool? BasicAttackReady);

public sealed record CombatDecision(CombatPhase Phase, CombatIntent Intent, string Reason)
{
    public bool CanExecute => false;
}

/// <summary>Pure single-owner combat preview. No game input or execution authority.</summary>
public sealed class StationaryCombatPlanner
{
    private string? session;
    private long lastSequence = -1;
    private long lastAcquired = -1;
    private long lastNowMs = -1;
    private bool stopped;
    private string? trackedTarget;
    private bool targetChangeLatched;
    private long? pendingSinceMs;
    private string? pendingTarget;
    private string? pendingCommandId;
    private bool lastObservationReady;

    public void Reset(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID required.", nameof(sessionId));
        session = sessionId;
        lastSequence = -1;
        lastAcquired = -1;
        lastNowMs = -1;
        stopped = false;
        trackedTarget = null;
        targetChangeLatched = false;
        pendingSinceMs = null;
        pendingTarget = null;
        pendingCommandId = null;
        lastObservationReady = false;
    }

    public void Stop()
    {
        stopped = true;
        pendingSinceMs = null;
        pendingTarget = null;
        pendingCommandId = null;
        lastObservationReady = false;
    }

    /// <summary>Record a hypothetical command dispatch; never issues an actual command.</summary>
    public bool BeginPreviewAttack(string commandId, string targetId, long nowMs)
    {
        if (stopped || session is null || !AcceptNow(nowMs) || pendingSinceMs is not null ||
            !lastObservationReady || targetChangeLatched ||
            string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(targetId) || trackedTarget != targetId ||
            lastAcquired < 0 || nowMs < lastAcquired || nowMs - lastAcquired > 2000)
            return false;
        lastObservationReady = false;
        pendingTarget = targetId;
        pendingCommandId = commandId;
        pendingSinceMs = nowMs;
        return true;
    }

    public bool CompletePreviewAttack(string commandId, string targetId, bool acknowledged, long nowMs)
    {
        if (stopped || session is null || !AcceptNow(nowMs) ||
            pendingSinceMs is null || pendingTarget != targetId || pendingCommandId != commandId)
            return false;
        if (nowMs < pendingSinceMs.Value || nowMs - pendingSinceMs.Value > 3000)
        {
            InvalidatePending();
            targetChangeLatched = true;
            return false;
        }
        pendingSinceMs = null;
        pendingTarget = null;
        pendingCommandId = null;
        // A command acknowledgement does not prove damage or a completed cast.
        targetChangeLatched = true;
        return acknowledged;
    }

    // A failed or out-of-order clock call invalidates readiness and requires
    // an explicit new session Reset; a later "earlier" time cannot resurrect it.
    private bool AcceptNow(long nowMs)
    {
        if (nowMs < 0 || nowMs < lastNowMs)
        {
            InvalidatePending();
            targetChangeLatched = true;
            return false;
        }
        lastNowMs = nowMs;
        return true;
    }

    private void InvalidatePending()
    {
        if (pendingSinceMs is not null)
            targetChangeLatched = true;
        pendingSinceMs = null;
        pendingTarget = null;
        pendingCommandId = null;
        lastObservationReady = false;
    }

    public CombatDecision Evaluate(CombatObservation sample, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (stopped)
            return new(CombatPhase.Stopped, CombatIntent.None, "stop-latched");
        if (session is null || sample.SessionId != session)
        {
            InvalidatePending();
            return new(CombatPhase.Idle, CombatIntent.None, "session-mismatch");
        }
        if (!AcceptNow(nowMs))
            return new(CombatPhase.Idle, CombatIntent.None, "clock-regressed");
        if (sample.AcquiredAtMs < 0 ||
            sample.AcquiredAtMs > nowMs || nowMs - sample.AcquiredAtMs > 2000 ||
            sample.Sequence <= lastSequence || sample.AcquiredAtMs < lastAcquired)
        {
            InvalidatePending();
            return new(CombatPhase.Idle, CombatIntent.None, "stale-or-reordered");
        }
        lastSequence = sample.Sequence;
        lastAcquired = sample.AcquiredAtMs;
        lastObservationReady = false;
        if (!sample.SourceQualified)
        {
            InvalidatePending();
            return new(CombatPhase.Idle, CombatIntent.None, "source-unqualified");
        }
        if (sample.PlayerAlive == false || sample.RecoveryRequired == true)
        {
            InvalidatePending();
            targetChangeLatched = true;
            return new(CombatPhase.Recover, CombatIntent.WaitForRecovery, "recovery-needed");
        }
        if (pendingSinceMs is long started)
        {
            if (sample.PlayerAlive is null || sample.RecoveryRequired is null ||
                sample.TargetHostile is not true || sample.TargetAlive is not true)
            {
                InvalidatePending();
                targetChangeLatched = true;
                return new(CombatPhase.Idle, CombatIntent.None, "pending-combat-state-lost");
            }
            if (nowMs - started > 3000)
            {
                InvalidatePending();
                targetChangeLatched = true;
                return new(CombatPhase.Idle, CombatIntent.None, "attack-timeout-reset-required");
            }
            if (sample.TargetId != pendingTarget)
            {
                InvalidatePending();
                targetChangeLatched = true;
                return new(CombatPhase.Idle, CombatIntent.None, "pending-target-changed");
            }
            return new(CombatPhase.AttackPending, CombatIntent.None, "awaiting-command-result");
        }
        if (sample.PlayerAlive is null || sample.RecoveryRequired is null)
            return new(CombatPhase.Idle, CombatIntent.None, "player-state-unknown");
        if (sample.PlayerAlive == false || sample.RecoveryRequired == true)
            return new(CombatPhase.Recover, CombatIntent.WaitForRecovery, "recovery-needed");
        if (targetChangeLatched)
            return new(CombatPhase.Idle, CombatIntent.None, "target-change-reset-required");
        if (sample.TargetId is null)
            return new(CombatPhase.Acquire, CombatIntent.RequestTarget, "target-unavailable");
        if (trackedTarget is not null && !string.Equals(trackedTarget, sample.TargetId, StringComparison.Ordinal))
        {
            targetChangeLatched = true;
            return new(CombatPhase.Idle, CombatIntent.None, "target-changed");
        }
        if (string.IsNullOrWhiteSpace(sample.TargetId) || sample.TargetAlive is null ||
            sample.TargetHostile is null || sample.TargetInRange is null)
            return new(CombatPhase.Idle, CombatIntent.None, "target-state-unknown");
        if (sample.TargetAlive == false)
            return new(CombatPhase.Acquire, CombatIntent.RequestTarget, "target-dead");
        if (sample.TargetHostile == false)
            return new(CombatPhase.Idle, CombatIntent.None, "nonhostile-target");
        if (sample.TargetInRange == false)
            return new(CombatPhase.Idle, CombatIntent.None, "out-of-range");
        if (sample.BasicAttackReady is null)
            return new(CombatPhase.Idle, CombatIntent.None, "ability-state-unknown");
        if (sample.BasicAttackReady == false)
            return new(CombatPhase.AttackReady, CombatIntent.None, "attack-not-ready");
        trackedTarget ??= sample.TargetId;
        lastObservationReady = true;
        return new(CombatPhase.AttackReady, CombatIntent.RequestBasicAttack, "preview-only");
    }
}
