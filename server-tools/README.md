# Project DNA Server Tools

跨平台启动脚本，用于以独立进程运行 Project DNA **Server**（团队共享知识服务）。

> 推荐拓扑：`Server(5051)` + `Client(5052)`  
> 本目录只负责启动 `Server`，IDE 的 MCP 请连接 `Client`。

## 快速开始

### 1. 编辑配置

复制模板并填写实际路径：

```bash
cp server-config.example.json server-config.json
```

编辑 `server-config.json`：

```json
{
  "appPath": "../publish/server/dna_server",
  "dbPath": ".dna",
  "port": 5051
}
```

| 字段 | 说明 | 示例 |
|------|------|------|
| `appPath` | Server 可执行文件（支持绝对路径、相对路径、PATH 命令名） | `"../publish/server/dna_server"` / `"..\\publish\\server\\dna_server.exe"` / `"dna_server"` |
| `dbPath` | 知识库目录（相对路径基于当前工作目录） | `".dna"` 或 `"C:/my-project/.dna"` |
| `port` | 监听端口 | `5051` |

`appPath` 常见写法：
- macOS/Linux 本地发布：`../publish/server/dna_server`
- Windows 本地发布：`..\\publish\\server\\dna_server.exe`
- 已加入 PATH：`dna_server`

### 2. 启动服务

Windows：双击 `start-server.cmd`，或在 PowerShell 中执行：

```powershell
.\start-server.ps1
```

macOS / Linux（需要 `jq` 或 `python3`）：

```bash
bash start-server.sh
```

### 3. 验证

```bash
curl http://localhost:5051/api/status
```

如果返回 JSON 状态，说明 Server 已启动成功。  
随后可再启动 Client（示例）：

```bash
dotnet run --project ../src/Client -- --server http://127.0.0.1:5051 --port 5052
```
