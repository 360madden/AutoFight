namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private enum VmOperation
    {
        None,
        ControllerStatus,
        PinnedTelemetryRead,
        ShortWatch,
        BoundPreflight,
        SupervisedPulse,
        SustainedQualification,
        SaveEvidence
    }

    // Dispatcher-owned, window-local exclusion only. Cross-process authority
    // remains with the independently supervised RR2 controller.
    private VmOperation _vmOperation;
    private CancellationTokenSource? _vmOperationCancellation;

    private CancellationTokenSource? TryBeginVmOperation(VmOperation kind)
    {
        if (_closing || kind == VmOperation.None || _vmOperationCancellation is not null)
            return null;

        var run = new CancellationTokenSource();
        _vmOperation = kind;
        _vmOperationCancellation = run;
        UpdateVmOperationControls();
        return run;
    }

    private bool OwnsVmOperation(VmOperation kind, CancellationTokenSource run) =>
        !_closing && _vmOperation == kind && ReferenceEquals(_vmOperationCancellation, run);

    private void EndVmOperation(VmOperation kind, CancellationTokenSource run)
    {
        if (_vmOperation == kind && ReferenceEquals(_vmOperationCancellation, run))
        {
            _vmOperationCancellation = null;
            _vmOperation = VmOperation.None;
            if (!_closing) UpdateVmOperationControls();
        }
        run.Dispose();
    }

    private void CancelVmOperation() => _vmOperationCancellation?.Cancel();

    private void UpdateVmOperationControls()
    {
        bool idle = !_closing && _vmOperationCancellation is null;
        VmRefreshButton.IsEnabled = idle;
        VmTelemetryButton.IsEnabled = idle;
        VmPulseButton.IsEnabled = idle;
        VmPreflightButton.IsEnabled = idle;
        VmWatchStartButton.IsEnabled = idle;
        VmWatchDuration.IsEnabled = idle;
        VmQualifyStartButton.IsEnabled = idle;
        VmQualifyLab.IsEnabled = idle;
        VmQualifySaveButton.IsEnabled = idle && _lastVmQualificationEvidence is not null;
        VmWatchStopButton.IsEnabled = !_closing && _vmOperation == VmOperation.ShortWatch
            && _vmOperationCancellation?.IsCancellationRequested == false;
        VmQualifyStopButton.IsEnabled = !_closing && _vmOperation == VmOperation.SustainedQualification
            && _vmOperationCancellation?.IsCancellationRequested == false;
    }

    // Runs on the nonshown WPF smoke path, never opening a controller or reader.
    private bool VmOperationSmokeCheck()
    {
        foreach (VmOperation kind in new[]
        {
            VmOperation.ControllerStatus, VmOperation.PinnedTelemetryRead,
            VmOperation.ShortWatch, VmOperation.BoundPreflight,
            VmOperation.SupervisedPulse, VmOperation.SustainedQualification,
            VmOperation.SaveEvidence
        })
        {
            CancellationTokenSource? run = TryBeginVmOperation(kind);
            if (run is null || VmRefreshButton.IsEnabled || VmTelemetryButton.IsEnabled ||
                VmPulseButton.IsEnabled || VmPreflightButton.IsEnabled ||
                VmWatchStartButton.IsEnabled || VmQualifyStartButton.IsEnabled ||
                VmQualifySaveButton.IsEnabled ||
                TryBeginVmOperation(VmOperation.SupervisedPulse) is not null)
                return false;
            run.Cancel();
            UpdateVmOperationControls();
            if (TryBeginVmOperation(VmOperation.BoundPreflight) is not null) return false;
            EndVmOperation(kind, run);
            if (!VmRefreshButton.IsEnabled || !VmTelemetryButton.IsEnabled ||
                !VmPulseButton.IsEnabled || !VmPreflightButton.IsEnabled ||
                !VmWatchStartButton.IsEnabled || !VmQualifyStartButton.IsEnabled)
                return false;
        }
        return true;
    }
}
