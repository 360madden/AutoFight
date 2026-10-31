using System.Text.Json;
using Rift.Automation.Core;
using static Rift.Automation.Specs.SpecRunner;

namespace Rift.Automation.Specs;

internal static class CoreSpecs
{
    internal static readonly SessionIdentity Session = new("spec-session", "synthetic-v1");
    internal static readonly PlayerPosition Zero = new(0, -10, 20);

    internal static PositionObservation Sample(
        long sequence = 1, ObservationStatus status = ObservationStatus.Present,
        PlayerPosition? value = null, long acquired = 0, long received = 0) =>
        new(Session, 1, sequence, status,
            status == ObservationStatus.Present ? value ?? Zero : value,
            acquired, acquired, received, "spec");

    private static ObservationEngine Engine()
    {
        var engine = new ObservationEngine();
        engine.Reset(Session, 1, 0);
        return engine;
    }

    internal static void Run()
    {
        Test("valid zero and negative coordinates", () =>
        {
            var e = Engine();
            Check(e.Apply(Sample(), 0).Accepted, "sample accepted");
            var v = e.Project(0);
            Check(v.Status == ViewStatus.Present && v.Position == Zero && v.CanObservePosition && !v.CanAutomate, "valid synthetic value");
        });
        Test("equal newer coordinates refresh acquisition", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(e.Apply(Sample(2, acquired: 900, received: 900), 900).Accepted, "equal new sample");
            Check(e.Project(1500).Status == ViewStatus.Present, "equal value must not be stale by equality");
        });
        Test("acquisition age exact boundary", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(e.Project(1000).Status == ViewStatus.Present, "inclusive boundary");
            var v = e.Project(1001);
            Check(v.Status == ViewStatus.Stale && v.Position is null && !v.CanObservePosition && v.LastKnownPosition == Zero, "stale is diagnostic only");
        });
        Test("fresh receipt cannot refresh old acquisition", () =>
        {
            var e = Engine();
            e.Apply(Sample(received: 900), 900);
            Check(e.Project(1001).Status == ViewStatus.Stale, "source age independent");
        });
        Test("oldest acquisition interval determines age", () =>
        {
            var e = Engine();
            e.Apply(Sample() with { AcquiredEndMs = 5000, ReceivedMs = 5000 }, 5000);
            Check(e.Project(5000).Status == ViewStatus.Stale && e.Project(5000).AcquisitionAgeMs == 5000, "old start cannot appear fresh");
        });
        Test("receive age independently bounded", () =>
        {
            var e = new ObservationEngine(maxAcquisitionAgeMs: 10000, maxReceiveAgeMs: 1000);
            e.Reset(Session, 1, 0);
            e.Apply(Sample(), 0);
            Check(e.Project(1001).Status == ViewStatus.Stale, "receipt deadline");
        });
        foreach (var status in new[] { ObservationStatus.Missing, ObservationStatus.Invalid, ObservationStatus.Error, ObservationStatus.Unsupported })
        {
            Test("accepted " + status + " clears eligibility", () =>
            {
                var e = Engine();
                e.Apply(Sample(), 0);
                Check(e.Apply(Sample(2, status), 1).Accepted, "typed failure observation accepted");
                var v = e.Project(1);
                Check(v.Position is null && !v.CanObservePosition && !v.CanAutomate && v.LastKnownPosition == Zero, "last-known only");
            });
        }
        Test("wrong context and old sequence preserve current", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            string before = JsonSerializer.Serialize(e.Project(0));
            foreach (var bad in new[]
            {
                Sample(2) with { Session = new SessionIdentity("old", "synthetic-v1") },
                Sample(2) with { Epoch = 0 }, Sample(1), Sample(0)
            })
            {
                Check(!e.Apply(bad, 0).Accepted, "old/context sample rejected");
                Check(JsonSerializer.Serialize(e.Project(0)) == before, "observation state unchanged");
            }
        });
        foreach (double coordinate in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Test("nonfinite newest coordinate invalidates " + coordinate, () =>
            {
                var e = Engine();
                e.Apply(Sample(), 0);
                Check(!e.Apply(Sample(3, value: new PlayerPosition(1, coordinate, 2)), 1).Accepted, "reject nonfinite");
                Check(e.Project(1).Position is null && e.Project(1).LastKnownPosition == Zero, "invalidate current");
                Check(!e.Apply(Sample(2), 1).Accepted, "consumed sequence prevents resurrection");
                Check(e.Apply(Sample(4, acquired: 2, received: 2), 2).Accepted, "newer good recovers");
            });
        }
        var invalidTimes = new Dictionary<string, PositionObservation>
        {
            ["inverted acquisition"] = Sample(3) with { AcquiredStartMs = 2, AcquiredEndMs = 1, ReceivedMs = 2 },
            ["receipt before acquisition"] = Sample(3) with { AcquiredStartMs = 1, AcquiredEndMs = 2, ReceivedMs = 1 },
            ["future receipt"] = Sample(3) with { ReceivedMs = 3 },
            ["negative acquisition"] = Sample(3) with { AcquiredStartMs = -1 },
            ["negative receipt"] = Sample(3) with { ReceivedMs = -1 }
        };
        foreach (var pair in invalidTimes)
        {
            Test("malformed newer time " + pair.Key, () =>
            {
                var e = Engine();
                e.Apply(Sample(), 0);
                Check(!e.Apply(pair.Value, 2).Accepted && e.Project(2).Position is null, "invalid time clears current");
                Check(!e.Apply(Sample(2), 2).Accepted, "sequence consumed");
            });
        }
        Test("status-value mismatch fails closed", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(!e.Apply(Sample(2, ObservationStatus.Missing, Zero), 1).Accepted && e.Project(1).Position is null, "mismatch invalidates");
        });
        Test("undefined status fails closed", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(!e.Apply(Sample(2) with { Status = (ObservationStatus)99 }, 1).Accepted && !e.Project(1).CanObservePosition, "undefined status rejected");
        });
        Test("reset clears state and rejects late prior epoch", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            e.Reset(Session, 2, 1);
            Check(e.Project(1).Position is null && e.Project(1).LastKnownPosition is null, "reset clears all data");
            Check(!e.Apply(Sample(99), 1).Accepted, "prior epoch rejected");
        });
        Test("reset capture floor blocks re-labeled older data", () =>
        {
            var e = Engine();
            e.Reset(Session, 2, 20);
            Check(!e.Apply(Sample() with { Epoch = 2, ReceivedMs = 21 }, 21).Accepted, "capture predates reset");
            Check(e.Project(21).Position is null, "old capture cannot recover");
        });
        Test("wrong build is unsupported", () =>
        {
            var e = new ObservationEngine();
            e.Reset(new SessionIdentity("wrong-build", "unqualified"), 1, 0);
            Check(e.Project(0).Status == ViewStatus.Unsupported && !e.Project(0).CanObservePosition, "build mismatch");
        });
        Test("gap invalidates until post-gap capture", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            e.MarkGap(10, "synthetic gap");
            Check(e.Project(10).Status == ViewStatus.Gap && e.Project(10).Position is null, "gap invalidates");
            Check(!e.Apply(Sample(2, received: 11), 11).Accepted, "pre-gap sample rejected");
            Check(e.Apply(Sample(3, acquired: 12, received: 12), 12).Accepted, "post-gap capture restores observation");
        });
        Test("clock cannot rewind after stale projection", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(e.Project(2000).Status == ViewStatus.Stale, "later view stale");
            Throws<ArgumentOutOfRangeException>(() => e.Project(20));
        });
        Test("clock cannot rewind after rejected observation", () =>
        {
            var e = Engine();
            Check(!e.Apply(Sample() with { Epoch = 99 }, 2000).Accepted, "wrong epoch rejected");
            Throws<ArgumentOutOfRangeException>(() => e.Apply(Sample(), 20));
        });
        Test("same-time projection leaves snapshot revision unchanged", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(e.Project(0) == e.Project(0), "projection stable");
        });
        Test("negative now and invalid reset rejected", () =>
        {
            var e = Engine();
            Throws<ArgumentOutOfRangeException>(() => e.Project(-1));
            Throws<ArgumentOutOfRangeException>(() => e.Reset(Session, 1, 0));
            Throws<ArgumentException>(() => e.Reset(new SessionIdentity(" ", "synthetic-v1"), 2, 0));
        });
        Test("bounded source and reason reject newest malformed sample", () =>
        {
            var e = Engine();
            e.Apply(Sample(), 0);
            Check(!e.Apply(Sample(2) with { SourceId = new string('x', 129) }, 1).Accepted, "source limit");
            Check(!e.Apply(Sample(3) with { Reason = "bad\u0001" }, 2).Accepted && e.Project(2).Position is null, "control rejected");
        });
        Test("seeded valid-invalid recovery sequence", () =>
        {
            var e = Engine();
            var random = new Random(4242);
            for (int i = 1; i <= 100; i++)
            {
                long now = i * 2L;
                var position = new PlayerPosition(random.Next(-500, 500), random.Next(-500, 500), random.Next(-500, 500));
                var sample = Sample(i, value: position, acquired: now, received: now);
                bool malformed = i % 3 == 0;
                if (malformed) sample = sample with { Value = new PlayerPosition(double.NaN, 0, 0) };
                Check(e.Apply(sample, now).Accepted != malformed, "expected acceptance");
                Check(e.Project(now).CanObservePosition != malformed && !e.Project(now).CanAutomate, "capabilities follow validity");
            }
        });
    }
}
