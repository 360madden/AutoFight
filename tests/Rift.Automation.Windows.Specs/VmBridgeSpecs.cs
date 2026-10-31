using System.Net;
using System.Net.Http;
using System.Text.Json;
using Rift.Automation.Windows;

internal static class VmBridgeSpecs
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        string config = Path.GetTempFileName();
        string lease = Path.GetTempFileName();
        string arm = Path.GetTempFileName();
        const string bearer = "test-controller-token-not-real";
        try
        {
            File.WriteAllText(config, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, uiPort = 49811, operatorToken = bearer
            }));
            File.WriteAllText(lease, JsonSerializer.Serialize(new
            {
                request_id = "fixture-lease-1", token = new string('a', 64)
            }));
            File.WriteAllText(arm, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, armToken = "short-lived-review-token-test",
                expiresInSeconds = 15
            }));

            test("VM status reaches loopback controller and identifies guest response", () =>
            {
                int calls = 0;
                using var bridge = new VmControllerBridge(config, new StubHandler(request =>
                {
                    calls++;
                    check(request.Method == HttpMethod.Get && request.RequestUri?.Host == "127.0.0.1"
                        && request.RequestUri.Port == 49811
                        && request.RequestUri.AbsolutePath == "/api/status", "status endpoint");
                    check(request.Headers.Authorization?.Scheme == "Bearer"
                        && request.Headers.Authorization.Parameter == bearer, "local authorization");
                    return Json(HttpStatusCode.OK, "{\"host\":{\"connected\":true},\"guest\":{\"status\":\"idle\"}}");
                }));
                var result = bridge.StatusAsync().GetAwaiter().GetResult();
                check(calls == 1 && result.HostAccepted && result.Connected && result.GuestResponded,
                    "status response");
                check(!result.GameFeedbackConfirmed && result.Body is null, "no fake gameplay confirmation");
            });

            test("VM bounded action requires supervised lease and arm artifacts", () =>
            {
                int calls = 0;
                using var bridge = new VmControllerBridge(config, new StubHandler(request =>
                {
                    calls++;
                    check(request.Method == HttpMethod.Post
                        && request.RequestUri?.AbsolutePath == "/api/action", "action endpoint");
                    using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    var root = body.RootElement;
                    check(root.GetProperty("access").GetProperty("request_id").GetString() == "fixture-lease-1"
                        && root.GetProperty("access").GetProperty("token").GetString() == new string('a', 64),
                        "supervisor grant included");
                    var payload = root.GetProperty("payload");
                    var action = payload.GetProperty("action");
                    check(payload.GetProperty("armToken").GetString() == "short-lived-review-token-test",
                        "reviewed arming token");
                    check(action.GetProperty("keys")[0].GetString() == "w"
                        && action.GetProperty("holdMilliseconds").GetInt32() == 50
                        && action.GetProperty("deltaX").GetInt32() == 0,
                        "bounded explicit pulse");
                    return Json(HttpStatusCode.OK, CleanActionAck());
                }));
                var result = bridge.PulseAsync(lease, arm, "w", 50).GetAwaiter().GetResult();
                check(calls == 1 && result.HostAccepted && !result.GameFeedbackConfirmed,
                    "acknowledged attempt not verified gameplay");
                check(result.Body is null, "command response not surfaced with private data");
            });

            test("VM bad actions never reach transport", () =>
            {
                int calls = 0;
                using var bridge = new VmControllerBridge(config, new StubHandler(request =>
                {
                    calls++; return Json(HttpStatusCode.OK, "{}");
                }));
                foreach (var (key, hold) in new[] { ("f", 50), ("w", 19), ("w", 251) })
                {
                    try
                    {
                        bridge.PulseAsync(lease, arm, key, hold).GetAwaiter().GetResult();
                        throw new InvalidOperationException("unsafe action accepted");
                    }
                    catch (ArgumentException) { }
                }
                check(calls == 0, "fail before network");
            });

            test("VM failed controller acknowledgment does not imply execution", () =>
            {
                using var bridge = new VmControllerBridge(config,
                    new StubHandler(_ => Json(HttpStatusCode.Conflict, "{\"error\":\"lease denied\"}")));
                var result = bridge.PulseAsync(lease, arm, "a", 20).GetAwaiter().GetResult();
                check(!result.HostAccepted && result.ErrorCode == "controller_rejected"
                    && !result.GameFeedbackConfirmed && result.Body is null && result.HttpStatus == 409,
                    "explicit rejected result");
            });

            test("VM action times out if headers arrive but the body stalls", () =>
            {
                int calls = 0;
                var clock = new ManualTimeProvider();
                using var stream = new StallingStream(clock.Fire);
                using var bridge = new VmControllerBridge(config, new StubHandler(_ =>
                {
                    calls++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(stream)
                    };
                }), clock);
                Task<VmControllerResult> pending = bridge.PulseAsync(lease, arm, "w", 50);
                try
                {
                    // Watchdog is test cleanup, not the expected timeout mechanism.
                    check(pending.Wait(TimeSpan.FromSeconds(2)),
                        "own five-second deadline must close stalled body without real waiting");
                    var result = pending.GetAwaiter().GetResult();
                    check(calls == 1 && stream.ReadCalls == 2,
                        "exactly one request and deadline during body read");
                    check(clock.RequestedDueTime == TimeSpan.FromSeconds(5),
                        "fixed request-wide five-second deadline");
                    check(!result.HostAccepted && !result.Connected && !result.GuestResponded &&
                        !result.GameFeedbackConfirmed && result.ErrorCode == "transport_timeout" &&
                        result.Body is null, "timeout cannot confirm delivery or expose body");
                }
                finally
                {
                    stream.Release();
                    try { pending.Wait(TimeSpan.FromSeconds(1)); }
                    catch (AggregateException) { }
                }
            });
            test("VM caller cancellation is not mislabeled as controller timeout", () =>
            {
                int calls = 0;
                using var cancelled = new CancellationTokenSource();
                using var stream = new StallingStream(cancelled.Cancel);
                using var bridge = new VmControllerBridge(config, new StubHandler(_ =>
                {
                    calls++;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(stream)
                    };
                }), new ManualTimeProvider());
                Task<VmControllerResult> pending = bridge.PulseAsync(
                    lease, arm, "w", 50, cancelled.Token);
                try
                {
                    var settled = Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)))
                        .GetAwaiter().GetResult();
                    check(settled == pending, "caller cancellation must not leave a hanging request");
                    try
                    {
                        pending.GetAwaiter().GetResult();
                        throw new InvalidOperationException("caller cancellation was swallowed");
                    }
                    catch (OperationCanceledException)
                    {
                        check(calls == 1 && stream.ReadCalls == 2,
                            "caller aborts one partially read response without retry");
                    }
                }
                finally
                {
                    stream.Release();
                    try { pending.Wait(TimeSpan.FromSeconds(1)); }
                    catch (AggregateException) { }
                }
            });
            test("VM rejects completed HTTP 200 actions with unsafe executor outcomes", () =>
            {
                var cases = new[]
                {
                    ("delivery", ActionAck(root =>
                    {
                        root["result"]!["deliverySucceeded"] = false;
                        root["result"]!["error"] = "insertion failed";
                    })),
                    ("release", ActionAck(root =>
                    {
                        root["result"]!["releaseSucceeded"] = false;
                        root["result"]!["stateUncertain"] = true;
                        root["result"]!["requiresCallerDisarm"] = true;
                    })),
                    ("cancelled", ActionAck(root => root["result"]!["cancelled"] = true)),
                    ("target lost", ActionAck(root => root["result"]!["targetLost"] = true)),
                    ("uncertain state", ActionAck(root => root["result"]!["stateUncertain"] = true)),
                    ("caller disarm", ActionAck(root => root["result"]!["requiresCallerDisarm"] = true)),
                    ("recovery", ActionAck(root => root["recoveryRequired"] = true)),
                    ("nested error", ActionAck(root => root["result"]!["error"] = "unexpected failure")),
                    ("contradictory top-level error", ActionAck(root => root["error"] = "guest failed"))
                };
                foreach (var (name, body) in cases)
                {
                    int calls = 0;
                    using var bridge = new VmControllerBridge(config, new StubHandler(_ =>
                    {
                        calls++;
                        return Json(HttpStatusCode.OK, body);
                    }));
                    var result = bridge.PulseAsync(lease, arm, "w", 50).GetAwaiter().GetResult();
                    check(calls == 1 && !result.HostAccepted &&
                        result.ErrorCode == "action_outcome_unconfirmed" &&
                        !result.GameFeedbackConfirmed && result.Body is null &&
                        result.HttpStatus == 200, name + " should not be clean input");
                }
            });
            test("VM malformed HTTP 200 action body cannot imply insertion or release", () =>
            {
                string clean = CleanActionAck();
                var cases = new[]
                {
                    ("empty", "{}"),
                    ("legacy fake", "{\"ok\":true,\"inputIssued\":true}"),
                    ("null result", "{\"result\":null,\"recoveryRequired\":false}"),
                    ("missing flag", ActionAck(root => root["result"]!.AsObject().Remove("releaseSucceeded"))),
                    ("string flag", ActionAck(root => root["result"]!["releaseSucceeded"] = "false")),
                    ("missing error", ActionAck(root => root["result"]!.AsObject().Remove("error"))),
                    ("duplicate release", clean.Replace("\"releaseSucceeded\":true",
                        "\"releaseSucceeded\":true,\"releaseSucceeded\":false", StringComparison.Ordinal)),
                    ("duplicate recovery", clean.Replace("\"recoveryRequired\":false",
                        "\"recoveryRequired\":false,\"recoveryRequired\":true", StringComparison.Ordinal)),
                    ("reverse duplicate release", clean.Replace("\"releaseSucceeded\":true",
                        "\"releaseSucceeded\":false,\"releaseSucceeded\":true", StringComparison.Ordinal))
                };
                foreach (var (name, body) in cases)
                {
                    using var bridge = new VmControllerBridge(config,
                        new StubHandler(_ => Json(HttpStatusCode.OK, body)));
                    var result = bridge.PulseAsync(lease, arm, "w", 50).GetAwaiter().GetResult();
                    check(!result.HostAccepted && result.ErrorCode == "invalid_action_ack" &&
                        !result.GameFeedbackConfirmed && result.Body is null && result.HttpStatus == 200,
                        name + " invalid acknowledgment");
                }
            });
            test("VM guest responseVerified does not substitute for own game feedback", () =>
            {
                using var bridge = new VmControllerBridge(config,
                    new StubHandler(_ => Json(HttpStatusCode.OK,
                        ActionAck(root => root["result"]!["responseVerified"] = true))));
                var result = bridge.PulseAsync(lease, arm, "w", 50).GetAwaiter().GetResult();
                check(result.HostAccepted && !result.GameFeedbackConfirmed && result.Body is null,
                    "clean input still has no independent game-response evidence");
            });
            test("VM rejects duplicate authorization config keys", () =>
            {
                File.WriteAllText(config,
                    "{\"uiPort\":49811,\"uiPort\":49812,\"operatorToken\":\"test-controller-token-not-real\"}");
                try
                {
                    using var bridge = new VmControllerBridge(config);
                    throw new InvalidOperationException("duplicate config accepted");
                }
                catch (ArgumentException) { }
                File.WriteAllText(config, JsonSerializer.Serialize(new
                {
                    uiPort = 49811, operatorToken = bearer
                }));
            });
        }
        finally
        {
            File.Delete(config);
            File.Delete(lease);
            File.Delete(arm);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        private bool _active;
        public TimeSpan? RequestedDueTime { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            RequestedDueTime = dueTime;
            _active = true;
            return new ManualTimer(this);
        }

        public void Fire()
        {
            if (_active) _callback?.Invoke(_state);
        }

        private sealed class ManualTimer(ManualTimeProvider clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                clock.RequestedDueTime = dueTime;
                clock._active = true;
                return true;
            }
            public void Dispose() => clock._active = false;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StallingStream(Action onStall) : Stream
    {
        private readonly CancellationTokenSource _release = new();
        private int _readCalls;
        private int _disposed;
        public int ReadCalls => Volatile.Read(ref _readCalls);
        public void Release()
        {
            try { _release.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCalls) == 1)
            {
                byte[] prefix = System.Text.Encoding.UTF8.GetBytes("{\"ok\":");
                prefix.CopyTo(buffer);
                return ValueTask.FromResult(prefix.Length);
            }
            onStall();
            return AwaitRelease(cancellationToken);
        }
        private async ValueTask<int> AwaitRelease(CancellationToken cancellationToken)
        {
            using var joined = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _release.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, joined.Token);
            return 0;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Release();
                _release.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static string CleanActionAck() => ActionAck();

    private static string ActionAck(Action<System.Text.Json.Nodes.JsonObject>? mutate = null)
    {
        string json = JsonSerializer.Serialize(new
        {
            result = new
            {
                deliverySucceeded = true, releaseSucceeded = true,
                cancelled = false, targetLost = false,
                stateUncertain = false, requiresCallerDisarm = false,
                observedDuration = "00:00:00.0500000",
                responseVerified = false, error = (string?)null
            },
            recoveryRequired = false,
            inputEvents = new[]
            {
                new { kind = "keyDownInserted", value = "w" },
                new { kind = "keyUpInserted", value = "w" }
            }
        });
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        mutate?.Invoke(root);
        return root.ToJsonString();
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(callback(request));
    }
}
