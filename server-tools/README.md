# Project DNA 服务端工具 (Server Tools)

这个目录包含了用于独立启动和管理 Project DNA Server 的跨平台脚本。

无论您使用哪种 IDE（Cursor, Cline, Copilot 等），只要您希望以**独立进程 (HTTP/SSE 模式)** 运行 Project DNA，都可以使用这里的工具。

## 配置文件

在启动服务器之前，请检查或修改 `server-config.json`：

```json
{
  "server": {
    "mode": "binary",  // "binary" 表示运行可执行文件, "source" 表示通过 dotnet run 运行源码
    "appPath": "dna",  // 可执行文件的路径，或源码项目的路径
    "dbPath": ".dna",  // 知识库 SQLite 文件的存储目录（支持相对路径或绝对路径）
    "port": 5051       // 服务器监听端口
  }
}
```

## 启动服务器

### Windows
在 PowerShell 中运行：
```powershell
.\start-server.ps1
```

### macOS / Linux
在终端中运行：
```bash
bash start-server.sh
```

## 验证运行状态

服务器启动后，您可以通过以下命令验证其是否正常运行：
```bash
curl http://localhost:5051/api/status
```
