using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class CombatOutcomeSpecs
{
    internal static void Run()
    {
        CombatAttemptWindow Attempt() => new("session", "cmd-1", "enemy", 200, 250);
        CombatHealthSnapshot Before() => new("session", "enemy", 1, 150, true, 80, 100);
        CombatHealthSnapshot After() => new("session", "enemy", 2, 300, true, 65, 100);
        Test("combat outcome correlates window but does not attribute damage", () =>
        {
            var result = CombatOutcomeAssessor.Assess(Attempt(), Before(), After());
            Check(result.Status == CombatOutcomeStatus.ObservedHealthDecrease && result.HealthDelta == -15 &&
                !result.ProvesCommandCausedEffect, "observation only");
        });
        Test("combat outcome rejects swapped target and session", () =>
        {
            Check(CombatOutcomeAssessor.Assess(Attempt() with { TargetId = "other" }, Before(), After()).Status == CombatOutcomeStatus.Invalid, "target");
            Check(CombatOutcomeAssessor.Assess(Attempt() with { SessionId = "other" }, Before(), After()).Status == CombatOutcomeStatus.Invalid, "session");
        });
        Test("combat outcome rejects pre-dispatch and pre-ack samples", () =>
        {
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before(), After() with { AcquiredAtMs = 190 }).Status == CombatOutcomeStatus.Invalid, "before dispatch");
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before(), After() with { AcquiredAtMs = 240 }).Status == CombatOutcomeStatus.Invalid, "before ack");
        });
        Test("combat outcome requires strict pre-dispatch baseline and post-ack observation", () =>
        {
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before() with { AcquiredAtMs = 200 }, After()).Status == CombatOutcomeStatus.Invalid, "simultaneous baseline");
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before() with { AcquiredAtMs = 0 }, After()).Status == CombatOutcomeStatus.ObservedHealthDecrease, "recent baseline");
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before(), After() with { AcquiredAtMs = 250 }).Status == CombatOutcomeStatus.Invalid, "simultaneous ack");
            Check(CombatOutcomeAssessor.Assess(Attempt() with { DispatchedAtMs = 3000, AcknowledgedAtMs = 3050 }, Before(), After() with { AcquiredAtMs = 3100 }).Status == CombatOutcomeStatus.Invalid, "old baseline");
        });
        Test("combat outcome rejects oversized windows and invalid health", () =>
        {
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before(), After() with { AcquiredAtMs = 11000 }).Status == CombatOutcomeStatus.Invalid, "window");
            Check(CombatOutcomeAssessor.Assess(Attempt(), Before(), After() with { CurrentHealth = double.NaN }).Status == CombatOutcomeStatus.Invalid, "health");
        });
    }
}
