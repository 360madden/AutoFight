using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Rift.Automation.Windows;

internal static class StandaloneTelemetryHostSessionSpecs
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);
    private const string Token = "synthetic-standalone-delivery-token-123456789";

    private sealed class FrozenClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static StandaloneTelemetryBinding Binding(
        string lab, string ip, string reader, string nonce, int processId,
        long startTicks, char hash) =>
        new(lab, ip, reader, nonce, processId, startTicks,
            @"C:\RIFT\rift_x64.exe", new string(hash, 64));

    private static string GuestObservation(
        StandaloneTelemetryBinding binding,
        long outerSequence = 21,
        bool gameAvailable = true)
    {
        JsonObject receipt = JsonNode.Parse(VmTelemetrySpecs.Receipt(Now))!.AsObject();
        JsonObject observation = receipt["observation"]!.AsObject();
        observation["instanceId"] = binding.ReaderInstanceId;
        observation["nonce"] = binding.ReaderNonce;
        observation["sequence"] = outerSequence;
        if (!gameAvailable)
        {
            observation["target"] = null;
        }
        else
        {
            JsonObject target = observation["target"]!.AsObject();
            target["processId"] = binding.GameProcessId;
            target["processStartUtcTicks"] = binding.GameProcessStartUtcTicks;
            target["executablePath"] = binding.GameExecutablePath;
            target["executableSha256"] = binding.GameExecutableSha256;
            target["readerSessionNonce"] = binding.ReaderNonce;
        }
        return observation.ToJsonString();
    }

    private static string Post(int port, string body, string token = Token,
        bool sendBody = true)
    {
        using var client = new TcpClient
        {
            ReceiveTimeout = 5000,
            SendTimeout = 5000
        };
        client.Connect("127.0.0.1", port);
        using NetworkStream stream = client.GetStream();
        byte[] payload = Encoding.UTF8.GetBytes(body);
        byte[] header = Encoding.ASCII.GetBytes(
            "POST /observation HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            $"X-RiftReader-Token: {token}\r\n" +
            $"Content-Length: {payload.Length}\r\n\r\n");
        stream.Write(header);
        if (sendBody) stream.Write(payload);
        client.Client.Shutdown(SocketShutdown.Send);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return Encoding.ASCII.GetString(output.ToArray());
    }

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Two owned telemetry sessions independently accept and expire real socket observations", () =>
        {
            var firstBinding = Binding(
                "lab1", "192.168.56.128", "reader-test-01",
                "0123456789abcdef0123456789abcdef",
                1001, 1234567891011L, 'a');
            var secondBinding = Binding(
                "lab2", "192.168.56.129", "reader-test-02",
                "fedcba9876543210fedcba9876543210",
                2002, 2234567891011L, 'b');
            var clock = new FrozenClock(Now);
            using var first = StandaloneTelemetryHostSession.CreateLoopbackForTests(
                firstBinding, Token, clock);
            using var second = StandaloneTelemetryHostSession.CreateLoopbackForTests(
                secondBinding, Token, clock);
            using var stop = new CancellationTokenSource();
            Task firstRun = first.RunAsync(stop.Token);
            Task secondRun = second.RunAsync(stop.Token);
            try
            {
                check(first.BoundPortForTests > 0 && second.BoundPortForTests > 0 &&
                    first.BoundPortForTests != second.BoundPortForTests,
                    "independent loopback ports are bound");
                string lab1Response = Post(
                    first.BoundPortForTests, GuestObservation(firstBinding));
                string denied = Post(
                    first.BoundPortForTests, GuestObservation(firstBinding),
                    "wrong-token", sendBody: false);
                string lab2Response = Post(
                    second.BoundPortForTests, GuestObservation(secondBinding));
                var lab1 = first.ReadLatest();
                var lab2 = second.ReadLatest();
                check(lab1Response.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) &&
                    lab2Response.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) &&
                    denied.StartsWith("HTTP/1.1 403", StringComparison.Ordinal),
                    "transport accepts pinned tokens and rejects wrong token");
                check(lab1.HasPosition && lab2.HasPosition &&
                    lab1.Lab == "lab1" && lab2.Lab == "lab2" &&
                    lab1.GameProcessId == 1001 && lab2.GameProcessId == 2002 &&
                    lab1.X == 0 && lab1.Y == -4 && lab1.Z == 5.75 &&
                    lab2.X == 0 && lab2.Y == -4 && lab2.Z == 5.75 &&
                    !lab1.CanAutomate && !lab2.CanAutomate,
                    "independent authenticated streams are decoded without input authority");

                string unavailable = Post(first.BoundPortForTests,
                    GuestObservation(firstBinding, outerSequence: 22,
                        gameAvailable: false));
                check(unavailable.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) &&
                    !first.ReadLatest().HasPosition && second.ReadLatest().HasPosition,
                    "accepted unavailable transport clears only its own lab XYZ");

                clock.UtcNow = Now.AddSeconds(3);
                check(!second.ReadLatest().HasPosition &&
                    second.ReadLatest().Reason == "stale_or_clock_uncertain",
                    "polling cannot rejuvenate the other lab's stale XYZ");

                JsonObject wrongGame = JsonNode.Parse(
                    GuestObservation(secondBinding, outerSequence: 22))!.AsObject();
                wrongGame["target"]!["processId"] = 9999;
                string invalid = Post(second.BoundPortForTests, wrongGame.ToJsonString());
                check(invalid.StartsWith("HTTP/1.1 400", StringComparison.Ordinal) &&
                    !second.ReadLatest().HasPosition &&
                    second.ReadLatest().Reason == "identity_mismatch",
                    "authenticated wrong-game delivery fails closed at the host session");
            }
            finally
            {
                stop.Cancel();
                Task.WhenAll(firstRun, secondRun).GetAwaiter().GetResult();
            }
            check(firstRun.IsCompletedSuccessfully && secondRun.IsCompletedSuccessfully,
                "both owned receivers stop cleanly on cancellation");
        });
    }
}
