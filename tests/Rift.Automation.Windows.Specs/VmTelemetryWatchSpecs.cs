using Rift.Automation.Windows;

internal static class VmTelemetryWatchSpecs
{
    private static VmTelemetrySnapshot Fresh(
        long outer = 12, long seq = 8, double clock = 20,
        string stream = "stream-a", string lab = "lab1",
        double x = 10, long epoch = 2) =>
        new(lab, "fresh_observed", "pinned", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 30, outer, seq, epoch,
            3300, x, 15, 20, "candidate-regions", stream, clock);

    private static VmTelemetrySnapshot Missing() =>
        new("lab1", "unavailable", "reader_not_running", DateTimeOffset.UnixEpoch,
            null, null, null, null, null, null, null, null, null);

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Live monitor distinguishes first, advancing and exact repeat", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            var first = watch.Observe(Fresh(), 0);
            var second = watch.Observe(Fresh(13, 9, 20.5), 500);
            var repeat = watch.Observe(Fresh(13, 9, 20.5), 1100);
            check(first.State == "first_sample" && first.NewSamples == 1, "first");
            check(second.State == "advancing" && second.NewSamples == 2 &&
                second.SinceLastAdvanceMs == 0, "advance");
            check(repeat.State == "repeated" && repeat.Repeated == 1 &&
                repeat.NewSamples == 2 && repeat.SinceLastAdvanceMs == 600,
                "repeat does not advance");
            check(repeat.LongestNoAdvanceMs == 600 && !repeat.CanAutomate,
                "diagnostic only");
        });
        test("Stale observation removes current XYZ and recovery needs a new sequence", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var lost = watch.Observe(Missing(), 500);
            var old = watch.Observe(Fresh(), 1000);
            var newData = watch.Observe(Fresh(13, 9, 21), 2000);
            check(lost.State == "unavailable" && !lost.HasCurrentPosition &&
                lost.Unavailable == 1, "availability loss");
            check(old.State == "awaiting_advance" && !old.HasCurrentPosition &&
                old.NewSamples == 1 && old.SinceLastAdvanceMs == 1000,
                "old sample cannot restore current position");
            check(newData.State == "recovered" && newData.NewSamples == 2 &&
                newData.LongestNoAdvanceMs == 2000, "new sample after gap");
        });
        test("Missing and conflicting events cannot preserve current coordinates", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var bad = watch.Observe(Fresh(13, 7, 20.5), 500);
            check(bad.State == "reordered" && !bad.HasCurrentPosition &&
                bad.Reordered == 1, "reordered");
            var recovered = watch.Observe(Fresh(14, 10, 21), 1000);
            check(recovered.State == "recovered" && recovered.Recovered == 1 &&
                recovered.HasCurrentPosition, "recovery");
        });
        test("Accepted redelivery retains the outer transport watermark", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var repeat = watch.Observe(Fresh(100, 8, 20), 500);
            check(repeat.State == "repeated" && repeat.Repeated == 1, "accepted repeat");
            var rollback = watch.Observe(Fresh(13, 9, 21), 1000);
            check(rollback.State == "reordered" && rollback.Current is null &&
                rollback.NewSamples == 1, "transport rollback must not revive current");
            var recovered = watch.Observe(Fresh(101, 9, 21), 1500);
            check(recovered.State == "recovered" && recovered.NewSamples == 2 &&
                recovered.HasCurrentPosition, "recovery requires new outer and producer advance");
        });
        test("Redelivery after an interruption still retains high watermark", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            watch.Observe(Missing(), 100);
            var repeat = watch.Observe(Fresh(100, 8, 20), 200);
            check(repeat.State == "awaiting_advance" && repeat.Current is null, "old position blocked");
            var rollback = watch.Observe(Fresh(13, 9, 21), 300);
            check(rollback.State == "reordered" && rollback.Current is null &&
                rollback.NewSamples == 1, "post-gap outer rollback blocked");
        });
        test("Longest interval includes a gap closed by a new advance", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            watch.Observe(Missing(), 500);
            var recovered = watch.Observe(Fresh(13, 9, 21), 2000);
            check(recovered.SinceLastAdvanceMs == 0 &&
                recovered.LongestNoAdvanceMs == 2000,
                "closed interval must count before last-advance resets");
        });
        test("Continuous sparse advances still count the elapsed interval", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var next = watch.Observe(Fresh(13, 9, 21), 5000);
            check(next.State == "advancing" && next.LongestNoAdvanceMs == 5000 &&
                next.SinceLastAdvanceMs == 0, "no hidden sparse interval");
        });
        test("Initial unavailable interval closes when the first sample arrives", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Missing(), 0);
            var first = watch.Observe(Fresh(), 2000);
            check(first.State == "first_sample" && first.SinceLastAdvanceMs == 0 &&
                first.LongestNoAdvanceMs == 2000 && first.NewSamples == 1,
                "first sample must retain initial unavailability");
        });
        test("A new stream closes the prior no-advance interval", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var replaced = watch.Observe(Fresh(1, 1, 1, stream: "stream-b"), 2000);
            check(replaced.State == "session_changed" &&
                replaced.LongestNoAdvanceMs == 2000 &&
                replaced.SinceLastAdvanceMs == 0, "new stream is not a time-machine");
        });
        test("Same sequence with conflicting position is not a legitimate repeat", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var bad = watch.Observe(Fresh(12, 8, 20, x: 12), 500);
            check(bad.State == "reordered" && bad.Current is null,
                "reject inconsistent repeated frame");
        });
        test("New reader or epoch starts new session, never continuous advance", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var reader = watch.Observe(Fresh(1, 1, 1, stream: "stream-b"), 500);
            var epoch = watch.Observe(Fresh(2, 2, 2, stream: "stream-b", epoch: 3), 1000);
            check(reader.State == "session_changed" && reader.SessionChanges == 1,
                "reader swap");
            check(epoch.State == "session_changed" && epoch.SessionChanges == 2,
                "epoch swap");
        });
        test("Older epoch frame cannot reset a pinned transport high watermark", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(100, 8, 20, epoch: 2), 0);
            var reset = watch.Observe(Fresh(101, 9, 21, epoch: 3), 500);
            check(reset.State == "session_changed" && reset.SessionChanges == 1,
                "new epoch on newer transport");
            var delayed = watch.Observe(Fresh(100, 8, 20, epoch: 2), 1000);
            check(delayed.State == "reordered" && delayed.Current is null &&
                delayed.SessionChanges == 1 && delayed.NewSamples == 2,
                "old epoch must not bypass transport watermark");
            var recovered = watch.Observe(Fresh(102, 10, 22, epoch: 3), 1500);
            check(recovered.State == "recovered" && recovered.Current?.OuterSequence == 102 &&
                recovered.SessionChanges == 1 && recovered.NewSamples == 3,
                "rollback must preserve accepted epoch and recover on true advance");
        });
        test("Retired producer epoch cannot regain current position via newer transport", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(100, 8, 20, epoch: 2), 0);
            watch.Observe(Fresh(101, 9, 21, epoch: 3), 500);
            var oldEpoch = watch.Observe(Fresh(102, 8, 20, epoch: 2), 1000);
            check(oldEpoch.State == "reordered" && oldEpoch.Current is null &&
                oldEpoch.SessionChanges == 1 && oldEpoch.NewSamples == 2,
                "retired epoch does not become a new session even with newer outer");
            var recovered = watch.Observe(Fresh(103, 10, 22, epoch: 3), 1500);
            check(recovered.State == "recovered" && recovered.Current?.OuterSequence == 103,
                "accepted epoch resumes without changing session identity");
        });
        test("Equal outer sequence cannot start new producer epoch", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(100, 8, 20, epoch: 2), 0);
            var conflict = watch.Observe(Fresh(100, 9, 21, epoch: 3), 500);
            check(conflict.State == "reordered" && conflict.Current is null &&
                conflict.NewSamples == 1 && conflict.SessionChanges == 0,
                "equal outer cannot represent new producer observation");
        });
        test("Wrong lab cannot mix with current session", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            var bad = watch.Observe(Fresh(lab: "lab2"), 500);
            check(bad.State == "unavailable" && bad.Reason == "lab_mismatch" &&
                bad.Current is null, "cross-lab isolation");
        });
        test("Clock regression is rejected without modifying the last snapshot", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 500);
            try { watch.Observe(Fresh(13, 9, 21), 400); throw new Exception("accepted rewind"); }
            catch (ArgumentOutOfRangeException) { }
            var current = watch.View();
            check(current.Polls == 1 && current.NewSamples == 1 &&
                current.State == "first_sample", "clock regression kept prior");
        });
        test("Watch reports open no-advance interval across missing samples", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            watch.Observe(Fresh(), 0);
            watch.Observe(Missing(), 2200);
            var gap = watch.Observe(Missing(), 4800);
            check(gap.LongestNoAdvanceMs == 4800 && gap.SinceLastAdvanceMs == 4800 &&
                !gap.HasCurrentPosition && gap.Unavailable == 2, "active loss");
        });
        test("Watch summary never serializes private stream binding identity", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            var report = watch.Observe(Fresh(stream: "private-reader-fingerprint"), 0);
            string json = System.Text.Json.JsonSerializer.Serialize(report);
            check(!json.Contains("private-reader-fingerprint", StringComparison.Ordinal) &&
                !json.Contains("AddonClock", StringComparison.Ordinal) &&
                json.Contains("first_sample", StringComparison.Ordinal),
                "only diagnostic fields serialized");
        });
        test("Invalid source identity cannot populate current position", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab1");
            VmTelemetrySnapshot bad = Fresh(stream: "");
            var report = watch.Observe(bad, 0);
            check(!report.HasCurrentPosition && report.Unavailable == 1 &&
                report.NewSamples == 0, "missing fingerprint rejected");
        });
        test("New monitor starts with no permissions and no stale session", () =>
        {
            var watch = new VmTelemetryWatchTracker("lab2");
            var empty = watch.View();
            check(empty.State == "not_started" && empty.Polls == 0 &&
                !empty.HasCurrentPosition && !empty.CanAutomate, "new watch");
            try { _ = new VmTelemetryWatchTracker("unknown"); throw new Exception("bad lab"); }
            catch (ArgumentException) { }
        });
    }
}
