namespace Rift.Automation.Core;

/// <summary>Offline evidence only. Correlates a hypothetical command window with target health readings.</summary>
public enum CombatOutcomeStatus { Invalid, NoObservedEffect, ObservedHealthDecrease, ObservedHealthIncrease }

public sealed record CombatAttemptWindow(
    string SessionId, string CommandId, string TargetId,
    long DispatchedAtMs, long AcknowledgedAtMs);

public sealed record CombatOutcomeAssessment(
    CombatOutcomeStatus Status, double? HealthDelta, string Reason)
{
    public bool ProvesCommandCausedEffect => false;
}

public static class CombatOutcomeAssessor
{
    public static CombatOutcomeAssessment Assess(
        CombatAttemptWindow attempt, CombatHealthSnapshot before, CombatHealthSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (string.IsNullOrWhiteSpace(attempt.CommandId) ||
            string.IsNullOrWhiteSpace(attempt.SessionId) ||
            string.IsNullOrWhiteSpace(attempt.TargetId) ||
            attempt.SessionId != before.SessionId || attempt.SessionId != after.SessionId ||
            attempt.TargetId != before.TargetId || attempt.TargetId != after.TargetId ||
            attempt.DispatchedAtMs < 0 || attempt.AcknowledgedAtMs < attempt.DispatchedAtMs ||
            before.AcquiredAtMs >= attempt.DispatchedAtMs ||
            attempt.DispatchedAtMs - before.AcquiredAtMs > 2000 ||
            after.AcquiredAtMs <= attempt.AcknowledgedAtMs ||
            after.AcquiredAtMs - attempt.DispatchedAtMs > 10000)
            return new(CombatOutcomeStatus.Invalid, null, "attempt-window-invalid");
        var effect = CombatEffectAssessor.Compare(before, after);
        return effect.Status switch
        {
            CombatEffectStatus.TargetHealthDecreased =>
                new(CombatOutcomeStatus.ObservedHealthDecrease, effect.HealthDelta, "damage-observed-not-attributed"),
            CombatEffectStatus.TargetHealthIncreased =>
                new(CombatOutcomeStatus.ObservedHealthIncrease, effect.HealthDelta, "healing-observed-not-attributed"),
            CombatEffectStatus.NoObservedHealthChange =>
                new(CombatOutcomeStatus.NoObservedEffect, 0, "no-health-change-observed"),
            _ => new(CombatOutcomeStatus.Invalid, null, "health-evidence-invalid")
        };
    }
}
