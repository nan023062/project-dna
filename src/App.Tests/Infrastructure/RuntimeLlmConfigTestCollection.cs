using Dna.Core.Config;
using Xunit;

namespace App.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeLlmConfigTestCollection
{
    public const string Name = "RuntimeLlmConfig";
}

internal sealed class RuntimeLlmConfigPathOverrideScope : IDisposable
{
    private readonly string? _previousValue;

    public RuntimeLlmConfigPathOverrideScope(string filePath)
    {
        _previousValue = Environment.GetEnvironmentVariable(RuntimeLlmConfigPaths.OverridePathEnvironmentVariable);
        Environment.SetEnvironmentVariable(RuntimeLlmConfigPaths.OverridePathEnvironmentVariable, filePath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(RuntimeLlmConfigPaths.OverridePathEnvironmentVariable, _previousValue);
    }
}
