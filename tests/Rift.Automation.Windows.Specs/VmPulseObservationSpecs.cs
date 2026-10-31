using Rift.Automation.Windows;

internal static class VmPulseObservationSpecs
{
    private static VmTelemetrySnapshot Fresh(
        string lab = "lab1", string stream = "source-a", long outer = 10,
        long sequence = 8, double clock = 20, long epoch = 2, double x = 10) =>
        new(lab, "fresh_observed", "pinned", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 20, outer, sequence, epoch, 3300,
            x, 15, 20, "whole-readable-private", stream, clock);

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Pulse preflight rejects wrong lab, incomplete identity and nonfinite values", () =>
        {
            VmTelemetrySnapshot good = Fresh();
            check(VmPulseObservationChecks.IsUsable("lab1", good), "valid baseline");
            var invalid = new[]
            {
                good with { Lab = "lab2" },
                good with { StreamIdentity = "" },
                good with { StreamIdentity = null },
                good with { GameProcessId = 0 },
                good with { AddonEpoch = 0 },
                good with { OuterSequence = 0 },
                good with { AddonSequence = 0 },
                good with { AddonClock = double.NaN },
                good with { AddonClock = double.NegativeInfinity },
                good with { AddonClock = -1 },
                good with { X = double.NaN },
                good with { Y = double.PositiveInfinity },
                good with { Z = double.NegativeInfinity },
                good with { Status = "unavailable" },
                good with { X = null }
            };
            foreach (VmTelemetrySnapshot bad in invalid)
                check(!VmPulseObservationChecks.IsUsable("lab1", bad), "bad source must be rejected");
            check(!VmPulseObservationChecks.IsUsable("unknown", good), "unknown target lab");
            check(!VmPulseObservationChecks.IsUsable("lab1", null), "null source");
        });

        test("Pulse pre-send reread permits an exact repeat, redelivery or complete advance", () =>
        {
            VmTelemetrySnapshot baseline = Fresh();
            check(VmPulseObservationChecks.CanRevalidate("lab1", baseline, baseline), "exact repeat");
            check(VmPulseObservationChecks.CanRevalidate("lab1", baseline,
                baseline with { OuterSequence = 15 }), "receiver redelivery");
            check(VmPulseObservationChecks.CanRevalidate("lab1", baseline,
                baseline with { OuterSequence = 11, AddonSequence = 9, AddonClock = 21,
                    SourceScope = "candidate-regions" }), "new producer frame");
        });

        test("Pulse pre-send reread blocks lab, stream, process and epoch substitution", () =>
        {
            VmTelemetrySnapshot baseline = Fresh();
            VmTelemetrySnapshot[] swapped =
            {
                baseline with { Lab = "lab2" },
                baseline with { StreamIdentity = "source-b" },
                baseline with { GameProcessId = 4400 },
                baseline with { AddonEpoch = 3 },
                baseline with { Status = "unavailable" }
            };
            foreach (VmTelemetrySnapshot candidate in swapped)
                check(!VmPulseObservationChecks.CanRevalidate("lab1", baseline, candidate),
                    "session replacement must not inherit user review");
        });

        test("Pulse pre-send reread rejects rollback, partial advance and coordinate conflicts", () =>
        {
            VmTelemetrySnapshot baseline = Fresh();
            VmTelemetrySnapshot[] bad =
            {
                baseline with { OuterSequence = 9 },
                baseline with { OuterSequence = 11, AddonSequence = 7 },
                baseline with { OuterSequence = 11, AddonClock = 21 },
                baseline with { OuterSequence = 11, AddonSequence = 9, AddonClock = 20 },
                baseline with { OuterSequence = 11, AddonSequence = 9, AddonClock = 19 },
                baseline with { OuterSequence = 11, X = 11 },
                baseline with { X = 11 },
                baseline with { OuterSequence = 11, AddonSequence = 9, AddonClock = double.NaN }
            };
            foreach (VmTelemetrySnapshot candidate in bad)
                check(!VmPulseObservationChecks.CanRevalidate("lab1", baseline, candidate),
                    "contradictory frame must not pass");
        });

        test("Pulse confirmation rereads source only after Yes and uses refreshed baseline", () =>
        {
            var events = new List<string>();
            int reads = 0;
            VmTelemetrySnapshot first = Fresh(), latest = Fresh(outer: 11, sequence: 9, clock: 21);
            VmPulseSourceReview review = VmPulseObservationChecks.CheckAcrossConfirmation(
                "lab1",
                () => { events.Add("read"); reads++; return reads == 1 ? first : latest; },
                () => { events.Add("confirm"); return true; },
                () => false);
            check(events.SequenceEqual(new[] { "read", "confirm", "read" }), "modal ordering");
            check(review.Disposition == VmPulseReviewDisposition.SourceRechecked &&
                review.BeforeSend == latest, "post-confirmation baseline replaces earlier frame");
            check(!VmPulseObservationChecks.IsNewerMatching("lab1", review.BeforeSend, latest),
                "source advancement during confirmation cannot be called feedback");
        });

        test("Pulse confirmation rejects changed source before any fake send", () =>
        {
            int reads = 0, sends = 0;
            VmTelemetrySnapshot current = Fresh();
            VmPulseSourceReview review = VmPulseObservationChecks.CheckAcrossConfirmation(
                "lab1",
                () => { reads++; return current; },
                () => { current = Fresh(stream: "source-b"); return true; },
                () => false);
            if (review.Disposition == VmPulseReviewDisposition.SourceRechecked) sends++;
            check(reads == 2 && sends == 0 &&
                review.Disposition == VmPulseReviewDisposition.SourceChanged &&
                review.BeforeSend is null, "source swap aborts after Yes, no send");
        });

        test("Pulse confirmation No, closing and initial wrong lab never request a send", () =>
        {
            int reads = 0, confirms = 0;
            VmPulseSourceReview declined = VmPulseObservationChecks.CheckAcrossConfirmation(
                "lab1", () => { reads++; return Fresh(); },
                () => { confirms++; return false; }, () => false);
            check(declined.Disposition == VmPulseReviewDisposition.Cancelled &&
                reads == 1 && confirms == 1, "No needs no second read");

            reads = confirms = 0;
            VmPulseSourceReview wrongLab = VmPulseObservationChecks.CheckAcrossConfirmation(
                "lab1", () => { reads++; return Fresh(lab: "lab2"); },
                () => { confirms++; return true; }, () => false);
            check(wrongLab.Disposition == VmPulseReviewDisposition.PrecheckFailed &&
                reads == 1 && confirms == 0, "wrong lab cannot prompt");

            reads = confirms = 0;
            VmPulseSourceReview closing = VmPulseObservationChecks.CheckAcrossConfirmation(
                "lab1", () => { reads++; return Fresh(); },
                () => { confirms++; return true; }, () => true);
            check(closing.Disposition == VmPulseReviewDisposition.Cancelled &&
                reads == 1 && confirms == 0, "closing before modal");
        });

        test("Pulse feedback requires the exact stream and three strict advances", () =>
        {
            VmTelemetrySnapshot before = Fresh();
            VmTelemetrySnapshot later = Fresh(outer: 11, sequence: 9, clock: 21);
            check(VmPulseObservationChecks.IsNewerMatching("lab1", before, later),
                "newer source observation");
            check(VmPulseObservationChecks.IsNewerMatching("lab1", before,
                later with { SourceScope = "candidate-regions", X = before.X }),
                "scope switch and zero displacement remain valid observations");
            var wrong = new[]
            {
                later with { Lab = "lab2" },
                later with { StreamIdentity = "source-b" },
                later with { GameProcessId = 4400 },
                later with { AddonEpoch = 3 },
                later with { OuterSequence = 10 },
                later with { AddonSequence = 8 },
                later with { AddonClock = 20 },
                later with { AddonClock = double.PositiveInfinity },
                later with { Z = double.NaN },
                later with { Status = "unavailable" }
            };
            foreach (VmTelemetrySnapshot candidate in wrong)
                check(!VmPulseObservationChecks.IsNewerMatching("lab1", before, candidate),
                    "unmatched or nonadvancing feedback rejected");
            check(!VmPulseObservationChecks.IsNewerMatching("lab1", before, before),
                "repeat is not new feedback");
        });

        test("Pulse feedback pairwise comparison rejects different source identity", () =>
        {
            VmTelemetrySnapshot before = Fresh();
            VmTelemetrySnapshot swapped = Fresh(stream: "source-b", outer: 12,
                sequence: 10, clock: 22);
            VmTelemetrySnapshot recovered = Fresh(outer: 13, sequence: 11, clock: 23);
            check(!VmPulseObservationChecks.IsNewerMatching("lab1", before, swapped) &&
                VmPulseObservationChecks.IsNewerMatching("lab1", before, recovered),
                "pairwise comparison requires matching binding");
        });
        test("Pulse feedback retains redelivery high watermark before accepting progression", () =>
        {
            VmTelemetrySnapshot before = Fresh();
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(before, 0);
            var redelivered = watch.Observe(before with { OuterSequence = 100 }, 400);
            check(redelivered.State == "repeated" &&
                !VmPulseObservationChecks.IsAcceptedFeedback("lab1", before, redelivered),
                "redelivery is not new feedback");
            VmTelemetrySnapshot rollback = Fresh(outer: 11, sequence: 9, clock: 21);
            check(VmPulseObservationChecks.IsNewerMatching("lab1", before, rollback),
                "pairwise rule alone would misclassify rollback");
            var rejected = watch.Observe(rollback, 800);
            check(rejected.State == "reordered" && rejected.Current is null &&
                !VmPulseObservationChecks.IsAcceptedFeedback("lab1", before, rejected),
                "tracked high watermark blocks rollback");
            var recovered = watch.Observe(Fresh(outer: 101, sequence: 9, clock: 21), 1200);
            check(recovered.State == "recovered" &&
                VmPulseObservationChecks.IsAcceptedFeedback("lab1", before, recovered),
                "only an actual new frame above retained watermark qualifies");
        });
        test("Pulse feedback cannot continue after observed stream replacement", () =>
        {
            VmTelemetrySnapshot before = Fresh();
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(before, 0);
            var replaced = watch.Observe(Fresh(stream: "source-b", outer: 11,
                sequence: 9, clock: 21), 400);
            check(replaced.SessionChanges == 1 &&
                !VmPulseObservationChecks.IsAcceptedFeedback("lab1", before, replaced),
                "different binding cannot qualify");
            var cameBack = watch.Observe(Fresh(outer: 12, sequence: 10, clock: 22), 800);
            check(cameBack.SessionChanges == 2 &&
                !VmPulseObservationChecks.IsAcceptedFeedback("lab1", before, cameBack),
                "a returning original fingerprint cannot erase a witnessed source switch");
        });
    }
}
