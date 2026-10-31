using System.Text;
using System.Text.Json;
using Rift.Automation.Windows;

internal static class VmSustainedEvidenceExportSpecs
{
    private static readonly DateTimeOffset Started =
        new(2026, 10, 31, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Finished = Started.AddMinutes(8);

    private static VmSustainedFeedbackReport PassingReport() =>
        new("ReadOnlySustainedTelemetryEvidence", VmSustainedFeedbackWindow.PolicyId,
            "lab1", true, true, "passed", 480000, 480000, 500, 4500,
            961, 961, 0, 0, 0, 0, 0, 500, 0);

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Evidence export is historical, redacted and not an action permit", () =>
        {
            VmSustainedEvidence evidence = VmSustainedEvidenceExporter.Capture(
                PassingReport(), "lab1", "0.5.7", Started, Finished);
            byte[] bytes = VmSustainedEvidenceExporter.Serialize(evidence, Finished.AddMinutes(1));
            using JsonDocument json = JsonDocument.Parse(bytes);
            JsonElement root = json.RootElement;
            check(root.GetProperty("SchemaVersion").GetInt32() == 1 &&
                root.GetProperty("HistoricalOnly").GetBoolean() &&
                !root.GetProperty("CurrentActionPermit").GetBoolean() &&
                root.GetProperty("ApplicationVersion").GetString() == "0.5.7" &&
                root.GetProperty("EvidenceOrigin").GetString() == "pinned-local-receiver",
                "historical envelope");
            JsonElement report = root.GetProperty("Report");
            check(report.GetProperty("SustainedTelemetryEvidencePassed").GetBoolean() &&
                !report.GetProperty("InputAttempted").GetBoolean() &&
                !report.GetProperty("GameFeedbackConfirmed").GetBoolean() &&
                !report.GetProperty("CanAutomate").GetBoolean(),
                "telemetry result never authorizes gameplay");
            string serialized = Encoding.UTF8.GetString(bytes);
            check(!serialized.Contains("sourcePin", StringComparison.OrdinalIgnoreCase) &&
                !serialized.Contains("receiptPath", StringComparison.OrdinalIgnoreCase) &&
                !serialized.Contains("host.json", StringComparison.OrdinalIgnoreCase) &&
                bytes.Length <= 16 * 1024, "only bounded aggregate evidence exported");
        });

        test("Evidence export cancellation demotes a late successful report", () =>
        {
            VmSustainedEvidence evidence = VmSustainedEvidenceExporter.Capture(
                PassingReport(), "lab1", "0.5.7", Started, Finished,
                stopRequested: true, origin: VmSustainedEvidenceExporter.SyntheticOrigin);
            check(evidence.Report.Reason == "cancelled" &&
                !evidence.Report.Completed && !evidence.Report.SustainedTelemetryEvidencePassed,
                "Stop wins over pass");
            string saved = Encoding.UTF8.GetString(
                VmSustainedEvidenceExporter.Serialize(evidence, Finished.AddSeconds(1)));
            check(saved.Contains("\"Reason\": \"cancelled\"", StringComparison.Ordinal) &&
                saved.Contains("\"HistoricalOnly\": true", StringComparison.Ordinal),
                "cancelled report is plainly historical");
        });

        test("Evidence export rejects wrong lab and forged policy or outcome", () =>
        {
            bool wrongLab = false, wrongPolicy = false, forgedPass = false;
            try { VmSustainedEvidenceExporter.Capture(PassingReport(), "lab2", "0.5.7", Started, Finished); }
            catch (ArgumentException) { wrongLab = true; }
            try
            {
                VmSustainedEvidenceExporter.Capture(
                    PassingReport() with { PolicyId = "other" }, "lab1", "0.5.7", Started, Finished);
            }
            catch (ArgumentException) { wrongPolicy = true; }
            try
            {
                VmSustainedEvidenceExporter.Capture(
                    PassingReport() with { Completed = false }, "lab1", "0.5.7", Started, Finished);
            }
            catch (ArgumentException) { forgedPass = true; }
            check(wrongLab && wrongPolicy && forgedPass, "unsupported evidence cannot be saved");
        });

        test("Evidence save creates once, never overwrites, and respects cancellation", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "autofight-evidence-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, "result.json");
            string cancelledDestination = Path.Combine(directory, "cancelled.json");
            byte[] bytes = VmSustainedEvidenceExporter.Serialize(
                VmSustainedEvidenceExporter.Capture(
                    PassingReport(), "lab1", "0.5.7", Started, Finished),
                Finished.AddMinutes(1));
            try
            {
                VmSustainedEvidenceExporter.SaveNew(destination, bytes);
                check(File.ReadAllBytes(destination).SequenceEqual(bytes), "new result exact bytes");
                bool collisionRejected = false;
                try { VmSustainedEvidenceExporter.SaveNew(destination, Encoding.UTF8.GetBytes("{}")); }
                catch (IOException) { collisionRejected = true; }
                check(collisionRejected && File.ReadAllBytes(destination).SequenceEqual(bytes),
                    "existing report unchanged");
                using var stopped = new CancellationTokenSource();
                stopped.Cancel();
                bool cancellationRejected = false;
                try { VmSustainedEvidenceExporter.SaveNew(cancelledDestination, bytes, stopped.Token); }
                catch (OperationCanceledException) { cancellationRejected = true; }
                check(cancellationRejected && !File.Exists(cancelledDestination),
                    "cancel before save leaves no destination");
            }
            finally
            {
                if (File.Exists(destination)) File.Delete(destination);
                if (File.Exists(cancelledDestination)) File.Delete(cancelledDestination);
                Directory.Delete(directory);
            }
        });
    }
}
