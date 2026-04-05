# Dna.Knowledge.Memory

> 状态：目标重构架构
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge/Memory`

## 模块定位

`Memory` 是知识体系中的记忆层。

它负责把工作过程中的上下文、经验、约定和事实保存为可检索、可回忆、可演进的记忆，
但不负责定义模块拓扑，也不负责最终模块知识的落库。

## 责任边界

`Memory` 负责：

- 短期记忆与长期记忆的统一读写
- 记忆的召回、检索、排序与统计
- 记忆的新鲜度、验证状态、归档状态
- 记忆与模块、标签、路径、学科等坐标的绑定
- 文件协议同步

`Memory` 不负责：

- 扫描工作区文件
- 定义模块父子结构
- 定义依赖和协作关系
- 直接决定哪些记忆应该升级为知识
- 保存最终模块知识文件

## 当前分层

```text
session files
    ->
MemoryStore / IMemoryEngine
    ->
Governance evolve / condense
    ->
TopoGraph NodeKnowledge
    ->
knowledge/modules/<uid>/identity.md
```

## Session / Memory 双层落地

### Session

- 物理目录：`.agentic-os/session/tasks|context`
- 语义：`ShortTerm`
- 特点：任务内、噪声更高、适合随工作推进被替换或归档

### Memory

- 物理目录：`.agentic-os/memory/*`
- 语义：`LongTerm`
- 特点：更稳定、可复用、可进入治理升级判断

## 当前稳定规则

### remember

- `Working` / `#active-task` 默认进入 `ShortTerm`
- 显式 `Stage` 优先于默认规则
- 默认路由逻辑集中在 `MemoryWriter`

### startup rebuild

- `MemoryStore.Initialize(...)` 会同时回读：
  - `.agentic-os/memory`
  - `.agentic-os/session`

### governance handoff

- `Memory` 提供统一记忆视图
- `Governance` 判断：
  - 哪些 `session` 应升级到 `memory`
  - 哪些 `memory` 应结构化进入 `knowledge`
- `TopoGraph` 持久化最终模块知识

## 架构结论

当前 `Memory` 的正确定位是：

> 一个面向 `ShortTerm` / `LongTerm` 记忆的基础技术模块，负责统一存取、检索和文件协议同步，
> 但不回收拓扑定义和知识沉淀职责。
