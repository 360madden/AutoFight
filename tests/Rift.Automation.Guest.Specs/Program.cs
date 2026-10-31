int passed = 0, failed = 0;

void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}

void Test(string name, Action action)
{
    try
    {
        action();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine(
            $"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
    }
}

GuestWireSpecs.Run(Test, Check);

Console.WriteLine($"AutoFight guest reader specs: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
