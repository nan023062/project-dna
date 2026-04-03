using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.FileProtocol;
using Dna.Knowledge.TopoGraph.Contracts;
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
    ITopoGraphFacade topoGraphFacade,
    FileBasedDefinitionStore fileDefinitionStore,
    ILogger<AppLocalRuntimeInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = projectConfig.SetProject(options.WorkspaceRoot, options.MetadataRootPath);
        if (!result.Success)
            throw new InvalidOperationException(result.Message);

        var storage = AppProjectStoragePaths.Prepare(projectConfig.MetadataRootPath, logger);

        // 旧系统初始化（包含文件协议回退）
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

        // 新系统初始化：从 .agentic-os/ 文件加载 TopoGraphFacade
        InitializeFileProtocol(storage.MetadataRootPath);

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

    private void InitializeFileProtocol(string metadataRootPath)
    {
        try
        {
            // 初始化文件协议定义存储
            fileDefinitionStore.Initialize(metadataRootPath);

            if (!fileDefinitionStore.HasKnowledgeFiles())
            {
                logger.LogDebug("未检测到 .agentic-os/knowledge/modules/，跳过新系统初始化");
                return;
            }

            // 初始化 TopoGraphFacade（新系统）
            topoGraphFacade.Initialize(metadataRootPath);

            var snapshot = topoGraphFacade.GetSnapshot();
            var issues = topoGraphFacade.Validate();

            logger.LogInformation(
                "TopoGraphFacade 已从文件加载: 节点={NodeCount}, 关系={RelationCount}, 校验问题={IssueCount}",
                snapshot.Nodes.Count,
                snapshot.Relations.Count,
                issues.Count);

            // 输出校验问题
            foreach (var issue in issues)
                logger.LogWarning("拓扑校验: [{Severity}] {Message}", issue.Severity, issue.Message);

            // 运行文件协议结构验证
            var validator = new FileProtocolValidator();
            var validationResult = validator.Validate(fileDefinitionStore.AgenticOsPath);

            foreach (var error in validationResult.Errors)
                logger.LogWarning("文件协议校验错误: {Error}", error);

            foreach (var warning in validationResult.Warnings)
                logger.LogDebug("文件协议校验警告: {Warning}", warning);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "文件协议初始化失败，不影响旧系统运行");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
