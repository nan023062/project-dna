# Dna.ExternalAgent

> 状态：第一阶段初始化骨架
> 最后更新：2026-04-04
> 适用范围：`src/Dna.ExternalAgent`

## 模块定位

`Dna.ExternalAgent` 是位于 `App` 与 `Dna.Workbench` 之间、与 `Dna.Agent` 平级的外置 Agent 适配模块。

它不负责任务编排本身。  
它的职责是把市面上的外置 Agent 产品能力，统一收敛到 Agentic OS 的知识拓扑、模块映射、任务闭环和关系类约束上。

一句话概括：

> `Dna.ExternalAgent` 不替外置 Agent 思考，而是确保它们进入项目后，只能先按 `TopoGraph + MCDP` 做需求拆解，再按 `startTask / endTask` 的单任务闭环请求项目能力。

## 分层位置

```text
App
  ->
Dna.Agent
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core

App
  ->
Dna.ExternalAgent
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

其中：

- `Dna.Agent`
  - 负责内置 Agent 的编排与执行
- `Dna.ExternalAgent`
  - 负责外置 Agent 产品适配、配置打包、工作流约束与插件化集成
- `Dna.Workbench`
  - 负责统一项目能力、需求拆解支持、任务上下文供给与任务结果闭环

## 为什么需要这个模块

Cursor、Claude Code、Codex、Copilot 这些产品都已经有自己的对话入口、任务执行器和 UI。

Agentic OS 不需要重新实现它们的编排器。  
真正需要的是：

- 把 Agentic OS 的知识拓扑、模块映射和关系类规则注入到这些产品
- 让这些产品都通过统一的 MCP / Tool / Instruction 语义访问项目能力
- 让这些产品都遵守统一的 `需求拆解 / 治理范围解析 -> 单任务 start/end -> 结果回写` 闭环

这正是 `Dna.ExternalAgent` 的职责。

## 产品逻辑

当前收口的产品逻辑是：

### 1. 统一产品抽象

每一个外置 Agent 产品都抽象成一个 `Adapter`：

- 标识自己支持的能力
- 声明自己管理哪些配置文件或插件产物
- 把同一份 Agentic OS 拓扑约束翻译成该产品能消费的格式

### 2. 统一工作流约束

无论底层产品是谁，都必须遵守以下 Agentic OS 规则：

- 先调用 Workbench 做需求拆解，再进入实现
- 先解析模块映射，再修改文件
- 遵守父子层级导航，不允许跳层乱改
- 遵守单向依赖，不允许逆向依赖式任务编排
- 涉及跨模块协作时，必须显式确认协作链
- 关键决策、完成记录、经验教训必须在 `endTask` 时回写

### 3. 统一能力入口

外置 Agent 最终看到的不是“某个私有 API”，而是 Workbench 的统一能力：

- `resolve requirement`
- `start task`
- `end task`
- `knowledge.*`
- `memory.*`
- `runtime.*`
- `tooling.*`

### 4. 统一交付物

`Dna.ExternalAgent` 输出的不是最终 UI，而是产品级接入包：

- MCP 配置
- 规则文件 / 提示文件
- 自定义 instructions
- agents 文件
- Claude Code plugin bundle 预览内容

### 5. 统一任务循环

外置 Agent 必须遵守下面这条循环：

1. 发起一次需求拆解请求
2. 从 Workbench 获得涉及模块、依赖链与协作链
3. 自行创建多个单模块 task
4. 对每个 task 调用 `startTask`
5. 获得该模块绑定的封闭操作空间、模块知识与相关记忆
6. 在这个上下文内执行分析、修改、查询与工具调用
7. 调用 `endTask` 返回结果、关键决策、经验教训、失败原因或前置依赖
8. 根据返回结果继续推进剩余任务链

这意味着外置 Agent 不允许把一个大需求无限展开成不受约束的连续长链任务。

并且：

- 外置 Agent 可以同时发起多个 task
- 但每个 task 的目标模块必须不同
- 如果目标模块已被其他活动 task 占用，必须收到冲突结果并重新规划

这里要明确：

- 冲突检测来自 Workbench 的模块锁
- 这不是 Workbench 替外置 Agent 做调度
- 而是 Workbench 阻止多个 Agent 同时修改同一模块，从而降低合并风险

外置 Agent 在治理场景下也遵守相同原则：

- 先请求治理范围
- 获取治理模块树
- 自行拆成多个治理型单模块 task
- 继续使用相同的 `startTask / endTask` 生命周期

## 当前支持口径

当前初始化骨架先覆盖四类产品：

- `Cursor`
  - 项目级 `.cursor/mcp.json`
  - rules / agents 文件
- `Codex`
  - 项目级 `.codex/config.toml`
  - prompts / agents 文件
- `Claude Code`
  - plugin bundle 预览内容
  - 后续再补正式安装器与 manifest 细节
- `GitHub Copilot`
  - `.github/copilot-instructions.md`
  - `.github/instructions/*.instructions.md`
  - `AGENTS.md`

## 当前实现边界

当前这一阶段只做：

- 产品适配抽象
- 默认产品目录
- 统一拓扑策略模型
- 统一接入包生成服务
- 文档与类图

当前暂不做：

- 真正的文件安装器迁移
- 真正的 MCP 工具调用迁移
- Claude Code 正式 plugin manifest schema
- 各产品的运行态回流接入
- 正式的 `resolveRequirement / startTask / endTask` 接口实现

## 核心接口

- `IExternalAgentAdapter`
  - 单个产品适配器
- `IExternalAgentAdapterCatalog`
  - 适配器目录
- `IExternalAgentIntegrationService`
  - 面向上层的统一接入包生成服务

## 当前结论

`Dna.ExternalAgent` 的核心意义不是“再造一个外置 Agent 平台”，而是：

> 把外置 Agent 的差异，收口成 Agentic OS 可以治理、可以约束、可以持续扩展的统一插件适配层，并确保所有外置 Agent 都按同一套“需求拆解 + 单任务闭环”进入项目。
