using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
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
    private PhysicalRiftPositionTraceRecorder? _physicalXyzTrace;
    // Stopwatch is local to each explicitly started observation session.
    private const int PhysicalXyzTraceLimit = 600;
    private ulong? _physicalXyzPinnedPlayerKey;
    private bool _physicalXyzStartPending;
    private bool _physicalXyzClosing;
    private int _physicalXyzAttempts;
    private int _physicalXyzCandidates;
    private int _physicalXyzUnavailable;
    private int _physicalXyzIdentityFaults;
    private int _physicalXyzContinuityFaults;
    private int _physicalXyzErrors;
    private int _physicalXyzChangedSamples;
    private const int PhysicalXyzRecentChangeLimit = 20;
    private readonly Queue<string> _physicalXyzRecentChanges = new();

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
            // A new trace epoch begins only after the exact PID/start/path preflight passes.
            // Previous trace evidence remains available if Start was denied or cancelled.
            _physicalXyzTrace = new PhysicalRiftPositionTraceRecorder(maxEntries: PhysicalXyzTraceLimit);
            _physicalXyzTrace.BeginSession(processId, startTicks);
            PhysicalXyzTraceSaveStatusText.Text = "New read-only trace session. Nothing has been saved to disk.";
            UpdatePhysicalXyzTraceSummary();
            _physicalXyzAttempts = 0;
            _physicalXyzCandidates = 0;
            _physicalXyzUnavailable = 0;
            _physicalXyzIdentityFaults = 0;
            _physicalXyzContinuityFaults = 0;
            _physicalXyzErrors = 0;
            ResetPhysicalXyzChangeHistory();
            PhysicalXyzHistorySourceText.Text = FormatPhysicalXyzHistorySource(processId, startTicks, null);
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
        // The same monotonic clock drives the comparison and trace.
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

                    // Capture every returned native result before any terminal rejection/early break.
                    // The trace recorder nulls rejected XYZ and pins identity independently.
                    long observedAtMs = watch.ElapsedMilliseconds;
                    DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
                    bool candidateUsable = ValidPhysicalXyzCandidate(sample, observedAtUtc);
                    PhysicalRiftPositionResult comparisonCandidate =
                        sample.Status == PhysicalRiftPositionStatus.Candidate && !candidateUsable
                            ? sample with { X = null, Y = null, Z = null }
                            : sample;
                    PhysicalRiftPositionChangeSnapshot comparison =
                        _physicalXyzComparison!.Observe(comparisonCandidate, observedAtMs);
                    RecordPhysicalXyzTrace(sample, comparison, observedAtMs, observedAtUtc);

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
                        && candidateUsable)
                    {
                        // This comparison was already observed and recorded for this read.
                        if (comparison.Status is PhysicalRiftPositionChangeStatus.IdentityChanged
                            or PhysicalRiftPositionChangeStatus.StaleAcquisitionGap
                            or PhysicalRiftPositionChangeStatus.OutOfOrder
                            or PhysicalRiftPositionChangeStatus.InvalidCandidate
                            or PhysicalRiftPositionChangeStatus.LimitReached)
                        {
                            if (comparison.Status == PhysicalRiftPositionChangeStatus.IdentityChanged)
                                _physicalXyzIdentityFaults++;
                            else
                                _physicalXyzContinuityFaults++;
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
                        if (_physicalXyzPinnedPlayerKey is null)
                            PhysicalXyzHistorySourceText.Text = FormatPhysicalXyzHistorySource(
                                processId, startTicks, sample.PlayerUnitKey);
                        _physicalXyzPinnedPlayerKey = sample.PlayerUnitKey;
                        _physicalXyzCandidates++;
                        _physicalXyzVisibleCandidate = sample;
                        PhysicalXyzCoordinatesText.Text = FormatPhysicalXyzCandidate(sample, candidateUsable);
                        PhysicalXyzStatusText.Text =
                            "Native XYZ candidate acquired from exact current-build process. "
                            + "Producer freshness and movement are NOT qualified.";
                        PhysicalXyzStatusText.Foreground = Brushes.LightGreen;
                        PhysicalXyzDifferenceText.Text = FormatPhysicalXyzComparison(comparison);
                        if (comparison.Status == PhysicalRiftPositionChangeStatus.Changed)
                            RecordPhysicalXyzCoordinateChange(sample, comparison);
                        UpdatePhysicalXyzCandidateAge();
                    }
                    else if (sample.Status == PhysicalRiftPositionStatus.Candidate)
                    {
                        // Comparison and outcome were already recorded before status classification.
                        _physicalXyzErrors++;
                        ApplyPhysicalXyzNonCandidate(
                            "Rejected an inconsistent or incorrectly pinned native candidate.",
                            Brushes.LightCoral);
                    }
                    else
                    {
                        // Comparison and outcome were already recorded before status classification.
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
                    RecordPhysicalXyzUnavailable("native_read_timeout", watch.ElapsedMilliseconds);
                    _physicalXyzComparison?.Reset();
                    ApplyPhysicalXyzNonCandidate("Native read exceeded its 15-second deadline.", Brushes.Khaki);
                }
                catch (Exception)
                {
                    if (!CanShowPhysicalXyzSample(owner))
                        return;
                    _physicalXyzErrors++;
                    RecordPhysicalXyzUnavailable("native_read_exception", watch.ElapsedMilliseconds);
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
                // Historical trace remains after the sampling clock stops.
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
                // The bounded historical trace intentionally survives Stop and faults.
                UpdatePhysicalXyzTraceSummary();
            }
            else
                owner.Dispose();
        }
    }

    private void RecordPhysicalXyzTrace(
        PhysicalRiftPositionResult sample, PhysicalRiftPositionChangeSnapshot comparison,
        long monotonicElapsedMilliseconds, DateTimeOffset observedAtUtc)
    {
        _physicalXyzTrace?.Record(sample, comparison, monotonicElapsedMilliseconds, observedAtUtc);
        UpdatePhysicalXyzTraceSummary();
    }

    private void RecordPhysicalXyzUnavailable(string safeReason, long monotonicElapsedMilliseconds)
    {
        _physicalXyzTrace?.RecordUnavailable(
            safeReason, monotonicElapsedMilliseconds, DateTimeOffset.UtcNow);
        UpdatePhysicalXyzTraceSummary();
    }

    private void UpdatePhysicalXyzTraceSummary()
    {
        if (_closing || _physicalXyzClosing || PhysicalXyzTraceSummaryText is null)
            return;

        PhysicalRiftPositionTraceReport? report = _physicalXyzTrace?.Snapshot();
        if (report is null)
        {
            PhysicalXyzTraceSummaryText.Text =
                "Trace: no verified observation session yet. 0 samples retained.";
            PhysicalXyzTraceSaveButton.IsEnabled = false;
            PhysicalXyzTraceAssessmentText.Text =
                "A/B assessment: insufficient evidence. Next: observe repeated XYZ values at one coordinate plateau.";
            PhysicalXyzTraceAssessmentText.Foreground = Brushes.Khaki;
            return;
        }

        bool sampling = _physicalXyzWatchCancellation is { IsCancellationRequested: false };
        PhysicalXyzTraceSummaryText.Text = FormatPhysicalXyzTraceSummary(report, sampling);
        PhysicalRiftPositionTraceAssessment assessment = PhysicalRiftPositionTraceAssessor.Assess(report);
        PhysicalXyzTraceAssessmentText.Text = FormatPhysicalXyzTraceAssessment(assessment, sampling);
        PhysicalXyzTraceAssessmentText.Foreground = assessment.State switch
        {
            PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved => Brushes.LightGreen,
            PhysicalRiftPositionTraceAssessmentState.Interrupted
                or PhysicalRiftPositionTraceAssessmentState.SourceChanged
                or PhysicalRiftPositionTraceAssessmentState.Truncated => Brushes.LightCoral,
            _ => Brushes.Khaki
        };
        PhysicalXyzTraceSaveButton.IsEnabled = report.TotalSamples > 0;
    }

    private static string FormatPhysicalXyzTraceSummary(
        PhysicalRiftPositionTraceReport report, bool sampling)
    {
        return "Trace (" + (sampling ? "sampling now" : "HISTORICAL after Stop/fault")
            + "): total native outcomes " + report.TotalSamples.ToString(CultureInfo.InvariantCulture)
            + ", retained " + report.RetainedSamples.ToString(CultureInfo.InvariantCulture)
            + "/" + PhysicalXyzTraceLimit.ToString(CultureInfo.InvariantCulture)
            + ", evicted " + report.EvictedSamples.ToString(CultureInfo.InvariantCulture)
            + ", candidates " + report.CandidateSamples.ToString(CultureInfo.InvariantCulture)
            + ", changes " + report.ChangedSamples.ToString(CultureInfo.InvariantCulture)
            + ", unavailable " + report.UnavailableSamples.ToString(CultureInfo.InvariantCulture)
            + ". Pinned PID " + (report.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "pending")
            + ", start UTC ticks " + (report.ProcessStartUtcTicks?.ToString(CultureInfo.InvariantCulture) ?? "pending")
            + ", player key " + (report.PlayerUnitKey?.ToString(CultureInfo.InvariantCulture) ?? "pending")
            + ". Producer freshness/gameplay movement NOT proven.";
    }

    /// <summary>
    /// Pure read-only display of the bounded A/B acquisition assessment.
    /// A/B observations never grant producer freshness or gameplay movement authority.
    /// </summary>
    private static string FormatPhysicalXyzTraceAssessment(
        PhysicalRiftPositionTraceAssessment assessment, bool sampling)
    {
        string prefix = "Read-only A/B (" + (sampling ? "sampling" : "HISTORICAL") + "): ";
        string next = assessment.State switch
        {
            PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence =>
                "insufficient evidence. Next: establish plateau A with at least three consecutive matching native XYZ.",
            PhysicalRiftPositionTraceAssessmentState.OneCoordinatePlateau =>
                "one repeated coordinate plateau observed ("
                + assessment.BaselineStableRunCount.ToString(CultureInfo.InvariantCulture)
                + " samples). Next: acquire a different XYZ value for three consecutive samples (plateau B).",
            PhysicalRiftPositionTraceAssessmentState.AwaitingFinalPlateau =>
                "different XYZ value observed. Next: establish three consecutive matching samples at the final coordinate (plateau B).",
            PhysicalRiftPositionTraceAssessmentState.Interrupted =>
                "interrupted native observations; no A/B conclusion. Next: verified new Start with uninterrupted matching samples.",
            PhysicalRiftPositionTraceAssessmentState.SourceChanged =>
                "source identity changed; no A/B conclusion. Next: refresh, reselect and verify a new Start.",
            PhysicalRiftPositionTraceAssessmentState.Truncated =>
                "historical trace truncated; no complete A/B conclusion. Next: verified new Start with a fresh bounded trace.",
            PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved =>
                "two repeated coordinate values OBSERVED: A XYZ "
                + FormatPhysicalXyzAssessmentCoordinates(
                    assessment.BeforeX, assessment.BeforeY, assessment.BeforeZ)
                + " at " + (assessment.BeforeCapturedAtUtc?.ToUniversalTime()
                    .ToString("HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture) ?? "UTC unknown")
                + " (+" + (assessment.BeforeMonotonicElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture)
                    ?? "?") + " ms); B XYZ "
                + FormatPhysicalXyzAssessmentCoordinates(
                    assessment.AfterX, assessment.AfterY, assessment.AfterZ)
                + " at " + (assessment.AfterCapturedAtUtc?.ToUniversalTime()
                    .ToString("HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture) ?? "UTC unknown")
                + " (+" + (assessment.AfterMonotonicElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture)
                    ?? "?") + " ms); distance3D "
                + (assessment.Distance3D?.ToString("F3", CultureInfo.InvariantCulture) ?? "?")
                + ". NOT proof of gameplay movement or passive producer freshness.",
            _ => "unknown assessment state; no movement or producer freshness claim."
        };
        return prefix + next;
    }

    private static string FormatPhysicalXyzAssessmentCoordinates(
        float? x, float? y, float? z) =>
        (x?.ToString("F3", CultureInfo.InvariantCulture) ?? "?") + "/"
        + (y?.ToString("F3", CultureInfo.InvariantCulture) ?? "?") + "/"
        + (z?.ToString("F3", CultureInfo.InvariantCulture) ?? "?");

    /// <summary>
    /// The only user-triggered write path: SaveFileDialog plus validated no-clobber exporter.
    /// No automatic trace persistence or exposure of selected destination paths.
    /// </summary>
    private void PhysicalXyzTraceSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || _physicalXyzClosing)
            return;
        PhysicalRiftPositionTraceReport? snapshot = _physicalXyzTrace?.Snapshot();
        if (snapshot is null || snapshot.TotalSamples == 0)
        {
            PhysicalXyzTraceSaveStatusText.Text = "No trace outcomes to save.";
            return;
        }

        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save historical read-only physical XYZ trace (new file)",
                Filter = "JSON trace (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = false,
                CheckPathExists = true,
                FileName = "autofight-physical-xyz-"
                    + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + ".json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                PhysicalXyzTraceSaveStatusText.Text =
                    "Save cancelled; the trace remains available in memory.";
                return;
            }

            PhysicalRiftPositionTraceExporter.SaveNew(dialog.FileName, snapshot);
            PhysicalXyzTraceSaveStatusText.Text = "Saved a new JSON file with "
                + snapshot.RetainedSamples.ToString(CultureInfo.InvariantCulture)
                + " retained read-only outcomes. Historical evidence only; no producer freshness or movement proof.";
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or NotSupportedException
            or System.Security.SecurityException)
        {
            PhysicalXyzTraceSaveStatusText.Text =
                "Save failed or destination exists. Choose a new JSON filename; no existing file was replaced.";
        }
    }

    private static bool ValidPhysicalXyzCandidate(PhysicalRiftPositionResult sample) =>
        ValidPhysicalXyzCandidate(sample, DateTimeOffset.UtcNow);

    private static bool ValidPhysicalXyzCandidate(
        PhysicalRiftPositionResult sample, DateTimeOffset observedAtUtc) =>
        sample.Status == PhysicalRiftPositionStatus.Candidate
        && string.Equals(sample.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
            StringComparison.OrdinalIgnoreCase)
        && sample.PlayerUnitKey is > 0
        && sample.X is { } x && float.IsFinite(x)
        && sample.Y is { } y && float.IsFinite(y)
        && sample.Z is { } z && float.IsFinite(z)
        && observedAtUtc >= sample.CapturedAtUtc
        && observedAtUtc - sample.CapturedAtUtc < TimeSpan.FromSeconds(2.5);

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

    private static string FormatPhysicalXyzCandidate(PhysicalRiftPositionResult sample) =>
        FormatPhysicalXyzCandidate(sample, ValidPhysicalXyzCandidate(sample));

    private static string FormatPhysicalXyzCandidate(PhysicalRiftPositionResult sample, bool candidateUsable)
    {
        if (!candidateUsable)
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

    /// <summary>
    /// A session's source remains labeled with the recorded history after Stop or
    /// source loss; mere refresh/selection never alters this pinned provenance.
    /// </summary>
    private static string FormatPhysicalXyzHistorySource(int processId, long startTicks, ulong? playerUnitKey)
    {
        return "History source (last successfully started session; HISTORICAL after Stop): PID "
            + processId.ToString(CultureInfo.InvariantCulture)
            + " | process start UTC ticks " + startTicks.ToString(CultureInfo.InvariantCulture)
            + " | player unit key " + (playerUnitKey is > 0
                ? playerUnitKey.Value.ToString(CultureInfo.InvariantCulture)
                : "pending first valid sample");
    }

    /// <summary>
    /// Retain only bounded, manually copyable evidence of coordinate VALUE changes.
    /// This is historical acquisition evidence, never current XYZ or movement proof.
    /// </summary>
    private void RecordPhysicalXyzCoordinateChange(
        PhysicalRiftPositionResult sample, PhysicalRiftPositionChangeSnapshot comparison)
    {
        if (comparison.Status != PhysicalRiftPositionChangeStatus.Changed)
            return;
        _physicalXyzChangedSamples++;
        string capturedUtc = sample.CapturedAtUtc.ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture);
        string distance = comparison.Distance3D.GetValueOrDefault()
            .ToString("F3", CultureInfo.InvariantCulture);
        string entry = "#"
            + _physicalXyzChangedSamples.ToString(CultureInfo.InvariantCulture)
            + "  " + capturedUtc
            + "  |ΔX|=" + comparison.AbsoluteDeltaX.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture)
            + "  |ΔY|=" + comparison.AbsoluteDeltaY.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture)
            + "  |ΔZ|=" + comparison.AbsoluteDeltaZ.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture)
            + "  distance3D=" + distance
            + "  | result XYZ "
            + sample.X.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture) + "/"
            + sample.Y.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture) + "/"
            + sample.Z.GetValueOrDefault().ToString("F3", CultureInfo.InvariantCulture);

        if (_physicalXyzRecentChanges.Count >= PhysicalXyzRecentChangeLimit)
            _physicalXyzRecentChanges.Dequeue();
        _physicalXyzRecentChanges.Enqueue(entry);
        PhysicalXyzChangeSummaryText.Text = "Observed coordinate-value differences this session: "
            + _physicalXyzChangedSamples.ToString(CultureInfo.InvariantCulture)
            + ". Last: " + capturedUtc + ", 3D step distance " + distance
            + ". HISTORICAL only; not proven gameplay movement.";
        PhysicalXyzChangeHistoryText.Text = string.Join(Environment.NewLine, _physicalXyzRecentChanges);
    }

    private void ResetPhysicalXyzChangeHistory()
    {
        _physicalXyzChangedSamples = 0;
        _physicalXyzRecentChanges.Clear();
        PhysicalXyzChangeSummaryText.Text =
            "Observed coordinate-value differences this session: 0. No changes recorded.";
        PhysicalXyzChangeHistoryText.Text = "No coordinate-value differences recorded in this session.";
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
            + "  |  Changes: " + _physicalXyzChangedSamples.ToString(CultureInfo.InvariantCulture)
            + "  |  Identity faults: " + _physicalXyzIdentityFaults.ToString(CultureInfo.InvariantCulture)
            + "  |  Continuity faults: " + _physicalXyzContinuityFaults.ToString(CultureInfo.InvariantCulture)
            + "  |  Errors: " + _physicalXyzErrors.ToString(CultureInfo.InvariantCulture);
        UpdatePhysicalXyzTraceSummary();
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
            // UI-only age invalidation is not a native read outcome: no trace entry.
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
        PhysicalRiftPositionResult syntheticMoved = example with
        {
            X = 4.25f,
            CapturedAtUtc = example.CapturedAtUtc.AddMilliseconds(100)
        };
        PhysicalRiftPositionChangeSnapshot moved = exampleTracker.Observe(syntheticMoved, 1200);
        var syntheticTrace = new PhysicalRiftPositionTraceRecorder(maxEntries: PhysicalXyzTraceLimit);
        syntheticTrace.BeginSession(123, DateTimeOffset.UnixEpoch.UtcTicks);
        syntheticTrace.Record(example, baseline, 100, example.CapturedAtUtc);
        syntheticTrace.Record(syntheticMoved, moved, 1200, syntheticMoved.CapturedAtUtc);
        syntheticTrace.RecordUnavailable("native_read_timeout", 1300, DateTimeOffset.UtcNow);
        PhysicalRiftPositionTraceReport syntheticReport = syntheticTrace.Snapshot();
        bool traceSynthetic = syntheticReport.TotalSamples == 3
            && syntheticReport.RetainedSamples == 3
            && syntheticReport.ProcessId == 123
            && syntheticReport.ProcessStartUtcTicks == DateTimeOffset.UnixEpoch.UtcTicks
            && syntheticReport.Samples.Count == 3
            && FormatPhysicalXyzTraceSummary(syntheticReport, sampling: false)
                .Contains("HISTORICAL after Stop/fault", StringComparison.Ordinal)
            && FormatPhysicalXyzTraceSummary(syntheticReport, sampling: true)
                .Contains("sampling now", StringComparison.Ordinal)
            && syntheticReport.ToJson().Contains("samples", StringComparison.OrdinalIgnoreCase);

        // Construct-only A/B fixtures: three identical A and three identical B acquisitions.
        // No native process access, game input, filesystem I/O or SaveFileDialog.
        var abTracker = new PhysicalRiftPositionChangeTracker();
        var abTrace = new PhysicalRiftPositionTraceRecorder(maxEntries: PhysicalXyzTraceLimit);
        abTrace.BeginSession(123, DateTimeOffset.UnixEpoch.UtcTicks);
        var interruptedTracker = new PhysicalRiftPositionChangeTracker();
        var interruptedTrace = new PhysicalRiftPositionTraceRecorder(maxEntries: PhysicalXyzTraceLimit);
        interruptedTrace.BeginSession(123, DateTimeOffset.UnixEpoch.UtcTicks);
        for (int i = 0; i < 6; i++)
        {
            PhysicalRiftPositionResult step = example with
            {
                X = i < 3 ? 1.25f : 4.25f,
                CapturedAtUtc = example.CapturedAtUtc.AddSeconds(i)
            };
            long elapsed = (i + 1L) * 1000;
            PhysicalRiftPositionChangeSnapshot abComparison = abTracker.Observe(step, elapsed);
            abTrace.Record(step, abComparison, elapsed, step.CapturedAtUtc);

            if (i < 3)
            {
                PhysicalRiftPositionChangeSnapshot interruptedComparison =
                    interruptedTracker.Observe(step, elapsed);
                interruptedTrace.Record(step, interruptedComparison, elapsed, step.CapturedAtUtc);
            }
        }
        interruptedTrace.RecordUnavailable("native_read_timeout", 4000,
            example.CapturedAtUtc.AddSeconds(3));
        PhysicalRiftPositionTraceAssessment positiveAssessment =
            PhysicalRiftPositionTraceAssessor.Assess(abTrace.Snapshot());
        PhysicalRiftPositionTraceAssessment interruptedAssessment =
            PhysicalRiftPositionTraceAssessor.Assess(interruptedTrace.Snapshot());
        PhysicalRiftPositionTraceAssessment emptyAssessment =
            PhysicalRiftPositionTraceAssessor.Assess(null);
        bool assessmentSynthetic =
            positiveAssessment.State == PhysicalRiftPositionTraceAssessmentState.TwoCoordinatePlateausObserved
            && FormatPhysicalXyzTraceAssessment(positiveAssessment, sampling: false)
                .Contains("two repeated coordinate values OBSERVED", StringComparison.Ordinal)
            && FormatPhysicalXyzTraceAssessment(positiveAssessment, sampling: false)
                .Contains("NOT proof of gameplay movement or passive producer freshness", StringComparison.Ordinal)
            && interruptedAssessment.State == PhysicalRiftPositionTraceAssessmentState.Interrupted
            && FormatPhysicalXyzTraceAssessment(interruptedAssessment, sampling: false)
                .Contains("no A/B conclusion", StringComparison.Ordinal)
            && emptyAssessment.State == PhysicalRiftPositionTraceAssessmentState.InsufficientEvidence;

        bool initial = PhysicalXyzTab is not null
            && ObserverTabs.Items.IndexOf(PhysicalXyzTab) == 0
            && PhysicalXyzClientPicker is { Items.Count: 0 }
            && PhysicalXyzStartButton is { IsEnabled: false }
            && PhysicalXyzStopButton is { IsEnabled: false }
            && PhysicalXyzTraceSaveButton is { IsEnabled: false }
            && PhysicalXyzTraceSummaryText.Text.Contains("no verified observation session yet", StringComparison.Ordinal)
            && PhysicalXyzTraceAssessmentText.Text.Contains("insufficient evidence", StringComparison.Ordinal)
            && PhysicalXyzTraceSaveStatusText.Text.Contains("Nothing saved", StringComparison.Ordinal)
            && _physicalXyzTrace is null
            && PhysicalXyzCoordinatesText?.Text == "XYZ: unavailable"
            && PhysicalXyzDifferenceText?.Text == "Position comparison: no baseline."
            && PhysicalXyzChangeSummaryText.Text.Contains("differences this session: 0", StringComparison.Ordinal)
            && PhysicalXyzChangeHistoryText.Text == "No coordinate-value differences recorded in this session."
            && PhysicalXyzHistorySourceText.Text == "History source: no pinned observation session yet."
            && FormatPhysicalXyzHistorySource(123, 456, null).Contains("PID 123 | process start UTC ticks 456 | player unit key pending", StringComparison.Ordinal)
            && FormatPhysicalXyzHistorySource(123, 456, 42).EndsWith("player unit key 42", StringComparison.Ordinal)
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

        // Pure presentation fixture; no process listing, memory reads, timers or game input.
        // Confirm historical differences remain visible and the retained list stays bounded.
        for (int i = 0; i < PhysicalXyzRecentChangeLimit + 3; i++)
            RecordPhysicalXyzCoordinateChange(example with { X = 4.25f + i }, moved);
        bool retained = _physicalXyzChangedSamples == PhysicalXyzRecentChangeLimit + 3
            && _physicalXyzRecentChanges.Count == PhysicalXyzRecentChangeLimit
            && !PhysicalXyzChangeHistoryText.Text.Contains("#1  ", StringComparison.Ordinal)
            && PhysicalXyzChangeHistoryText.Text.Contains("#23  ", StringComparison.Ordinal)
            && PhysicalXyzChangeSummaryText.Text.Contains("HISTORICAL only", StringComparison.Ordinal)
            && PhysicalXyzChangeSummaryText.Text.Contains("not proven gameplay movement", StringComparison.Ordinal);
        ResetPhysicalXyzChangeHistory();
        bool reset = _physicalXyzChangedSamples == 0 && _physicalXyzRecentChanges.Count == 0
            && PhysicalXyzChangeHistoryText.Text == "No coordinate-value differences recorded in this session.";
        return initial && retained && reset && traceSynthetic && assessmentSynthetic;
    }
}
