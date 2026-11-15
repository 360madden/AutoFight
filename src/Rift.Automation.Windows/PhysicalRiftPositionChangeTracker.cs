namespace Rift.Automation.Windows;

/// <summary>
/// Classification of an observed native position comparison. "StaleAcquisitionGap"
/// means the caller did not provide another timely native acquisition; it says
/// nothing about the game's internal position writer or producer freshness.
/// </summary>
public enum PhysicalRiftPositionChangeStatus
{
    Baseline,
    Unchanged,
    Changed,
    Unavailable,
    InvalidCandidate,
    IdentityChanged,
    OutOfOrder,
    StaleAcquisitionGap,
    Cancelled,
    LimitReached
}

/// <summary>
/// Bounded deterministic comparison evidence. Changed describes observed coordinate
/// value differences, not a proven gameplay movement, action or producer update.
/// </summary>
public sealed record PhysicalRiftPositionChangeSnapshot(
    PhysicalRiftPositionChangeStatus Status,
    string Reason,
    bool HasBaseline,
    int Observations,
    int CandidateSamples,
    int ChangedSamples,
    int UnchangedSamples,
    int Invalidations,
    int? ProcessId,
    long? ProcessStartUtcTicks,
    string? ExecutableSha256,
    ulong? PlayerUnitKey,
    long? MonotonicElapsedMilliseconds,
    double? AbsoluteDeltaX,
    double? AbsoluteDeltaY,
    double? AbsoluteDeltaZ,
    double? Distance3D)
{
    public bool ProducerFreshnessQualified => false;
    public bool SustainedQualification => false;
    public bool CanAutomate => false;
}

/// <summary>
/// Single-owner, pure-memory comparison of independently sampled native XYZ candidates.
/// Caller supplies a monotonic elapsed time (Stopwatch.ElapsedMilliseconds), NOT the
/// per-sample PhysicalRiftPositionResult.ElapsedMilliseconds, which may decrease.
/// Retains at most one baseline; no process access, automation, or background timers.
/// </summary>
public sealed class PhysicalRiftPositionChangeTracker
{
    private const double MaxIndividualReadMilliseconds = 15_000;
    private readonly int maxGapMilliseconds;
    private readonly int maxObservations;
    private Identity? identity;
    private (float X, float Y, float Z) previous;
    private long previousMonotonicMs;
    private int observations;
    private int candidates;
    private int changed;
    private int unchanged;
    private int invalidations;
    private bool cancelled;

    private sealed record Identity(int ProcessId, long ProcessStartUtcTicks, string ExecutableSha256, ulong PlayerUnitKey);

    public PhysicalRiftPositionChangeTracker(int maxGapMilliseconds = 4500, int maxObservations = 1024)
    {
        if (maxGapMilliseconds is < 1 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(maxGapMilliseconds));
        if (maxObservations is < 2 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(maxObservations));
        this.maxGapMilliseconds = maxGapMilliseconds;
        this.maxObservations = maxObservations;
        LastSnapshot = Create(PhysicalRiftPositionChangeStatus.Unavailable,
            "no_acquisition", null);
    }

    public PhysicalRiftPositionChangeSnapshot LastSnapshot { get; private set; }

    public PhysicalRiftPositionChangeSnapshot Observe(
        PhysicalRiftPositionResult sample, long acquisitionMonotonicElapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (cancelled)
            return Set(PhysicalRiftPositionChangeStatus.Cancelled, "cancelled_reset_required", null);
        if (observations >= maxObservations)
        {
            Invalidate();
            return Set(PhysicalRiftPositionChangeStatus.LimitReached,
                "bounded_observation_limit_reset_required", null);
        }
        observations++;
        if (acquisitionMonotonicElapsedMilliseconds < 0)
        {
            Invalidate();
            return Set(PhysicalRiftPositionChangeStatus.InvalidCandidate,
                "negative_monotonic_acquisition_elapsed", null);
        }
        if (sample.Status != PhysicalRiftPositionStatus.Candidate)
        {
            Invalidate();
            if (sample.Status == PhysicalRiftPositionStatus.IdentityMismatch)
                return Set(PhysicalRiftPositionChangeStatus.IdentityChanged,
                    "native_reader_process_or_build_identity_mismatch",
                    acquisitionMonotonicElapsedMilliseconds);
            return Set(PhysicalRiftPositionChangeStatus.Unavailable,
                "source_status_" + sample.Status.ToString().ToLowerInvariant(),
                acquisitionMonotonicElapsedMilliseconds);
        }
        if (sample.ProcessId <= 0 || sample.ProcessStartUtcTicks <= 0 ||
            !string.Equals(sample.ExecutableSha256, PhysicalRiftPositionReader.GameSha256,
                StringComparison.OrdinalIgnoreCase) ||
            sample.PlayerUnitKey is not ulong unitKey || unitKey == 0 ||
            sample.X is not float x || sample.Y is not float y || sample.Z is not float z ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
            !double.IsFinite(sample.ElapsedMilliseconds) ||
            sample.ElapsedMilliseconds is < 0 or > MaxIndividualReadMilliseconds ||
            sample.CapturedAtUtc == default)
        {
            Invalidate();
            return Set(PhysicalRiftPositionChangeStatus.InvalidCandidate,
                "native_candidate_missing_invalid_or_unpinned",
                acquisitionMonotonicElapsedMilliseconds);
        }

        var nextIdentity = new Identity(sample.ProcessId, sample.ProcessStartUtcTicks,
            PhysicalRiftPositionReader.GameSha256, unitKey);
        if (identity is not null)
        {
            if (identity != nextIdentity)
            {
                Invalidate();
                return Set(PhysicalRiftPositionChangeStatus.IdentityChanged,
                    "native_player_or_process_identity_changed",
                    acquisitionMonotonicElapsedMilliseconds);
            }
            if (acquisitionMonotonicElapsedMilliseconds <= previousMonotonicMs)
            {
                Invalidate();
                return Set(PhysicalRiftPositionChangeStatus.OutOfOrder,
                    "nonadvancing_monotonic_acquisition_elapsed",
                    acquisitionMonotonicElapsedMilliseconds);
            }
            if (acquisitionMonotonicElapsedMilliseconds - previousMonotonicMs > maxGapMilliseconds)
            {
                Invalidate();
                return Set(PhysicalRiftPositionChangeStatus.StaleAcquisitionGap,
                    "native_acquisition_gap_exceeded_limit_not_game_producer_staleness",
                    acquisitionMonotonicElapsedMilliseconds);
            }

            double dx = (double)x - previous.X;
            double dy = (double)y - previous.Y;
            double dz = (double)z - previous.Z;
            double ax = Math.Abs(dx), ay = Math.Abs(dy), az = Math.Abs(dz);
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!double.IsFinite(distance))
            {
                Invalidate();
                return Set(PhysicalRiftPositionChangeStatus.InvalidCandidate,
                    "native_xyz_difference_nonfinite",
                    acquisitionMonotonicElapsedMilliseconds);
            }
            previous = (x, y, z);
            previousMonotonicMs = acquisitionMonotonicElapsedMilliseconds;
            candidates++;
            if (ax == 0 && ay == 0 && az == 0)
            {
                unchanged++;
                return Set(PhysicalRiftPositionChangeStatus.Unchanged,
                    "new_acquisition_equal_xyz_no_producer_freshness_claim",
                    acquisitionMonotonicElapsedMilliseconds, ax, ay, az, distance);
            }
            changed++;
            return Set(PhysicalRiftPositionChangeStatus.Changed,
                "observed_xyz_value_change_not_proven_gameplay_movement",
                acquisitionMonotonicElapsedMilliseconds, ax, ay, az, distance);
        }

        identity = nextIdentity;
        previous = (x, y, z);
        previousMonotonicMs = acquisitionMonotonicElapsedMilliseconds;
        candidates = 1;
        changed = 0;
        unchanged = 0;
        return Set(PhysicalRiftPositionChangeStatus.Baseline,
            "first_exact_identity_native_xyz_acquisition", acquisitionMonotonicElapsedMilliseconds);
    }

    /// <summary>Stop this tracker permanently until explicit Reset; no prior XYZ may survive.</summary>
    public PhysicalRiftPositionChangeSnapshot Cancel()
    {
        Invalidate();
        cancelled = true;
        return Set(PhysicalRiftPositionChangeStatus.Cancelled, "cancelled_reset_required", null);
    }

    /// <summary>Starts a new independent comparison epoch and drops all historical state.</summary>
    public void Reset()
    {
        identity = null;
        previous = default;
        previousMonotonicMs = 0;
        observations = candidates = changed = unchanged = invalidations = 0;
        cancelled = false;
        LastSnapshot = Create(PhysicalRiftPositionChangeStatus.Unavailable, "reset_no_baseline", null);
    }

    private void Invalidate()
    {
        identity = null;
        previous = default;
        previousMonotonicMs = 0;
        candidates = changed = unchanged = 0;
        if (invalidations < maxObservations)
            invalidations++;
    }

    private PhysicalRiftPositionChangeSnapshot Set(
        PhysicalRiftPositionChangeStatus status, string reason,
        long? at, double? dx = null, double? dy = null, double? dz = null, double? distance = null)
    {
        LastSnapshot = Create(status, reason, at, dx, dy, dz, distance);
        return LastSnapshot;
    }

    private PhysicalRiftPositionChangeSnapshot Create(
        PhysicalRiftPositionChangeStatus status, string reason,
        long? at, double? dx = null, double? dy = null, double? dz = null, double? distance = null) =>
        new(status, reason, identity is not null, observations, candidates, changed, unchanged,
            invalidations, identity?.ProcessId, identity?.ProcessStartUtcTicks,
            identity?.ExecutableSha256, identity?.PlayerUnitKey, at, dx, dy, dz, distance);
}
