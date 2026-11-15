using System.Text;
using Rift.Automation.Windows;

internal static class PhysicalRiftPositionTraceImportSpecs
{
    private const int Pid = 6001;
    private const long StartTicks = 639303416167439797;
    private static readonly DateTimeOffset Captured =
        new(2026, 11, 15, 22, 30, 0, TimeSpan.Zero);

    private static PhysicalRiftPositionTraceReport Report(
        int count = 6, int maxEntries = 600)
    {
        var recorder = new PhysicalRiftPositionTraceRecorder(maxEntries);
        var tracker = new PhysicalRiftPositionChangeTracker(maxObservations: 1024);
        recorder.BeginSession(Pid, StartTicks);
        for (int index = 0; index < count; index++)
        {
            long elapsed = index * 1000L;
            float x = index < 3 ? 1 : 4;
            var sample = new PhysicalRiftPositionResult(
                PhysicalRiftPositionStatus.Candidate, "native-current-build-candidate",
                Pid, StartTicks, PhysicalRiftPositionReader.GameSha256,
                42, x, 2, 3, Captured.AddMilliseconds(elapsed), 12);
            recorder.Record(sample, tracker.Observe(sample, elapsed), elapsed,
                sample.CapturedAtUtc.AddMilliseconds(100));
        }
        return recorder.Snapshot();
    }

    private static void WithPrivateDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "AutoFight-M6TraceImportSpecs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return true; }
        return false;
    }

    private static string SaveNew(string directory, PhysicalRiftPositionTraceReport report)
    {
        string path = Path.Combine(directory, "original.json");
        PhysicalRiftPositionTraceExporter.SaveNew(path, report);
        return path;
    }

    private static bool RejectsAltered(string valid, string replacement)
    {
        if (string.Equals(valid, replacement, StringComparison.Ordinal))
            throw new InvalidOperationException("The tampered fixture was not changed.");
        return WithTampered(replacement);
    }

    private static bool WithTampered(string json)
    {
        bool rejected = false;
        WithPrivateDirectory(directory =>
        {
            string path = Path.Combine(directory, "tampered.json");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            byte[] before = File.ReadAllBytes(path);
            rejected = Throws<InvalidDataException>(() =>
                PhysicalRiftPositionTraceImporter.Load(path)) &&
                File.ReadAllBytes(path).SequenceEqual(before);
        });
        return rejected;
    }

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical XYZ import exactly reopens genuine A3 B3 export with source and timestamp", () =>
            WithPrivateDirectory(directory =>
            {
                var original = Report();
                string path = SaveNew(directory, original);
                byte[] before = File.ReadAllBytes(path);
                var recovered = PhysicalRiftPositionTraceImporter.Load(path);
                var result = PhysicalRiftPositionTraceAssessor.Assess(recovered);
                check(recovered.Schema == PhysicalRiftPositionTraceRecorder.EvidenceSchema &&
                      recovered.TotalSamples == 6 && recovered.RetainedSamples == 6 &&
                      recovered.ProcessId == Pid && recovered.ProcessStartUtcTicks == StartTicks &&
                      recovered.ExecutableSha256 == PhysicalRiftPositionReader.GameSha256 &&
                      recovered.PlayerUnitKey == 42 &&
                      recovered.Samples[5].CapturedAtUtc == Captured.AddSeconds(5) &&
                      recovered.Samples[0].MonotonicElapsedMilliseconds == 0 &&
                      result.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved &&
                      result.BeforeX == 1 && result.AfterX == 4 && result.Distance3D == 3 &&
                      !result.CanAutomate && !result.GameplayMovementProven &&
                      !result.PassiveFreshnessQualified &&
                      File.ReadAllBytes(path).SequenceEqual(before),
                    "genuine M4 file roundtrips with complete source/times and pure M5 A/B semantics");
            }));

        test("Physical XYZ import accepts complete 600-ring JSON but assessment marks evictions", () =>
            WithPrivateDirectory(directory =>
            {
                string path = SaveNew(directory, Report(605));
                var loaded = PhysicalRiftPositionTraceImporter.Load(path);
                var assessment = PhysicalRiftPositionTraceAssessor.Assess(loaded);
                check(loaded.TotalSamples == 605 && loaded.RetainedSamples == 600 &&
                      loaded.EvictedSamples == 5 && loaded.Samples[0].Sequence == 6 &&
                      loaded.Samples[^1].Sequence == 605 &&
                      assessment.State == PhysicalRiftPositionTraceAssessmentState.Truncated &&
                      !assessment.CanAutomate,
                    "full retained FIFO is imported without inventing missing earliest samples");
            }));

        test("Physical XYZ import accepts error-only canonical report without synthetic XYZ", () =>
            WithPrivateDirectory(directory =>
            {
                var recorder = new PhysicalRiftPositionTraceRecorder();
                recorder.BeginSession(Pid, StartTicks);
                recorder.RecordUnavailable("secret=ignored", 123, Captured);
                var loaded = PhysicalRiftPositionTraceImporter.Load(
                    SaveNew(directory, recorder.Snapshot()));
                var assessment = PhysicalRiftPositionTraceAssessor.Assess(loaded);
                check(loaded.CandidateSamples == 0 && loaded.RejectedSamples == 1 &&
                      loaded.ExecutableSha256 is null && loaded.PlayerUnitKey is null &&
                      loaded.Samples[0].X is null && loaded.Samples[0].Y is null &&
                      assessment.State == PhysicalRiftPositionTraceAssessmentState.Interrupted &&
                      !assessment.CanAutomate,
                    "no-source error evidence imports, but cannot acquire position or authority");
            }));

        test("Physical XYZ import rejects forged true or omitted computed denial flags", () =>
        {
            string valid = Report().ToJson();
            check(RejectsAltered(valid,
                      valid.Replace("\"canAutomate\":false", "\"canAutomate\":true",
                          StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace("\"gameplayMovementProven\":false",
                          "\"gameplayMovementProven\":true", StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace("\"passiveFreshnessQualified\":false",
                          "\"passiveFreshnessQualified\":true", StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace(",\"canAutomate\":false", "", StringComparison.Ordinal)),
                "computed flags may not deserialize silently away or get omitted");
        });

        test("Physical XYZ import rejects duplicate unknown or malformed canonical fields", () =>
        {
            string valid = Report().ToJson();
            check(RejectsAltered(valid,
                      valid.Replace("\"schema\":",
                          "\"schema\":\"autofight-physical-xyz-trace/1\",\"schema\":",
                          StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace("\"schema\":", "\"unrecognized\":0,\"schema\":",
                          StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace("\"schema\":\"autofight-physical-xyz-trace/1\"",
                          "\"schema\":\"autofight-physical-xyz-trace/2\"",
                          StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace("\"totalSamples\":6", "\"totalSamples\":999",
                          StringComparison.Ordinal)) &&
                  RejectsAltered(valid, valid + "\n") &&
                  WithTampered("{\"schema\":1"),
                "unexpected, duplicate, missing, altered or noncanonical data fails closed");
        });

        test("Physical XYZ import rejects forged source and rejected-coordinate disclosure", () =>
        {
            string valid = Report().ToJson();
            string unavailableJson;
            {
                var recorder = new PhysicalRiftPositionTraceRecorder();
                recorder.BeginSession(Pid, StartTicks);
                recorder.RecordUnavailable("native_timeout", 20, Captured);
                unavailableJson = recorder.Snapshot().ToJson();
            }
            check(RejectsAltered(valid, valid.Replace(
                      PhysicalRiftPositionReader.GameSha256,
                      new string('F', PhysicalRiftPositionReader.GameSha256.Length),
                      StringComparison.Ordinal)) &&
                  RejectsAltered(unavailableJson,
                      unavailableJson.Replace("\"x\":null", "\"x\":123",
                          StringComparison.Ordinal)) &&
                  RejectsAltered(valid,
                      valid.Replace("\"acceptedForComparison\":true",
                          "\"acceptedForComparison\":false", StringComparison.Ordinal)),
                "source fingerprint, unaccepted XYZ and acceptance status cannot be forged");
        });

        test("Physical XYZ import bounds size and rejects invalid UTF8 without disk changes", () =>
            WithPrivateDirectory(directory =>
            {
                string path = Path.Combine(directory, "oversize.json");
                using (var output = new FileStream(path, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                    output.SetLength(PhysicalRiftPositionTraceExporter.MaximumUtf8JsonBytes + 1L);
                bool oversized = Throws<InvalidDataException>(() =>
                    PhysicalRiftPositionTraceImporter.Load(path));
                string bad = Path.Combine(directory, "invalid-utf8.json");
                File.WriteAllBytes(bad, [0xC3, 0x28]);
                byte[] before = File.ReadAllBytes(bad);
                check(oversized &&
                      Throws<InvalidDataException>(() =>
                          PhysicalRiftPositionTraceImporter.Load(bad)) &&
                      File.ReadAllBytes(bad).SequenceEqual(before),
                    "1MiB ceiling, strict UTF-8 and no mutation of source files");
            }));

        test("Physical XYZ import rejects relative device ADS reserved and non-JSON paths", () =>
        {
            string[] invalid =
            [
                "relative.json",
                @"\\?\C:\temp\device.json",
                @"\\.\C:\temp\device.json",
                @"\\server\share\trace.json",
                @"C:\temp\stream:more.json",
                @"C:\temp\CON.json",
                @"C:\temp\COM1.json",
                @"C:\temp\trace.txt",
                ""
            ];
            check(invalid.All(path => Throws<ArgumentException>(() =>
                    PhysicalRiftPositionTraceImporter.Load(path))),
                "only ordinary absolute drive JSON files may be read");
        });

        test("Physical XYZ import missing path and directory do not invent a report", () =>
            WithPrivateDirectory(directory =>
            {
                string missing = Path.Combine(directory, "missing.json");
                string actualDirectory = Path.Combine(directory, "folder.json");
                Directory.CreateDirectory(actualDirectory);
                bool directoryRejected = false;
                try { _ = PhysicalRiftPositionTraceImporter.Load(actualDirectory); }
                catch (IOException) { directoryRejected = true; }
                catch (UnauthorizedAccessException) { directoryRejected = true; }
                check(Throws<FileNotFoundException>(() =>
                          PhysicalRiftPositionTraceImporter.Load(missing)) &&
                      directoryRejected,
                    "missing file and actual directory cannot become an accepted trace");
            }));
    }
}
