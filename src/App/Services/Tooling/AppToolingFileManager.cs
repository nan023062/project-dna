using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dna.App.Services.Tooling;

public sealed class AppToolingFileManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public bool IsMcpConfigured(string mcpFile, string endpoint, string serverName)
    {
        if (!File.Exists(mcpFile))
            return false;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(mcpFile)) as JsonObject;
            var mcpServers = root?["mcpServers"] as JsonObject;
            if (mcpServers == null)
                return false;

            var byName = mcpServers[serverName] as JsonObject;
            var byNameUrl = byName?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(byNameUrl))
                return true;

            foreach (var server in mcpServers)
            {
                var obj = server.Value as JsonObject;
                var url = obj?["url"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                if (string.Equals(url, endpoint, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public void UpdateMcpConfig(
        string mcpFile,
        string serverName,
        string mcpEndpoint,
        AppToolingInstallReport report)
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
                report.Warnings.Add($"Existing mcp.json parse failed, fallback to overwrite: {mcpFile}");
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

    public void WriteManagedFile(
        string path,
        string content,
        bool replaceExisting,
        AppToolingInstallReport report)
    {
        if (File.Exists(path) && !replaceExisting)
        {
            report.SkippedFiles.Add(path);
            return;
        }

        if (File.Exists(path))
        {
            var backup = CreateBackup(path);
            if (backup != null)
                report.BackupFiles.Add(backup);
        }

        File.WriteAllText(path, content);
        report.WrittenFiles.Add(path);
    }

    private void WriteJsonFile(string path, JsonObject root, AppToolingInstallReport report)
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
