using System.Text;
using System.Text.Json.Nodes;
using Rift.Automation.Windows;

internal static class StandaloneTelemetrySpecs
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);
    private const string GuestIp = "192.168.56.128";
    private const string Token = "synthetic-standalone-delivery-token-123456789";

    private static StandaloneTelemetryIngest NewIngest() => new(
        new StandaloneTelemetryBinding("lab1", GuestIp, "reader-test-01",
            "0123456789abcdef0123456789abcdef", 1001, 1234567891011L,
            @"C:\RIFT\rift_x64.exe", new string('a', 64)), Token);

    private static byte[] GuestObservation(
        DateTimeOffset at, Action<JsonObject>? modify = null)
    {
        JsonObject receipt =
            JsonNode.Parse(VmTelemetrySpecs.Receipt(at))!.AsObject();
        JsonObject guest = receipt["observation"]!.AsObject();
        modify?.Invoke(guest);
        return Encoding.UTF8.GetBytes(guest.ToJsonString());
    }

    internal static void Run(
        Action<string, Action> test, Action<bool, string> check)
    {
        test("Standalone telemetry starts unavailable then receives diagnostic XYZ", () =>
        {
            var ingest = NewIngest();
            var empty = ingest.ReadLatest(Now);
            check(!empty.HasPosition && empty.Reason == "delivery_not_received",
                "no fabricated initial position");

            var delivery = ingest.AcceptDelivery(
                GuestObservation(Now), Token, GuestIp, Now);
            var position = delivery.Snapshot;

            check(delivery.Accepted && delivery.Reason == "recorded" &&
                position.HasPosition && position.Lab == "lab1" &&
                position.X == 0 && position.Y == -4 && position.Z == 5.75 &&
                position.OuterSequence == 21 && position.AddonSequence == 20 &&
                position.GameProcessId == 1001 && !position.CanAutomate,
                "bound producer schema accepted; no action authority");
            check(ingest.ReadLatest(Now.AddMilliseconds(100)).HasPosition,
                "receipt revalidates without RiftReader2 receiver file");
        });

        test("Standalone wrong token and peer cannot replace valid telemetry", () =>
        {
            var ingest = NewIngest();
            ingest.Accept(GuestObservation(Now), Token, GuestIp, Now);
            var unauthorized = ingest.AcceptDelivery(
                GuestObservation(Now), "wrong-token", GuestIp,
                Now.AddMilliseconds(100));
            var wrongPeer = ingest.AcceptDelivery(
                GuestObservation(Now), Token, "192.168.56.129",
                Now.AddMilliseconds(120));

            check(!unauthorized.Accepted &&
                unauthorized.Snapshot.Reason == "delivery_unauthorized" &&
                !wrongPeer.Accepted &&
                wrongPeer.Snapshot.Reason == "delivery_wrong_guest",
                "untrusted deliveries rejected");
            check(ingest.ReadLatest(Now.AddMilliseconds(200)).HasPosition,
                "untrusted traffic does not poison accepted stream");
        });

        test("Standalone malformed and oversized deliveries invalidate XYZ", () =>
        {
            var ingest = NewIngest();
            ingest.Accept(GuestObservation(Now), Token, GuestIp, Now);
            var malformed = ingest.AcceptDelivery(
                Encoding.UTF8.GetBytes("{"), Token, GuestIp,
                Now.AddMilliseconds(100));

            check(!malformed.Accepted &&
                malformed.Snapshot.Reason == "delivery_invalid_json" &&
                !ingest.ReadLatest(Now.AddMilliseconds(150)).HasPosition,
                "malformed authenticated payload drops former position");

            var oversized = ingest.AcceptDelivery(
                new byte[StandaloneTelemetryIngest.MaximumObservationBytes + 1],
                Token, GuestIp, Now.AddMilliseconds(200));

            check(!oversized.Accepted &&
                oversized.Snapshot.Reason == "delivery_size_limit" &&
                !ingest.ReadLatest(Now.AddMilliseconds(250)).HasPosition,
                "oversized body does not resurrect former position");
        });

        test("Standalone game identity swap cannot inherit bound source", () =>
        {
            var ingest = NewIngest();
            ingest.Accept(GuestObservation(Now), Token, GuestIp, Now);
            var changed = GuestObservation(Now.AddMilliseconds(500), guest =>
            {
                guest["target"]!["processId"] = 1002;
                guest["sequence"] = 22;
            });

            var rejected = ingest.AcceptDelivery(
                changed, Token, GuestIp, Now.AddMilliseconds(500));

            check(!rejected.Accepted &&
                rejected.Snapshot.Reason == "identity_mismatch" &&
                !ingest.ReadLatest(Now.AddMilliseconds(600)).HasPosition,
                "foreign RIFT process cannot inherit reader identity");
        });

        test("Standalone replay and addon sequence rollback fail closed", () =>
        {
            var replay = NewIngest();
            byte[] first = GuestObservation(Now);
            check(replay.AcceptDelivery(first, Token, GuestIp, Now).Accepted,
                "first transport accepted");

            var duplicate = replay.AcceptDelivery(
                first, Token, GuestIp, Now.AddMilliseconds(500));

            check(!duplicate.Accepted &&
                duplicate.Snapshot.Reason == "delivery_replayed" &&
                !replay.ReadLatest(Now.AddMilliseconds(600)).HasPosition,
                "replayed packet invalidates former position");

            var regressed = NewIngest();
            regressed.Accept(first, Token, GuestIp, Now);
            var lowerProducer = GuestObservation(Now.AddMilliseconds(500), guest =>
            {
                guest["sequence"] = 22;
                guest["riftReader2"]!["observation"]!["sequence"] = 19;
            });

            var rejected = regressed.AcceptDelivery(
                lowerProducer, Token, GuestIp, Now.AddMilliseconds(500));

            check(!rejected.Accepted &&
                rejected.Snapshot.Reason == "delivery_sequence_regressed",
                "higher outer sequence cannot mask addon rollback");
        });

        test("Standalone stale polling cannot renew old XYZ", () =>
        {
            var ingest = NewIngest();
            ingest.Accept(GuestObservation(Now), Token, GuestIp, Now);
            var stale = ingest.ReadLatest(Now.AddSeconds(3));

            check(!stale.HasPosition &&
                stale.Reason == "stale_or_clock_uncertain",
                "rereading is not a new source event");

            var next = GuestObservation(Now.AddSeconds(3), guest =>
            {
                guest["sequence"] = 22;
                guest["riftReader2"]!["observation"]!["sequence"] = 21;
                guest["riftReader2"]!["observation"]!["clock"]!["value"] = 53.25;
            });

            var recovered = ingest.AcceptDelivery(
                next, Token, GuestIp, Now.AddSeconds(3));

            check(recovered.Accepted && recovered.Snapshot.HasPosition &&
                recovered.Snapshot.OuterSequence == 22 &&
                recovered.Snapshot.AddonSequence == 21 &&
                !recovered.Snapshot.CanAutomate,
                "new advancing source restores diagnostic XYZ");
        });

        test("Standalone unavailable guest frames advance transport sequence", () =>
        {
            var cases = new (string Name, Action<JsonObject> Mutate)[]
            {
                ("reader stopped", guest => guest["readerStatus"] = "terminated"),
                ("missing target", guest => guest["target"] = null),
                ("producer expired", guest =>
                    guest["riftReader2"]!["freshnessStatus"] = "expired")
            };

            foreach (var item in cases)
            {
                var ingest = NewIngest();
                check(ingest.Accept(GuestObservation(Now), Token, GuestIp, Now)
                    .HasPosition, "initial source");

                var unavailable = GuestObservation(
                    Now.AddMilliseconds(500), guest =>
                    {
                        guest["sequence"] = 22;
                        guest["riftReader2"]!["observation"]!["sequence"] = 21;
                        guest["riftReader2"]!["observation"]!["clock"]!["value"] = 50.75;
                        item.Mutate(guest);
                    });

                var noPosition = ingest.AcceptDelivery(
                    unavailable, Token, GuestIp, Now.AddMilliseconds(500));

                check(noPosition.Accepted && !noPosition.Snapshot.HasPosition,
                    item.Name + " accepted as transport without XYZ");

                var sameOuterValid = GuestObservation(
                    Now.AddMilliseconds(600), guest =>
                    {
                        guest["sequence"] = 22;
                        guest["riftReader2"]!["observation"]!["sequence"] = 22;
                        guest["riftReader2"]!["observation"]!["clock"]!["value"] = 50.85;
                    });

                var replay = ingest.AcceptDelivery(
                    sameOuterValid, Token, GuestIp, Now.AddMilliseconds(600));

                check(!replay.Accepted &&
                    replay.Snapshot.Reason == "delivery_replayed",
                    item.Name + " still advances replay high-water");
            }
        });

        test("Standalone valid advancing packets survive backward UTC correction", () =>
        {
            var ingest = NewIngest();
            check(ingest.Accept(GuestObservation(Now), Token, GuestIp, Now)
                .HasPosition, "baseline accepted");

            DateTimeOffset correctedUtc = Now.AddMilliseconds(-50);
            var next = GuestObservation(correctedUtc, guest =>
            {
                guest["sequence"] = 22;
                guest["riftReader2"]!["observation"]!["sequence"] = 21;
                guest["riftReader2"]!["observation"]!["clock"]!["value"] = 50.75;
            });

            var accepted = ingest.AcceptDelivery(
                next, Token, GuestIp, correctedUtc);

            check(accepted.Accepted && accepted.Snapshot.HasPosition &&
                accepted.Snapshot.OuterSequence == 22 &&
                accepted.Snapshot.AddonSequence == 21,
                "outer and addon order remain valid across UTC adjustment");
        });

        test("Standalone missing and duplicate fields fail closed without exceptions", () =>
        {
            var ingest = NewIngest();
            byte[] missing = GuestObservation(Now, guest =>
                guest.Remove("instanceId"));
            var noId = ingest.AcceptDelivery(missing, Token, GuestIp, Now);
            check(!noId.Accepted &&
                noId.Snapshot.Reason == "delivery_invalid_json",
                "required instance absence is a typed error");

            string valid = Encoding.UTF8.GetString(GuestObservation(Now));
            string duplicate = valid.Replace(
                "\"schemaVersion\":1,", "\"schemaVersion\":1,\"schemaVersion\":1,",
                StringComparison.Ordinal);
            var duplicated = ingest.AcceptDelivery(
                Encoding.UTF8.GetBytes(duplicate), Token, GuestIp, Now);
            check(!duplicated.Accepted &&
                duplicated.Snapshot.Reason == "delivery_duplicate_field" &&
                !ingest.ReadLatest(Now).HasPosition,
                "ambiguous duplicate roots never promote game state");
        });

        test("Standalone listener rejects unsafe binding before opening sockets", () =>
        {
            static bool Accept(
                ReadOnlyMemory<byte> body, string token,
                string peer, DateTimeOffset received) => true;

            void Reject(string host, int port, string guest, string token)
            {
                bool rejected = false;
                try
                {
                    using var listener = new StandaloneTelemetryHttpListener(
                        host, port, guest, token, Accept);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                check(rejected, "unsafe listener must reject configuration");
            }

            Reject("0.0.0.0", 9000, GuestIp, Token);
            Reject("192.168.56.1", 80, GuestIp, Token);
            Reject("192.168.56.1", 9000, "8.8.8.8", Token);
            Reject("192.168.56.1", 9000, GuestIp, "short");
        });
    }
}
