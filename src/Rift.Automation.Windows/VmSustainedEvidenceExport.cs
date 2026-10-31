using System.Text.Json;

namespace Rift.Automation.Windows;

/// <summary>A completed observation's immutable, redacted, historical result.</summary>
public sealed record VmSustainedEvidence(
    string ApplicationVersion, string EvidenceOrigin,
    DateTimeOffset StartedAtUtc, DateTimeOffset FinishedAtUtc,
    VmSustainedFeedbackReport Report);

/// <summary>
/// No reader, controller or input dependency. Only app-defined aggregate fields
/// may cross this user-triggered export boundary; the file is never an action permit.
/// </summary>
public static class VmSustainedEvidenceExporter
{
    public const int SchemaVersion = 1;
    public const string ArtifactType = "rift-sustained-telemetry-evidence";
    public const string PinnedFileOrigin = "pinned-local-receiver";
    public const string SyntheticOrigin = "synthetic-fixture";
    private const int MaximumJsonBytes = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static VmSustainedEvidence Capture(
        VmSustainedFeedbackReport report, string expectedLab,
        string applicationVersion, DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc, bool stopRequested = false,
        string origin = PinnedFileOrigin)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (expectedLab is not ("lab1" or "lab2") ||
            !string.Equals(report.Lab, expectedLab, StringComparison.Ordinal))
            throw new ArgumentException("Result identity does not match the requested lab.");
        if (origin is not (PinnedFileOrigin or SyntheticOrigin))
            throw new ArgumentException("Evidence origin is not supported.");
        if (applicationVersion.Length is < 5 or > 24 ||
            applicationVersion.Split('.').Length != 3 ||
            !Version.TryParse(applicationVersion, out _))
            throw new ArgumentException("Invalid application version.");
        if (startedAtUtc == default || finishedAtUtc == default ||
            startedAtUtc.Offset != TimeSpan.Zero || finishedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Run timestamps must be explicit UTC values.");

        // Stop wins over any late success. Preserve measured aggregates, but neither
        // the effective displayed result nor its export may claim a pass.
        VmSustainedFeedbackReport effective = stopRequested
            ? report with
            {
                Completed = false,
                SustainedTelemetryEvidencePassed = false,
                Reason = "cancelled"
            }
            : report;
        Validate(effective);
        return new(applicationVersion, origin, startedAtUtc, finishedAtUtc, effective);
    }

    public static byte[] Serialize(VmSustainedEvidence evidence, DateTimeOffset exportedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Validate(evidence.Report);
        if (evidence.EvidenceOrigin is not (PinnedFileOrigin or SyntheticOrigin) ||
            evidence.ApplicationVersion.Length is < 5 or > 24 ||
            evidence.ApplicationVersion.Split('.').Length != 3 ||
            !Version.TryParse(evidence.ApplicationVersion, out _) ||
            evidence.StartedAtUtc == default || evidence.FinishedAtUtc == default ||
            evidence.StartedAtUtc.Offset != TimeSpan.Zero ||
            evidence.FinishedAtUtc.Offset != TimeSpan.Zero ||
            exportedAtUtc == default || exportedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid evidence envelope.");

        var envelope = new
        {
            SchemaVersion,
            ArtifactType,
            evidence.ApplicationVersion,
            evidence.EvidenceOrigin,
            evidence.StartedAtUtc,
            evidence.FinishedAtUtc,
            ExportedAtUtc = exportedAtUtc,
            HistoricalOnly = true,
            CurrentActionPermit = false,
            evidence.Report
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (json.Length > MaximumJsonBytes)
            throw new InvalidOperationException("Evidence serialization exceeded its size limit.");
        return json;
    }

    /// <summary>
    /// A same-directory staged, no-overwrite save. A completed destination is
    /// never exposed before all bytes have been written; cross-filesystem and
    /// power-loss atomicity are not claimed.
    /// </summary>
    public static void SaveNew(string selectedPath, byte[] json, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedPath);
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length is < 2 or > MaximumJsonBytes)
            throw new ArgumentException("Evidence JSON is empty or oversized.");
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(selectedPath) ||
            selectedPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            selectedPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("An ordinary, absolute file path is required.");

        string destination = Path.GetFullPath(selectedPath);
        string fileName = Path.GetFileName(destination);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(':', StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(stem) || IsReservedName(stem))
            throw new ArgumentException("An ordinary JSON filename is required.");

        string parent = Path.GetDirectoryName(destination) ??
            throw new ArgumentException("A parent directory is required.");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("The destination directory does not exist.");

        // No final-path precheck can enforce no-clobber. File.Move(false) does.
        string staging = Path.Combine(parent,
            "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        bool ownsStaging = false;
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                ownsStaging = true;
                token.ThrowIfCancellationRequested();
                file.Write(json);
                file.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(staging, destination, overwrite: false);
            ownsStaging = false;
        }
        finally
        {
            if (ownsStaging)
            {
                try { File.Delete(staging); }
                catch (IOException) { /* Cleanup failure never deletes the destination. */ }
                catch (UnauthorizedAccessException) { /* Report the original write outcome. */ }
            }
        }
    }

    private static bool IsReservedName(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL" ||
            (upper.Length == 4 && upper[3] is >= '1' and <= '9' &&
             (upper.StartsWith("COM", StringComparison.Ordinal) ||
              upper.StartsWith("LPT", StringComparison.Ordinal)));
    }

    private static void Validate(VmSustainedFeedbackReport report)
    {
        if (report.Mode != "ReadOnlySustainedTelemetryEvidence" ||
            report.PolicyId != VmSustainedFeedbackWindow.PolicyId ||
            report.Lab is not ("lab1" or "lab2") ||
            report.RequestedDurationMs != VmSustainedFeedbackWindow.RequiredDurationMs ||
            report.PollIntervalMs != VmSustainedFeedbackWindow.PollIntervalMs ||
            report.ForbiddenGapFromMs != VmSustainedFeedbackWindow.ForbiddenGapFromMs)
            throw new ArgumentException("Evidence policy or identity is unsupported.");
        if (!AllowedReason(report.Reason))
            throw new ArgumentException("Evidence reason is unsupported.");
        if (report.ObservedDurationMs < 0 || report.Polls < 0 || report.NewSamples < 0 ||
            report.Repeated < 0 || report.Unavailable < 0 || report.SessionChanges < 0 ||
            report.Reordered < 0 || report.Recovered < 0 || report.LongestNoAdvanceMs < 0 ||
            report.SinceLastAdvanceMs < 0 ||
            report.NewSamples > report.Polls || report.Repeated > report.Polls ||
            report.Unavailable > report.Polls || report.SessionChanges > report.Polls ||
            report.Reordered > report.Polls || report.Recovered > report.NewSamples ||
            report.SinceLastAdvanceMs > report.LongestNoAdvanceMs ||
            (report.Completed &&
             report.ObservedDurationMs < VmSustainedFeedbackWindow.RequiredDurationMs))
            throw new ArgumentException("Evidence aggregates are inconsistent.");
        if (report.SustainedTelemetryEvidencePassed != (report.Reason == "passed") ||
            (report.SustainedTelemetryEvidencePassed &&
             (!report.Completed ||
              report.NewSamples < 2 || report.SessionChanges != 0 ||
              report.Reordered != 0 ||
              report.LongestNoAdvanceMs >= VmSustainedFeedbackWindow.ForbiddenGapFromMs ||
              report.SinceLastAdvanceMs >= VmSustainedFeedbackWindow.ForbiddenGapFromMs)))
            throw new ArgumentException("Evidence outcome is inconsistent.");
    }

    private static bool AllowedReason(string reason) => reason is
        "passed" or "cancelled" or "incomplete_window" or "insufficient_progress" or
        "no_advance_limit_exceeded" or "terminal_source_unavailable" or
        "source_identity_changed" or "source_session_changed" or "source_ordering_conflict" or
        "invalid_ordering_evidence" or "invalid_initial_clock" or "initial_lab_mismatch" or
        "initial_source_unavailable" or "lab_mismatch" or "invalid_source_interruption" or
        "reader_not_running" or "identity_mismatch" or "source_conflict" or "file_unavailable" or
        "invalid_source_pin" or "invalid_receipt_or_pin" or "invalid_sequence" or
        "invalid_source_timing" or "source_clock_or_player_invalid" or
        "source_expired" or "stale_or_clock_uncertain" or "source_not_fresh" or
        "scan_not_complete" or "schema_invalid" or "size_limit" or
        "player_unavailable" or "coordinates_unavailable" or "source_unavailable" or
        "read_error" or "clock_regression" or "poll_limit_exceeded" or "invalid_lab" or
        "interrupted";
}
