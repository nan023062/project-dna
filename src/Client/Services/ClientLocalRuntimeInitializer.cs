using Dna.Core.Config;
using Dna.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dna.Client.Services;

public sealed class ClientLocalRuntimeInitializer(
    ClientRuntimeOptions options,
    ProjectConfig projectConfig,
    IGraphEngine graph,
    IMemoryEngine memory,
    ILogger<ClientLocalRuntimeInitializer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var result = projectConfig.SetProject(options.WorkspaceRoot, options.MetadataRootPath);
        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        var storePath = projectConfig.DnaStorePath;
        graph.Initialize(storePath);
        memory.Initialize(storePath);
        graph.BuildTopology();

        logger.LogInformation(
            "Client local runtime initialized: project={ProjectName}, root={ProjectRoot}, store={StorePath}, api={ApiBaseUrl}",
            options.ProjectName,
            options.WorkspaceRoot,
            storePath,
            options.ApiBaseUrl);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
