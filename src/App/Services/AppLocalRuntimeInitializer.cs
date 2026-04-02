using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.Workspace;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dna.App.Services;

public sealed class AppLocalRuntimeInitializer(
    AppRuntimeOptions options,
    ProjectConfig projectConfig,
    ITopoGraphStore topoGraphStore,
    IGraphEngine graph,
    IMemoryEngine memory,
    IWorkspaceEngine workspace,
    ILogger<AppLocalRuntimeInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = projectConfig.SetProject(options.WorkspaceRoot, options.MetadataRootPath);
        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        var storage = AppProjectStoragePaths.Prepare(projectConfig.MetadataRootPath, logger);

        topoGraphStore.Initialize(storage.KnowledgeRootPath);
        graph.Initialize(storage.KnowledgeRootPath);
        memory.Initialize(storage.MemoryRootPath);

        var architecture = topoGraphStore.GetArchitecture();
        workspace.Initialize(options.WorkspaceRoot, architecture);
        var metadataSync = await workspace.EnsureDirectoryMetadataTreeAsync(
            options.WorkspaceRoot,
            architecture,
            cancellationToken: cancellationToken);

        graph.BuildTopology();

        logger.LogInformation(
            "App local runtime initialized: project={ProjectName}, root={ProjectRoot}, metadata={MetadataRoot}, memory={MemoryRoot}, knowledge={KnowledgeRoot}, metadataFiles={MetadataCount}, migratedMemoryFiles={MigratedMemoryFiles}, migratedKnowledgeFiles={MigratedKnowledgeFiles}, api={ApiBaseUrl}",
            options.ProjectName,
            options.WorkspaceRoot,
            storage.MetadataRootPath,
            storage.MemoryRootPath,
            storage.KnowledgeRootPath,
            metadataSync.CreatedMetadataCount,
            storage.MigratedMemoryFileCount,
            storage.MigratedKnowledgeFileCount,
            options.ApiBaseUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
