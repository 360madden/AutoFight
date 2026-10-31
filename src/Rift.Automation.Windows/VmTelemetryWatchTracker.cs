namespace Rift.Automation.Windows;

/// <summary>Single-lab, caller-clocked telemetry progression. No IO or gameplay input.</summary>
public sealed class VmTelemetryWatchTracker
{
    private readonly string _lab;
    private long _lastTimeMs = -1;
    private long _firstTimeMs = -1;
    private long? _lastAdvanceMs;
    private VmTelemetrySnapshot? _previous;
    private VmTelemetrySnapshot? _current;
    private string _state = "not_started";
    private string _reason = "not_started";
    private int _polls;
    private int _newSamples;
    private int _repeats;
    private int _unavailable;
    private int _sessionChanges;
    private int _reordered;
    private int _recovered;
    private long _longestNoAdvanceMs;

    public VmTelemetryWatchTracker(string lab)
    {
        if (lab is not ("lab1" or "lab2"))
            throw new ArgumentException("Lab must be lab1 or lab2.", nameof(lab));
        _lab = lab;
    }

    public VmTelemetryWatchReport Observe(VmTelemetrySnapshot sample, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (elapsedMs < 0 || elapsedMs < _lastTimeMs)
            throw new ArgumentOutOfRangeException(nameof(elapsedMs));
        _lastTimeMs = elapsedMs;
        if (_firstTimeMs == -1) _firstTimeMs = elapsedMs;
        // Account for the interval that ends at this poll before an advancing
        // sample resets its origin. A sparse polling schedule must not erase it.
        long sinceAdvance = _lastAdvanceMs is long ms
            ? elapsedMs - ms : elapsedMs - _firstTimeMs;
        if (sinceAdvance > _longestNoAdvanceMs) _longestNoAdvanceMs = sinceAdvance;
        _polls++;

        if (sample.Lab != _lab || !sample.HasPosition ||
            string.IsNullOrEmpty(sample.StreamIdentity) ||
            sample.AddonClock is not double candidateClock ||
            !double.IsFinite(candidateClock) ||
            sample.OuterSequence is not long outer ||
            sample.AddonSequence is not long sequence ||
            sample.AddonEpoch is not long epoch ||
            outer <= 0 || sequence <= 0 || epoch <= 0)
        {
            _unavailable++;
            _current = null;
            _state = "unavailable";
            _reason = sample.Lab == _lab ? sample.Reason : "lab_mismatch";
            return View();
        }

        if (_previous is null)
        {
            _newSamples++;
            _previous = _current = sample;
            _lastAdvanceMs = elapsedMs;
            _state = "first_sample";
            _reason = "awaiting_second_advance";
            return View();
        }

        // Require nondecreasing epochs within an accepted pinned transport.
        // A lower epoch could be a delayed producer frame OR an addon reload
        // that reused the old binding; neither can safely restore current XYZ
        // without a reviewed new stream identity. An increasing epoch still
        // cannot reset the receiver sequence.
        bool sameTransport = sample.StreamIdentity == _previous.StreamIdentity &&
            sample.GameProcessId == _previous.GameProcessId;
        bool retiredEpoch = epoch < _previous.AddonEpoch;
        bool epochTransportRollback = epoch != _previous.AddonEpoch &&
            outer <= _previous.OuterSequence;
        if (sameTransport && (retiredEpoch || epochTransportRollback))
        {
            _reordered++;
            _current = null;
            _state = "reordered";
            _reason = retiredEpoch ? "retired_producer_epoch" : "epoch_change_transport_rollback";
            return View();
        }

        if (!sameTransport || sample.AddonEpoch != _previous.AddonEpoch)
        {
            _sessionChanges++;
            _newSamples++;
            _previous = _current = sample;
            _lastAdvanceMs = elapsedMs;
            _state = "session_changed";
            _reason = "new_stream_requires_new_continuity";
            return View();
        }

        bool advancing = outer > _previous.OuterSequence &&
            sequence > _previous.AddonSequence &&
            candidateClock > _previous.AddonClock;
        if (advancing)
        {
            _newSamples++;
            bool resumed = _state is "unavailable" or "reordered" or "awaiting_advance";
            if (resumed) _recovered++;
            _previous = _current = sample;
            _lastAdvanceMs = elapsedMs;
            _state = resumed ? "recovered" : "advancing";
            _reason = resumed ? "after_gap" : "new_producer_sample";
            return View();
        }

        bool redelivered = outer >= _previous.OuterSequence &&
            sequence == _previous.AddonSequence &&
            candidateClock == _previous.AddonClock &&
            sample.X == _previous.X &&
            sample.Y == _previous.Y &&
            sample.Z == _previous.Z;
        if (redelivered)
        {
            _repeats++;
            // The producer did not advance, but the transport did. Retain the
            // newest accepted outer watermark so an older frame cannot pass.
            _previous = sample;
            bool afterGap = _state is "unavailable" or "reordered" or "awaiting_advance";
            _current = afterGap ? null : sample;
            _state = afterGap ? "awaiting_advance" : "repeated";
            _reason = afterGap ? "old_sample_cannot_close_gap" : "no_new_producer_sample";
            return View();
        }

        _reordered++;
        _current = null;
        _state = "reordered";
        _reason = "sequence_clock_or_position_conflict";
        return View();
    }

    public VmTelemetryWatchReport View()
    {
        long elapsed = _firstTimeMs < 0 ? 0 : _lastTimeMs - _firstTimeMs;
        long sinceAdvance = _lastAdvanceMs is long ms ? _lastTimeMs - ms : elapsed;
        if (sinceAdvance > _longestNoAdvanceMs) _longestNoAdvanceMs = sinceAdvance;
        return new(
            _lab, _state, _reason, elapsed, _polls, _newSamples, _repeats,
            _unavailable, _sessionChanges, _reordered, _recovered,
            sinceAdvance, _longestNoAdvanceMs, _current);
    }
}

public sealed record VmTelemetryWatchReport(
    string Lab, string State, string Reason, long ElapsedMs,
    int Polls, int NewSamples, int Repeated, int Unavailable,
    int SessionChanges, int Reordered, int Recovered,
    long SinceLastAdvanceMs, long LongestNoAdvanceMs,
    VmTelemetrySnapshot? Current)
{
    public bool HasCurrentPosition => Current?.HasPosition == true;
    public bool CanAutomate => false;
}
