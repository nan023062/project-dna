using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dna.ExternalAgent.Contracts;

namespace Dna.App.Services.Tooling;

public sealed class AppFolderPickerService : IExternalAgentFolderPicker
{
    public async Task<string?> PickFolderAsync(string? defaultPath = null, string? prompt = null, CancellationToken cancellationToken = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return await PickFolderOnMacAsync(defaultPath, prompt, cancellationToken);

        throw new PlatformNotSupportedException("当前平台暂不支持系统文件夹选择窗口。");
    }

    private static async Task<string?> PickFolderOnMacAsync(string? defaultPath, string? prompt, CancellationToken cancellationToken)
    {
        var script = BuildMacAppleScript(defaultPath, prompt);
        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            ArgumentList = { "-e", script },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode == 0)
            return string.IsNullOrWhiteSpace(stdout) ? null : Path.GetFullPath(stdout);

        // 用户取消选择时，osascript 常见返回码是 1，提示里包含 -128。
        if (stderr.Contains("-128", StringComparison.OrdinalIgnoreCase))
            return null;

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
            ? $"文件夹选择失败，退出码：{process.ExitCode}"
            : stderr);
    }

    private static string BuildMacAppleScript(string? defaultPath, string? prompt)
    {
        var safePrompt = EscapeAppleScriptString(string.IsNullOrWhiteSpace(prompt)
            ? "选择需要安装 IDE 工作流配置的项目目录"
            : prompt.Trim());
        var normalized = NormalizeMacDefaultPath(defaultPath);

        var sb = new StringBuilder();
        sb.AppendLine($"set pickedFolder to choose folder with prompt \"{safePrompt}\"");
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var safePath = EscapeAppleScriptString(normalized);
            sb.Clear();
            sb.AppendLine($"set defaultFolder to POSIX file \"{safePath}\"");
            sb.AppendLine($"set pickedFolder to choose folder with prompt \"{safePrompt}\" default location defaultFolder");
        }

        sb.AppendLine("POSIX path of pickedFolder");
        return sb.ToString();
    }

    private static string? NormalizeMacDefaultPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(raw);
            if (!Directory.Exists(fullPath))
                return null;

            return fullPath.EndsWith('/') ? fullPath : $"{fullPath}/";
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeAppleScriptString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
