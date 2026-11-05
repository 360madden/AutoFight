namespace Rift.Automation.Windows;

/// <summary>
/// Owns one explicitly pinned, observation-only guest receiver.
/// No controller, gameplay input, or external service is started.
/// </summary>
public sealed class StandaloneTelemetryHostSession : IDisposable
{
    private readonly StandaloneTelemetryIngest _ingest;
    private readonly StandaloneTelemetryHttpListener _listener;
    private readonly TimeProvider _time;
    private int _disposed;

    public StandaloneTelemetryHostSession(
        StandaloneTelemetryBinding binding,
        string bindIpv4,
        int port,
        string deliveryToken,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _time = timeProvider ?? TimeProvider.System;
        _ingest = new StandaloneTelemetryIngest(binding, deliveryToken);
        _listener = new StandaloneTelemetryHttpListener(
            bindIpv4, port, binding.GuestIpv4, deliveryToken,
            (body, token, peer, receivedAt) =>
                _ingest.AcceptDelivery(body, token, peer, receivedAt).Accepted,
            _time);
    }

    // Testing-only socket seam. The HTTP listener validates a real loopback
    // peer; the authenticated ingest receives the fixture's pinned guest
    // address because loopback traffic cannot originate from a VMware guest.
    // Production construction always passes the actual socket peer.
    internal static StandaloneTelemetryHostSession CreateLoopbackForTests(
        StandaloneTelemetryBinding binding,
        string deliveryToken,
        TimeProvider? timeProvider = null) =>
        new(binding, deliveryToken, timeProvider);

    internal int BoundPortForTests => _listener.BoundPort;

    private StandaloneTelemetryHostSession(
        StandaloneTelemetryBinding binding,
        string deliveryToken,
        TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _time = timeProvider ?? TimeProvider.System;
        _ingest = new StandaloneTelemetryIngest(binding, deliveryToken);
        _listener = StandaloneTelemetryHttpListener.CreateLoopbackForTests(
            deliveryToken,
            (body, token, _, receivedAt) =>
                _ingest.AcceptDelivery(
                    body, token, binding.GuestIpv4, receivedAt).Accepted,
            _time);
    }

    public string ReceiverInstanceId => _ingest.ReceiverInstanceId;

    /// <summary>
    /// Runs one listener until cancellation or disposal. A fault is propagated
    /// to the owning caller; a fresh session is required to restart.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0, this);
        return _listener.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Re-evaluates freshness against the original delivery time.
    /// An HTTP acknowledgement alone never establishes usable XYZ.
    /// </summary>
    public VmTelemetrySnapshot ReadLatest()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0, this);
        return _ingest.ReadLatest(_time.GetUtcNow());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _listener.Dispose();
    }
}
