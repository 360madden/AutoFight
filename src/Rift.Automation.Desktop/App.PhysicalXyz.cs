using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class App
{
    private static async Task<int> RunPhysicalXyzAsync(string processIdText, string startTicksText)
    {
        if (!int.TryParse(processIdText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || pid <= 0
            || !long.TryParse(startTicksText, NumberStyles.None, CultureInfo.InvariantCulture, out long startTicks)
            || startTicks <= 0
            || startTicks > DateTime.MaxValue.Ticks)
        {
            Console.Error.WriteLine("Physical XYZ: expected a positive PID and exact process creation UTC ticks from --clients; no process opened.");
            return 2;
        }

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            PhysicalRiftPositionResult result = await PhysicalRiftPositionReader.ReadPinnedAsync(
                pid, startTicks, deadline.Token);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mode = "AutoFightPhysicalNativeXyz",
                sample = result,
                passiveFreshnessQualified = false,
                sustainedQualification = false,
                canAutomate = false
            }, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter<PhysicalRiftPositionStatus>() }
            }));
            return result.Status == PhysicalRiftPositionStatus.Candidate ? 0 : 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Physical XYZ: read cancelled or exceeded 15 seconds; no input attempted.");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or Win32Exception
            or SecurityException or NotSupportedException)
        {
            Console.Error.WriteLine("Physical XYZ: bounded read unavailable; no input attempted.");
            return 2;
        }
    }
}
