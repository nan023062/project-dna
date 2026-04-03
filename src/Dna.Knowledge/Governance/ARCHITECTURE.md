# Dna.Knowledge.Governance

> 状态：重构后架构基线
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge/Governance`

## 模块定位

`Dna.Knowledge.Governance` 是知识域最上层的治理与编排层。

它不保存底层记忆数据，不维护图谱定义文件，也不扫描工作区。
它只负责判断哪些记忆应该衰减、归档、压缩，并把稳定结果沉淀为模块知识。

一句话概括：

> Governance 负责“治理记忆，并驱动记忆向模块知识升级”。

## 核心职责

- 治理短期记忆与长期记忆的生命周期
- 识别冲突、陈旧和可归档记忆
- 将稳定记忆压缩为模块级知识摘要
- 调用拓扑应用服务执行架构校验
- 保持“短期记忆 -> 长期记忆 -> 模块知识”的升级链路

## 依赖方向

```text
Dna.Knowledge.Governance
    ->
Dna.Knowledge.Memory
    ->
Dna.Knowledge.TopoGraph
    ->
Dna.Knowledge.Workspace
```

约束如下：

- `Governance` 只编排下层能力，不回收下层实现职责
- `Memory` 与 `TopoGraph` 不允许反向依赖 `Governance`
- 模块知识最终落点在 `TopoGraph`，不是 `Memory`

## 与其他模块的边界

- `Workspace`
  - 提供真实文件与目录事实
- `TopoGraph`
  - 提供模块定义、关系和知识落位目标
- `Memory`
  - 提供记忆读写、检索和生命周期基础能力
- `Governance`
  - 负责压缩、归档、冲突治理和架构健康校验

## 对外提供

- `IGovernanceEngine`
- `GovernanceEngine`
- `KnowledgeCondenseResult`
- `GovernanceReport`

## 当前架构结论

当前治理主链路已经完全切到新架构：

- 记忆治理直接消费 `MemoryStore`
- 节点知识落位直接消费精简后的 `ITopoGraphStore`
- 治理目标节点解析来自 `ITopoGraphApplicationService.GetManagementSnapshot()`
- 架构校验统一通过 `ITopoGraphApplicationService.ValidateArchitecture()`

因此，`Governance` 已不再承担任何旧图谱运行时兼容职责。
