namespace Rift.Automation.Core;

public static class DemoReplay
{
    public static ReplayScenario Create()
    {
        var session = new SessionIdentity("synthetic-demo", "synthetic-v1");
        var zero = new PlayerPosition(0, 0, 0);
        PositionObservation Sample(
            long epoch, long sequence, long acquired, long received,
            ObservationStatus status = ObservationStatus.Present,
            PlayerPosition? value = null, string? reason = null) =>
            new(session, epoch, sequence, status,
                status == ObservationStatus.Present ? value ?? zero : null,
                acquired, acquired, received, "synthetic-demo", reason);

        return new ReplayScenario("SYNTHETIC observer demonstration", new ReplayStep[]
        {
            new ResetStep(0, session, 1),
            new SampleStep(10, Sample(1, 1, 0, 10)),
            new SampleStep(100, Sample(1, 2, 100, 100)),
            new TickStep(1101),
            new SampleStep(1110, Sample(1, 3, 1110, 1110,
                ObservationStatus.Error, reason: "synthetic source error")),
            new GapStep(1120, "synthetic source gap"),
            new ResetStep(1200, session, 2),
            new SampleStep(1210, Sample(1, 4, 1200, 1210)),
            new SampleStep(1220, Sample(2, 1, 1220, 1220,
                value: new PlayerPosition(0, 10, 20))),
            new TickStep(2221)
        });
    }
}
