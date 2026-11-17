using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class CombatAttemptPreviewSpecs
{
    internal static void Run()
    {
        CombatObservation Ready(long sequence = 1, long time = 100, bool qualified = true) =>
            new("session", sequence, time, qualified, true, false,
                "enemy", true, true, true, true);
        CombatHealthSnapshot Health(long sequence = 1, long time = 100, double hp = 80,
            bool qualified = true) =>
            new("session", "enemy", sequence, time, qualified, hp, 100);
        StationaryCombatAttemptPreview NewReady()
        {
            var preview = new StationaryCombatAttemptPreview("session");
            Check(preview.ObserveReadiness(Ready(), Health(), 100).Stage ==
                CombatPreviewAttemptStage.Ready, "matched synthetic baseline");
            return preview;
        }
        StationaryCombatAttemptPreview NewPending()
        {
            var preview = NewReady();
            Check(preview.BeginPreviewAttack("cmd", 200).Stage ==
                CombatPreviewAttemptStage.AwaitingAcknowledgment, "hypothetical begin");
            return preview;
        }
        StationaryCombatAttemptPreview NewAcknowledged()
        {
            var preview = NewPending();
            Check(preview.CompletePreviewAttack("cmd", "enemy", true, 250).Stage ==
                CombatPreviewAttemptStage.AwaitingHealth, "hypothetical ack");
            return preview;
        }

        Test("combat one-attempt preview composes pending, ack and historical health comparison", () =>
        {
            var preview = NewPending();
            Check(preview.ObservePending(Ready(2, 220), Health(2, 220, 78), 220).Stage ==
                CombatPreviewAttemptStage.AwaitingAcknowledgment, "pending continuity");
            Check(preview.CompletePreviewAttack("cmd", "enemy", true, 250).Stage ==
                CombatPreviewAttemptStage.AwaitingHealth, "ack");
            var result = preview.ObservePostAckHealth(Ready(3, 300), Health(3, 300, 65), 300);
            Check(result is { Stage: CombatPreviewAttemptStage.Completed,
                Outcome: { Status: CombatOutcomeStatus.ObservedHealthDecrease, HealthDelta: -15 } } &&
                !result.CanExecute && !result.Outcome!.ProvesCommandCausedEffect,
                "observation is neither executable nor command-attributed");
            Check(ReferenceEquals(result, preview.BeginPreviewAttack("again", 301)) &&
                ReferenceEquals(result, preview.CompletePreviewAttack("cmd", "enemy", true, 301)) &&
                ReferenceEquals(result, preview.ObservePostAckHealth(Ready(4, 400), Health(4, 400, 1), 400)) &&
                ReferenceEquals(result, preview.Stop()), "terminal outcome immutable and one-shot");
        });

        Test("combat one-attempt preview requires matched qualified baseline", () =>
        {
            void Reject(CombatObservation observation, CombatHealthSnapshot health)
            {
                var preview = new StationaryCombatAttemptPreview("session");
                Check(preview.ObserveReadiness(observation, health, 100).Stage ==
                    CombatPreviewAttemptStage.Failed, "baseline rejected");
                Check(preview.BeginPreviewAttack("cmd", 200).Stage ==
                    CombatPreviewAttemptStage.Failed, "invalid baseline cannot dispatch");
            }
            Reject(Ready(qualified: false), Health());
            Reject(Ready(), Health(qualified: false));
            Reject(Ready(), Health(sequence: 2));
            Reject(Ready(), Health(time: 99));
            Reject(Ready() with { SessionId = "other" }, Health());
            Reject(Ready() with { TargetId = "other" }, Health());
            Reject(Ready(), Health(hp: 0));
            Reject(Ready(), Health(hp: double.NaN));
            Reject(Ready(), Health() with { MaximumHealth = 0 });
            Reject(Ready(time: 101), Health(time: 101));
        });

        Test("combat one-attempt preview requires strictly later bounded dispatch", () =>
        {
            var same = NewReady();
            Check(same.BeginPreviewAttack("cmd", 100).Stage == CombatPreviewAttemptStage.Failed,
                "same-ms baseline cannot anchor outcome assessor");
            var later = NewReady();
            Check(later.BeginPreviewAttack("cmd", 101).Stage ==
                CombatPreviewAttemptStage.AwaitingAcknowledgment, "one-ms strict advance");
            var edge = NewReady();
            Check(edge.BeginPreviewAttack("cmd", 2100).Stage ==
                CombatPreviewAttemptStage.AwaitingAcknowledgment, "2000-ms inclusive readiness");
            var expired = NewReady();
            Check(expired.BeginPreviewAttack("cmd", 2101).Stage ==
                CombatPreviewAttemptStage.Failed, "readiness expired at 2001 ms");
        });

        Test("combat one-attempt preview preserves exact ack and effect deadlines", () =>
        {
            var edge = NewPending();
            Check(edge.CompletePreviewAttack("cmd", "enemy", true, 3200).Stage ==
                CombatPreviewAttemptStage.AwaitingHealth, "3000-ms ack included");
            Check(edge.ObservePostAckHealth(Ready(2, 3201), Health(2, 3201, 65), 3201).Stage ==
                CombatPreviewAttemptStage.Completed, "valid post-ack observation");
            var late = NewPending();
            Check(late.CompletePreviewAttack("cmd", "enemy", true, 3201).Stage ==
                CombatPreviewAttemptStage.Failed, "3001-ms ack rejected");
            var observationEdge = NewAcknowledged();
            Check(observationEdge.ObservePostAckHealth(Ready(2, 10200), Health(2, 10200, 65), 10200).Stage ==
                CombatPreviewAttemptStage.Completed, "10000-ms observation window included");
            var observationLate = NewAcknowledged();
            Check(observationLate.ObservePostAckHealth(Ready(2, 10201), Health(2, 10201, 65), 10201).Stage ==
                CombatPreviewAttemptStage.Failed, "10001-ms observation window rejected");
            var lateDelivery = NewAcknowledged();
            var delayed = lateDelivery.ObservePostAckHealth(Ready(2, 10200),
                Health(2, 10200, 65), 11000);
            Check(delayed.Stage == CombatPreviewAttemptStage.Failed &&
                delayed.Reason == "observation-timeout",
                "late delivery cannot revive an on-time acquired sample without a timer tick");
        });

        Test("combat one-attempt preview source loss, target swap and reordered post-ack input fail closed", () =>
        {
            var targetSwitch = NewPending();
            Check(targetSwitch.ObservePending(Ready(2, 220) with { TargetId = "other" },
                Health(2, 220, 78) with { TargetId = "other" }, 220).Stage ==
                CombatPreviewAttemptStage.Failed, "target identity change latches");
            Check(targetSwitch.ObservePending(Ready(3, 230), Health(3, 230, 78), 230).Stage ==
                CombatPreviewAttemptStage.Failed, "A-B-A cannot revive");

            var lost = NewPending();
            Check(lost.ObservePending(Ready(2, 220, qualified: false),
                Health(2, 220, 78), 220).Stage == CombatPreviewAttemptStage.Failed,
                "unqualified pending state fails");
            var contradictory = NewPending();
            Check(contradictory.ObservePending(Ready(2, 220), Health(2, 220, 0), 220).Stage ==
                CombatPreviewAttemptStage.Failed, "pre-ack alive target cannot simultaneously have zero HP");
            var recovering = NewPending();
            Check(recovering.ObservePending(Ready(2, 220) with { PlayerAlive = false },
                Health(2, 220, 78), 220).Stage == CombatPreviewAttemptStage.Failed,
                "player recovery aborts");

            var reordered = NewPending();
            Check(reordered.ObservePending(Ready(2, 220), Health(2, 220, 78), 220).Stage ==
                CombatPreviewAttemptStage.AwaitingAcknowledgment, "pending newer producer");
            reordered.CompletePreviewAttack("cmd", "enemy", true, 250);
            Check(reordered.ObservePostAckHealth(Ready(2, 300), Health(2, 300, 65), 300).Stage ==
                CombatPreviewAttemptStage.Failed, "old sequence cannot be post-ack result");
            Check(reordered.ObservePostAckHealth(Ready(3, 350), Health(3, 350, 65), 350).Stage ==
                CombatPreviewAttemptStage.Failed, "failed attempt cannot be repaired");
        });

        Test("combat one-attempt preview stop, negative ack and clock rollback are terminal", () =>
        {
            var stopped = NewPending();
            Check(stopped.Stop().Stage == CombatPreviewAttemptStage.Stopped &&
                stopped.CompletePreviewAttack("cmd", "enemy", true, 250).Stage ==
                    CombatPreviewAttemptStage.Stopped, "stop latches");
            var negative = NewPending();
            Check(negative.CompletePreviewAttack("cmd", "enemy", false, 250).Stage ==
                CombatPreviewAttemptStage.Failed, "negative ack fails");
            var wrong = NewPending();
            Check(wrong.CompletePreviewAttack("other", "enemy", true, 250).Stage ==
                CombatPreviewAttemptStage.Failed, "wrong command cannot be replayed");
            var backwards = NewAcknowledged();
            Check(backwards.ObservePostAckHealth(Ready(2, 300), Health(2, 300, 65), 249).Stage ==
                CombatPreviewAttemptStage.Failed, "clock regression latches");
        });

        Test("combat one-attempt preview no-telemetry timeouts and first invalid health fail closed", () =>
        {
            var ready = NewReady();
            Check(ready.AdvanceTime(2100).Stage == CombatPreviewAttemptStage.Ready &&
                ready.AdvanceTime(2101).Stage == CombatPreviewAttemptStage.Failed,
                "readiness deadline");
            var ack = NewPending();
            Check(ack.AdvanceTime(3200).Stage == CombatPreviewAttemptStage.AwaitingAcknowledgment &&
                ack.AdvanceTime(3201).Stage == CombatPreviewAttemptStage.Failed,
                "ack deadline");
            var health = NewAcknowledged();
            Check(health.AdvanceTime(10200).Stage == CombatPreviewAttemptStage.AwaitingHealth &&
                health.AdvanceTime(10201).Stage == CombatPreviewAttemptStage.Failed,
                "post-ack deadline");
            var badFirst = NewAcknowledged();
            Check(badFirst.ObservePostAckHealth(Ready(2, 300), Health(2, 300, 65, qualified: false), 300).Stage ==
                CombatPreviewAttemptStage.Failed, "unqualified first health invalidates");
            Check(badFirst.ObservePostAckHealth(Ready(3, 350), Health(3, 350, 60), 350).Stage ==
                CombatPreviewAttemptStage.Failed, "no wait for favorable later evidence");
        });

        Test("combat one-attempt preview accepts target reaching zero without attribution", () =>
        {
            var preview = NewAcknowledged();
            var dead = Ready(2, 300) with { TargetAlive = false };
            var result = preview.ObservePostAckHealth(dead, Health(2, 300, 0), 300);
            Check(result is { Stage: CombatPreviewAttemptStage.Completed,
                Outcome: { Status: CombatOutcomeStatus.ObservedHealthDecrease, HealthDelta: -80 } } &&
                !result.CanExecute && !result.Outcome!.ProvesCommandCausedEffect,
                "zero HP is a valid historical observation, not attack proof");
        });
    }
}
