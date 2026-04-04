using System.Text.Json;
using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Memory.Models;
using Dna.Workbench.Contracts;
using Dna.Workbench.DependencyInjection;
using Dna.Workbench.Tooling;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests;

public sealed class WorkbenchToolServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"dna-workbench-tools-{Guid.NewGuid():N}");
    private readonly string _workspaceRoot;
    private readonly string _metadataRoot;
    private readonly ServiceProvider _services;

    public WorkbenchToolServiceTests()
    {
        _workspaceRoot = Path.Combine(_tempRoot, "workspace");
        _metadataRoot = Path.Combine(_workspaceRoot, ".agentic-os");

        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(_metadataRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "README.md"), "# agentic-os");

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
    public void ListTools_ShouldExposeCoreWorkbenchToolCatalog()
    {
        var service = _services.GetRequiredService<IWorkbenchToolService>();

        var tools = service.ListTools();

        Assert.Contains(tools, item => item.Name == WorkbenchToolConstants.ToolNames.GetTopology);
        Assert.Contains(tools, item => item.Name == WorkbenchToolConstants.ToolNames.GetWorkspaceSnapshot);
        Assert.Contains(tools, item => item.Name == WorkbenchToolConstants.ToolNames.GetModuleKnowledge);
        Assert.Contains(tools, item => item.Name == WorkbenchToolConstants.ToolNames.Remember);
        Assert.Contains(tools, item => item.Name == WorkbenchToolConstants.ToolNames.Recall);
        Assert.Contains(tools, item => item.Name == WorkbenchToolConstants.ToolNames.GetRuntimeProjection);
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnWorkspaceSnapshot_ForActiveWorkspace()
    {
        var service = _services.GetRequiredService<IWorkbenchToolService>();

        var result = await service.InvokeAsync(new WorkbenchToolInvocationRequest
        {
            Name = WorkbenchToolConstants.ToolNames.GetWorkspaceSnapshot,
            Arguments = JsonSerializer.SerializeToElement(new
            {
                relativePath = (string?)null
            })
        });

        Assert.True(result.Success, result.Error ?? string.Empty);
        Assert.Equal(WorkbenchToolConstants.ToolNames.GetWorkspaceSnapshot, result.ToolName);
        var projectRoot = GetPropertyCaseInsensitive(result.Payload, "projectRoot").GetString();
        Assert.False(string.IsNullOrWhiteSpace(projectRoot));
        Assert.True(Directory.Exists(projectRoot));
        Assert.Equal(string.Empty, GetPropertyCaseInsensitive(result.Payload, "relativePath").GetString());
        Assert.True(GetPropertyCaseInsensitive(result.Payload, "entries").ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task InvokeAsync_ShouldPersistMemoryEntry()
    {
        var service = _services.GetRequiredService<IWorkbenchToolService>();

        var remember = await service.InvokeAsync(new WorkbenchToolInvocationRequest
        {
            Name = WorkbenchToolConstants.ToolNames.Remember,
            Arguments = JsonSerializer.SerializeToElement(new
            {
                type = MemoryType.Semantic,
                nodeType = NodeType.Technical,
                source = MemorySource.Ai,
                content = "Workbench tool service should support future internal and external agent access.",
                summary = "Shared workbench capability surface",
                disciplines = new[] { "engineering" },
                tags = new[] { "decision", "workbench" },
                stage = MemoryStage.LongTerm,
                importance = 0.9
            })
        });

        Assert.True(remember.Success, remember.Error ?? string.Empty);
        var rememberedId = GetPropertyCaseInsensitive(remember.Payload, "id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(rememberedId));
        Assert.False(string.IsNullOrWhiteSpace(GetPropertyCaseInsensitive(remember.Payload, "stage").ToString()));
    }

    private static JsonElement GetPropertyCaseInsensitive(JsonElement element, string propertyName)
    {
        if (TryGetPropertyCaseInsensitive(element, propertyName, out var property))
            return property;

        throw new KeyNotFoundException($"Property '{propertyName}' was not found.");
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }
}
