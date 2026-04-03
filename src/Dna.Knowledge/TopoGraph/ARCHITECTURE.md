# Dna.Knowledge.TopoGraph

> 状态：目标重构架构
> 最后更新：2026-04-02
> 适用范围：`src/Dna.Knowledge/TopoGraph`

## 模块定位

`Dna.Knowledge.TopoGraph` 是整套知识库中的“模块与关系解释层”。

它不负责扫描物理文件系统，也不负责记忆治理。
它负责把工程中的组织结构、技术结构、业务结构和协作结构抽象成一套稳定的知识拓扑，同时作为最终模块知识的组织与存放载体。

当前抽象口径以工程管理视角为核心，并与 `agentic-os-mcdp-protocol` 中强调的模块治理思路保持一致。

一句话概括：

> TopoGraph 回答“这些工程内容在工程管理与知识组织视角下，被解释成哪些模块、它们如何归属、如何依赖、如何协作，以及每个模块沉淀了哪些最终知识”。

## 依赖方向

```text
Dna.Knowledge.TopoGraph
    ->
Dna.Knowledge.Workspace
```

约束如下：

- 不允许依赖 `Dna.Knowledge.Memory`
- 不允许依赖 `Dna.Knowledge.Governance`
- 向下读取数据只能通过自身定义的契约或 `Workspace` 事实能力
- 不允许绕过接口直接绑定具体存储实现

## 核心职责

当前 `TopoGraph` 的职责应收敛为：

- 定义图谱节点类型与关系类型
- 管理主树层级结构
- 管理技术依赖关系
- 管理协作关系
- 管理模块到物理路径的映射解释
- 按模块组织和存放最终知识
- 生成拓扑快照
- 提供图谱查询、上下文裁剪与架构校验

## 非职责范围

`TopoGraph` 不负责：

- 扫描目录和文件
- 保存目录元数据
- 记忆 CRUD
- 短期记忆与长期记忆召回
- 记忆压缩与知识沉淀决策
- 工作流编排

这意味着：

> TopoGraph 存的是模块结构与模块知识，不存记忆过程本身。

## 与 Memory 的边界

这里必须明确一条架构原则：

- `Memory` 只保存短期记忆和长期记忆
- `Governance` 负责把长期记忆筛选、压缩、确认
- `TopoGraph` 负责承接最终结果，并按模块存放知识

因此：

- `TopoGraph` 中的知识不是“记忆的另一种叫法”
- `TopoGraph` 中的知识也不是原始对话、原始观察或原始经验全文
- 它是经过治理后，沉淀到模块边界里的稳定知识表达

## 核心抽象

### 抽象基类

`TopoGraph` 中的所有模块都只允许落在两个抽象类别中：

- `Group`
  - 只负责分组、归属、导航
  - 是非叶子节点
  - 不参与技术依赖
- `Module`
  - 只负责能力或业务落地
  - 是叶子节点
  - 参与依赖与协作
  - 是模块知识的主要沉淀载体

这是 `TopoGraph` 的第一层抽象边界。

### 具体类型

在 `Group / Module` 之下，当前只允许存在四种核心模块类型：

- `Project : Group`
- `Department : Group`
- `Technical : Module`
- `Team : Module`

这里不再允许继续引入第五种核心模块类型，除非未来经过架构层重新定义。

## 目标类图

`TopoGraph` 的目标类图已拆分到单独文档：

- `src/Dna.Knowledge/TopoGraph/CLASS-DIAGRAM.md`

该文档只负责描述目标领域模型，不承担完整架构说明。

## 四类模块的正式语义

### `Project`

- 项目级根模块
- 整张图中唯一根节点
- 负责工程总愿景、全局边界与一级索引
- 只承担归属和导航语义
- 不参与技术依赖

### `Department`

- 领域或职能域模块
- 用于表达较大的业务或组织分区
- 可以多级嵌套
- 只承担分组与归属语义
- 不参与技术依赖

### `Technical`

- 技术服务模块
- 是具体技术实现与能力提供者
- 应保持高度内聚、职责单一
- 对外提供 API、接口或稳定能力边界
- 只允许单向依赖其他 `Technical`
- 是最终模块知识的主要载体之一

### `Team`

- 业务模块
- 是具体业务闭环、责任归属和协作落地单元
- 负责把多个 `Technical` 模块组织成可交付业务
- 只允许依赖 `Technical`
- 不允许被任何其他模块依赖
- 也是最终模块知识的主要载体之一

这里需要特别强调：

> `Team` 在图谱里的语义是“业务模块”，不是行政组织结构里的团队。

## 主树规则

`TopoGraph` 的主树负责回答：

- 谁属于谁
- 谁被谁管理
- 谁位于哪个组织或领域边界中

推荐主树结构为：

```text
Project
  -> Department
    -> Department
      -> Technical
      -> Team
```

也就是说：

- `Project` 只能作为根节点
- `Department` 只能挂在 `Project` 或 `Department` 之下
- `Technical` 只能挂在 `Department` 之下
- `Team` 只能挂在 `Department` 之下

## 依赖规则

依赖边只表达“谁在技术上使用谁”，不表达组织归属。

允许的依赖：

- `Technical -> Technical`
- `Team -> Technical`

不允许的依赖：

- 任何节点依赖 `Project`
- 任何节点依赖 `Department`
- `Technical -> Team`
- `Team -> Team` 的 `Dependency`
- 任何节点依赖 `Team`

## 协作规则

协作关系与依赖关系必须分离：

- `Dependency`
  - 表达技术依赖
- `Collaboration`
  - 表达业务或交付协作

所有跨模块协作逻辑应在 `Team` 层表达，而不是在 `Technical` 层表达。

## 与 Workspace 的边界

### `Workspace` 负责

- 真实目录
- 真实文件
- `.agentic.meta`
- 路径安全
- 目录快照

### `TopoGraph` 负责

- 模块定义
- 模块类型
- 父子层级
- 依赖关系
- 协作关系
- 模块知识容器与模块知识快照
- 模块到物理路径的映射解释

这意味着：

- 目录不是模块
- 目录只是物理事实
- 模块是知识图中的解释实体
- 模块知识也属于模块解释层，而不属于工作区事实层

## 模块与物理路径映射原则

这里需要明确一条重要设计原则：

> 理想状态下，一个模块应尽量映射一个主物理目录；但系统设计不能假设模块与物理目录永远严格一一对应。

因此推荐采用：

- `MainPath`
  - 模块的主物理目录
- `ManagedPaths`
  - 模块额外管理或覆盖的物理路径

这允许：

- 一个模块对应多个物理路径
- 一个目录暂时不属于任何模块
- 一个目录中存在混合内容

但仍应优先追求：

- 一个模块尽量对应一个主目录
- 一个目录尽量服务一个模块

## 模块知识的定位

`TopoGraph` 中的模块知识应按模块单独组织和存放。

它表达的是：

- 模块身份
- 模块边界
- 模块职责
- 模块对外契约
- 模块稳定事实
- 模块沉淀后的经验摘要

它不是：

- 原始短期记忆
- 原始长期记忆全文
- 临时工作上下文
- 未经治理的草稿判断

也就是说：

> 模块知识是治理结果，不是记忆原文。

## 当前实现与目标架构的差异

当前实现仍有几个明显的过渡特征：

- 节点主要仍由旧清单模型驱动生成
- `Project` / `Department` 骨架尚未完全内建为图谱正式节点
- API 层仍承担了一部分图谱骨架拼装职责
- 某些旧知识摘要仍与 `MemoryStore` 耦合

## 下一步重构基线

后续 `TopoGraph` 重构建议按以下顺序推进：

1. 固化 `Project / Department / Technical / Team` 四类正式节点
2. 让 `Project` 与 `Department` 骨架节点由 `TopoGraph` 自己产出，而不是由 API 层临时拼装
3. 把主树、依赖边、协作边明确分层构建
4. 把最终模块知识的存放从旧 `MemoryStore` 耦合中迁回 `TopoGraph`
5. 继续把“模块定义”和“物理目录事实”彻底分离

## 对外提供

当前这一层对外提供：

- `ITopoGraphFacade`
- `ITopoGraphDefinitionStore`
- 与拓扑快照、节点、关系、注册信息相关的领域模型

## 当前架构结论

`TopoGraph` 的正确方向不是“代码依赖图”，而是：

> 从工程管理与知识组织视角，对项目、部门、技术能力和业务模块进行统一抽象，并按模块组织最终知识的知识拓扑层。

后续所有 `TopoGraph` 重构，都应以这份文档作为基础约束。
## 当前对外门面（2026-04-03）

为避免 UI、MCP、CLI 继续直接依赖兼容态 `KnowledgeNode` 结构，`TopoGraph` 当前统一通过 `ITopoGraphApplicationService` 暴露稳定门面：

- `GetModuleKnowledge(nodeIdOrName)`
  - 按模块读取稳定知识视图。
  - 返回模块身份、边界、路径、依赖与 `NodeKnowledge` 沉淀结果。

- `ListModuleKnowledge()`
  - 枚举当前所有模块的稳定知识视图。
  - 供模块列表、知识管理面板、CLI 批量查询使用。

- `SaveModuleKnowledge(command)`
  - 以模块为单位回写知识文档。
  - 内部统一走 `TopoGraphStore` 文件协议持久化，再刷新拓扑缓存。

- `GetModuleRelations(nodeIdOrName)`
  - 返回模块的入边与出边视图。
  - 统一覆盖 `Containment / Dependency / Collaboration` 三类关系。
- `GetWorkbenchSnapshot()`
  - 返回可直接供 App / UI 使用的工作台拓扑视图。
  - 将 `project / discipline / module / edge` 的拼装职责收口回 `TopoGraph`，避免宿主层重复组装。

这组门面意味着：

- 上层不需要感知 `identity.md` 的文件格式细节。
- 上层不需要直接访问 `ITopoGraphStore`。
- `TopoGraphApplicationService` 负责“模块知识 + 模块关系”的稳定读写语义。
- `TopoGraphStore` 继续下沉为持久化细节与运行时缓存实现。
