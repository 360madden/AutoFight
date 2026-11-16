namespace Rift.Automation.Windows;

/// <summary>Owns a bounded same-host listener and physical game observation ingest.</summary>
public sealed class PhysicalTelemetryHostSession : IDisposable
{
    private readonly StandaloneTelemetryHttpListener listener;
    private readonly PhysicalTelemetryIngest ingest;
    public PhysicalTelemetryHostSession(PhysicalTelemetryBinding binding, int port, string token)
    {
        ingest = new PhysicalTelemetryIngest(binding, token);
        listener = StandaloneTelemetryHttpListener.CreateLocal(port, token,
            (body, suppliedToken, peer, receivedAt) =>
                ingest.Accept(body, suppliedToken, peer, receivedAt));
    }

    public PhysicalTelemetryReceipt? Latest => ingest.Latest;
    public PhysicalTelemetryReceipt? ReadFresh(DateTimeOffset nowUtc, TimeSpan maxAge) =>
        ingest.ReadFresh(nowUtc, maxAge);
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        listener.RunAsync(cancellationToken);
    public void Dispose() => listener.Dispose();
}
