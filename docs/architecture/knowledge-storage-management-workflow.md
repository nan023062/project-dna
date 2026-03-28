# Project DNA：知识存储与管理流程（DB-first）

> 版本：v1.0  
> 更新时间：2026-03-29  
> 适用范围：Project DNA（`agentic-os` + `project-dna`）

---

## 1. 存储总览

当前采用 **全 DB** 策略，知识不再依赖 JSON 文件：

- `graph.db`：长期知识层（模块图、依赖、CrossWork、`NodeKnowledge` 物化视图）
- `index.db`：短期记忆层（原始记忆、检索索引、FTS/向量元数据）

核心原则：

- 一切实体都是模块图中的 `KnowledgeNode`
- 每条记忆只归属一个节点（`NodeId`）
- 模块长期知识由短期记忆定期压缩提炼得到

---

## 2. 写入流程（知识进入系统）

1. 先定位模块：`search_modules` / `get_context`
2. 确认归属节点：为知识选择唯一 `NodeId`
3. 写入短期记忆：`remember` / `batch_remember`（进入 `index.db`）
4. 必要时修订：`update_memory` / `delete_memory`
5. 治理压缩：将模块短期记忆提炼为 `NodeKnowledge`（写入 `graph.db`）

---

## 3. 读取流程（Agent 使用知识）

1. 修改前先取上下文：`get_context("模块名")`
2. 深度问题走语义检索：`recall("问题")`
3. 结构化排查用：`query_memories` / `get_memory`
4. 架构风险检查：`validate_architecture` / `check_freshness`

---

## 4. 管理流程（日常维护）

### 4.1 日常写入

- 新决策、新约定、新任务结果，优先写短期记忆（`index.db`）
- 严格绑定 `NodeId`，避免“跨模块悬空记忆”

### 4.2 定期压缩

- 单模块压缩：`condense_module_knowledge`
- 全量压缩：`condense_all_module_knowledge`
- 调度执行：由 `KnowledgeCondenseScheduler` 按配置自动运行

### 4.3 质量治理

- 鲜活度检查：标记可能过期知识
- 冲突检测：发现同节点矛盾记忆
- 归档策略：压缩后将细粒度旧记忆归档降噪

---

## 5. 运行与配置约定

- 启动方式统一：`--db <知识库目录>`
- 不再使用：`architecture.json`、`modules.json`、`memory/entries/*.json`
- 调度配置入口：
  - REST API：`/api/config/governance/condense-schedule`
  - Dashboard：治理面板中的压缩与调度设置

---

## 6. 团队操作标准（建议）

- 改代码前：先 `get_context`，有疑问先 `recall`
- 任务完成后：沉淀一条可复用知识（决策/约定/教训/结果）
- 每日或每周：执行一次模块知识压缩，确保长期知识持续收敛

---

## 7. 一句话版本

> `index.db` 管短期记忆，`graph.db` 管长期模块知识；  
> 先按 `NodeId` 写入，再通过治理压缩提炼，最终让 Agent 通过模块上下文直接消费高质量知识。
