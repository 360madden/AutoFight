using System.Text.Json;
using Rift.Automation.Windows;

internal static class VmSustainedFeedbackSpecs
{
    private sealed class ImmediateProgress<T>(Action<T> capture) : IProgress<T>
    {
        public void Report(T value) => capture(value);
    }

    private static VmTelemetrySnapshot Fresh(
        long outer = 100, long seq = 20, double clock = 50,
        string stream = "private-reader-a", long epoch = 2,
        string lab = "lab1", string source = "192.168.56.128",
        string path = @"C:\games\rift_x64.exe",
        string hash = "") =>
        new(lab, "fresh_observed", "synthetic", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 20, outer, seq, epoch, 3300,
            10, 15, 20, "candidate-regions", stream, clock,
            source, 638000000000000000L, path,
            hash == "" ? new string('A', 64) : hash);

    private static VmTelemetrySnapshot Missing(
        bool eligible = true, string reason = "source_expired", string lab = "lab1",
        long outer = 100, long seq = 20, double clock = 50) =>
        Fresh(outer, seq, clock, lab: lab) with
        {
            Status = "unavailable", Reason = reason,
            X = null, Y = null, Z = null,
            GapProducerX = 10, GapProducerY = 15, GapProducerZ = 20,
            FreshnessOnlyGap = eligible
        };

    private static VmSustainedFeedbackWindow Continuous(
        long step = 500, long until = VmSustainedFeedbackWindow.RequiredDurationMs)
    {
        var window = new VmSustainedFeedbackWindow("lab1");
        for (long ms = 0; ms <= until; ms += step)
        {
            long ordinal = ms / step;
            window.Observe(Fresh(100 + ordinal, 20 + ordinal, 50 + ordinal), ms);
        }
        return window;
    }

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Sustained policy passes full 480s of source-bound advancing data without motion", () =>
        {
            var result = Continuous().Finish(true);
            check(result.Completed && result.SustainedTelemetryEvidencePassed &&
                result.ObservedDurationMs == 480_000 && result.Polls == 961 &&
                result.LongestNoAdvanceMs == 500 && result.NewSamples == 961 &&
                !result.CanAutomate && !result.GameFeedbackConfirmed && !result.InputAttempted,
                "complete evidence, not a gameplay permit");
        });

        test("Repeated producer frames and five temporary freshness gaps can pass", () =>
        {
            var window = new VmSustainedFeedbackWindow("lab1");
            int[] gaps = [101, 200, 401, 602, 801];
            for (long i = 0; i <= 960; i++)
            {
                VmTelemetrySnapshot sample = Array.IndexOf(gaps, (int)i) >= 0
                    ? Missing(outer: 100 + i, seq: 20 + i / 2, clock: 50 + i / 2)
                    : Fresh(100 + i, 20 + i / 2, 50 + i / 2);
                window.Observe(sample, i * 500);
            }
            var result = window.Finish(true);
            check(result.SustainedTelemetryEvidencePassed &&
                result.Unavailable == 5 && result.Repeated > 0 &&
                result.Recovered > 0 && result.LongestNoAdvanceMs < 4500,
                "bounded transients and repeats do not count as new progress");
        });

        test("Exact 4499ms accepted-advance gap passes strict policy", () =>
        {
            var window = new VmSustainedFeedbackWindow("lab1");
            window.Observe(Fresh(), 0);
            window.Observe(Fresh(101, 21, 51), 4499);
            long counter = 2;
            for (long ms = 4999; ms < 480000; ms += 500)
                window.Observe(Fresh(100 + counter, 20 + counter, 50 + counter++), ms);
            window.Observe(Fresh(100 + counter, 20 + counter, 50 + counter), 480000);
            var result = window.Finish(true);
            check(result.SustainedTelemetryEvidencePassed &&
                result.LongestNoAdvanceMs == 4499, "4499ms boundary");
        });

        test("Gap at 4500ms fails permanently even when later samples advance", () =>
        {
            var window = new VmSustainedFeedbackWindow("lab1");
            window.Observe(Fresh(), 0);
            window.Observe(Fresh(101, 21, 51), 4500);
            window.Observe(Fresh(102, 22, 52), 480000);
            var result = window.Finish(true);
            check(!result.SustainedTelemetryEvidencePassed &&
                result.Reason == "no_advance_limit_exceeded" &&
                result.LongestNoAdvanceMs >= 4500, "strict gap latch");
        });

        test("Last unavailable source and early finish cannot masquerade as completed", () =>
        {
            var window = Continuous(until: 479500);
            check(!window.Finish(true).Completed && !window.Finish(true).SustainedTelemetryEvidencePassed,
                "early termination");
            window.Observe(Missing(outer: 1060, seq: 980, clock: 1010), 480000);
            var result = window.Finish(true);
            check(result.Completed && !result.SustainedTelemetryEvidencePassed &&
                result.Reason == "terminal_source_unavailable", "terminal observation needed");
        });

        test("Disallowed source loss and wrong lab are permanent faults", () =>
        {
            var disallowed = new VmSustainedFeedbackWindow("lab1");
            disallowed.Observe(Fresh(), 0);
            disallowed.Observe(Missing(eligible: false, reason: "reader_not_running"), 500);
            disallowed.Observe(Fresh(102, 22, 52), 1000);
            check(disallowed.HasTerminalFault &&
                disallowed.Finish(false).Reason == "reader_not_running", "stopped reader");
            var wrong = new VmSustainedFeedbackWindow("lab1");
            wrong.Observe(Fresh(), 0);
            wrong.Observe(Missing(lab: "lab2"), 500);
            wrong.Observe(Fresh(102, 22, 52), 1000);
            check(wrong.Finish(false).Reason == "lab_mismatch", "cross-lab data latch");
        });

        test("Full source tuple swap is latched even with same stream and counters", () =>
        {
            foreach (var changed in new[]
            {
                Fresh(101, 21, 51, source: "192.168.56.129"),
                Fresh(101, 21, 51, path: @"D:\rift\rift_x64.exe"),
                Fresh(101, 21, 51, hash: new string('B', 64)),
                Fresh(101, 21, 51, stream: "different-reader"),
                Fresh(101, 21, 51, epoch: 3),
                Fresh(101, 21, 51, lab: "lab2")
            })
            {
                var window = new VmSustainedFeedbackWindow("lab1");
                window.Observe(Fresh(), 0);
                window.Observe(changed, 500);
                window.Observe(Fresh(102, 22, 52), 1000);
                check(window.HasTerminalFault && !window.Finish(false).SustainedTelemetryEvidencePassed,
                    "changed source tuple/session/lab");
            }
        });

        test("A transient source swap is rejected even when source returns before the gap limit", () =>
        {
            var window = new VmSustainedFeedbackWindow("lab1");
            window.Observe(Fresh(), 0);
            window.Observe(Missing() with { ReaderSourceAddress = "192.168.56.129" }, 500);
            window.Observe(Fresh(102, 22, 52), 1000);
            check(window.HasTerminalFault &&
                window.Finish(false).Reason == "source_identity_changed",
                "validated source B during unavailable interval");
        });

        test("Expired gap transport rollback and high-water redelivery latch ordering", () =>
        {
            var oldGap = new VmSustainedFeedbackWindow("lab1");
            oldGap.Observe(Fresh(outer: 100, seq: 20, clock: 50), 0);
            oldGap.Observe(Missing(outer: 99, seq: 19, clock: 49), 500);
            oldGap.Observe(Fresh(101, 21, 51), 1000);
            check(oldGap.Finish(false).Reason == "source_ordering_conflict",
                "expired older envelope is not a harmless missing poll");
            var higherGap = new VmSustainedFeedbackWindow("lab1");
            higherGap.Observe(Fresh(100, 20, 50), 0);
            higherGap.Observe(Missing(outer: 200, seq: 20, clock: 50), 500);
            higherGap.Observe(Fresh(101, 21, 51), 1000);
            check(higherGap.Finish(false).Reason == "source_ordering_conflict",
                "higher stale outer watermark retained");
            var recovered = new VmSustainedFeedbackWindow("lab1");
            recovered.Observe(Fresh(100, 20, 50), 0);
            recovered.Observe(Missing(outer: 200, seq: 20, clock: 50), 500);
            recovered.Observe(Fresh(201, 21, 51), 1000);
            check(!recovered.HasTerminalFault, "higher stale outer followed by genuine advancement");
        });

        test("Transport high-watermark rollback is not rehabilitated by later progress", () =>
        {
            var window = new VmSustainedFeedbackWindow("lab1");
            window.Observe(Fresh(), 0);
            window.Observe(Fresh(200, 20, 50), 500);
            window.Observe(Fresh(101, 21, 51), 1000);
            window.Observe(Fresh(201, 22, 52), 1500);
            var result = window.Finish(false);
            check(result.Reordered == 1 && result.Reason == "source_ordering_conflict",
                "reordered frame latched");
        });

        test("Fresh but invalid coordinates cannot become evidence", () =>
        {
            var window = new VmSustainedFeedbackWindow("lab1");
            window.Observe(Fresh(), 0);
            window.Observe(Fresh(101, 21, 51) with { X = double.NaN }, 500);
            check(window.HasTerminalFault && !window.Finish(false).SustainedTelemetryEvidencePassed,
                "nonfinite position rejected");
        });

        test("Initial stopped reader and invalid selected lab exit without a delay or repeat read", () =>
        {
            int reads = 0, waits = 0;
            VmTelemetrySnapshot Read() { reads++; return Missing(false, "reader_not_running"); }
            Task Delay(int _, CancellationToken unused) { waits++; return Task.CompletedTask; }
            var initial = VmSustainedFeedbackRunner.RunObservedAsync(
                "lab1", Read, () => 0, Delay).GetAwaiter().GetResult();
            check(initial.Reason == "reader_not_running" && reads == 1 && waits == 0 &&
                !initial.Completed, "fail-fast stopped reader");
            var wrong = VmSustainedFeedbackRunner.RunObservedAsync(
                "bad", Read, () => 0, Delay).GetAwaiter().GetResult();
            check(wrong.Reason == "invalid_lab" && reads == 1, "invalid selected lab before IO");
        });

        test("Injected clock completes 480s instantly without real delay", () =>
        {
            long elapsed = 0, reads = 0;
            var report = VmSustainedFeedbackRunner.RunObservedAsync("lab1",
                () => Fresh(100 + reads, 20 + reads, 50 + reads++),
                () => elapsed,
                (ms, _) => { elapsed += ms; return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            check(report.SustainedTelemetryEvidencePassed && report.Completed &&
                report.ObservedDurationMs == 480000 && report.Polls == 961,
                "synthetic elapsed not wall-clock sleep");
        });

        test("Cancellation during qualification is incomplete and never action-ready", () =>
        {
            using var source = new CancellationTokenSource();
            long elapsed = 0, reads = 0;
            var report = VmSustainedFeedbackRunner.RunObservedAsync("lab1",
                () => Fresh(100 + reads, 20 + reads, 50 + reads++),
                () => elapsed,
                (ms, _) =>
                {
                    elapsed += ms;
                    if (elapsed >= 5000) source.Cancel();
                    return Task.CompletedTask;
                }, source.Token).GetAwaiter().GetResult();
            check(!report.Completed && !report.SustainedTelemetryEvidencePassed &&
                report.Reason == "cancelled" && !report.CanAutomate, "cancelled");
        });

        test("Cancellation during last synchronous receipt read cannot return completed pass", () =>
        {
            using var source = new CancellationTokenSource();
            long elapsed = 0, reads = 0;
            var report = VmSustainedFeedbackRunner.RunObservedAsync("lab1",
                () =>
                {
                    reads++;
                    if (elapsed >= 480000) source.Cancel();
                    return Fresh(100 + reads, 20 + reads, 50 + reads);
                },
                () => elapsed,
                (ms, _) => { elapsed += ms; return Task.CompletedTask; },
                source.Token).GetAwaiter().GetResult();
            check(!report.Completed && !report.SustainedTelemetryEvidencePassed &&
                report.Reason == "cancelled" && report.ObservedDurationMs < 480000,
                "late cancel wins");
        });

        test("Sustained runner reports exact redacted aggregate progress through terminal observation", () =>
        {
            long elapsed = 0, reads = 0;
            var progress = new List<VmSustainedFeedbackProgress>();
            var report = VmSustainedFeedbackRunner.RunObservedAsync("lab1",
                () => Fresh(100 + reads, 20 + reads, 50 + reads++),
                () => elapsed,
                (ms, _) => { elapsed += ms; return Task.CompletedTask; },
                progress: new ImmediateProgress<VmSustainedFeedbackProgress>(progress.Add))
                .GetAwaiter().GetResult();
            check(report.SustainedTelemetryEvidencePassed && progress.Count == report.Polls &&
                progress[0].ElapsedMs == 0 && progress[^1].ElapsedMs == 480000 &&
                progress[^1].NewSamples == report.NewSamples && !progress[^1].TerminalFault,
                "progress is authoritative and ends at the terminal observation");
            string json = JsonSerializer.Serialize(progress);
            check(!json.Contains("private-reader-a", StringComparison.Ordinal) &&
                !json.Contains("192.168.", StringComparison.Ordinal) &&
                !json.Contains("C:\\\\games", StringComparison.Ordinal) &&
                !json.Contains("GameProcessId", StringComparison.Ordinal) &&
                !json.Contains("GapProducerX", StringComparison.Ordinal),
                "no source identity, path, game process or XYZ in progress");
        });

        test("Tolerated temporary freshness gap is visible as unavailable, not as a terminal fault", () =>
        {
            using var cancel = new CancellationTokenSource();
            long elapsed = 0, reads = 0;
            var progress = new List<VmSustainedFeedbackProgress>();
            var result = VmSustainedFeedbackRunner.RunObservedAsync("lab1",
                () =>
                {
                    long i = reads++;
                    return i == 3 ? Missing(outer: 103, seq: 23, clock: 53)
                        : Fresh(100 + i, 20 + i, 50 + i);
                },
                () => elapsed,
                (ms, _) =>
                {
                    elapsed += ms;
                    if (elapsed == 3000) cancel.Cancel();
                    return Task.CompletedTask;
                }, cancel.Token,
                new ImmediateProgress<VmSustainedFeedbackProgress>(progress.Add))
                .GetAwaiter().GetResult();
            check(progress.Any(p => p.Unavailable == 1 && !p.TerminalFault) &&
                progress.Last().NewSamples > 2 && result.Reason == "cancelled" &&
                !result.SustainedTelemetryEvidencePassed,
                "progress separates temporary unavailable from an actionable verdict");
        });

        test("Exact forbidden 4500ms gap emits a latched terminal progress fault", () =>
        {
            long elapsed = 0;
            var progress = new List<VmSustainedFeedbackProgress>();
            var result = VmSustainedFeedbackRunner.RunObservedAsync("lab1",
                () => Fresh(),
                () => elapsed,
                (ms, _) => { elapsed += ms; return Task.CompletedTask; },
                progress: new ImmediateProgress<VmSustainedFeedbackProgress>(progress.Add))
                .GetAwaiter().GetResult();
            check(result.Reason == "no_advance_limit_exceeded" &&
                progress[^1].ElapsedMs == 4500 && progress[^1].TerminalFault &&
                !result.Completed && !result.SustainedTelemetryEvidencePassed,
                "strict gap reported with no false pass");
        });

        test("Qualification report does not leak source identity or paths", () =>
        {
            var report = Continuous().Finish(true);
            string json = JsonSerializer.Serialize(report);
            check(!json.Contains("192.168.", StringComparison.Ordinal) &&
                !json.Contains("C:\\games", StringComparison.Ordinal) &&
                !json.Contains("private-reader-a", StringComparison.Ordinal) &&
                !json.Contains("3300", StringComparison.Ordinal),
                "aggregate public receipt only");
        });
    }
}
