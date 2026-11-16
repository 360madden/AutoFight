using System.Diagnostics;
using Rift.Automation.Guest.Application;
using Rift.Automation.Guest.Domain;
using Rift.Automation.Guest.Infrastructure;

namespace Rift.Automation.Guest.Live;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Explicit entry mode: an accidental launch never starts memory scans.
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine(
                "AutoFight GuestReader: --live requires autofight-guest-reader.json beside the executable.");
            return 0;
        }

        if (args.Length != 1 || args[0] != "--live")
        {
            Console.Error.WriteLine("Choose --live explicitly, or --help for usage.");
            return 2;
        }

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, LiveConfiguration.FileName)))
        {
            Console.Error.WriteLine("Guest reader configuration is missing. No scan started.");
            return 2;
        }

        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            Console.Error.WriteLine("AutoFight GuestReader requires 64-bit Windows.");
            return 2;
        }

        LiveConfiguration configuration;
        try
        {
            configuration = LiveConfiguration.LoadBesideExecutable();
            configuration.Validate();
            if (configuration.Delivery is null)
                throw new InvalidDataException(
                    "Authenticated private-host delivery is required for --live.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Configuration error: {exception.Message}. No scan started.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(configuration.DurationSeconds));
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        var timeProvider = TimeProvider.System;
        var launchedAt = timeProvider.GetTimestamp();
        var instanceId = Guid.NewGuid();
        var nonce = NewNonce();
        PrintSessionIdentity(instanceId, nonce);
        PrintBindingInstruction(nonce);
        var outputPath = configuration.ResolveOutputPath();
        using var delivery = new LiveDeliveryClient(configuration.Delivery);
        LiveAddonFreshnessTracker? tracker = null;
        DiscoveredTarget? attachedTarget = null;
        LiveScanResult lastScan = EmptyScan("not-started", "No scan has completed.");
        LiveAddonSelection lastSelection = new(LiveAddonSelectionStatus.IncompleteScan, null, 0, 0, "scan-not-started");
        var lastFullScanStartedAt = launchedAt;
        FastObservationSession? fastSession = null;
        FastObservationReport? lastFastObservation = null;
        FastObservationAnchorDiagnostic? fastObservationDiagnostic = null;
        long outerSequence = 0;
        var failed = false;

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var scanStartedAt = timeProvider.GetTimestamp();
                var discovery = await TargetDiscovery.DiscoverAsync(
                    configuration.ProcessName,
                    nonce,
                    cancellation.Token,
                    configuration.TargetProcessId);

                if (discovery.Target is null)
                {
                    var hadAttachment = tracker is not null || attachedTarget is not null;
                    lastScan = EmptyScan("target-unavailable", discovery.Reason);
                    lastSelection = new(LiveAddonSelectionStatus.IncompleteScan, null, 0, 0, discovery.Reason);
                    LiveAddonFreshness? freshness = null;
                    if (tracker is not null)
                    {
                        var invalidating = new LiveAddonSelection(
                            LiveAddonSelectionStatus.Conflict,
                            null,
                            0,
                            0,
                            discovery.Reason ?? "target-unavailable");
                        freshness = tracker.Observe(tracker.Identity, invalidating, scanStartedAt);
                    }
                    await PublishUnavailableAsync(
                        configuration,
                        delivery,
                        instanceId,
                        nonce,
                        ++outerSequence,
                        launchedAt,
                        outputPath,
                        scanStartedAt,
                        freshness,
                        lastScan,
                        lastSelection,
                        discovery.Reason ?? "target-unavailable",
                        timeProvider,
                        cancellation.Token);
                    tracker = null;
                    attachedTarget = null;
                    fastSession = null;
                    lastFastObservation = null;
                    fastObservationDiagnostic = null;
                    if (hadAttachment)
                    {
                        nonce = NewNonce();
                        PrintSessionIdentity(instanceId, nonce);
                        PrintBindingInstruction(nonce);
                    }
                    await DelayAsync(configuration.PollIntervalMilliseconds, cancellation.Token);
                    continue;
                }

                if (attachedTarget is null || !SameProcess(attachedTarget, discovery.Target))
                {
                    if (attachedTarget is not null)
                    {
                        nonce = NewNonce();
                        PrintSessionIdentity(instanceId, nonce);
                        PrintBindingInstruction(nonce);
                        discovery = await TargetDiscovery.DiscoverAsync(
                            configuration.ProcessName,
                            nonce,
                            cancellation.Token,
                            configuration.TargetProcessId);
                        if (discovery.Target is null)
                            continue;
                    }
                    attachedTarget = discovery.Target;
                    tracker = new LiveAddonFreshnessTracker(
                        attachedTarget.Identity,
                        new LiveFreshnessOptions
                        {
                            FreshnessTimeout = TimeSpan.FromSeconds(configuration.FreshnessTimeoutSeconds),
                            MaxClockSkew = TimeSpan.FromSeconds(1)
                        },
                        timeProvider);
                    fastSession = null;
                    lastFastObservation = null;
                    fastObservationDiagnostic = null;
                }

                var freshnessOptions = new LiveFreshnessOptions
                {
                    FreshnessTimeout = TimeSpan.FromSeconds(configuration.FreshnessTimeoutSeconds),
                    MaxClockSkew = TimeSpan.FromSeconds(1)
                };
                LiveAddonFreshness freshnessResult;
                TargetVitalsCandidateDiagnostic? targetVitalsDiagnostic = null;
                TargetDiscoveryResult after;
                if (fastSession is not null && fastSession.CanObserve(attachedTarget, scanStartedAt, out _))
                {
                    var scopedScan = await Task.Run(
                        () => WindowsMemoryScanner.ScanCandidateRegions(
                            attachedTarget,
                            nonce,
                            fastSession.Regions,
                            FastObservationSession.MaximumRegionBytes,
                            TimeSpan.FromSeconds(Math.Min(2, configuration.MaxScanSeconds)),
                            cancellation.Token),
                        cancellation.Token);
                    var scopedCompletedAt = timeProvider.GetTimestamp();
                    after = await TargetDiscovery.DiscoverAsync(
                        configuration.ProcessName,
                        nonce,
                        cancellation.Token,
                        configuration.TargetProcessId);
                    var scopedTargetStable = after.Target is not null && SameProcess(attachedTarget, after.Target);
                    targetVitalsDiagnostic = TargetVitalsCandidateReporting.Build(scopedScan, nonce, scopedTargetStable);
                    var scopedSelection = !scopedTargetStable
                        ? new LiveAddonSelection(
                            LiveAddonSelectionStatus.Conflict, null, 0, 0,
                            after.Reason ?? "target-identity-changed-during-scoped-scan")
                        : LiveAddonCandidateSelector.Select(scopedScan.Candidates, nonce, scopedScan.Completed);
                    var scopedTarget = after.Target ?? attachedTarget;
                    lastFastObservation = fastSession.Observe(
                        scopedTarget,
                        scopedScan,
                        scopedSelection,
                        scanStartedAt,
                        scopedCompletedAt,
                        out var canContinue);
                    if (!canContinue)
                        fastSession = null;
                    freshnessResult = scopedTargetStable
                        ? tracker!.GetCurrent()
                        : tracker!.Observe(
                            after.Target?.Identity ?? attachedTarget.Identity,
                            scopedSelection,
                            scanStartedAt,
                            scopedCompletedAt);
                    if (freshnessResult.Status == LiveFreshnessStatus.Invalidated)
                        targetVitalsDiagnostic = null;
                }
                else
                {
                    lastFullScanStartedAt = scanStartedAt;
                    lastScan = await Task.Run(
                        () => WindowsMemoryScanner.Scan(
                            attachedTarget,
                            nonce,
                            configuration.MaxScanBytes,
                            TimeSpan.FromSeconds(configuration.MaxScanSeconds),
                            cancellation.Token,
                            () => lastFullScanStartedAt = scanStartedAt = timeProvider.GetTimestamp()),
                        cancellation.Token);
                    var scanCompletedAt = timeProvider.GetTimestamp();
                    after = await TargetDiscovery.DiscoverAsync(
                        configuration.ProcessName,
                        nonce,
                        cancellation.Token,
                        configuration.TargetProcessId);
                    targetVitalsDiagnostic = TargetVitalsCandidateReporting.Build(lastScan, nonce,
                        after.Target is not null && SameProcess(attachedTarget, after.Target));
                    lastSelection = after.Target is null || !SameProcess(attachedTarget, after.Target)
                        ? new LiveAddonSelection(
                            LiveAddonSelectionStatus.Conflict, null, 0, 0,
                            after.Reason ?? "target-identity-changed-during-scan")
                        : LiveAddonCandidateSelector.Select(lastScan.Candidates, nonce, lastScan.Completed);
                    freshnessResult = tracker!.Observe(
                        after.Target?.Identity ?? attachedTarget.Identity,
                        lastSelection,
                        scanStartedAt,
                        scanCompletedAt);
                    if (freshnessResult.Status == LiveFreshnessStatus.Invalidated)
                        targetVitalsDiagnostic = null;
                    lastFastObservation = null;
                    fastObservationDiagnostic = null;
                    fastSession = after.Target is not null && SameProcess(attachedTarget, after.Target) &&
                        FastObservationSession.TryCreate(
                            after.Target,
                            lastScan,
                            lastSelection,
                            scanStartedAt,
                            scanCompletedAt,
                            freshnessOptions,
                            timeProvider,
                            out var createdFastSession,
                            out _,
                            out fastObservationDiagnostic)
                        ? createdFastSession
                        : null;
                }
                var sequence = ++outerSequence;
                var beforeDelivery = CreateSnapshot(
                    instanceId,
                    nonce,
                    sequence,
                    launchedAt,
                    "running",
                    null,
                    attachedTarget,
                    tracker,
                    freshnessResult,
                    lastSelection,
                    lastScan,
                    lastFullScanStartedAt,
                    configuration,
                    delivery.State,
                    timeProvider,
                    lastFastObservation,
                    fastObservationDiagnostic,
                    targetVitalsDiagnostic);
                await delivery.DeliverAsync(beforeDelivery, cancellation.Token);
                var afterDelivery = CreateSnapshot(
                    instanceId,
                    nonce,
                    sequence,
                    launchedAt,
                    "running",
                    null,
                    attachedTarget,
                    tracker,
                    tracker.GetCurrent(),
                    lastSelection,
                    lastScan,
                    lastFullScanStartedAt,
                    configuration,
                    delivery.State,
                    timeProvider,
                    lastFastObservation,
                    fastObservationDiagnostic,
                    targetVitalsDiagnostic);
                await LiveOutput.WriteAtomicAsync(outputPath, afterDelivery, cancellation.Token);
                PrintStatus(afterDelivery);

                if (freshnessResult.Status == LiveFreshnessStatus.Invalidated)
                {
                    tracker = null;
                    attachedTarget = null;
                    fastSession = null;
                    lastFastObservation = null;
                    fastObservationDiagnostic = null;
                    nonce = NewNonce();
                    PrintSessionIdentity(instanceId, nonce);
                    PrintBindingInstruction(nonce);
                }
                await DelayAsync(configuration.PollIntervalMilliseconds, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failed = true;
            Console.Error.WriteLine($"Live reader failed: {exception.Message}");
        }
        finally
        {
            var sequence = ++outerSequence;
            var termination = CreateTerminationSnapshot(
                instanceId,
                nonce,
                sequence,
                launchedAt,
                attachedTarget,
                tracker,
                lastSelection,
                lastScan,
                configuration,
                delivery.State,
                timeProvider);
            try
            {
                using var terminationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await delivery.DeliverAsync(termination, terminationTimeout.Token);
                termination = termination with { Delivery = delivery.State };
                await LiveOutput.WriteAtomicAsync(outputPath, termination, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Termination report failed: {exception.Message}");
            }
            Console.WriteLine("Rift.Automation.Guest.Live stopped. Prior observations must not be treated as current.");
        }
        return failed ? 1 : 0;
    }

    private static LiveReaderSnapshot CreateSnapshot(
        Guid instanceId,
        string nonce,
        long sequence,
        long launchedAt,
        string readerStatus,
        string? readerReason,
        DiscoveredTarget target,
        LiveAddonFreshnessTracker tracker,
        LiveAddonFreshness freshness,
        LiveAddonSelection selection,
        LiveScanResult scan,
        long scanStartedAt,
        LiveConfiguration configuration,
        LiveDeliveryReport delivery,
        TimeProvider timeProvider,
        FastObservationReport? fastObservation,
        FastObservationAnchorDiagnostic? fastObservationDiagnostic,
        TargetVitalsCandidateDiagnostic? targetVitalsDiagnostic)
    {
        var publishedAt = timeProvider.GetTimestamp();
        return new(
            1,
            instanceId,
            nonce,
            sequence,
            DateTimeOffset.UtcNow.ToString("O"),
            timeProvider.GetElapsedTime(launchedAt, publishedAt).TotalMilliseconds,
            readerStatus,
            readerReason,
            TargetReport(target),
            LiveOutput.CreateRiftReader2Report(
                freshness,
                selection,
                scan,
                scanStartedAt,
                publishedAt,
                TimeSpan.FromSeconds(configuration.FreshnessTimeoutSeconds),
                timeProvider),
            delivery)
        {
            FastObservation = fastObservation is null
                ? null
                : LiveOutput.StampFastObservation(fastObservation, publishedAt, timeProvider),
            FastObservationDiagnostic = fastObservationDiagnostic,
            TargetVitalsCandidate = targetVitalsDiagnostic
        };
    }

    private static async Task PublishUnavailableAsync(
        LiveConfiguration configuration,
        LiveDeliveryClient delivery,
        Guid instanceId,
        string nonce,
        long sequence,
        long launchedAt,
        string outputPath,
        long scanStartedAt,
        LiveAddonFreshness? freshness,
        LiveScanResult scan,
        LiveAddonSelection selection,
        string reason,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var identity = freshness?.Identity ?? new LiveAttachmentIdentity(1, 1, new string('0', 64), nonce);
        var unavailable = freshness ?? new LiveAddonFreshness(
            LiveFreshnessStatus.Unavailable,
            identity,
            null,
            null,
            null,
            null,
            reason);
        LiveReaderSnapshot Build(LiveDeliveryReport deliveryState)
        {
            var now = timeProvider.GetTimestamp();
            return new(
                1,
                instanceId,
                nonce,
                sequence,
                DateTimeOffset.UtcNow.ToString("O"),
                timeProvider.GetElapsedTime(launchedAt, now).TotalMilliseconds,
                "running",
                reason,
                null,
                LiveOutput.CreateRiftReader2Report(
                    unavailable,
                    selection,
                    scan,
                    scanStartedAt,
                    now,
                    TimeSpan.FromSeconds(configuration.FreshnessTimeoutSeconds),
                    timeProvider,
                    "unavailable",
                    reason),
                deliveryState);
        }
        await delivery.DeliverAsync(Build(delivery.State), cancellationToken);
        await LiveOutput.WriteAtomicAsync(outputPath, Build(delivery.State), cancellationToken);
    }

    private static LiveReaderSnapshot CreateTerminationSnapshot(
        Guid instanceId,
        string nonce,
        long sequence,
        long launchedAt,
        DiscoveredTarget? target,
        LiveAddonFreshnessTracker? tracker,
        LiveAddonSelection selection,
        LiveScanResult scan,
        LiveConfiguration configuration,
        LiveDeliveryReport delivery,
        TimeProvider timeProvider)
    {
        var now = timeProvider.GetTimestamp();
        var identity = tracker?.Identity ?? new LiveAttachmentIdentity(1, 1, new string('0', 64), nonce);
        var freshness = tracker?.GetCurrent() ?? new LiveAddonFreshness(
            LiveFreshnessStatus.Unavailable,
            identity,
            null,
            null,
            null,
            null,
            "reader-terminated");
        return new(
            1,
            instanceId,
            nonce,
            sequence,
            DateTimeOffset.UtcNow.ToString("O"),
            timeProvider.GetElapsedTime(launchedAt, now).TotalMilliseconds,
            "terminated",
            "reader-terminated; prior observations expire no later than the reported freshness timeout",
            target is null ? null : TargetReport(target),
            LiveOutput.CreateRiftReader2Report(
                freshness,
                selection,
                scan,
                now,
                now,
                TimeSpan.FromSeconds(configuration.FreshnessTimeoutSeconds),
                timeProvider,
                "unavailable",
                "reader-terminated"),
            delivery);
    }

    private static LiveTargetReport TargetReport(DiscoveredTarget target) => new(
        target.Identity.ProcessId,
        target.Identity.ProcessStartUtcTicks,
        target.ExecutablePath,
        target.Identity.ExecutableSha256,
        target.Identity.ReaderSessionNonce);

    private static bool SameProcess(DiscoveredTarget left, DiscoveredTarget right) =>
        left.Identity.ProcessId == right.Identity.ProcessId &&
        left.Identity.ProcessStartUtcTicks == right.Identity.ProcessStartUtcTicks &&
        string.Equals(left.Identity.ExecutableSha256, right.Identity.ExecutableSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);

    private static LiveScanResult EmptyScan(string status, string? reason) =>
        new(false, status, 0, 0, 0, TimeSpan.Zero, [], 0, 0, reason);

    private static string NewNonce() => Guid.NewGuid().ToString("N");

    // Token-free coordination signal emitted before the first memory scan and on nonce rotation.
    private static void PrintSessionIdentity(Guid instanceId, string nonce)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType = "autofight-reader-session-v1",
            instanceId,
            nonce
        }));
    }

    private static void PrintBindingInstruction(string nonce)
    {
        Console.WriteLine();
        foreach (var line in BindingInstructions(nonce))
            Console.WriteLine(line);
        Console.WriteLine();
    }

    internal static IReadOnlyList<string> BindingInstructions(string nonce) =>
        new List<string>
        {
            "============================================================",
            "If this addon load is already bound to another reader, enter /reloadui and wait for it to finish first.",
            "Then enter this command locally in RIFT chat:",
            $"/afbind {nonce}",
            "This challenge is valid only for this reader attachment.",
            "============================================================"
        };

    private static void PrintStatus(LiveReaderSnapshot snapshot)
    {
        var fast = snapshot.FastObservation is null
            ? string.Empty
            : $" fastScan={snapshot.FastObservation.Scan.Status}" +
              $" fastFreshness={snapshot.FastObservation.FreshnessStatus}" +
              $" fastAgeMs={snapshot.FastObservation.LastAdvanceAgeMilliseconds?.ToString("F0") ?? "-"}";
        Console.WriteLine(
            $"{snapshot.CapturedAtUtc} scan={snapshot.RiftReader2.Scan.Status} " +
            $"selection={snapshot.RiftReader2.SelectionStatus} freshness={snapshot.RiftReader2.FreshnessStatus} " +
            $"ageMs={snapshot.RiftReader2.LastAdvanceAgeMilliseconds?.ToString("F0") ?? "-"}" +
            $"{fast} delivery={snapshot.Delivery.Status}");
    }

    private static async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
