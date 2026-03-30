# Project DNA Server Tools

跨平台启动脚本，用于以独立进程 (HTTP/SSE) 运行 Project DNA Server。

## 快速开始

### 1. 编辑配置

打开 `server-config.json`，填入实际路径：

```json
{
  "appPath": "dna",
  "dbPath": ".dna",
  "port": 5051
}
```

| 字段 | 说明 | 示例 |
|------|------|------|
| `appPath` | 可执行文件路径（支持全局命令或绝对路径） | `"dna"` 或 `"D:/path/to/dna.exe"` |
| `dbPath` | 知识库目录（相对路径基于当前工作目录） | `".dna"` 或 `"C:/my-project/.dna"` |
| `port` | 监听端口 | `5051` |

### 2. 启动服务器

**Windows** — 双击 `start-server.cmd`，或在 PowerShell 中：

```powershell
.\start-server.ps1
```

**macOS / Linux**（需要 `jq` 或 `python3`）：

```bash
bash start-server.sh
```

### 3. 验证

```bash
curl http://localhost:5051/api/status
```
