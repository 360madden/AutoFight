using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Rift.Automation.Windows;
using System.Windows;
using Rift.Automation.Core;

namespace Rift.Automation.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--help"])
        {
            Console.WriteLine("AutoFight 0.5.7 - standalone VMware diagnostics and supervised legacy tools");
            Console.WriteLine("Usage: Rift.Automation.Desktop [--owned-telemetry <private-config.local.json> <seconds> | --vm-feedback <source-pin> | --vm-watch <source-pin> <seconds> | --vm-qualify-feedback <lab> <source-pin> | --vm-status <config> | --vm-preflight <lab> <source-pin> <config> | --vm-pulse <lab> <source-pin> <config> <lease> <arm> <key> <hold-ms> | --clients | --inspect-unit <file> | --replay <file> | --smoke-test | --help]");
            Console.WriteLine("VM controls use an existing supervised controller on localhost; no automatic arming.");
            Console.WriteLine("--vm-preflight checks current selected-lab identity/readiness; no input or authorization artifacts.");
            Console.WriteLine("--vm-qualify-feedback checks 480s pinned telemetry with every progress gap strictly below 4500ms; no input or controller contact.");
            Console.WriteLine("--owned-telemetry listens for AutoFight GuestReader telemetry with exact private identity binding; no input.");
            Console.WriteLine("VM feedback reads legacy RR2 latest.json through a pinned reader identity, without touching input.");
            Console.WriteLine("--clients lists local RIFT process metadata as JSON without opening a window.");
            Console.WriteLine("--inspect-unit reads one manual RAC-TSV/1 snapshot file, not live telemetry.");
            Console.WriteLine("No native game-memory sampler, independent guest controller or addon deployment.");
            Shutdown(0);
            return;
        }

        if (e.Args is ["--owned-telemetry", string ownedConfig, string ownedSeconds])
        {
            Shutdown(await RunOwnedTelemetryAsync(ownedConfig, ownedSeconds));
            return;
        }

        if (e.Args is ["--clients"])
        {
            ClientInventorySnapshot clients = RiftClientInventory.Scan();
            Console.WriteLine(JsonSerializer.Serialize(clients));
            Shutdown(clients.ErrorCode is null ? 0 : 2);
            return;
        }

        if (e.Args is ["--inspect-unit", string unitPath])
        {
            ManualUnitReadResult result = ManualUnitSnapshotReader.OpenFile(unitPath);
            if (!result.Success || result.Snapshot is null)
            {
                Console.Error.WriteLine("Manual snapshot rejected: " + result.ErrorCode);
                Shutdown(2);
                return;
            }
            Console.WriteLine(JsonSerializer.Serialize(result.Snapshot));
            Shutdown(0);
            return;
        }

        if (e.Args is ["--vm-feedback", string feedbackPin])
        {
            VmTelemetrySnapshot result = VmTelemetryReader.ReadPinned(feedbackPin);
            Console.WriteLine(JsonSerializer.Serialize(result));
            Shutdown(result.HasPosition ? 0 : 2);
            return;
        }
        if (e.Args is ["--vm-watch", string monitorPin, string durationText])
        {
            await WatchVmAsync(monitorPin, durationText);
            return;
        }
        if (e.Args is ["--vm-qualify-feedback", string evidenceLab, string evidencePin])
        {
            await RunVmSustainedAsync(evidenceLab, evidencePin);
            return;
        }
        if (e.Args is ["--vm-status", string vmConfig])
        {
            await RunVmStatusAsync(vmConfig);
            return;
        }
        if (e.Args is ["--vm-preflight", string requestedLab, string checkedPin, string checkedConfig])
        {
            await RunVmPreflightAsync(requestedLab, checkedPin, checkedConfig);
            return;
        }
        if (e.Args is ["--vm-pulse", string lab, string pin, string actionConfig,
            string lease, string arm, string key, string holdText])
        {
            await RunVmBoundPulseAsync(lab, pin, actionConfig, lease, arm, key, holdText);
            return;
        }

        bool smoke = e.Args is ["--smoke-test"];
        if (e.Args.Length != 0 && !smoke && e.Args is not ["--replay", _])
        {
            Console.Error.WriteLine("Invalid arguments. Use --help.");
            Shutdown(2);
            return;
        }

        ReplayScenario scenario = DemoReplay.Create();
        if (e.Args is ["--replay", string path])
        {
            ReplayReadResult result = ReadReplay(path);
            if (!result.Success || result.Scenario is null)
            {
                Console.Error.WriteLine($"Replay rejected: {result.ErrorCode}");
                Shutdown(2);
                return;
            }
            scenario = result.Scenario;
        }

        var window = new MainWindow(scenario);
        MainWindow = window;
        if (smoke)
        {
            try
            {
                bool passed = window.RunSmokeCheck();
                Console.WriteLine(passed ? "PASS: WPF replay construction and controls" : "FAIL: WPF replay smoke");
                bool clientsPassed = await Rift.Automation.Desktop.MainWindow.RunClientSmokeCheckAsync();
                Console.WriteLine(clientsPassed ? "PASS: client selection, errors, refresh overlap and close cancellation" : "FAIL: client controls");
                passed &= clientsPassed;
                bool unitPassed = window.RunUnitSmokeCheck();
                Console.WriteLine(unitPassed ? "PASS: manual snapshot display and failure clearing" : "FAIL: manual snapshot view");
                passed &= unitPassed;
                bool vmPassed = window.RunVmSmokeCheck();
                Console.WriteLine(vmPassed ? "PASS: VMware controller controls" : "FAIL: VMware controller controls");
                passed &= vmPassed;
                bool qualificationUiPassed = await Rift.Automation.Desktop.MainWindow.RunVmQualificationSmokeCheckAsync();
                Console.WriteLine(qualificationUiPassed
                    ? "PASS: VM sustained qualification UI, operation isolation, stop and close"
                    : "FAIL: VM sustained qualification UI");
                passed &= qualificationUiPassed;
                Shutdown(passed ? 0 : 1);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                Console.Error.WriteLine("FAIL: WPF replay initialization");
                Shutdown(1);
            }
            return;
        }

        if (e.Args is ["--replay", _]) window.ShowReplay();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private static async Task<int> RunOwnedTelemetryAsync(string configPath, string durationText)
    {
        if (!int.TryParse(durationText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int seconds) ||
            seconds is < 1 or > 120)
        {
            Console.Error.WriteLine("Owned telemetry: duration must be 1 through 120 seconds; no listener started.");
            return 2;
        }

        try
        {
            // Neither private configuration nor token/path leave this process.
            StandaloneTelemetryHostConfiguration config =
                StandaloneTelemetryHostConfiguration.Load(configPath);
            using var session = new StandaloneTelemetryHostSession(
                config.Binding, config.BindIpv4, config.Port, config.DeliveryToken);
            using var stop = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                Task listenerTask = session.RunAsync(stop.Token);
                var elapsed = Stopwatch.StartNew();
                var tracker = new VmTelemetryWatchTracker(config.Binding.Lab);
                VmTelemetryWatchReport? report = null;
                string? lastPrinted = null;
                bool interrupted = false;
                try
                {
                    while (elapsed.ElapsedMilliseconds < seconds * 1000L)
                    {
                        if (listenerTask.IsCompleted)
                        {
                            // An early receiver exit (including bind failure)
                            // is never evidence of active observation.
                            await listenerTask;
                            Console.Error.WriteLine("Owned telemetry: listener_stopped_early");
                            return 2;
                        }

                        VmTelemetrySnapshot sample = session.ReadLatest();
                        report = tracker.Observe(sample, elapsed.ElapsedMilliseconds);
                        string state = string.Join(":", sample.Status, sample.Reason,
                            sample.OuterSequence, sample.AddonSequence, report.State);
                        if (state != lastPrinted)
                        {
                            Console.WriteLine(JsonSerializer.Serialize(new
                            {
                                mode = "AutoFightOwnedTelemetry",
                                sample,
                                watchState = report.State,
                                watchReason = report.Reason,
                                canAutomate = false
                            }));
                            lastPrinted = state;
                        }

                        long remaining = seconds * 1000L - elapsed.ElapsedMilliseconds;
                        if (remaining <= 0) break;
                        try
                        {
                            await Task.Delay((int)Math.Min(500L, remaining), stop.Token);
                        }
                        catch (OperationCanceledException) when (stop.IsCancellationRequested)
                        {
                            interrupted = true;
                            break;
                        }
                    }

                    // Check the listener at the terminal boundary, not only
                    // at the beginning of a polling iteration.
                    if (listenerTask.IsCompleted)
                    {
                        await listenerTask;
                        Console.Error.WriteLine("Owned telemetry: listener_stopped_early");
                        return 2;
                    }

                    // A short advancing XYZ watch never grants action authority.
                    interrupted |= stop.IsCancellationRequested;
                    bool advanced = !interrupted &&
                        report is { NewSamples: >= 2, HasCurrentPosition: true,
                            SessionChanges: 0, Reordered: 0 };
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        mode = "AutoFightOwnedTelemetryWatch",
                        report,
                        advancingFreshTelemetry = advanced,
                        sustainedQualification = false,
                        canAutomate = false,
                        interrupted
                    }));
                    return advanced ? 0 : 2;
                }
                finally
                {
                    stop.Cancel();
                    try { await listenerTask; }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or JsonException
            or NotSupportedException or System.Net.Sockets.SocketException
            or System.Security.SecurityException or OverflowException)
        {
            Console.Error.WriteLine("Owned telemetry: invalid_config_or_listener_failure; no input attempted");
            return 2;
        }
    }

    private async Task WatchVmAsync(string sourcePin, string durationText)
    {
        if (!int.TryParse(durationText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int seconds) ||
            seconds is < 1 or > 15)
        {
            Console.Error.WriteLine("VM watch duration must be 1 through 15 seconds.");
            Shutdown(2);
            return;
        }

        var timer = Stopwatch.StartNew();
        VmTelemetryWatchTracker? monitor = null;
        VmTelemetryWatchReport? final = null;
        while (true)
        {
            VmTelemetrySnapshot observation = VmTelemetryReader.ReadPinned(sourcePin);
            if (monitor is null)
            {
                if (observation.Lab is not ("lab1" or "lab2"))
                {
                    Console.Error.WriteLine("VM watch source pin has no valid lab.");
                    Shutdown(2);
                    return;
                }
                monitor = new VmTelemetryWatchTracker(observation.Lab);
            }
            final = monitor.Observe(observation, timer.ElapsedMilliseconds);
            // Use one monotonic sample: a second read can cross the deadline
            // and produce Task.Delay(-1), which means an infinite wait.
            long remainingMs = seconds * 1000L - timer.ElapsedMilliseconds;
            if (remainingMs <= 0) break;
            await Task.Delay((int)Math.Min(500L, remainingMs));
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            mode = "VmPinnedTelemetryWatch",
            report = final,
            hasSustainedQualification = false, canAutomate = false
        }));
        Shutdown(final is { NewSamples: > 1, HasCurrentPosition: true,
            SessionChanges: 0, Reordered: 0, Unavailable: 0 }
            ? 0 : 2);
    }

    private async Task RunVmStatusAsync(string config)
    {
        try
        {
            using var link = new VmControllerBridge(config);
            VmControllerResult result = await link.StatusAsync();
            if (result.HostAccepted)
            {
                Console.WriteLine(JsonSerializer.Serialize(result));
                Shutdown(0);
            }
            else
            {
                Console.Error.WriteLine("VM controller: " + (result.ErrorCode ?? "unknown_failure"));
                Shutdown(2);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or JsonException
            or NotSupportedException or System.Security.SecurityException)
        {
            Console.Error.WriteLine("VM controller: invalid_configuration_or_authorization");
            Shutdown(2);
        }
    }

    private async Task RunVmSustainedAsync(string lab, string pin)
    {
        try
        {
            VmSustainedFeedbackReport report = await VmSustainedFeedbackRunner.RunAsync(lab, pin);
            // Only fixed aggregate evidence leaves the process; never echo a private source pin.
            Console.WriteLine(JsonSerializer.Serialize(report));
            Shutdown(report.SustainedTelemetryEvidencePassed ? 0 : 2);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or
            UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            Console.Error.WriteLine("VM sustained telemetry: invalid_source_pin; no input attempted");
            Shutdown(2);
        }
    }

    private async Task RunVmPreflightAsync(string lab, string pin, string config)
    {
        VmBoundPreflightReport report = await VmBoundPreflight.CheckAsync(lab, pin, config);
        // Public JSON is fixed and redacted: never echo paths, bearer, PID or host response bodies.
        Console.WriteLine(JsonSerializer.Serialize(report));
        Shutdown(report.BindingAndReadinessPassed ? 0 : 2);
    }

    private async Task RunVmBoundPulseAsync(
        string lab, string pin, string config, string lease, string arm, string key, string holdText)
    {
        bool sendAttempted = false;
        try
        {
            if (VmControllerBindingChecks.ExpectedResource(lab) is null ||
                !int.TryParse(holdText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int hold) ||
                hold is < 20 or > 250)
                throw new ArgumentException("Invalid supervised pulse selection.");

            VmTelemetrySnapshot reviewed = VmTelemetryReader.ReadPinned(pin);
            if (!VmControllerBindingChecks.HasSourceBinding(lab, reviewed))
            {
                Console.Error.WriteLine("VM controller: source_binding_unavailable; no input sent");
                Shutdown(2);
                return;
            }
            using var link = new VmControllerBridge(config);
            VmControllerResult bound = await link.VerifyPulseTargetAsync(lab, reviewed);
            if (!bound.HostAccepted)
            {
                Console.Error.WriteLine("VM controller: " +
                    (bound.ErrorCode ?? "controller_binding_unavailable") + "; no input sent");
                Shutdown(2);
                return;
            }
            // The authenticated status GET can take five seconds: re-read the
            // reviewed pin immediately before the sole possible action POST.
            VmTelemetrySnapshot latest = VmTelemetryReader.ReadPinned(pin);
            if (!VmControllerBindingChecks.HasStableSourceBinding(lab, reviewed, latest) ||
                !VmPulseObservationChecks.CanRevalidate(lab, reviewed, latest))
            {
                Console.Error.WriteLine("VM controller: source_changed_after_status; no input sent");
                Shutdown(2);
                return;
            }
            sendAttempted = true;
            VmControllerResult action = await link.PulseAsync(lease, arm, key, hold);
            if (!action.HostAccepted)
            {
                Console.Error.WriteLine("VM controller: " +
                    (action.ErrorCode ?? "action_outcome_unconfirmed") + "; no automatic retry");
                Shutdown(2);
                return;
            }
            // A clean controller acknowledgment is not independent RIFT feedback.
            Console.WriteLine(JsonSerializer.Serialize(action));
            Shutdown(0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or JsonException
            or NotSupportedException or System.Security.SecurityException)
        {
            Console.Error.WriteLine(sendAttempted
                ? "VM controller: action_outcome_unknown; may have been delivered; no automatic retry"
                : "VM controller: invalid_configuration_or_authorization; no input sent");
            Shutdown(2);
        }
    }

    internal static ReplayReadResult ReadReplay(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReplayReader.Read(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return new ReplayReadResult(false, null, "file_unavailable");
        }
    }
}
