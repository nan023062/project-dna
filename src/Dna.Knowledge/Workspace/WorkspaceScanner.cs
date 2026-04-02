using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;

namespace Dna.Knowledge.Workspace;

/// <summary>
/// Scans a single workspace directory into stable directory/file facts.
/// </summary>
public static class WorkspaceScanner
{
    public static WorkspaceDirectorySnapshot ScanDirectory(
        string projectRoot,
        string relativePath,
        ArchitectureManifest architecture,
        ModulesManifest manifest)
    {
        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);
        var fullPath = WorkspacePath.Combine(projectRoot, normalizedPath);
        var context = BuildContext(projectRoot, manifest);
        var excludes = DefaultExcludes.BuildWithCustom(architecture.ExcludeDirs);

        if (!Directory.Exists(fullPath))
        {
            return new WorkspaceDirectorySnapshot
            {
                ProjectRoot = Path.GetFullPath(projectRoot),
                RelativePath = normalizedPath,
                Name = ResolveDirectoryName(projectRoot, normalizedPath),
                FullPath = fullPath,
                Exists = false,
                ScannedAtUtc = DateTime.UtcNow
            };
        }

        var directories = ScanDirectories(fullPath, normalizedPath, context, excludes);
        var files = ScanFiles(fullPath, normalizedPath, context, excludes);
        var entries = directories.Concat(files).ToList();

        return new WorkspaceDirectorySnapshot
        {
            ProjectRoot = Path.GetFullPath(projectRoot),
            RelativePath = normalizedPath,
            Name = ResolveDirectoryName(projectRoot, normalizedPath),
            FullPath = fullPath,
            Exists = true,
            ScannedAtUtc = DateTime.UtcNow,
            DirectoryCount = directories.Count,
            FileCount = files.Count,
            Entries = entries
        };
    }

    private static List<WorkspaceFileNode> ScanDirectories(
        string parentFullPath,
        string parentRelativePath,
        ScanContext context,
        HashSet<string> excludes)
    {
        var entries = new List<WorkspaceFileNode>();

        try
        {
            foreach (var directoryPath in Directory.GetDirectories(parentFullPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directoryPath);
                if (string.IsNullOrWhiteSpace(name) || excludes.Contains(name))
                    continue;

                var relativePath = AppendPath(parentRelativePath, name);
                var node = BuildDirectoryNode(directoryPath, relativePath, context, excludes);
                if (node != null)
                    entries.Add(node);
            }
        }
        catch
        {
            // Best effort. Permission-denied entries are intentionally skipped.
        }

        return entries;
    }

    private static List<WorkspaceFileNode> ScanFiles(
        string parentFullPath,
        string parentRelativePath,
        ScanContext context,
        HashSet<string> excludes)
    {
        var entries = new List<WorkspaceFileNode>();

        try
        {
            foreach (var filePath in Directory.GetFiles(parentFullPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(name) ||
                    excludes.Contains(name) ||
                    string.Equals(name, WorkspaceMetadataFile.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = AppendPath(parentRelativePath, name);
                var node = BuildFileNode(filePath, relativePath, context);
                if (node != null)
                    entries.Add(node);
            }
        }
        catch
        {
            // Best effort. Permission-denied entries are intentionally skipped.
        }

        return entries;
    }

    private static WorkspaceFileNode? BuildDirectoryNode(
        string fullPath,
        string relativePath,
        ScanContext context,
        HashSet<string> excludes)
    {
        var name = ResolveEntryName(fullPath);
        if (name.Length == 0 || excludes.Contains(name))
            return null;

        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);
        var ownership = TryResolveOwnership(normalizedPath, context);
        var descriptorDocument = WorkspaceMetadataFile.TryRead(context.ProjectRoot, normalizedPath);
        var descriptor = WorkspaceMetadataFile.ToDescriptorInfo(descriptorDocument, normalizedPath);
        var (childDirectoryCount, childFileCount) = CountVisibleChildren(fullPath, excludes);
        var hasChildren = childDirectoryCount > 0 || childFileCount > 0;

        var status = ResolveStatus(WorkspaceEntryKind.Directory, normalizedPath, ownership, descriptor, context);
        var moduleInfo = ownership == null ? null : BuildModuleInfo(ownership);

        return new WorkspaceFileNode
        {
            Name = name,
            Path = normalizedPath,
            ParentPath = WorkspacePath.GetParentPath(normalizedPath),
            FullPath = fullPath,
            Kind = WorkspaceEntryKind.Directory,
            Status = status,
            StatusLabel = GetStatusLabel(status, WorkspaceEntryKind.Directory),
            Badge = BuildBadge(status, ownership, descriptor, context),
            Extension = null,
            SizeBytes = null,
            LastModifiedUtc = SafeReadLastWriteUtc(fullPath, isDirectory: true),
            Exists = true,
            HasChildren = hasChildren,
            ChildDirectoryCount = childDirectoryCount,
            ChildFileCount = childFileCount,
            Ownership = ownership,
            Module = moduleInfo,
            Descriptor = descriptor,
            Actions = BuildActions(status, normalizedPath, descriptor != null, context)
        };
    }

    private static WorkspaceFileNode? BuildFileNode(
        string fullPath,
        string relativePath,
        ScanContext context)
    {
        var name = ResolveEntryName(fullPath);
        if (name.Length == 0)
            return null;

        var normalizedPath = WorkspacePath.NormalizeRelativePath(relativePath);
        var ownership = TryResolveOwnership(normalizedPath, context);
        var status = ResolveStatus(WorkspaceEntryKind.File, normalizedPath, ownership, null, context);

        var fileInfo = new FileInfo(fullPath);
        return new WorkspaceFileNode
        {
            Name = name,
            Path = normalizedPath,
            ParentPath = WorkspacePath.GetParentPath(normalizedPath),
            FullPath = fullPath,
            Kind = WorkspaceEntryKind.File,
            Status = status,
            StatusLabel = GetStatusLabel(status, WorkspaceEntryKind.File),
            Badge = BuildBadge(status, ownership, null, context),
            Extension = Path.GetExtension(name),
            SizeBytes = SafeReadLength(fileInfo),
            LastModifiedUtc = SafeReadLastWriteUtc(fullPath, isDirectory: false),
            Exists = true,
            HasChildren = false,
            ChildDirectoryCount = 0,
            ChildFileCount = 0,
            Ownership = ownership,
            Module = ownership == null ? null : BuildModuleInfo(ownership),
            Descriptor = null,
            Actions = new FileNodeActions()
        };
    }

    private static FileNodeStatus ResolveStatus(
        WorkspaceEntryKind kind,
        string normalizedPath,
        WorkspaceOwnershipInfo? ownership,
        WorkspaceDirectoryDescriptorInfo? descriptor,
        ScanContext context)
    {
        if (ownership != null)
        {
            if (ownership.IsExactMatch)
            {
                if (ownership.IsCrossWorkModule)
                    return FileNodeStatus.CrossWork;

                return ownership.Kind == WorkspaceOwnershipKind.ManagedPath
                    ? FileNodeStatus.Managed
                    : FileNodeStatus.Registered;
            }

            return FileNodeStatus.Tracked;
        }

        if (kind == WorkspaceEntryKind.Directory && descriptor != null)
            return FileNodeStatus.Described;

        if (kind == WorkspaceEntryKind.Directory && context.ContainerPaths.Contains(normalizedPath))
            return FileNodeStatus.Container;

        return kind == WorkspaceEntryKind.Directory
            ? FileNodeStatus.Candidate
            : FileNodeStatus.Untracked;
    }

    private static string GetStatusLabel(FileNodeStatus status, WorkspaceEntryKind kind)
    {
        return status switch
        {
            FileNodeStatus.Registered => WorkspaceConstants.Labels.RegisteredModule,
            FileNodeStatus.CrossWork => WorkspaceConstants.Labels.CrossWorkModule,
            FileNodeStatus.Described => WorkspaceConstants.Labels.DescribedDirectory,
            FileNodeStatus.Managed => WorkspaceConstants.Labels.ManagedScope,
            FileNodeStatus.Tracked => WorkspaceConstants.Labels.TrackedByModuleScope,
            FileNodeStatus.Container => WorkspaceConstants.Labels.ModuleContainer,
            FileNodeStatus.Candidate => WorkspaceConstants.Labels.CandidateDirectory,
            _ when kind == WorkspaceEntryKind.File => WorkspaceConstants.Labels.UntrackedFile,
            _ => WorkspaceConstants.Labels.Untracked
        };
    }

    private static string? BuildBadge(
        FileNodeStatus status,
        WorkspaceOwnershipInfo? ownership,
        WorkspaceDirectoryDescriptorInfo? descriptor,
        ScanContext context)
    {
        if (ownership != null)
        {
            var discipline = ownership.Discipline.Length == 0
                ? WorkspaceConstants.Badges.RootDiscipline
                : ownership.Discipline;
            return status switch
            {
                FileNodeStatus.CrossWork => $"{discipline}/{WorkspaceConstants.Badges.LayerPrefix}{ownership.Layer} {WorkspaceConstants.Badges.CrossWorkSuffix}",
                FileNodeStatus.Registered => $"{discipline}/{WorkspaceConstants.Badges.LayerPrefix}{ownership.Layer}",
                FileNodeStatus.Managed => $"{discipline} {WorkspaceConstants.Badges.ManagedSuffix}",
                FileNodeStatus.Tracked => $"{ownership.ModuleName} {WorkspaceConstants.Badges.ScopeSuffix}",
                _ => null
            };
        }

        if (status == FileNodeStatus.Described && descriptor != null)
            return WorkspaceConstants.Badges.Metadata;

        return status == FileNodeStatus.Container ? WorkspaceConstants.Badges.Container : null;
    }

    private static FileNodeActions BuildActions(
        FileNodeStatus status,
        string normalizedPath,
        bool hasDescriptor,
        ScanContext context)
    {
        if (status is FileNodeStatus.Registered or FileNodeStatus.CrossWork or FileNodeStatus.Managed)
            return new FileNodeActions { CanEdit = true };

        if (status == FileNodeStatus.Described)
            return new FileNodeActions { CanEdit = true, CanRegister = true };

        if (status != FileNodeStatus.Candidate)
            return new FileNodeActions();

        return new FileNodeActions
        {
            CanRegister = true,
            SuggestedDiscipline = GuessDiscipline(normalizedPath),
            SuggestedLayer = GuessSiblingLayer(normalizedPath, context.ModulesByPath)
        };
    }

    private static FileNodeModuleInfo BuildModuleInfo(WorkspaceOwnershipInfo ownership)
    {
        return new FileNodeModuleInfo
        {
            Id = ownership.ModuleId,
            Name = ownership.ModuleName,
            Discipline = ownership.Discipline,
            Layer = ownership.Layer,
            IsCrossWorkModule = ownership.IsCrossWorkModule,
            RegistrationPath = ownership.ScopePath
        };
    }

    private static WorkspaceOwnershipInfo? TryResolveOwnership(string normalizedPath, ScanContext context)
    {
        foreach (var scope in context.OwnedScopes)
        {
            if (!WorkspacePath.IsSameOrDescendantOf(scope.ScopePath, normalizedPath))
                continue;

            return new WorkspaceOwnershipInfo
            {
                ModuleId = scope.Module.Id,
                ModuleName = scope.Module.Name,
                Discipline = scope.Discipline,
                Layer = scope.Module.Layer,
                IsCrossWorkModule = scope.Module.IsCrossWorkModule,
                Kind = scope.Kind,
                IsExactMatch = string.Equals(scope.ScopePath, normalizedPath, StringComparison.OrdinalIgnoreCase),
                ScopePath = scope.ScopePath
            };
        }

        return null;
    }

    private static (int directoryCount, int fileCount) CountVisibleChildren(string fullPath, HashSet<string> excludes)
    {
        var directoryCount = 0;
        var fileCount = 0;

        try
        {
            foreach (var directoryPath in Directory.GetDirectories(fullPath))
            {
                var name = Path.GetFileName(directoryPath);
                if (!string.IsNullOrWhiteSpace(name) && !excludes.Contains(name))
                    directoryCount++;
            }
        }
        catch
        {
            // Ignore permission errors and keep best-effort counts.
        }

        try
        {
            foreach (var filePath in Directory.GetFiles(fullPath))
            {
                var name = Path.GetFileName(filePath);
                if (!string.IsNullOrWhiteSpace(name) &&
                    !excludes.Contains(name) &&
                    !string.Equals(name, WorkspaceMetadataFile.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    fileCount++;
                }
            }
        }
        catch
        {
            // Ignore permission errors and keep best-effort counts.
        }

        return (directoryCount, fileCount);
    }

    private static long? SafeReadLength(FileInfo fileInfo)
    {
        try
        {
            return fileInfo.Length;
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? SafeReadLastWriteUtc(string fullPath, bool isDirectory)
    {
        try
        {
            return isDirectory
                ? Directory.GetLastWriteTimeUtc(fullPath)
                : File.GetLastWriteTimeUtc(fullPath);
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveDirectoryName(string projectRoot, string normalizedPath)
    {
        if (normalizedPath.Length == 0)
            return new DirectoryInfo(Path.GetFullPath(projectRoot)).Name;

        return normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];
    }

    private static string ResolveEntryName(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        if (!string.IsNullOrEmpty(name))
            return name;

        return Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
    }

    private static string AppendPath(string parentPath, string childName)
    {
        return parentPath.Length == 0
            ? childName
            : $"{parentPath}/{childName}";
    }

    private sealed record ScopeRegistration(
        string Discipline,
        ModuleRegistration Module,
        string ScopePath,
        WorkspaceOwnershipKind Kind);

    private sealed class ScanContext
    {
        public string ProjectRoot { get; init; } = string.Empty;

        public Dictionary<string, (string discipline, ModuleRegistration module)> ModulesByPath { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<ScopeRegistration> OwnedScopes { get; } = [];

        public HashSet<string> ContainerPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static ScanContext BuildContext(string projectRoot, ModulesManifest manifest)
    {
        var context = new ScanContext
        {
            ProjectRoot = Path.GetFullPath(projectRoot)
        };

        foreach (var (discipline, modules) in manifest.Disciplines)
        {
            foreach (var module in modules)
            {
                RegisterScope(context, discipline, module, module.Path, WorkspaceOwnershipKind.ModuleRoot, isModulePath: true);

                foreach (var managedPath in module.ManagedPaths ?? [])
                    RegisterScope(context, discipline, module, managedPath, WorkspaceOwnershipKind.ManagedPath, isModulePath: false);
            }
        }

        context.OwnedScopes.Sort(static (left, right) =>
        {
            var byLength = right.ScopePath.Length.CompareTo(left.ScopePath.Length);
            if (byLength != 0)
                return byLength;

            return string.Compare(left.ScopePath, right.ScopePath, StringComparison.OrdinalIgnoreCase);
        });

        return context;
    }

    private static void RegisterScope(
        ScanContext context,
        string discipline,
        ModuleRegistration module,
        string? rawPath,
        WorkspaceOwnershipKind kind,
        bool isModulePath)
    {
        var scopePath = WorkspacePath.NormalizeRelativePath(rawPath);
        if (scopePath.Length == 0)
            return;

        context.OwnedScopes.Add(new ScopeRegistration(discipline, module, scopePath, kind));

        if (isModulePath)
            context.ModulesByPath[scopePath] = (discipline, module);

        var current = scopePath;
        while (current.Contains(WorkspaceConstants.Paths.RelativeSeparator))
        {
            current = WorkspacePath.GetParentPath(current);
            if (current.Length > 0)
                context.ContainerPaths.Add(current);
        }
    }

    private static string? GuessDiscipline(string path)
    {
        var lower = path.ToLowerInvariant();
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.Engineering)) return WorkspaceConstants.Disciplines.Engineering;
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.Art)) return WorkspaceConstants.Disciplines.Art;
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.Design)) return WorkspaceConstants.Disciplines.Design;
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.DevOps)) return WorkspaceConstants.Disciplines.DevOps;
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.Qa)) return WorkspaceConstants.Disciplines.Qa;
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.TechSupport)) return WorkspaceConstants.Disciplines.TechSupport;
        if (StartsWithAny(lower, WorkspaceConstants.DisciplinePathPrefixes.ProductDesign)) return WorkspaceConstants.Disciplines.ProductDesign;
        return null;
    }

    private static bool StartsWithAny(string value, IEnumerable<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int? GuessSiblingLayer(
        string path,
        Dictionary<string, (string discipline, ModuleRegistration module)> modulesByPath)
    {
        var parentPath = WorkspacePath.GetParentPath(path);

        foreach (var (registeredPath, registration) in modulesByPath)
        {
            if (!string.Equals(WorkspacePath.GetParentPath(registeredPath), parentPath, StringComparison.OrdinalIgnoreCase))
                continue;

            return registration.module.Layer;
        }

        return null;
    }
}
