using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private async void VmRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || !VmRefreshButton.IsEnabled) return;
        VmRefreshButton.IsEnabled = false;
        VmLab1Status.Text = "Lab 1: checking authenticated controller...";
        VmLab2Status.Text = "Lab 2: checking authenticated controller...";
        try
        {
            var one = GetVmStatusAsync(VmLab1Config.Text);
            var two = GetVmStatusAsync(VmLab2Config.Text);
            await Task.WhenAll(one, two);
            if (_closing) return;
            VmLab1Status.Text = "Lab 1: " + FormatVmStatus(await one);
            VmLab2Status.Text = "Lab 2: " + FormatVmStatus(await two);
        }
        finally
        {
            if (!_closing) VmRefreshButton.IsEnabled = true;
        }
    }

    private static async Task<VmControllerResult> GetVmStatusAsync(string configPath)
    {
        try
        {
            using var bridge = new VmControllerBridge(configPath);
            return await bridge.StatusAsync();
        }
        catch (Exception e) when (e is ArgumentException or IOException
            or UnauthorizedAccessException or InvalidOperationException or JsonException
            or NotSupportedException or System.Security.SecurityException)
        {
            return new(false, false, false, false, "config_unavailable");
        }
    }

    private static string FormatVmStatus(VmControllerResult result)
    {
        if (!result.HostAccepted) return "unavailable (" + result.ErrorCode + ")";
        if (!result.Connected) return "controller reachable; guest disconnected";
        return result.GuestResponded
            ? "controller connected; guest replied. Game feedback not qualified."
            : "controller connected; guest reply unavailable";
    }

    private async void VmTelemetryClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || !VmTelemetryButton.IsEnabled) return;
        VmTelemetryButton.IsEnabled = false;
        VmLab1Telemetry.Text = "Lab 1 telemetry: checking...";
        VmLab2Telemetry.Text = "Lab 2 telemetry: checking...";
        try
        {
            string lab1Pin = VmLab1Pin.Text;
            string lab2Pin = VmLab2Pin.Text;
            Task<VmTelemetrySnapshot> one = Task.Run(() => VmTelemetryReader.ReadPinned(lab1Pin));
            Task<VmTelemetrySnapshot> two = Task.Run(() => VmTelemetryReader.ReadPinned(lab2Pin));
            await Task.WhenAll(one, two);
            if (_closing) return;
            VmLab1Telemetry.Text = FormatVmTelemetry(one.Result);
            VmLab2Telemetry.Text = FormatVmTelemetry(two.Result);
        }
        finally
        {
            if (!_closing) VmTelemetryButton.IsEnabled = true;
        }
    }

    private static string FormatVmTelemetry(VmTelemetrySnapshot s)
    {
        if (!s.HasPosition)
            return s.Lab + " telemetry unavailable: " + s.Reason +
                ". Current position not displayed; reader may need a new reviewed source pin.";
        return string.Create(CultureInfo.InvariantCulture,
            $"{s.Lab}: X {s.X:0.###}  Y {s.Y:0.###}  Z {s.Z:0.###}, " +
            $"source {s.SourceScope}, outer {s.OuterSequence}, addon {s.AddonSequence}, " +
            $"age {s.ReceiptAgeMs:0} ms. Diagnostic only, not movement qualified.");
    }

    private async void VmBoundPreflightClicked(object sender, RoutedEventArgs e)
    {
        CancellationTokenSource? run = TryBeginVmOperation(VmOperation.BoundPreflight);
        if (run is null) return;
        // Immutable selection for the whole run; edits during GET affect only a later run.
        string lab1Pin = VmLab1Pin.Text;
        string lab2Pin = VmLab2Pin.Text;
        string lab1Config = VmLab1Config.Text;
        string lab2Config = VmLab2Config.Text;
        VmLab1Binding.Text = "Lab 1: checking source and controller (no input)...";
        VmLab2Binding.Text = "Lab 2: checking source and controller (no input)...";
        try
        {
            Task<VmBoundPreflightReport> one = Task.Run(
                () => VmBoundPreflight.CheckAsync("lab1", lab1Pin, lab1Config, run.Token),
                run.Token);
            Task<VmBoundPreflightReport> two = Task.Run(
                () => VmBoundPreflight.CheckAsync("lab2", lab2Pin, lab2Config, run.Token),
                run.Token);
            await Task.WhenAll(one, two);
            if (_closing || run.IsCancellationRequested) return;
            VmLab1Binding.Text = FormatVmBinding(one.Result);
            VmLab2Binding.Text = FormatVmBinding(two.Result);
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
            {
                VmLab1Binding.Text = "Lab 1: check cancelled; no input.";
                VmLab2Binding.Text = "Lab 2: check cancelled; no input.";
            }
        }
        catch (Exception)
        {
            if (!_closing)
            {
                VmLab1Binding.Text = "Lab 1: diagnostic interrupted; no input.";
                VmLab2Binding.Text = "Lab 2: diagnostic interrupted; no input.";
            }
        }
        finally
        {
            EndVmOperation(VmOperation.BoundPreflight, run);
        }
    }

    private static string FormatVmBinding(VmBoundPreflightReport report)
    {
        string heading = report.Lab == "lab1" ? "Lab 1" : "Lab 2";
        if (report.BindingAndReadinessPassed)
            return heading + ": source and controller matched; sampled pulse readiness passed. " +
                "Not a lasting permit. No input sent.";
        if (report.IdentityMatched)
            return heading + ": game/controller identity matched, but pulse readiness is blocked (" +
                report.Reason + "). No input sent.";
        return heading + ": not ready (" + report.Reason + "), source " +
            report.SourceStatus + ". No input sent.";
    }

    private async void VmPulseClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        string lab = (VmActionLab.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        string key = (VmActionKey.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        string configPath = lab == "lab1" ? VmLab1Config.Text : VmLab2Config.Text;
        string sourcePin = lab == "lab1" ? VmLab1Pin.Text : VmLab2Pin.Text;
        string leaseFile = VmLeaseFile.Text;
        string armFile = VmArmFile.Text;
        if (lab is not ("lab1" or "lab2") || string.IsNullOrWhiteSpace(key) ||
            !int.TryParse(VmActionHold.Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out int hold) || hold is < 20 or > 250 ||
            string.IsNullOrWhiteSpace(leaseFile) || string.IsNullOrWhiteSpace(armFile))
        {
            VmActionResult.Text = "Not sent: select a lab, key, 20–250 ms hold and both existing authorization files.";
            return;
        }

        // Own the window-local operation before modal confirmation or any source reads.
        CancellationTokenSource? pulseRun = TryBeginVmOperation(VmOperation.SupervisedPulse);
        if (pulseRun is null) return;
        bool sendAttempted = false;
        bool acknowledged = false;
        try
        {
            VmPulseSourceReview review = VmPulseObservationChecks.CheckAcrossConfirmation(
                lab,
                () => VmTelemetryReader.ReadPinned(sourcePin),
                () => MessageBox.Show(this,
                    $"Send one {key.ToUpperInvariant()} key pulse for {hold} ms to {lab}?\n" +
                    "This can affect the running game. The controller must validate the existing lease and arm token.",
                    "Confirm supervised VM input", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    == MessageBoxResult.Yes,
                () => _closing);
            if (_closing || review.Disposition == VmPulseReviewDisposition.Cancelled) return;
            if (review.Disposition == VmPulseReviewDisposition.PrecheckFailed)
            {
                VmActionResult.Text = "Not sent: fresh pinned position for the selected lab and " +
                    "complete source identity are required (" + review.Initial.Reason + ").";
                return;
            }
            if (review.Disposition != VmPulseReviewDisposition.SourceRechecked ||
                review.BeforeSend is not VmTelemetrySnapshot before)
            {
                VmActionResult.Text = "Not sent: position source changed, expired or regressed " +
                    "while awaiting confirmation. Review a fresh source before retrying.";
                return;
            }

            VmActionResult.Text = "Checking controller, lab and current game identity... No input requested yet.";
            using var bridge = new VmControllerBridge(configPath);
            VmControllerResult binding = await bridge.VerifyPulseTargetAsync(lab, before, pulseRun.Token);
            if (_closing) return;
            if (!binding.HostAccepted)
            {
                VmActionResult.Text = "Not sent: controller/lab/game binding not verified (" +
                    binding.ErrorCode + "). Check coordinated host identity and renewed source pin.";
                return;
            }
            // Status is a bounded network round-trip. A second post-status
            // source read is mandatory immediately before the only POST.
            VmTelemetrySnapshot finalSource = VmTelemetryReader.ReadPinned(sourcePin);
            if (!VmControllerBindingChecks.HasStableSourceBinding(lab, before, finalSource) ||
                !VmPulseObservationChecks.CanRevalidate(lab, before, finalSource))
            {
                VmActionResult.Text = "Not sent: pinned game source changed or expired during " +
                    "the controller identity check. Reconfirm a fresh source.";
                return;
            }
            before = finalSource;
            if (_closing) return;
            pulseRun.Token.ThrowIfCancellationRequested();
            VmActionResult.Text = "Requesting supervised input... Do not assume it was delivered.";
            // Set before invocation: even a synchronous transport failure may be ambiguous.
            sendAttempted = true;
            VmControllerResult result = await bridge.PulseAsync(leaseFile, armFile, key, hold, pulseRun.Token);
            if (_closing) return;
            if (!result.HostAccepted)
            {
                VmActionResult.Text = "Controller did not confirm clean input delivery and release (" +
                    result.ErrorCode + "). Outcome may be ambiguous; no automatic retry.";
                return;
            }

            acknowledged = true;
            VmActionResult.Text = "Controller reports input delivery and release. Checking matching position feedback...";
            // Retain accepted receiver high-water marks, including repeated
            // producer frames, so a later out-of-order envelope is not feedback.
            var tracker = new VmTelemetryWatchTracker(lab);
            VmTelemetryWatchReport feedback = tracker.Observe(before, 0);
            long feedbackStart = Stopwatch.GetTimestamp();
            for (int attempt = 0; attempt < 6 && !_closing; attempt++)
            {
                await Task.Delay(400, pulseRun.Token);
                if (_closing) return;
                VmTelemetrySnapshot after = VmTelemetryReader.ReadPinned(sourcePin);
                long elapsedMs = (long)Stopwatch.GetElapsedTime(feedbackStart).TotalMilliseconds;
                feedback = tracker.Observe(after, elapsedMs);
                if (VmPulseObservationChecks.IsAcceptedFeedback(lab, before, feedback)) break;
            }
            if (_closing) return;
            if (VmPulseObservationChecks.IsAcceptedFeedback(lab, before, feedback))
            {
                VmTelemetrySnapshot after = feedback.Current!;
                double dx = after.X!.Value - before.X!.Value;
                double dy = after.Y!.Value - before.Y!.Value;
                double dz = after.Z!.Value - before.Z!.Value;
                if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dz))
                {
                    VmActionResult.Text = "Newer matching position received, but the coordinate delta " +
                        "is outside the numeric display range. Outcome unknown; no automatic retry.";
                    return;
                }
                VmActionResult.Text = string.Create(CultureInfo.InvariantCulture,
                    $"Newer matching position received after the request. Delta X {dx:0.###}, " +
                    $"Y {dy:0.###}, Z {dz:0.###}. Input causality and movement success not independently confirmed.");
            }
            else
            {
                VmActionResult.Text = "Controller reported input delivery and release, but no newer matching " +
                    "position arrived during six follow-up checks. Outcome unknown; no automatic retry.";
            }
        }
        catch (OperationCanceledException) when (pulseRun.IsCancellationRequested)
        {
            if (!_closing)
                VmActionResult.Text = !sendAttempted
                    ? "Cancelled before input request; no pulse sent."
                    : acknowledged
                        ? "Cancelled after controller acknowledgment; game response remains unconfirmed."
                        : "Cancelled during or after input request. Delivery is uncertain; no retry.";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
            or InvalidOperationException or JsonException or NotSupportedException
            or System.Security.SecurityException)
        {
            if (!_closing)
                VmActionResult.Text = !sendAttempted
                    ? "Not sent: configuration, authorization or preflight inspection failed."
                    : acknowledged
                        ? "Controller reported delivery and release, but position feedback was interrupted. " +
                          "Outcome unknown; no automatic retry."
                        : "Controller request was attempted, but its outcome cannot be confirmed. " +
                          "Input may have been delivered; no automatic retry.";
        }
        finally
        {
            EndVmOperation(VmOperation.SupervisedPulse, pulseRun);
        }
    }

    internal bool RunVmSmokeCheck()
    {
        ObserverTabs.SelectedItem = VmTab;
        return VmRefreshButton.IsEnabled && VmPulseButton.IsEnabled
            && VmPreflightButton.IsEnabled &&
            VmLab1Binding.Text == "Lab 1 binding: not checked" &&
            VmLab2Binding.Text == "Lab 2 binding: not checked" &&
            VmTelemetryButton.IsEnabled
            && VmWatchStartButton.IsEnabled && !VmWatchStopButton.IsEnabled
            && VmWatchDuration.SelectedIndex == 1
            && VmLab1Pin.Text.Contains("lab1", StringComparison.Ordinal)
            && VmLab2Pin.Text.Contains("lab2", StringComparison.Ordinal)
            && VmLab1Config.Text.Contains("lab1", StringComparison.Ordinal)
            && VmLab2Config.Text.Contains("lab2", StringComparison.Ordinal)
            && VmActionResult.Text == "No input requested."
            && VmQualifyLab.SelectedIndex == 0
            && !VmQualifyStopButton.IsEnabled
            && VmQualifyStartButton.IsEnabled
            && VmOperationSmokeCheck();
    }
}
