using System.Diagnostics;

namespace Rift.Automation.Windows;

/// <summary>Conservative diagnostic only: file timestamps cannot prove addon activation.</summary>
public static class PhysicalAddonReloadPreflight
{
    public static string? Inspect(int gameProcessId, string addonDirectory)
    {
        if (gameProcessId <= 0 || string.IsNullOrWhiteSpace(addonDirectory))
            throw new ArgumentException("A positive PID and addon directory are required.");
        if (RequiredFiles(addonDirectory).Any(path => !File.Exists(path)))
            return "AutoFight addon files are missing; activation cannot be confirmed.";
        using var game = Process.GetProcessById(gameProcessId);
        if (game.HasExited || !string.Equals(game.ProcessName, "rift_x64", StringComparison.OrdinalIgnoreCase))
            return "Pinned RIFT process unavailable.";
        return InspectFilesAtStart(addonDirectory, game.StartTime.ToUniversalTime());
    }

    internal static string? InspectFilesAtStart(string addonDirectory, DateTime startedUtc)
    {
        if (string.IsNullOrWhiteSpace(addonDirectory) || startedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Addon directory and UTC process start are required.");
        var files = RequiredFiles(addonDirectory);
        if (files.Any(path => !File.Exists(path)))
            return "AutoFight addon files are missing; activation cannot be confirmed.";
        var newer = files.Where(path => File.GetLastWriteTimeUtc(path) > startedUtc.AddSeconds(2))
            .Select(Path.GetFileName).ToArray();
        return newer.Length == 0 ? null :
            "Installed AutoFight files changed after RIFT started (" +
            string.Join(", ", newer) +
            "). Run /reloadui in the correct game client before binding; file timestamps alone cannot prove the loaded addon version.";
    }

    private static string[] RequiredFiles(string directory) =>
        new[] { "RiftAddon.toc", "main.lua", "target_vitals_v2.lua" }
            .Select(name => Path.Combine(directory, name)).ToArray();
}
