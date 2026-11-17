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
        test("target vitals v2 expired freshness cannot revive after clock rollback", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out _), "initial candidate");
            check(!intake.TryGetFresh(1501, 500, out _), "expired");
            check(!intake.TryGetFresh(1500, 500, out var revived) &&
                revived is null && intake.Faulted && intake.LastAccepted is null,
                "backward query latches and clears stale data");
            check(!intake.TryAccept(Frame(Good), 1600, out _), "latched reader refuses further frames");
            intake.Reset();
            check(intake.TryAccept(Frame(Good), 800, out _) &&
                intake.TryGetFresh(800, 500, out _), "reset permits a new lower-clock session");
        });
        test("target vitals v2 rejects receipt timestamp older than prior freshness check", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out _), "initial");
            check(intake.TryGetFresh(1100, 500, out _), "freshness probe");
            var next = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            next["sequence"] = 3;
            next["clock"] = 13;
            check(!intake.TryAccept(Frame(next.ToJsonString()), 1099, out _) &&
                intake.Faulted && intake.LastAccepted is null, "delayed receipt cannot rewind caller clock");
        });
        test("target vitals v2 equal timestamps allowed but negative time invalidates", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out _) &&
                intake.TryGetFresh(1000, 0, out var fresh) && fresh is not null,
                "same-tick fresh query accepted");
            check(!intake.TryGetFresh(-1, 500, out _) &&
                intake.Faulted && intake.LastAccepted is null, "negative clock latches");
        });
        test("target vitals v2 invalid age bounds cannot bypass the clock watermark", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out _), "first");
            check(!intake.TryGetFresh(1200, -1, out _) &&
                !intake.TryGetFresh(1201, 5001, out _), "invalid freshness windows");
            check(intake.TryGetFresh(1201, 500, out _), "invalid age does not corrupt advancing query");
            check(!intake.TryGetFresh(1199, 500, out _) && intake.Faulted,
                "failed query still updates clock watermark");
        });
        test("target vitals v2 empty query fences later older receipt", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(!intake.TryGetFresh(2000, 500, out _), "empty is not fresh");
            check(!intake.TryAccept(Frame(Good), 1999, out _) &&
                intake.Faulted && intake.LastAccepted is null,
                "previous empty query still prevents time rollback");
        });
        test("target vitals v2 new frame may equal last freshness query timestamp", () =>
        {
            var intake = new TargetVitalsV2Intake();
            check(intake.TryAccept(Frame(Good), 1000, out _) &&
                intake.TryGetFresh(1100, 500, out _), "prior query");
            var next = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            next["sequence"] = 3;
            next["clock"] = 13;
            check(intake.TryAccept(Frame(next.ToJsonString()), 1100, out var accepted) &&
                accepted?.Sequence == 3 && intake.TryGetFresh(1100, 0, out _),
                "same caller clock tick across query and advancing new receipt");
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
        test("target vitals v2 scoped scan deadline rejects post-decode overrun", () =>
        {
            var source = new LiveScanResult(true, "complete", 1024, 1024, 1,
                TimeSpan.FromMilliseconds(90), [new ReadOnlyMemory<byte>([1, 2])], 1, 0, null)
            { TargetVitalsCandidates = [new ReadOnlyMemory<byte>([3, 4])] };
            var expired = WindowsMemoryScanner.FinalizeScopedCandidateScan(
                source, TimeSpan.FromMilliseconds(101), TimeSpan.FromMilliseconds(100));
            check(!expired.Completed && expired.Status == "time-budget-exceeded" &&
                expired.PlannedBytes == 1024 && expired.ScannedBytes == 1024 &&
                expired.Elapsed == TimeSpan.FromMilliseconds(101) &&
                expired.Candidates.Count == 0 && expired.TargetVitalsCandidates.Count == 0,
                "completion cannot expose a candidate after final decode overruns deadline");
            var boundary = WindowsMemoryScanner.FinalizeScopedCandidateScan(
                source, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            check(boundary.Completed && boundary.TargetVitalsCandidates.Count == 1 &&
                boundary.Elapsed == TimeSpan.FromMilliseconds(100),
                "deadline equality remains permitted without losing candidate diagnostics");
        });
        test("target vitals v2 scanner retains AFT2-only fast-scan region", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var frame = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good +
                "|ENDAFT2|ENDRR2L1");
            var addon = new EnvelopeCandidateAccumulator(nonce);
            var vitals = new TargetVitalsV2CandidateAccumulator(nonce);
            var region = new LiveCandidateRegion((nint)0x1000, 4096, (nint)0x1000, 0, 0, 0, 0);
            var scoped = new HashSet<LiveCandidateRegion>();
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(frame, addon, vitals,
                region, Array.Empty<LiveCandidateRegion>(), scoped);
            check(addon.ValidCandidates.Count == 0 && vitals.Candidates.Count == 1 &&
                scoped.SetEquals([region]), "AFT2-only memory discovered and scoped");
            var selection = TargetVitalsCandidateReporting.Build(new LiveScanResult(
                true, "complete", 4096, 4096, 1, TimeSpan.Zero, [], 0, 0, null)
                { TargetVitalsCandidates = vitals.Candidates }, nonce, true);
            check(selection.Status == "candidate-unqualified" && selection.Health == 40,
                "retained AFT2 can reach existing diagnostic selector");
        });
        test("target vitals v2 scanner retains both sides of contiguous AFT2 split", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var frame = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good +
                "|ENDAFT2|ENDRR2L1");
            var addon = new EnvelopeCandidateAccumulator(nonce);
            var vitals = new TargetVitalsV2CandidateAccumulator(nonce);
            var a = new LiveCandidateRegion((nint)0x1000, 4096, (nint)0x1000, 0, 0, 0, 0);
            var b = new LiveCandidateRegion((nint)0x2000, 4096, (nint)0x2000, 0, 0, 0, 0);
            var scoped = new HashSet<LiveCandidateRegion>();
            var split = frame.Length / 2;
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(frame.AsSpan(0, split), addon, vitals,
                a, [], scoped);
            check(scoped.Count == 0, "incomplete first half cannot create scope");
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(frame.AsSpan(split), addon, vitals,
                b, [a], scoped);
            check(vitals.Candidates.Count == 1 && scoped.SetEquals([a, b]),
                "completing AFT2 selects both adjacent regions");
            var isolatedAddon = new EnvelopeCandidateAccumulator(nonce);
            var isolatedVitals = new TargetVitalsV2CandidateAccumulator(nonce);
            var isolatedScope = new HashSet<LiveCandidateRegion>();
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(frame.AsSpan(0, split),
                isolatedAddon, isolatedVitals, a, [], isolatedScope);
            isolatedAddon.ResetMemoryBoundary();
            isolatedVitals.ResetMemoryBoundary();
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(frame.AsSpan(split),
                isolatedAddon, isolatedVitals, b, [], isolatedScope);
            check(isolatedVitals.Candidates.Count == 0 && isolatedScope.Count == 0,
                "noncontiguous memory never gets a synthesized envelope");
        });
        test("target vitals v2 scanner ignores malformed or wrong-nonce region", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var addon = new EnvelopeCandidateAccumulator(nonce);
            var vitals = new TargetVitalsV2CandidateAccumulator(nonce);
            var region = new LiveCandidateRegion((nint)0x1000, 4096, (nint)0x1000, 0, 0, 0, 0);
            var scoped = new HashSet<LiveCandidateRegion>();
            var wrong = Encoding.ASCII.GetBytes("RR2L1|fedcba9876543210fedcba9876543210|AFT2|" +
                Good + "|ENDAFT2|ENDRR2L1");
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(wrong, addon, vitals,
                region, [], scoped);
            var malformed = Encoding.ASCII.GetBytes("RR2L1|" + nonce +
                "|AFT2|bad|ENDAFT2|ENDRR2L1");
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(malformed, addon, vitals,
                region, [], scoped);
            check(vitals.Candidates.Count == 0 && addon.ValidCandidates.Count == 0 &&
                scoped.Count == 0, "unbound and malformed bytes cannot expand scope");
        });
        test("target vitals v2 scanner combines legacy and AFT2 region discoveries", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var old = new {
                schemaVersion = 1, addonVersion = "0.1.0", sequence = 1, epoch = 1,
                clock = new { status = "ok", value = 10.0 },
                player = new { status = "ok", value = "p" },
                fields = new {
                    coordX = new { status = "ok", value = 1.0 },
                    coordY = new { status = "ok", value = 0.0 },
                    coordZ = new { status = "ok", value = 2.0 },
                    health = new { status = "ok", value = 4.0 },
                    healthMax = new { status = "ok", value = 5.0 },
                    combat = new { status = "ok", value = false }
                }
            };
            var legacyFrame = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|RR2A1|" +
                System.Text.Json.JsonSerializer.Serialize(old) + "|ENDRR2A1|ENDRR2L1");
            var targetFrame = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good +
                "|ENDAFT2|ENDRR2L1");
            var addon = new EnvelopeCandidateAccumulator(nonce);
            var vitals = new TargetVitalsV2CandidateAccumulator(nonce);
            var regionA = new LiveCandidateRegion((nint)0x1000, 4096, (nint)0x1000, 0, 0, 0, 0);
            var regionB = new LiveCandidateRegion((nint)0x3000, 4096, (nint)0x3000, 0, 0, 0, 0);
            var scoped = new HashSet<LiveCandidateRegion>();
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(legacyFrame, addon, vitals,
                regionA, [], scoped);
            addon.ResetMemoryBoundary();
            vitals.ResetMemoryBoundary();
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(targetFrame, addon, vitals,
                regionB, [], scoped);
            WindowsMemoryScanner.FeedAndTrackCandidateRegions(targetFrame, addon, vitals,
                regionB, [], scoped);
            check(addon.ValidCandidates.Count == 1 && vitals.Candidates.Count >= 1 &&
                scoped.SetEquals([regionA, regionB]), "both formats share bounded deduplicated scope");
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
        test("target vitals v2 selector rejects mixed epochs in either memory order", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            byte[] Wrap(string json) => Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" +
                json + "|ENDAFT2|ENDRR2L1");
            var old = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            old["sequence"] = 100;
            old["clock"] = 100;
            var replacement = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            replacement["epoch"] = 2;
            replacement["sequence"] = 1;
            replacement["clock"] = 1;
            var a = Wrap(old.ToJsonString());
            var b = Wrap(replacement.ToJsonString());
            check(TargetVitalsV2CandidateSelector.Select([a, b], nonce, true) is null &&
                TargetVitalsV2CandidateSelector.Select([b, a], nonce, true) is null,
                "stale high sequence cannot win across producer epochs");
        });
        test("target vitals v2 selector rejects mixed player and target identities", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            byte[] Wrap(string json) => Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" +
                json + "|ENDAFT2|ENDRR2L1");
            var next = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            next["sequence"] = 3;
            next["clock"] = 13;
            var changedPlayer = next.DeepClone().AsObject();
            changedPlayer["playerId"] = "different-player";
            var changedTarget = next.DeepClone().AsObject();
            changedTarget["targetId"] = "different-target";
            check(TargetVitalsV2CandidateSelector.Select(
                [Wrap(Good), Wrap(changedPlayer.ToJsonString())], nonce, true) is null,
                "mixed players");
            check(TargetVitalsV2CandidateSelector.Select(
                [Wrap(Good), Wrap(changedTarget.ToJsonString())], nonce, true) is null,
                "mixed target generations");
        });
        test("target vitals v2 selector rejects producer clock inversion by sequence", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            byte[] Wrap(string json) => Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" +
                json + "|ENDAFT2|ENDRR2L1");
            var wrong = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            wrong["sequence"] = 3;
            wrong["clock"] = 12;
            var later = Wrap(wrong.ToJsonString());
            var first = Wrap(Good);
            check(TargetVitalsV2CandidateSelector.Select([first, later], nonce, true) is null &&
                TargetVitalsV2CandidateSelector.Select([later, first], nonce, true) is null,
                "higher sequence with older producer time");
        });
        test("target vitals v2 selector accepts reordered consistent copies but caps candidates", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            byte[] Wrap(string json) => Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" +
                json + "|ENDAFT2|ENDRR2L1");
            var next = System.Text.Json.Nodes.JsonNode.Parse(Good)!.AsObject();
            next["sequence"] = 3;
            next["clock"] = 13;
            next["health"] = 30;
            var old = Wrap(Good);
            var fresh = Wrap(next.ToJsonString());
            var selected = TargetVitalsV2CandidateSelector.Select([fresh, old, old], nonce, true);
            check(selected?.Observation.Sequence == 3 && selected.Observation.Health == 30,
                "memory enumeration order and identical older duplicates");
            var atCap = Enumerable.Repeat<ReadOnlyMemory<byte>>(old, 256).ToArray();
            check(TargetVitalsV2CandidateSelector.Select(atCap, nonce, true)?.Observation.Sequence == 2,
                "exact candidate limit remains valid");
            var excessive = Enumerable.Repeat<ReadOnlyMemory<byte>>(old, 257).ToArray();
            check(TargetVitalsV2CandidateSelector.Select(excessive, nonce, true) is null,
                "candidate cap prevents unbounded standalone selection");
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
            var reported = TargetVitalsCandidateReporting.Build(scan, nonce, true);
            check(reported.Status == "candidate-unqualified" && reported.Sequence == 2 &&
                reported.Epoch == 1 && reported.PlayerId == "p" && reported.TargetId == "t" &&
                reported.ProducerClock == 12.5 && reported.Health == 40 && reported.HealthMax == 100 &&
                reported.FrameSha256?.Length == 64 && reported.EnvelopeSha256?.Length == 64,
                "full candidate provenance retained without qualification");
            var wire = System.Text.Json.JsonSerializer.Serialize(reported,
                Rift.Automation.Guest.Infrastructure.JsonFormat.Options);
            using var decodedWire = System.Text.Json.JsonDocument.Parse(wire);
            check(decodedWire.RootElement.GetProperty("playerId").GetString() == "p" &&
                decodedWire.RootElement.GetProperty("producerClock").GetDouble() == 12.5,
                "published wire uses independent producer fields");
            var unavailable = TargetVitalsCandidateReporting.Build(scan, nonce, false);
            check(unavailable.Health is null && unavailable.PlayerId is null &&
                unavailable.ProducerClock is null && unavailable.EnvelopeSha256 is null,
                "source-identity failure redacts entire candidate");
            check(TargetVitalsCandidateReporting.Build(scan with { Completed = false }, nonce, true).Health is null, "incomplete");
            check(TargetVitalsCandidateReporting.Build(scan with { TargetVitalsCandidates = [] }, nonce, true).Status == "not-found", "empty");
        });
        test("target vitals v2 serialized guest report reaches physical host as unqualified tuple", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var id = Guid.NewGuid();
            var token = new string('b', 40);
            var binding = new Rift.Automation.Windows.PhysicalTelemetryBinding(
                id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var envelope = Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" + Good +
                "|ENDAFT2|ENDRR2L1");
            var scan = new LiveScanResult(true, "complete", 100, 100, 1, TimeSpan.Zero,
                Array.Empty<ReadOnlyMemory<byte>>(), 0, 0, null)
            { TargetVitalsCandidates = new ReadOnlyMemory<byte>[] { envelope } };
            var candidate = TargetVitalsCandidateReporting.Build(scan, nonce, true);
            var scanReport = new LiveScanReport("complete", true, 100, 100, 1, 0, 0, 0, 0, 0, null);
            var rr2 = new LiveRiftReader2Report(
                1, "unbound", "unavailable", null, 0, 0, null, null,
                5000, null, "not-found", null, null, null, null, null, scanReport);
            var when = new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);
            var snapshot = new LiveReaderSnapshot(
                1, id, nonce, 1, when.ToString("O"), 100, "running", null,
                new LiveTargetReport(binding.GameProcessId, binding.GameProcessStartUtcTicks,
                    binding.GameExecutablePath, binding.GameExecutableSha256, nonce),
                rr2, new LiveDeliveryReport(true, "ready", null, null, null))
            { TargetVitalsCandidate = candidate };
            var wire = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonFormat.Options);
            var host = new Rift.Automation.Windows.PhysicalTelemetryIngest(binding, token);
            check(host.Accept(wire, token, "127.0.0.1", when), "guest serializer delivered");
            var received = host.Latest?.TargetVitals;
            check(received is { PlayerId: "p", TargetId: "t", Epoch: 1, Sequence: 2,
                ProducerClock: 12.5, Health: 40, HealthMax: 100, SourceQualified: false } &&
                received.FrameSha256 == candidate.FrameSha256 &&
                received.EnvelopeSha256 == candidate.EnvelopeSha256,
                "physical host retains exact producer data without qualification");
        });
        test("target vitals v2 multi-scan guest-to-host lifecycle stays diagnostic only", () =>
        {
            const string nonce = "0123456789abcdef0123456789abcdef";
            var id = Guid.NewGuid();
            var token = new string('b', 40);
            var binding = new Rift.Automation.Windows.PhysicalTelemetryBinding(
                id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var host = new Rift.Automation.Windows.PhysicalTelemetryIngest(binding, token);
            var at = new DateTimeOffset(2026, 11, 16, 22, 0, 0, TimeSpan.Zero);
            byte[] Frame(long producerSequence, string targetId, double health) =>
                Encoding.ASCII.GetBytes("RR2L1|" + nonce + "|AFT2|" +
                    System.Text.Json.JsonSerializer.Serialize(new {
                        schemaVersion = 2, sequence = producerSequence, epoch = 1,
                        clock = producerSequence + 10.5, playerId = "p", targetId,
                        health, healthMax = 100.0
                    }) + "|ENDAFT2|ENDRR2L1");
            TargetVitalsCandidateDiagnostic Diagnostic(bool identityStable, params byte[][] frames)
            {
                // Each synthetic scan owns a fresh accumulator; heap copies in one
                // scan are evaluated together, not mistaken for a producer stream.
                var accumulator = new TargetVitalsV2CandidateAccumulator(nonce);
                foreach (var frame in frames)
                {
                    accumulator.ResetMemoryBoundary();
                    var split = frame.Length / 2;
                    accumulator.Feed(frame.AsSpan(0, split));
                    accumulator.Feed(frame.AsSpan(split));
                }
                var scan = new LiveScanResult(true, "complete", 4096, 4096, frames.Length,
                    TimeSpan.Zero, [], 0, 0, null)
                { TargetVitalsCandidates = accumulator.Candidates };
                return TargetVitalsCandidateReporting.Build(scan, nonce, identityStable);
            }
            var scanReport = new LiveScanReport("complete", true, 4096, 4096, 1, 0, 0, 0, 0, 0, null);
            var rr2 = new LiveRiftReader2Report(
                1, "unbound", "unavailable", null, 0, 0, null, null,
                5000, null, "not-found", null, null, null, null, null, scanReport);
            bool Deliver(long outer, TargetVitalsCandidateDiagnostic diagnostic)
            {
                var receivedAt = at.AddMilliseconds(outer * 100);
                var snapshot = new LiveReaderSnapshot(
                    1, id, nonce, outer, receivedAt.ToString("O"), outer * 100, "running", null,
                    new LiveTargetReport(binding.GameProcessId, binding.GameProcessStartUtcTicks,
                        binding.GameExecutablePath, binding.GameExecutableSha256, nonce),
                    rr2, new LiveDeliveryReport(true, "ready", null, null, null))
                { TargetVitalsCandidate = diagnostic };
                var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonFormat.Options);
                return host.Accept(json, token, "127.0.0.1", receivedAt);
            }
            var a1 = Frame(2, "target-a", 40);
            var a2 = Frame(3, "target-a", 30);
            var b1 = Frame(4, "target-b", 25);
            check(Deliver(1, Diagnostic(true, a1)), "first synthetic frame delivered");
            var first = host.Latest?.TargetVitals;
            check(first is { TargetId: "target-a", Sequence: 2, Health: 40,
                ProducerClock: 12.5, SourceQualified: false }, "initial producer tuple retained");
            check(Deliver(2, Diagnostic(true, a2, a1)), "advancing synthetic producer selected despite reverse heap order");
            var advanced = host.Latest?.TargetVitals;
            check(advanced is { TargetId: "target-a", Sequence: 3, Health: 30,
                ProducerClock: 13.5, SourceQualified: false } &&
                first is not null && advanced.FrameSha256 != first.FrameSha256, "genuine changed frame is still unqualified");
            check(Deliver(3, Diagnostic(true, a1, a2)), "repeated producer selected despite forward heap order and newer transport");
            check(host.Latest?.TargetVitals is { Sequence: 3, Health: 30, SourceQualified: false },
                "new outer receipt alone never advances producer or grants authority");
            var conflict = Diagnostic(true, a2, b1);
            check(conflict.Status == "conflict" && conflict.Health is null &&
                Deliver(4, conflict) && host.Latest?.TargetVitals is null &&
                host.Latest?.TargetVitalsStatus == "conflict",
                "mixed current and old targets suppress health without selecting arbitrary heap copy");
            check(Deliver(5, Diagnostic(true, b1)) &&
                host.Latest?.TargetVitals is { TargetId: "target-b", Sequence: 4, Health: 25,
                    SourceQualified: false }, "isolated new target remains diagnostic only");
            check(Deliver(6, Diagnostic(true)) &&
                host.Latest?.TargetVitals is null &&
                host.Latest?.TargetVitalsStatus == "not-found",
                "empty scan clears prior target");
            check(Deliver(7, Diagnostic(true, b1)) &&
                host.ReadFresh(at.AddMilliseconds(750), TimeSpan.FromSeconds(1))?.TargetVitals
                    is { TargetId: "target-b", SourceQualified: false },
                "reappearance is reported without inventing source freshness");
            check(Deliver(8, Diagnostic(false, b1)) &&
                host.Latest?.TargetVitals is null &&
                host.Latest?.TargetVitalsStatus == "unavailable",
                "source identity failure redacts all producer health");
            check(Deliver(9, Diagnostic(true, b1)) &&
                host.Latest?.TargetVitals is { TargetId: "target-b", SourceQualified: false },
                "new diagnostic can recover only as unqualified candidate");
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
