using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Rift.Automation.Windows;

internal static class PhysicalTelemetryIngestSpecs
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical receiver rotation refuses stale session before touching game", () =>
        {
            var tracker = new PhysicalReaderSessionTracker();
            var signal = new PhysicalReaderSessionSignal(Guid.NewGuid(), new string('a', 32));
            using var cancellation = new CancellationTokenSource();
            var rotation = new PhysicalReceiverRotation(tracker, 999999, 49196, new string('b', 40));
            bool rejected = false;
            try { rotation.RotateAsync(signal, 1, cancellation.Token).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "untracked session rejected");
            rotation.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
        test("Physical addon preflight reports missing files without touching game", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "af-addon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var warning = PhysicalAddonReloadPreflight.Inspect(37928, directory);
                check(warning is not null && warning.Contains("missing"), "missing addon files reported");
            }
            finally { Directory.Delete(directory); }
        });
        test("Physical coordinator refuses invalid reader executable before launch", () =>
        {
            bool rejected = false;
            try
            {
                PhysicalTelemetryCoordinator.RunAsync(@"C:\\Windows\\System32\\cmd.exe",
                    999999, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "coordinator cannot launch arbitrary executable");
        });
        test("Physical receiver concurrent stale rotations do not start a listener", () =>
        {
            var tracker = new PhysicalReaderSessionTracker();
            var signal = new PhysicalReaderSessionSignal(Guid.NewGuid(), new string('a', 32));
            using var stop = new CancellationTokenSource();
            var rotation = new PhysicalReceiverRotation(tracker, 999999, 49197, new string('b', 40));
            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () => {
                try { await rotation.RotateAsync(signal, 1, stop.Token); return false; }
                catch (InvalidOperationException) { return true; }
            })).ToArray();
            Task.WaitAll(tasks);
            check(tasks.All(task => task.Result), "all stale rotations rejected");
            rotation.StopAsync().GetAwaiter().GetResult();
            rotation.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });
        test("Physical reader supervisor refuses unpinned executable paths", () =>
        {
            var tracker = new PhysicalReaderSessionTracker();
            Task Callback(PhysicalReaderSessionSignal signal, long generation, CancellationToken ct) =>
                Task.CompletedTask;
            foreach (var path in new[] { "reader.exe", @"C:\\Windows\\System32\\cmd.exe",
                @"C:\\not-present\\Rift.Automation.GuestReader.exe" })
            {
                bool rejected = false;
                try
                {
                    PhysicalReaderProcessSupervisor.RunAsync(path, tracker, Callback,
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (ArgumentException) { rejected = true; }
                check(rejected, "invalid executable rejected");
            }
        });
        test("Reader monitor forwards only bounded status lines", () =>
        {
            var valid = "2026-11-16T03:30:00Z scan=complete selection=NotFound freshness=Unavailable ageMs=- delivery=delivered-http-200";
            var malicious = "Bearer SECRET scan=complete selection=NotFound freshness=Unavailable ageMs=- delivery=delivered-http-200";
            using var input = new StringReader("arbitrary secret\n" + valid + "\n" + malicious + "\n");
            var forwarded = new List<string>();
            PhysicalReaderSessionMonitor.MonitorAsync(input, new PhysicalReaderSessionTracker(),
                (_, _, _) => Task.CompletedTask, CancellationToken.None,
                line => forwarded.Add(line)).GetAwaiter().GetResult();
            check(forwarded.Count == 1 && forwarded[0] == valid, "only approved status relayed");
        });
        test("Physical reader stdout monitor observes rotations and revokes on EOF", () =>
        {
            var id = Guid.NewGuid();
            string Event(char c) => JsonSerializer.Serialize(new {
                eventType = "autofight-reader-session-v1", instanceId = id, nonce = new string(c, 32)
            });
            using var input = new StringReader("noise\n" + Event('a') + "\n" + Event('a') + "\n" + Event('b') + "\n");
            var tracker = new PhysicalReaderSessionTracker();
            var observed = new List<string>();
            PhysicalReaderSessionMonitor.MonitorAsync(input, tracker, (signal, generation, ct) => {
                check(tracker.IsCurrent(signal, generation), "callback pinned");
                observed.Add(signal.Nonce);
                return Task.CompletedTask;
            }, CancellationToken.None).GetAwaiter().GetResult();
            check(observed.Count == 2 && observed[0] == new string('a', 32) &&
                observed[1] == new string('b', 32), "only changes dispatched");
            check(tracker.Snapshot().Signal is null, "EOF revokes session");
        });
        test("Physical reader stdout monitor revokes session when callback fails", () =>
        {
            var id = Guid.NewGuid();
            var line = JsonSerializer.Serialize(new {
                eventType = "autofight-reader-session-v1", instanceId = id, nonce = new string('a', 32)
            });
            using var input = new StringReader(line + "\n");
            var tracker = new PhysicalReaderSessionTracker();
            bool threw = false;
            try
            {
                PhysicalReaderSessionMonitor.MonitorAsync(input, tracker,
                    (_, _, _) => throw new InvalidOperationException("callback failed"),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { threw = true; }
            check(threw && tracker.Snapshot().Signal is null, "callback fault revokes pin");
        });
        test("Physical reader session signal strictly parses startup identity", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var valid = JsonSerializer.Serialize(new { eventType = "autofight-reader-session-v1", instanceId = id, nonce });
            check(PhysicalReaderSessionSignal.TryParse(valid, out var parsed), "valid event");
            check(parsed?.InstanceId == id && parsed.Nonce == nonce, "identity");
            check(!PhysicalReaderSessionSignal.TryParse("ordinary reader output", out _), "non-event");
            check(!PhysicalReaderSessionSignal.TryParse(valid.Replace("autofight-reader-session-v1", "unknown"), out _), "wrong type");
            check(!PhysicalReaderSessionSignal.TryParse(valid.Replace(nonce, nonce.ToUpperInvariant()), out _), "uppercase nonce");
            check(!PhysicalReaderSessionSignal.TryParse(valid.Replace("}", ",\\\"nonce\\\":\\\"bbbb\\\"}"), out _), "duplicate key");
            check(!PhysicalReaderSessionSignal.TryParse(valid.Replace("}", ",\\\"deliveryToken\\\":\\\"secret\\\"}"), out _), "unknown field");
            check(!PhysicalReaderSessionSignal.TryParse(new string('x', 1025), out _), "bounded length");
        });
        test("Physical reader session tracker invalidates old nonce and generation", () =>
        {
            var tracker = new PhysicalReaderSessionTracker();
            var id = Guid.NewGuid();
            string Event(string nonce) => JsonSerializer.Serialize(new {
                eventType = "autofight-reader-session-v1", instanceId = id, nonce
            });
            check(!tracker.ObserveLine("ordinary output"), "ignore non-event");
            check(tracker.ObserveLine(Event(new string('a', 32))), "initial event");
            var first = tracker.Snapshot();
            check(first.Signal is not null && tracker.IsCurrent(first.Signal, first.Generation), "initial pin");
            check(!tracker.ObserveLine(Event(new string('a', 32))), "duplicate event");
            check(tracker.Snapshot().Generation == first.Generation, "duplicate keeps generation");
            check(tracker.ObserveLine(Event(new string('b', 32))), "nonce rotation");
            check(!tracker.IsCurrent(first.Signal!, first.Generation), "old pin revoked");
            var second = tracker.Snapshot();
            check(second.Signal?.Nonce == new string('b', 32) && second.Generation > first.Generation, "new pin");
            tracker.Invalidate();
            check(tracker.Snapshot().Signal is null && !tracker.IsCurrent(second.Signal!, second.Generation),
                "reader exit invalidates pin");
        });
        test("Physical ingest pins session process and monotonic sequence", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var ingest = new PhysicalTelemetryIngest(binding, token);
            byte[] Frame(long seq, int pid = 1234, string? session = null) =>
                JsonSerializer.SerializeToUtf8Bytes(new {
                    schemaVersion = 1, instanceId = id, nonce = session ?? nonce, sequence = seq,
                    readerStatus = "running",
                    target = new {
                        processId = pid, processStartUtcTicks = binding.GameProcessStartUtcTicks,
                        executablePath = binding.GameExecutablePath,
                        executableSha256 = binding.GameExecutableSha256,
                        readerSessionNonce = nonce
                    },
                    targetVitalsCandidate = new { status = "candidate-unqualified", sequence = 7 }
                });
            var now = DateTimeOffset.UtcNow;
            check(!ingest.Accept(Frame(1), "wrong", "127.0.0.1", now), "token");
            check(!ingest.Accept(Frame(1), token, "192.168.56.1", now), "peer");
            check(!ingest.Accept(Frame(1, 99), token, "127.0.0.1", now), "game PID");
            check(!ingest.Accept(Frame(1, session: new string('d', 32)), token, "127.0.0.1", now), "nonce");
            var badStatus = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Frame(1))
                .Replace("running", "invented", StringComparison.Ordinal));
            check(!ingest.Accept(badStatus, token, "127.0.0.1", now), "unknown reader status");
            check(ingest.Latest is null, "rejected traffic cannot populate state");
            check(ingest.Accept(Frame(1), token, "127.0.0.1", now), "valid");
            check(ingest.Latest?.TargetVitalsSequence == 7, "vitals receipt");
            check(!ingest.Accept(Frame(1), token, "127.0.0.1", now), "replay");
            check(ingest.Accept(Frame(2), token, "127.0.0.1", now), "advance");
            check(ingest.Latest?.Sequence == 2, "latest");
            check(ingest.ReadFresh(now.AddMilliseconds(100), TimeSpan.FromSeconds(1))?.Sequence == 2, "fresh");
            check(ingest.ReadFresh(now.AddSeconds(2), TimeSpan.FromSeconds(1)) is null, "expired");
            check(ingest.ReadFresh(now.AddSeconds(-1), TimeSpan.FromSeconds(1)) is null, "future receipt");
        });
        test("Physical ingest does not expose vitals after game identity becomes unavailable", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var ingest = new PhysicalTelemetryIngest(binding, token);
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion = 1, instanceId = id, nonce, sequence = 1,
                readerStatus = "terminated", target = (object?)null,
                targetVitalsCandidate = new { status = "candidate-unqualified", sequence = 9 }
            });
            check(ingest.Accept(payload, token, "127.0.0.1", DateTimeOffset.UtcNow), "termination recorded");
            check(ingest.Latest?.TargetVitalsStatus is null &&
                ingest.Latest?.TargetVitalsSequence is null, "unqualified candidate withheld");
            byte[] restart = JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion = 1, instanceId = id, nonce, sequence = 2,
                readerStatus = "running", target = (object?)null
            });
            check(!ingest.Accept(restart, token, "127.0.0.1", DateTimeOffset.UtcNow), "terminal session cannot revive");
            check(ingest.Latest?.ReaderStatus == "terminated", "terminal receipt retained");
        });
        test("Physical ingest rejects invalid target-vitals diagnostic status and sequence", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var ingest = new PhysicalTelemetryIngest(binding, token);
            byte[] Frame(string status, object? sequence) => JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion = 1, instanceId = id, nonce, sequence = 1,
                readerStatus = "running",
                target = new {
                    processId = binding.GameProcessId,
                    processStartUtcTicks = binding.GameProcessStartUtcTicks,
                    executablePath = binding.GameExecutablePath,
                    executableSha256 = binding.GameExecutableSha256,
                    readerSessionNonce = nonce
                },
                targetVitalsCandidate = new { status, sequence }
            });
            var now = DateTimeOffset.UtcNow;
            check(!ingest.Accept(Frame("unknown", 7), token, "127.0.0.1", now), "unknown status");
            check(!ingest.Accept(Frame("candidate-unqualified", -1), token, "127.0.0.1", now), "negative sequence");
            check(!ingest.Accept(Frame("not-found", 7), token, "127.0.0.1", now), "status sequence mismatch");
            check(!ingest.Accept(Frame("candidate-unqualified", "7"), token, "127.0.0.1", now), "wrong sequence type");
            check(ingest.Latest is null, "no invalid receipt");
            check(ingest.Accept(Frame("candidate-unqualified", 7), token, "127.0.0.1", now), "valid after failures");
        });
        test("Physical host session accepts authenticated HTTP and rejects replay", () =>
        {
            var binding = new PhysicalTelemetryBinding(Guid.NewGuid(), new string('a', 32),
                1234, 638000000000000000, @"C:\RIFT\rift_x64.exe", new string('c', 64));
            string token = new('b', 40);
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            using var session = new PhysicalTelemetryHostSession(binding, port, token);
            using var stop = new CancellationTokenSource();
            Task run = session.RunAsync(stop.Token);
            using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            string Send(long sequence, string suppliedToken)
            {
                var payload = JsonSerializer.Serialize(new {
                    schemaVersion = 1, instanceId = binding.ReaderInstanceId,
                    nonce = binding.ReaderNonce, sequence, readerStatus = "running",
                    target = new {
                        processId = binding.GameProcessId,
                        processStartUtcTicks = binding.GameProcessStartUtcTicks,
                        executablePath = binding.GameExecutablePath,
                        executableSha256 = binding.GameExecutableSha256,
                        readerSessionNonce = binding.ReaderNonce
                    }
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/observation")
                { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
                request.Headers.Add("X-RiftReader-Token", suppliedToken);
                try
                {
                    using var response = client.Send(request);
                    return $"HTTP/1.1 {(int)response.StatusCode}";
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException($"HTTP sequence={sequence}: {ex}", ex);
                }
            }
            try
            {
                check(Send(1, token).StartsWith("HTTP/1.1 200", StringComparison.Ordinal), "accepted");
                check(session.Latest?.Sequence == 1, "recorded");
                check(Send(1, token).StartsWith("HTTP/1.1 400", StringComparison.Ordinal), "replay");
                // Wrong-token rejection is covered by the header-only socket test.
                // An early 403 can close before HttpClient finishes streaming its body.
                check(Send(2, token).StartsWith("HTTP/1.1 200", StringComparison.Ordinal), "advanced");
                check(session.Latest?.Sequence == 2, "latest");
            }
            finally
            {
                stop.Cancel();
                run.GetAwaiter().GetResult();
            }
        });
        test("Physical ingest malformed and missing fields fail closed", () =>
        {
            var binding = new PhysicalTelemetryBinding(Guid.NewGuid(), new string('a', 32),
                1234, 638000000000000000, @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var ingest = new PhysicalTelemetryIngest(binding, new string('b', 40));
            var now = DateTimeOffset.UtcNow;
            check(!ingest.Accept("{}"u8.ToArray(), new string('b', 40), "127.0.0.1", now), "missing fields");
            check(!ingest.Accept("{bad"u8.ToArray(), new string('b', 40), "127.0.0.1", now), "bad JSON");
            check(!ingest.Accept("{}"u8.ToArray(), new string('é', 40), "127.0.0.1", now), "nonascii token");
            check(ingest.Latest is null, "no sample");
        });
    }
}
