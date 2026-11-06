using Rift.Automation.Windows;

internal static class StandaloneTelemetryQualificationSpecs
{
    private static VmTelemetrySnapshot Fresh(long sequence = 1, string lab = "lab1") =>
        new(lab, "fresh_observed", "synthetic", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 10, sequence, sequence, 2, 7164,
            42, -1, 99, "candidate-regions", "owned-reader-a", sequence,
            "192.168.56.128", 639294897866244204L,
            @"C:\RIFT_TRANSFER\RIFT\rift_x64.exe", new string('a', 64));

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Owned sustained qualification passes 480s with stationary advancing XYZ", () =>
        {
            long ms = 0, nextSequence = 1;
            var report = StandaloneTelemetryQualification.RunObservedAsync(
                "lab1", () => Fresh(nextSequence++), () => ms,
                (delay, _) => { ms += delay; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(report.SustainedTelemetryEvidencePassed && report.Completed &&
                report.ObservedDurationMs == 480_000 && report.Polls == 961 &&
                report.Mode == StandaloneTelemetryQualification.Mode &&
                report.PolicyId == StandaloneTelemetryQualification.PolicyId &&
                !report.CanAutomate && !report.InputAttempted &&
                !report.GameFeedbackConfirmed,
                "480s policy cannot grant gameplay input");
        });

        test("Owned sustained qualification rejects exact 4500ms source gap", () =>
        {
            long ms = 0, sequence = 1;
            var report = StandaloneTelemetryQualification.RunObservedAsync(
                "lab1", () => Fresh(sequence++), () => ms,
                (_, _) => { ms += 4500; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(!report.SustainedTelemetryEvidencePassed &&
                report.Reason == "no_advance_limit_exceeded" &&
                report.LongestNoAdvanceMs >= 4500,
                "strict less-than 4500ms");
        });

        test("Owned sustained qualification rejects wrong-lab source swap", () =>
        {
            long ms = 0, sequence = 1;
            var report = StandaloneTelemetryQualification.RunObservedAsync(
                "lab1", () => Fresh(sequence++, sequence > 2 ? "lab2" : "lab1"),
                () => ms, (delay, _) =>
                { ms += delay; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(!report.SustainedTelemetryEvidencePassed &&
                report.Reason == "lab_mismatch" && !report.CanAutomate,
                "cross-lab source rejected");
        });

        test("Owned sustained qualification rejects stale terminal XYZ", () =>
        {
            long ms = 0, sequence = 1;
            VmTelemetrySnapshot Read()
            {
                var sample = Fresh(sequence++);
                return ms == 480_000 ? sample with
                {
                    Status = "unavailable", X = null, Y = null, Z = null,
                    Reason = "reader_not_running"
                } : sample;
            }
            var report = StandaloneTelemetryQualification.RunObservedAsync(
                "lab1", Read, () => ms,
                (delay, _) => { ms += delay; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(!report.SustainedTelemetryEvidencePassed &&
                report.ObservedDurationMs == 480_000 &&
                report.Reason != "passed", "last XYZ must be current");
        });

        test("Owned sustained qualification cancellation cannot count as success", () =>
        {
            using var source = new CancellationTokenSource();
            long ms = 0, sequence = 1;
            var report = StandaloneTelemetryQualification.RunObservedAsync(
                "lab1", () => Fresh(sequence++), () => ms,
                (delay, _) =>
                {
                    ms += delay;
                    if (ms >= 3000) source.Cancel();
                    return Task.CompletedTask;
                }, source.Token).GetAwaiter().GetResult();
            check(!report.SustainedTelemetryEvidencePassed &&
                report.Reason == "cancelled" &&
                !report.Completed, "cancelled incomplete");
        });

        test("Owned qualifier warmup requires identity-bound fresh source", () =>
        {
            long ms = 0;
            int reads = 0;
            var ready = StandaloneTelemetryQualification.WaitForInitialSourceAsync(
                "lab1", () =>
                {
                    reads++;
                    return reads < 4 ? Fresh() with
                    {
                        Status = "unavailable", X = null, Y = null, Z = null
                    } : Fresh();
                },
                () => ms,
                (delay, _) => { ms += delay; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(ready && reads == 4 && ms == 1500,
                "warmup starts after first fully bound fresh XYZ");

            ms = 0; reads = 0;
            bool wrong = StandaloneTelemetryQualification.WaitForInitialSourceAsync(
                "lab1", () => Fresh(lab: "lab2"),
                () => ms,
                (delay, _) => { ms += delay; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(!wrong && ms == 0, "wrong lab aborts without a timed wait");

            ms = 0; reads = 0;
            bool timeout = StandaloneTelemetryQualification.WaitForInitialSourceAsync(
                "lab1", () =>
                {
                    reads++;
                    return Fresh() with { Status = "unavailable", X = null, Y = null, Z = null };
                },
                () => ms,
                (delay, _) => { ms += delay; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(!timeout && ms == 30000 && reads == 61,
                "bounded 30-second no-source warmup");
        });

        test("Owned qualifier warmup cancellation and regressed clock fail closed", () =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                _ = StandaloneTelemetryQualification.WaitForInitialSourceAsync(
                    "lab1", () => Fresh(), () => 0,
                    static (_, _) => Task.CompletedTask,
                    cancelled.Token).GetAwaiter().GetResult();
                throw new InvalidOperationException("accepted cancellation");
            }
            catch (OperationCanceledException) { }

            long ms = 1000;
            bool rollback = StandaloneTelemetryQualification.WaitForInitialSourceAsync(
                "lab1",
                () => Fresh() with { Status = "unavailable", X = null, Y = null, Z = null },
                () => ms,
                (_, _) => { ms = 999; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(!rollback, "nonmonotonic warmup clock");
        });
    }
}
