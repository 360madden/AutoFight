using System.Text;
using System.Text.Json;
using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class ReplaySpecs
{
    private const string ResetJson = """{"kind":"reset","atMs":0,"sessionId":"spec-session","buildId":"synthetic-v1","epoch":1}""";
    private const string SampleJson = """{"kind":"sample","atMs":10,"sessionId":"spec-session","buildId":"synthetic-v1","epoch":1,"sequence":1,"status":"present","value":{"x":0,"y":-10,"z":20},"acquiredStartMs":0,"acquiredEndMs":0,"receivedMs":10,"sourceId":"spec"}""";

    private static string Document(string steps) =>
        """{"schema":"rift-replay/1","origin":"synthetic","name":"spec","steps":[""" + steps + "]}";

    private static ReplayReadResult Read(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return ReplayReader.Read(stream);
    }

    private static void Reject(string text)
    {
        ReplayReadResult result = Read(text);
        Check(!result.Success && result.Scenario is null && !string.IsNullOrWhiteSpace(result.ErrorCode), "atomic rejection with fixed diagnostic");
    }

    internal static void Run()
    {
        string good = Document(ResetJson + "," + SampleJson);
        Test("strict valid replay parses", () =>
        {
            var result = Read(good);
            Check(result.Success && result.Scenario?.Steps.Count == 2, "valid schema");
        });
        Test("deterministic complete transition trace", () =>
        {
            ReplayScenario scenario = DemoReplay.Create();
            Check(Trace(scenario) == Trace(scenario), "all transitions deterministic");
        });
        Test("pinned demo transition oracle", () =>
        {
            const string expected = """
0|0|0|0|Unknown|-|-|-|-|unavailable|no_session|False|ready
1|0|1|1|Unknown|-|-|-|-|unavailable|awaiting_sample|False|reset
2|10|1|2|Present|0,0,0|0,0,0|10|0|synthetic-demo|present|True|accepted
3|100|1|3|Present|0,0,0|0,0,0|0|0|synthetic-demo|present|True|accepted
4|1101|1|3|Stale|-|0,0,0|1001|1001|synthetic-demo|acquisition_stale|False|tick
5|1110|1|4|Error|-|0,0,0|0|0|synthetic-demo|synthetic source error|False|accepted
6|1120|1|5|Gap|-|0,0,0|-|-|synthetic-demo|synthetic source gap|False|gap
7|1200|2|6|Unknown|-|-|-|-|unavailable|awaiting_sample|False|reset
8|1210|2|6|Unknown|-|-|-|-|unavailable|awaiting_sample|False|wrong_epoch
9|1220|2|7|Present|0,10,20|0,10,20|0|0|synthetic-demo|present|True|accepted
10|2221|2|7|Stale|-|0,10,20|1001|1001|synthetic-demo|acquisition_stale|False|tick
""";
            var player = new ReplayPlayer(DemoReplay.Create());
            var rows = new List<string> { OracleRow(player) };
            while (player.Step()) rows.Add(OracleRow(player));
            Check(string.Join("\n", rows) == expected.Replace("\r\n", "\n"), "full expected transition oracle");
        });
        Test("player exhaustion and reset", () =>
        {
            var player = new ReplayPlayer(DemoReplay.Create());
            string initial = JsonSerializer.Serialize(player.View);
            int count = 0;
            while (player.Step()) count++;
            Check(count == player.Count && !player.CanStep && !player.Step(), "each step consumed once");
            player.Reset();
            Check(player.Index == 0 && player.NowMs == 0 && player.CanStep
                && JsonSerializer.Serialize(player.View) == initial, "reset clears replay engine/time");
        });

        var invalid = new Dictionary<string, string>
        {
            ["malformed JSON"] = "{",
            ["empty input"] = "",
            ["root not object"] = "[]",
            ["duplicate root key"] = good.Replace("\"schema\":", "\"schema\":\"rift-replay/1\",\"schema\":"),
            ["unknown schema"] = good.Replace("rift-replay/1", "rift-replay/2"),
            ["unknown origin"] = good.Replace("\"synthetic\"", "\"live\""),
            ["unknown kind"] = good.Replace("\"sample\"", "\"actuate\""),
            ["unknown root field"] = good.Replace("\"name\":", "\"extra\":1,\"name\":"),
            ["unknown status"] = good.Replace("\"present\"", "\"guess\""),
            ["numeric status"] = good.Replace("\"present\"", "0"),
            ["case variant status"] = good.Replace("\"present\"", "\"Present\""),
            ["missing field"] = good.Replace("\"sequence\":1,", ""),
            ["missing kind"] = good.Replace("\"kind\":\"sample\",", ""),
            ["negative time"] = good.Replace("\"atMs\":10", "\"atMs\":-1"),
            ["nonnumeric time"] = good.Replace("\"atMs\":10", "\"atMs\":\"10\""),
            ["fractional time"] = good.Replace("\"atMs\":10", "\"atMs\":10.5"),
            ["overflow integer"] = good.Replace("\"atMs\":10", "\"atMs\":9223372036854775808"),
            ["overflow coordinate"] = good.Replace("\"x\":0", "\"x\":1e999"),
            ["unknown nested field"] = good.Replace("\"x\":0", "\"extra\":1,\"x\":0"),
            ["duplicate coordinate"] = good.Replace("\"x\":0", "\"x\":0,\"x\":1"),
            ["escaped duplicate coordinate"] = good.Replace("\"x\":0", "\"x\":0,\"\\u0078\":1"),
            ["duplicate step field"] = good.Replace("\"sequence\":1", "\"sequence\":1,\"sequence\":2"),
            ["unknown step field"] = good.Replace("\"sequence\":1", "\"sequence\":1,\"extra\":0"),
            ["nonpresent with value"] = good.Replace("\"present\"", "\"missing\""),
            ["present null value"] = good.Replace("{\"x\":0,\"y\":-10,\"z\":20}", "null"),
            ["ID length"] = good.Replace("spec-session", new string('a', 129)),
            ["control ID"] = good.Replace("spec-session", "bad\\u0001id"),
            ["unpaired escaped surrogate"] = good.Replace("spec-session", "bad\\uD800"),
            ["depth limit"] = good.Replace("\"name\":\"spec\"", "\"name\":[[[[[[[[[0]]]]]]]]]"),
            ["comment"] = good.Replace("\"name\":", "/* no */\"name\":"),
            ["trailing comma"] = good[..^1] + ",}",
            ["step limit"] = Document(ResetJson + "," + string.Join(",", Enumerable.Repeat("{\"kind\":\"tick\",\"atMs\":0}", 256))),
            ["first step not reset"] = Document("{\"kind\":\"tick\",\"atMs\":0}"),
            ["empty steps"] = Document(""),
            ["reset epoch reused"] = Document(ResetJson + "," + ResetJson),
            ["gap reason control"] = Document(ResetJson + ",{\"kind\":\"gap\",\"atMs\":1,\"reason\":\"bad\\u0001reason\"}"),
            ["gap reason length"] = Document(ResetJson + ",{\"kind\":\"gap\",\"atMs\":1,\"reason\":\"" + new string('a', 257) + "\"}"),
            ["time decreases"] = Document(ResetJson + "," + SampleJson + ",{\"kind\":\"tick\",\"atMs\":9}"),
            ["future receipt"] = good.Replace("\"receivedMs\":10", "\"receivedMs\":11"),
            ["inverted capture"] = good.Replace("\"acquiredStartMs\":0", "\"acquiredStartMs\":1")
        };
        foreach (var pair in invalid)
            Test("reader rejects " + pair.Key, () => Reject(pair.Value));

        Test("malformed UTF8 atomically rejected", () =>
        {
            using var stream = new MemoryStream(new byte[] { 0x7b, 0x22, 0xc3, 0x28, 0x22, 0x7d });
            var result = ReplayReader.Read(stream);
            Check(!result.Success && result.Scenario is null, "strict UTF8 rejection");
        });
        Test("UTF8 BOM rejected explicitly", () =>
        {
            using var stream = new MemoryStream(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(good)).ToArray());
            Check(!ReplayReader.Read(stream).Success, "no BOM ambiguity");
        });
        Test("exact byte bound accepted and next byte rejected", () =>
        {
            string atLimit = good + new string(' ', 65536 - Encoding.UTF8.GetByteCount(good));
            Check(Read(atLimit).Success, "exact byte boundary");
            Reject(atLimit + " ");
        });
        Test("unseekable oversize stream bounded", () =>
        {
            using var stream = new UnseekableBytes(Encoding.UTF8.GetBytes(new string(' ', 200000)));
            var result = ReplayReader.Read(stream);
            Check(!result.Success && result.Scenario is null && stream.BytesRead <= 65537, "bounded sentinel read, no partial scenario");
        });
        Test("disposed and null streams return typed failure", () =>
        {
            var stream = new MemoryStream();
            stream.Dispose();
            Check(!ReplayReader.Read(stream).Success && !ReplayReader.Read(null!).Success, "expected stream failures contained");
        });
        Test("explicit null missing sample accepted", () =>
        {
            string missing = SampleJson.Replace("\"present\"", "\"missing\"").Replace("{\"x\":0,\"y\":-10,\"z\":20}", "null");
            Check(Read(Document(ResetJson + "," + missing)).Success, "typed missing observation");
        });
        Test("semantic old context reaches reducer rejection", () =>
        {
            var result = Read(Document(ResetJson + "," + SampleJson.Replace("spec-session", "old-session")));
            Check(result.Success && result.Scenario is not null, "structurally valid adversarial scenario");
            var player = new ReplayPlayer(result.Scenario!);
            player.Step(); player.Step();
            Check(player.View.Position is null && !player.View.CanObservePosition, "old context cannot populate view");
        });
        Test("duplicate sequence trace preserves file order", () =>
        {
            var result = Read(Document(ResetJson + "," + SampleJson + "," + SampleJson.Replace("\"x\":0", "\"x\":999")));
            Check(result.Success && result.Scenario is not null, "duplicate sequence allowed as semantic test");
            var player = new ReplayPlayer(result.Scenario!);
            while (player.Step()) { }
            Check(player.View.Position == CoreSpecs.Zero && player.LastResultCode == "nonadvancing_sequence", "same-time order preserves first accepted observation");
        });
        Test("player snapshots caller-owned steps", () =>
        {
            var steps = new List<ReplayStep>
            {
                new ResetStep(0, CoreSpecs.Session, 1),
                new SampleStep(1, CoreSpecs.Sample(acquired: 1, received: 1))
            };
            var scenario = new ReplayScenario("mutable-input", steps);
            var player = new ReplayPlayer(scenario);
            steps.Clear();
            Check(player.Count == 2 && player.Step() && player.Step() && !player.Step(), "owned immutable copy");
            Check(player.View.Position == CoreSpecs.Zero, "caller mutation has no effect");
            if (scenario.Steps is IList<ReplayStep> list)
                Throws<NotSupportedException>(() => list[0] = new TickStep(0));
        });
        Test("public scenario count bounded before playback", () =>
        {
            Throws<ArgumentException>(() => new ReplayScenario("large", Enumerable.Repeat<ReplayStep>(new TickStep(0), 257).ToArray()));
        });
        Test("public invalid setup rejected before playback", () =>
        {
            Throws<ArgumentException>(() => new ReplayPlayer(new ReplayScenario("bad", new ReplayStep[] { new TickStep(0) })));
            Throws<ArgumentException>(() => new ReplayPlayer(new ReplayScenario("unknown", new ReplayStep[]
            {
                new ResetStep(0, CoreSpecs.Session, 1), new UnknownStep(1)
            })));
        });
        Test("256-step exact count accepted", () =>
        {
            var result = Read(Document(ResetJson + "," + string.Join(",", Enumerable.Repeat("{\"kind\":\"tick\",\"atMs\":0}", 255))));
            Check(result.Success && result.Scenario?.Steps.Count == 256, "exact step boundary");
        });
    }

    private static string OracleRow(ReplayPlayer player)
    {
        PositionView view = player.View;
        Check(view.Mode == "SyntheticReplay" && !view.CanAutomate, "synthetic-only trace");
        Check(view.SessionId == (player.Index == 0 ? "unavailable" : "synthetic-demo")
            && view.BuildId == (player.Index == 0 ? "unavailable" : "synthetic-v1"), "exact demo identity");
        static string Position(PlayerPosition? p) => p is { } q ? FormattableString.Invariant($"{q.X},{q.Y},{q.Z}") : "-";
        static string Age(long? n) => n?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        return FormattableString.Invariant($"{player.Index}|{player.NowMs}|{view.Epoch}|{view.Revision}|{view.Status}|{Position(view.Position)}|{Position(view.LastKnownPosition)}|{Age(view.AcquisitionAgeMs)}|{Age(view.ReceiveAgeMs)}|{view.SourceId}|{view.Reason}|{view.CanObservePosition}|{player.LastResultCode}");
    }

    private sealed record UnknownStep(long AtMs) : ReplayStep(AtMs);

    private sealed class UnseekableBytes(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
