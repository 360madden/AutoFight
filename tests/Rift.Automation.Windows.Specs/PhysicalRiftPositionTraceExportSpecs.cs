using System.Text;
using System.Text.Json;
using Rift.Automation.Windows;

internal static class PhysicalRiftPositionTraceExportSpecs
{
    private static readonly DateTimeOffset Captured =
        new(2026, 11, 15, 22, 0, 0, TimeSpan.Zero);
    private const int Pid = 6001;
    private const long StartTicks = 639303416167439797;

    private static PhysicalRiftPositionResult Candidate(int index) =>
        new(PhysicalRiftPositionStatus.Candidate, "native-current-build-candidate",
            Pid, StartTicks, PhysicalRiftPositionReader.GameSha256, 42,
            index, 2, 3, Captured.AddMilliseconds(index * 100L), 12);

    private static PhysicalRiftPositionTraceReport Report(int count = 3, int retention = 600)
    {
        var recorder = new PhysicalRiftPositionTraceRecorder(retention);
        var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 1024);
        recorder.BeginSession(Pid, StartTicks);
        for (int index = 0; index < count; index++)
        {
            var sample = Candidate(index);
            long elapsed = index * 100L;
            recorder.Record(sample, tracker.Observe(sample, elapsed), elapsed,
                sample.CapturedAtUtc.AddMilliseconds(100));
        }
        return recorder.Snapshot();
    }

    private static void WithPrivateTemporaryDirectory(Action<string> action)
    {
        // Only a uniquely created OS-temp subdirectory belonging to this test is removed.
        string directory = Path.Combine(Path.GetTempPath(),
            "AutoFight-PhysicalXYZExportSpecs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool FailsWith<TException>(Action action)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical XYZ export creates one UTF8 no-BOM versioned JSON report", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                string destination = Path.Combine(directory, "manual-physical-xyz.json");
                var report = Report();
                PhysicalRiftPositionTraceExporter.SaveNew(destination, report);
                byte[] file = File.ReadAllBytes(destination);
                string text = Encoding.UTF8.GetString(file);
                using var json = JsonDocument.Parse(text);
                var root = json.RootElement;
                check(file.Length > 0 && file.Length < PhysicalRiftPositionTraceExporter.MaximumUtf8JsonBytes &&
                    !file.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }),
                    "bounded UTF8 JSON does not prepend a BOM");
                check(root.ValueKind == JsonValueKind.Object &&
                    root.GetProperty("schema").GetString() ==
                        PhysicalRiftPositionTraceRecorder.EvidenceSchema &&
                    root.GetProperty("totalSamples").GetInt32() == 3 &&
                    root.GetProperty("samples").GetArrayLength() == 3 &&
                    root.GetProperty("candidateSamples").GetInt32() == 3,
                    "one versioned JSON document contains exactly the expected sampled data");
                check(!root.GetProperty("passiveFreshnessQualified").GetBoolean() &&
                    !root.GetProperty("gameplayMovementProven").GetBoolean() &&
                    !root.GetProperty("canAutomate").GetBoolean(),
                    "historical export explicitly denies producer freshness and action authority");
            }));

        test("Physical XYZ export refuses second same filename and preserves original exact bytes", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                string destination = Path.Combine(directory, "no-clobber.json");
                PhysicalRiftPositionTraceExporter.SaveNew(destination, Report(3));
                byte[] original = File.ReadAllBytes(destination);
                bool blocked = FailsWith<IOException>(() =>
                    PhysicalRiftPositionTraceExporter.SaveNew(destination, Report(4)));
                check(blocked && File.ReadAllBytes(destination).SequenceEqual(original),
                    "existing file is never replaced even with a different new report");
                check(Directory.GetFiles(directory, "*.tmp").Length == 0 &&
                    Directory.GetFiles(directory).Length == 1,
                    "the failed same-directory staged publish cleans only its own temp file");
            }));

        test("Physical XYZ export blocks relative extension ADS device and reserved filenames", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                var report = Report(1);
                string[] invalid =
                [
                    "relative.json",
                    Path.Combine(directory, "wrong.txt"),
                    Path.Combine(directory, "stream:alternate.json"),
                    Path.Combine(directory, "CON.json"),
                    Path.Combine(directory, "nul.json"),
                    Path.Combine(directory, "COM1.json"),
                    Path.Combine(directory, "Lpt9.json"),
                    @"\\?\C:\temp\device.json",
                    @"\\.\C:\temp\device.json",
                    "//?/C:/temp/device.json",
                    ""
                ];
                bool allRejected = invalid.All(path =>
                    FailsWith<ArgumentException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(path, report)));
                check(allRejected && Directory.GetFiles(directory).Length == 0,
                    "malformed or device/alternate-stream targets do not create files");
                string missingParent = Path.Combine(directory, "not-created", "trace.json");
                check(FailsWith<DirectoryNotFoundException>(() =>
                    PhysicalRiftPositionTraceExporter.SaveNew(missingParent, report)) &&
                    !Directory.Exists(Path.GetDirectoryName(missingParent)!),
                    "export never creates an implicit parent directory");
            }));

        test("Physical XYZ export rejects empty and untrusted report before filesystem IO", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                string target = Path.Combine(directory, "untrusted.json");
                var empty = new PhysicalRiftPositionTraceRecorder();
                empty.BeginSession(Pid, StartTicks);
                var good = Report(2);
                bool blocked =
                    FailsWith<InvalidDataException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(target, empty.Snapshot())) &&
                    FailsWith<InvalidDataException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(target,
                            good with { Schema = "unsafe-trace/999" })) &&
                    FailsWith<InvalidDataException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(target,
                            good with { MaxEntries = 601 })) &&
                    FailsWith<InvalidDataException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(target,
                            good with { CandidateSamples = 0 })) &&
                    FailsWith<InvalidDataException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(target,
                            good with { ExecutableSha256 = new string('F', 64) })) &&
                    FailsWith<InvalidDataException>(() =>
                        PhysicalRiftPositionTraceExporter.SaveNew(target,
                            good with
                            {
                                Samples = good.Samples.Select((entry, index) =>
                                    index == 0 ? entry with
                                    {
                                        ComparisonReasonCode = "token=C:\\private\\secrets.json"
                                    } : entry).ToArray()
                            }));
                check(blocked && !File.Exists(target) && Directory.GetFiles(directory).Length == 0,
                    "zero, forged-schema, oversize metadata, bad fingerprint and malicious strings never reach disk");
            }));

        test("Physical XYZ export rejects untrusted rejected-XYZ payload", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                var good = Report(1);
                string destination = Path.Combine(directory, "forged-coordinates.json");
                var forged = good with
                {
                    Samples = good.Samples.Select(entry =>
                        entry with
                        {
                            AcceptedForComparison = false,
                            Disposition = PhysicalRiftPositionTraceDisposition.Unavailable,
                            X = 123f
                        }).ToArray()
                };
                check(FailsWith<InvalidDataException>(() =>
                    PhysicalRiftPositionTraceExporter.SaveNew(destination, forged)) &&
                    !File.Exists(destination), "unaccepted coordinates cannot be exported");
            }));

        test("Physical XYZ export 600 retained samples roundtrip within bounded byte budget", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                var report = Report(count: 605);
                check(report.TotalSamples == 605 && report.RetainedSamples == 600 &&
                    report.EvictedSamples == 5 && report.Samples[0].Sequence == 6,
                    "synthetic source provides full bounded ring evidence");
                string target = Path.Combine(directory, "six-hundred.json");
                PhysicalRiftPositionTraceExporter.SaveNew(target, report);
                byte[] bytes = File.ReadAllBytes(target);
                using var json = JsonDocument.Parse(bytes);
                var root = json.RootElement;
                var samples = root.GetProperty("samples");
                check(bytes.Length is > 0 and <= PhysicalRiftPositionTraceExporter.MaximumUtf8JsonBytes &&
                    samples.GetArrayLength() == 600 &&
                    samples[0].GetProperty("sequence").GetInt32() == 6 &&
                    samples[599].GetProperty("sequence").GetInt32() == 605,
                    "all 600 retained samples serialize in-order, with no unbounded JSON");
                check(root.GetProperty("evictedSamples").GetInt32() == 5 &&
                    !root.GetProperty("canAutomate").GetBoolean(),
                    "whole-report count and denied authority survive JSON roundtrip");
            }));

        test("Physical XYZ export allows error-only source-pinned trace without invented build or XYZ", () =>
            WithPrivateTemporaryDirectory(directory =>
            {
                var recorder = new PhysicalRiftPositionTraceRecorder();
                recorder.BeginSession(Pid, StartTicks);
                recorder.RecordUnavailable("token=SECRET C:\\private\\rift.exe", 100, Captured);
                string destination = Path.Combine(directory, "unavailable-only.json");
                PhysicalRiftPositionTraceExporter.SaveNew(destination, recorder.Snapshot());
                string text = File.ReadAllText(destination);
                using var json = JsonDocument.Parse(text);
                var root = json.RootElement;
                var item = root.GetProperty("samples")[0];
                check(root.GetProperty("processId").GetInt32() == Pid &&
                    root.GetProperty("executableSha256").ValueKind == JsonValueKind.Null &&
                    root.GetProperty("playerUnitKey").ValueKind == JsonValueKind.Null &&
                    item.GetProperty("x").ValueKind == JsonValueKind.Null &&
                    item.GetProperty("y").ValueKind == JsonValueKind.Null &&
                    item.GetProperty("z").ValueKind == JsonValueKind.Null,
                    "failure-only trace keeps exact selected process but no fabricated native data");
                check(!text.Contains("SECRET", StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains("private", StringComparison.OrdinalIgnoreCase),
                    "untrusted native exception reason is redacted");
            }));
    }
}
