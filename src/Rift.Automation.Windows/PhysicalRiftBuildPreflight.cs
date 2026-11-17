using System.Security.Cryptography;

namespace Rift.Automation.Windows;

/// <summary>A disk-only RIFT build comparison, never a source or input qualification.</summary>
public enum PhysicalRiftBuildCheckState
{
    MatchesPinnedBuild,
    RequiresRequalification,
    Unavailable,
    InvalidPath,
    ChangedDuringRead
}

public sealed record PhysicalRiftBuildCheckReport(
    PhysicalRiftBuildCheckState State,
    string PinnedSha256,
    string? ObservedSha256,
    long? FileLength,
    DateTimeOffset? LastWriteUtc)
{
    public bool CanAutomate => false;
    public bool NativeOffsetsRequalified => false;
    public bool MatchesPinnedFingerprint => State == PhysicalRiftBuildCheckState.MatchesPinnedBuild;
}

/// <summary>
/// Hashes a caller-selected on-disk RIFT EXE without enumerating/opening processes
/// or reading game memory. A match does not prove any live source is qualified.
/// </summary>
public static class PhysicalRiftBuildPreflight
{
    private const long MaximumExecutableBytes = 1L << 30;

    public static Task<PhysicalRiftBuildCheckReport> CheckAsync(
        string? executablePath, CancellationToken cancellationToken = default) =>
        CheckAgainstExpectedAsync(executablePath, PhysicalRiftPositionReader.GameSha256, cancellationToken);

    internal static async Task<PhysicalRiftBuildCheckReport> CheckAgainstExpectedAsync(
        string? executablePath, string expectedSha256, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected SHA-256 must be 64 hexadecimal characters.", nameof(expectedSha256));

        PhysicalRiftBuildCheckReport Report(PhysicalRiftBuildCheckState state,
            string? observed = null, long? length = null, DateTimeOffset? modified = null) =>
            new(state, expectedSha256.ToUpperInvariant(), observed, length, modified);

        if (string.IsNullOrWhiteSpace(executablePath))
            return Report(PhysicalRiftBuildCheckState.InvalidPath);

        string path;
        try
        {
            if (!Path.IsPathFullyQualified(executablePath) ||
                !string.Equals(Path.GetFileName(executablePath), "rift_x64.exe", StringComparison.OrdinalIgnoreCase))
                return Report(PhysicalRiftBuildCheckState.InvalidPath);
            path = Path.GetFullPath(executablePath);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Report(PhysicalRiftBuildCheckState.InvalidPath);
        }

        try
        {
            var before = new FileInfo(path);
            if (!before.Exists)
                return Report(PhysicalRiftBuildCheckState.Unavailable);
            if ((before.Attributes & FileAttributes.ReparsePoint) != 0 ||
                before.Length is <= 0 or > MaximumExecutableBytes)
                return Report(PhysicalRiftBuildCheckState.Unavailable);

            long length = before.Length;
            DateTime modifiedUtc = before.LastWriteTimeUtc;
            DateTime createdUtc = before.CreationTimeUtc;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 128 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
            if (stream.Length != length)
                return Report(PhysicalRiftBuildCheckState.ChangedDuringRead);
            string observed = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            cancellationToken.ThrowIfCancellationRequested();

            before.Refresh();
            if (!before.Exists || before.Length != length ||
                before.LastWriteTimeUtc != modifiedUtc || before.CreationTimeUtc != createdUtc ||
                (before.Attributes & FileAttributes.ReparsePoint) != 0)
                return Report(PhysicalRiftBuildCheckState.ChangedDuringRead);
            return Report(string.Equals(observed, expectedSha256, StringComparison.OrdinalIgnoreCase)
                    ? PhysicalRiftBuildCheckState.MatchesPinnedBuild
                    : PhysicalRiftBuildCheckState.RequiresRequalification,
                observed, length, new DateTimeOffset(modifiedUtc, TimeSpan.Zero));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return Report(PhysicalRiftBuildCheckState.Unavailable);
        }
    }
}
