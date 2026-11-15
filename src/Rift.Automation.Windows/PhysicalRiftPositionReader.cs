using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Rift.Automation.Windows;

/// <summary>Evidence classification for one read-only, exact-build native XYZ acquisition.</summary>
public enum PhysicalRiftPositionStatus
{
    Candidate,
    Unavailable,
    IdentityMismatch,
    Error
}

/// <summary>
/// A newly acquired Candidate is not proof that RIFT passively refreshed its position.
/// No producer clock is supplied. It must not be promoted to qualified motion feedback.
/// </summary>
public sealed record PhysicalRiftPositionResult(
    PhysicalRiftPositionStatus Status,
    string Reason,
    int ProcessId,
    long ProcessStartUtcTicks,
    string? ExecutableSha256,
    ulong? PlayerUnitKey,
    float? X,
    float? Y,
    float? Z,
    DateTimeOffset CapturedAtUtc,
    double ElapsedMilliseconds);

/// <summary>
/// Standalone physical RIFT read-only position candidate, adapted from RR2's verified
/// CurrentBuildPlayerLookup and CurrentBuildPositionCandidateReader. No RR2 services,
/// Lua addon, foreground focus, gameplay input or whole-process scan is used.
/// The exact-build offsets are rejected for any other game executable SHA-256.
/// </summary>
public static class PhysicalRiftPositionReader
{
    public const string GameSha256 = "1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8";
    internal const ulong ManagerRva = 0x33624A8;
    internal const ulong PlayerSystemRva = 0x33623E0;
    internal const ulong PositionIndexRva = 0x2F588A0;
    internal const ulong PositionComponentVptrRva = 0x26B4FB0;
    private const ulong MinimumUserAddress = 0x10000;
    private const ulong MaximumUserAddress = 0x7FFF_FFFF_FFFF;
    private const uint ProcessReadAccess = 0x0010 | 0x0400 | 0x1000 | 0x0010_0000;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = 0xFFFF_FFFF;

    public static async Task<PhysicalRiftPositionResult> ReadPinnedAsync(
        int processId, long expectedStartUtcTicks, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        PhysicalRiftPositionResult Failure(PhysicalRiftPositionStatus status, string reason) =>
            new(status, reason, processId, expectedStartUtcTicks, null,
                null, null, null, null, DateTimeOffset.UtcNow, watch.Elapsed.TotalMilliseconds);

        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            return Failure(PhysicalRiftPositionStatus.Unavailable, "Requires an x64 Windows process.");
        if (processId <= 0 || expectedStartUtcTicks <= 0)
            return Failure(PhysicalRiftPositionStatus.IdentityMismatch, "A pinned PID and process UTC-start ticks are required.");
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Cooperates with RR2's per-process native read lease. This is a file-lock
            // protocol, not a dependency on an RR2 process or service.
            await using var lease = await PhysicalRiftNativeReadLease.AcquireAsync(
                processId, expectedStartUtcTicks, GameSha256,
                TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

            using SafeProcessHandle handle = OpenProcess(ProcessReadAccess, false, processId);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess for native read failed.");
            cancellationToken.ThrowIfCancellationRequested();
            var actualStartUtcTicks = GetPinnedStartTicks(handle);
            if (actualStartUtcTicks != expectedStartUtcTicks)
                throw new PhysicalRiftIdentityException("Process start ticks differ from requested epoch.");

            var path = GetExecutablePath(handle);
            var sha = await HashExecutableAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sha, GameSha256, StringComparison.OrdinalIgnoreCase))
                throw new PhysicalRiftIdentityException("Executable SHA-256 is not this verified RIFT build.");
            var (moduleBase, moduleSize) = GetMainModule(processId, path);
            RequireUnchangedIdentity(handle, expectedStartUtcTicks, path);

            var reader = new PhysicalRiftNativeMemory(handle, moduleBase, moduleSize, cancellationToken);
            var (unitKey, x, y, z) = reader.ReadAnchoredPlayerPosition();
            cancellationToken.ThrowIfCancellationRequested();

            RequireUnchangedIdentity(handle, expectedStartUtcTicks, path);
            var moduleAfter = GetMainModule(processId, path);
            if (moduleAfter.BaseAddress != moduleBase || moduleAfter.Size != moduleSize)
                throw new PhysicalRiftIdentityException("RIFT module mapping changed after position acquisition.");
            var shaAfter = await HashExecutableAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(shaAfter, sha, StringComparison.Ordinal))
                throw new PhysicalRiftIdentityException("On-disk RIFT executable changed after capture.");
            RequireUnchangedIdentity(handle, expectedStartUtcTicks, path);

            return new(PhysicalRiftPositionStatus.Candidate,
                "current-build-native-candidate; passive-producer-refresh-not-qualified",
                processId, expectedStartUtcTicks, sha, unitKey, x, y, z,
                DateTimeOffset.UtcNow, watch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PhysicalRiftIdentityException error)
        {
            return Failure(PhysicalRiftPositionStatus.IdentityMismatch, error.Message);
        }
        catch (TimeoutException)
        {
            return Failure(PhysicalRiftPositionStatus.Unavailable, "Native-read lease was busy for two seconds.");
        }
        catch (InvalidDataException error)
        {
            return Failure(PhysicalRiftPositionStatus.Unavailable, error.Message);
        }
        catch (Win32Exception error)
        {
            return Failure(PhysicalRiftPositionStatus.Unavailable,
                string.Create(CultureInfo.InvariantCulture, $"Windows native read unavailable (Win32 {error.NativeErrorCode})."));
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(PhysicalRiftPositionStatus.Unavailable, "Native-read access or lease directory denied.");
        }
        catch (IOException)
        {
            return Failure(PhysicalRiftPositionStatus.Unavailable, "Native-read file or lease unavailable.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException
            or NotSupportedException or System.Security.SecurityException)
        {
            return Failure(PhysicalRiftPositionStatus.Error, error.GetType().Name);
        }
    }

    private static long GetPinnedStartTicks(SafeProcessHandle handle)
    {
        uint wait = WaitForSingleObject(handle, 0);
        if (wait == 0)
            throw new PhysicalRiftIdentityException("RIFT process is no longer live.");
        if (wait == WaitFailed)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed.");
        if (wait != WaitTimeout)
            throw new PhysicalRiftIdentityException("Unexpected RIFT process wait status.");
        if (!GetProcessTimes(handle, out long createdFileTime, out long exitFileTime, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcessTimes failed.");
        if (exitFileTime != 0)
            throw new PhysicalRiftIdentityException("RIFT process has an exit timestamp.");
        try { return DateTime.FromFileTimeUtc(createdFileTime).Ticks; }
        catch (ArgumentOutOfRangeException)
        {
            throw new PhysicalRiftIdentityException("RIFT process returned an invalid creation time.");
        }
    }

    private static string GetExecutablePath(SafeProcessHandle handle)
    {
        var name = new StringBuilder(32768);
        int capacity = name.Capacity;
        if (!QueryFullProcessImageName(handle, 0, name, ref capacity) || capacity <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryFullProcessImageName failed.");
        return Path.GetFullPath(name.ToString());
    }

    private static void RequireUnchangedIdentity(SafeProcessHandle handle, long startUtcTicks, string path)
    {
        if (GetPinnedStartTicks(handle) != startUtcTicks ||
            !string.Equals(GetExecutablePath(handle), path, StringComparison.OrdinalIgnoreCase))
            throw new PhysicalRiftIdentityException("RIFT process identity changed during sampling.");
    }

    private static async Task<string> HashExecutableAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static (ulong BaseAddress, ulong Size) GetMainModule(int processId, string expectedPath)
    {
        using Process process = Process.GetProcessById(processId);
        process.Refresh();
        ProcessModule? module = process.MainModule;
        if (module is null || module.ModuleMemorySize <= 0)
            throw new InvalidDataException("RIFT main module unavailable.");
        string fileName = Path.GetFullPath(module.FileName);
        if (!string.Equals(fileName, expectedPath, StringComparison.OrdinalIgnoreCase))
            throw new PhysicalRiftIdentityException("RIFT main module path differs from pinned executable.");
        ulong moduleBase = checked((ulong)module.BaseAddress.ToInt64());
        ulong size = checked((ulong)module.ModuleMemorySize);
        if (moduleBase < MinimumUserAddress || moduleBase > MaximumUserAddress ||
            size < ManagerRva + 8 || size > MaximumUserAddress - moduleBase)
            throw new InvalidDataException("RIFT main module lies outside expected image bounds.");
        return (moduleBase, size);
    }

    private sealed class PhysicalRiftNativeMemory(
        SafeProcessHandle handle, ulong moduleBase, ulong moduleSize, CancellationToken token)
    {
        private readonly List<(ulong Address, byte[] Bytes)> anchors = new(32);

        internal (ulong UnitKey, float X, float Y, float Z) ReadAnchoredPlayerPosition()
        {
            token.ThrowIfCancellationRequested();
            ulong system = Pointer(U64(Take(checked(moduleBase + PlayerSystemRva), 8)));
            ulong key = U64(Take(checked(system + 0x360), 8));
            if (key == 0) throw new InvalidDataException("Player key is missing.");
            ulong manager = Pointer(U64(Take(checked(moduleBase + ManagerRva), 8)));
            ulong table = Pointer(U64(Take(checked(manager + 0x50), 8)));
            byte[] header = Take(table, 24);
            ulong buckets = Pointer(U64(header.AsSpan(0, 8)));
            ulong stride = U64(header.AsSpan(8, 8));
            ulong count = U64(header.AsSpan(16, 8));
            if (stride is < 24 or > 4096 || stride % 8 != 0 || count is < 1 or > 1 << 24)
                throw new InvalidDataException("Native player table header is out of bounds.");

            ulong current = Pointer(checked(buckets + BucketIndex(key, count) * stride), 24);
            var visited = new HashSet<ulong>();
            ulong entity = 0;
            for (int index = 0; index < 8; ++index)
            {
                token.ThrowIfCancellationRequested();
                if (!visited.Add(current))
                    throw new InvalidDataException("Player bucket traversal is cyclic.");
                byte[] node = Take(current, 24);
                ulong taggedNext = U64(node.AsSpan(0, 8));
                ulong candidateKey = U64(node.AsSpan(8, 8));
                if ((index > 0 || (taggedNext & 1) == 0) && candidateKey == key)
                {
                    entity = Pointer(U64(node.AsSpan(16, 8)), 128);
                    break;
                }
                ulong next = taggedNext & ~1UL;
                if (next == 0)
                    throw new InvalidDataException("Player was absent from the native entity table.");
                current = Pointer(next, 24);
            }
            if (entity == 0)
                throw new InvalidDataException("Player entity lookup exceeded its eight-node bound.");

            byte[] entityBytes = Take(entity, 128);
            if (PositionIndexRva > moduleSize - 4)
                throw new InvalidDataException("Position selector lies outside the pinned module.");
            int positionIndex = BinaryPrimitives.ReadInt32LittleEndian(
                Take(checked(moduleBase + PositionIndexRva), 4));
            if (positionIndex is < 0 or >= 0x60)
                throw new InvalidDataException("Position component selector is invalid.");
            ulong componentTable = Pointer(U64(entityBytes.AsSpan(0x78, 8)));
            byte slot = entityBytes[0x18 + positionIndex];
            if (slot == 0xFF)
                throw new InvalidDataException("Player position component is absent.");
            ulong componentSlot = Pointer(checked(componentTable + (ulong)slot * 8));
            ulong component = Pointer(U64(Take(componentSlot, 8)), 0x110);

            byte[] payload = ReadExact(component, 0x110);
            ulong expectedVptr = checked(moduleBase + PositionComponentVptrRva);
            if (U64(payload.AsSpan(0, 8)) != expectedVptr)
                throw new InvalidDataException("Position component dispatch pointer mismatches this build.");
            if (U64(payload.AsSpan(0x10, 8)) != entity)
                throw new InvalidDataException("Position component entity backlink mismatches player.");

            byte[] xyzRaw = payload.AsSpan(0x48, 12).ToArray();
            VerifyAnchors();
            if (!ReadExact(component, 8).AsSpan().SequenceEqual(payload.AsSpan(0, 8)) ||
                !ReadExact(checked(component + 0x48), 12).AsSpan().SequenceEqual(xyzRaw) ||
                !ReadExact(checked(component + 0x10), 8).AsSpan().SequenceEqual(payload.AsSpan(0x10, 8)))
                throw new InvalidDataException("Position payload changed during the read.");
            VerifyAnchors();

            float x = BinaryPrimitives.ReadSingleLittleEndian(xyzRaw.AsSpan(0, 4));
            float y = BinaryPrimitives.ReadSingleLittleEndian(xyzRaw.AsSpan(4, 4));
            float z = BinaryPrimitives.ReadSingleLittleEndian(xyzRaw.AsSpan(8, 4));
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new InvalidDataException("Position XYZ is nonfinite.");
            return (key, x, y, z);
        }

        private void VerifyAnchors()
        {
            foreach (var (address, bytes) in anchors)
            {
                token.ThrowIfCancellationRequested();
                if (!ReadExact(address, bytes.Length).AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("Native player ownership changed during the read.");
            }
        }

        private byte[] Take(ulong address, int length)
        {
            byte[] bytes = ReadExact(address, length);
            anchors.Add((address, (byte[])bytes.Clone()));
            return bytes;
        }

        private byte[] ReadExact(ulong address, int length)
        {
            token.ThrowIfCancellationRequested();
            if (length is < 1 or > 4096 || address < MinimumUserAddress ||
                address > MaximumUserAddress - (ulong)length)
                throw new InvalidDataException("Native read range falls outside bounded user memory.");
            var bytes = GC.AllocateUninitializedArray<byte>(length);
            bool ok = ReadProcessMemory(handle, new nint(checked((long)address)),
                bytes, (nuint)length, out nuint read);
            if (!ok || read != (nuint)length)
                throw new InvalidDataException("Native process memory was unreadable or partial.");
            return bytes;
        }

        internal static ulong BucketIndex(ulong key, ulong count)
        {
            if (count is < 1 or > 1 << 24)
                throw new ArgumentOutOfRangeException(nameof(count));
            int low = unchecked((int)(uint)key);
            int quotient = low / 127773;
            int remainder = low - quotient * 127773;
            int signed = unchecked((int)((long)remainder * 16807 - (long)quotient * 2836));
            long adjusted = signed >= 0 ? signed : (long)signed + 2147483647;
            return unchecked((uint)adjusted) % count;
        }

        private static ulong U64(ReadOnlySpan<byte> bytes) =>
            BinaryPrimitives.ReadUInt64LittleEndian(bytes);

        private static ulong Pointer(ulong address, int length = 8)
        {
            if (length < 1 || address < MinimumUserAddress || address % 8 != 0 ||
                address > MaximumUserAddress - (ulong)length)
                throw new InvalidDataException("Native process pointer failed bounds/alignment.");
            return address;
        }
    }

    private sealed class PhysicalRiftIdentityException(string message) : IOException(message);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        SafeProcessHandle process, nint address, [Out] byte[] buffer, nuint size, out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}

/// <summary>RR2-compatible cooperative lease, with no RR2 runtime dependency.</summary>
internal sealed class PhysicalRiftNativeReadLease : IAsyncDisposable
{
    private FileStream? stream;

    private PhysicalRiftNativeReadLease(FileStream stream) => this.stream = stream;

    internal static async Task<PhysicalRiftNativeReadLease> AcquireAsync(
        int pid, long startUtcTicks, string sha256, TimeSpan timeout, CancellationToken token)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RiftReader2", "NativeReadLeases");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Native-read lease directory is invalid.");
        if (new Uri(root).IsUnc)
            throw new InvalidDataException("Native-read lease directory must be local.");
        Directory.CreateDirectory(root);
        for (DirectoryInfo? node = new DirectoryInfo(root); node is not null; node = node.Parent)
            if ((node.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Native-read lease directory is redirected.");

        var identity = string.Join(":", "RR2-NATIVE-READ-1",
            pid.ToString(CultureInfo.InvariantCulture),
            startUtcTicks.ToString(CultureInfo.InvariantCulture), sha256);
        string name = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(identity))) + ".lock";
        string path = Path.Combine(root, name);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                RejectReparse(path, true);
                var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    RejectReparse(path, false);
                    return new PhysicalRiftNativeReadLease(file);
                }
                catch
                {
                    file.Dispose();
                    throw;
                }
            }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33)
            {
                var remaining = timeout - timer.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException("Native-read lease is busy.", error);
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(50)
                    ? remaining : TimeSpan.FromMilliseconds(50), token).ConfigureAwait(false);
            }
        }
    }

    private static void RejectReparse(string path, bool allowMissing)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Native-read lease file is redirected.");
        }
        catch (FileNotFoundException) when (allowMissing) { }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref stream, null)?.Dispose();
        return ValueTask.CompletedTask;
    }
}
