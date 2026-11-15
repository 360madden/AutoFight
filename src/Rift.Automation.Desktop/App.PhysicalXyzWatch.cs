using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class App
{
    /// <summary>
    /// Bounded, on-demand native position watch. This records current-build acquisition
    /// candidates, not producer-time freshness or action authorization.
    /// </summary>
    private static async Task<int> RunPhysicalXyzWatchAsync(
        string pidText, string startTicksText, string secondsText)
    {
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || pid <= 0
            || !long.TryParse(startTicksText, NumberStyles.None, CultureInfo.InvariantCulture, out long startTicks)
            || startTicks <= 0 || startTicks > DateTime.MaxValue.Ticks
            || !int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || seconds is < 2 or > 30)
        {
            Console.Error.WriteLine(
                "Physical XYZ watch requires exact PID and process-start UTC ticks from --clients, and 2..30 seconds. No game access attempted.");
            return 2;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter<PhysicalRiftPositionStatus>() }
        };
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        int attempts = 0, candidates = 0, unavailable = 0, identityFaults = 0, errors = 0;
        bool cancelled = false;
        var watch = Stopwatch.StartNew();
        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        TimeSpan nextDue = TimeSpan.Zero;

        try
        {
            while (watch.Elapsed < duration && !stop.IsCancellationRequested)
            {
                TimeSpan remaining = duration - watch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;
                if (nextDue > watch.Elapsed)
                {
                    TimeSpan untilDue = nextDue - watch.Elapsed;
                    await Task.Delay(untilDue < remaining ? untilDue : remaining, stop.Token);
                    if (watch.Elapsed >= duration)
                        break;
                }
                remaining = duration - watch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;
                int index = ++attempts;
                var sampleStartedAtUtc = DateTimeOffset.UtcNow;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                // Both the individual read and the overall watch have deadlines.
                TimeSpan perSampleLimit = TimeSpan.FromSeconds(15);
                deadline.CancelAfter(remaining < perSampleLimit ? remaining : perSampleLimit);

                try
                {
                    PhysicalRiftPositionResult sample = await PhysicalRiftPositionReader.ReadPinnedAsync(
                        pid, startTicks, deadline.Token);

                    switch (sample.Status)
                    {
                        case PhysicalRiftPositionStatus.Candidate:
                            candidates++;
                            break;
                        case PhysicalRiftPositionStatus.Unavailable:
                            unavailable++;
                            break;
                        case PhysicalRiftPositionStatus.IdentityMismatch:
                            identityFaults++;
                            break;
                        default:
                            errors++;
                            break;
                    }

                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        recordType = "autofight-physical-xyz-watch-sample/1",
                        index,
                        sampledAtUtc = sample.CapturedAtUtc,
                        sampleStartedAtUtc,
                        watchElapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                        sample,
                        newlyAcquiredNativeCandidate = sample.Status == PhysicalRiftPositionStatus.Candidate,
                        passiveFreshnessQualified = false,
                        sustainedQualification = false,
                        canAutomate = false
                    }, jsonOptions));

                    if (sample.Status == PhysicalRiftPositionStatus.IdentityMismatch)
                        break;
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    if (watch.Elapsed >= duration)
                        break; // Normal watch boundary, not an unavailable source.
                    unavailable++;
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        recordType = "autofight-physical-xyz-watch-sample/1",
                        index,
                        sampledAtUtc = DateTimeOffset.UtcNow,
                        sampleStartedAtUtc,
                        status = "Unavailable",
                        reason = "individual_sample_15_second_deadline",
                        newlyAcquiredNativeCandidate = false,
                        passiveFreshnessQualified = false,
                        sustainedQualification = false,
                        canAutomate = false
                    }, jsonOptions));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    or InvalidOperationException or ArgumentException or Win32Exception
                    or SecurityException or NotSupportedException)
                {
                    errors++;
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        recordType = "autofight-physical-xyz-watch-sample/1",
                        index,
                        sampledAtUtc = DateTimeOffset.UtcNow,
                        sampleStartedAtUtc,
                        status = "Error",
                        reason = error.GetType().Name,
                        newlyAcquiredNativeCandidate = false,
                        passiveFreshnessQualified = false,
                        sustainedQualification = false,
                        canAutomate = false
                    }, jsonOptions));
                }

                // A slow read never produces a burst of catch-up samples. Reacquire
                // no sooner than one second after completion; old XYZ is never reused.
                nextDue = watch.Elapsed + TimeSpan.FromSeconds(1);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        cancelled |= stop.IsCancellationRequested;
        bool accepted = !cancelled && identityFaults == 0 && candidates >= 2;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            recordType = "autofight-physical-xyz-watch-summary/1",
            processId = pid,
            processStartUtcTicks = startTicks,
            requestedSeconds = seconds,
            elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
            attempts,
            candidateSamples = candidates,
            unavailableSamples = unavailable,
            identityFaults,
            errorSamples = errors,
            cancelled,
            status = accepted ? "candidate-sequence-observed" : "unavailable",
            passiveFreshnessQualified = false,
            sustainedQualification = false,
            canAutomate = false,
            reason = accepted
                ? "at_least_two_current_build_native_reacquisitions_not_producer_freshness"
                : cancelled ? "cancelled"
                : identityFaults > 0 ? "process_or_build_identity_changed"
                : "fewer_than_two_verified_native_candidates"
        }, jsonOptions));

        return accepted ? 0 : 2;
    }
}
