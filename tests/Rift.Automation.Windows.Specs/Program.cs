using System.Diagnostics;
using Rift.Automation.Windows;

int passed = 0, failed = 0;
void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex.GetType().Name} {ex.Message}"); }
}

Test("Inventory is metadata only", () =>
{
    var result = RiftClientInventory.ScanNamed("rift-process-name-that-does-not-exist");
    Check(result.Mode == "WindowsProcessMetadata" && !result.HasGameState, "no game state");
    Check(result.Clients.Count == 0 && result.ErrorCode is null && !result.Truncated, "empty process list");
});
Test("Cancellation before discovery", () =>
{
    using var source = new CancellationTokenSource();
    source.Cancel();
    try { RiftClientInventory.ScanNamed("dotnet", source.Token); throw new InvalidOperationException("did not cancel"); }
    catch (OperationCanceledException) { }
});
Test("Invalid process name", () =>
{
    try { RiftClientInventory.ScanNamed("  "); throw new InvalidOperationException("accepted empty name"); }
    catch (ArgumentException) { }
});
Test("Title sanitization", () =>
{
    Check(RiftClientInventory.CleanTitle(" abc\r\nDEF ") == "abc  DEF", "control characters");
    Check(RiftClientInventory.CleanTitle(" ") is null, "blank title");
    Check(RiftClientInventory.CleanTitle(new string('z', 300))?.Length == 256, "title bounded");
});
Test("Own process metadata without memory reading", () =>
{
    using var current = Process.GetCurrentProcess();
    RiftClientInfo info = RiftClientInventory.Inspect(current);
    Check(info.ProcessId == Environment.ProcessId, "PID");
    Check(info.StartedAtUtc is not null && info.Identity is not null, "creation time");
    Check(info.WindowsSessionId is not null, "session");
    Check(!string.IsNullOrWhiteSpace(info.ImagePath), "path");
    Check(info.Status != "Unavailable" && info.Status != "Exited", "liveness");
    Check(info.Detail.Contains("Metadata only.", StringComparison.Ordinal) || info.Status == "Partial", "scope");
});
Test("Own-process snapshot discovery", () =>
{
    using var current = Process.GetCurrentProcess();
    ClientInventorySnapshot result = RiftClientInventory.ScanNamed(current.ProcessName);
    Check(result.ErrorCode is null && !result.HasGameState && result.Clients.Count <= 64, "bounded metadata");
    Check(result.ElapsedMs >= 0 && result.CheckedAtUtc != default, "timing");
    Check(result.Clients.All(x => x.ProcessId > 0), "valid PIDs");
});

string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "rac-tsv", name);
Test("Manual player saved snapshot retains partial coordinates", () =>
{
    var result = ManualUnitSnapshotReader.OpenFile(Fixture("player_available.tsv"));
    Check(result.Success && result.Snapshot is not null, "valid export");
    var unit = result.Snapshot!;
    Check(unit.Origin == "ManualSavedFile" && unit.Subject == "player" && unit.Sequence == 1, "manual provenance");
    Check(unit.X.Value == 0 && unit.Y.Value is null && unit.Y.Status == "missing" && unit.Z.Value == -3.25, "partial XYZ");
    Check(unit.Combat.Value == false && unit.Name == "Joey", "false and name");
    Check(!unit.HasCompletePosition && !unit.HasLiveSampleTime && !unit.CanAutomate, "no live promotion");
});
Test("Manual saved target unavailable does not invent values", () =>
{
    var result = ManualUnitSnapshotReader.OpenFile(Fixture("target_unavailable.tsv"));
    Check(result.Success && result.Snapshot is not null, "valid unavailable record");
    var unit = result.Snapshot!;
    Check(unit.RecordStatus == "unavailable" && unit.RecordReason == "target_unresolved", "typed failure");
    Check(unit.X.Status == "not_in_record" && unit.X.Value is null && unit.Combat.Value is null, "no invented fields");
});
Test("Complete manual XYZ still cannot automate", () =>
{
    var raw = File.ReadAllText(Fixture("player_available.tsv"), System.Text.Encoding.ASCII);
    raw = raw.Replace("F\tcoordY\tnumber\tunavailable\t\tapi_nil\n",
        "F\tcoordY\tnumber\tavailable\t1.5\t\n", StringComparison.Ordinal);
    var result = ManualUnitSnapshotReader.Parse(System.Text.Encoding.ASCII.GetBytes(raw));
    Check(result.Success && result.Snapshot?.HasCompletePosition == true, "all coordinates");
    Check(result.Snapshot!.Y.Value == 1.5 && !result.Snapshot.CanAutomate && !result.Snapshot.HasLiveSampleTime,
        "complete saved snapshot is not live");
});
Test("Invalid and oversized unit data fail closed", () =>
{
    var invalid = ManualUnitSnapshotReader.Parse(System.Text.Encoding.ASCII.GetBytes("garbage\n"));
    Check(!invalid.Success && invalid.ErrorCode == "invalid_tsv" && invalid.Snapshot is null, "invalid wire");
    var oversized = ManualUnitSnapshotReader.Parse(new byte[8193]);
    Check(!oversized.Success && oversized.ErrorCode == "wire_limit" && oversized.Snapshot is null, "bound");
});
Test("Manual file errors do not expose earlier data", () =>
{
    var absent = ManualUnitSnapshotReader.OpenFile(Fixture("not-present.tsv"));
    Check(!absent.Success && absent.ErrorCode == "file_unavailable" && absent.Snapshot is null, "not found");
    var folder = ManualUnitSnapshotReader.OpenFile(AppContext.BaseDirectory);
    Check(!folder.Success && folder.Snapshot is null, "directory not a file");
});

VmBridgeSpecs.Run(Test, Check);
VmTelemetrySpecs.Run(Test, Check);
StandaloneTelemetrySpecs.Run(Test, Check);
StandaloneTelemetryHttpSpecs.Run(Test, Check);
PhysicalTelemetryIngestSpecs.Run(Test, Check);
StandaloneTelemetryHostSessionSpecs.Run(Test, Check);
StandaloneTelemetryHostConfigurationSpecs.Run(Test, Check);
StandaloneTelemetryQualificationSpecs.Run(Test, Check);
StandaloneVmPulseSpecs.Run(Test, Check);
PhysicalRiftPositionSpecs.Run(Test, Check);
PhysicalRiftPositionChangeSpecs.Run(Test, Check);
PhysicalRiftPositionTraceSpecs.Run(Test, Check);
PhysicalRiftPositionTraceExportSpecs.Run(Test, Check);
PhysicalRiftPositionTraceImportSpecs.Run(Test, Check);
PhysicalRiftPositionTraceCaptureSpecs.Run(Test, Check);
PhysicalRiftPositionTraceAssessmentSpecs.Run(Test, Check);
VmTelemetryWatchSpecs.Run(Test, Check);
VmPulseObservationSpecs.Run(Test, Check);
VmControllerBindingSpecs.Run(Test, Check);
VmBoundPreflightSpecs.Run(Test, Check);
VmSustainedFeedbackSpecs.Run(Test, Check);
VmSustainedEvidenceExportSpecs.Run(Test, Check);

Console.WriteLine($"Windows / VM telemetry specs: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
