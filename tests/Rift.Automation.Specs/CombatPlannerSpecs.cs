using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class CombatPlannerSpecs
{
    internal static void Run()
    {
        CombatObservation Sample(long sequence = 1, long time = 100, bool qualified = true,
            bool hostile = true, bool inRange = true) =>
            new("a", sequence, time, qualified, true, false, "target-1", hostile, true, inRange, true);
        Test("combat preview proposes but never executes", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            var d = p.Evaluate(Sample(), 100);
            Check(d.Intent == CombatIntent.RequestBasicAttack && !d.CanExecute, "preview");
        });
        Test("combat preview rejects duplicate and stale", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.Evaluate(Sample(), 100).Intent == CombatIntent.None, "duplicate");
            Check(p.Evaluate(Sample(2), 2101).Intent == CombatIntent.None, "stale");
        });
        Test("combat preview rejects unqualified and friendly", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            Check(p.Evaluate(Sample(1, qualified: false), 100).Intent == CombatIntent.None, "unqualified");
            Check(p.Evaluate(Sample(2, hostile: false), 100).Intent == CombatIntent.None, "friendly");
        });
        Test("combat preview STOP latched", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Stop();
            Check(p.Evaluate(Sample(), 100).Phase == CombatPhase.Stopped, "stop");
            p.Reset("a");
            Check(p.Evaluate(Sample(), 100).Intent == CombatIntent.RequestBasicAttack, "reset");
        });
        Test("combat preview rejects unknown player, target and ability state", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            Check(p.Evaluate(Sample() with { PlayerAlive = null }, 100).Reason == "player-state-unknown", "player");
            Check(p.Evaluate(Sample(2) with { TargetHostile = null }, 100).Reason == "target-state-unknown", "target");
            Check(p.Evaluate(Sample(3) with { BasicAttackReady = null }, 100).Reason == "ability-state-unknown", "ability");
        });
        Test("combat preview distinguishes absent from dead target", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            Check(p.Evaluate(Sample() with { TargetId = null }, 100).Intent == CombatIntent.RequestTarget, "absent");
            Check(p.Evaluate(Sample(2) with { TargetAlive = false }, 100).Reason == "target-dead", "dead");
        });
        Test("combat preview latches target switch until reset", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            Check(p.Evaluate(Sample(), 100).Intent == CombatIntent.RequestBasicAttack, "initial");
            Check(p.Evaluate(Sample(2) with { TargetId = "target-2" }, 100).Reason == "target-changed", "switch");
            Check(p.Evaluate(Sample(3) with { TargetId = "target-1" }, 100).Intent == CombatIntent.None, "latched");
            p.Reset("a");
            Check(p.Evaluate(Sample(), 100).Intent == CombatIntent.RequestBasicAttack, "reset");
        });
        Test("combat preview target loss does not clear change latch", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.Evaluate(Sample(2) with { TargetId = "other" }, 100).Reason == "target-changed", "changed");
            Check(p.Evaluate(Sample(3) with { TargetId = null }, 100).Intent == CombatIntent.None, "no bypass via null");
            Check(p.Evaluate(Sample(4), 100).Intent == CombatIntent.None, "still latched");
        });
        Test("combat preview pending attack suppresses repeated command", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin");
            Check(p.Evaluate(Sample(2, 200), 200).Phase == CombatPhase.AttackPending, "pending");
            Check(!p.BeginPreviewAttack("command-1", "target-1", 200), "no repeat");
            Check(!p.CompletePreviewAttack("wrong-command", "target-1", true, 250), "wrong command rejected");
            Check(p.Evaluate(Sample(3, 250), 250).Phase == CombatPhase.AttackPending, "wrong command leaves pending");
            Check(p.CompletePreviewAttack("command-1", "target-1", true, 300), "complete");
            Check(p.Evaluate(Sample(4, 300), 300).Intent == CombatIntent.None, "ack is not combat proof");
            Check(!p.BeginPreviewAttack("command-1", "target-1", 300), "no repeat after ack");
            p.Reset("a");
            Check(p.Evaluate(Sample(), 100).Intent == CombatIntent.RequestBasicAttack, "explicit reset");
        });
        Test("combat preview does not dispatch from stale readiness", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            p.Evaluate(Sample(2, qualified: false), 200);
            Check(!p.BeginPreviewAttack("command-1", "target-1", 200), "unqualified later reading");
        });
        Test("combat preview pending target swap and timeout fail closed", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin");
            Check(p.Evaluate(Sample(2, 200) with { TargetId = "other" }, 200).Reason == "pending-target-changed", "swap");
            Check(!p.CompletePreviewAttack("command-1", "target-1", true, 300), "late result rejected");
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin again");
            Check(p.Evaluate(Sample(2, 100), 3101).Reason == "stale-or-reordered", "stale source rejected");
            Check(!p.CompletePreviewAttack("command-1", "target-1", true, 300), "late ack rejected after stale input");
            Check(p.Evaluate(Sample(3, 3200), 3200).Reason == "target-change-reset-required", "fail closed");
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin timeout case");
            Check(p.Evaluate(Sample(2, 3200), 3200).Reason == "attack-timeout-reset-required", "timeout");
        });
        Test("combat preview rejects late ack after source qualification loss", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin");
            Check(p.Evaluate(Sample(2, 200, qualified: false), 200).Reason == "source-unqualified", "fault");
            Check(!p.CompletePreviewAttack("command-1", "target-1", true, 300), "late ack");
            Check(p.Evaluate(Sample(3, 300), 300).Intent == CombatIntent.None, "latch");
        });
        Test("combat preview rejects delayed acknowledgment without another sample", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin");
            Check(!p.CompletePreviewAttack("command-1", "target-1", true, 3101), "late ack");
            Check(p.Evaluate(Sample(2, 3200), 3200).Intent == CombatIntent.None, "latched");
        });
        Test("combat preview recovery clears pending command identity", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("a");
            p.Evaluate(Sample(), 100);
            Check(p.BeginPreviewAttack("command-1", "target-1", 100), "begin");
            Check(p.Evaluate(Sample(2, 200) with { PlayerAlive = false }, 200).Phase == CombatPhase.Recover, "recover");
            Check(!p.CompletePreviewAttack("command-1", "target-1", true, 250), "late ack");
            Check(p.Evaluate(Sample(3, 300), 300).Intent == CombatIntent.None, "latch");
        });
        Test("combat preview rejects session and range", () =>
        {
            var p = new StationaryCombatPlanner();
            p.Reset("b");
            Check(p.Evaluate(Sample(), 100).Intent == CombatIntent.None, "session");
            p.Reset("a");
            Check(p.Evaluate(Sample(inRange: false), 100).Intent == CombatIntent.None, "range");
        });
    }
}
