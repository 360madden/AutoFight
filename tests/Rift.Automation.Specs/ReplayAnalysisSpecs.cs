using System.Text.Json;
using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class ReplayAnalysisSpecs
{
    private static readonly SessionIdentity Session = new("analysis-session", "synthetic-v1");

    private static SampleStep Sample(long at, long sequence = 1, long epoch = 1,
        ObservationStatus status = ObservationStatus.Present, long? acquired = null,
        long? end = null, long? received = null, SessionIdentity? session = null)
    {
        long start = acquired ?? at;
        return new(at, new PositionObservation(session ?? Session, epoch, sequence, status,
            status == ObservationStatus.Present ? new PlayerPosition(0, -1, 2) : null,
            start, end ?? start, received ?? at, "analysis"));
    }

    private static ReplayAnalysisReport Analyze(params ReplayStep[] steps) =>
        ReplayAnalysis.Analyze(new ReplayScenario("analysis", steps));

    private static long Duration(ReplayAnalysisReport report, ViewStatus status) =>
        report.StatusDurations.Single(item => item.Status == status.ToString()).DurationMs;

    private static void Invariants(ReplayAnalysisReport report)
    {
        Check(report.UsableDurationMs + report.UnavailableDurationMs == report.DurationMs, "coverage partition");
        Check(report.StatusDurations.Sum(item => item.DurationMs) == report.DurationMs, "status coverage partition");
        Check(report.AcceptedSampleCount + report.RejectedSampleCount == report.SampleCount, "sample partition");
        Check(report.InputStatusCounts.Sum(item => item.Count) == report.SampleCount, "input status partition");
        Check(report.SampleResults.Sum(item => item.Count) == report.SampleCount, "result partition");
        Check(report.ResetCount + report.GapCount + report.TickCount + report.SampleCount == report.StepCount, "step partition");
        Check(report.AcquisitionSpan.Count == report.SampleCount &&
            report.ReceiptDelay.Count == report.SampleCount &&
            report.ApplicationQueueWait.Count == report.SampleCount &&
            report.AcquisitionToApply.Count == report.SampleCount, "latency population");
        Check(report.LongestUnavailableIntervalMs <= report.UnavailableDurationMs &&
            report.TrailingUnavailableDurationMs <= report.LongestUnavailableIntervalMs, "outage bounds");
        Check(report.Origin == "SyntheticReplay" && !report.IsLiveQualification && !report.CanAutomate, "no live promotion");
    }

    internal static void Run()
    {
        Test("analysis pinned full demo report", () =>
        {
            var actual = ReplayAnalysis.Analyze(DemoReplay.Create());
            var zero = new ReplayLatencySummary(5, 0, 0, 0, 0, 0);
            var delayed = new ReplayLatencySummary(5, 0, 0, 10, 10, 10);
            var expected = new ReplayAnalysisReport(
                "SYNTHETIC observer demonstration", 10, 0, 2221, 1000, 1000,
                2, 1, 2, 5, 4, 1, 3, 3, 2092, 129, 119, 0, true, "Stale",
                new ReplayStatusCount[] { new("Present", 4), new("Missing", 0),
                    new("Unsupported", 0), new("Invalid", 0), new("Error", 1) },
                new ReplayStatusDuration[] { new("Unknown", 30), new("Present", 2092),
                    new("Stale", 9), new("Missing", 0), new("Unsupported", 0),
                    new("Invalid", 0), new("Error", 10), new("Gap", 80) },
                new ReplayResultCount[] { new("accepted", 4), new("wrong_epoch", 1) },
                zero, delayed, zero, delayed);
            Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected), "manually derived complete oracle");
            Check(actual.Schema == "rift-replay-analysis/1" &&
                actual.CoverageModel == "integer_ms_slots_first_reset_to_final_step_exclusive" &&
                actual.LatencyPopulation == "all_structurally_valid_sample_attempts_including_rejected" &&
                actual.PercentileMethod == "nearest_rank", "explicit analysis model");
            Check(actual.UsablePercent == 100.0 * 2092 / 2221, "percentage denominator");
            Invariants(actual);
        });

        Test("analysis excludes time before delayed reset", () =>
        {
            var r = Analyze(new ResetStep(50, Session, 1), Sample(50), new TickStep(60));
            Check(r.WindowStartMs == 50 && r.DurationMs == 10 && r.UsableDurationMs == 10, "no pre-session charging");
            Invariants(r);
        });
        Test("analysis zero horizon has null percentage and empty metrics", () =>
        {
            var r = Analyze(new ResetStep(99, Session, 1));
            Check(r.DurationMs == 0 && r.UsablePercent is null && r.LongestUnavailableIntervalMs == 0, "zero horizon");
            Check(r.AcquisitionSpan == new ReplayLatencySummary(0, null, null, null, null, null), "empty is not zero latency");
            Check(r.TrailingUnavailableIsRightCensored && r.TrailingUnavailableDurationMs == 0, "zero open interval");
            Invariants(r);
        });
        Test("analysis expiry without explicit expiry tick", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(0), new TickStep(2000));
            Check(r.UsableDurationMs == 1001 && r.UnavailableDurationMs == 999 &&
                r.LongestUnavailableIntervalMs == 999 && r.TrailingUnavailableDurationMs == 999, "automatic expiry split");
            Invariants(r);
        });
        foreach (long end in new long[] { 1000, 1001 })
        {
            Test("analysis terminal inclusive boundary " + end, () =>
            {
                var r = Analyze(new ResetStep(0, Session, 1), Sample(0), new TickStep(end));
                Check(r.UsableDurationMs == end && r.UnavailableDurationMs == 0, "terminal instant excluded");
                Check(r.FinalStatus == (end == 1000 ? "Present" : "Stale"), "endpoint state separate");
                Check(r.TrailingUnavailableIsRightCensored == (end == 1001) &&
                    r.TrailingUnavailableDurationMs == 0, "endpoint censor boundary");
                Invariants(r);
            });
        }
        Test("analysis uses acquisition start not end or receipt", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(900, acquired: 0, end: 900), new TickStep(1500));
            Check(r.UsableDurationMs == 101 && r.UnavailableDurationMs == 1399, "conservative acquisition age");
            Check(Duration(r, ViewStatus.Stale) == 499 && Duration(r, ViewStatus.Unknown) == 900, "implicit stale span");
            Invariants(r);
        });
        Test("analysis accepted late present is not usable", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(1100, acquired: 0, received: 100), new TickStep(1500));
            Check(r.AcceptedPresentSampleCount == 1 && r.FreshPresentOnArrivalCount == 0 &&
                r.UsableDurationMs == 0 && r.LongestUnavailableIntervalMs == 1500, "accepted is not fresh");
            Invariants(r);
        });
        Test("analysis sample then same-time gap cannot split outage", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(100), new GapStep(100, "gap"), new TickStep(200));
            Check(r.FreshPresentOnArrivalCount == 1 && r.UsableDurationMs == 0 &&
                r.LongestUnavailableIntervalMs == 200, "zero-duration present has no slots");
            Invariants(r);
        });
        Test("analysis same-time gap then sample owns following slots", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), new GapStep(100, "gap"), Sample(100), new TickStep(200));
            Check(r.UsableDurationMs == 100 && r.UnavailableDurationMs == 100 &&
                r.LongestUnavailableIntervalMs == 100 && !r.TrailingUnavailableIsRightCensored, "source ordering");
            Invariants(r);
        });
        foreach (var status in new[] { ObservationStatus.Missing, ObservationStatus.Unsupported,
                     ObservationStatus.Invalid, ObservationStatus.Error })
        {
            Test("analysis accepted non-present " + status, () =>
            {
                var r = Analyze(new ResetStep(0, Session, 1), Sample(0),
                    Sample(50, sequence: 2, status: status), new TickStep(100));
                Check(r.AcceptedSampleCount == 2 && r.AcceptedPresentSampleCount == 1 &&
                    r.UsableDurationMs == 50 && r.UnavailableDurationMs == 50, "nonpresent ends eligibility");
                Check(r.InputStatusCounts.Single(x => x.Status == status.ToString()).Count == 1, "typed input count");
                Invariants(r);
            });
        }
        foreach (string rejection in new[] { "wrong_epoch", "wrong_session", "nonadvancing_sequence" })
        {
            Test("analysis rejected " + rejection + " cannot refresh current", () =>
            {
                SampleStep rejected = rejection switch
                {
                    "wrong_epoch" => Sample(500, sequence: 2, epoch: 2),
                    "wrong_session" => Sample(500, sequence: 2, session: new("other", "synthetic-v1")),
                    _ => Sample(500)
                };
                var r = Analyze(new ResetStep(0, Session, 1), Sample(0), rejected, new TickStep(2000));
                Check(r.RejectedSampleCount == 1 && r.SampleResults.Any(x => x.Code == rejection && x.Count == 1), "rejection counted");
                Check(r.UsableDurationMs == 1001 && r.UnavailableDurationMs == 999 &&
                    r.AcquisitionSpan.Count == 2, "rejection measured but not applied");
                Invariants(r);
            });
        }
        Test("analysis gap capture floor and recovery", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(0), new GapStep(500, "gap"),
                Sample(600, sequence: 2, acquired: 499), Sample(650, sequence: 3), new TickStep(2000));
            Check(r.UsableDurationMs == 1501 && r.UnavailableDurationMs == 499 &&
                r.LongestUnavailableIntervalMs == 349 && Duration(r, ViewStatus.Invalid) == 50, "gap fence and recovery");
            Check(r.SampleResults.Any(x => x.Code == "capture_before_reset_or_gap"), "old capture rejected");
            Invariants(r);
        });
        Test("analysis unavailable statuses merge across reset", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(20, status: ObservationStatus.Error),
                new GapStep(30, "gap"), new ResetStep(50, Session, 2), new TickStep(200));
            Check(r.LongestUnavailableIntervalMs == 200 && r.TrailingUnavailableDurationMs == 200, "one uninterrupted outage");
            Invariants(r);
        });
        Test("analysis final recovery closes outage without inventing coverage", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(10));
            Check(r.UsableDurationMs == 0 && r.UnavailableDurationMs == 10 &&
                r.LongestUnavailableIntervalMs == 10 && r.TrailingUnavailableDurationMs == 0 &&
                !r.TrailingUnavailableIsRightCensored && r.FinalStatus == "Present", "final recovery endpoint");
            Invariants(r);
        });
        Test("analysis final gap starts zero-length censored outage", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(0), new GapStep(10, "gap"));
            Check(r.UsableDurationMs == 10 && r.UnavailableDurationMs == 0 &&
                r.TrailingUnavailableDurationMs == 0 && r.TrailingUnavailableIsRightCensored, "new endpoint gap");
            Invariants(r);
        });
        Test("analysis pipeline components include rejected arrival", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1),
                Sample(100, epoch: 2, acquired: 10, end: 30, received: 70), new TickStep(101));
            Check(r.RejectedSampleCount == 1 && r.AcceptedSampleCount == 0, "rejected population");
            Check(r.AcquisitionSpan == new ReplayLatencySummary(1, 20, 20, 20, 20, 20) &&
                r.ReceiptDelay == new ReplayLatencySummary(1, 40, 40, 40, 40, 40) &&
                r.ApplicationQueueWait == new ReplayLatencySummary(1, 30, 30, 30, 30, 30) &&
                r.AcquisitionToApply == new ReplayLatencySummary(1, 90, 90, 90, 90, 90), "20 + 40 + 30 = 90");
            Invariants(r);
        });
        Test("analysis nearest-rank quantiles have pinned twenty-sample oracle", () =>
        {
            var steps = new List<ReplayStep> { new ResetStep(0, Session, 1) };
            for (int value = 1; value <= 20; value++)
                steps.Add(Sample(value, sequence: value, acquired: 0));
            var r = ReplayAnalysis.Analyze(new ReplayScenario("quantiles", steps));
            Check(r.ReceiptDelay == new ReplayLatencySummary(20, 1, 10, 19, 20, 20), "nearest-rank not interpolated");
            Invariants(r);
        });
        foreach (long length in new long[] { 1000, 1001, 2000 })
        {
            Test("analysis near maximum timestamp " + length, () =>
            {
                long start = long.MaxValue - length;
                var r = Analyze(new ResetStep(start, Session, 1), Sample(start), new TickStep(long.MaxValue));
                long usable = Math.Min(length, 1001);
                Check(r.DurationMs == length && r.UsableDurationMs == usable &&
                    r.UnavailableDurationMs == length - usable, "no expiry addition overflow");
                Check(r.FinalStatus == (length == 1000 ? "Present" : "Stale"), "near max endpoint");
                Invariants(r);
            });
        }
        Test("analysis maximum outage merges across large status boundaries", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), new GapStep(1, "gap"),
                new ResetStep(long.MaxValue - 1, Session, 2), new TickStep(long.MaxValue));
            Check(r.DurationMs == long.MaxValue && r.LongestUnavailableIntervalMs == long.MaxValue &&
                r.TrailingUnavailableDurationMs == long.MaxValue &&
                Duration(r, ViewStatus.Unknown) == 2 && Duration(r, ViewStatus.Gap) == long.MaxValue - 2, "bounded sum reaches maximum exactly");
            Invariants(r);
        });
        Test("analysis long-range outage is broken only by usable slots", () =>
        {
            var r = Analyze(new ResetStep(0, Session, 1), Sample(1000), new TickStep(long.MaxValue));
            Check(r.UsableDurationMs == 1001 && r.UnavailableDurationMs == long.MaxValue - 1001 &&
                r.LongestUnavailableIntervalMs == long.MaxValue - 2001, "large stale tail");
            Invariants(r);
        });
        Test("analysis rejects structurally invalid direct scenario", () =>
        {
            Throws<ArgumentException>(() => ReplayAnalysis.Analyze(new ReplayScenario("bad",
                new ReplayStep[] { new ResetStep(0, Session, 1), new TickStep(-1) })));
            Throws<ArgumentNullException>(() => ReplayAnalysis.Analyze(null!));
        });
        Test("analysis repeatability and read-only output ownership", () =>
        {
            var scenario = DemoReplay.Create();
            string input = JsonSerializer.Serialize(scenario);
            var first = ReplayAnalysis.Analyze(scenario);
            var second = ReplayAnalysis.Analyze(scenario);
            Check(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second), "deterministic report");
            Check(JsonSerializer.Serialize(scenario) == input, "input unchanged");
            Throws<NotSupportedException>(() => ((IList<ReplayStatusDuration>)first.StatusDurations)[0] = new("fake", 1));
            Throws<NotSupportedException>(() => ((IList<ReplayResultCount>)first.SampleResults)[0] = new("fake", 1));
            Throws<NotSupportedException>(() => ((IList<ReplayStatusCount>)first.InputStatusCounts)[0] = new("fake", 1));
        });

        Test("analysis live-looking identifiers cannot promote evidence", () =>
        {
            var identity = new SessionIdentity("live-rift", "1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8");
            var r = ReplayAnalysis.Analyze(new ReplayScenario("qualified native source (untrusted label)",
                new ReplayStep[] { new ResetStep(0, identity, 1), Sample(0, session: identity), new TickStep(1) }));
            Check(r.FinalStatus == "Unsupported" && r.UsableDurationMs == 0, "unknown build unsupported");
            Invariants(r);
        });
        Test("analysis CLI emits exactly one complete JSON report", () =>
        {
            var r = InvokeCli("analyze", Fixture("synthetic-observer.json"));
            Check(r.ExitCode == 0 && r.StandardError.Length == 0, "success streams");
            Check(r.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length == 1, "one report line");
            using var document = JsonDocument.Parse(r.StandardOutput);
            var value = document.RootElement;
            Check(value.GetProperty("Schema").GetString() == "rift-replay-analysis/1" &&
                value.GetProperty("Origin").GetString() == "SyntheticReplay" &&
                !value.GetProperty("IsLiveQualification").GetBoolean() &&
                !value.GetProperty("CanAutomate").GetBoolean(), "CLI evidence labels");
        });
        Test("analysis CLI late malformed step emits no partial report", () =>
        {
            string path = Fixture("analysis-malformed-tail.json");
            Check(File.Exists(path), "negative fixture is present, not missing");
            var r = InvokeCli("analyze", path);
            Check(r.ExitCode == 2 && r.StandardOutput.Length == 0 &&
                r.StandardError.Trim() == "Synthetic replay rejected: invalid_scenario", "atomic intake");
        });
        Test("analysis CLI missing file rejects without stdout", () =>
        {
            string path = Fixture("not-a-fixture.json");
            Check(!File.Exists(path), "missing fixture remains absent");
            var r = InvokeCli("analyze", path);
            Check(r.ExitCode == 2 && r.StandardOutput.Length == 0 &&
                r.StandardError.Trim() == "Synthetic replay file unavailable.", "file failure");
        });
        Test("analysis CLI directory rejects without stdout", () =>
        {
            var r = InvokeCli("analyze", AppContext.BaseDirectory);
            Check(r.ExitCode == 2 && r.StandardOutput.Length == 0 &&
                r.StandardError.Trim() == "Synthetic replay file unavailable.", "directory is not replay");
        });
        Test("analysis CLI rejects missing and extra arguments", () =>
        {
            foreach (var args in new[] { new[] { "analyze" },
                         new[] { "analyze", Fixture("synthetic-observer.json"), "extra" } })
            {
                var r = InvokeCli(args);
                Check(r.ExitCode == 2 && r.StandardOutput.Length == 0 &&
                    r.StandardError.Trim() == "Invalid arguments; use --help.", "strict arguments");
            }
        });
        Test("analysis CLI help states measurement scope", () =>
        {
            var r = InvokeCli("--help");
            Check(r.ExitCode == 0 && r.StandardError.Length == 0 &&
                r.StandardOutput.Contains("analyze <path>", StringComparison.Ordinal) &&
                r.StandardOutput.Contains("not live qualification", StringComparison.Ordinal), "discoverable scope");
        });
        Test("analysis CLI preserves legacy trace mode", () =>
        {
            var r = InvokeCli("replay", Fixture("synthetic-observer.json"));
            Check(r.ExitCode == 0 && r.StandardError.Length == 0, "trace mode success");
            string[] lines = r.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Check(lines.Length == 12, "header, initial state and ten transition rows");
            using var header = JsonDocument.Parse(lines[0]);
            Check(header.RootElement.GetProperty("traceSchema").GetString() == "rift-observer-trace/1", "trace schema unchanged");
        });

        for (int seed = 0; seed < 24; seed++)
        {
            int capturedSeed = seed;
            Test("analysis independent millisecond oracle seed " + capturedSeed, () =>
            {
                ReplayScenario scenario = GeneratedScenario(capturedSeed);
                ReplayAnalysisReport report = ReplayAnalysis.Analyze(scenario);
                CheckDiscreteOracle(scenario, report);
                Invariants(report);
            });
        }
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "replay", name);

    private static (int ExitCode, string StandardOutput, string StandardError) InvokeCli(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            int result = SpecRunner.Run(args);
            return (result, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static ReplayScenario GeneratedScenario(int seed)
    {
        var random = new Random(seed);
        var steps = new List<ReplayStep> { new ResetStep(0, Session, 1) };
        long at = 0, epoch = 1, sequence = 0;
        for (int i = 0; i < 24; i++)
        {
            at += random.Next(0, 200);
            switch (random.Next(0, 7))
            {
                case 0:
                    steps.Add(new ResetStep(at, Session, ++epoch));
                    sequence = 0;
                    break;
                case 1:
                    steps.Add(new GapStep(at, "generated gap"));
                    break;
                case 2:
                    steps.Add(new TickStep(at));
                    break;
                default:
                    long acquired = Math.Max(0, at - random.Next(0, 1600));
                    long end = acquired + random.Next(0, (int)(at - acquired) + 1);
                    long received = end + random.Next(0, (int)(at - end) + 1);
                    long sampleSequence = random.Next(0, 4) == 0 ? Math.Max(1, sequence) : ++sequence;
                    long sampleEpoch = random.Next(0, 5) == 0 ? Math.Max(1, epoch - 1) : epoch;
                    var status = random.Next(0, 3) == 0
                        ? (ObservationStatus)random.Next(1, 5) : ObservationStatus.Present;
                    steps.Add(Sample(at, sampleSequence, sampleEpoch, status, acquired, end, received));
                    break;
            }
        }
        steps.Add(new TickStep(at + 2000));
        return new ReplayScenario("generated-" + seed, steps);
    }

    private static void CheckDiscreteOracle(ReplayScenario scenario, ReplayAnalysisReport report)
    {
        // Intentionally slow independent integration: project every integer ms,
        // rather than copying the analyzer's expiry-splitting arithmetic.
        var engine = new ObservationEngine();
        var durations = Enum.GetValues<ViewStatus>().ToDictionary(status => status, _ => 0L);
        int index = 0;
        long currentOutage = 0, longest = 0;
        for (long time = scenario.Steps[0].AtMs; time < scenario.Steps[^1].AtMs; time++)
        {
            while (index < scenario.Steps.Count && scenario.Steps[index].AtMs == time)
            {
                switch (scenario.Steps[index++])
                {
                    case ResetStep reset: engine.Reset(reset.Session, reset.Epoch, time); break;
                    case SampleStep sample: engine.Apply(sample.Observation, time); break;
                    case GapStep gap: engine.MarkGap(time, gap.Reason); break;
                    case TickStep: break;
                }
            }
            PositionView view = engine.Project(time);
            durations[view.Status]++;
            currentOutage = view.CanObservePosition ? 0 : currentOutage + 1;
            longest = Math.Max(longest, currentOutage);
        }
        foreach (var status in Enum.GetValues<ViewStatus>())
            Check(Duration(report, status) == durations[status], "per-ms oracle " + status);
        Check(report.LongestUnavailableIntervalMs == longest, "per-ms longest outage");
    }
}
