# Project DNA 接入指南 (IDE Integration Guides)

Project DNA 作为 AI Agent 的项目认知引擎，通过标准的 MCP (Model Context Protocol) 接口与主流 IDE 深度集成。

本目录包含了各种 IDE 和 AI 助手的接入指南，帮助您快速将 Project DNA 的能力赋予您的开发环境。

## 支持的 IDE / 平台

目前我们提供以下平台的接入指南与自动化工具：

- [**Cursor**](./cursor/README.md) - 深度集成 AI 的现代 IDE，支持通过 Skill 自动化配置 MCP 和 Prompt 模板。
- *(即将支持)* **VS Code** - 通过 Cline 等插件支持 MCP。
- *(即将支持)* **JetBrains IDEs** - 适配相关 AI 插件。

## 核心概念

无论您使用哪种 IDE，接入 Project DNA 通常包含以下两个核心步骤：

1. **配置 MCP 连接**：让 IDE 的 AI 能够调用 DNA Server 提供的工具（如 `get_context`, `recall`, `remember` 等）。
2. **导入系统提示词 (Prompt Hooks)**：让 AI 知道**何时**以及**如何**使用这些工具（例如：“在修改代码前，先调用 `get_context` 获取架构约束”）。

如果您使用的 IDE 尚未在上方列表中，您可以参考上述核心概念，手动配置您的 AI 助手。

## 贡献指南

如果您成功在其他 IDE 或工具中接入了 Project DNA，欢迎提交 PR，将您的配置步骤补充到本目录中！
