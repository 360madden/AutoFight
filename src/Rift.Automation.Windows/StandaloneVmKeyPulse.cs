using System.IO;

namespace Rift.Automation.Windows;

/// <summary>A bounded input request, not evidence of game movement.</summary>
public sealed record StandaloneVmKeyPulse(string Key, int HoldMilliseconds);

/// <summary>
/// Implemented by a separately authorized guest-local adapter.
/// The implementation must bind input to an exact process/window identity.
/// </summary>
public interface IStandaloneVmKeyboard
{
    bool IsTargetCurrent();
    void KeyDown(string key);
    void KeyUp(string key);
}

/// <summary>Input delivery and release remain separate from game feedback.</summary>
public sealed record StandaloneVmPulseResult(
    bool DeliverySucceeded,
    bool ReleaseSucceeded,
    bool Cancelled,
    bool TargetLost,
    bool StateUncertain,
    bool RequiresDisarm,
    TimeSpan ObservedDuration,
    string? Error);

/// <summary>
/// One bounded, supervised key pulse, adapted from RiftReader2's bounded executor.
/// No target discovery, OS input, focus changes, auto-arming or retries.
/// </summary>
public sealed class StandaloneVmKeyPulseExecutor
{
    private static readonly HashSet<string> AllowedKeys =
        new(["w", "a", "s", "d", "q", "e"], StringComparer.Ordinal);

    private static readonly TimeSpan MaximumCheckInterval =
        TimeSpan.FromMilliseconds(10);

    private readonly IStandaloneVmKeyboard _keyboard;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public StandaloneVmKeyPulseExecutor(
        IStandaloneVmKeyboard keyboard,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _clock = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync ??
            ((duration, token) => Task.Delay(duration, _clock, token));
    }

    public static void Validate(StandaloneVmKeyPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        if (pulse.Key is null || !AllowedKeys.Contains(pulse.Key))
            throw new InvalidDataException(
                "Only lowercase w, a, s, d, q or e is supported.");

        if (pulse.HoldMilliseconds is < 20 or > 250)
            throw new InvalidDataException(
                "Hold must be from 20 through 250 milliseconds.");
    }

    public async Task<StandaloneVmPulseResult> ExecuteAsync(
        StandaloneVmKeyPulse pulse,
        CancellationToken cancellationToken = default)
    {
        Validate(pulse);

        long startedAt = _clock.GetTimestamp();

        bool pressAttempted = false;
        bool delivered = false;
        bool releaseSucceeded = true;
        bool cancelled = false;
        bool targetLost = false;
        bool uncertain = false;
        string? error = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CheckTarget())
                throw new TargetChangedException("Target changed before input.");

            // A native KeyDown may insert input before throwing.
            pressAttempted = true;
            _keyboard.KeyDown(pulse.Key);

            long heldAt = _clock.GetTimestamp();
            TimeSpan hold = TimeSpan.FromMilliseconds(pulse.HoldMilliseconds);

            while (_clock.GetElapsedTime(heldAt) < hold)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!CheckTarget())
                    throw new TargetChangedException("Target changed during input.");

                TimeSpan remaining = hold - _clock.GetElapsedTime(heldAt);

                if (remaining <= TimeSpan.Zero)
                    break;

                TimeSpan interval =
                    remaining < MaximumCheckInterval ? remaining : MaximumCheckInterval;

                long beforeWait = _clock.GetTimestamp();

                await _delayAsync(interval, cancellationToken)
                    .ConfigureAwait(false);

                if (_clock.GetElapsedTime(beforeWait) <= TimeSpan.Zero)
                    throw new InvalidOperationException(
                        "Hold completed without monotonic clock progress.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!CheckTarget())
                throw new TargetChangedException("Target changed before completion.");

            delivered = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            error = "Pulse cancelled.";
        }
        catch (TargetChangedException exception)
        {
            targetLost = true;
            error = exception.Message;
        }
        catch (Exception exception)
        {
            uncertain = true;
            error =
                $"Pulse failed: {exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            // Release is attempted even when KeyDown threw.
            if (pressAttempted)
            {
                try
                {
                    _keyboard.KeyUp(pulse.Key);
                }
                catch (Exception exception)
                {
                    releaseSucceeded = false;
                    uncertain = true;

                    string failure =
                        $"Release failed: {exception.GetType().Name}: " +
                        exception.Message;

                    error = error is null ? failure : error + " " + failure;
                }
            }
        }

        bool requiresDisarm =
            cancelled || targetLost || uncertain || !delivered;

        return new StandaloneVmPulseResult(
            delivered,
            releaseSucceeded,
            cancelled,
            targetLost,
            uncertain,
            requiresDisarm,
            _clock.GetElapsedTime(startedAt),
            error);

        bool CheckTarget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _keyboard.IsTargetCurrent();
        }
    }

    private sealed class TargetChangedException(string message)
        : Exception(message);
}
