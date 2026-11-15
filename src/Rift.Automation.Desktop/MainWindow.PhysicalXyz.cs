using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

/// <summary>
/// Explicit, physical-host-only RIFT position observation. Every native acquisition
/// is exact-PID/start/build pinned and read-only; candidates are never movement authority.
/// </summary>
public partial class MainWindow
{
    private CancellationTokenSource? _physicalXyzRefreshCancellation;
    private CancellationTokenSource? _physicalXyzStartCancellation;
    private CancellationTokenSource? _physicalXyzWatchCancellation;
    private Task? _physicalXyzWatchTask;
    private DispatcherTimer? _physicalXyzAgeTimer;
    private PhysicalRiftPositionResult? _physicalXyzVisibleCandidate;
    private PhysicalRiftPositionChangeTracker? _physicalXyzComparison;
    private ulong? _physicalXyzPinnedPlayerKey;
    private bool _physicalXyzStartPending;
    private bool _physicalXyzClosing;
    private int _physicalXyzAttempts;
    private int _physicalXyzCandidates;
    private int _physicalXyzUnavailable;
    private int _physicalXyzIdentityFaults;
    private int _physicalXyzErrors;

    private void PhysicalXyzRefreshClicked(object sender, RoutedEventArgs e)
    {
        _ = RefreshPhysicalXyzClientsAsync();
    }

    private async Task RefreshPhysicalXyzClientsAsync()
    {
        if (_closing || _physicalXyzClosing || _physicalXyzStartPending
            || _physicalXyzRefreshCancellation is not null || _physicalXyzWatchCancellation is not null)
            return;

        using var cancellation = new CancellationTokenSource();
        _physicalXyzRefreshCancellation = cancellation;
        PhysicalXyzClientPicker.ItemsSource = null;
        PhysicalXyzPinnedText.Text = "No process selected. A new exact process identity is required.";
        PhysicalXyzInventorySummary.Text = "Checking physical RIFT processes on this PC...";
        PhysicalXyzRefreshButton.IsEnabled = false;
        PhysicalXyzStartButton.IsEnabled = false;

        try
        {
            // This is the existing on-demand process inventory. It does not read
            // game memory; on the real UI the injected default runs off the UI thread.
            ClientInventorySnapshot snapshot = await _scanClients(cancellation.Token);
            if (_closing || _physicalXyzClosing || cancellation.IsCancellationRequested)
                return;

            if (snapshot.ErrorCode is not null)
            {
                PhysicalXyzInventorySummary.Text = "Windows process inventory unavailable. Refresh to retry.";
                return;
            }

            PhysicalXyzClientPicker.ItemsSource = snapshot.Clients;
            PhysicalXyzInventorySummary.Text = snapshot.Clients.Count == 0
                ? "No physical RIFT process detected. VMware guest processes are intentionally excluded."
                : snapshot.Clients.Count.ToString(CultureInfo.InvariantCulture)
                    + " physical RIFT candidate(s). Select one explicitly; Start rechecks its identity.";
            PhysicalXyzInventorySummary.Text += " Checked "
                + snapshot.CheckedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                + (snapshot.Truncated ? "; inventory truncated." : ".");
        }
        catch (OperationCanceledException)
        {
            if (!_closing && !_physicalXyzClosing)
                PhysicalXyzInventorySummary.Text = "Physical RIFT process refresh cancelled.";
        }
        catch (Exception)
        {
            if (!_closing && !_physicalXyzClosing)
                PhysicalXyzInventorySummary.Text = "Physical RIFT process refresh failed. Retry explicitly.";
        }
        finally
        {
            if (ReferenceEquals(_physicalXyzRefreshCancellation, cancellation))
                _physicalXyzRefreshCancellation = null;
            if (!_closing && !_physicalXyzClosing)
            {
                PhysicalXyzRefreshButton.IsEnabled = true;
                UpdatePhysicalXyzStartAvailability();
            }
        }
    }

    private void PhysicalXyzClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PhysicalXyzPinnedText is null || PhysicalXyzStartButton is null)
            return; // WPF may raise this while constructing the XAML visual tree.

        if (PhysicalXyzClientPicker.SelectedItem is RiftClientInfo client
            && client.StartedAtUtc is { } started)
        {
            PhysicalXyzPinnedText.Text = "Selected PID "
                + client.ProcessId.ToString(CultureInfo.InvariantCulture)
                + " | Process start UTC ticks "
                + started.UtcTicks.ToString(CultureInfo.InvariantCulture)
                + " | " + client.Status
                + " | Build SHA will be checked by the native reader.";
        }
        else
            PhysicalXyzPinnedText.Text = "No process with a verifiable start time selected.";

        UpdatePhysicalXyzStartAvailability();
    }

    private void UpdatePhysicalXyzStartAvailability()
    {
        if (PhysicalXyzStartButton is null)
            return;

        bool idle = !_closing && !_physicalXyzClosing && !_physicalXyzStartPending
            && _physicalXyzRefreshCancellation is null && _physicalXyzWatchCancellation is null;
        PhysicalXyzStartButton.IsEnabled = idle
            && PhysicalXyzClientPicker.SelectedItem is RiftClientInfo
            {
                ProcessId: > 0,
                StartedAtUtc: not null,
                ImagePath: not null
            } client && client.Status is not ("Exited" or "Unavailable");
        PhysicalXyzClientPicker.IsEnabled = idle;
        PhysicalXyzRefreshButton.IsEnabled = idle;
        PhysicalXyzStopButton.IsEnabled = !_physicalXyzClosing
            && _physicalXyzWatchCancellation is { IsCancellationRequested: false };
    }

    private void PhysicalXyzStartClicked(object sender, RoutedEventArgs e)
    {
        _ = StartPhysicalXyzAsync();
    }

    private async Task StartPhysicalXyzAsync()
    {
        if (_closing || _physicalXyzClosing || _physicalXyzStartPending
            || _physicalXyzWatchCancellation is not null || _physicalXyzRefreshCancellation is not null)
            return;
        if (PhysicalXyzClientPicker.SelectedItem is not RiftClientInfo selected
            || selected.StartedAtUtc is not { } selectedStart
            || selected.ProcessId <= 0 || selected.ImagePath is null
            || selected.Status is "Exited" or "Unavailable")
        {
            PhysicalXyzStatusText.Text = "Choose a physical RIFT process with a verified PID/start time.";
            ClearPhysicalXyzPosition();
            return;
        }

        int processId = selected.ProcessId;
        long startTicks = selectedStart.UtcTicks;
        using var startCancellation = new CancellationTokenSource();
        _physicalXyzStartCancellation = startCancellation;
        _physicalXyzStartPending = true;
        UpdatePhysicalXyzStartAvailability();
        ClearPhysicalXyzPosition();
        PhysicalXyzStatusText.Text = "Rechecking exact PID/start identity before the first memory read...";

        try
        {
            // Never trust a previously selected row or select an arbitrary process.
            // An exact start-tick match prevents accepting a recycled PID.
            ClientInventorySnapshot current = await _scanClients(startCancellation.Token);
            if (_closing || _physicalXyzClosing || startCancellation.IsCancellationRequested)
                return;

            RiftClientInfo? matched = current.ErrorCode is null
                ? current.Clients.FirstOrDefault(client => client.ProcessId == processId
                    && client.StartedAtUtc?.UtcTicks == startTicks
                    && client.ImagePath is not null
                    && string.Equals(client.ImagePath, selected.ImagePath,
                        StringComparison.OrdinalIgnoreCase)
                    && client.Status is not ("Exited" or "Unavailable"))
                : null;
            if (matched is null)
            {
                PhysicalXyzStatusText.Text =
                    "Start denied: selected physical RIFT identity is no longer current. Refresh and reselect.";
                PhysicalXyzStatusText.Foreground = Brushes.LightCoral;
                ClearPhysicalXyzPosition();
                PhysicalXyzClientPicker.ItemsSource = null;
                return;
            }

            _physicalXyzPinnedPlayerKey = null;
            _physicalXyzComparison = new PhysicalRiftPositionChangeTracker(maxObservations: 3600);
            _physicalXyzAttempts = 0;
            _physicalXyzCandidates = 0;
            _physicalXyzUnavailable = 0;
            _physicalXyzIdentityFaults = 0;
            _physicalXyzErrors = 0;
            UpdatePhysicalXyzCounters();

            var stop = new CancellationTokenSource();
            _physicalXyzWatchCancellation = stop;
            _physicalXyzAgeTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                PhysicalXyzAgeTick, Dispatcher);
            _physicalXyzAgeTimer.Start();

            PhysicalXyzStatusText.Text = "Reading PID "
                + processId.ToString(CultureInfo.InvariantCulture)
                + " with exact process-start and game-build identity checks...";
            PhysicalXyzStatusText.Foreground = Brushes.Khaki;
            _physicalXyzWatchTask = RunPhysicalXyzWatchAsync(processId, startTicks, stop);
        }
        catch (OperationCanceledException)
        {
            if (!_closing && !_physicalXyzClosing)
                PhysicalXyzStatusText.Text = "Start cancelled. No native watch attached.";
            ClearPhysicalXyzPosition();
        }
        catch (Exception)
        {
            if (!_closing && !_physicalXyzClosing)
            {
                PhysicalXyzStatusText.Text = "Start failed before native sampling. Refresh and retry.";
                PhysicalXyzStatusText.Foreground = Brushes.LightCoral;
            }
            ClearPhysicalXyzPosition();
        }
        finally
        {
            if (ReferenceEquals(_physicalXyzStartCancellation, startCancellation))
                _physicalXyzStartCancellation = null;
            _physicalXyzStartPending = false;
            if (!_closing && !_physicalXyzClosing)
                UpdatePhysicalXyzStartAvailability();
        }
    }

    private async Task RunPhysicalXyzWatchAsync(
        int processId, long startTicks, CancellationTokenSource owner)
    {
        string ending = "Stopped by user. No gameplay input was sent.";
        var watch = Stopwatch.StartNew();
        try
        {
            while (!owner.IsCancellationRequested && !_closing && !_physicalXyzClosing)
            {
                _physicalXyzAttempts++;
                try
                {
                    using var sampleDeadline =
                        CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
                    sampleDeadline.CancelAfter(TimeSpan.FromSeconds(15));

                    // The native reader performs synchronous OS memory calls in
                    // parts of its operation; never execute them on the WPF dispatcher.
                    PhysicalRiftPositionResult sample = await Task.Run(
                        () => PhysicalRiftPositionReader.ReadPinnedAsync(
                            processId, startTicks, sampleDeadline.Token),
                        sampleDeadline.Token);

                    if (!CanShowPhysicalXyzSample(owner))
                        return;

                    if (sample.ProcessId != processId || sample.ProcessStartUtcTicks != startTicks)
                    {
                        _physicalXyzIdentityFaults++;
                        ApplyPhysicalXyzNonCandidate(
                            "Identity mismatch: native result returned a different PID/start time.",
                            Brushes.LightCoral);
                        ending = "Stopped: native result identity changed. Refresh and reselect.";
                        break;
                    }

                    if (sample.Status == PhysicalRiftPositionStatus.IdentityMismatch)
                    {
                        _physicalXyzIdentityFaults++;
                        ApplyPhysicalXyzNonCandidate(
                            "RIFT process/build identity changed; no position accepted.",
                            Brushes.LightCoral);
                        ending = "Stopped: physical RIFT identity changed. Refresh and reselect.";
                        break;
                    }

                    if (sample.Status == PhysicalRiftPositionStatus.Candidate
                        && ValidPhysicalXyzCandidate(sample))
                    {
                        PhysicalRiftPositionChangeSnapshot comparison = _physicalXyzComparison!.Observe(
                            sample, watch.ElapsedMilliseconds);
                        if (comparison.Status is PhysicalRiftPositionChangeStatus.IdentityChanged
                            or PhysicalRiftPositionChangeStatus.StaleAcquisitionGap
                            or PhysicalRiftPositionChangeStatus.OutOfOrder
                            or PhysicalRiftPositionChangeStatus.InvalidCandidate
                            or PhysicalRiftPositionChangeStatus.LimitReached)
                        {
                            _physicalXyzIdentityFaults++;
                            ApplyPhysicalXyzNonCandidate(
                                "Comparison invalidated: " + comparison.Reason + ". Refresh and reselect.",
                                Brushes.LightCoral);
                            ending = "Stopped: position comparison identity/freshness bounds failed.";
                            break;
                        }
                        if (_physicalXyzPinnedPlayerKey is { } priorKey
                            && priorKey != sample.PlayerUnitKey)
                        {
                            _physicalXyzIdentityFaults++;
                            ApplyPhysicalXyzNonCandidate(
                                "Player identity changed during sampling; prior XYZ rejected.",
                                Brushes.LightCoral);
                            ending = "Stopped: player identity changed. Refresh and reselect.";
                            break;
                        }
                        _physicalXyzPinnedPlayerKey = sample.PlayerUnitKey;
                        _physicalXyzCandidates++;
                        _physicalXyzVisibleCandidate = sample;
                        PhysicalXyzCoordinatesText.Text = FormatPhysicalXyzCandidate(sample);
                        PhysicalXyzStatusText.Text =
                            "Native XYZ candidate acquired from exact current-build process. "
                            + "Producer freshness and movement are NOT qualified.";
                        PhysicalXyzStatusText.Foreground = Brushes.LightGreen;
                        PhysicalXyzDifferenceText.Text = FormatPhysicalXyzComparison(comparison);
                        UpdatePhysicalXyzCandidateAge();
                    }
                    else if (sample.Status == PhysicalRiftPositionStatus.Candidate)
                    {
                        _physicalXyzComparison?.Observe(sample, watch.ElapsedMilliseconds);
                        _physicalXyzErrors++;
                        ApplyPhysicalXyzNonCandidate(
                            "Rejected an inconsistent or incorrectly pinned native candidate.",
                            Brushes.LightCoral);
                    }
                    else
                    {
                        _physicalXyzComparison?.Observe(sample, watch.ElapsedMilliseconds);
                        if (sample.Status == PhysicalRiftPositionStatus.Error)
                            _physicalXyzErrors++;
                        else
                            _physicalXyzUnavailable++;
                        ApplyPhysicalXyzNonCandidate(
                            sample.Status.ToString() + ": "
                            + ShortPhysicalXyzReason(sample.Reason), Brushes.Khaki);
                    }
                }
                catch (OperationCanceledException) when (owner.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    if (!CanShowPhysicalXyzSample(owner))
                        return;
                    _physicalXyzUnavailable++;
                    _physicalXyzComparison?.Reset();
                    ApplyPhysicalXyzNonCandidate("Native read exceeded its 15-second deadline.", Brushes.Khaki);
                }
                catch (Exception)
                {
                    if (!CanShowPhysicalXyzSample(owner))
                        return;
                    _physicalXyzErrors++;
                    _physicalXyzComparison?.Reset();
                    ApplyPhysicalXyzNonCandidate("Native acquisition failed unexpectedly; no XYZ accepted.",
                        Brushes.LightCoral);
                }

                if (CanShowPhysicalXyzSample(owner))
                    UpdatePhysicalXyzCounters();
                // One bounded read per tick, with no burst/catch-up work after a slow scan.
                await Task.Delay(TimeSpan.FromSeconds(1), owner.Token);
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // Normal Stop or window close; the read lease is owned by the reader.
        }
        catch (Exception)
        {
            ending = "Stopped after an unexpected watch failure; no XYZ accepted.";
        }
        finally
        {
            if (ReferenceEquals(_physicalXyzWatchCancellation, owner))
            {
                _physicalXyzWatchCancellation = null;
                _physicalXyzWatchTask = null;
                _physicalXyzPinnedPlayerKey = null;
                _physicalXyzComparison?.Cancel();
                _physicalXyzComparison = null;
                StopPhysicalXyzAgeTimer();
                ClearPhysicalXyzPosition();
                owner.Dispose();
                if (!_closing && !_physicalXyzClosing)
                {
                    PhysicalXyzStatusText.Text = ending;
                    PhysicalXyzStatusText.Foreground =
                        ending.StartsWith("Stopped:", StringComparison.Ordinal)
                        ? Brushes.LightCoral : Brushes.Khaki;
                    UpdatePhysicalXyzCounters();
                    UpdatePhysicalXyzStartAvailability();
                }
            }
            else
                owner.Dispose();
        }
    }

    private static bool ValidPhysicalXyzCandidate(PhysicalRiftPositionResult sample) =>
        sample.Status == PhysicalRiftPositionStatus.Candidate
        && string.Equals(sample.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
            StringComparison.OrdinalIgnoreCase)
        && sample.PlayerUnitKey is > 0
        && sample.X is { } x && float.IsFinite(x)
        && sample.Y is { } y && float.IsFinite(y)
        && sample.Z is { } z && float.IsFinite(z)
        && DateTimeOffset.UtcNow >= sample.CapturedAtUtc
        && DateTimeOffset.UtcNow - sample.CapturedAtUtc < TimeSpan.FromSeconds(2.5);

    private bool CanShowPhysicalXyzSample(CancellationTokenSource owner) =>
        !_closing && !_physicalXyzClosing && !owner.IsCancellationRequested
        && ReferenceEquals(_physicalXyzWatchCancellation, owner);

    private static string ShortPhysicalXyzReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "unknown";
        // Avoid putting unbounded, potentially path-bearing exception contents in the UI.
        string clean = new(reason.Where(character => !char.IsControl(character)).Take(120).ToArray());
        return clean;
    }

    private static string FormatPhysicalXyzCandidate(PhysicalRiftPositionResult sample)
    {
        if (!ValidPhysicalXyzCandidate(sample))
            return "XYZ: unavailable";
        return "X: " + sample.X!.Value.ToString("F3", CultureInfo.InvariantCulture)
            + "   Y: " + sample.Y!.Value.ToString("F3", CultureInfo.InvariantCulture)
            + "   Z: " + sample.Z!.Value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string FormatPhysicalXyzComparison(PhysicalRiftPositionChangeSnapshot comparison)
    {
        return comparison.Status switch
        {
            PhysicalRiftPositionChangeStatus.Baseline =>
                "Comparison: first pinned native XYZ baseline; no prior coordinate.",
            PhysicalRiftPositionChangeStatus.Unchanged =>
                "Comparison: unchanged XYZ across "
                + comparison.CandidateSamples.ToString(CultureInfo.InvariantCulture)
                + " acquisitions (not proof of passive producer refresh).",
            PhysicalRiftPositionChangeStatus.Changed =>
                "Comparison: coordinate difference OBSERVED, not proven gameplay movement. "
                + "|ΔX|=" + comparison.AbsoluteDeltaX.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture)
                + "  |ΔY|=" + comparison.AbsoluteDeltaY.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture)
                + "  |ΔZ|=" + comparison.AbsoluteDeltaZ.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture)
                + "  3D distance=" + comparison.Distance3D.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture),
            _ => "Comparison: " + comparison.Status.ToString() + " (baseline invalidated)."
        };
    }

    private void ApplyPhysicalXyzNonCandidate(string status, Brush statusBrush)
    {
        ClearPhysicalXyzPosition();
        PhysicalXyzStatusText.Text = status;
        PhysicalXyzStatusText.Foreground = statusBrush;
    }

    private void ClearPhysicalXyzPosition()
    {
        _physicalXyzVisibleCandidate = null;
        if (PhysicalXyzCoordinatesText is not null)
            PhysicalXyzCoordinatesText.Text = "XYZ: unavailable";
        if (PhysicalXyzCandidateAgeText is not null)
            PhysicalXyzCandidateAgeText.Text = "Candidate age: unavailable";
        if (PhysicalXyzDifferenceText is not null)
            PhysicalXyzDifferenceText.Text = "Position comparison: unavailable; previous baseline cleared.";
    }

    private void UpdatePhysicalXyzCounters()
    {
        PhysicalXyzCountsText.Text =
            "Attempts: " + _physicalXyzAttempts.ToString(CultureInfo.InvariantCulture)
            + "  |  Candidates: " + _physicalXyzCandidates.ToString(CultureInfo.InvariantCulture)
            + "  |  Unavailable: " + _physicalXyzUnavailable.ToString(CultureInfo.InvariantCulture)
            + "  |  Identity faults: " + _physicalXyzIdentityFaults.ToString(CultureInfo.InvariantCulture)
            + "  |  Errors: " + _physicalXyzErrors.ToString(CultureInfo.InvariantCulture);
    }

    private void PhysicalXyzAgeTick(object? sender, EventArgs e)
    {
        if (!_closing && !_physicalXyzClosing)
            UpdatePhysicalXyzCandidateAge();
    }

    private void UpdatePhysicalXyzCandidateAge()
    {
        PhysicalRiftPositionResult? last = _physicalXyzVisibleCandidate;
        if (last is null)
        {
            PhysicalXyzCandidateAgeText.Text = "Candidate age: unavailable";
            return;
        }
        var age = DateTimeOffset.UtcNow - last.CapturedAtUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(2.5))
        {
            // A stalled native read must not leave a plausible old coordinate or baseline visible.
            _physicalXyzComparison?.Reset();
            ClearPhysicalXyzPosition();
            return;
        }
        PhysicalXyzCandidateAgeText.Text =
            "Native candidate age: " + age.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)
            + " ms (acquisition age only; no producer timestamp)";
    }

    private void StopPhysicalXyzAgeTimer()
    {
        if (_physicalXyzAgeTimer is null)
            return;
        _physicalXyzAgeTimer.Stop();
        _physicalXyzAgeTimer.Tick -= PhysicalXyzAgeTick;
        _physicalXyzAgeTimer = null;
    }

    private void PhysicalXyzStopClicked(object sender, RoutedEventArgs e)
    {
        CancellationTokenSource? current = _physicalXyzWatchCancellation;
        if (current is null || current.IsCancellationRequested)
            return;
        current.Cancel();
        _physicalXyzComparison?.Cancel();
        ClearPhysicalXyzPosition();
        PhysicalXyzStatusText.Text = "Stopping read-only physical XYZ sampling...";
        PhysicalXyzStatusText.Foreground = Brushes.Khaki;
        UpdatePhysicalXyzStartAvailability();
    }

    /// <summary>
    /// Called by MainWindow.Clients.cs OnClosed; synchronous cancellation happens
    /// before background native reads can report again. Their guarded finally
    /// blocks dispose the cancellation and never write to the closed window.
    /// </summary>
    internal void CancelPhysicalXyzOnClose()
    {
        _physicalXyzClosing = true;
        _physicalXyzRefreshCancellation?.Cancel();
        _physicalXyzStartCancellation?.Cancel();
        _physicalXyzWatchCancellation?.Cancel();
        _physicalXyzComparison?.Cancel();
        StopPhysicalXyzAgeTimer();
        ClearPhysicalXyzPosition();
    }

    /// <summary>Construct-only fixture: no process enumeration or live memory reads.</summary>
    internal bool RunPhysicalXyzUiSmokeCheck()
    {
        var example = new PhysicalRiftPositionResult(
            PhysicalRiftPositionStatus.Candidate,
            "synthetic-current-build-candidate",
            123, DateTimeOffset.UnixEpoch.UtcTicks,
            PhysicalRiftPositionReader.GameSha256, 42,
            1.25f, 2.5f, 3.75f,
            DateTimeOffset.UtcNow,
            12);
        var notFresh = example with { Status = PhysicalRiftPositionStatus.Unavailable };
        var exampleTracker = new PhysicalRiftPositionChangeTracker();
        PhysicalRiftPositionChangeSnapshot baseline = exampleTracker.Observe(example, 100);
        PhysicalRiftPositionChangeSnapshot moved = exampleTracker.Observe(
            example with { X = 4.25f, CapturedAtUtc = DateTimeOffset.UtcNow }, 1200);
        return PhysicalXyzTab is not null
            && ObserverTabs.Items.IndexOf(PhysicalXyzTab) == 0
            && PhysicalXyzClientPicker is { Items.Count: 0 }
            && PhysicalXyzStartButton is { IsEnabled: false }
            && PhysicalXyzStopButton is { IsEnabled: false }
            && PhysicalXyzCoordinatesText?.Text == "XYZ: unavailable"
            && PhysicalXyzDifferenceText?.Text == "Position comparison: no baseline."
            && baseline.Status == PhysicalRiftPositionChangeStatus.Baseline
            && moved.Status == PhysicalRiftPositionChangeStatus.Changed
            && FormatPhysicalXyzComparison(moved).Contains("not proven gameplay movement", StringComparison.Ordinal)
            && moved.Distance3D == 3.0
            && _physicalXyzWatchCancellation is null
            && _physicalXyzWatchTask is null
            && _physicalXyzRefreshCancellation is null
            && FormatPhysicalXyzCandidate(example) == "X: 1.250   Y: 2.500   Z: 3.750"
            && FormatPhysicalXyzCandidate(notFresh) == "XYZ: unavailable"
            && PhysicalXyzEvidenceText.Text.Contains("NOT QUALIFIED", StringComparison.Ordinal)
            && PhysicalXyzEvidenceText.Text.Contains("input authority NONE", StringComparison.Ordinal);
    }
}
