using System.Security.Cryptography;
using Rift.Automation.Windows;

internal static class PhysicalRiftBuildPreflightSpecs
{
    private static void WithFixture(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "AutoFight-BuildPreflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "rift_x64.exe")); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static PhysicalRiftBuildCheckReport Check(string path, string expected) =>
        PhysicalRiftBuildPreflight.CheckAgainstExpectedAsync(path, expected)
            .GetAwaiter().GetResult();

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("RIFT update preflight matches synthetic executable without process access", () =>
            WithFixture(path =>
            {
                byte[] contents = "standalone-test-rift-binary"u8.ToArray();
                File.WriteAllBytes(path, contents);
                string sha = Convert.ToHexString(SHA256.HashData(contents));
                var result = Check(path, sha.ToLowerInvariant());
                check(result.State == PhysicalRiftBuildCheckState.MatchesPinnedBuild &&
                      result.MatchesPinnedFingerprint && result.ObservedSha256 == sha &&
                      result.PinnedSha256 == sha && result.FileLength == contents.Length &&
                      result.LastWriteUtc is not null && !result.CanAutomate &&
                      !result.NativeOffsetsRequalified,
                    "matching disk bytes are diagnostic only and do not qualify offsets or automation");
            }));

        test("RIFT update preflight requires requalification after changed executable bytes", () =>
            WithFixture(path =>
            {
                File.WriteAllBytes(path, "new-game-build"u8.ToArray());
                var result = PhysicalRiftBuildPreflight.CheckAsync(path).GetAwaiter().GetResult();
                check(result.State == PhysicalRiftBuildCheckState.RequiresRequalification &&
                      !result.MatchesPinnedFingerprint && !result.NativeOffsetsRequalified &&
                      !result.CanAutomate && result.ObservedSha256 is { Length: 64 } &&
                      result.ObservedSha256 != result.PinnedSha256,
                    "changed build must never inherit old offsets or input capability");
            }));

        test("RIFT update preflight cannot read missing, empty or directory paths as a match", () =>
            WithFixture(path =>
            {
                var missing = PhysicalRiftBuildPreflight.CheckAsync(path).GetAwaiter().GetResult();
                File.WriteAllBytes(path, Array.Empty<byte>());
                var empty = PhysicalRiftBuildPreflight.CheckAsync(path).GetAwaiter().GetResult();
                var directory = PhysicalRiftBuildPreflight.CheckAsync(Path.GetDirectoryName(path))
                    .GetAwaiter().GetResult();
                check(missing.State == PhysicalRiftBuildCheckState.Unavailable &&
                      empty.State == PhysicalRiftBuildCheckState.Unavailable &&
                      directory.State == PhysicalRiftBuildCheckState.InvalidPath &&
                      missing.ObservedSha256 is null && empty.ObservedSha256 is null,
                    "unavailable binary and directory do not expose fabricated hashes");
            }));

        test("RIFT update preflight rejects relative paths and non-RIFT executable names", () =>
        {
            var relative = PhysicalRiftBuildPreflight.CheckAsync("rift_x64.exe").GetAwaiter().GetResult();
            var wrong = PhysicalRiftBuildPreflight.CheckAsync(
                Path.Combine(Path.GetTempPath(), "not-rift.exe")).GetAwaiter().GetResult();
            var blank = PhysicalRiftBuildPreflight.CheckAsync("  ").GetAwaiter().GetResult();
            var malformed = PhysicalRiftBuildPreflight.CheckAsync(
                "C:\\invalid" + '\u0000' + "\\rift_x64.exe").GetAwaiter().GetResult();
            check(relative.State == PhysicalRiftBuildCheckState.InvalidPath &&
                  wrong.State == PhysicalRiftBuildCheckState.InvalidPath &&
                  blank.State == PhysicalRiftBuildCheckState.InvalidPath &&
                  malformed.State == PhysicalRiftBuildCheckState.InvalidPath &&
                  malformed.ObservedSha256 is null &&
                  !relative.CanAutomate && !wrong.CanAutomate,
                "only explicit absolute rift_x64.exe path is accepted");
        });

        test("RIFT update preflight cancellation happens before touching a file", () =>
            WithFixture(path =>
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                try
                {
                    PhysicalRiftBuildPreflight.CheckAsync(path, cancellation.Token)
                        .GetAwaiter().GetResult();
                    check(false, "canceled check unexpectedly returned");
                }
                catch (OperationCanceledException) { check(true, "canceled before file access"); }
            }));

        test("RIFT update preflight cannot substitute a malformed expected signature", () =>
            WithFixture(path =>
            {
                try
                {
                    Check(path, "unknown");
                    check(false, "invalid expected signature was accepted");
                }
                catch (ArgumentException) { check(true, "signature is pinned and validated"); }
            }));
    }
}
