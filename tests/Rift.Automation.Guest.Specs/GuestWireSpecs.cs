using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rift.Automation.Guest.Domain;
using Rift.Automation.Guest.Infrastructure;
using Rift.Automation.Guest.Live;
using Rift.Automation.Windows;

internal static class GuestWireSpecs
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string OtherNonce = "fedcba9876543210fedcba9876543210";

    private static byte[] Frame(long sequence = 1, long epoch = 1,
        double clock = 10, string player = "player-1") =>
        Encoding.ASCII.GetBytes("RR2A1|" + JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            addonVersion = "0.1.0",
            sequence,
            epoch,
            clock = new { status = "ok", value = clock },
            player = new { status = "ok", value = player },
            fields = new
            {
                coordX = new { status = "ok", value = -1.5 },
                coordY = new { status = "ok", value = 0.0 },
                coordZ = new { status = "ok", value = 3.25 },
                health = new { status = "ok", value = 4.0 },
                healthMax = new { status = "ok", value = 5.0 },
                combat = new { status = "ok", value = false }
            }
        }) + "|ENDRR2A1");

    private static byte[] Envelope(
        byte[] frame, string challenge = Nonce) =>
        Encoding.ASCII.GetBytes(
            $"RR2L1|{challenge}|{Encoding.ASCII.GetString(frame)}|ENDRR2L1");

    private static bool Rejects(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    internal static void Run(Action<string, Action> test,
        Action<bool, string> check)
    {
        test("Guest addon decoder preserves zero negative XYZ and false combat", () =>
        {
            var result = AddonFrameDecoder.Decode(Frame(7, 2, 12.5));
            check(result.Sequence == 7 && result.Epoch == 2 &&
                result.Clock.Value == 12.5 && result.Fields.CoordX.Value == -1.5 &&
                result.Fields.CoordY.Value == 0 &&
                result.Fields.CoordZ.Value == 3.25 &&
                result.Fields.Combat.Value == false &&
                result.Player.Value == "player-1",
                "known RR2A1 wire schema remains intact");
        });

        test("Guest addon decoder rejects malformed and oversized frames", () =>
        {
            check(AddonFrameDecoder.MaxFrameBytes == 4096,
                "original bounded frame limit");
            check(Rejects(() => AddonFrameDecoder.Decode(
                    Encoding.ASCII.GetBytes("RR2A1|{}|ENDRR2A1"))) &&
                Rejects(() => AddonFrameDecoder.Decode(
                    Encoding.ASCII.GetBytes("RR2A1|ENDRR2A1"))) &&
                Rejects(() => AddonFrameDecoder.Decode(
                    new byte[AddonFrameDecoder.MaxFrameBytes + 1])),
                "bad framing or structure rejected before use");
        });

        test("Guest envelope validates nonce and preserves byte identities", () =>
        {
            byte[] frame = Frame(8, 2, 13);
            byte[] envelope = Envelope(frame);
            var decoded = LiveAddonEnvelopeDecoder.Decode(envelope, Nonce);

            check(LiveAddonEnvelopeDecoder.MaxEnvelopeBytes == 4144 &&
                decoded.Challenge == Nonce &&
                decoded.Observation.Sequence == 8 &&
                decoded.EnvelopeSha256 ==
                    Convert.ToHexString(SHA256.HashData(envelope)) &&
                decoded.AddonFrameSha256 ==
                    Convert.ToHexString(SHA256.HashData(frame)),
                "exact envelope and decoded frame agree");
        });

        test("Guest envelope rejects stale nonce and malformed markers", () =>
        {
            check(Rejects(() => LiveAddonEnvelopeDecoder.Decode(
                    Envelope(Frame(), OtherNonce), Nonce)) &&
                Rejects(() => LiveAddonEnvelopeDecoder.Decode(
                    Encoding.ASCII.GetBytes("RR2L2|" + Nonce + "|bad"), Nonce)) &&
                Rejects(() => LiveAddonEnvelopeDecoder.Decode(
                    new byte[LiveAddonEnvelopeDecoder.MaxEnvelopeBytes + 1],
                    Nonce)),
                "guest cannot accept another binding or unbounded envelope");
        });

        test("Guest candidate selection chooses latest with duplicate collapse", () =>
        {
            byte[] earlier = Envelope(Frame(3, 1, 3));
            byte[] latest = Envelope(Frame(4, 1, 4));
            byte[] stale = Envelope(Frame(99), OtherNonce);
            var selected = LiveAddonCandidateSelector.Select(
                new ReadOnlyMemory<byte>[]
                {
                    earlier, latest, latest.ToArray(), stale
                }, Nonce, true);

            check(selected.Status == LiveAddonSelectionStatus.Selected &&
                selected.Frame?.Observation.Sequence == 4 &&
                selected.MatchingCandidateCount == 3 &&
                selected.IdenticalCopyCount == 2,
                "same nonce and largest sequence win");
        });

        test("Guest candidate selection fails closed on conflicts and partial scans", () =>
        {
            byte[] one = Envelope(Frame(5, 1, 5, "player-A"));
            byte[] another = Envelope(Frame(5, 1, 5, "player-B"));
            var conflict = LiveAddonCandidateSelector.Select(
                new ReadOnlyMemory<byte>[] { one, another }, Nonce, true);
            var partial = LiveAddonCandidateSelector.Select(
                new ReadOnlyMemory<byte>[] { one }, Nonce, false);

            check(conflict.Status == LiveAddonSelectionStatus.Conflict &&
                conflict.Frame is null &&
                partial.Status == LiveAddonSelectionStatus.IncompleteScan &&
                partial.Frame is null,
                "identity conflict and incomplete memory scans never promote XYZ");
        });

        test("Guest candidate accumulator handles every chunk boundary", () =>
        {
            byte[] envelope = Envelope(Frame());
            for (int split = 1; split < envelope.Length; split++)
            {
                var accumulator = new EnvelopeCandidateAccumulator(Nonce);
                accumulator.Feed(envelope.AsSpan(0, split));
                accumulator.Feed(envelope.AsSpan(split));
                check(accumulator.ValidCandidates.Count == 1 &&
                    accumulator.ValidCandidates[0].Span.SequenceEqual(envelope),
                    "split " + split);
            }
        });

        test("Guest candidate accumulator does not join unrelated memory regions", () =>
        {
            byte[] envelope = Envelope(Frame());
            int split = envelope.Length / 2;
            var accumulator = new EnvelopeCandidateAccumulator(Nonce);
            accumulator.Feed(envelope.AsSpan(0, split));
            accumulator.ResetMemoryBoundary();
            accumulator.Feed(envelope.AsSpan(split));
            check(accumulator.ValidCandidates.Count == 0,
                "cross-region string fragments cannot form a candidate");
        });

        test("Guest accumulator rejects malformed current-attachment candidates", () =>
        {
            var malformed = Encoding.ASCII.GetBytes(
                $"RR2L1|{Nonce}|not-json|ENDRR2L1");
            var accumulator = new EnvelopeCandidateAccumulator(Nonce);
            accumulator.Feed(malformed);

            check(accumulator.ValidCandidates.Count == 0 &&
                accumulator.EndpointCandidateCount == 1 &&
                accumulator.MalformedCandidateCount == 1,
                "malformed candidate never becomes position");
        });

        test("Guest HTTP delivery configuration is restricted to private IPv4", () =>
        {
            new LiveDeliveryConfiguration(
                "http://192.168.56.1:9911/observation",
                new string('a', 32)).Validate();

            bool invalidPublic = Rejects(() =>
                new LiveDeliveryConfiguration(
                    "http://8.8.8.8:9911/observation",
                    new string('a', 32)).Validate());

            bool invalidPath = Rejects(() =>
                new LiveDeliveryConfiguration(
                    "http://192.168.56.1:9911/command",
                    new string('a', 32)).Validate());

            check(invalidPublic && invalidPath,
                "guest can send only to a private observation endpoint");
        });

        test("Guest default config remains bounded and AutoFight-owned", () =>
        {
            var config = LiveConfiguration.Defaults;
            config.Validate();
            check(config.DurationSeconds >= 480 &&
                config.DurationSeconds <= 3600 &&
                config.PollIntervalMilliseconds >= 250 &&
                config.MaxScanSeconds <= 60 &&
                config.ProcessName == "rift_x64" &&
                LiveConfiguration.FileName == "autofight-guest-reader.json",
                "known bounded options and own config name");
        });

        test("Guest serialized snapshot is accepted by AutoFight host without RR2", () =>
        {
            DateTimeOffset at =
                new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);
            Guid instance = Guid.Parse("11111111-2222-3333-4444-555555555555");
            const string guest = "192.168.56.128";
            const string token = "synthetic-guest-to-host-delivery-token-12345";
            string image = @"C:\RIFT\rift_x64.exe";
            string sha = new('a', 64);

            var scan = new LiveScanReport(
                "complete", true, 4096, 4096, 1, 10,
                1, 0, 1, 1, null);
            var decoded = AddonFrameDecoder.Decode(
                Frame(7, 2, 12.5));
            var report = new LiveRiftReader2Report(
                1, "bound", "fresh", null,
                1, 2, 2, 25.0, 5000.0, 4975.0,
                "selected", new string('b', 64), new string('c', 64),
                7, 2,
                AddonDecodedReport.ToWireObservation(decoded), scan);
            var target = new LiveTargetReport(
                1001, 1234567891011L, image, sha, Nonce);
            var snapshot = new LiveReaderSnapshot(
                1, instance, Nonce, 21,
                at.AddMilliseconds(-120).ToString("O"),
                1200, "running", null, target, report,
                new LiveDeliveryReport(true, "ready", null, null, null));

            var ingest = new StandaloneTelemetryIngest(
                new StandaloneTelemetryBinding(
                    "lab1", guest, instance.ToString("D"), Nonce,
                    1001, 1234567891011L, image, sha), token);

            byte[] wire = JsonSerializer.SerializeToUtf8Bytes(
                snapshot, JsonFormat.Options);
            var accepted = ingest.AcceptDelivery(wire, token, guest, at);

            check(accepted.Accepted && accepted.Snapshot.HasPosition &&
                accepted.Snapshot.X == -1.5 &&
                accepted.Snapshot.Y == 0 &&
                accepted.Snapshot.Z == 3.25 &&
                accepted.Snapshot.AddonSequence == 7 &&
                accepted.Snapshot.AddonEpoch == 2 &&
                !accepted.Snapshot.CanAutomate,
                "real guest serializer and host validator share a stable wire");

            var noTarget = snapshot with
            {
                Sequence = 22,
                Target = null,
                CapturedAtUtc = at.AddMilliseconds(380).ToString("O")
            };
            byte[] unavailableWire = JsonSerializer.SerializeToUtf8Bytes(
                noTarget, JsonFormat.Options);
            var unavailable = ingest.AcceptDelivery(
                unavailableWire, token, guest, at.AddMilliseconds(500));
            check(unavailable.Accepted && !unavailable.Snapshot.HasPosition,
                "valid producer without a game target is still delivered");

            var replay = ingest.AcceptDelivery(
                wire, token, guest, at.AddMilliseconds(600));
            check(!replay.Accepted &&
                replay.Snapshot.Reason == "delivery_replayed" &&
                !ingest.ReadLatest(at.AddMilliseconds(700)).HasPosition,
                "unavailable frame advances replay watermark");
        });

        test("Guest binary has no native-power or external RR2 assembly contract", () =>
        {
            var properties = typeof(LiveReaderSnapshot).GetProperties();
            check(!properties.Any(p => p.Name == "NativePower") &&
                typeof(LiveReaderSnapshot).Assembly.GetName().Name ==
                    "Rift.Automation.GuestReader",
                "only AutoFight guest assembly needed");
        });
    }
}
