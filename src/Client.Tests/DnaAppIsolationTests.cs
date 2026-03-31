using Dna.Core.Framework;
using Xunit;

namespace Client.Tests;

public class DnaAppIsolationTests
{
    [Fact]
    public async Task Create_ShouldKeepCliRegistrationsIsolatedPerInstance()
    {
        var app1 = DnaApp.Create(["cli", "alpha"], new AppOptions { AppName = "app-1" });
        app1.AddCliCommand(new StubCliCommand("alpha", "first command", 7));

        var app2 = DnaApp.Create(["cli", "alpha"], new AppOptions { AppName = "app-2" });

        var firstResult = await app1.RunAsync();
        var secondResult = await app2.RunAsync();

        Assert.Equal(7, firstResult);
        Assert.Equal(1, secondResult);
    }

    private sealed class StubCliCommand(string name, string description, int result) : ICliCommand
    {
        public string Name => name;
        public string Description => description;

        public Task<int> ExecuteAsync(string[] args)
        {
            _ = args;
            return Task.FromResult(result);
        }
    }
}
