# Dna.Knowledge 架构总览

> 状态：当前有效
> 最后更新：2026-04-04
> 适用范围：`src/Dna.Knowledge`

## 模块定位

`Dna.Knowledge` 是整个知识域的父级模块。

它在当前建模中属于一个 `Department` 级边界，负责组织知识域的核心子模块，并对上提供统一知识能力基础。

一句话概括：

> `Dna.Knowledge` 负责定义知识域轮廓、子模块边界、依赖方向与组合方式。

## 上下游关系

目标分层如下：

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

- `App`
  - 桌面宿主与外部适配层
- `Dna.Agent`
  - 内置 Agent 的任务编排与执行层（目标架构）
- `Dna.Workbench`
  - 应用服务、项目能力门面、运行时观测入口
- `Dna.Knowledge`
  - 知识域能力
- `Dna.Core`
  - 基础设施

这意味着：

- `Dna.Knowledge` 的主要上游应是 `Dna.Workbench`
- `Dna.Agent` 通过 `Dna.Workbench` 间接使用知识能力
- `App` 最终不应继续直接承载大量知识用例编排
- `Dna.Knowledge` 不允许反向依赖 `Dna.Workbench` 或 `Dna.Agent`

## 子模块构成

当前 `Dna.Knowledge` 由 4 个核心子模块组成：

- `Workspace`
  - 负责真实工作区文件系统事实
  - 提供目录、文件、元数据、监听与安全读写
- `TopoGraph`
  - 负责模块、层级、依赖、协作与模块知识容器
- `Memory`
  - 负责短期记忆与长期记忆的存储、查询与召回
- `Governance`
  - 负责记忆治理、知识压缩、知识演化与健康检查

根级 `Dna.Knowledge` 文档只描述这 4 个子模块的边界和组合方式，不展开各子模块内部细节。

## 子模块依赖方向

`Dna.Knowledge` 内部保持固定的单向依赖：

```text
Governance
  ->
Memory
  ->
TopoGraph
  ->
Workspace
```

约束如下：

- 只允许自上而下依赖
- 禁止反向依赖
- 禁止循环依赖
- 父级模块负责边界约束，不吞并子模块职责

## 统一认知流程

整个知识域围绕下面这条认知流程组织：

```text
Workspace 事实
  ->
短期记忆
  ->
长期记忆
  ->
按模块沉淀的知识
```

这里需要明确：

- `Workspace` 记录真实物理事实
- `Memory` 记录交互、经验、过程记忆
- 最终稳定知识不直接存放在 `Memory`
- 最终稳定知识由 `Governance` 沉淀到 `TopoGraph`

## 父级模块职责

作为知识域父模块，`Dna.Knowledge` 只负责：

- 定义知识域的子模块划分
- 约束子模块之间的依赖方向
- 约束统一术语和领域边界
- 对上提供统一知识域装配入口

## 非职责范围

`Dna.Knowledge` 根级模块不直接负责：

- 桌面窗口布局与交互
- CLI 参数解析
- MCP 工具协议映射
- HTTP 端点协议适配
- Agent 任务编排
- Agent 执行循环

这些职责分别属于：

- `App`
- `Dna.Agent`
- `Dna.Workbench`

## 如何组合出完整能力

完整知识能力不是来自某一个子模块，而是来自 4 个子模块的协作：

1. `Workspace`
   - 提供真实工作区事实
2. `TopoGraph`
   - 将工作区事实解释为模块、层级和关系
3. `Memory`
   - 沉淀短期与长期记忆
4. `Governance`
   - 治理记忆并把稳定内容升级为知识

上层的 `Dna.Workbench` 再在这个基础上组合出：

- 项目级知识查询
- 项目级知识修改
- 统一运行时观测能力

而未来的 `Dna.Agent` 再在 `Dna.Workbench` 之上负责：

- 任务编排
- 执行循环
- 模型与工具调用策略

## 对外能力

从上层视角看，`Dna.Knowledge` 当前统一提供这些能力基础：

- 工作区事实访问
- 模块拓扑与关系访问
- 模块知识访问
- 记忆写入、查询与召回
- 架构治理、记忆压缩与知识演化
- 统一依赖注入与装配入口

## 当前结论

`Dna.Knowledge` 的正确定位不是某一个具体功能模块，而是：

> 一个面向知识域的父级 `Department` 模块，负责组织 `Workspace`、`TopoGraph`、`Memory`、`Governance` 四个子模块，并为 `Dna.Workbench` 提供统一的知识域能力基础。

