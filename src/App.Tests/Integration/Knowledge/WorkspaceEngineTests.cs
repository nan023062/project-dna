using Dna.Knowledge;
using Dna.Knowledge.Workspace;
using Dna.Knowledge.Workspace.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Xunit;

namespace App.Tests;

public sealed class WorkspaceEngineTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "dna-workspace-engine-tests", Guid.NewGuid().ToString("N"));
    private readonly WorkspaceEngine _engine;

    public WorkspaceEngineTests()
    {
        Directory.CreateDirectory(_workspaceRoot);
        _engine = new WorkspaceEngine(new WorkspaceTreeCache(NullLogger<WorkspaceTreeCache>.Instance));
    }

    [Fact]
    public void GetRootSnapshot_ShouldIncludeFiles_AndExcludeMetadataDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".agentic-os"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "README.md"), "# test");

        var snapshot = _engine.GetRootSnapshot(_workspaceRoot, new WorkspaceTopologyContext());

        Assert.True(snapshot.Exists);
        Assert.Equal(string.Empty, snapshot.RelativePath);
        Assert.Equal(1, snapshot.DirectoryCount);
        Assert.Equal(1, snapshot.FileCount);
        Assert.Equal(["src", "README.md"], snapshot.Entries.Select(entry => entry.Name).ToArray());

        var sourceDirectory = Assert.Single(snapshot.Entries, entry => entry.Kind == WorkspaceEntryKind.Directory);
        Assert.Equal(FileNodeStatus.Candidate, sourceDirectory.Status);
        Assert.True(sourceDirectory.Actions.CanRegister);

        var readme = Assert.Single(snapshot.Entries, entry => entry.Kind == WorkspaceEntryKind.File);
        Assert.Equal("README.md", readme.Name);
        Assert.Equal(FileNodeStatus.Untracked, readme.Status);
    }

    [Fact]
    public void GetRootSnapshot_ShouldExcludeHiddenDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".xxx"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".cache", "nested"));

        var snapshot = _engine.GetRootSnapshot(_workspaceRoot, new WorkspaceTopologyContext());

        Assert.Equal(["src"], snapshot.Entries.Select(entry => entry.Name).ToArray());
        Assert.DoesNotContain(snapshot.Entries, entry => entry.Name.StartsWith(".", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDirectorySnapshot_ShouldAnnotateRegisteredManagedAndTrackedEntries()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src", "App"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs", "api"));
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "assets"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "src", "App", "Program.cs"), "class Program {}");
        File.WriteAllText(Path.Combine(_workspaceRoot, "docs", "api", "contracts.md"), "contract");

        var topology = CreateTopologyContext(new WorkspaceModuleRegistration
        {
            Id = "app-module",
            Name = "App",
            Discipline = "engineering",
            Path = "src/App",
            Layer = 2,
            ManagedPaths = ["docs/api"]
        });

        var srcSnapshot = _engine.GetDirectorySnapshot(_workspaceRoot, "src", topology);
        var appModule = Assert.Single(srcSnapshot.Entries);
        Assert.Equal("App", appModule.Name);
        Assert.Equal(FileNodeStatus.Registered, appModule.Status);
        Assert.True(appModule.Actions.CanEdit);
        Assert.Equal("app-module", appModule.Module?.Id);
        Assert.Equal(WorkspaceOwnershipKind.ModuleRoot, appModule.Ownership?.Kind);
        Assert.True(appModule.Ownership?.IsExactMatch);

        var docsSnapshot = _engine.GetDirectorySnapshot(_workspaceRoot, "docs", topology);
        var managedScope = Assert.Single(docsSnapshot.Entries);
        Assert.Equal("api", managedScope.Name);
        Assert.Equal(FileNodeStatus.Managed, managedScope.Status);
        Assert.Equal(WorkspaceOwnershipKind.ManagedPath, managedScope.Ownership?.Kind);
        Assert.True(managedScope.Actions.CanEdit);

        var moduleSnapshot = _engine.GetDirectorySnapshot(_workspaceRoot, "src/App", topology);
        var trackedFile = Assert.Single(moduleSnapshot.Entries);
        Assert.Equal("Program.cs", trackedFile.Name);
        Assert.Equal(WorkspaceEntryKind.File, trackedFile.Kind);
        Assert.Equal(FileNodeStatus.Tracked, trackedFile.Status);
        Assert.Equal("app-module", trackedFile.Ownership?.ModuleId);
        Assert.Equal(WorkspaceOwnershipKind.ModuleRoot, trackedFile.Ownership?.Kind);

        var rootSnapshot = _engine.GetRootSnapshot(_workspaceRoot, topology);
        var docsDirectory = rootSnapshot.Entries.Single(entry => entry.Name == "docs");
        Assert.Equal(FileNodeStatus.Container, docsDirectory.Status);
    }

    [Fact]
    public void TryGetEntry_ShouldResolveNormalizedPaths_AndReturnManagedFiles()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs", "api"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "docs", "api", "contracts.md"), "contract");

        var topology = CreateTopologyContext(new WorkspaceModuleRegistration
        {
            Id = "workspace-module",
            Name = "Workspace",
            Discipline = "engineering",
            Path = "src/Workspace",
            Layer = 1,
            ManagedPaths = ["docs/api"]
        });

        var file = _engine.TryGetEntry(_workspaceRoot, @"docs\api\contracts.md", topology);

        Assert.NotNull(file);
        Assert.Equal("docs/api/contracts.md", file!.Path);
        Assert.Equal(WorkspaceEntryKind.File, file.Kind);
        Assert.Equal(FileNodeStatus.Tracked, file.Status);
        Assert.Equal("Workspace", file.Ownership?.ModuleName);
        Assert.Equal(WorkspaceOwnershipKind.ManagedPath, file.Ownership?.Kind);
        Assert.False(file.Ownership?.IsExactMatch);
        Assert.Null(_engine.TryGetEntry(_workspaceRoot, "missing/file.txt", topology));
    }

    [Fact]
    public async Task WorkspaceEngine_ShouldProvideSafePathMapping_AndBasicReadWrite()
    {
        _engine.EnsureDirectory(_workspaceRoot, "src/App");
        await _engine.WriteTextAsync(_workspaceRoot, "src/App/Program.cs", "Console.WriteLine(\"ok\");", Encoding.UTF8);

        var fullPath = _engine.ResolveFullPath(_workspaceRoot, "src/App/Program.cs");
        var content = await _engine.ReadTextAsync(_workspaceRoot, "src/App/Program.cs", Encoding.UTF8);
        var bytes = await _engine.ReadBytesAsync(_workspaceRoot, "src/App/Program.cs");

        Assert.True(File.Exists(fullPath));
        Assert.Equal("Console.WriteLine(\"ok\");", content);
        Assert.NotEmpty(bytes);

        var invalid = Assert.Throws<InvalidOperationException>(() =>
            _engine.ResolveFullPath(_workspaceRoot, "../outside.txt"));
        Assert.Contains("escapes project root", invalid.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectoryMetadata_ShouldBeStoredInAgenticMetaFile_AndRecognizedByScanner()
    {
        _engine.EnsureDirectory(_workspaceRoot, "src/App");

        var created = await _engine.EnsureDirectoryMetadataAsync(_workspaceRoot, "src/App");
        Assert.False(string.IsNullOrWhiteSpace(created.StableGuid));

        var metadataPath = _engine.ResolveMetadataFilePath(_workspaceRoot, "src/App");
        Assert.True(File.Exists(metadataPath));

        var loaded = _engine.TryReadDirectoryMetadata(_workspaceRoot, "src/App");
        Assert.NotNull(loaded);
        Assert.Equal(created.StableGuid, loaded!.StableGuid);

        loaded.Summary = "Desktop application workspace directory.";
        await _engine.WriteDirectoryMetadataAsync(_workspaceRoot, "src/App", loaded);

        var topology = new WorkspaceTopologyContext();
        var snapshot = _engine.GetDirectorySnapshot(_workspaceRoot, "src", topology);
        var appDirectory = Assert.Single(snapshot.Entries);

        Assert.Equal("App", appDirectory.Name);
        Assert.Equal(FileNodeStatus.Described, appDirectory.Status);
        Assert.True(appDirectory.Actions.CanEdit);
        Assert.NotNull(appDirectory.Descriptor);
        Assert.Equal(created.StableGuid, appDirectory.Descriptor!.StableGuid);
        Assert.Equal("Desktop application workspace directory.", appDirectory.Descriptor.Summary);

        var appSnapshot = _engine.GetDirectorySnapshot(_workspaceRoot, "src/App", topology);
        Assert.DoesNotContain(appSnapshot.Entries, entry => string.Equals(entry.Name, ".agentic.meta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnsureDirectoryMetadataTreeAsync_ShouldCreateMetadataForVisibleDirectoriesOnly()
    {
        _engine.EnsureDirectory(_workspaceRoot, "src/App");
        _engine.EnsureDirectory(_workspaceRoot, "src/Shared");
        _engine.EnsureDirectory(_workspaceRoot, "docs/specs");
        _engine.EnsureDirectory(_workspaceRoot, ".git/hooks");
        _engine.EnsureDirectory(_workspaceRoot, ".agentic-os/cache");

        var result = await _engine.EnsureDirectoryMetadataTreeAsync(_workspaceRoot, new WorkspaceTopologyContext());

        Assert.Equal(string.Empty, result.RootRelativePath);
        Assert.Equal(6, result.ProcessedDirectoryCount);
        Assert.Equal(6, result.CreatedMetadataCount);

        Assert.True(File.Exists(_engine.ResolveMetadataFilePath(_workspaceRoot, string.Empty)));
        Assert.True(File.Exists(_engine.ResolveMetadataFilePath(_workspaceRoot, "src")));
        Assert.True(File.Exists(_engine.ResolveMetadataFilePath(_workspaceRoot, "src/App")));
        Assert.True(File.Exists(_engine.ResolveMetadataFilePath(_workspaceRoot, "src/Shared")));
        Assert.True(File.Exists(_engine.ResolveMetadataFilePath(_workspaceRoot, "docs")));
        Assert.True(File.Exists(_engine.ResolveMetadataFilePath(_workspaceRoot, "docs/specs")));

        Assert.False(File.Exists(Path.Combine(_workspaceRoot, ".git", ".agentic.meta")));
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, ".agentic-os", ".agentic.meta")));
    }

    [Fact]
    public void Invalidate_ShouldPublishWorkspaceChangeEvent()
    {
        _engine.Initialize(_workspaceRoot, new WorkspaceTopologyContext());

        WorkspaceChangeSet? received = null;
        _engine.Changed += OnChanged;

        try
        {
            _engine.Invalidate("src/App");
        }
        finally
        {
            _engine.Changed -= OnChanged;
        }

        Assert.NotNull(received);
        Assert.Equal(_workspaceRoot, received!.ProjectRoot);
        var change = Assert.Single(received.Entries);
        Assert.Equal(WorkspaceChangeKind.Invalidated, change.Kind);
        Assert.Equal("src/App", change.Path);
        Assert.Equal("src", change.ParentPath);

        void OnChanged(object? sender, WorkspaceChangeSet changeSet)
        {
            _ = sender;
            received = changeSet;
        }
    }

    [Fact]
    public async Task DeleteEntry_ShouldRemoveFilesAndDirectories()
    {
        await _engine.WriteTextAsync(_workspaceRoot, "docs/specs/a.txt", "temp");

        Assert.True(_engine.DeleteEntry(_workspaceRoot, "docs/specs/a.txt"));
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, "docs", "specs", "a.txt")));

        Assert.True(_engine.DeleteEntry(_workspaceRoot, "docs/specs", recursive: true));
        Assert.False(Directory.Exists(Path.Combine(_workspaceRoot, "docs", "specs")));
        Assert.False(_engine.DeleteEntry(_workspaceRoot, "docs/specs", recursive: true));
    }

    public void Dispose()
    {
        _engine.Dispose();
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }

    private static WorkspaceTopologyContext CreateTopologyContext(params WorkspaceModuleRegistration[] modules)
    {
        return new WorkspaceTopologyContext
        {
            Modules = modules.ToList()
        };
    }
}
