using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using Rift.Automation.Core;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private VmSustainedEvidence? _lastVmQualificationEvidence;
    private string _vmEvidenceOrigin = VmSustainedEvidenceExporter.PinnedFileOrigin;
    private Func<string?>? _vmEvidencePathPicker;
    private Func<string, byte[], CancellationToken, Task> _vmEvidenceWriter =
        static (path, bytes, token) =>
            Task.Run(() => VmSustainedEvidenceExporter.SaveNew(path, bytes, token), token);

    // Injectable only for deterministic nonshown WPF smoke. Production runs
    // the whole async reader on a worker: its initial read is synchronous.
    private Func<string, string, CancellationToken, IProgress<VmSustainedFeedbackProgress>?,
        Task<VmSustainedFeedbackReport>> _vmQualificationRunner =
        static (lab, sourcePin, token, progress) =>
            Task.Run(() => VmSustainedFeedbackRunner.RunAsync(lab, sourcePin, token, progress), token);

    private async void VmQualifyStartClicked(object sender, RoutedEventArgs e) =>
        await RunVmQualificationAsync();

    private async Task RunVmQualificationAsync()
    {
        if (_closing) return;
        string lab = (VmQualifyLab.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        if (lab is not ("lab1" or "lab2"))
        {
            VmQualifySummary.Text = "Select Lab 1 or Lab 2; no evidence collected.";
            return;
        }

        // Capture exactly once before any await. Later edits never retarget the run.
        string capturedPin = lab == "lab1" ? VmLab1Pin.Text : VmLab2Pin.Text;
        if (string.IsNullOrWhiteSpace(capturedPin))
        {
            VmQualifySummary.Text = "A reviewed source pin is required; no evidence collected.";
            return;
        }

        CancellationTokenSource? run = TryBeginVmOperation(VmOperation.SustainedQualification);
        if (run is null) return;

        // A new accepted attempt owns its own report; rejected Starts leave the old one intact.
        _lastVmQualificationEvidence = null;
        VmQualifySaveStatus.Text = "No finished result to save while this check is running.";
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        string label = lab == "lab1" ? "Lab 1" : "Lab 2";
        bool receivingProgress = true;
        VmQualifyProgress.Text = label + ": reviewed source selection captured; checking 0/480 seconds.";
        VmQualifySummary.Text = "Running read-only policy. No verdict yet; no input or controller contact.";
        IProgress<VmSustainedFeedbackProgress> progress = new Progress<VmSustainedFeedbackProgress>(update =>
        {
            if (!receivingProgress || !OwnsVmOperation(VmOperation.SustainedQualification, run) ||
                run.IsCancellationRequested || update.Lab != lab)
                return;
            VmQualifyProgress.Text = FormatQualificationProgress(label, update);
        });

        try
        {
            VmSustainedFeedbackReport report =
                await _vmQualificationRunner(lab, capturedPin, run.Token, progress);
            if (!OwnsVmOperation(VmOperation.SustainedQualification, run)) return;
            receivingProgress = false;
            // This normalized object is the one displayed AND saved. Stop cannot
            // leave a cached pass behind a cancelled UI verdict.
            VmSustainedEvidence evidence = VmSustainedEvidenceExporter.Capture(
                report, lab, typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
                startedAtUtc, DateTimeOffset.UtcNow, run.IsCancellationRequested,
                _vmEvidenceOrigin);
            VmSustainedFeedbackReport effective = evidence.Report;
            _lastVmQualificationEvidence = evidence;
            VmQualifyProgress.Text = string.Create(CultureInfo.InvariantCulture,
                $"{label}: observed {effective.ObservedDurationMs / 1000d:0.0}/480 seconds, " +
                $"polls {effective.Polls}, accepted {effective.NewSamples}, " +
                $"repeats {effective.Repeated}, unavailable {effective.Unavailable}, " +
                $"longest gap {effective.LongestNoAdvanceMs} ms.");
            VmQualifySaveStatus.Text = "Redacted historical result ready to save; no action permission.";

            if (effective.Reason == "cancelled")
                VmQualifySummary.Text = "Cancelled/incomplete. No evidence pass; no action permission.";
            else if (effective.SustainedTelemetryEvidencePassed)
                VmQualifySummary.Text = "Telemetry evidence PASSED for this observation window. " +
                    "Not movement qualification or an action permit; no input sent.";
            else
                VmQualifySummary.Text = "Failed/incomplete (" + SafeQualificationReason(effective.Reason) +
                    "). No action permission; no input sent.";
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            if (OwnsVmOperation(VmOperation.SustainedQualification, run))
                VmQualifySummary.Text = "Cancelled/incomplete. No evidence pass; no action permission.";
        }
        catch (Exception)
        {
            if (OwnsVmOperation(VmOperation.SustainedQualification, run))
                VmQualifySummary.Text = "Failed (diagnostic_interrupted). No action permission; no input sent.";
        }
        finally
        {
            receivingProgress = false;
            EndVmOperation(VmOperation.SustainedQualification, run);
        }
    }

    private async void VmQualifySaveClicked(object sender, RoutedEventArgs e) =>
        await SaveVmQualificationAsync();

    private async Task SaveVmQualificationAsync()
    {
        VmSustainedEvidence? snapshot = _lastVmQualificationEvidence;
        if (_closing || snapshot is null) return;
        CancellationTokenSource? run = TryBeginVmOperation(VmOperation.SaveEvidence);
        if (run is null) return;
        try
        {
            // The operation lease is held through the dialog and disk publication.
            // Never read current lab controls or telemetry again to populate a save.
            string? destination = ChooseVmEvidenceSavePath(snapshot);
            if (!OwnsVmOperation(VmOperation.SaveEvidence, run)) return;
            if (destination is null)
            {
                VmQualifySaveStatus.Text = "Save cancelled. The last result remains available.";
                return;
            }
            byte[] json = VmSustainedEvidenceExporter.Serialize(snapshot, DateTimeOffset.UtcNow);
            await _vmEvidenceWriter(destination, json, run.Token);
            if (OwnsVmOperation(VmOperation.SaveEvidence, run))
                VmQualifySaveStatus.Text = "Saved a redacted historical report. No current action permission.";
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            if (OwnsVmOperation(VmOperation.SaveEvidence, run))
                VmQualifySaveStatus.Text = "Save interrupted. The result remains available.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.Security.SecurityException)
        {
            if (OwnsVmOperation(VmOperation.SaveEvidence, run))
                VmQualifySaveStatus.Text = "Save failed or destination already exists. Choose a new JSON filename; nothing was replaced.";
        }
        finally
        {
            EndVmOperation(VmOperation.SaveEvidence, run);
        }
    }

    private string? ChooseVmEvidenceSavePath(VmSustainedEvidence snapshot)
    {
        if (_vmEvidencePathPicker is not null) return _vmEvidencePathPicker();
        var dialog = new SaveFileDialog
        {
            Title = "Save redacted historical telemetry evidence (new file)",
            Filter = "JSON evidence (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = "rift-evidence-" + snapshot.Report.Lab + "-" +
                snapshot.FinishedAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static string FormatQualificationProgress(string label, VmSustainedFeedbackProgress update) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{label}: {(update.TerminalFault ? "terminal policy fault" : "running, no verdict")} " +
            $"{update.ElapsedMs / 1000d:0.0}/480s; polls {update.Polls}, accepted {update.NewSamples}, " +
            $"repeats {update.Repeated}, temporary unavailable {update.Unavailable}, " +
            $"longest gap {update.LongestNoAdvanceMs} ms.");

    private static string SafeQualificationReason(string reason) => reason switch
    {
        "passed" or "cancelled" or "incomplete_window" or "insufficient_progress" or
        "no_advance_limit_exceeded" or "terminal_source_unavailable" or
        "source_identity_changed" or "source_session_changed" or "source_ordering_conflict" or
        "invalid_ordering_evidence" or "invalid_initial_clock" or "initial_lab_mismatch" or
        "reader_not_running" or "identity_mismatch" or "source_conflict" or "file_unavailable" or
        "invalid_source_pin" or "invalid_receipt_or_pin" or "invalid_sequence" or
        "invalid_source_timing" or "source_clock_or_player_invalid" or
        "source_expired" or "stale_or_clock_uncertain" or "source_not_fresh" or
        "scan_not_complete" or "schema_invalid" or "size_limit" or
        "player_unavailable" or "coordinates_unavailable" or "source_unavailable" or
        "read_error" or "clock_regression" or "poll_limit_exceeded" => reason,
        _ => "unverified_outcome"
    };

    // A fake asynchronous qualifier verifies lifecycle without a real 480-second wait,
    // VM controller, game process or source file.
    internal static async Task<bool> RunVmQualificationSmokeCheckAsync()
    {
        var pending = new TaskCompletionSource<VmSustainedFeedbackReport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new MainWindow(DemoReplay.Create());
        int starts = 0;
        string capturedLab = "", capturedPin = "";
        CancellationToken observedToken = default;
        IProgress<VmSustainedFeedbackProgress>? oldProgress = null;
        window._vmEvidenceOrigin = VmSustainedEvidenceExporter.SyntheticOrigin;
        window._vmQualificationRunner = (lab, pin, token, progress) =>
        {
            starts++;
            capturedLab = lab;
            capturedPin = pin;
            observedToken = token;
            oldProgress = progress;
            return pending.Task;
        };
        Task active = window.RunVmQualificationAsync();
        bool held = starts == 1 && capturedLab == "lab1" &&
            capturedPin.Contains("lab1", StringComparison.OrdinalIgnoreCase) &&
            !window.VmPulseButton.IsEnabled && !window.VmPreflightButton.IsEnabled &&
            !window.VmWatchStartButton.IsEnabled && !window.VmQualifyStartButton.IsEnabled &&
            window.VmQualifyStopButton.IsEnabled;

        window.VmLab1Pin.Text = @"C:\private\new-pin-after-start.json";
        window.VmQualifyLab.SelectedIndex = 1;
        await window.RunVmQualificationAsync(); // An overlapping Start must do nothing.
        held &= starts == 1 && capturedLab == "lab1" &&
            !capturedPin.Contains("new-pin-after-start", StringComparison.Ordinal);

        window.VmQualifyStopClicked(window, new RoutedEventArgs());
        held &= observedToken.IsCancellationRequested && !window.VmPulseButton.IsEnabled &&
            !window.VmQualifyStartButton.IsEnabled && !window.VmQualifyStopButton.IsEnabled;
        pending.SetResult(FakeQualificationReport("lab1", true)); // Late success must lose to Stop.
        await active;
        oldProgress?.Report(new VmSustainedFeedbackProgress(
            "lab1", 480000, 480000, 961, 961, 0, 0, 500, false));
        await Task.Yield(); // Drain any queued Progress<T> notifications on this dispatcher.
        held &= window.VmQualifySummary.Text.Contains("Cancelled/incomplete", StringComparison.Ordinal) &&
            !window.VmQualifySummary.Text.Contains("PASSED", StringComparison.Ordinal) &&
            window.VmQualifyStartButton.IsEnabled && window.VmPulseButton.IsEnabled;

        window.VmLab2Pin.Text = @"C:\private\lab2-unique-secret.json";
        window._vmQualificationRunner = (lab, pin, _, _) =>
        {
            capturedLab = lab;
            capturedPin = pin;
            return Task.FromResult(FakeQualificationReport(lab, true));
        };
        await window.RunVmQualificationAsync();
        held &= capturedLab == "lab2" &&
            capturedPin.EndsWith("lab2-unique-secret.json", StringComparison.Ordinal) &&
            window.VmQualifySummary.Text.Contains("PASSED", StringComparison.Ordinal) &&
            !window.VmQualifySummary.Text.Contains("lab2-unique-secret", StringComparison.Ordinal) &&
            window.VmQualifyStartButton.IsEnabled;

        string? savedPath = null;
        byte[]? savedJson = null;
        window._vmEvidencePathPicker = () => @"C:\fake\synthetic-telemetry.json";
        window._vmEvidenceWriter = (path, bytes, _) =>
        {
            savedPath = path;
            savedJson = bytes;
            return Task.CompletedTask;
        };
        await window.SaveVmQualificationAsync();
        string savedText = savedJson is null
            ? string.Empty : System.Text.Encoding.UTF8.GetString(savedJson);
        held &= savedPath == @"C:\fake\synthetic-telemetry.json" &&
            savedText.Contains("\"HistoricalOnly\": true", StringComparison.Ordinal) &&
            savedText.Contains("\"CurrentActionPermit\": false", StringComparison.Ordinal) &&
            savedText.Contains("\"EvidenceOrigin\": \"synthetic-fixture\"", StringComparison.Ordinal) &&
            !savedText.Contains("lab2-unique-secret", StringComparison.Ordinal) &&
            window.VmQualifySaveStatus.Text.Contains("Saved", StringComparison.Ordinal);

        var closePending = new TaskCompletionSource<VmSustainedFeedbackReport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new MainWindow(DemoReplay.Create());
        CancellationToken closeToken = default;
        closing._vmEvidenceOrigin = VmSustainedEvidenceExporter.SyntheticOrigin;
        closing._vmQualificationRunner = (_, _, token, _) =>
        {
            closeToken = token;
            return closePending.Task;
        };
        Task closeTask = closing.RunVmQualificationAsync();
        string beforeClose = closing.VmQualifySummary.Text;
        closing.CancelClientRefresh(); // Same close latch used by OnClosed.
        closePending.SetResult(FakeQualificationReport("lab1", true));
        await closeTask;
        held &= closeToken.IsCancellationRequested &&
            closing.VmQualifySummary.Text == beforeClose &&
            !closing.VmPulseButton.IsEnabled && !closing.VmQualifyStartButton.IsEnabled;

        window.Close();
        closing.Close();
        return held;
    }

    private static VmSustainedFeedbackReport FakeQualificationReport(string lab, bool passed) =>
        new("ReadOnlySustainedTelemetryEvidence",
            VmSustainedFeedbackWindow.PolicyId, lab, passed, passed,
            passed ? "passed" : "reader_not_running",
            480000, passed ? 480000 : 0, 500, 4500,
            passed ? 961 : 1, passed ? 961 : 0, 0, 0, 0, 0, 0,
            passed ? 500 : 0, 0);

    private void VmQualifyStopClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || _vmOperation != VmOperation.SustainedQualification) return;
        CancelVmOperation();
        UpdateVmOperationControls();
        VmQualifyProgress.Text = "Stop requested; waiting for the active read to settle. No input sent.";
        VmQualifySummary.Text = "Cancellation requested. Evidence remains incomplete.";
    }
}
