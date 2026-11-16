using System.Text;
using System.Text.Json;
using Rift.Automation.Core;

namespace Rift.Automation.Specs;

internal static class SpecRunner
{
    private static int _passed;
    private static int _failed;

    internal static void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    internal static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    internal static string Trace(ReplayScenario scenario)
    {
        var player = new ReplayPlayer(scenario);
        var output = new StringBuilder();
        output.AppendLine(TraceLine(player));
        while (player.Step())
            output.AppendLine(TraceLine(player));
        return output.ToString();
    }

    private static string TraceLine(ReplayPlayer player) =>
        JsonSerializer.Serialize(new { player.Index, player.NowMs, player.View, player.LastResultCode });

    public static int Run(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Synthetic offline observer specs: [selftest] | replay <path> | analyze <path> | --help");
            Console.WriteLine("No game access, memory reader, input executor or deployment.");
            Console.WriteLine("Analyze reports synthetic millisecond-slot coverage and pipeline latencies, not live qualification.");
            return 0;
        }
        if (args is [string mode, string path] && (mode == "replay" || mode == "analyze"))
        {
            try
            {
                using var stream = File.OpenRead(path);
                ReplayReadResult result = ReplayReader.Read(stream);
                if (!result.Success || result.Scenario is null)
                {
                    Console.Error.WriteLine("Synthetic replay rejected: " + result.ErrorCode);
                    return 2;
                }
                if (mode == "analyze")
                {
                    ReplayAnalysisReport report = ReplayAnalysis.Analyze(result.Scenario);
                    Console.WriteLine(JsonSerializer.Serialize(report));
                    return 0;
                }
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    traceSchema = "rift-observer-trace/1",
                    origin = "SyntheticReplay",
                    scenario = result.Scenario.Name,
                    canAutomate = false
                }));
                Console.Write(Trace(result.Scenario));
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                Console.Error.WriteLine("Synthetic replay file unavailable.");
                return 2;
            }
        }
        if (args.Length > 1 || (args.Length == 1 && args[0] != "selftest"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 2;
        }

        CoreSpecs.Run();
        CombatPlannerSpecs.Run();
        CombatEffectSpecs.Run();
        CombatOutcomeSpecs.Run();
        ReplaySpecs.Run();
        ReplayAnalysisSpecs.Run();
        Console.WriteLine($"Synthetic offline specs: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }
}
