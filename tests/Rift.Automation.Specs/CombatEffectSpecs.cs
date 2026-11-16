using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class CombatEffectSpecs
{
    internal static void Run()
    {
        CombatHealthSnapshot Before() => new("a", "enemy", 1, 100, true, 80, 100);
        Test("combat effect reports damage as observation not attribution", () =>
        {
            var result = CombatEffectAssessor.Compare(Before(), Before() with { Sequence = 2, AcquiredAtMs = 200, CurrentHealth = 65 });
            Check(result.Status == CombatEffectStatus.TargetHealthDecreased && result.HealthDelta == -15 && !result.ProvesAttackSucceeded, "damage evidence");
        });
        Test("combat effect rejects swapped target or session", () =>
        {
            var first = Before();
            Check(CombatEffectAssessor.Compare(first, first with { TargetId = "other", Sequence = 2, AcquiredAtMs = 200 }).Status == CombatEffectStatus.InvalidEvidence, "target");
            Check(CombatEffectAssessor.Compare(first, first with { SessionId = "b", Sequence = 2, AcquiredAtMs = 200 }).Status == CombatEffectStatus.InvalidEvidence, "session");
        });
        Test("combat effect rejects stale, missing and invalid health", () =>
        {
            var first = Before();
            Check(CombatEffectAssessor.Compare(first, first with { Sequence = 1, AcquiredAtMs = 200 }).Status == CombatEffectStatus.InvalidEvidence, "sequence");
            Check(CombatEffectAssessor.Compare(first, first with { Sequence = 2, AcquiredAtMs = 200, CurrentHealth = null }).Status == CombatEffectStatus.InvalidEvidence, "missing");
            Check(CombatEffectAssessor.Compare(first, first with { Sequence = 2, AcquiredAtMs = 200, CurrentHealth = double.NaN }).Status == CombatEffectStatus.InvalidEvidence, "nan");
        });
        Test("combat effect distinguishes unchanged and increased health", () =>
        {
            var first = Before();
            Check(CombatEffectAssessor.Compare(first, first with { Sequence = 2, AcquiredAtMs = 200 }).Status == CombatEffectStatus.NoObservedHealthChange, "unchanged");
            Check(CombatEffectAssessor.Compare(first, first with { Sequence = 2, AcquiredAtMs = 200, CurrentHealth = 90 }).Status == CombatEffectStatus.TargetHealthIncreased, "healed");
        });
    }
}
