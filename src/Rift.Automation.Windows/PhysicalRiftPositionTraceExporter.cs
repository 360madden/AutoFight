using System.Text;

namespace Rift.Automation.Windows;

/// <summary>
/// Explicit user-invoked persistence of one historical, read-only physical XYZ trace.
/// Validates a bounded redacted report before touching disk, then creates a NEW .json file
/// atomically via a same-directory private staging file. Never replaces an existing file.
/// </summary>
public static class PhysicalRiftPositionTraceExporter
{
    public const int MaximumUtf8JsonBytes = 1_048_576;

    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    private static readonly HashSet<string> NativeReasonCodes = new(StringComparer.Ordinal)
    {
        "native_candidate",
        "native_identity_mismatch",
        "native_unavailable",
        "native_error",
        "native_acquisition_not_returned"
    };

    private static readonly HashSet<string> ComparisonReasonCodes = new(StringComparer.Ordinal)
    {
        "first_exact_identity_native_xyz_acquisition",
        "new_acquisition_equal_xyz_no_producer_freshness_claim",
        "observed_xyz_value_change_not_proven_gameplay_movement",
        "no_acquisition",
        "reset_no_baseline",
        "cancelled_reset_required",
        "bounded_observation_limit_reset_required",
        "negative_monotonic_acquisition_elapsed",
        "native_reader_process_or_build_identity_mismatch",
        "source_status_unavailable",
        "source_status_identitymismatch",
        "source_status_error",
        "native_candidate_missing_invalid_or_unpinned",
        "native_player_or_process_identity_changed",
        "nonadvancing_monotonic_acquisition_elapsed",
        "native_acquisition_gap_exceeded_limit_not_game_producer_staleness",
        "native_xyz_difference_nonfinite",
        "comparison_reason_redacted",
        "no_comparison_result"
    };

    /// <summary>
    /// Saves the immutable snapshot as a bounded redacted JSON file only if the named
    /// destination does not already exist. Does not start sampling or modify the game.
    /// </summary>
    /// <exception cref="ArgumentException">Destination is not an ordinary absolute JSON path.</exception>
    /// <exception cref="InvalidDataException">Report is empty, inconsistent or not this trace format.</exception>
    /// <exception cref="IOException">An existing destination, missing folder or filesystem error.</exception>
    public static void SaveNew(string selectedPath, PhysicalRiftPositionTraceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        // Report validation and size bounding MUST precede filesystem interaction.
        ValidateReport(report);
        string json = report.ToJson();
        int utf8Bytes = Utf8WithoutBom.GetByteCount(json);
        if (utf8Bytes is < 1 or > MaximumUtf8JsonBytes)
            throw new InvalidDataException("The physical XYZ trace exceeds its bounded JSON export size.");
        byte[] bytes = Utf8WithoutBom.GetBytes(json);

        string destination = ResolveOrdinaryJsonDestination(selectedPath);
        string parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("JSON destination has no parent directory.", nameof(selectedPath));
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("JSON destination directory does not exist.");

        string fileName = Path.GetFileName(destination);
        string staging = Path.Combine(parent,
            "." + fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        bool ownsStaging = false;
        try
        {
            using (var output = new FileStream(staging, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                ownsStaging = true;
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            // Same-directory rename is no-clobber. A racing writer is preserved intact.
            File.Move(staging, destination, overwrite: false);
            ownsStaging = false;
        }
        finally
        {
            if (ownsStaging)
            {
                // Only a uniquely named temp file created by THIS call may be removed.
                // Never delete, truncate or overwrite the user's requested destination.
                try { File.Delete(staging); }
                catch (IOException) { /* Do not disguise the original failure. */ }
                catch (UnauthorizedAccessException) { /* Do not disguise the original failure. */ }
            }
        }
    }

    private static string ResolveOrdinaryJsonDestination(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || selectedPath.Length > 32_000)
            throw new ArgumentException("Select an ordinary absolute JSON filename.", nameof(selectedPath));

        string slashNormalized = selectedPath.Replace('/', '\\');
        if (slashNormalized.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            slashNormalized.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            slashNormalized.StartsWith(@"\??\", StringComparison.Ordinal))
            throw new ArgumentException("Windows device namespace paths are not supported.", nameof(selectedPath));

        if (!Path.IsPathFullyQualified(selectedPath))
            throw new ArgumentException("An absolute JSON destination is required.", nameof(selectedPath));

        string destination = Path.GetFullPath(selectedPath);
        string canonical = destination.Replace('/', '\\');
        if (canonical.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            canonical.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            canonical.StartsWith(@"\??\", StringComparison.Ordinal))
            throw new ArgumentException("Windows device namespace paths are not supported.", nameof(selectedPath));

        // Only the drive designator's colon is legal. Other colons are NTFS
        // alternate streams, which could bypass the intended .json destination.
        int firstLegalColon = canonical.Length >= 2 &&
            char.IsAsciiLetter(canonical[0]) && canonical[1] == ':' ? 1 : -1;
        for (int index = 0; index < canonical.Length; index++)
        {
            if (canonical[index] == ':' && index != firstLegalColon)
                throw new ArgumentException("Alternate stream paths are not supported.", nameof(selectedPath));
        }

        string fileName = Path.GetFileName(destination);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(' ') || fileName.EndsWith('.') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            IsReservedDeviceFilename(fileName))
            throw new ArgumentException("Choose a normal .json filename.", nameof(selectedPath));

        return destination;
    }

    private static bool IsReservedDeviceFilename(string fileName)
    {
        string stem = fileName.Split('.')[0].TrimEnd(' ', '.');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;

        if (stem.Length != 4)
            return false;
        string prefix = stem[..3];
        return (prefix.Equals("COM", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("LPT", StringComparison.OrdinalIgnoreCase)) &&
            (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }

    internal static void ValidateReport(PhysicalRiftPositionTraceReport report)
    {
        if (!string.Equals(report.Schema, PhysicalRiftPositionTraceRecorder.EvidenceSchema,
                StringComparison.Ordinal) ||
            report.MaxEntries is < 1 or > PhysicalRiftPositionTraceRecorder.DefaultMaxEntries ||
            report.Samples is null ||
            report.RetainedSamples is < 1 or > PhysicalRiftPositionTraceRecorder.DefaultMaxEntries ||
            report.RetainedSamples > report.MaxEntries ||
            report.Samples.Count != report.RetainedSamples ||
            report.TotalSamples < report.RetainedSamples ||
            report.EvictedSamples != report.TotalSamples - report.RetainedSamples ||
            report.CandidateSamples < 0 || report.ChangedSamples < 0 ||
            report.UnavailableSamples < 0 || report.RejectedSamples < 0 ||
            (long)report.CandidateSamples + report.RejectedSamples != report.TotalSamples ||
            report.ChangedSamples > report.CandidateSamples ||
            report.UnavailableSamples > report.RejectedSamples ||
            report.ProcessId is null or <= 0 ||
            report.ProcessStartUtcTicks is null or <= 0 ||
            !((report.ExecutableSha256 is null && report.PlayerUnitKey is null) ||
              (string.Equals(report.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
                   StringComparison.OrdinalIgnoreCase) && report.PlayerUnitKey is > 0)) ||
            report.PassiveFreshnessQualified || report.GameplayMovementProven || report.CanAutomate)
            throw new InvalidDataException("Physical XYZ trace report is empty, inconsistent or untrusted.");

        int sequence = 0;
        foreach (PhysicalRiftPositionTraceEntry entry in report.Samples)
        {
            if (entry is null ||
                entry.Sequence <= sequence ||
                entry.ProcessId is not null && entry.ProcessId != report.ProcessId ||
                entry.ProcessStartUtcTicks is not null &&
                    entry.ProcessStartUtcTicks != report.ProcessStartUtcTicks ||
                entry.ExecutableSha256 is not null &&
                    !string.Equals(entry.ExecutableSha256, report.ExecutableSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                entry.PlayerUnitKey is not null && entry.PlayerUnitKey != report.PlayerUnitKey ||
                entry.ObservedProcessId is <= 0 || entry.ObservedProcessStartUtcTicks is <= 0 ||
                entry.ObservedPlayerUnitKey is 0 ||
                entry.CapturedAtUtc is { } acquisitionUtc && acquisitionUtc.Offset != TimeSpan.Zero ||
                !NativeReasonCodes.Contains(entry.NativeReasonCode) ||
                !ComparisonReasonCodes.Contains(entry.ComparisonReasonCode) ||
                entry.NativeReadElapsedMilliseconds is double readMs &&
                    (!double.IsFinite(readMs) || readMs is < 0 or > 15_000))
                throw new InvalidDataException("Physical XYZ trace contains an invalid source or entry.");

            if (entry.AcceptedForComparison)
            {
                if (entry.Disposition != PhysicalRiftPositionTraceDisposition.Accepted ||
                    entry.NativeStatus != PhysicalRiftPositionStatus.Candidate ||
                    entry.ComparisonStatus is not (PhysicalRiftPositionChangeStatus.Baseline or
                        PhysicalRiftPositionChangeStatus.Changed or
                        PhysicalRiftPositionChangeStatus.Unchanged) ||
                    report.ExecutableSha256 is null || report.PlayerUnitKey is null ||
                    entry.X is not float x || !float.IsFinite(x) ||
                    entry.Y is not float y || !float.IsFinite(y) ||
                    entry.Z is not float z || !float.IsFinite(z) ||
                    !FiniteOrNull(entry.AbsoluteDeltaX) ||
                    !FiniteOrNull(entry.AbsoluteDeltaY) ||
                    !FiniteOrNull(entry.AbsoluteDeltaZ) ||
                    !FiniteOrNull(entry.Distance3D))
                    throw new InvalidDataException("Physical XYZ trace contains an untrusted accepted XYZ.");
            }
            else if (entry.Disposition == PhysicalRiftPositionTraceDisposition.Accepted ||
                entry.X is not null || entry.Y is not null || entry.Z is not null ||
                entry.AbsoluteDeltaX is not null || entry.AbsoluteDeltaY is not null ||
                entry.AbsoluteDeltaZ is not null || entry.Distance3D is not null)
            {
                throw new InvalidDataException("Rejected physical XYZ trace entry contains position values.");
            }

            sequence = entry.Sequence;
        }
    }

    private static bool FiniteOrNull(double? value) =>
        value is null || double.IsFinite(value.Value);
}
