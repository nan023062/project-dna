# Dna.Knowledge.Governance

> 状态：目标重构架构
> 最后更新：2026-04-02
> 适用范围：`src/Dna.Knowledge/Governance`

## 模块定位

`Dna.Knowledge.Governance` 是最上层治理与演化层。

它不负责底层事实存储，也不负责图谱定义。
它的职责是站在治理视角，决定哪些信息应保留为短期记忆、哪些应沉淀为长期记忆、哪些长期记忆应最终升级为 TopoGraph 中按模块存放的知识。

一句话概括：

> Governance 负责“治理记忆，并把长期记忆沉淀为模块知识”。

## 核心职责

- 治理短期记忆
- 压缩与筛选长期记忆
- 识别可稳定沉淀的模块知识
- 推动“短期记忆 -> 长期记忆 -> 模块知识”的升级流程
- 校验沉淀后的知识是否满足拓扑与模块边界约束

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

- 顶层可以编排下层
- 下层绝不允许反向依赖本层
- `Governance` 可以消费 `Memory` 与 `TopoGraph`，但不接管它们的底层实现

## 与其他模块的边界

- `Workspace`
  - 提供真实事实源
- `TopoGraph`
  - 提供模块结构和模块知识存放目标
- `Memory`
  - 提供短期记忆和长期记忆的存取与召回
- `Governance`
  - 负责升级判定、压缩沉淀、归档淘汰与知识落位

## 对外提供

- `IGovernanceEngine`
- `GovernanceEngine`
- 与记忆治理、知识沉淀相关的结果模型

## 当前架构结论

这一层只做治理与编排，不接管底层存储实现。
知识的最终落点不在 `Memory`，而在 `TopoGraph` 中按模块组织的知识结构。

当前过渡态说明：

- `CheckFreshness`、`DetectMemoryConflicts`、`CondenseNodeKnowledgeAsync`、`CondenseAllNodesAsync` 已直接消费 `MemoryStore + ITopoGraphStore`，不再依赖 legacy `TopologySnapshot`
- `ValidateArchitecture` 仍临时委托 legacy `GraphEngine` 执行拓扑构建与校验，待新的 TopoGraph 校验体系完成后再替换
- legacy TopoGraph runtime 目前仅作为兼容层临时托管在 `TopoGraph/_Deprecated`，不再通过 `_legacy-cache` 直接参与编译
