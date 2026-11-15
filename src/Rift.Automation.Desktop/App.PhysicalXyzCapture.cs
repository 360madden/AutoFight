using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class App
{
    /// <summary>
    /// Explicit, bounded, read-only physical RIFT XYZ capture to a NEW historical
    /// JSON report. The operator moves manually; this command never sends input.
    /// A completed recording is NOT producer freshness or movement proof.
    /// </summary>
    private static async Task<int> RunPhysicalXyzTraceCaptureAsync(
        string pidText, string startTicksText, string secondsText, string outputPath)
    {
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || pid <= 0
            || !long.TryParse(startTicksText, NumberStyles.None, CultureInfo.InvariantCulture, out long startTicks)
            || startTicks <= 0 || startTicks > DateTime.MaxValue.Ticks
            || !int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || seconds is < 10 or > 120)
        {
            Console.Error.WriteLine(
                "Physical XYZ trace capture rejected: invalid_arguments (exact PID/start from --clients and 10..120 seconds required; no game read).");
            return 2;
        }

        // Validate and reject existing paths BEFORE process discovery or any game read.
        // SaveNew repeats these checks and enforces atomic no-overwrite at publish.
        try
        {
            PhysicalRiftPositionTraceExporter.CheckNewDestination(outputPath);
        }
        catch (Exception error) when (error is ArgumentException or IOException
            or UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            Console.Error.WriteLine("Physical XYZ trace capture rejected: invalid_or_existing_destination (no game read).");
            return 2;
        }

        // Unlike the older watch CLI, the recorder requires a fresh host inventory
        // preflight. Do not trust a stale PID or a process selected on another PC.
        try
        {
            ClientInventorySnapshot inventory = RiftClientInventory.Scan();
            RiftClientInfo[] exact = inventory.ErrorCode is null
                ? inventory.Clients.Where(client =>
                    client.ProcessId == pid &&
                    client.StartedAtUtc?.UtcTicks == startTicks &&
                    !string.IsNullOrWhiteSpace(client.ImagePath) &&
                    client.Status is not ("Exited" or "Unavailable"))
                    .Take(2).ToArray()
                : [];
            if (exact.Length != 1)
            {
                Console.Error.WriteLine(
                    "Physical XYZ trace capture rejected: exact_physical_process_not_available (no game read).");
                return 2;
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException
            or UnauthorizedAccessException or InvalidOperationException or SecurityException)
        {
            Console.Error.WriteLine(
                "Physical XYZ trace capture rejected: process_inventory_unavailable (no game read).");
            return 2;
        }

        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        PhysicalRiftPositionTraceCaptureResult result;
        var clock = Stopwatch.StartNew();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            recordType = "autofight-physical-xyz-trace-capture-start/1",
            processId = pid,
            processStartUtcTicks = startTicks,
            requestedSeconds = seconds,
            status = "read_only_recording_started",
            manualMovement = "optional_operator_action_only",
            producerFreshnessQualified = false,
            gameplayMovementProven = false,
            canAutomate = false
        }));

        try
        {
            result = await PhysicalRiftPositionTraceCaptureRunner.RunAsync(
                pid,
                startTicks,
                seconds,
                PhysicalRiftPositionReader.ReadPinnedAsync,
                () => clock.ElapsedMilliseconds,
                () => DateTimeOffset.UtcNow,
                (duration, cancellation) => Task.Delay(duration, cancellation),
                stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Console.Error.WriteLine("Physical XYZ trace capture interrupted: cancelled_no_complete_report.");
            return 2;
        }
        catch (Exception error) when (error is IOException or Win32Exception
            or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or SecurityException or NotSupportedException)
        {
            Console.Error.WriteLine("Physical XYZ trace capture interrupted: sampling_unavailable.");
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        // Caller explicitly requested this output pathname; an incomplete trace is
        // still useful historical diagnostics if at least one outcome exists. It is
        // NEVER described as a completed capture after cancellation or source loss.
        if (result.Report.TotalSamples == 0)
        {
            Console.Error.WriteLine("Physical XYZ trace capture incomplete: no_outcomes_to_save.");
            return 2;
        }
        try
        {
            PhysicalRiftPositionTraceExporter.SaveNew(outputPath, result.Report);
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException
            or IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or SecurityException)
        {
            // Never reveal arbitrary paths, native exception strings or report data.
            Console.Error.WriteLine("Physical XYZ trace capture failed: destination_unavailable_or_invalid.");
            return 2;
        }

        PhysicalRiftPositionTraceAssessment assessment =
            PhysicalRiftPositionTraceAssessor.Assess(result.Report);
        bool completed = result.CompletedWindow && !result.Cancelled;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            recordType = "autofight-physical-xyz-trace-capture-summary/1",
            status = completed ? "historical_recording_saved_full_duration"
                : "historical_recording_saved_incomplete",
            reportSavedNew = true,
            requestedSeconds = seconds,
            captureElapsedMilliseconds = clock.ElapsedMilliseconds,
            processId = pid,
            processStartUtcTicks = startTicks,
            totalSamples = result.Report.TotalSamples,
            retainedSamples = result.Report.RetainedSamples,
            candidateSamples = result.Report.CandidateSamples,
            rejectedSamples = result.Report.RejectedSamples,
            evictedSamples = result.Report.EvictedSamples,
            result.Attempts,
            result.CompletedWindow,
            result.Cancelled,
            result.TerminalReasonCode,
            assessmentState = assessment.State.ToString(),
            assessmentReasonCode = assessment.ReasonCode,
            beforeX = assessment.BeforeX,
            beforeY = assessment.BeforeY,
            beforeZ = assessment.BeforeZ,
            afterX = assessment.AfterX,
            afterY = assessment.AfterY,
            afterZ = assessment.AfterZ,
            assessment.Distance3D,
            passiveFreshnessQualified = false,
            gameplayMovementProven = false,
            canAutomate = false,
            evidenceNote = "Saved native coordinate-value history only; no independent producer freshness, caused movement, or input authority."
        }));

        // Exit 0 denotes recording and file publication, NOT a proven A/B move.
        return completed ? 0 : 2;
    }
}
