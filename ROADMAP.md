# Roadmap

> 最后更新：2026-04-04
> 当前口径：单桌面 App、单进程、本地知识运行时

本文档只保留后续待推进内容，不再重复已完成阶段。

## 当前架构方向

目标分层收口为：

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
```

其中：

- `Dna.Agent`
  - 负责内置 Agent 的编排、执行、模型调用、工具策略
- `Dna.Workbench`
  - 负责给内置 Agent、外置 Agent、CLI、桌面宿主提供统一项目能力
- `Dna.Knowledge`
  - 负责 Workspace / TopoGraph / Memory / Governance

## 近期主线

### 1. 抽离 Dna.Agent

- 把 `src/Dna.Workbench/Agent/*` 迁出到 `src/Dna.Agent`
- 把 `src/Dna.Workbench/Models/Agent/*` 迁出到 `src/Dna.Agent`
- 重新定义内置 Agent 的稳定入口与会话模型
- 保证 `Dna.Agent -> Dna.Workbench -> Dna.Knowledge` 单向依赖

### 2. 收口 Workbench 边界

- 保留知识能力门面
- 补稳定的运行时观测门面
- 规划统一工具能力门面
- 停止在 Workbench 内新增任务编排职责

### 3. 桌面内部去 HTTP 化

- 让桌面内部 UI 优先通过进程内服务使用 Workbench / Dna.Agent
- 把 Chat 从 `/agent/*` 路径收口到新 `Dna.Agent`
- 本地 HTTP 只保留给兼容层、CLI、MCP、自动化接入

### 4. 外置入口统一到 Workbench

- CLI 从“直接拼 `/api/...`”迁到“调用 Workbench 能力”
- MCP 工具从“本地 API 转发”迁到“Workbench 能力适配”
- 保证外置 Agent 与内置 Agent 看到同一套项目能力面

### 5. 工具调用内核

- 定义工具注册模型
- 定义工具调用上下文
- 定义工具执行结果模型
- 让内置 Agent 和外置 Agent 使用统一工具能力语义

### 6. 拓扑运行态联动

- 统一运行时事件模型
- 让拓扑图显示 Agent 的节点进入、关系穿越、知识查询、记忆写入、工具调用状态
- 保证该运行态既适用于内置 Agent，也适用于外置 Agent

## 暂缓事项

以下内容不在当前优先级：

- 多人协作版服务器回归
- 团队权限系统
- 重型任务编排 UI
- 与外部开源 Agent Runtime 的深度整合实现

## 当前完成标准

本轮架构收口完成的判断标准是：

1. `Dna.Agent` 成为内置 Agent 的明确落点
2. `Dna.Workbench` 不再承担任务编排职责
3. 桌面内部主路径不再依赖本地 HTTP
4. CLI / MCP / 内置 Agent 共用同一套项目能力面

