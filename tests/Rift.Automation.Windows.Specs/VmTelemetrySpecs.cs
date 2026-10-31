using System.Text;
using System.Text.Json;
using Rift.Automation.Windows;

internal static class VmTelemetrySpecs
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 22, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Pin = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        schemaVersion = 1, lab = "lab1",
        reader = new
        {
            instanceId = "reader-test-01",
            nonce = "0123456789abcdef0123456789abcdef",
            sourceAddress = "192.168.56.128"
        },
        receiver = new { instanceId = "receiver-test-01" },
        gameIdentity = new
        {
            processId = 1001, processStartUtcTicks = 1234567891011L,
            executablePath = @"C:\RIFT\rift_x64.exe",
            executableSha256 = new string('a', 64)
        }
    }));

    private static string PinWithReceiptPath(string receiptPath, string lab = "lab1")
    {
        var pin = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(Pin))!.AsObject();
        pin["receiptPath"] = receiptPath;
        pin["lab"] = lab;
        return pin.ToJsonString();
    }

    internal static string Receipt(DateTimeOffset now) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        receiverInstance = "receiver-test-01",
        receivedAtUtc = now.AddMilliseconds(-90).ToString("O"),
        receivedMonotonicNs = 100000000L,
        sourceAddress = "192.168.56.128",
        observation = new
        {
            schemaVersion = 1, instanceId = "reader-test-01",
            nonce = "0123456789abcdef0123456789abcdef",
            sequence = 21L, capturedAtUtc = now.AddMilliseconds(-120).ToString("O"),
            readerStatus = "running",
            target = new
            {
                processId = 1001, processStartUtcTicks = 1234567891011L,
                executablePath = @"C:\RIFT\rift_x64.exe",
                executableSha256 = new string('a', 64),
                readerSessionNonce = "0123456789abcdef0123456789abcdef"
            },
            riftReader2 = new
            {
                bindingStatus = "bound", freshnessStatus = "fresh",
                selectionStatus = "selected", lastAdvanceAgeMilliseconds = 25.0,
                freshnessTimeoutMilliseconds = 5000.0,
                scan = new { status = "complete", completed = true },
                observation = new
                {
                    schemaVersion = 1, sequence = 20L, epoch = 2L,
                    player = new { status = "ok", value = "synthetic-player" },
                    clock = new { status = "ok", value = 50.25 },
                    fields = new
                    {
                        coordX = new { status = "ok", value = 0.0 },
                        coordY = new { status = "ok", value = -4.0 },
                        coordZ = new { status = "ok", value = 5.75 }
                    }
                }
            }
        }
    });
    private static VmTelemetrySnapshot Read(string receipt, DateTimeOffset? now = null, byte[]? pin = null) =>
        VmTelemetryReader.ParsePinned(pin ?? Pin, Encoding.UTF8.GetBytes(receipt), now ?? Now);

    private static string FastReceipt(DateTimeOffset time,
        Action<System.Text.Json.Nodes.JsonObject>? modify = null)
    {
        var receipt = System.Text.Json.Nodes.JsonNode.Parse(Receipt(time))!.AsObject();
        var observation = receipt["observation"]!.AsObject();
        var full = observation["riftReader2"]!.AsObject();
        full["bindingStatus"] = "unbound";
        full["freshnessStatus"] = "unavailable";
        full["selectionStatus"] = "unavailable";
        observation["fastObservation"] = System.Text.Json.Nodes.JsonNode.Parse(
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                provenance = "recent-full-scan-anchored-candidate-regions/1",
                bindingStatus = "bound-scoped",
                freshnessStatus = "fresh-scoped",
                reason = (string?)null,
                anchor = new
                {
                    fullScanStartedAtTimestamp = 100000L,
                    fullScanCompletedAtTimestamp = 105000L,
                    timestampFrequency = 1000000L,
                    envelopeSha256 = new string('a', 64),
                    addonFrameSha256 = new string('a', 64),
                    candidateSequence = 20L,
                    candidateEpoch = 2L,
                    candidateClock = 50.0,
                    playerId = "synthetic-player",
                    regionCount = 1L,
                    totalRegionBytes = 4096L,
                    maxAgeMilliseconds = 10000L
                },
                selectionStatus = "selected-scoped",
                envelopeSha256 = new string('b', 64),
                addonFrameSha256 = new string('b', 64),
                candidateSequence = 22L,
                candidateEpoch = 2L,
                observation = new
                {
                    schemaVersion = 1,
                    addonVersion = "0.1.0",
                    sequence = 22L,
                    epoch = 2L,
                    clock = new { status = "ok", value = 50.5 },
                    player = new { status = "ok", value = "synthetic-player" },
                    fields = new
                    {
                        coordX = new { status = "ok", value = 9.5 },
                        coordY = new { status = "ok", value = -2.0 },
                        coordZ = new { status = "ok", value = 8.25 }
                    }
                },
                scan = new
                {
                    scope = "candidate-regions",
                    status = "complete",
                    completed = true,
                    startedAtTimestamp = 110000L,
                    completedAtTimestamp = 120000L,
                    plannedBytes = 4096L,
                    scannedBytes = 4096L,
                    regionCount = 1L,
                    elapsedMilliseconds = 10.0,
                    endpointCandidateCount = 2L,
                    malformedCandidateCount = 0L,
                    matchingCandidateCount = 2L,
                    identicalCopyCount = 1L,
                    reason = (string?)null
                },
                publishedAtTimestamp = 121000L,
                lastAdvanceAtTimestamp = 115000L,
                lastAdvanceAgeMilliseconds = 6.0,
                freshnessTimeoutMilliseconds = 5000.0
            }))!;
        modify?.Invoke(receipt);
        return receipt.ToJsonString();
    }

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Pinned real-schema receipt yields diagnostic XYZ without action authority", () =>
        {
            VmTelemetrySnapshot snap = Read(Receipt(Now));
            check(snap.Lab == "lab1" && snap.Status == "fresh_observed"
                && snap.HasPosition && !snap.CanAutomate, "scope");
            check(snap.X == 0 && snap.Y == -4 && snap.Z == 5.75
                && snap.GameProcessId == 1001 && snap.OuterSequence == 21
                && snap.AddonSequence == 20 && snap.AddonEpoch == 2, "position / identity");
            check(snap.ReaderSourceAddress == "192.168.56.128" &&
                snap.GameProcessStartUtcTicks == 1234567891011L &&
                snap.GameExecutablePath == @"C:\RIFT\rift_x64.exe" &&
                snap.GameExecutableSha256 == new string('a', 64) &&
                VmControllerBindingChecks.HasSourceBinding("lab1", snap),
                "private controller comparison fields derive from validated source");
            string json = JsonSerializer.Serialize(snap);
            check(!json.Contains("ReaderSourceAddress", StringComparison.Ordinal) &&
                !json.Contains("GameExecutablePath", StringComparison.Ordinal) &&
                !json.Contains("GameProcessStartUtcTicks", StringComparison.Ordinal),
                "private binding details never leave telemetry JSON");
        });
        test("Expired receiver observation cannot revive a position", () =>
        {
            VmTelemetrySnapshot snap = Read(Receipt(Now), Now.AddSeconds(5));
            check(!snap.HasPosition && snap.X is null
                && snap.Status == "unavailable" && snap.Reason == "stale_or_clock_uncertain", "expiry");
        });
        test("Typed freshness-only gaps distinguish expiration from invalid identity/timing", () =>
        {
            string baseReceipt = Receipt(Now);
            var aged = Read(baseReceipt, Now.AddSeconds(5));
            check(aged.Reason == "stale_or_clock_uncertain" && aged.FreshnessOnlyGap,
                "positive transport staleness is a timing gap");
            var future = Read(baseReceipt, Now.AddSeconds(-5));
            check(future.Reason == "stale_or_clock_uncertain" && !future.FreshnessOnlyGap,
                "future-clock uncertainty is not automatically recoverable");
            var expired = Read(baseReceipt.Replace(
                "\"lastAdvanceAgeMilliseconds\":25",
                "\"lastAdvanceAgeMilliseconds\":4900", StringComparison.Ordinal));
            check(expired.Reason == "source_expired" && expired.FreshnessOnlyGap,
                "ordinary positive source age expiry");
            var invalidAge = Read(baseReceipt.Replace(
                "\"lastAdvanceAgeMilliseconds\":25",
                "\"lastAdvanceAgeMilliseconds\":-5", StringComparison.Ordinal));
            check(invalidAge.Reason == "invalid_source_timing" &&
                !invalidAge.FreshnessOnlyGap, "negative producer age is invalid");
            var notFresh = baseReceipt.Replace(
                "\"freshnessStatus\":\"fresh\"",
                "\"freshnessStatus\":\"expired\"", StringComparison.Ordinal);
            check(Read(notFresh).FreshnessOnlyGap, "bound selected expired report");
            var unbound = notFresh.Replace(
                "\"bindingStatus\":\"bound\"",
                "\"bindingStatus\":\"unbound\"", StringComparison.Ordinal);
            check(!Read(unbound).FreshnessOnlyGap,
                "unbound source never receives freshness-only exemption");
            check(!Read(baseReceipt.Replace(
                "\"readerStatus\":\"running\"",
                "\"readerStatus\":\"terminated\"", StringComparison.Ordinal))
                .FreshnessOnlyGap, "stopped reader is fatal");
            check(!JsonSerializer.Serialize(aged).Contains("FreshnessOnlyGap",
                StringComparison.Ordinal), "private qualifier flag not serialized");
        });
        test("Expired receipt cannot hide malformed or unbound producer evidence", () =>
        {
            string original = Receipt(Now);
            var malformed = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            malformed["observation"]!.AsObject().Remove("riftReader2");
            check(!Read(malformed.ToJsonString(), Now.AddSeconds(5)).FreshnessOnlyGap,
                "no malformed stale freshness exemption");
            var unbound = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            unbound["observation"]!["riftReader2"]!["bindingStatus"] = "unbound";
            check(!Read(unbound.ToJsonString(), Now.AddSeconds(5)).FreshnessOnlyGap,
                "unbound stale source");
            var invalidClock = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            invalidClock["observation"]!["riftReader2"]!["observation"]!["clock"]!["value"] = -5.0;
            check(!Read(invalidClock.ToJsonString(), Now.AddSeconds(5)).FreshnessOnlyGap,
                "invalid retained clock");
            var incompleteScan = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            incompleteScan["observation"]!["riftReader2"]!["scan"]!["status"] = "incomplete";
            check(!Read(incompleteScan.ToJsonString(), Now.AddSeconds(5)).FreshnessOnlyGap,
                "incomplete scan");
            var staleBadTiming = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            staleBadTiming["observation"]!["riftReader2"]!["lastAdvanceAgeMilliseconds"] = -1.0;
            check(!Read(staleBadTiming.ToJsonString(), Now.AddSeconds(5)).FreshnessOnlyGap,
                "stale outer age cannot hide negative inner timing");
            var staleBadTimeout = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            staleBadTimeout["observation"]!["riftReader2"]!["freshnessTimeoutMilliseconds"] = 0.0;
            check(!Read(staleBadTimeout.ToJsonString(), Now.AddSeconds(5)).FreshnessOnlyGap,
                "stale outer age cannot hide invalid timeout");
            var declaredExpiredBad = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            declaredExpiredBad["observation"]!["riftReader2"]!["freshnessStatus"] = "expired";
            declaredExpiredBad["observation"]!["riftReader2"]!["lastAdvanceAgeMilliseconds"] = -1.0;
            check(!Read(declaredExpiredBad.ToJsonString()).FreshnessOnlyGap,
                "declared expiration requires valid inner timing");
            var expiredMalformed = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            var report = expiredMalformed["observation"]!["riftReader2"]!;
            report["lastAdvanceAgeMilliseconds"] = 4900.0;
            report["observation"]!.AsObject().Remove("fields");
            check(!Read(expiredMalformed.ToJsonString()).FreshnessOnlyGap,
                "inner expiry cannot hide malformed fields");
        });

        test("Parser-backed A to stale B to A source swap is a terminal window fault", () =>
        {
            string original = Receipt(Now);
            VmTelemetrySnapshot sourceA = Read(original);
            var changedPin = System.Text.Json.Nodes.JsonNode.Parse(
                Encoding.UTF8.GetString(Pin))!.AsObject();
            changedPin["reader"]!["sourceAddress"] = "192.168.56.129";
            var changedReceipt = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            changedReceipt["sourceAddress"] = "192.168.56.129";
            var sourceB = Read(changedReceipt.ToJsonString(), Now.AddSeconds(5),
                Encoding.UTF8.GetBytes(changedPin.ToJsonString()));
            check(sourceB.FreshnessOnlyGap && !sourceB.HasPosition &&
                sourceB.StreamIdentity == sourceA.StreamIdentity, "B is validated stale source");
            var window = new VmSustainedFeedbackWindow("lab1");
            window.Observe(sourceA, 0);
            window.Observe(sourceB, 500);
            window.Observe(sourceA with { OuterSequence = 22, AddonSequence = 21,
                AddonClock = 51 }, 1000);
            check(window.HasTerminalFault &&
                window.Finish(false).Reason == "source_identity_changed",
                "source IP mismatch during short lapse latched");
            var epochReceipt = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            epochReceipt["observation"]!["riftReader2"]!["observation"]!["epoch"] = 3L;
            var epochGap = Read(epochReceipt.ToJsonString(), Now.AddSeconds(5));
            check(epochGap.FreshnessOnlyGap && epochGap.AddonEpoch == 3,
                "valid newer producer epoch exposed privately");
            var epochWindow = new VmSustainedFeedbackWindow("lab1");
            epochWindow.Observe(sourceA, 0);
            epochWindow.Observe(epochGap, 500);
            epochWindow.Observe(sourceA with { OuterSequence = 22, AddonSequence = 21,
                AddonClock = 51 }, 1000);
            check(epochWindow.HasTerminalFault &&
                epochWindow.Finish(false).Reason == "source_identity_changed",
                "observed retired epoch cannot be hidden in expiration");
        });
        test("Terminated reader reports no current XYZ", () =>
        {
            string data = Receipt(Now).Replace("\"readerStatus\":\"running\"",
                "\"readerStatus\":\"terminated\"", StringComparison.Ordinal);
            var snap = Read(data);
            check(snap.Reason == "reader_not_running" && !snap.HasPosition, "terminated");
        });
        test("Wrong nonce and wrong process identity fail closed", () =>
        {
            string data = Receipt(Now);
            string badNonce = data.Replace("0123456789abcdef0123456789abcdef",
                "ffffffffffffffffffffffffffffffff", StringComparison.Ordinal);
            check(Read(badNonce).Reason == "identity_mismatch", "nonce");
            string badPid = data.Replace("\"processId\":1001",
                "\"processId\":1002", StringComparison.Ordinal);
            check(Read(badPid).Reason == "identity_mismatch", "process identity");
        });
        test("Incomplete coordinate and expired inner age fail closed", () =>
        {
            string data = Receipt(Now);
            string partial = data.Replace("\"coordY\":{\"status\":\"ok\",\"value\":-4}",
                "\"coordY\":{\"status\":\"unavailable\"}", StringComparison.Ordinal);
            var snap = Read(partial);
            check(snap.Reason == "coordinates_unavailable" && !snap.HasPosition
                && snap.Y is null, "missing coordinate");
            string expired = data.Replace("\"lastAdvanceAgeMilliseconds\":25",
                "\"lastAdvanceAgeMilliseconds\":4900", StringComparison.Ordinal);
            check(Read(expired).Reason == "source_expired", "inner age");
        });
        test("Malformed source-pin file becomes a typed unavailable result", () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{not-valid-json");
                VmTelemetrySnapshot s = VmTelemetryReader.ReadPinned(path, Now);
                check(s.Lab == "unknown" && s.Reason == "invalid_source_pin" &&
                    !s.HasPosition, "bad pin");
            }
            finally { File.Delete(path); }
        });
        test("Valid pin keeps its lab when receipt is missing then recovers", () =>
        {
            string pinPath = Path.GetTempFileName();
            string receiptPath = Path.Combine(Path.GetTempPath(),
                "rift-m4g-receipt-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(pinPath, PinWithReceiptPath(receiptPath));
                var absent = VmTelemetryReader.ReadPinned(pinPath, Now);
                check(absent.Lab == "lab1" && absent.Reason == "file_unavailable" &&
                    !absent.HasPosition && absent.X is null, "known lab on missing receipt");
                File.WriteAllText(pinPath, PinWithReceiptPath(receiptPath, "lab2"));
                var absentLab2 = VmTelemetryReader.ReadPinned(pinPath, Now);
                check(absentLab2.Lab == "lab2" && absentLab2.Reason == "file_unavailable" &&
                    !absentLab2.HasPosition, "lab2 missing receipt attributed correctly");
                File.WriteAllText(pinPath, PinWithReceiptPath(receiptPath));
                var watch = new VmTelemetryWatchTracker("lab1");
                var missing = watch.Observe(absent, 0);
                check(missing.State == "unavailable" && missing.Reason == "file_unavailable" &&
                    missing.Unavailable == 1, "start watch on missing receipt");
                File.WriteAllText(receiptPath, Receipt(Now));
                var present = VmTelemetryReader.ReadPinned(pinPath, Now);
                var recovered = watch.Observe(present, 1000);
                check(recovered.State == "first_sample" && recovered.HasCurrentPosition &&
                    recovered.LongestNoAdvanceMs == 1000 && recovered.NewSamples == 1,
                    "watch waits and observes restored receipt");
            }
            finally
            {
                File.Delete(pinPath);
                File.Delete(receiptPath);
            }
        });
        test("Malformed, empty and oversized receipts preserve validated pin lab", () =>
        {
            string pinPath = Path.GetTempFileName();
            string receiptPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(pinPath, PinWithReceiptPath(receiptPath));
                File.WriteAllText(receiptPath, "{not-valid-json");
                var malformed = VmTelemetryReader.ReadPinned(pinPath, Now);
                check(malformed.Lab == "lab1" && malformed.Reason == "invalid_receipt_or_pin" &&
                    !malformed.HasPosition, "malformed receipt");
                File.WriteAllText(receiptPath, "");
                var empty = VmTelemetryReader.ReadPinned(pinPath, Now);
                check(empty.Lab == "lab1" && empty.Reason == "size_limit" &&
                    !empty.HasPosition, "empty receipt");
                File.WriteAllText(receiptPath, new string('x', 133121));
                var oversized = VmTelemetryReader.ReadPinned(pinPath, Now);
                check(oversized.Lab == "lab1" && oversized.Reason == "size_limit" &&
                    !oversized.HasPosition, "oversize receipt");
            }
            finally
            {
                File.Delete(pinPath);
                File.Delete(receiptPath);
            }
        });
        test("Invalid pin identity never inherits a claimed lab from a bad receipt", () =>
        {
            var malformedReceipt = Encoding.UTF8.GetBytes("{invalid-receipt");
            var pin = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(Pin))!.AsObject();
            pin["reader"]!.AsObject().Remove("nonce");
            byte[] invalidPin = Encoding.UTF8.GetBytes(pin.ToJsonString());
            var parsed = VmTelemetryReader.ParsePinned(invalidPin, malformedReceipt, Now);
            check(parsed.Lab == "unknown" && parsed.Reason == "invalid_receipt_or_pin" &&
                !parsed.HasPosition, "invalid pin cannot claim lab on receipt parse failure");
            string path = Path.GetTempFileName();
            try
            {
                pin["receiptPath"] = Path.Combine(Path.GetTempPath(),
                    "rift-m4g-missing-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(path, pin.ToJsonString());
                var fromFile = VmTelemetryReader.ReadPinned(path, Now);
                check(fromFile.Lab == "unknown" && fromFile.Reason == "invalid_source_pin" &&
                    !fromFile.HasPosition, "pin identity validated before receipt I/O");
                var validPin = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(Pin))!.AsObject();
                validPin["schemaVersion"] = 2;
                var wrongSchema = VmTelemetryReader.ParsePinned(
                    Encoding.UTF8.GetBytes(validPin.ToJsonString()), malformedReceipt, Now);
                check(wrongSchema.Lab == "unknown" && !wrongSchema.HasPosition,
                    "unsupported pin schema cannot claim lab");
            }
            finally { File.Delete(path); }
        });
        test("Duplicate receipt properties preserve a validated lab without XYZ", () =>
        {
            var result = VmTelemetryReader.ParsePinned(Pin,
                Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}"), Now);
            check(result.Lab == "lab1" && result.Reason == "invalid_receipt_or_pin" &&
                !result.HasPosition && result.StreamIdentity is null,
                "receipt rejected after valid pin identity");
        });
        test("Fast-scoped candidate observation yields real-shape diagnostic XYZ", () =>
        {
            VmTelemetrySnapshot snap = Read(FastReceipt(Now));
            check(snap.HasPosition && snap.SourceScope == "candidate-regions" &&
                snap.X == 9.5 && snap.Y == -2 && snap.Z == 8.25 &&
                snap.AddonSequence == 22 && snap.AddonEpoch == 2 && !snap.CanAutomate,
                "anchored fast sample");
        });
        test("Full scan remains the selected source when complete and fresh", () =>
        {
            var snap = Read(Receipt(Now));
            check(snap.HasPosition && snap.SourceScope == "whole-readable-private",
                "full scan priority");
        });
        test("Two fresh sources select latest concordant sequence and clock", () =>
        {
            string dual = FastReceipt(Now, r =>
            {
                var full = r["observation"]!["riftReader2"]!;
                full["bindingStatus"] = "bound";
                full["freshnessStatus"] = "fresh";
                full["selectionStatus"] = "selected";
            });
            VmTelemetrySnapshot newestFast = Read(dual);
            check(newestFast.HasPosition && newestFast.SourceScope == "candidate-regions" &&
                newestFast.AddonSequence == 22, "newer fast scope");
            string newerFull = FastReceipt(Now, r =>
            {
                var full = r["observation"]!["riftReader2"]!;
                full["bindingStatus"] = "bound";
                full["freshnessStatus"] = "fresh";
                full["selectionStatus"] = "selected";
                full["observation"]!["sequence"] = 25;
                full["observation"]!["clock"]!["value"] = 51.0;
            });
            VmTelemetrySnapshot latestFull = Read(newerFull);
            check(latestFull.HasPosition && latestFull.SourceScope == "whole-readable-private" &&
                latestFull.AddonSequence == 25 && latestFull.X == 0, "newer full scope");
        });
        test("Two fresh contradictory sources must not populate a position", () =>
        {
            string conflict = FastReceipt(Now, r =>
            {
                var full = r["observation"]!["riftReader2"]!;
                full["bindingStatus"] = "bound";
                full["freshnessStatus"] = "fresh";
                full["selectionStatus"] = "selected";
                r["observation"]!["fastObservation"]!["observation"]!["clock"]!["value"] = 50.2;
            });
            VmTelemetrySnapshot result = Read(conflict);
            check(!result.HasPosition && result.Reason == "source_conflict", "inconsistent ordering");
        });
        test("Fast scope rejects incomplete region scan and conflicting epochs", () =>
        {
            string wrongRegion = FastReceipt(Now, r =>
                r["observation"]!["fastObservation"]!["scan"]!["scannedBytes"] = 2048);
            string wrongEpoch = FastReceipt(Now, r =>
                r["observation"]!["fastObservation"]!["candidateEpoch"] = 3);
            check(!Read(wrongRegion).HasPosition &&
                Read(wrongRegion).Reason == "source_not_fresh", "partial fast region");
            check(!Read(wrongEpoch).HasPosition &&
                Read(wrongEpoch).Reason == "source_not_fresh", "fast epoch conflict");
        });
        test("Fast scope rejects non-advancing candidate and unchanged hashes", () =>
        {
            string oldSequence = FastReceipt(Now, r =>
            {
                r["observation"]!["fastObservation"]!["candidateSequence"] = 20;
                r["observation"]!["fastObservation"]!["observation"]!["sequence"] = 20;
            });
            string repeatedHash = FastReceipt(Now, r =>
                r["observation"]!["fastObservation"]!["envelopeSha256"] =
                    new string('a', 64));
            check(!Read(oldSequence).HasPosition, "unadvanced candidate");
            check(!Read(repeatedHash).HasPosition, "unchanged anchor hash");
        });
        test("Fast scope rejects expired anchor and inconsistent monotonic age", () =>
        {
            string expired = FastReceipt(Now, r =>
            {
                var f = r["observation"]!["fastObservation"]!;
                f["scan"]!["startedAtTimestamp"] = 10190000L;
                f["scan"]!["completedAtTimestamp"] = 10200000L;
                f["lastAdvanceAtTimestamp"] = 10195000L;
                f["publishedAtTimestamp"] = 10201000L;
            });
            string badAge = FastReceipt(Now, r =>
                r["observation"]!["fastObservation"]!["lastAdvanceAgeMilliseconds"] = 1000.0);
            check(!Read(expired).HasPosition, "expired full scan anchor");
            check(!Read(badAge).HasPosition, "wrong monotonic age");
        });
        test("Fast scope cannot bypass expired receiver or stopped reader", () =>
        {
            var stale = Read(FastReceipt(Now), Now.AddSeconds(4));
            string stopped = FastReceipt(Now, r =>
                r["observation"]!["readerStatus"] = "terminated");
            check(!stale.HasPosition && stale.Reason == "stale_or_clock_uncertain",
                "outer expiry");
            check(!Read(stopped).HasPosition && Read(stopped).Reason == "reader_not_running",
                "reader stopped");
        });
        test("Duplicate and malformed JSON never populate game values", () =>
        {
            string data = Receipt(Now);
            string duplicate = data.Replace("\"readerStatus\":\"running\"",
                "\"readerStatus\":\"running\",\"readerStatus\":\"running\"", StringComparison.Ordinal);
            check(Read(duplicate).Reason == "invalid_receipt_or_pin"
                && !Read(duplicate).HasPosition, "duplicate");
            check(Read("{").Reason == "invalid_receipt_or_pin", "invalid json");
            check(VmTelemetryReader.ParsePinned(Pin, new byte[133121], Now).Reason == "size_limit",
                "read bounds");
        });
    }
}
