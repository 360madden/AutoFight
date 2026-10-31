using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

[assembly: InternalsVisibleTo("Rift.Automation.Windows.Specs")]

namespace Rift.Automation.Windows;

public sealed record RiftClientInfo(
    int ProcessId, DateTimeOffset? StartedAtUtc, string? ImagePath,
    int? WindowsSessionId, long? WindowHandle, string? WindowTitle,
    string Status, string Detail)
{
    // A selection hint, not an attachment or a promise the process still exists.
    public string? Identity => StartedAtUtc is { } start
        ? string.Create(CultureInfo.InvariantCulture, $"{ProcessId}:{start.UtcTicks}") : null;
    public string DisplayName => $"PID {ProcessId} | {Status} | {WindowTitle ?? "No window title"}";
}

public sealed record ClientInventorySnapshot(
    DateTimeOffset CheckedAtUtc, double ElapsedMs,
    IReadOnlyList<RiftClientInfo> Clients, string? ErrorCode, bool Truncated)
{
    public string Mode => "WindowsProcessMetadata";
    public bool HasGameState => false;
}

/// <summary>On-demand local process metadata only. No memory sampling or game input.</summary>
public static class RiftClientInventory
{
    private const int MaximumClients = 64;
    private const uint QueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    public static ClientInventorySnapshot Scan(CancellationToken cancellationToken = default) =>
        ScanNamed("rift_x64", cancellationToken);

    internal static ClientInventorySnapshot ScanNamed(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        long start = Stopwatch.GetTimestamp();
        Process[] processes;
        try { processes = Process.GetProcessesByName(name); }
        catch (Exception ex) when (IsExpected(ex))
        {
            return new(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                Array.Empty<RiftClientInfo>(), "process_list_unavailable", false);
        }

        try
        {
            var clients = new List<RiftClientInfo>(Math.Min(processes.Length, MaximumClients));
            foreach (Process process in processes.Take(MaximumClients))
            {
                cancellationToken.ThrowIfCancellationRequested();
                clients.Add(Inspect(process));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                Array.AsReadOnly(clients.OrderBy(client => client.ProcessId).ToArray()),
                null, processes.Length > MaximumClients);
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    internal static RiftClientInfo Inspect(Process process)
    {
        int pid = process.Id;
        DateTimeOffset? created = null;
        string? imagePath = null, title = null;
        int? sessionId = null;
        long? window = null;
        var issues = new List<string>();
        // Only query permission is requested; no VM_READ, VM_WRITE or input access.
        using SafeProcessHandle handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            return new(pid, null, null, null, null, null, "Unavailable",
                error == 5 ? "Access denied; client was not inspected." : "Process exited or could not be opened.");
        }

        if (GetProcessTimes(handle, out long createdFileTime, out _, out _, out _))
        {
            try { created = new DateTimeOffset(DateTime.FromFileTimeUtc(createdFileTime)); }
            catch (ArgumentOutOfRangeException) { issues.Add("Invalid process creation time."); }
        }
        else issues.Add("Process creation time unavailable.");

        var pathBuffer = new StringBuilder(32768);
        int pathLength = pathBuffer.Capacity;
        if (QueryFullProcessImageName(handle, 0, pathBuffer, ref pathLength))
            imagePath = pathBuffer.ToString();
        else issues.Add("Executable path unavailable.");

        try { sessionId = process.SessionId; }
        catch (Exception ex) when (IsExpected(ex)) { issues.Add("Windows session unavailable."); }
        try
        {
            process.Refresh();
            window = process.MainWindowHandle.ToInt64();
            title = CleanTitle(process.MainWindowTitle);
        }
        catch (Exception ex) when (IsExpected(ex)) { issues.Add("Window metadata unavailable."); }

        // The pinned query handle belongs to the process observed above, even if its PID is reused.
        if (!GetExitCodeProcess(handle, out uint exitCode))
            return new(pid, null, null, null, null, null, "Unavailable", "Process liveness could not be checked.");
        if (exitCode != StillActive)
            return new(pid, created, null, null, null, null, "Exited", "Process exited during inspection; refresh the list.");

        string status = issues.Count > 0 ? "Partial" : window is null or 0 ? "No window" : "Detected";
        return new(pid, created, imagePath, sessionId, window, title, status,
            issues.Count > 0 ? string.Join(" ", issues) : "Metadata only. Game data is not connected.");
    }

    internal static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        string result = new(title.Take(256).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return result.Trim();
    }

    private static bool IsExpected(Exception ex) => ex is Win32Exception or InvalidOperationException
        or NotSupportedException or UnauthorizedAccessException or System.Security.SecurityException;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
