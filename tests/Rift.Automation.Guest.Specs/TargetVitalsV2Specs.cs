using System.Text;
using Rift.Automation.Guest.Infrastructure;
using Rift.Automation.Guest.Live;

internal static class TargetVitalsV2Specs
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        byte[] Frame(string payload) => Encoding.ASCII.GetBytes("AFT2|" + payload + "|ENDAFT2");
        const string Good = "{\"schemaVersion\":2,\"sequence\":2,\"epoch\":1,\"clock\":12.5,\"playerId\":\"p\",\"targetId\":\"t\",\"health\":40,\"healthMax\":100}";
        bool Rejects(string payload)
        {
            try { TargetVitalsV2Decoder.Decode(Frame(payload)); return false; }
            catch (InvalidDataException) { return true; }
        }
        test("target vitals v2 decodes bounded identity and health", () =>
        {
            var result = TargetVitalsV2Decoder.Decode(Frame(Good));
            check(result.TargetId == "t" && result.PlayerId == "p" && result.Health == 40 && result.Epoch == 1, "v2");
        });
        test("target vitals v2 rejects duplicate and unknown fields", () =>
        {
            check(Rejects(Good.Replace("\"health\":40", "\"health\":40,\"health\":20")) &&
                  Rejects(Good.Replace("\"health\":40", "\"unexpected\":4,\"health\":40")), "exact fields");
        });
        test("target vitals v2 rejects bad health and identity", () =>
        {
            check(Rejects(Good.Replace("\"health\":40", "\"health\":140")) &&
                  Rejects(Good.Replace("\"targetId\":\"t\"", "\"targetId\":\"\"")), "health and target");
        });
        test("target vitals v2 continuity accepts advancing same-target frames", () =>
        {
            var guard = new TargetVitalsV2Continuity();
            var first = TargetVitalsV2Decoder.Decode(Frame(Good));
            check(guard.Accept(first) && guard.Accept(first with { Sequence = 3, Clock = 13 }), "advancing");
        });
        test("target vitals v2 continuity latches target switch and replay", () =>
        {
            var first = TargetVitalsV2Decoder.Decode(Frame(Good));
            var guard = new TargetVitalsV2Continuity();
            check(guard.Accept(first) && !guard.Accept(first with { TargetId = "other", Sequence = 3, Clock = 13 }), "target switch");
            check(guard.Faulted && !guard.Accept(first with { Sequence = 4, Clock = 14 }), "latched");
            guard.Reset();
            check(guard.Accept(first), "explicit reset");
            check(!guard.Accept(first), "replay");
        });
        test("target vitals v2 continuity rejects epoch or clock regression", () =>
        {
            var first = TargetVitalsV2Decoder.Decode(Frame(Good));
            var guard = new TargetVitalsV2Continuity();
            guard.Accept(first);
            check(!guard.Accept(first with { Sequence = 3, Clock = 12.5 }), "clock");
            guard.Reset();
            guard.Accept(first);
            check(!guard.Accept(first with { Epoch = 2, Sequence = 3, Clock = 13 }), "epoch");
        });
        test("target vitals v2 intake decodes and tracks advancing receipts", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out var first) && first?.Health == 40, "first");
            var next = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            next["sequence"] = 3;
            next["clock"] = 13;
            check(intake.TryAccept(Frame(next.ToJsonString()), 1500, out var second) && second?.Sequence == 3, "next");
        });
        test("target vitals v2 intake rejects malformed frame and remains faulted", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(!intake.TryAccept(Frame("{}"), 100, out _) && intake.Faulted, "malformed");
            check(!intake.TryAccept(Frame(Good), 200, out _), "latched");
            intake.Reset();
            check(intake.TryAccept(Frame(Good), 300, out _), "reset");
        });
        test("target vitals v2 intake rejects reordered local receipt time", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out _), "first");
            check(!intake.TryAccept(Frame(Good), 1000, out _) && intake.Faulted && intake.LastAccepted is null, "receipt replay invalidates cached observation");
        });
        test("target vitals v2 intake invalidates cached observation after corrupt next frame", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 100, out _), "first");
            check(!intake.TryAccept(Frame("{}"), 200, out _) && intake.LastAccepted is null, "invalidate");
            check(!intake.TryAccept(Frame(Good), 300, out _) && intake.LastAccepted is null, "no stale exposure");
        });
        test("target vitals v2 intake exposes only bounded fresh receipts", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(!intake.TryGetFresh(1000, 1000, out _), "empty");
            check(intake.TryAccept(Frame(Good), 1000, out _), "first");
            check(intake.TryGetFresh(1500, 500, out var fresh) && fresh?.TargetId == "t", "boundary");
            check(!intake.TryGetFresh(1501, 500, out _), "expired");
            check(!intake.TryGetFresh(999, 500, out _), "clock regression");
            check(!intake.TryGetFresh(1001, 5001, out _), "invalid max age");
            check(!intake.TryAccept(Frame("{}"), 1600, out _), "fault");
            check(!intake.TryGetFresh(1600, 500, out _), "fault blocks cached sample");
        });
        test("target vitals v2 rejects ASCII DEL and embedded control characters", () =>
        {
            check(Rejects(Good.Replace("targetId", "tar\\u007FgetId")) &&
                Rejects(Good.Replace("targetId", "tar\\ngetId")), "control bytes");
        });
        test("target vitals v2 envelope validates bound reader challenge", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var envelope = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good + "|ENDAFT2|ENDRR2L1");
            var result = TargetVitalsV2EnvelopeDecoder.Decode(envelope, nonce);
            check(result.Observation.TargetId == "t" && result.EnvelopeSha256.Length == 64, "bound envelope");
            bool mismatch;
            try { TargetVitalsV2EnvelopeDecoder.Decode(envelope, "fedcba9876543210fedcba9876543210"); mismatch = false; }
            catch (InvalidDataException) { mismatch = true; }
            check(mismatch, "nonce mismatch");
        });
        test("target vitals v2 envelope rejects legacy payload", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var envelope = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|RR2A1|" + Good + "|ENDRR2A1|ENDRR2L1");
            bool rejected;
            try { TargetVitalsV2EnvelopeDecoder.Decode(envelope, nonce); rejected = false; }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "legacy payload not target vitals");
        });
        test("target vitals v2 scanner discovers envelope across chunks", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var bytes = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good + "|ENDAFT2|ENDRR2L1");
            var scanner = new TargetVitalsV2CandidateAccumulator(nonce);
            scanner.Feed(bytes.AsSpan(0, 23));
            scanner.Feed(bytes.AsSpan(23));
            check(scanner.Candidates.Count == 1, "one candidate");
            check(TargetVitalsV2EnvelopeDecoder.Decode(scanner.Candidates[0].Span, nonce).Observation.Health == 40, "decoded");
        });
        test("target vitals v2 scanner ignores other challenge and memory boundaries", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var bytes = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good + "|ENDAFT2|ENDRR2L1");
            var scanner = new TargetVitalsV2CandidateAccumulator(nonce);
            scanner.Feed(bytes.AsSpan(0, 23));
            scanner.ResetMemoryBoundary();
            scanner.Feed(bytes.AsSpan(23));
            check(scanner.Candidates.Count == 0, "no cross-region joins");
        });
        test("target vitals v2 selection requires completed scan and highest sequence", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            byte[] Wrap(string json) => Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + json + "|ENDAFT2|ENDRR2L1");
            var next = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            next["sequence"] = 3;
            next["clock"] = 13;
            var frames = new ReadOnlyMemory<byte>[] { Wrap(Good), Wrap(next.ToJsonString()) };
            check(TargetVitalsV2CandidateSelector.Select(frames, nonce, false) is null, "incomplete");
            check(TargetVitalsV2CandidateSelector.Select(frames, nonce, true)?.Observation.Sequence == 3, "highest");
        });
        test("target vitals v2 selection rejects conflicting same-sequence envelopes", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            byte[] Wrap(string json) => Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + json + "|ENDAFT2|ENDRR2L1");
            var conflict = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            conflict["health"] = 30;
            var frames = new ReadOnlyMemory<byte>[] { Wrap(Good), Wrap(conflict.ToJsonString()) };
            check(TargetVitalsV2CandidateSelector.Select(frames, nonce, true) is null, "conflict");
        });
        test("target vitals v2 malformed candidate fails closed without throwing", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var good = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good + "|ENDAFT2|ENDRR2L1");
            var malformed = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|bad|ENDAFT2|ENDRR2L1");
            var frames = new ReadOnlyMemory<byte>[] { good, malformed };
            check(TargetVitalsV2CandidateSelector.Select(frames, nonce, true) is null, "malformed rejected");
            var scan = new LiveScanResult(true, "complete", 100, 100, 1, TimeSpan.Zero,
                Array.Empty<ReadOnlyMemory<byte>>(), 0, 0, null)
            { TargetVitalsCandidates = frames };
            check(TargetVitalsCandidateReporting.Build(scan, nonce, true).Status == "conflict", "report fail closed");
        });
        test("target vitals v2 reporting uses current scan and fails closed", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var frame = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good + "|ENDAFT2|ENDRR2L1");
            var scan = new LiveScanResult(true, "complete", 100, 100, 1, TimeSpan.Zero,
                Array.Empty<ReadOnlyMemory<byte>>(), 0, 0, null)
            { TargetVitalsCandidates = new ReadOnlyMemory<byte>[] { frame } };
            check(TargetVitalsCandidateReporting.Build(scan, nonce, true).Status == "candidate-unqualified", "current");
            check(TargetVitalsCandidateReporting.Build(scan, nonce, false).Health is null, "identity");
            check(TargetVitalsCandidateReporting.Build(scan with { Completed = false }, nonce, true).Health is null, "incomplete");
            check(TargetVitalsCandidateReporting.Build(scan with { TargetVitalsCandidates = [] }, nonce, true).Status == "not-found", "empty");
        });
        test("target vitals v2 preserves legacy RR2A1 isolation", () =>
        {
            bool rejected;
            try { TargetVitalsV2Decoder.Decode(Encoding.ASCII.GetBytes("RR2A1|" + Good + "|ENDRR2A1")); rejected = false; }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "separate protocol");
        });
    }
}
