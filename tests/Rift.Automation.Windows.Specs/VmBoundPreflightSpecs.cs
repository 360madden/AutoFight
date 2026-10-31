using System.Text.Json;
using Rift.Automation.Windows;

internal static class VmBoundPreflightSpecs
{
    private static readonly string Hash = new('A', 64);
    private static VmTelemetrySnapshot Fresh(string lab = "lab1") =>
        new(lab, "fresh_observed", "pinned", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, 20, 10, 8, 2, 3300, 10, 15, 20,
            "whole-readable-private", "source-a", 20, "192.168.56.128",
            638000000000000000, @"C:\games\rift_x64.exe", Hash);

    private static readonly VmControllerResult Clean =
        new(true, true, true, false, null, 200);

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("VM read-only preflight rejects unknown lab without source or status access", () =>
        {
            int reads = 0, statuses = 0;
            var report = VmBoundPreflight.CheckObservedAsync("other",
                () => { reads++; return Fresh(); },
                (_, _) => { statuses++; return Task.FromResult(Clean); })
                .GetAwaiter().GetResult();
            check(reads == 0 && statuses == 0 && report.Reason == "invalid_lab" &&
                !report.IdentityMatched && !report.InputAttempted, "no work for invalid lab");
        });

        test("VM read-only preflight skips controller for stopped or wrong-lab source", () =>
        {
            foreach (VmTelemetrySnapshot source in new[]
            {
                Fresh() with { Status = "unavailable", Reason = "reader_not_running", X = null },
                Fresh("lab2"),
                Fresh() with { ReaderSourceAddress = null }
            })
            {
                int statuses = 0;
                var report = VmBoundPreflight.CheckObservedAsync("lab1", () => source,
                    (_, _) => { statuses++; return Task.FromResult(Clean); })
                    .GetAwaiter().GetResult();
                check(!report.BindingAndReadinessPassed && statuses == 0,
                    "invalid source stops before GET");
                check(report.Reason == (source.Lab == "lab2" ? "lab_mismatch" :
                    source.HasPosition ? "source_binding_unavailable" : "reader_not_running"),
                    "specific expected diagnostic without adopting wrong lab");
            }
        });

        test("VM read-only preflight accepts one status with two unchanged source reads", () =>
        {
            int reads = 0, statuses = 0;
            var report = VmBoundPreflight.CheckObservedAsync("lab1",
                () => { reads++; return Fresh(); },
                (_, _) => { statuses++; return Task.FromResult(Clean); })
                .GetAwaiter().GetResult();
            check(reads == 2 && statuses == 1 && report.IdentityMatched &&
                report.ActionReadyAtCheckTime && report.BindingAndReadinessPassed &&
                !report.InputAttempted && !report.GameFeedbackConfirmed && !report.CanAutomate &&
                report.Reason == "checks_passed", "read-only success is point in time");
        });

        test("VM read-only preflight accepts completely advancing source after status", () =>
        {
            VmTelemetrySnapshot first = Fresh();
            VmTelemetrySnapshot next = first with
            { OuterSequence = 11, AddonSequence = 9, AddonClock = 21 };
            int reads = 0;
            var report = VmBoundPreflight.CheckObservedAsync("lab1",
                () => ++reads == 1 ? first : next,
                (_, _) => Task.FromResult(Clean)).GetAwaiter().GetResult();
            check(reads == 2 && report.BindingAndReadinessPassed, "advancing same source passes");
        });

        test("VM read-only preflight distinguishes matched disarmed status from mismatched identity", () =>
        {
            int reads = 0;
            var disarmed = VmBoundPreflight.CheckObservedAsync("lab1",
                () => { reads++; return Fresh(); },
                (_, _) => Task.FromResult(new VmControllerResult(
                    false, false, false, false, "controller_not_ready", 200)))
                .GetAwaiter().GetResult();
            check(reads == 2 && disarmed.IdentityMatched &&
                !disarmed.ActionReadyAtCheckTime && !disarmed.CanAutomate &&
                disarmed.Reason == "controller_not_ready", "identity may match while disarmed");
            reads = 0;
            var mismatch = VmBoundPreflight.CheckObservedAsync("lab1",
                () => { reads++; return Fresh(); },
                (_, _) => Task.FromResult(new VmControllerResult(
                    false, false, false, false, "controller_identity_mismatch", 200)))
                .GetAwaiter().GetResult();
            check(reads == 1 && !mismatch.IdentityMatched &&
                mismatch.Reason == "controller_identity_mismatch",
                "identity mismatch is distinct and does not need final source read");
        });

        test("VM read-only preflight blocks final provenance swap or stale source", () =>
        {
            VmTelemetrySnapshot first = Fresh();
            VmTelemetrySnapshot advanced = first with
            { OuterSequence = 11, AddonSequence = 9, AddonClock = 21 };
            foreach (VmTelemetrySnapshot changed in new[]
            {
                advanced with { ReaderSourceAddress = "192.168.56.129" },
                advanced with { GameExecutablePath = @"C:\games\impostor.exe" },
                advanced with { GameExecutableSha256 = new string('B', 64) },
                advanced with { GameProcessStartUtcTicks = 638000000000000001 },
                advanced with { StreamIdentity = "changed" },
                first with { Status = "unavailable", Reason = "stale_or_clock_uncertain", X = null },
                first with { OuterSequence = 9 },
                first with { AddonSequence = 9, AddonClock = 20 }
            })
            {
                int reads = 0, statuses = 0;
                var report = VmBoundPreflight.CheckObservedAsync("lab1",
                    () => ++reads == 1 ? first : changed,
                    (_, _) => { statuses++; return Task.FromResult(Clean); })
                    .GetAwaiter().GetResult();
                check(reads == 2 && statuses == 1 && !report.IdentityMatched &&
                    report.Reason == "source_changed_or_expired_after_status" &&
                    !report.BindingAndReadinessPassed, "no stale positive diagnostic");
            }
        });

        test("VM read-only preflight does not retry rejected controller response", () =>
        {
            int statuses = 0;
            var report = VmBoundPreflight.CheckObservedAsync("lab1", () => Fresh(),
                (_, _) =>
                {
                    statuses++;
                    return Task.FromResult(new VmControllerResult(
                        false, false, false, false, "transport_timeout"));
                }).GetAwaiter().GetResult();
            check(statuses == 1 && report.Reason == "transport_timeout" &&
                !report.BindingAndReadinessPassed, "single status request; no retry");
        });

        test("VM read-only preflight cancellation before start skips every dependency", () =>
        {
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            int reads = 0, statuses = 0;
            bool cancelled = false;
            try
            {
                VmBoundPreflight.CheckObservedAsync("lab1",
                    () => { reads++; return Fresh(); },
                    (_, _) => { statuses++; return Task.FromResult(Clean); }, stop.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && reads == 0 && statuses == 0, "cancelled before source read");
        });

        test("VM read-only preflight cancellation during final source read never returns ready", () =>
        {
            using var stop = new CancellationTokenSource();
            int reads = 0, statuses = 0;
            bool cancelled = false;
            try
            {
                VmBoundPreflight.CheckObservedAsync("lab1",
                    () =>
                    {
                        if (++reads == 2) stop.Cancel();
                        return Fresh();
                    },
                    (_, _) => { statuses++; return Task.FromResult(Clean); },
                    stop.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && reads == 2 && statuses == 1,
                "late cancellation cannot produce a ready report");
        });

        test("VM preflight report serialization excludes all private game and controller identifiers", () =>
        {
            var report = VmBoundPreflight.CheckObservedAsync("lab1", () => Fresh(),
                (_, _) => Task.FromResult(Clean)).GetAwaiter().GetResult();
            string text = JsonSerializer.Serialize(report);
            check(text.Contains("ReadOnlyBoundPreflight", StringComparison.Ordinal) &&
                text.Contains("InputAttempted\":false", StringComparison.Ordinal) &&
                !text.Contains("192.168.56.128", StringComparison.Ordinal) &&
                !text.Contains("rift_x64", StringComparison.Ordinal) &&
                !text.Contains(Hash, StringComparison.Ordinal) &&
                !text.Contains("source-a", StringComparison.Ordinal),
                "only safe diagnostic fields are published");
        });
    }
}
