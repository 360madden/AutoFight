namespace Rift.Automation.Core;

public sealed record SessionIdentity(string SessionId, string BuildId);
public readonly record struct PlayerPosition(double X, double Y, double Z);

public enum ObservationStatus { Present, Missing, Unsupported, Invalid, Error }
public enum ViewStatus { Unknown, Present, Stale, Missing, Unsupported, Invalid, Error, Gap }

public sealed record PositionObservation(
    SessionIdentity Session, long Epoch, long Sequence,
    ObservationStatus Status, PlayerPosition? Value,
    long AcquiredStartMs, long AcquiredEndMs, long ReceivedMs,
    string SourceId, string? Reason = null);

public sealed record ApplyResult(bool Accepted, string Code);

public sealed record PositionView(
    string Mode, string SessionId, string BuildId, long Epoch, long Revision,
    ViewStatus Status, PlayerPosition? Position, PlayerPosition? LastKnownPosition,
    long? AcquisitionAgeMs, long? ReceiveAgeMs, string SourceId,
    string Reason, bool CanObservePosition)
{
    public bool CanAutomate => false;
}
