# Agentic OS Tools

`tools` 用于存放本地 App 的配套工具，以及给外部 IDE 接入 MCP 的辅助目录。

## 一键启动 App

Windows 下可以直接使用：

- 双击 `tools/start-app.cmd`
- 或在 PowerShell 中运行 `tools/start-app.ps1`

启动器会自动：

- 检查 `publish/agentic-os.dll`
- 如果产物不存在，则执行 `dotnet build src/App/App.csproj`
- 构建成功后启动本地 App

## IDE 接入工具

- `dna-for-cursor/`
- `dna-for-codex/`

这两个目录用于把本地 App 的 MCP 能力接入外部 IDE。
