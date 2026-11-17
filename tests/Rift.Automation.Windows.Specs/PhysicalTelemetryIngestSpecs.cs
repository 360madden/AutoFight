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
                var warning = PhysicalAddonReloadPreflight.InspectFilesAtStart(directory, DateTime.UtcNow);
                check(warning is not null && warning.Contains("missing"), "missing addon files reported");
            }
            finally { Directory.Delete(directory); }
        });
        test("Physical addon timestamp preflight distinguishes post-start files without claiming reload", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "af-addon-time-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var start = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
                foreach (var name in new[] { "RiftAddon.toc", "main.lua", "target_vitals_v2.lua" })
                {
                    var path = Path.Combine(directory, name);
                    File.WriteAllText(path, "test");
                    File.SetLastWriteTimeUtc(path, start.AddMinutes(-1));
                }
                check(PhysicalAddonReloadPreflight.InspectFilesAtStart(directory, start) is null,
                    "pre-start files do not trigger warning");
                File.SetLastWriteTimeUtc(Path.Combine(directory, "main.lua"), start.AddMinutes(1));
                var warning = PhysicalAddonReloadPreflight.InspectFilesAtStart(directory, start);
                check(warning is not null && warning.Contains("main.lua") &&
                    warning.Contains("cannot prove"), "post-start change warns without claiming loaded state");
            }
            finally { Directory.Delete(directory, true); }
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
            var spoof = "SECRET scan=complete selection=NotFound freshness=Unavailable ageMs=- delivery=delivered-http-200";
            var validFractional = "2026-11-16T03:30:00.1234567+00:00 scan=complete selection=NotFound freshness=Unavailable ageMs=42 fastScan=complete fastFreshness=Fresh fastAgeMs=12 delivery=delivered-http-200";
            using var input = new StringReader("arbitrary secret\n" + valid + "\n" + malicious + "\n" + spoof + "\n" + validFractional + "\n");
            var forwarded = new List<string>();
            PhysicalReaderSessionMonitor.MonitorAsync(input, new PhysicalReaderSessionTracker(),
                (_, _, _) => Task.CompletedTask, CancellationToken.None,
                line => forwarded.Add(line)).GetAwaiter().GetResult();
            check(forwarded.Count == 2 && forwarded[0] == valid && forwarded[1] == validFractional,
                "only timestamped status relayed, including fractional timestamps");
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
        test("Physical ingest retains full target-vitals provenance without granting source authority", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var ingest = new PhysicalTelemetryIngest(binding, token);
            byte[] Frame(long outer, object? candidate) => JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion = 1, instanceId = id, nonce, sequence = outer, readerStatus = "running",
                target = new {
                    processId = binding.GameProcessId,
                    processStartUtcTicks = binding.GameProcessStartUtcTicks,
                    executablePath = binding.GameExecutablePath,
                    executableSha256 = binding.GameExecutableSha256,
                    readerSessionNonce = nonce
                },
                targetVitalsCandidate = candidate
            });
            var candidate = new {
                status = "candidate-unqualified", candidateCount = 1, sequence = 7,
                playerId = "player-1", targetId = "enemy-2", epoch = 3,
                producerClock = 12.5, health = 40.0, healthMax = 100.0,
                envelopeSha256 = new string('A', 64), frameSha256 = new string('B', 64)
            };
            var now = new DateTimeOffset(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);
            check(ingest.Accept(Frame(1, candidate), token, "127.0.0.1", now), "first full candidate delivered");
            var value = ingest.Latest?.TargetVitals;
            check(value is not null && value.PlayerId == "player-1" && value.TargetId == "enemy-2" &&
                value.Epoch == 3 && value.Sequence == 7 && value.ProducerClock == 12.5 &&
                value.Health == 40 && value.HealthMax == 100 &&
                value.EnvelopeSha256.Length == 64 && value.FrameSha256.Length == 64 &&
                !value.SourceQualified, "typed candidate complete but remains unqualified");
            check(ingest.Accept(Frame(2, candidate), token, "127.0.0.1", now.AddMilliseconds(100)),
                "outer transport advances");
            check(ingest.Latest?.TargetVitals is { Sequence: 7, ProducerClock: 12.5, SourceQualified: false },
                "repeated producer data not promoted by transport");
            check(ingest.ReadFresh(now.AddSeconds(3), TimeSpan.FromSeconds(1)) is null,
                "host receipt freshness expires independently");
            check(ingest.Accept(Frame(3, null), token, "127.0.0.1", now.AddSeconds(4)), "missing next candidate");
            check(ingest.Latest?.TargetVitals is null &&
                ingest.Latest?.TargetVitalsSequence is null, "no stale candidate exposure");
        });
        test("Physical ingest malformed newer pinned vitals revokes stale diagnostic and fences replay", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            byte[] Frame(long outer, object? candidate, int processId = 1234) =>
                JsonSerializer.SerializeToUtf8Bytes(new {
                    schemaVersion = 1, instanceId = id, nonce, sequence = outer, readerStatus = "running",
                    target = new {
                        processId, processStartUtcTicks = binding.GameProcessStartUtcTicks,
                        executablePath = binding.GameExecutablePath,
                        executableSha256 = binding.GameExecutableSha256,
                        readerSessionNonce = nonce
                    },
                    targetVitalsCandidate = candidate
                });
            var full = new {
                status = "candidate-unqualified", candidateCount = 1, sequence = 7,
                playerId = "p", targetId = "t", epoch = 2, producerClock = 12.5,
                health = 40.0, healthMax = 100.0,
                envelopeSha256 = new string('A', 64), frameSha256 = new string('B', 64)
            };
            var unknown = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(full))!.AsObject();
            unknown["extra"] = 1;
            object[] malformed = [
                new { full.status, full.sequence, full.playerId, full.health },
                full with { health = 140.0 },
                full with { epoch = 0 },
                full with { producerClock = -1.0 },
                full with { frameSha256 = "bad" },
                full with { candidateCount = 257 },
                full with { status = "unrecognized" },
                new { full.status, sequence = "seven" },
                unknown,
                "unexpected-type"
            ];
            var now = new DateTimeOffset(2026, 11, 16, 21, 0, 0, TimeSpan.Zero);
            foreach (var bad in malformed)
            {
                var ingest = new PhysicalTelemetryIngest(binding, token);
                check(ingest.Accept(Frame(10, full), token, "127.0.0.1", now), "seed pinned candidate");
                var original = ingest.Latest!;
                check(!ingest.Accept(Frame(11, bad), token, "127.0.0.1", now.AddMilliseconds(50)),
                    "newer malformed candidate rejected");
                var after = ingest.Latest;
                check(after is not null && after.Sequence == original.Sequence &&
                    after.ReaderStatus == original.ReaderStatus &&
                    after.ReceivedAtUtc == original.ReceivedAtUtc &&
                    after.TargetVitals is null && after.TargetVitalsStatus is null &&
                    after.TargetVitalsSequence is null, "no older candidate survives newer invalid diagnostic");
                check(ingest.ReadFresh(now.AddMilliseconds(100), TimeSpan.FromSeconds(1))?.TargetVitals is null,
                    "fresh last-valid receipt cannot expose stale candidate after fault");
                check(!ingest.Accept(Frame(11, full), token, "127.0.0.1", now.AddMilliseconds(150)),
                    "invalid outer sequence cannot be reused to resurrect the diagnostic");
                check(ingest.Accept(Frame(12, full), token, "127.0.0.1", now.AddMilliseconds(200)) &&
                    ingest.Latest?.TargetVitals is { SourceQualified: false },
                    "strictly newer valid report recovers diagnostic without combat authority");
                check(!ingest.Accept(Frame(13, bad), "wrong", "127.0.0.1", now.AddMilliseconds(250)) &&
                    !ingest.Accept(Frame(13, bad), token, "192.168.1.1", now.AddMilliseconds(250)) &&
                    !ingest.Accept(Frame(13, bad, processId: 999), token, "127.0.0.1", now.AddMilliseconds(250)) &&
                    !ingest.Accept(Frame(11, bad), token, "127.0.0.1", now.AddMilliseconds(250)) &&
                    ingest.Latest?.TargetVitals is { SourceQualified: false },
                    "unauthorized, mismatched identity and older invalid replays cannot clear current candidate");
            }
        });
        test("Physical ingest duplicate vitals and out-of-order concurrent delivery stay fail-closed", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            byte[] Frame(long outer, object candidate, bool bound = true,
                int schema = 1, string readerStatus = "running") =>
                JsonSerializer.SerializeToUtf8Bytes(new {
                    schemaVersion = schema, instanceId = id, nonce, sequence = outer, readerStatus,
                    target = bound ? (object)new {
                        processId = binding.GameProcessId,
                        processStartUtcTicks = binding.GameProcessStartUtcTicks,
                        executablePath = binding.GameExecutablePath,
                        executableSha256 = binding.GameExecutableSha256,
                        readerSessionNonce = nonce
                    } : null,
                    targetVitalsCandidate = candidate
                });
            var full = new {
                status = "candidate-unqualified", candidateCount = 1, sequence = 7,
                playerId = "p", targetId = "t", epoch = 2, producerClock = 12.5,
                health = 40.0, healthMax = 100.0,
                envelopeSha256 = new string('A', 64), frameSha256 = new string('B', 64)
            };
            var bad = full with { health = 400.0 };
            var now = new DateTimeOffset(2026, 11, 16, 21, 0, 0, TimeSpan.Zero);
            var ingest = new PhysicalTelemetryIngest(binding, token);
            check(ingest.Accept(Frame(10, full), token, "127.0.0.1", now), "seed");
            check(!ingest.Accept(Frame(11, bad, bound: false), token, "127.0.0.1", now) &&
                !ingest.Accept(Frame(11, bad, schema: 2), token, "127.0.0.1", now) &&
                !ingest.Accept(Frame(11, bad, readerStatus: "unavailable"), token, "127.0.0.1", now),
                "unbound, outer schema and unavailable status reports reject");
            check(ingest.Latest?.TargetVitals is { SourceQualified: false },
                "unverified outer reports do not revoke authenticated pinned cache");
            var originalJson = Encoding.UTF8.GetString(Frame(11, full));
            var duplicate = originalJson.Replace("\"targetId\":\"t\"",
                "\"targetId\":\"t\",\"targetId\":\"other\"", StringComparison.Ordinal);
            check(!string.Equals(duplicate, originalJson, StringComparison.Ordinal), "duplicate fixture constructed");
            check(!ingest.Accept(Encoding.UTF8.GetBytes(duplicate), token, "127.0.0.1", now),
                "duplicate inner JSON field rejected");
            check(ingest.Latest is { TargetVitals: null, TargetVitalsStatus: null, TargetVitalsSequence: null },
                "duplicate inner field revokes prior candidate");
            var concurrent = new PhysicalTelemetryIngest(binding, token);
            check(concurrent.Accept(Frame(10, full), token, "127.0.0.1", now), "parallel seed");
            var invalid = Task.Run(() => concurrent.Accept(Frame(11, bad),
                token, "127.0.0.1", now.AddMilliseconds(100)));
            var valid = Task.Run(() => concurrent.Accept(Frame(12, full),
                token, "127.0.0.1", now.AddMilliseconds(200)));
            Task.WaitAll(invalid, valid);
            var final = concurrent.Latest;
            check(!invalid.Result && valid.Result && final is { Sequence: 12, TargetVitals: { SourceQualified: false } },
                "higher valid report wins irrespective of concurrent arrival order");
        });
        test("Physical ingest rejects partial, malformed and unknown target-vitals provenance", () =>
        {
            var id = Guid.NewGuid();
            var nonce = new string('a', 32);
            var token = new string('b', 40);
            var binding = new PhysicalTelemetryBinding(id, nonce, 1234, 638000000000000000,
                @"C:\RIFT\rift_x64.exe", new string('c', 64));
            var ingest = new PhysicalTelemetryIngest(binding, token);
            byte[] Frame(object candidate) => JsonSerializer.SerializeToUtf8Bytes(new {
                schemaVersion = 1, instanceId = id, nonce, sequence = 1, readerStatus = "running",
                target = new {
                    processId = binding.GameProcessId,
                    processStartUtcTicks = binding.GameProcessStartUtcTicks,
                    executablePath = binding.GameExecutablePath,
                    executableSha256 = binding.GameExecutableSha256,
                    readerSessionNonce = nonce
                },
                targetVitalsCandidate = candidate
            });
            var valid = new {
                status = "candidate-unqualified", candidateCount = 1, sequence = 7,
                playerId = "p", targetId = "t", epoch = 2, producerClock = 12.5,
                health = 40.0, healthMax = 100.0,
                envelopeSha256 = new string('A', 64), frameSha256 = new string('B', 64)
            };
            var now = DateTimeOffset.UtcNow;
            var partial = new { valid.status, valid.sequence, valid.playerId, valid.health };
            check(!ingest.Accept(Frame(partial), token, "127.0.0.1", now), "partial tuple");
            check(!ingest.Accept(Frame(valid with { health = 140.0 }), token, "127.0.0.1", now), "invalid HP");
            check(!ingest.Accept(Frame(valid with { epoch = -1 }), token, "127.0.0.1", now), "invalid epoch");
            check(!ingest.Accept(Frame(valid with { producerClock = -1.0 }), token, "127.0.0.1", now), "invalid clock");
            check(!ingest.Accept(Frame(valid with { frameSha256 = "bad" }), token, "127.0.0.1", now), "bad hash");
            check(!ingest.Accept(Frame(valid with { candidateCount = 257 }), token, "127.0.0.1", now),
                "count above valid candidate cap");
            var unknown = System.Text.Json.Nodes.JsonNode.Parse(
                JsonSerializer.Serialize(valid))!.AsObject();
            unknown["unexpected"] = 1;
            check(!ingest.Accept(Frame(unknown), token, "127.0.0.1", now), "unknown field");
            check(ingest.Latest is null, "invalid reports never recorded");
            check(ingest.Accept(Frame(new { status = "candidate-unqualified", sequence = 7 }),
                token, "127.0.0.1", now), "legacy status-only compatible");
            check(ingest.Latest?.TargetVitals is null && ingest.Latest?.TargetVitalsSequence == 7,
                "legacy candidate never gains invented provenance");
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
