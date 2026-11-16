namespace Rift.Automation.Core;

/// <summary>Historical evidence comparison only; not proof that an input caused damage.</summary>
public enum CombatEffectStatus
{
    Unknown, InvalidEvidence, NoObservedHealthChange, TargetHealthDecreased, TargetHealthIncreased
}

public sealed record CombatHealthSnapshot(
    string SessionId, string TargetId, long Sequence, long AcquiredAtMs,
    bool SourceQualified, double? CurrentHealth, double? MaximumHealth);

public sealed record CombatEffectAssessment(CombatEffectStatus Status, double? HealthDelta, string Reason)
{
    public bool ProvesAttackSucceeded => false;
}

public static class CombatEffectAssessor
{
    public static CombatEffectAssessment Compare(CombatHealthSnapshot before, CombatHealthSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (string.IsNullOrWhiteSpace(before.SessionId) || string.IsNullOrWhiteSpace(before.TargetId) ||
            before.SessionId != after.SessionId || before.TargetId != after.TargetId ||
            !before.SourceQualified || !after.SourceQualified ||
            before.Sequence < 0 || after.Sequence <= before.Sequence ||
            before.AcquiredAtMs < 0 || after.AcquiredAtMs <= before.AcquiredAtMs)
            return new(CombatEffectStatus.InvalidEvidence, null, "identity-or-continuity-invalid");
        if (before.CurrentHealth is not double oldHealth || after.CurrentHealth is not double newHealth ||
            before.MaximumHealth is not double oldMax || after.MaximumHealth is not double newMax ||
            !double.IsFinite(oldHealth) || !double.IsFinite(newHealth) ||
            !double.IsFinite(oldMax) || !double.IsFinite(newMax) ||
            oldMax <= 0 || newMax <= 0 || oldMax != newMax ||
            oldHealth < 0 || newHealth < 0 || oldHealth > oldMax || newHealth > newMax)
            return new(CombatEffectStatus.InvalidEvidence, null, "health-unavailable-or-invalid");
        double delta = newHealth - oldHealth;
        if (delta < 0)
            return new(CombatEffectStatus.TargetHealthDecreased, delta, "observed-health-decrease-not-attributed");
        if (delta > 0)
            return new(CombatEffectStatus.TargetHealthIncreased, delta, "observed-health-increase");
        return new(CombatEffectStatus.NoObservedHealthChange, 0, "no-observed-change");
    }
}
