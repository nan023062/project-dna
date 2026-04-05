using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dna.ExternalAgent.Models;

namespace Dna.ExternalAgent.Services;

internal sealed class ExternalAgentFileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public bool IsMcpConfigured(
        ExternalAgentAdapterDescriptor descriptor,
        string workspaceRoot,
        string endpoint,
        string serverName)
    {
        var configPath = ResolveManagedConfigPath(descriptor, workspaceRoot);
        if (configPath == null)
            return true;

        if (!File.Exists(configPath))
            return false;

        return descriptor.ProductId switch
        {
            ExternalAgentConstants.ProductIds.Cursor => IsCursorMcpConfigured(configPath, endpoint, serverName),
            ExternalAgentConstants.ProductIds.Codex => IsCodexMcpConfigured(configPath, endpoint, serverName),
            _ => true
        };
    }

    public void WriteManagedFile(
        string workspaceRoot,
        string serverName,
        string mcpEndpoint,
        ExternalAgentManagedFile managedFile,
        bool replaceExisting,
        ExternalAgentToolingInstallReport report)
    {
        var fullPath = ResolveFullPath(workspaceRoot, managedFile.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (IsCursorMcpConfig(managedFile.RelativePath))
        {
            UpsertCursorMcpConfig(fullPath, serverName, mcpEndpoint, report);
            return;
        }

        if (IsCodexMcpConfig(managedFile.RelativePath))
        {
            UpsertCodexMcpConfig(fullPath, serverName, mcpEndpoint, report);
            return;
        }

        if (File.Exists(fullPath) && !replaceExisting)
        {
            report.SkippedFiles.Add(fullPath);
            return;
        }

        if (File.Exists(fullPath))
        {
            var backup = CreateBackup(fullPath);
            if (backup != null)
                report.BackupFiles.Add(backup);
        }

        File.WriteAllText(fullPath, managedFile.Content);
        report.WrittenFiles.Add(fullPath);
    }

    private static bool IsCursorMcpConfigured(string mcpFile, string endpoint, string serverName)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(mcpFile)) as JsonObject;
            var mcpServers = root?["mcpServers"] as JsonObject;
            if (mcpServers == null)
                return false;

            var byName = mcpServers[serverName] as JsonObject;
            var byNameUrl = byName?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(byNameUrl))
                return string.Equals(byNameUrl, endpoint, StringComparison.OrdinalIgnoreCase);

            foreach (var server in mcpServers)
            {
                var obj = server.Value as JsonObject;
                var url = obj?["url"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(url) &&
                    string.Equals(url, endpoint, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsCodexMcpConfigured(string configFile, string endpoint, string serverName)
    {
        var content = File.ReadAllText(configFile);
        var header = $"[mcp_servers.{serverName}]";
        var urlLine = $"url = \"{endpoint}\"";
        return content.Contains(header, StringComparison.OrdinalIgnoreCase) &&
               content.Contains(urlLine, StringComparison.OrdinalIgnoreCase);
    }

    private void UpsertCursorMcpConfig(
        string mcpFile,
        string serverName,
        string mcpEndpoint,
        ExternalAgentToolingInstallReport report)
    {
        JsonObject root;

        if (File.Exists(mcpFile))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(mcpFile)) as JsonObject ?? new JsonObject();
            }
            catch
            {
                report.Warnings.Add($"Existing MCP JSON parse failed, fallback to overwrite: {mcpFile}");
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        var mcpServers = root["mcpServers"] as JsonObject;
        if (mcpServers == null)
        {
            mcpServers = new JsonObject();
            root["mcpServers"] = mcpServers;
        }

        mcpServers[serverName] = new JsonObject
        {
            ["url"] = mcpEndpoint
        };

        WriteJsonFile(mcpFile, root, report);
    }

    private void UpsertCodexMcpConfig(
        string configFile,
        string serverName,
        string mcpEndpoint,
        ExternalAgentToolingInstallReport report)
    {
        var header = $"[mcp_servers.{serverName}]";
        var urlLine = $"url = \"{mcpEndpoint}\"";
        var newBlock = $"{header}{Environment.NewLine}{urlLine}";

        string content;
        if (File.Exists(configFile))
        {
            content = File.ReadAllText(configFile);
            var backup = CreateBackup(configFile);
            if (backup != null)
                report.BackupFiles.Add(backup);
        }
        else
        {
            content = string.Empty;
        }

        var updated = UpsertTomlSection(content, header, newBlock);
        File.WriteAllText(configFile, updated, Encoding.UTF8);
        report.WrittenFiles.Add(configFile);
    }

    private static string UpsertTomlSection(string content, string header, string newBlock)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var headerIndex = lines.FindIndex(line => string.Equals(line.Trim(), header, StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
        {
            var trimmed = content.TrimEnd();
            return string.IsNullOrWhiteSpace(trimmed)
                ? newBlock + Environment.NewLine
                : trimmed + Environment.NewLine + Environment.NewLine + newBlock + Environment.NewLine;
        }

        var endIndex = headerIndex + 1;
        while (endIndex < lines.Count && !lines[endIndex].TrimStart().StartsWith("[", StringComparison.Ordinal))
            endIndex++;

        var newLines = newBlock.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        lines.RemoveRange(headerIndex, endIndex - headerIndex);
        lines.InsertRange(headerIndex, newLines);
        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private void WriteJsonFile(string path, JsonObject root, ExternalAgentToolingInstallReport report)
    {
        var content = JsonSerializer.Serialize(root, JsonOptions);
        if (File.Exists(path))
        {
            var backup = CreateBackup(path);
            if (backup != null)
                report.BackupFiles.Add(backup);
        }

        File.WriteAllText(path, content);
        report.WrittenFiles.Add(path);
    }

    private static string? ResolveManagedConfigPath(ExternalAgentAdapterDescriptor descriptor, string workspaceRoot)
    {
        var relativePath = descriptor.ManagedPaths.FirstOrDefault(path => IsCursorMcpConfig(path) || IsCodexMcpConfig(path));
        return relativePath == null ? null : ResolveFullPath(workspaceRoot, relativePath);
    }

    private static bool IsCursorMcpConfig(string relativePath)
        => string.Equals(relativePath, ExternalAgentConstants.ManagedPaths.CursorMcp, StringComparison.OrdinalIgnoreCase);

    private static bool IsCodexMcpConfig(string relativePath)
        => string.Equals(relativePath, ExternalAgentConstants.ManagedPaths.CodexConfig, StringComparison.OrdinalIgnoreCase);

    private static string ResolveFullPath(string workspaceRoot, string relativePath)
        => Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string? CreateBackup(string path)
    {
        try
        {
            var backupPath = $"{path}.{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }
}
