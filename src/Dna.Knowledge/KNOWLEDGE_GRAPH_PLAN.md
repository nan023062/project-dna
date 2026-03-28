# 知识图谱系统建设计划

> 目标：快速搭建知识图谱体系，让团队通过 Cursor/Codex Agent 灌入和消费项目知识。
> 不涉及内置 Agent、任务编排、架构治理等内容。

---

## 定位

**知识图谱 = 项目的集体大脑**，替代飞书文档、Confluence、口头约定中散落的全部项目知识。

**Project DNA 只做存储 + CRUD 接口**，知识灌入由团队成员通过外部 IDE Agent 完成。

**部署架构**：单服 MCP（服务器 P4 工作区）+ N 个 Cursor + 1 个 Dashboard。

---

## Phase 0：基础设施 [DONE]

| 模块 | 交付物 |
|------|--------|
| 存储引擎 | SQLite WAL + JSON entries + FTS5 全文索引 + 内存向量索引 |
| 记忆模型 | MemoryEntry（五层坐标系 + 七职能 + 鲜活度 + 来源） |
| 四通道召回 | 向量语义 + FTS + 标签匹配 + 坐标过滤，融合排序 |
| 约束链 | ExpandConstraintChain（L0→L3 上层约束自动展开） |
| 模块注册 | modules.json + architecture.json（部门/分层/模块/CrossWork） |
| 拓扑构建 | TopologyBuilder（从 manifest 构建模块依赖图） |
| 鲜活度 | FreshnessChecker（时间衰退 + 路径降级）+ MemoryMaintainer（冲突检测 + 归档） |

## Phase 1：MCP CRUD 完善 [DONE]

| 模块 | 交付物 |
|------|--------|
| 记忆写入 | `remember` / `batch_remember`（单条 + 批量，上限 50 条） |
| 记忆修改 | `update_memory`（只传要改的字段，自动刷新索引） |
| 记忆删除 | `delete_memory`（同步清理 SQLite + FTS + 向量） |
| 记忆查询 | `recall`（语义搜索）/ `query_memories`（结构化筛选）/ `get_memory`（按 ID）/ `get_memory_stats`（统计） |
| 架构管理 | `register_discipline` / `register_module` / `delete_module` / `register_crosswork` / `delete_crosswork` / `get_manifest` / `list_disciplines` |
| 模块属性 | summary / boundary / publicApi / constraints / metadata（结构化 + 自定义扩展） |
| 工具描述 | MemoryTools + KnowledgeTools 全部重写（场景说明 + 参数示例 + 工具间区别） |
| HTTP API | `POST /api/memory/batch` |
| 文档 | setup-cursor.md / setup-codex.md 完整重写 |
| Cursor Rules | `dna-workflow.mdc`（开发用）+ `knowledge-injection.mdc`（灌入用） |
| 前端 | memory-editor 自定义 Modal + Toast（替换原生 alert/confirm） |
| 并发安全 | SQLite WAL 模式 + busy_timeout |

---

## Phase 2：Dashboard 补齐 [TODO]

> 目标：Dashboard 能展示和编辑模块结构化属性，提供知识灌入进度看板。

### 2.1 模块编辑器支持新字段

文件：`src/Server/wwwroot/js/dialogs/module-editor.js`

| 任务 | 验收 |
|------|------|
| 表单增加 summary 文本框 | 编辑模块时可填写/修改职责描述 |
| 表单增加 boundary 下拉（open/semi-open/closed） | 选择边界模式 |
| 表单增加 publicApi 标签输入 | 可添加/删除对外接口 |
| 表单增加 constraints 列表输入 | 可添加/删除约束规则 |
| 表单增加 metadata key-value 编辑器 | 可自由添加/删除扩展属性 |
| 保存后 modules.json 正确写入新字段 | Dashboard 刷新后字段保留 |

### 2.2 知识健康度看板

文件：`src/Server/wwwroot/js/panels/governance.js`

| 任务 | 验收 |
|------|------|
| 展示各层级知识分布（L0/L1/L2/L3/L4 各多少条） | 能直观看到哪层空缺 |
| 展示各部门知识覆盖（哪些 discipline 有知识、哪些空） | 识别未灌入的部门 |
| 展示模块属性完整度（多少模块填了 summary、多少没填） | 追踪灌入进度 |
| 知识新鲜度分布（Fresh/Aging/Stale 各占比） | 识别老化知识 |
| 冲突知识标记展示 | 一键查看所有 #conflict 条目 |

---

## Phase 3：质量加固 [TODO]

> 目标：提升检索质量，加固边界处理，补齐蒸馏能力。

### 3.1 recall 质量优化

| 任务 | 验收 |
|------|------|
| recall 结果增加来源标记（AI 灌入 / 人工灌入 / 蒸馏生成） | Agent 可判断知识权威性 |
| recall 结果按鲜活度排序加权（Fresh 优先于 Aging） | 新知识优先展示 |
| 空检索结果时返回建议（推荐相关标签/模块/层级） | 减少「未找到」的死胡同 |

### 3.2 MCP 边界处理加固

| 任务 | 验收 |
|------|------|
| register_module 重复模块名时返回明确提示（更新 vs 冲突） | 不会静默覆盖 |
| register_crosswork 引用不存在的模块时警告 | 提示先注册模块 |
| batch_remember 中单条失败不影响其他条目 | 部分成功 + 失败详情 |
| delete_module 时检查是否有其他模块依赖它 | 返回影响范围警告 |

### 3.3 LLM 语义蒸馏

| 任务 | 验收 |
|------|------|
| MemoryMaintainer 接入 LLM 做语义冲突检测 | 同模块矛盾记忆自动标记 #conflict |
| 冗余记忆合并（同主题多条 → 合并为一条） | 手动触发蒸馏后记忆数减少 |
| 蒸馏结果标记来源为 `MemorySource.Distilled` | 可区分原始灌入 vs 蒸馏产物 |

---

## Phase 4：首个项目落地 [TODO]

> 目标：在真实游戏项目上跑通完整的灌入→消费流程。

### 4.1 服务器部署

| 任务 | 验收 |
|------|------|
| 在内网服务器上部署 dna | `http://服务器IP:5051` 可访问 Dashboard |
| 配置 P4 工作区作为 projectRoot | `get_manifest()` 返回空架构（新项目） |
| 团队 3 人配置 Cursor MCP 连接 | 3 人均可调用 `get_memory_stats()` |

### 4.2 架构骨架灌入

| 任务 | 验收 |
|------|------|
| 定义部门和分层（gameplay/ui/network/design/art 等） | `list_disciplines()` 返回完整部门列表 |
| 注册核心模块（20-30 个主要模块） | `get_topology()` 显示分层拓扑 |
| 填写模块结构化属性（summary/boundary/publicApi/constraints） | `begin_task("模块名")` 显示职责和约束 |
| 声明关键 CrossWork（5-10 个跨模块协作） | `list_crossworks()` 返回协作声明 |
| `validate_architecture()` 无严重违规 | 架构健康检查通过 |

### 4.3 知识内容灌入

| 任务 | 验收 |
|------|------|
| L0 项目愿景（8-12 条） | `recall("项目用什么引擎")` 命中 |
| L1 核心部门规范（每部门 5-10 条） | `query_memories(layer=L1)` 返回 30+ 条 |
| L2 关键跨部门协议（10-20 条） | `recall("配表导出流程")` 命中完整流程 |
| L3 核心系统设计（每系统 3-5 条） | `get_feature_knowledge("combat")` 返回多职能知识 |
| 总计 >= 100 条有效知识 | `get_memory_stats()` total >= 100 |

### 4.4 消费验证

| 场景 | 验收 |
|------|------|
| Agent 修改 Combat 模块前调用 `begin_task` | 返回完整约束链 + 模块属性 + CrossWork + 教训 |
| Agent 查询 GC 规范 | `recall("GC 规范")` 返回准确规范内容 |
| Agent 按规范开发后记录教训 | `remember(#lesson)` → 后续 `recall` 可命中 |
| 策划用 Cursor 灌入配表规范 | `batch_remember` 写入 → `query_memories(layer=L2)` 确认 |
| Dashboard 查看知识健康度 | 各层级/部门分布清晰可见 |

---

## Phase 5：持续运营 [TODO]

> 目标：知识库保持鲜活、不腐化。

| 任务 | 频率 | 验收 |
|------|------|------|
| 定期鲜活度巡检 | 每周 | Dashboard 触发 check-freshness，Aging/Stale 条目被标记 |
| 过时知识归档 | 每月 | 超 30 天的 Stale 记忆自动归档 |
| 冲突知识清理 | 每月 | #conflict 标记的条目被人工审核并修正 |
| 蒸馏触发 | 按需 | 模块积累 10+ 条记忆后提醒蒸馏为结构化属性 |
| 知识库统计报告 | 每周 | `get_memory_stats` 发送到飞书/Slack |

---

## 执行优先级

```
Phase 0 [DONE] → Phase 1 [DONE] → Phase 4（落地验证）→ Phase 2（Dashboard）→ Phase 3（质量）→ Phase 5（运营）
                                        ↑ 当前重点
```

**Phase 4 优先于 Phase 2/3**：先在真实项目上跑通，暴露真实痛点，再回来补 Dashboard 和质量。Dashboard 美化和蒸馏可以边用边补。
