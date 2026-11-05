using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private StandaloneTelemetryHostSession? _ownedTelemetrySession;
    private CancellationTokenSource? _ownedTelemetryCancellation;
    private Task? _ownedTelemetryRun;
    private VmTelemetryWatchTracker? _ownedTelemetryTracker;
    private Stopwatch? _ownedTelemetryClock;
    private DispatcherTimer? _ownedTelemetryTimer;
    private bool _ownedTelemetryStopping;

    private void OwnedTelemetryStartClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || _ownedTelemetryStopping || _ownedTelemetrySession is not null)
            return;

        string path = OwnedTelemetryConfigPath.Text.Trim();
        if (path.Length == 0)
        {
            OwnedTelemetryStatusText.Text = "Choose an explicitly reviewed private receiver configuration first.";
            return;
        }

        StandaloneTelemetryHostSession? session = null;
        CancellationTokenSource? cancellation = null;
        try
        {
            // Load only after an explicit click. Never display/log the config, token,
            // path, or raw exception text.
            StandaloneTelemetryHostConfiguration config =
                StandaloneTelemetryHostConfiguration.Load(path);
            session = new StandaloneTelemetryHostSession(
                config.Binding, config.BindIpv4, config.Port, config.DeliveryToken);
            cancellation = new CancellationTokenSource();
            Task run = session.RunAsync(cancellation.Token);

            _ownedTelemetrySession = session;
            _ownedTelemetryCancellation = cancellation;
            _ownedTelemetryRun = run;
            _ownedTelemetryTracker = new VmTelemetryWatchTracker(config.Binding.Lab);
            _ownedTelemetryClock = Stopwatch.StartNew();
            _ownedTelemetryTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                OwnedTelemetryTick, Dispatcher);
            _ownedTelemetryTimer.Start();

            OwnedTelemetryConfigPath.IsEnabled = false;
            OwnedTelemetryStartButton.IsEnabled = false;
            OwnedTelemetryStopButton.IsEnabled = true;
            OwnedTelemetryStatusText.Text =
                $"Receiver starting for {config.Binding.Lab}; waiting for a verified guest observation.";
            OwnedTelemetryPositionText.Text = "XYZ: unavailable";
            OwnedTelemetryAgeText.Text = "Receipt age: unavailable";
            OwnedTelemetrySequenceText.Text = "Sequence: unavailable | Input authority: none";

            // This observer also handles a listener that faults before the first tick.
            _ = ObserveOwnedTelemetryRunAsync(session, run);
        }
        catch (Exception)
        {
            cancellation?.Dispose();
            session?.Dispose();
            // No exception messages: configuration and HTTP errors can include private data.
            OwnedTelemetryStatusText.Text =
                "Receiver could not start. Check the private configuration, bind address, and port.";
            OwnedTelemetryPositionText.Text = "XYZ: unavailable";
            OwnedTelemetryAgeText.Text = "Receipt age: unavailable";
            OwnedTelemetrySequenceText.Text = "Sequence: unavailable | Input authority: none";
        }
    }

    private async Task ObserveOwnedTelemetryRunAsync(
        StandaloneTelemetryHostSession session, Task run)
    {
        bool failed = false;
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
            failed = true; // Unexpected unless the user already requested Stop.
        }
        catch (Exception)
        {
            failed = true;
        }

        if (_ownedTelemetrySession != session || _ownedTelemetryStopping || _closing)
            return;

        await StopOwnedTelemetryAsync(
            closing: false,
            message: failed
                ? "Receiver stopped: listener failed or its bind/port became unavailable."
                : "Receiver stopped unexpectedly. Start a new session to reconnect.");
    }

    private void OwnedTelemetryTick(object? sender, EventArgs e)
    {
        if (_closing || _ownedTelemetryStopping || _ownedTelemetrySession is not { } session)
            return;

        try
        {
            VmTelemetrySnapshot snapshot = session.ReadLatest();
            VmTelemetryWatchReport progress = _ownedTelemetryTracker!.Observe(
                snapshot, _ownedTelemetryClock!.ElapsedMilliseconds);
            OwnedTelemetryStatusText.Text =
                $"{snapshot.Lab}: {snapshot.Status} | {snapshot.Reason} | " +
                $"Producer progress: {progress.State} ({progress.NewSamples} advances; " +
                $"{progress.Repeated} repeats) — short diagnostic, not qualified";
            OwnedTelemetryPositionText.Text = FormatOwnedTelemetryPosition(snapshot);
            OwnedTelemetryAgeText.Text = snapshot.ReceiptAgeMs is { } age
                ? $"Receipt age: {age.ToString("F0", CultureInfo.InvariantCulture)} ms"
                : "Receipt age: unavailable";
            OwnedTelemetrySequenceText.Text =
                $"Outer sequence: {snapshot.OuterSequence?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
                $"Addon sequence: {snapshot.AddonSequence?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
                $"Epoch: {snapshot.AddonEpoch?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
                $"Source: {snapshot.SourceScope} | Input authority: none";
        }
        catch (Exception)
        {
            _ = StopOwnedTelemetryAsync(
                closing: false, message: "Receiver stopped: telemetry inspection failed.");
        }
    }

    private static string FormatOwnedTelemetryPosition(VmTelemetrySnapshot snapshot)
    {
        if (!snapshot.HasPosition)
            return "XYZ: unavailable";

        return $"X: {snapshot.X!.Value.ToString("F3", CultureInfo.InvariantCulture)}  " +
               $"Y: {snapshot.Y!.Value.ToString("F3", CultureInfo.InvariantCulture)}  " +
               $"Z: {snapshot.Z!.Value.ToString("F3", CultureInfo.InvariantCulture)}";
    }

    // Deterministic fixture: verifies generated XAML controls and visibility rules
    // without binding ports, reading private config, or contacting a VM.
    internal bool RunOwnedTelemetryUiSmokeCheck()
    {
        var fresh = new VmTelemetrySnapshot(
            "lab1", "fresh_observed", "fixture", DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, 5, 10, 7, 2, 123, 1.25, 2.5, 3.75);
        var stale = fresh with { Status = "stale", ReceiptAgeMs = 2500 };
        var incomplete = fresh with { Z = null };
        return OwnedTelemetryConfigPath is not null && string.IsNullOrWhiteSpace(OwnedTelemetryConfigPath.Text)
            && OwnedTelemetryStartButton is { IsEnabled: true }
            && OwnedTelemetryStopButton is { IsEnabled: false }
            && OwnedTelemetryPositionText?.Text == "XYZ: unavailable"
            && _ownedTelemetrySession is null && _ownedTelemetryRun is null
            && FormatOwnedTelemetryPosition(fresh) == "X: 1.250  Y: 2.500  Z: 3.750"
            && FormatOwnedTelemetryPosition(stale) == "XYZ: unavailable"
            && FormatOwnedTelemetryPosition(incomplete) == "XYZ: unavailable"
            && !fresh.CanAutomate;
    }

    private async void OwnedTelemetryStopClicked(object sender, RoutedEventArgs e)
    {
        await StopOwnedTelemetryAsync(
            closing: false, message: "Receiver stopped by user. No gameplay input was sent.");
    }

    private async void OwnedTelemetryWindowClosed(object? sender, EventArgs e)
    {
        await StopOwnedTelemetryAsync(closing: true);
    }

    private async Task StopOwnedTelemetryAsync(bool closing, string? message = null)
    {
        if (_ownedTelemetryStopping)
            return;
        _ownedTelemetryStopping = true;

        _ownedTelemetryTimer?.Stop();
        if (_ownedTelemetryTimer is not null)
            _ownedTelemetryTimer.Tick -= OwnedTelemetryTick;
        _ownedTelemetryTimer = null;

        StandaloneTelemetryHostSession? session = _ownedTelemetrySession;
        CancellationTokenSource? cancellation = _ownedTelemetryCancellation;
        Task? run = _ownedTelemetryRun;
        _ownedTelemetrySession = null;
        _ownedTelemetryCancellation = null;
        _ownedTelemetryRun = null;
        _ownedTelemetryTracker = null;
        _ownedTelemetryClock = null;

        if (!closing && !_closing)
        {
            OwnedTelemetryStatusText.Text = "Stopping receiver...";
            OwnedTelemetryStopButton.IsEnabled = false;
        }

        try
        {
            cancellation?.Cancel();
            // Release the listening socket synchronously for every Stop.
            // A window can close while this await is pending, before the
            // dispatcher can resume cleanup; Dispose is idempotent.
            session?.Dispose();
            if (run is not null)
                await run;
        }
        catch (OperationCanceledException)
        {
            // Expected after a supervised Stop or window close.
        }
        catch (Exception)
        {
            // Fault already classified by the observer; do not expose private data.
        }
        finally
        {
            session?.Dispose();
            cancellation?.Dispose();
            _ownedTelemetryStopping = false;
            if (!closing && !_closing)
            {
                OwnedTelemetryConfigPath.IsEnabled = true;
                OwnedTelemetryStartButton.IsEnabled = true;
                OwnedTelemetryStopButton.IsEnabled = false;
                OwnedTelemetryStatusText.Text = message ?? "Receiver stopped.";
                OwnedTelemetryPositionText.Text = "XYZ: unavailable";
                OwnedTelemetryAgeText.Text = "Receipt age: unavailable";
                OwnedTelemetrySequenceText.Text = "Sequence: unavailable | Input authority: none";
            }
        }
    }
}
