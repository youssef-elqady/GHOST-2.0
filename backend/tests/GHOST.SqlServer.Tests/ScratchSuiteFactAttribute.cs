namespace GHOST.SqlServer.Tests;

/// <summary>Skipped unless GHOST_SCRATCH_TEST_CONNECTION_STRING is set. A set but unsafe value fails closed.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ScratchFactAttribute : FactAttribute
{
    public ScratchFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScratchDb.EnvironmentVariableName)))
            Skip = $"Set {ScratchDb.EnvironmentVariableName} (GHOST_SCRATCH_* database) to run.";
    }
}
