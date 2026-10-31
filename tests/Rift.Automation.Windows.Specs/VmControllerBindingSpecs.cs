using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rift.Automation.Windows;

internal static class VmControllerBindingSpecs
{
    private const string Machine = "RIFT-LAB";
    private const string ReaderIp = "192.168.56.128";
    private const string Vmx = @"C:\vm\RIFT-Lab-1.vmx";
    private const string GamePath = @"C:\games\rift_x64.exe";
    private static readonly string GameHash = new('A', 64);

    private static VmTelemetrySnapshot Fresh(string lab = "lab1") =>
        new(lab, "fresh_observed", "pinned", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 20, 10, 8, 2, 3300, 10, 15, 20,
            "whole-readable-private", "source-a", 20, ReaderIp,
            638000000000000000, GamePath, GameHash);

    private static string Status(Action<JsonObject>? mutate = null)
    {
        string json = JsonSerializer.Serialize(new
        {
            host = new
            {
                connected = true, sourceAddress = ReaderIp,
                instanceId = "agent-a", machineName = Machine,
                vmIdentity = new
                {
                    vmx = Vmx, port = 5905, pid = 7777, listenerPid = 7777,
                    logEpoch = "2026-10-31T01:30:00Z",
                    startTicks = 134063478000000000L,
                    image = @"C:\Program Files\VMware\VMware Workstation\x64\vmware-vmx.exe"
                },
                consoleReservation = new { held = true, closing = false, faulted = false },
                coordination = new
                {
                    state = "ready", resource = "vm:rift-lab-1",
                    supervisor = new { state = "running", threadAlive = true },
                    shutdownRequested = false
                }
            },
            guest = new
            {
                instanceId = "agent-a",
                target = new
                {
                    machineName = Machine, processId = 3300,
                    processStartUtcTicks = 638000000000000000,
                    executablePath = GamePath, executableSha256 = GameHash,
                    isForeground = true
                },
                armed = true, busy = false, recoveryRequired = false,
                expiresInSeconds = 12.5, desktopInputsDown = Array.Empty<string>()
            }
        });
        var root = JsonNode.Parse(json)!.AsObject();
        mutate?.Invoke(root);
        return root.ToJsonString();
    }

    private static string Config(string path, string guest = ReaderIp,
        string vmx = Vmx, string machine = Machine)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, uiPort = 49811,
            operatorToken = "synthetic-not-an-operator-token",
            guestAddress = guest, expectedMachineName = machine,
            vmx, vmrun = @"C:\VMware\vmrun.exe", vncPort = 5905
        }));
        return path;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
        new(status) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static string CleanActionAck() => JsonSerializer.Serialize(new
    {
        result = new
        {
            deliverySucceeded = true, releaseSucceeded = true,
            cancelled = false, targetLost = false,
            stateUncertain = false, requiresCallerDisarm = false,
            error = (string?)null
        },
        recoveryRequired = false
    });

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        string config = Path.GetTempFileName();
        string lease = Path.GetTempFileName();
        string arm = Path.GetTempFileName();
        try
        {
            Config(config);
            File.WriteAllText(lease, JsonSerializer.Serialize(new
            {
                request_id = "synthetic-lease", token = new string('a', 64)
            }));
            File.WriteAllText(arm, JsonSerializer.Serialize(new
            {
                armToken = "synthetic-arm-token"
            }));

            test("VM binding maps selected lab to independently reviewed coordination resource", () =>
            {
                check(VmControllerBindingChecks.ExpectedResource("lab1") == "vm:rift-lab-1" &&
                    VmControllerBindingChecks.ExpectedResource("lab2") == "vm:rift-lab-2" &&
                    VmControllerBindingChecks.ExpectedResource("lab3") is null,
                    "lab mapping does not come from config or machine name");
            });
            test("VM binding accepts authenticated matching target without claiming gameplay feedback", () =>
            {
                int gets = 0;
                using var bridge = new VmControllerBridge(config, new StubHandler(request =>
                {
                    check(request.Method == HttpMethod.Get &&
                        request.RequestUri?.AbsolutePath == "/api/status", "status only");
                    gets++;
                    return Json(HttpStatusCode.OK, Status());
                }));
                VmControllerResult result = bridge.VerifyPulseTargetAsync("lab1", Fresh())
                    .GetAwaiter().GetResult();
                check(gets == 1 && result.HostAccepted && result.Connected &&
                    result.GuestResponded && !result.GameFeedbackConfirmed && result.Body is null,
                    "identity-qualified status remains noncausal and redacted");
            });
            test("VM selected lab1 cannot send using internally consistent lab2 host/status", () =>
            {
                var routes = new List<string>();
                string wrongStatus = Status(root =>
                {
                    root["host"]!["coordination"]!["resource"] = "vm:rift-lab-2";
                    root["host"]!["sourceAddress"] = "192.168.56.129";
                    root["host"]!["vmIdentity"]!["vmx"] = @"C:\vm\RIFT-Lab-2.vmx";
                });
                Config(config, guest: "192.168.56.129", vmx: @"C:\vm\RIFT-Lab-2.vmx");
                using (var bridge = new VmControllerBridge(config, new StubHandler(request =>
                {
                    routes.Add(request.RequestUri!.AbsolutePath);
                    return request.Method == HttpMethod.Get
                        ? Json(HttpStatusCode.OK, wrongStatus)
                        : Json(HttpStatusCode.OK, CleanActionAck());
                })))
                {
                    VmControllerResult preflight = bridge.VerifyPulseTargetAsync("lab1", Fresh())
                        .GetAwaiter().GetResult();
                    if (preflight.HostAccepted)
                        bridge.PulseAsync(lease, arm, "w", 50).GetAwaiter().GetResult();
                    check(!preflight.HostAccepted &&
                        preflight.ErrorCode == "controller_identity_mismatch" &&
                        routes.SequenceEqual(new[] { "/api/status" }), "zero POST to wrong lab");
                }
                Config(config);
            });
            test("VM binding same machine name does not bypass wrong coordination resource", () =>
            {
                using var doc = JsonDocument.Parse(Status(root =>
                    root["host"]!["coordination"]!["resource"] = "vm:rift-lab-2"));
                string code = VmControllerBindingChecks.Classify("lab1", Fresh(),
                    new VmHostBinding(ReaderIp, Machine, Vmx), doc.RootElement);
                check(code == "controller_identity_mismatch", "hostname alone has no lab authority");
            });
            test("VM binding refuses identity swaps and mismatched game target tuple", () =>
            {
                var wrong = new Action<JsonObject>[]
                {
                    root => root["host"]!["coordination"]!["resource"] = "vm:rift-lab-2",
                    root => root["host"]!["sourceAddress"] = "192.168.56.129",
                    root => root["host"]!["instanceId"] = "agent-b",
                    root => root["host"]!["machineName"] = "foreign",
                    root => root["guest"]!["target"]!["machineName"] = "foreign",
                    root => root["guest"]!["target"]!["processId"] = 3301,
                    root => root["guest"]!["target"]!["processStartUtcTicks"] = 638000000000000001,
                    root => root["guest"]!["target"]!["executablePath"] = @"C:\games\other.exe",
                    root => root["guest"]!["target"]!["executableSha256"] = new string('B', 64),
                    root => root["host"]!["vmIdentity"]!["vmx"] = @"C:\vm\RIFT-Lab-2.vmx"
                };
                foreach (Action<JsonObject> mutate in wrong)
                {
                    using var doc = JsonDocument.Parse(Status(mutate));
                    string code = VmControllerBindingChecks.Classify("lab1", Fresh(),
                        new VmHostBinding(ReaderIp, Machine, Vmx), doc.RootElement);
                    check(code == "controller_identity_mismatch", "mismatched selected-lab/game identity");
                }
            });
            test("VM binding refuses connected but unready, unarmed or stuck guest", () =>
            {
                var blocked = new Action<JsonObject>[]
                {
                    root => root["host"]!["connected"] = false,
                    root => root["host"]!["coordination"]!["state"] = "waiting",
                    root => root["host"]!["coordination"]!["supervisor"]!["threadAlive"] = false,
                    root => root["host"]!["consoleReservation"]!["held"] = false,
                    root => root["host"]!["consoleReservation"]!["faulted"] = true,
                    root => root["host"]!["coordination"]!["shutdownRequested"] = true,
                    root => root["guest"]!["armed"] = false,
                    root => root["guest"]!["busy"] = true,
                    root => root["guest"]!["recoveryRequired"] = true,
                    root => root["guest"]!["expiresInSeconds"] = 0,
                    root => root["guest"]!["desktopInputsDown"] = new JsonArray("w"),
                    root => root["guest"]!["target"]!["isForeground"] = false
                };
                foreach (Action<JsonObject> mutate in blocked)
                {
                    using var doc = JsonDocument.Parse(Status(mutate));
                    check(VmControllerBindingChecks.Classify("lab1", Fresh(),
                        new VmHostBinding(ReaderIp, Machine, Vmx), doc.RootElement)
                        == "controller_not_ready", "unready state must block");
                }
            });
            test("VM binding refuses missing, duplicate or wrongly typed identity fields", () =>
            {
                var invalid = new Action<JsonObject>[]
                {
                    root => root["guest"] = null,
                    root => root["guest"] = new JsonArray(),
                    root => root["guest"]!["target"] = null,
                    root => root["host"]!["vmIdentity"] = null,
                    root => root["host"]!["vmIdentity"]!["listenerPid"] = null,
                    root => root["host"]!["vmIdentity"]!["listenerPid"] = 7778,
                    root => root["host"]!["vmIdentity"]!["port"] = 70000,
                    root => root["host"]!["vmIdentity"]!["startTicks"] = "not-ticks",
                    root => root["host"]!["vmIdentity"]!["image"] = "unqualified.exe",
                    root => root["host"]!["vmIdentity"]!["logEpoch"] = "not-an-epoch",
                    root => root["host"]!["coordination"] = null,
                    root => root["host"]!["sourceAddress"] = 42,
                    root => root["host"]!["connected"] = "true",
                    root => root["guest"]!["armed"] = "true",
                    root => root["guest"]!["desktopInputsDown"] = "none",
                    root => root["guest"]!["target"]!["processId"] = -1,
                    root => root["guest"]!["target"]!["processStartUtcTicks"] = "638000000000000000",
                    root => root["error"] = "contradictory host error"
                };
                foreach (Action<JsonObject> mutate in invalid)
                {
                    using var doc = JsonDocument.Parse(Status(mutate));
                    check(VmControllerBindingChecks.Classify("lab1", Fresh(),
                        new VmHostBinding(ReaderIp, Machine, Vmx), doc.RootElement)
                        == "invalid_controller_identity", "malformed identity blocked");
                }
                string raw = Status().Replace("\"instanceId\":\"agent-a\"",
                    "\"instanceId\":\"agent-a\",\"instanceId\":\"agent-b\"",
                    StringComparison.Ordinal);
                using var dup = JsonDocument.Parse(raw);
                check(VmControllerBindingChecks.Classify("lab1", Fresh(),
                    new VmHostBinding(ReaderIp, Machine, Vmx), dup.RootElement)
                    == "invalid_controller_identity", "duplicate JSON keys blocked");
            });
            test("VM binding rejects incomplete telemetry provenance before GET", () =>
            {
                var fields = new[]
                {
                    Fresh() with { ReaderSourceAddress = null },
                    Fresh() with { ReaderSourceAddress = "not-an-ip" },
                    Fresh() with { GameProcessStartUtcTicks = null },
                    Fresh() with { GameProcessStartUtcTicks = 0 },
                    Fresh() with { GameExecutableSha256 = null },
                    Fresh() with { GameExecutableSha256 = new string('Z', 64) },
                    Fresh() with { GameExecutablePath = null },
                    Fresh() with { Lab = "lab2" }
                };
                int calls = 0;
                foreach (VmTelemetrySnapshot bad in fields)
                {
                    using var bridge = new VmControllerBridge(config, new StubHandler(_ =>
                    {
                        calls++; return Json(HttpStatusCode.OK, Status());
                    }));
                    VmControllerResult r = bridge.VerifyPulseTargetAsync("lab1", bad)
                        .GetAwaiter().GetResult();
                    check(!r.HostAccepted && r.ErrorCode == "source_or_controller_binding_unavailable",
                        "nonqualified snapshot blocked");
                }
                check(calls == 0, "no GET or POST when source lacks binding");
                string serialized = JsonSerializer.Serialize(Fresh());
                check(!serialized.Contains("ReaderSourceAddress", StringComparison.Ordinal) &&
                    !serialized.Contains("GameExecutablePath", StringComparison.Ordinal) &&
                    !serialized.Contains("GameExecutableSha256", StringComparison.Ordinal) &&
                    !serialized.Contains(ReaderIp, StringComparison.Ordinal),
                    "private binding metadata excluded from JSON diagnostics");
            });
            test("VM post-status gate refuses changed address and executable path with same stream", () =>
            {
                VmTelemetrySnapshot before = Fresh();
                VmTelemetrySnapshot advanced = before with
                {
                    OuterSequence = before.OuterSequence + 1,
                    AddonSequence = before.AddonSequence + 1,
                    AddonClock = before.AddonClock + 1
                };
                check(VmControllerBindingChecks.HasStableSourceBinding("lab1", before, advanced) &&
                    VmPulseObservationChecks.CanRevalidate("lab1", before, advanced),
                    "normal advancing frame remains bound");
                foreach (VmTelemetrySnapshot swapped in new[]
                {
                    advanced with { ReaderSourceAddress = "192.168.56.129" },
                    advanced with { GameExecutablePath = @"C:\games\impostor.exe" },
                    advanced with { GameExecutableSha256 = new string('B', 64) },
                    advanced with { GameProcessStartUtcTicks = advanced.GameProcessStartUtcTicks + 1 }
                })
                {
                    check(!VmControllerBindingChecks.HasStableSourceBinding("lab1", before, swapped),
                        "post-status provenance swap blocked even if frame advances");
                }
                VmTelemetrySnapshot addressOnly = advanced with
                {
                    ReaderSourceAddress = "192.168.56.129"
                };
                check(VmPulseObservationChecks.CanRevalidate("lab1", before, addressOnly),
                    "regression reproduces historical stream-only gap");
            });
            test("VM binding resolves relative VMX against config directory", () =>
            {
                string relative = Path.Combine("nested", "guest.vmx");
                Config(config, vmx: relative);
                using var json = JsonDocument.Parse(File.ReadAllText(config));
                VmHostBinding? binding = VmHostBinding.FromConfig(json.RootElement, config);
                check(binding is not null &&
                    binding.ExpectedVmx == Path.GetFullPath(
                        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config))!, relative)),
                    "relative VMX binds to same config folder");
                Config(config);
            });
            test("VM binding only GETs on malformed or rejected controller response", () =>
            {
                foreach (var (http, response) in new[]
                {
                    (HttpStatusCode.Conflict, "{\"error\":\"not ready\"}"),
                    (HttpStatusCode.OK, "{not json}"),
                    (HttpStatusCode.OK, "{\"host\":{}}")
                })
                {
                    int gets = 0;
                    using var bridge = new VmControllerBridge(config, new StubHandler(request =>
                    {
                        gets++;
                        check(request.Method == HttpMethod.Get, "no POST on invalid status");
                        return Json(http, response);
                    }));
                    VmControllerResult result = bridge.VerifyPulseTargetAsync("lab1", Fresh())
                        .GetAwaiter().GetResult();
                    check(!result.HostAccepted && result.Body is null &&
                        !result.GameFeedbackConfirmed && gets == 1, "status failure is bounded/read-only");
                }
            });
            test("VM bound controller may make exactly one POST after verified GET", () =>
            {
                var requests = new List<string>();
                using var bridge = new VmControllerBridge(config, new StubHandler(request =>
                {
                    requests.Add(request.RequestUri!.AbsolutePath);
                    return request.Method == HttpMethod.Get
                        ? Json(HttpStatusCode.OK, Status())
                        : Json(HttpStatusCode.OK, CleanActionAck());
                }));
                VmTelemetrySnapshot before = Fresh();
                VmControllerResult verified = bridge.VerifyPulseTargetAsync("lab1", before)
                    .GetAwaiter().GetResult();
                VmTelemetrySnapshot afterStatus = before with
                {
                    OuterSequence = 11, AddonSequence = 9, AddonClock = 21
                };
                check(verified.HostAccepted &&
                    VmControllerBindingChecks.HasStableSourceBinding("lab1", before, afterStatus) &&
                    VmPulseObservationChecks.CanRevalidate("lab1", before, afterStatus),
                    "both independent gates required before dispatch");
                VmControllerResult response = bridge.PulseAsync(lease, arm, "w", 50)
                    .GetAwaiter().GetResult();
                check(requests.SequenceEqual(new[] { "/api/status", "/api/action" }) &&
                    response.HostAccepted && !response.GameFeedbackConfirmed,
                    "one status then one requested pulse, no inferred RIFT feedback");
            });
        }
        finally
        {
            File.Delete(config);
            File.Delete(lease);
            File.Delete(arm);
        }
    }
}
