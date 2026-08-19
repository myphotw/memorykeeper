namespace MemoryKeeper.Tests.UnitTests;

public sealed class LiveBackendFactAttribute : FactAttribute
{
    public LiveBackendFactAttribute()
    {
        if (!IsEnabled("RUN_LIVE_BACKEND_TESTS"))
        {
            Skip = "Set RUN_LIVE_BACKEND_TESTS=true to run read-only TC-Backend tests.";
        }
    }

    internal static bool IsEnabled(string variableName) =>
        string.Equals(
            Environment.GetEnvironmentVariable(variableName),
            "true",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class LiveBackendWriteFactAttribute : FactAttribute
{
    public LiveBackendWriteFactAttribute()
    {
        if (!LiveBackendFactAttribute.IsEnabled("RUN_LIVE_BACKEND_TESTS")
            || !LiveBackendFactAttribute.IsEnabled("RUN_LIVE_BACKEND_WRITE_TESTS"))
        {
            Skip = "Live write tests require both RUN_LIVE_BACKEND_TESTS=true and RUN_LIVE_BACKEND_WRITE_TESTS=true.";
        }
    }
}
