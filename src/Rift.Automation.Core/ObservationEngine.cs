namespace Rift.Automation.Core;

/// <summary>
/// Single-writer synthetic observer. All time is supplied by the caller.
/// This type performs no I/O and cannot execute game actions.
/// </summary>
public sealed class ObservationEngine
{
    private readonly string _supportedBuildId;
    private readonly long _maxAcquisitionAgeMs;
    private readonly long _maxReceiveAgeMs;
    public long MaxAcquisitionAgeMs => _maxAcquisitionAgeMs;
    public long MaxReceiveAgeMs => _maxReceiveAgeMs;
    private SessionIdentity? _session;
    private long _epoch;
    private long _sequence;
    private long _revision;
    private long _lastObservedMs;
    private long _observationFloorMs;
    private PositionObservation? _current;
    private PlayerPosition? _lastKnown;
    private ViewStatus _status = ViewStatus.Unknown;
    private string _sourceId = "unavailable";
    private string _reason = "no_session";

    public ObservationEngine(
        string supportedBuildId = "synthetic-v1",
        long maxAcquisitionAgeMs = 1000,
        long maxReceiveAgeMs = 1000)
    {
        if (!ValidText(supportedBuildId, 128))
            throw new ArgumentException("A bounded build identity is required.", nameof(supportedBuildId));
        ArgumentOutOfRangeException.ThrowIfNegative(maxAcquisitionAgeMs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReceiveAgeMs);
        _supportedBuildId = supportedBuildId;
        _maxAcquisitionAgeMs = maxAcquisitionAgeMs;
        _maxReceiveAgeMs = maxReceiveAgeMs;
    }

    public void Reset(SessionIdentity session, long epoch, long nowMs)
    {
        AdmitTime(nowMs);
        if (!ValidIdentity(session))
            throw new ArgumentException("A bounded session identity is required.", nameof(session));
        if (epoch <= 0 || epoch <= _epoch)
            throw new ArgumentOutOfRangeException(nameof(epoch), "Session epochs must increase.");

        _session = session;
        _epoch = epoch;
        _observationFloorMs = nowMs;
        _sequence = 0;
        _current = null;
        _lastKnown = null;
        _sourceId = "unavailable";
        _status = IsSupported ? ViewStatus.Unknown : ViewStatus.Unsupported;
        _reason = IsSupported ? "awaiting_sample" : "unsupported_build";
        Mutated();
    }

    public ApplyResult Apply(PositionObservation observation, long nowMs)
    {
        AdmitTime(nowMs);
        if (_session is null)
            return new(false, "no_session");
        if (observation is null)
            return new(false, "null_observation");
        if (!ValidIdentity(observation.Session) || observation.Session != _session)
            return new(false, "wrong_session");
        if (observation.Epoch != _epoch)
            return new(false, "wrong_epoch");
        if (observation.Sequence <= 0 || observation.Sequence <= _sequence)
            return new(false, "nonadvancing_sequence");

        // A newer malformed sample retires current validity. Consuming its
        // sequence prevents an older success from silently reviving state.
        _sequence = observation.Sequence;
        string? invalid = InvalidReason(observation, nowMs, _observationFloorMs);
        if (invalid is not null)
        {
            _current = null;
            _status = IsSupported ? ViewStatus.Invalid : ViewStatus.Unsupported;
            _reason = invalid;
            _sourceId = ValidText(observation.SourceId, 128) ? observation.SourceId : "unavailable";
            Mutated();
            return new(false, invalid);
        }

        _sourceId = observation.SourceId;
        if (!IsSupported)
        {
            _current = null;
            _status = ViewStatus.Unsupported;
            _reason = "unsupported_build";
            Mutated();
            return new(false, "unsupported_build");
        }

        _current = observation;
        _status = observation.Status switch
        {
            ObservationStatus.Present => ViewStatus.Present,
            ObservationStatus.Missing => ViewStatus.Missing,
            ObservationStatus.Unsupported => ViewStatus.Unsupported,
            ObservationStatus.Invalid => ViewStatus.Invalid,
            ObservationStatus.Error => ViewStatus.Error,
            _ => throw new InvalidOperationException("Validated status changed.")
        };
        _reason = observation.Reason ?? observation.Status.ToString().ToLowerInvariant();
        if (observation.Status == ObservationStatus.Present)
            _lastKnown = observation.Value;
        Mutated();
        return new(true, "accepted");
    }

    public void MarkGap(long nowMs, string reason)
    {
        AdmitTime(nowMs);
        if (_session is null)
            throw new InvalidOperationException("A session must be established first.");
        if (!ValidText(reason, 256))
            throw new ArgumentException("A bounded gap reason is required.", nameof(reason));
        _current = null;
        _observationFloorMs = nowMs;
        _status = IsSupported ? ViewStatus.Gap : ViewStatus.Unsupported;
        _reason = IsSupported ? reason : "unsupported_build";
        Mutated();
    }

    public PositionView Project(long nowMs)
    {
        AdmitTime(nowMs);
        ViewStatus status = _status;
        long? acquisitionAge = _current is null ? null : nowMs - _current.AcquiredStartMs;
        long? receiveAge = _current is null ? null : nowMs - _current.ReceivedMs;
        string reason = _reason;
        if (status == ViewStatus.Present &&
            (acquisitionAge > _maxAcquisitionAgeMs || receiveAge > _maxReceiveAgeMs))
        {
            status = ViewStatus.Stale;
            reason = acquisitionAge > _maxAcquisitionAgeMs ? "acquisition_stale" : "receipt_stale";
        }

        bool available = status == ViewStatus.Present;
        return new PositionView(
            "SyntheticReplay", _session?.SessionId ?? "unavailable",
            _session?.BuildId ?? "unavailable", _epoch, _revision,
            status, available ? _current?.Value : null, _lastKnown,
            acquisitionAge, receiveAge, _sourceId, reason, available);
    }

    private bool IsSupported => _session is not null &&
        string.Equals(_session.BuildId, _supportedBuildId, StringComparison.Ordinal);

    private static string? InvalidReason(PositionObservation observation, long nowMs, long captureFloorMs)
    {
        if (!ValidText(observation.SourceId, 128))
            return "invalid_source";
        if (observation.Reason is not null && !ValidText(observation.Reason, 256))
            return "invalid_reason";
        if (!Enum.IsDefined(observation.Status))
            return "invalid_status";
        if (observation.AcquiredStartMs < 0 ||
            observation.AcquiredEndMs < observation.AcquiredStartMs ||
            observation.ReceivedMs < observation.AcquiredEndMs ||
            observation.ReceivedMs > nowMs)
            return "invalid_time";
        if (observation.AcquiredStartMs < captureFloorMs)
            return "capture_before_reset_or_gap";
        if (observation.Status == ObservationStatus.Present)
        {
            if (observation.Value is not PlayerPosition value ||
                !double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
                return "invalid_value";
        }
        else if (observation.Value is not null)
            return "invalid_status_value";
        return null;
    }

    private static bool ValidIdentity(SessionIdentity? session) =>
        session is not null && ValidText(session.SessionId, 128) && ValidText(session.BuildId, 128);

    private static bool ValidText(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    // Projection does not mutate observations/revision, but admits clock time.
    // A caller cannot query a later stale state and then revive it by rewinding.
    private void AdmitTime(long nowMs)
    {
        if (nowMs < 0 || nowMs < _lastObservedMs)
            throw new ArgumentOutOfRangeException(nameof(nowMs), "Observed time must not regress.");
        _lastObservedMs = nowMs;
    }

    private void Mutated() => _revision++;
}
