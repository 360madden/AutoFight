using Rift.Automation.Windows;

internal static class PhysicalRiftPositionSpecs
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Physical XYZ exact-build signature is pinned", () =>
        {
            string sha = PhysicalRiftPositionReader.GameSha256;
            check(sha.Length == 64 && sha.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F'),
                "only the exact previously verified uppercase RIFT build hash is allowed");
            check(sha == "1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8",
                "native offsets must not apply to a guessed build");
        });

        test("Physical XYZ requires an explicitly pinned PID and process epoch", () =>
        {
            var result = PhysicalRiftPositionReader.ReadPinnedAsync(0, 0).GetAwaiter().GetResult();
            check(result.Status == PhysicalRiftPositionStatus.IdentityMismatch &&
                result.ProcessId == 0 && result.ProcessStartUtcTicks == 0,
                "invalid process identity must fail closed without opening memory");
            check(result.X is null && result.Y is null && result.Z is null &&
                result.PlayerUnitKey is null && result.ExecutableSha256 is null,
                "invalid sample must not fabricate player XYZ, key, or an observed executable hash");
        });

        test("Physical XYZ cancellation precedes all process memory operations", () =>
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            try
            {
                PhysicalRiftPositionReader.ReadPinnedAsync(123456789, 111, source.Token)
                    .GetAwaiter().GetResult();
                check(false, "cancelled read must not complete");
            }
            catch (OperationCanceledException)
            {
                check(true, "cancelled read aborts before lease or process access");
            }
        });
    }
}
