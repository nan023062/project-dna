using Dna.ExternalAgent.Contracts;
using Dna.ExternalAgent.Models;
using Dna.Workbench.Tooling;

namespace Dna.ExternalAgent.Services;

internal sealed class ExternalAgentToolingService(
    IExternalAgentAdapterCatalog adapterCatalog,
    IExternalAgentIntegrationService integration,
    IExternalAgentToolCatalogService toolCatalog,
    ExternalAgentFileManager fileManager) : IExternalAgentToolingService
{
    public IReadOnlyList<WorkbenchToolDescriptor> ListMcpTools() => toolCatalog.ListTools();

    public IReadOnlyList<ExternalAgentToolingTargetStatus> GetTargetStatuses(
        string workspaceRoot,
        string mcpEndpoint,
        string serverName)
    {
        return adapterCatalog.ListAdapters()
            .Select(adapter => BuildStatus(adapter, workspaceRoot, mcpEndpoint, serverName))
            .OrderBy(status => status.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ExternalAgentToolingTargetStatus GetTargetStatus(
        string productId,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName)
    {
        var adapter = adapterCatalog.FindAdapter(productId)
            ?? throw new ArgumentException($"Unsupported target: {productId}", nameof(productId));
        return BuildStatus(adapter.Descriptor, workspaceRoot, mcpEndpoint, serverName);
    }

    public ExternalAgentToolingInstallReport InstallTarget(
        string productId,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName,
        bool replaceExisting)
    {
        var package = integration.BuildPackage(new ExternalAgentPackageRequest
        {
            ProductId = productId,
            WorkspaceRoot = workspaceRoot,
            McpEndpoint = mcpEndpoint,
            ServerName = serverName
        });

        var report = new ExternalAgentToolingInstallReport
        {
            ProductId = package.Adapter.ProductId,
            DisplayName = package.Adapter.DisplayName,
            InstallMode = package.Adapter.InstallMode,
            ManagedFiles = BuildManagedFileStatuses(workspaceRoot, package.ManagedFiles)
        };

        foreach (var managedFile in package.ManagedFiles)
        {
            try
            {
                fileManager.WriteManagedFile(workspaceRoot, serverName, mcpEndpoint, managedFile, replaceExisting, report);
            }
            catch (Exception ex)
            {
                report.Warnings.Add(ex.Message);
            }
        }

        report.Warnings.AddRange(package.Notes);
        return report;
    }

    private ExternalAgentToolingTargetStatus BuildStatus(
        ExternalAgentAdapterDescriptor descriptor,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName)
    {
        var package = integration.BuildPackage(new ExternalAgentPackageRequest
        {
            ProductId = descriptor.ProductId,
            WorkspaceRoot = workspaceRoot,
            McpEndpoint = mcpEndpoint,
            ServerName = serverName
        });

        var managedFiles = BuildManagedFileStatuses(workspaceRoot, package.ManagedFiles);
        var filesExist = managedFiles.All(file => file.Exists);
        var mcpConfigured = fileManager.IsMcpConfigured(descriptor, workspaceRoot, mcpEndpoint, serverName);

        return new ExternalAgentToolingTargetStatus
        {
            ProductId = descriptor.ProductId,
            DisplayName = descriptor.DisplayName,
            Description = descriptor.Description,
            InstallMode = descriptor.InstallMode,
            Installed = filesExist && mcpConfigured,
            McpConfigured = mcpConfigured,
            ManagedFiles = managedFiles
        };
    }

    private static IReadOnlyList<ExternalAgentManagedFileStatus> BuildManagedFileStatuses(
        string workspaceRoot,
        IReadOnlyList<ExternalAgentManagedFile> managedFiles)
    {
        return managedFiles
            .Select(file =>
            {
                var fullPath = Path.Combine(workspaceRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                return new ExternalAgentManagedFileStatus
                {
                    RelativePath = file.RelativePath,
                    FullPath = fullPath,
                    Purpose = file.Purpose,
                    Exists = File.Exists(fullPath)
                };
            })
            .ToList();
    }
}
