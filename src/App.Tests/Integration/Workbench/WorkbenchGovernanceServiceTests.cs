using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.DependencyInjection;
using Dna.Workbench.Governance;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public sealed class WorkbenchGovernanceServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dna-workbench-governance-{Guid.NewGuid():N}");
    private readonly string _workspaceRoot;
    private readonly string _metadataRoot;
    private readonly ServiceProvider _services;

    public WorkbenchGovernanceServiceTests()
    {
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _metadataRoot = Path.Combine(_workspaceRoot, ".agentic-os");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_metadataRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));

        _services = new ServiceCollection()
            .AddLogging()
            .AddHttpClient()
            .AddSingleton(new Dna.App.Services.AppRuntimeOptions
            {
                ProjectName = "agentic-os-dev",
                WorkspaceRoot = _workspaceRoot,
                MetadataRootPath = _metadataRoot
            })
            .AddSingleton<ProjectConfig>()
            .AddKnowledgeGraph()
            .AddWorkbench()
            .BuildServiceProvider();

        var initializer = ActivatorUtilities.CreateInstance<Dna.App.Services.AppLocalRuntimeInitializer>(_services);
        initializer.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        SeedModules();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);

                break;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public async Task ResolveGovernanceAsync_ShouldReturnActiveModulesAndDependencyExpandedContexts()
    {
        var knowledge = _services.GetRequiredService<IKnowledgeWorkbenchService>();
        var governance = _services.GetRequiredService<IWorkbenchGovernanceService>();

        await knowledge.RememberAsync(new RememberRequest
        {
            Type = MemoryType.Working,
            NodeType = NodeType.Technical,
            Source = MemorySource.Ai,
            NodeId = "AgenticOs/Platform/Core",
            Content = "{\"task\":\"split governance service\",\"status\":\"doing\"}",
            Summary = "split governance service",
            Disciplines = ["engineering"],
            Tags = [WellKnownTags.ActiveTask],
            Stage = MemoryStage.ShortTerm,
            Importance = 0.85
        });

        var context = await governance.ResolveGovernanceAsync(new WorkbenchGovernanceRequest
        {
            Cadence = GovernanceCadence.HighFrequency,
            Scope = GovernanceScopeKind.ActiveChanges,
            ActiveWindow = TimeSpan.FromDays(1),
            IncludeDirectDependencies = true
        });

        Assert.NotEmpty(context.Rules);
        Assert.Single(context.ActiveModules);
        Assert.Contains(context.Modules, item => item.NodeId == "AgenticOs/Platform/Core" && item.IsDirectlyActive);
        Assert.Contains(context.Modules, item => item.NodeId == "AgenticOs/Platform/Foundation" && item.AddedByDependencyExpansion);
    }

    [Fact]
    public async Task ResolveGovernanceAsync_ShouldReturnSubtreeContexts_ForScopedGovernance()
    {
        var governance = _services.GetRequiredService<IWorkbenchGovernanceService>();

        var context = await governance.ResolveGovernanceAsync(new WorkbenchGovernanceRequest
        {
            Cadence = GovernanceCadence.LowFrequency,
            Scope = GovernanceScopeKind.Subtree,
            NodeIdOrName = "Platform",
            IncludeDirectDependencies = false
        });

        Assert.Equal("AgenticOs/Platform", context.ScopeNodeId);
        Assert.Contains(context.Modules, item => item.NodeId == "AgenticOs/Platform");
        Assert.Contains(context.Modules, item => item.NodeId == "AgenticOs/Platform/Core");
        Assert.Contains(context.Modules, item => item.NodeId == "AgenticOs/Platform/Foundation");
    }

    private void SeedModules()
    {
        var topology = _services.GetRequiredService<ITopoGraphApplicationService>();

        topology.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Platform",
            Name = "Platform",
            Path = "src/platform",
            Layer = 0,
            Summary = "Platform root"
        });

        topology.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Platform/Foundation",
            Name = "Foundation",
            Path = "src/foundation",
            Layer = 1,
            ParentModuleId = "AgenticOs/Platform",
            Summary = "Foundation module"
        });

        topology.RegisterModule("engineering", new TopologyModuleDefinition
        {
            Id = "AgenticOs/Platform/Core",
            Name = "Core",
            Path = "src/core",
            Layer = 2,
            ParentModuleId = "AgenticOs/Platform",
            Dependencies = ["AgenticOs/Platform/Foundation"],
            Summary = "Core module"
        });

        topology.BuildTopology();
    }
}
