# Dna.Knowledge.Governance

> 状态：重构后架构基线
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge/Governance`

## 模块定位

`Governance` 是知识域最上层的治理与编排模块。
它不直接存储底层记忆，不维护拓扑定义，也不扫描工作区；它负责判断哪些记忆需要压缩、归档、升级，以及哪些结果应该沉淀为模块级知识。

## 依赖方向

```text
Governance
    ->
Memory
    ->
TopoGraph
    ->
Workspace
```

约束：
- `Governance` 只能编排下层能力，不能回收下层职责。
- `Memory` / `TopoGraph` 不允许反向依赖 `Governance`。
- 模块知识最终落在 `TopoGraph`，不属于 `Memory`。

## 核心职责

### 1. Freshness / Conflict / Archive

- 检查记忆新鲜度。
- 标记冲突 identity。
- 归档长期失效的记忆。

### 2. Evolve

`EvolveKnowledgeAsync(...)` 是治理建议接口。
它会：
- 从 `MemoryStore` 读取 `ShortTerm` / `LongTerm` 记忆。
- 通过 `ITopoGraphApplicationService.GetManagementSnapshot()` 分析候选模块。
- 输出两类建议：
  - `session -> memory`
  - `memory -> knowledge`

它不会直接写文件，也不会直接修改 `.agentic-os`。

### 3. Condense

`CondenseNodeKnowledgeAsync(...)` 是治理执行接口。
它会：
- 读取某个节点的可用记忆。
- 生成新的 identity memory。
- 生成一条长期 `upgrade trail`。
- 归档已沉淀的工作型 / 情节型记忆。
- 通过 `ITopoGraphStore.UpsertNodeKnowledge(...)` 回写模块知识。

## 写回约定

治理层的沉淀结果以文件协议为主存储：

1. `.agentic-os/knowledge/modules/<uid>/identity.md`
   - `## Summary`
   - `## Lessons`
   - `## Active Tasks`
   - `## Facts`
   - `## Governance`

`## Governance` 段至少包含：
- `Identity Memory`
- `Upgrade Trail`
- `Source Count`
- `Referenced Memories`

2. `graph.db`
   - 不再作为模块知识主存储。
   - 仅保留可重建的 TopoGraph 运行时缓存职责，例如 `computed dependencies`。

## 架构结论

当前治理主链已经从旧架构切到新架构：
- 治理目标节点来自 `ITopoGraphApplicationService.GetManagementSnapshot()`。
- 模块知识落点来自 `ITopoGraphStore`，并以文件协议为准。
- 治理建议通过 `evolve`。
- 治理执行通过 `condense`。

因此 `Governance` 当前承担的是“升级判断 + 沉淀编排”，而不再承担 legacy TopoGraph 的兼容存储职责。
