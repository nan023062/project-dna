# 工程 DNA 生态扩展设计

> **状态**：设计稿 v1.0 — 2026-03-28
> **作者**：李南
> **前置**：阅读 [project-dna-design.md](./project-dna-design.md) 了解核心模型

---

## 一、设计目标

工程 DNA 不只是一个工具，而是一个**可以承载社区的平台**。

核心判断标准：一个开源项目能否形成社区，取决于它是否有——
- **清晰的最小贡献单元**：社区成员能贡献什么？
- **低门槛**：贡献一个扩展有多容易？
- **即时价值**：别人安装这个扩展后能立刻获益吗？
- **可组合**：扩展之间能叠加使用吗？

对标：

| 系统 | 最小贡献单元 | 社区规模驱动力 |
|------|-------------|---------------|
| ESLint | Rule 插件 | 每种框架都需要自己的规则集 |
| Docker Hub | Image 模板 | 每种中间件都需要预构建镜像 |
| Terraform Registry | Provider + Module | 每种云服务都需要适配 |
| Codex Skills | Skill 文件 | 每种操作流程都可以封装为技能 |

**DNA 的驱动力**：每种项目类型都需要自己的 DNA 模板和扫描器。

---

## 二、六大扩展维度

```
┌──────────────────────────────────────────────────────────┐
│                     DNA Registry（社区仓库）               │
│                                                          │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌───────────┐  │
│  │ Templates │ │ Scanners │ │ Gov Rules │ │Extractors │  │
│  │          │ │          │ │           │ │           │  │
│  │unity-game│ │csharp    │ │clean-arch │ │confluence │  │
│  │react-app │ │typescript│ │unity-perf │ │jira       │  │
│  │spring    │ │swagger   │ │game-team  │ │markdown   │  │
│  │unreal    │ │excel     │ │react-arch │ │swagger    │  │
│  │flutter   │ │fbx-asset │ │...        │ │...        │  │
│  └──────────┘ └──────────┘ └───────────┘ └───────────┘  │
│                                                          │
│  ┌──────────────┐ ┌──────────────────┐                   │
│  │ Interpreters │ │ Storage Backends │                   │
│  │ qa / devops  │ │ postgres / s3    │                   │
│  │ pm / ta      │ │ git-native       │                   │
│  └──────────────┘ └──────────────────┘                   │
└──────────────────────────────────────────────────────────┘
          ↓ dna install
┌──────────────────────────────────────────────────────────┐
│                  Project DNA Server                       │
│                                                          │
│  Template(unity-game) + Scanner(csharp+unity)            │
│  + Rules(unity-perf) + Extractor(confluence)             │
│  + Interpreter(coder+designer+art+qa)                    │
└──────────────────────────────────────────────────────────┘
```

### 2.1 DNA 模板（Templates）— P0，社区飞轮的起点

**是什么**：预构建的 DNA 结构，描述某种项目类型的标准节点树、依赖模式、默认知识和约束。

**类比**：Docker Image / cookiecutter 模板 / `create-react-app`

**目录结构**：

```
dna-templates/
├── unity-game/
│   ├── dna-template.json     ← 节点树 + 依赖 + 默认知识
│   ├── README.md
│   └── preview.png           ← 拓扑预览图
├── react-app/
├── spring-boot-service/
├── unreal-project/
├── python-ml-pipeline/
└── flutter-app/
```

**`dna-template.json` 示例**（unity-game 片段）：

```json
{
  "name": "unity-game",
  "description": "Unity 游戏项目标准 DNA 模板",
  "version": "1.0.0",
  "nodes": [
    {
      "name": "Root",
      "type": "Root",
      "knowledge": {
        "identity": "Unity 游戏项目",
        "constraints": ["Unity 2022+ LTS", "目标平台 iOS/Android"]
      }
    },
    {
      "name": "技术部",
      "type": "Department",
      "parent": "Root",
      "children": ["引擎框架", "战斗系统", "UI系统", "网络层"]
    },
    {
      "name": "引擎框架",
      "type": "Module",
      "parent": "技术部",
      "pathPattern": "Assets/Scripts/Framework/**",
      "knowledge": {
        "constraints": ["禁止 Update 中 new 引用类型", "对象池统一用 PoolManager"],
        "contract": "IFrameworkService: Init/Tick/Dispose 生命周期"
      }
    }
  ],
  "suggestedScanners": ["csharp", "unity-asmdef"],
  "suggestedRules": ["unity-performance-rules"]
}
```

**用户体验**：

```bash
dna init --template=unity-game
# → 生成 .agentic-os/ 目录
# → 预填充节点树、约束、知识
# → 提示："建议安装 Scanner: csharp, unity-asmdef"

dna init --template=react-app
# → 预填充：src/components, src/hooks, src/api, src/store
# → 预填充约束："组件层不直接调 API"
```

**为什么是 P0**：
- 贡献门槛最低——一个 JSON 文件就是一个模板
- 价值最直接——用户 init 后立刻获得有意义的 DNA，不用从零开始
- 覆盖面最广——每种技术栈都需要自己的模板
- 可组合——模板可以继承（`unity-game` 继承 `generic-game`）

### 2.2 扫描器插件（Scanners）— P0，降低使用门槛的关键

**是什么**：自动分析项目文件系统，推导出节点、依赖和知识。

**类比**：ESLint 的 parser / Terraform 的 provider

**接口定义**：

```csharp
public interface IDnaScanner
{
    string Name { get; }
    string[] FilePatterns { get; }          // 感兴趣的文件 glob
    bool CanScan(string projectRoot);       // 能否识别这种项目
    ScanResult Scan(string projectRoot);    // 扫描并输出结果
}

public class ScanResult
{
    public List<ScannedNode> Nodes { get; set; } = [];
    public List<ScannedEdge> Edges { get; set; } = [];
    public List<ScannedKnowledge> Knowledge { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
```

**社区可贡献的 Scanner**：

| Scanner | 扫描源 | 输出 |
|---------|--------|------|
| `CSharpScanner` | `.csproj` / `using` 语句 | 模块节点 + 引用依赖 |
| `UnityAsmdefScanner` | Assembly Definition 文件 | 程序集模块 + 依赖 |
| `TypeScriptScanner` | `import` / `package.json` | 模块 + npm 依赖 |
| `PythonScanner` | `import` / `requirements.txt` | 包模块 + 依赖 |
| `SwaggerScanner` | OpenAPI spec | Contract 知识 |
| `ExcelTableScanner` | 策划配表的列头和外键 | 配表模块 + 引用依赖 |
| `FbxAssetScanner` | `.fbx` / `.png` 元数据 | 美术资源规格知识 |
| `DockerfileScanner` | `Dockerfile` / `docker-compose.yml` | 服务模块 + 端口依赖 |
| `TerraformScanner` | `.tf` 文件 | 基础设施模块 + 资源依赖 |
| `GradleScanner` | `build.gradle` | Java 模块 + 依赖 |

**用户体验**：

```bash
dna scan
# → 自动检测项目类型，运行匹配的 Scanner
# → "发现 23 个模块、47 条依赖、建议创建 3 个 CrossWork"
# → 用户确认后写入 DNA

dna scan --scanner=unity-asmdef --dry-run
# → 预览扫描结果，不写入
```

**为什么是 P0**：没有自动扫描，用户要手动注册每个节点和依赖——对于几十个模块的项目，这不现实。Scanner 让用户 5 分钟上手。

### 2.3 治理规则包（Governance Rules）— P1

**是什么**：可插拔的架构检查规则，每条规则检测一种模式并给出建议。

**类比**：ESLint Rule / SonarQube Quality Profile

**接口定义**：

```csharp
public interface IGovernanceRule
{
    string Id { get; }              // "unity-perf/draw-call-budget"
    string Name { get; }            // "DrawCall 预算检查"
    string Category { get; }        // "performance"
    RuleSeverity Severity { get; }  // Suggestion / Warning / Critical
    GovernanceSuggestion? Check(KnowledgeNode node, TopologySnapshot topology);
}

public class GovernanceSuggestion
{
    public string RuleId { get; init; }
    public string NodeName { get; init; }
    public string Message { get; init; }
    public string? Suggestion { get; init; }
}
```

**社区可贡献的规则包**：

| 规则包 | 包含规则示例 |
|--------|-------------|
| `unity-performance-rules` | DrawCall 预算、禁止 Resources.Load、GC 约束检查 |
| `clean-architecture-rules` | Domain 不依赖 Infrastructure、单向依赖流 |
| `react-architecture-rules` | 组件层不直接调 API、状态管理集中 |
| `game-team-rules` | 跨职能必须有 CrossWork、美术资源必须有规格约束 |
| `microservice-rules` | 服务间依赖不超过 3 层、每个服务必须有 Contract |

**用户体验**：

```bash
dna install rules clean-architecture-rules
dna validate
# → "建议：UserService 节点缺少 Contract 声明"
# → "建议：Repository 模块被 Controller 直接依赖，应通过 UseCase 层间接引用"
```

### 2.4 知识提取器（Knowledge Extractors）— P1

**是什么**：从已有文档和外部系统自动提取结构化知识，灌入节点的 Knowledge。

**接口定义**：

```csharp
public interface IKnowledgeExtractor
{
    string Name { get; }
    string[] SupportedSources { get; }    // "*.md", "confluence", "jira"
    Task<List<ExtractedKnowledge>> ExtractAsync(ExtractionContext context);
}

public class ExtractedKnowledge
{
    public string TargetNodeName { get; init; }  // 归属哪个节点
    public string Content { get; init; }
    public string? Summary { get; init; }
    public List<string> Tags { get; init; } = [];
}
```

**社区可贡献的 Extractor**：

| Extractor | 来源 | 提取内容 |
|-----------|------|----------|
| `MarkdownExtractor` | README / 设计文档 | 模块描述、约定 |
| `ConfluenceExtractor` | Confluence 页面 | 团队规范、设计文档 |
| `JiraExtractor` | JIRA ticket | 教训、决策记录 |
| `SwaggerExtractor` | OpenAPI spec | API Contract |
| `GitHistoryExtractor` | Git log | 高频修改模块、热点文件 |
| `CodeCommentExtractor` | 源码注释 `// DNA:` | 开发者内联标注的知识 |

### 2.5 角色解释器（Context Interpreters）— P2

**是什么**：同一份 DNA，不同角色看到不同的上下文呈现。

当前已有三种角色（`coder` / `designer` / `art`），社区可扩展：

| Interpreter | 视角 | 看到什么 |
|-------------|------|----------|
| `coder` | 程序员 | 代码约束、接口契约、GC 规范 |
| `designer` | 策划 | 数值公式、配表字段、玩法逻辑 |
| `art` | 美术 | 面数/贴图/骨骼规格、命名规范 |
| `qa` | 测试 | 测试用例、回归清单、已知缺陷 |
| `devops` | 运维 | 部署依赖、环境要求、监控指标 |
| `pm` | 项目经理 | 进度、里程碑、资源瓶颈、风险 |
| `ta` | 技术美术 | 着色器规范、美术管线约束 |

### 2.6 存储后端（Storage Backends）— P2

**是什么**：DNA 数据的持久化方式。

| Backend | 适用场景 | 特点 |
|---------|---------|------|
| `SqliteBackend`（默认） | 单机 / 小团队 | 零配置，开箱即用 |
| `PostgresBackend` | 大团队 / 企业 | 支持并发、备份、审计 |
| `GitNativeBackend` | 离线 / 纯文件偏好 | 类 Pensieve，纯 Markdown 降级 |
| `S3Backend` | 云原生 | 无服务器持久化 |

---

## 三、与 Skill 系统的关系

DNA 和 Skill 不是竞品，而是**互补的两个层**：

| 维度 | Skill（Codex/Cursor） | DNA |
|------|----------------------|-----|
| 回答什么 | **How** — 怎么做某件事 | **What** — 这个项目是什么 |
| 类比 | 操作手册 | 基因蓝图 |
| 生命周期 | 按需加载，用完即弃 | 伴随项目持续演化 |
| 社区贡献物 | 「如何部署 K8s」 | 「K8s 项目的标准 DNA 模板」 |

**协同方式**：

1. **DNA Template 推荐 Skill**：模板 JSON 里的 `suggestedSkills` 字段推荐安装哪些 Skill
2. **Skill 读 DNA**：技能执行前先查 DNA 获取项目约束，做到上下文感知
3. **DNA 记录 Skill 执行结果**：Skill 执行完的教训/变更，自动写回 DNA 的 Knowledge

```
DNA Template: unity-game
├── suggestedSkills: ["unity-optimization", "unity-profiling"]
│
│   Skill: unity-optimization
│   ├── 执行前：读 DNA 获取当前模块的 GC 约束
│   ├── 执行中：按约束优化代码
│   └── 执行后：remember("优化了对象池，GC 降低 40%") → 写回 DNA
```

---

## 四、用户旅程

### 4.1 新用户首次接入（5 分钟）

```bash
# 1. 初始化 — 选模板，5 秒生成基础 DNA
dna init --template=unity-game

# 2. 扫描 — 自动分析项目文件，1 分钟完成
dna scan

# 3. 启动 — 接入 Cursor/Codex
dna serve
```

### 4.2 团队日常使用

```
开发者打开 Cursor
  → Agent 自动调 get_node() 获取当前模块上下文
  → 带着约束修改代码
  → 完成后 remember() 写回知识
  → DNA 持续演化
```

### 4.3 社区贡献者

```bash
# 1. Fork dna-registry 仓库
# 2. 创建新模板/扫描器/规则包
# 3. 本地测试
dna validate --template=my-template

# 4. 提交 PR
# 5. 通过审核后发布到 Registry
```

---

## 五、社区飞轮

```
更多模板/扫描器
      ↓
更多项目类型的用户涌入
      ↓
更多人贡献规则包和提取器
      ↓
DNA 覆盖更多技术栈
      ↓
更多模板/扫描器 ...（正向循环）
```

**启动飞轮的最小动作**：

1. 发布核心引擎（GraphEngine + MemoryEngine + GovernanceEngine）
2. 发布 2-3 个高质量模板（unity-game、react-app、generic）
3. 发布 2-3 个核心 Scanner（csharp、typescript、python）
4. 定义清晰的扩展接口和贡献指南
5. 建立 `dna-registry` 仓库作为社区扩展的集散地

---

## 六、优先级路线

| 阶段 | 交付物 | 目标 |
|------|--------|------|
| **Phase 0** | 核心引擎 + 统一节点模型 + 三引擎拆分 | 系统可用 |
| **Phase 1** | Template 机制 + `dna init` 命令 + 3 个模板 | 用户能快速上手 |
| **Phase 1** | Scanner 机制 + `dna scan` 命令 + 3 个扫描器 | 用户不用手动建图 |
| **Phase 2** | Governance Rule 机制 + 2 个规则包 | 治理能力可扩展 |
| **Phase 2** | Extractor 机制 + Markdown/Confluence | 已有知识能迁入 |
| **Phase 3** | dna-registry 仓库 + 贡献指南 + CI 验证 | 社区基础设施就绪 |
| **Phase 3** | Interpreter 扩展 + Storage Backend 扩展 | 企业级适配 |

---

## 七、一句话总结

> **DNA 的核心是图引擎，DNA 的生态是扩展体系。**
>
> Template 让用户 5 分钟上手，Scanner 让 DNA 自动生成，
> Governance Rules 让最佳实践可复用，Extractor 让已有知识可迁入。
> 每种项目类型都需要自己的 DNA——这就是社区贡献的永动机。
