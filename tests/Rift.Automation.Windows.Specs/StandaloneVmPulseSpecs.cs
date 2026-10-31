using System.IO;
using Rift.Automation.Windows;

internal static class StandaloneVmPulseSpecs
{
    private sealed class Keyboard : IStandaloneVmKeyboard
    {
        public bool Current { get; set; } = true;
        public bool FailDown { get; set; }
        public bool FailUp { get; set; }
        public List<string> Calls { get; } = [];

        public bool IsTargetCurrent() => Current;

        public void KeyDown(string key)
        {
            Calls.Add("down:" + key);
            if (FailDown) throw new IOException("Synthetic keydown failure.");
        }

        public void KeyUp(string key)
        {
            Calls.Add("up:" + key);
            if (FailUp) throw new IOException("Synthetic keyup failure.");
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan duration) =>
            _ticks = checked(_ticks + duration.Ticks);
    }

    private static StandaloneVmKeyPulseExecutor Instant(
        Keyboard keyboard, Clock clock) =>
        new(keyboard, clock, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.Advance(duration);
            return Task.CompletedTask;
        });

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Standalone pulse validates key and hold before input", () =>
        {
            var keyboard = new Keyboard();
            var executor = Instant(keyboard, new Clock());

            foreach (var request in new[]
            {
                new StandaloneVmKeyPulse("W", 50),
                new StandaloneVmKeyPulse("f", 50),
                new StandaloneVmKeyPulse("w", 19),
                new StandaloneVmKeyPulse("w", 251)
            })
            {
                bool rejected = false;
                try
                {
                    executor.ExecuteAsync(request).GetAwaiter().GetResult();
                }
                catch (InvalidDataException)
                {
                    rejected = true;
                }

                check(rejected, "invalid request must be rejected");
            }

            check(keyboard.Calls.Count == 0, "no input after invalid request");
        });

        test("Standalone pulse delivers and releases one bounded key", () =>
        {
            var keyboard = new Keyboard();
            var result = Instant(keyboard, new Clock())
                .ExecuteAsync(new StandaloneVmKeyPulse("w", 50))
                .GetAwaiter().GetResult();

            check(result.DeliverySucceeded && result.ReleaseSucceeded &&
                !result.Cancelled && !result.TargetLost && !result.StateUncertain &&
                !result.RequiresDisarm && result.Error is null &&
                result.ObservedDuration == TimeSpan.FromMilliseconds(50) &&
                keyboard.Calls.SequenceEqual(["down:w", "up:w"]),
                "one bounded synthetic pulse, not verified game movement");
        });

        test("Standalone pulse blocks wrong target before keydown", () =>
        {
            var keyboard = new Keyboard { Current = false };
            var result = Instant(keyboard, new Clock())
                .ExecuteAsync(new StandaloneVmKeyPulse("a", 50))
                .GetAwaiter().GetResult();

            check(result.TargetLost && result.RequiresDisarm &&
                !result.DeliverySucceeded && keyboard.Calls.Count == 0,
                "wrong target receives no input");
        });

        test("Standalone pre-cancellation does not press any key", () =>
        {
            var keyboard = new Keyboard();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            var result = Instant(keyboard, new Clock())
                .ExecuteAsync(new StandaloneVmKeyPulse("w", 50), cancelled.Token)
                .GetAwaiter().GetResult();

            check(result.Cancelled && result.RequiresDisarm &&
                keyboard.Calls.Count == 0, "pre-cancelled request cannot press");
        });

        test("Standalone cancellation during hold releases key", () =>
        {
            var keyboard = new Keyboard();
            var clock = new Clock();
            using var cancelled = new CancellationTokenSource();

            var executor = new StandaloneVmKeyPulseExecutor(
                keyboard, clock, (duration, token) =>
                {
                    clock.Advance(duration);
                    cancelled.Cancel();
                    return Task.CompletedTask;
                });

            var result = executor.ExecuteAsync(
                new StandaloneVmKeyPulse("w", 50), cancelled.Token)
                .GetAwaiter().GetResult();

            check(result.Cancelled && result.ReleaseSucceeded &&
                result.RequiresDisarm && !result.DeliverySucceeded &&
                keyboard.Calls.SequenceEqual(["down:w", "up:w"]),
                "cancellation releases key once");
        });

        test("Standalone target loss during hold releases key", () =>
        {
            var keyboard = new Keyboard();
            var clock = new Clock();
            var executor = new StandaloneVmKeyPulseExecutor(
                keyboard, clock, (duration, token) =>
                {
                    clock.Advance(duration);
                    keyboard.Current = false;
                    return Task.CompletedTask;
                });

            var result = executor.ExecuteAsync(new StandaloneVmKeyPulse("s", 50))
                .GetAwaiter().GetResult();

            check(result.TargetLost && !result.DeliverySucceeded &&
                result.ReleaseSucceeded && result.RequiresDisarm &&
                keyboard.Calls.SequenceEqual(["down:s", "up:s"]),
                "target loss disarms and releases");
        });

        test("Standalone keydown and keyup errors remain uncertain", () =>
        {
            var failedDown = new Keyboard { FailDown = true };
            var down = Instant(failedDown, new Clock())
                .ExecuteAsync(new StandaloneVmKeyPulse("d", 50))
                .GetAwaiter().GetResult();

            check(!down.DeliverySucceeded && down.StateUncertain &&
                down.RequiresDisarm && down.ReleaseSucceeded &&
                failedDown.Calls.SequenceEqual(["down:d", "up:d"]),
                "keydown may partially insert and always attempts release");

            var failedUp = new Keyboard { FailUp = true };
            var up = Instant(failedUp, new Clock())
                .ExecuteAsync(new StandaloneVmKeyPulse("d", 50))
                .GetAwaiter().GetResult();

            check(up.DeliverySucceeded && !up.ReleaseSucceeded &&
                up.StateUncertain && up.RequiresDisarm &&
                failedUp.Calls.SequenceEqual(["down:d", "up:d"]),
                "keyup uncertainty requires disarm");
        });

        test("Standalone stalled monotonic clock cannot hang input", () =>
        {
            var keyboard = new Keyboard();
            var executor = new StandaloneVmKeyPulseExecutor(
                keyboard, new Clock(), (_, _) => Task.CompletedTask);

            var result = executor.ExecuteAsync(new StandaloneVmKeyPulse("q", 50))
                .GetAwaiter().GetResult();

            check(!result.DeliverySucceeded && result.StateUncertain &&
                result.RequiresDisarm && result.ReleaseSucceeded &&
                keyboard.Calls.SequenceEqual(["down:q", "up:q"]),
                "clock stall does not leave a key held");
        });
    }
}
