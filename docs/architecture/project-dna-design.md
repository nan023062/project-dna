# Project DNA: Simplicity is the Ultimate Sophistication, Unity in All Things

> **Status**: Design Draft v1.4 (synced to implementation) — 2026-03-29
> **Author**: nave
> **Scope**: Core model and refactoring direction for the Agentic OS Project DNA system
> **Documentation Convention**: In `project-dna/docs/architecture`, only this design doc and `project-dna-ecosystem.md` are kept. Planning/action/roadmap docs stay in the parent `agentic-os` repository.

---

## 1. Design Philosophy

### 1.0 Purpose

**Project DNA is the distillation of content knowledge and relationships from a project's engineering file system.**

A project file system contains thousands of files — an Agent cannot traverse and comprehend them from scratch every time.
Project DNA encodes the file system's structure, knowledge, and relationships into a queryable graph —
by reading the DNA, an Agent can quickly obtain context, understand dependencies, follow process conventions, orchestrate tasks, and precisely modify target files.

```
Physical Layer (File System)     Cognitive Layer (Project DNA)        Action Layer (Agent)
┌──────────────────────┐      ┌───────────────────────────┐      ┌────────────────────────┐
│ Thousands of files   │─Distill→│ Dozens of knowledge-rich │─Serve→│ Precisely operate on   │
│ Dirs/Code/Resources  │      │ nodes with deps/collab/   │      │ target files with full  │
└──────────────────────┘      │ constraints               │      │ context                │
                              └───────────────────────────┘      └────────────────────────┘
```

DNA is not the organism itself, but the organism's blueprint.
Project DNA is not the files themselves, but the **cognitive encoding** of the file system.

| Biological DNA | Project DNA |
|----------------|-------------|
| Gene segments encode protein functions | Nodes encode module responsibilities, constraints, contracts |
| Genes have regulatory relationships | Nodes have dependency and collaboration relationships |
| DNA guides how cells work | DNA guides how Agents operate on files |
| Mutations are detected by repair mechanisms | Constraint violations are discovered by the Governance Engine |
| DNA can be inherited and replicated | Knowledge can be accumulated, passed down, and reused |

### 1.1 Core Idea

**Organizational architecture and software architecture are isomorphic.**

All roles, teams, and collaboration relationships follow the same design principles as software architecture.
Project DNA is not "an accessory to code," but a **project cognitive network** modeled with software architecture thinking.

### 1.2 Fundamental Axioms

| Axiom | Software Architecture | Organizational Form |
|-------|----------------------|---------------------|
| **Cohesion** | Single responsibility, high cohesion & low coupling | Specialized roles (combat programmer, character artist) |
| **Contract** | Interfaces/APIs with stable external exposure | Team's external commitments: deliverables and SLAs |
| **Dependency** | Unidirectional dependency, no cycles allowed | Consumption relationship: I use your service/data |
| **Collaboration** | Composite pattern, mediator coordination | Task forces / working groups co-delivering composite goals |
| **Aggregation** | Packages / namespaces for logical grouping | Departments / divisions managing multiple teams |
| **Governance** | DAG validation, architecture recommendations | Architecture advisors, organizational health checks |

### 1.3 Three Iron Rules

| # | Rule | Meaning |
|---|------|---------|
| 1 | **Containment is a tree** | Each node has at most one Parent; hierarchy is determined by tree depth |
| 2 | **Dependencies form a DAG** | Any node can establish a unidirectional dependency on another, regardless of organizational boundaries, as long as there are no cycles |
| 3 | **Cycle = should merge** | A circular dependency means these nodes are effectively a single cohesive unit and should be merged or formed into a working group |

Dependency and collaboration are two orthogonal dimensions:

| Concept | Essence | Direction | Example |
|---------|---------|-----------|---------|
| **Dependency** (Edge) | "I need to use your output" | Unidirectional consumption | Combat code reads skill config table |
| **Collaboration** (CrossWork) | "We deliver something together" | Multi-party co-creation | Battle group: design + code + art + audio |

Dependencies can freely cross organizational boundaries, just like `import` can reference any package — as long as there are no cycles.
CrossWork is a proactively declared collaboration entity, not a replacement for dependencies.

### 1.4 Fractal Principle

Every level — from the project root to the smallest module — is a **recursive instance of the same pattern**.
A department and a module have exactly the same attribute structure; the only difference is granularity and containment.

```
Studio ⊃ Department ⊃ Team/Task Force ⊃ Module
  │        │            │                 │
  └────────┴────────────┴─────────────────┘
         Same node type, different scales
```

---

## 2. Unified Node Model

### 2.1 KnowledgeNode — The Single Graph Node Type

Abandon the three separate data structures: `GraphNode` / `CrossWork` / `Department`.
**The graph has only one node type: `KnowledgeNode`**, expressing all organizational forms through `NodeType` and hierarchical relationships.

```csharp
public class KnowledgeNode
{
    // ── Identity ──
    public string Id { get; set; }
    public string Name { get; set; }
    public NodeType Type { get; set; }

    // ── Hierarchy (Tree: Containment) ──
    public string? ParentId { get; set; }
    public List<string> ChildIds { get; set; } = [];
    // Hierarchy needs no explicit declaration — the tree depth from node to Root IS the level.

    // ── Dependencies (Directed Acyclic Graph) ──
    public List<string> Dependencies { get; set; } = [];
    // Dependencies can cross any organizational boundary; the only constraint is no cycles.

    // ── Contract ──
    public string? Contract { get; set; }
    public List<string>? PublicApi { get; set; }
    public List<string>? Constraints { get; set; }

    // ── Structural Attributes ──
    public string? RelativePath { get; set; }
    public string? Maintainer { get; set; }
    public string? Boundary { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    // ── Embedded Knowledge (Materialized View) ──
    public NodeKnowledge Knowledge { get; set; } = new();
}

public enum NodeType
{
    Root,         // Project / Studio
    Department,   // Department / Division
    Module,       // Cohesive module / Specialized role
    CrossWork     // Task force / Working group
}
```

### 2.2 NodeKnowledge — The Knowledge Payload Embedded in Every Node

Every node — regardless of type — carries structured knowledge.
This is a **materialized view** projected from the Memory Engine, not a full copy of all memories.

```csharp
public class NodeKnowledge
{
    public string? Identity { get; set; }
    public string? Contract { get; set; }
    public List<LessonSummary> Lessons { get; set; } = [];
    public List<string> ActiveTasks { get; set; } = [];
    public List<string> Facts { get; set; } = [];
    public int TotalMemoryCount { get; set; }
    public List<string> MemoryIds { get; set; } = [];
}

public class LessonSummary
{
    public string Title { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string? Resolution { get; set; }
}
```

### 2.3 Organizational Mapping of the Four Node Types

| NodeType | Software Analogy | Organizational Analogy | Example |
|----------|-----------------|----------------------|---------|
| `Root` | System / Application | Studio / Project | "XX Mobile Game Project" |
| `Department` | Package / Namespace | Department / Division | Tech Platform, Art Center |
| `Module` | Class / Service | Specialized role / Team | Combat Programmer, Character Artist |
| `CrossWork` | Composite / Mediator | Task Force / Joint Team | Battle Group (design + code + art + audio) |

Hierarchy example:

```
Root: Game Studio
├── Department: Engineering Dept
│   ├── Module: Engine Support Team
│   ├── Module: Frontend Team
│   ├── Module: Backend Team
│   └── Module: DevOps
├── Department: Art Dept
│   ├── Module: Character Team
│   ├── Module: Environment Team
│   └── Module: VFX Team
├── Department: Design Dept
│   ├── Module: Combat Design
│   ├── Module: Systems Design
│   └── Module: Level Design
└── CrossWork: Battle System Task Force
    ├── Participant: Combat Design
    ├── Participant: Combat Programmer (Frontend Team)
    ├── Participant: Character Artist (Character Team)
    └── Participant: Battle Audio
```

---

## 3. Mapping File System to DNA

### 3.1 Core Rules

The file system is the physical structure (nested folders + files); DNA is the cognitive structure (nodes + relationships).
There are only two mapping rules:

| File System | DNA Node Type | Reason |
|-------------|--------------|--------|
| Folder with subdirectories | **Department** | It's a container, not an implementation |
| Folder with only files | **Module** | It's the smallest implementation unit |

**Key Principle: A node is either a container (Department) or an implementation (Module) — never both.**

If a folder has **both subdirectories and loose files**, the loose files must be promoted to an independent Module:

```
File System:                           DNA:
Battle/                                Battle (Department — pure container)
├── Skill/                             ├── Unit (Module, path: Battle/*.cs)
│   └── SkillCaster.cs                 ├── Skill (Module, path: Battle/Skill/)
├── Buff/                              ├── Buff (Module, path: Battle/Buff/)
│   └── BuffManager.cs                 └── Attribute (Module, path: Battle/Attribute/)
├── Attribute/
│   └── AttributeSystem.cs            Dependencies:
├── Unit.cs          ← loose file      Unit → Skill
├── UnitFactory.cs   ← loose file      Unit → Buff
└── BattleManager.cs ← loose file      Unit → Attribute
```

Why not assign loose files to the Battle parent node? Because it would create the confusing situation of "parent module depends on child module."
By promoting loose files to a Module, all dependencies become **unidirectional relationships between siblings** — clean and clear.

### 3.2 Code Engineering Example

```
src/                                   Root: MyGame
├── Battle/                            ├── Battle (Department)
│   ├── Skill/                         │   ├── Unit (Module) ──→ Skill, Buff, Attribute
│   │   ├── Active/                    │   ├── Skill (Department)
│   │   └── Passive/                   │   │   ├── ActiveSkill (Module)
│   ├── Buff/                          │   │   └── PassiveSkill (Module)
│   ├── Attribute/                     │   ├── Buff (Module)
│   ├── Unit.cs                        │   └── Attribute (Module)
│   └── BattleManager.cs              │
├── UI/                                ├── UI (Module)
│   └── UIManager.cs                   │
└── Network/                           └── Network (Module)
    └── NetClient.cs
```

The rule applies recursively: `Skill/` has subdirectories `Active/` and `Passive/` → Skill is promoted to Department, and each subdirectory becomes a Module.

### 3.3 Art Asset Example

The same rules apply to art assets:

```
Art/                                   Art (Department — Art Dept)
├── Characters/                        ├── ArtCommon (Module, path: Art/*.pdf)
│   ├── Hero/                          │   knowledge: "Common art standards, atlas config"
│   │   ├── Mesh/                      ├── Characters (Department — Character Team)
│   │   ├── Texture/                   │   ├── Hero (Module)
│   │   └── Animation/                 │   │   knowledge: "poly count ≤ 8000, bones ≤ 60"
│   ├── Monster/                       │   ├── Monster (Module)
│   └── NPC/                           │   └── NPC (Module)
├── VFX/                               ├── VFX (Department — VFX Team)
│   ├── Skill/                         │   ├── SkillVFX (Module)
│   └── UI/                            │   └── UIVFX (Module)
├── ArtStandard.pdf    ← loose file    │
└── TextureAtlas.asset ← loose file    Dependencies: Hero → SkillVFX (hero needs skill effects)
```

Art node Knowledge stores art specifications:

```
Hero node:
  Identity: "Hero character assets, responsible for Mesh/Texture/Animation"
  Constraints: ["poly count ≤ 8000", "bones ≤ 60", "max texture 1024x1024"]
  Contract: "Output FBX+PNG, naming: hero_{name}_{variant}"
  Lessons: ["hero_warrior's extra IK bones caused frame drops on mobile"]
```

### 3.4 Design Data Table Example

```
Design/                                Design (Department — Design Dept)
├── Tables/                            ├── DesignCommon (Module, path: Design/*.md)
│   ├── Skill/                         ├── Tables (Department — Numerical Team)
│   │   ├── SkillConfig.xlsx           │   ├── TablesCommon (Module, path: Design/Tables/*.xlsx)
│   │   └── SkillEffect.xlsx           │   ├── SkillTable (Module)
│   ├── Buff/                          │   │   knowledge: "fields: id/name/cooldown/damage"
│   │   └── BuffConfig.xlsx            │   ├── BuffTable (Module)
│   ├── Monster/                       │   └── MonsterTable (Module)
│   │   └── MonsterConfig.xlsx         ├── Documents (Department — Systems Design)
│   └── GlobalConfig.xlsx ← loose      │   ├── CombatDesign (Module)
├── Documents/                         │   └── EconomyDesign (Module)
│   ├── CombatDesign.docx              └── LevelDesign (Department — Level Design)
│   └── EconomyDesign.docx                 ├── Chapter1 (Module)
├── LevelDesign/                           └── Chapter2 (Module)
│   ├── Chapter1/
│   └── Chapter2/                      Dependencies:
└── DesignGuideline.md ← loose         SkillTable → BuffTable (skill table references Buff IDs)
                                       MonsterTable → SkillTable (monster table references skill IDs)
                                       Chapter1 → MonsterTable (level references monster configs)
```

### 3.5 Cross-functional CrossWork Example

The real value of DNA shines in cross-functional collaboration — code, art, and design nodes each reside in their own Department, linked through CrossWork:

```
Root: MyGame
├── Tech (Department)
│   └── BattleCode (Module) ──→ SkillTable   ← Code reads design data
├── Art (Department)
│   └── SkillVFX (Module)
├── Design (Department)
│   └── SkillTable (Module)
│
└── CrossWork: Fireball Task Force
    ├── Participant: SkillTable  — Design provides numerical values and behavior descriptions
    ├── Participant: BattleCode  — Code implements casting logic
    ├── Participant: SkillVFX    — Art creates flame effects
    └── Knowledge:
        ├── "Design finalizes data table → Code integrates → Art creates effects → Joint debugging"
        ├── "VFX play duration must match skill windup time"
        └── "Last time explosion radius didn't match data table — table used cm, code used m"
```

### 3.6 Universal Mapping Rules Summary

| Rule | Code | Art | Design |
|------|------|-----|--------|
| Has subdirectories → Department | `Battle/` | `Characters/` | `Tables/` |
| Files-only directory → Module | `Buff/` | `Hero/` | `SkillTable/` |
| Loose files → promote to Module | `Unit.cs` | `ArtStandard.pdf` | `GlobalConfig.xlsx` |
| Knowledge content | Coding standards, GC constraints | Poly count/bones/texture specs | Field specs, reference relationships |
| Sibling dependencies | `Unit → Skill` | `Hero → SkillVFX` | `SkillTable → BuffTable` |
| Cross-functional collaboration → CrossWork | Code implements logic | Art creates assets | Design provides data tables |

**One set of rules, all disciplines, the same DNA.**

---

## 4. Three Relationship Types in the Graph

The graph has only three relationship types — fully orthogonal and non-interfering:

- **Tree** governs organizational ownership
- **DAG** governs who uses what
- **CrossWork** governs who works together on what

### 3.1 Containment Relationship (Tree)

Expressed through `ParentId` / `ChildIds` to represent aggregation hierarchy.

- Root → Department → Module (organizational tree)
- A node can only have one Parent (tree constraint)
- Hierarchy = tree depth from node to Root; no extra field needed
- CrossWork's ChildIds reference participating nodes (reference relationship — does not change participants' Parent)

### 3.2 Dependency Relationship (Directed Acyclic Graph)

Expressed through the `Dependencies` list and an independent `Edge` entity for unidirectional dependencies.

```csharp
public class KnowledgeEdge
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public bool IsComputed { get; init; }
}
```

Edge has no type field — **an edge is an edge**, meaning "From consumes the service/output of To."

**Rules**:

- Dependencies can **freely cross organizational boundaries** (across Parents, across Departments — all legal)
- The only constraint: **the entire graph must be acyclic** (DAG)
- If a cycle is detected → not an error, but a **refactoring suggestion**: these nodes should be merged or formed into a working group
- Edge is a first-class citizen and can be independently added or removed

**Example**: Cross-department dependencies are perfectly legal

```
Engineering Dept            Design Dept
├── Combat Code ──→ Engine  ├── Combat Design
│       │                        ↑
│       └────────────────────────┘   ← Cross-department dependency, legal
└── UI Framework ──→ Engine └── Systems Design
```

### 3.3 Collaboration Relationship (CrossWork)

CrossWork is not a replacement for dependencies, but a **declaration of multi-party co-creation**.

- When multiple nodes need to jointly deliver a business objective, establish a CrossWork
- CrossWork nodes themselves do not produce dependency edges
- CrossWork nodes carry collaboration knowledge (contracts, joint debugging lessons, sequencing constraints, etc.)
- A simple "I use your output" does not need CrossWork — a single Edge suffices

**When to use Edge vs CrossWork**:

| Scenario | Use What |
|----------|----------|
| Combat code reads skill config table | Edge (unidirectional consumption) |
| Battle group: design + code + art + audio jointly delivering the battle system | CrossWork (multi-party co-creation) |
| Backend depends on database middleware | Edge (unidirectional consumption) |
| Tutorial task force: design defines flow + code implements + QA validates | CrossWork (multi-party co-creation) |

---

## 5. Memory Ownership: One Memory Belongs to One Node

### 4.1 Farewell to Many-to-Many

Old model: `MemoryEntry.ModuleIds: List<string>` — a memory associated with multiple modules, ambiguous ownership.

New model: `MemoryEntry.NodeId: string` — each memory strictly belongs to one node in the graph.

### 4.2 Ownership Rules

| Memory Nature | Owner Node | Example |
|---------------|-----------|---------|
| Module-internal knowledge | That Module node | "BattleSystem must not `new` objects in Update" |
| Cross-module collaboration knowledge | CrossWork node | "Fireball: damage calculation must wait for VFX callback before deducting HP" |
| Department-level standards | Department node | "Engineering dept code reviews require 2+ reviewers" |
| Project-level global knowledge | Root node | "Project uses Unity 2022.3 LTS, minimum frame rate 30fps" |

### 4.3 Materialized View Mechanism

Write path:

```
Client → remember(nodeId, content)
       → MemoryEngine stores the memory
       → Notifies GraphEngine to refresh that node's Knowledge summary
```

Read path:

```
Client → get_node(name)
       → Returns KnowledgeNode (with Knowledge summary — one call satisfies 80% of scenarios)

Client → recall(question, nodeId)
       → MemoryEngine performs semantic search, returns detailed memories (deep query)
```

### 4.4 Dependency Initiator Priority Principle

When a piece of knowledge involves two modules with a dependency relationship (not a CrossWork), assign it to the **dependency initiator** (the From node), because that's the one who "stepped on the mine."
If the knowledge describes a long-term collaboration agreement, the system suggests upgrading to a CrossWork.

---

## 6. Architecture Principle Isomorphism Table

### 5.1 Structural

| Software Principle | Graph Manifestation | Governance Suggestion |
|-------------------|---------------------|----------------------|
| Single Responsibility (SRP) | Each Module node is responsible for a single cohesive domain | Whether node Knowledge content exceeds declared responsibilities |
| Open/Closed (OCP) | Contract is stable externally, extensible internally | Contract changes require approval workflow |
| Dependency Inversion (DIP) | Dependencies point to Contracts, not internal implementations | Flag dependencies on nodes without Contracts |
| Interface Segregation (ISP) | PublicApi exposes only necessary interfaces | Interface bloat detection |

### 5.2 Relational

| Software Principle | Graph Manifestation |
|-------------------|---------------------|
| DAG Constraint | Entire graph is acyclic; Tarjan SCC detection |
| Cycle = insufficient cohesion | Cycle detected → suggest merging into one node or forming a working group |
| Tree ownership | Parent-Child containment; hierarchy = tree depth |
| Law of Demeter (LoD) | Visibility control: nodes not on the dependency chain or in the same CrossWork are invisible |

### 5.3 Collaborative

| Software Pattern | Organizational Mapping | Graph Manifestation |
|-----------------|----------------------|---------------------|
| Mediator | Project manager, producer | CrossWork node |
| Facade | Tech director, external liaison | Department node's Contract |
| Adapter | Technical Artist (TA) | IContextInterpreter translates context by role |
| Observer | Weekly reports / standups / change notifications | Memory write triggers node Knowledge refresh |
| Event-Driven | Milestone triggers workflow | ExecutionPlan topological sort |

### 5.4 Governance

The Governance Engine's positioning is **architecture advisor**, not rule police — it offers suggestions, not violation reports.

| Detection | Suggestion |
|-----------|-----------|
| A ↔ B form a cycle | "A and B are tightly coupled; suggest merging into one node or creating a working group" |
| A node is depended on by many others | "This is a critical infrastructure node; ensure contract stability" |
| CrossWork participants have no interactions | "This task force might be splittable" |
| Memory expired and unverified | "The following knowledge may be outdated; suggest confirming" |
| Contradictory knowledge on the same node | "The following memories may conflict; suggest verification" |

Other governance mappings:

| Software Practice | Organizational Mapping | Graph Manifestation |
|------------------|----------------------|---------------------|
| Architecture review | Technical review meeting | GovernanceEngine.Validate() |
| Health check | Team retrospective / performance review | FreshnessChecker |
| Circuit breaker | Isolate problematic group to prevent cascading failures | Node blocked status |
| Version management | Process/standards forward-compatible iteration | MemoryEntry.Version + EvolutionChain |
| Observability | Project weekly reports, knowledge accumulation | The memory system itself |

---

## 7. Project DNA Three-Engine Architecture

Split the current `KnowledgeGraph` god class into three orthogonal subsystems:

The system ultimately consists of **two independent projects**: the backend (pure knowledge API) and the frontend (Dashboard).

```
┌─────────────────────────────────────────────────────────┐
│           DNA Server (Backend, Pure Knowledge Service)    │
│           No file system access, deployable anywhere      │
│                                                          │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────┐  │
│  │ GraphEngine  │  │ MemoryEngine │  │  Governance    │  │
│  │              │  │              │  │    Engine      │  │
│  │  Node CRUD   │  │  Memory R/W  │  │  Governance    │  │
│  │  Edge CRUD   │  │  Semantic    │  │  analysis      │  │
│  │  Topology    │  │  search      │  │  Freshness     │  │
│  │  computation │  │  Vector      │  │  Conflict      │  │
│  │  Execution   │  │  index       │  │  detection     │  │
│  │  plans       │  │  Import/     │  │  Drift         │  │
│  │              │  │  Export      │  │  detection     │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬─────────┘  │
│         └─────────────────┴─────────────────┘            │
│                      StorageLayer                         │
│           SQLite only (Graph + Memory are DB-first)       │
│                ~/.dna/projects/{name}/                    │
│                                                          │
│  External interfaces: MCP (stdio/SSE) + REST API          │
└─────────────────────────────────────────────────────────┘
          ▲ REST / MCP            ▲ REST
          │                       │
┌─────────┴────────┐    ┌────────┴──────────────────────────┐
│  Agent Client    │    │   Dashboard (Frontend, Separate    │
│  (Cursor/Codex)  │    │   Project)                         │
│                  │    │                                    │
│  Reads/writes    │    │  ┌──────────────────────────────┐  │
│  project files   │    │  │ Knowledge Base Visualization  │  │
│  Reads/writes    │    │  │  Topology / Memory list /     │  │
│  DNA knowledge   │    │  │  Governance                   │  │
│  via MCP         │    │  └──────────────────────────────┘  │
└──────────────────┘    │  ┌──────────────────────────────┐  │
                        │  │ Project Management (Optional)  │  │
                        │  │  Specify project path          │  │
                        │  │  File tree browsing            │  │
                        │  │  Scan project → write to DNA   │  │
                        │  └──────────────────────────────┘  │
                        └────────────────────────────────────┘
```

**Core Principle**: DNA Server is a pure knowledge service that does not access the project file system. All knowledge is written through APIs.
Dashboard is an optional management frontend that can connect to a local project to assist with scanning and graph building.

### 6.1 GraphEngine

Full lifecycle management of the graph. Nodes and edges are first-class citizens, with support for incremental topology updates.

**Responsibilities**:
- Node CRUD (add / get / update / remove / list)
- Edge CRUD (add / remove / query)
- Topology snapshots (full / incremental)
- Execution plans (topological sort)
- Context building (visibility filtering + direct node Knowledge retrieval)

**Storage**:
- `graph.db` (SQLite) — nodes, edges, and materialized `NodeKnowledge` (replaces `architecture.json` + `modules.json`)

### 6.2 MemoryEngine

Storage and retrieval of knowledge entries. Purely manages MemoryEntry, no longer mixed with manifest management.

**Responsibilities**:
- Memory write (remember / batch_remember / update / delete)
- Semantic retrieval (recall — four-channel retrieval + fusion ranking + constraint chain expansion)
- Structured query (query — filter by coordinates)
- Index maintenance (rebuild / sync / export)

**Post-write callback**: After successful write, notify GraphEngine to refresh the target node's Knowledge summary.

**Storage**:
- `memory/index.db` (SQLite) — short-term memories and retrieval indexes (FTS/vector metadata)
- `memory/entries/*.json` is no longer used in the DB-first model

### 6.3 GovernanceEngine

Architecture advisor — combines data from GraphEngine + MemoryEngine to provide refactoring suggestions rather than violation reports.

**Responsibilities**:
- Cycle detection (Tarjan SCC) → suggest merging or forming a working group
- Critical node identification (nodes depended on by many others) → flag contract stability
- CrossWork health (whether participants have actual interactions) → suggest splitting or dissolving
- Freshness checks + expired memory decay/archiving
- Conflict detection (contradictory knowledge on the same node)
- Module knowledge condensation: aggregate short-term memories by `NodeId`, distill and upsert long-term `NodeKnowledge`
- Scheduled governance: run full condensation periodically with configurable schedule (API / Dashboard)

---

## 8. MCP Tool Surface

### 7.1 GraphTools

| Tool | Description |
|------|-------------|
| `get_context` | Get module context (constraints, dependencies, CrossWork, lessons) |
| `add_node` | Add a node (any type) |
| `get_node` | Get a node (with embedded knowledge) |
| `update_node` | Update node attributes |
| `remove_node` | Delete a node |
| `list_nodes` | List nodes (filter by type/department) |
| `add_edge` | Add a unidirectional dependency |
| `remove_edge` | Remove a dependency |
| `get_topology` | Get the complete topology snapshot |
| `get_execution_plan` | Get topologically sorted execution plan |
| `search_modules` | Search for nodes |

### 7.2 MemoryTools

| Tool | Description |
|------|-------------|
| `remember` | Write a memory (assigned to a specified NodeId) |
| `recall` | Semantic retrieval |
| `batch_remember` | Batch write |
| `update_memory` | Update a memory |
| `delete_memory` | Delete a memory |
| `query_memories` | Structured query |
| `get_memory` | Get by ID |
| `verify_memory` | Verify a memory is still valid |
| `rebuild_index` | Rebuild index |
| `export_to_json` | Compatibility placeholder (no longer a primary flow in DB-first mode) |

### 7.3 GovernanceTools

| Tool | Description |
|------|-------------|
| `validate_architecture` | Architecture compliance check |
| `get_memory_stats` | Knowledge base statistics |
| `check_freshness` | Freshness check |
| `condense_module_knowledge` | Condense one module's knowledge (short-term → long-term) |
| `condense_all_module_knowledge` | Condense all modules' knowledge |

---

## 9. Responsibility Division of the Two Projects

### 9.1 DNA Server (Backend)

A pure knowledge service that does not access the project file system. Can be deployed locally, on an intranet server, or in the cloud.

**External Interfaces**:
- MCP (stdio / SSE) — for Agent Client (Cursor / Codex / Claude Code) integration
- REST API — for Dashboard and other tool calls

**Responsibility Boundaries**:
- Knowledge CRUD (nodes, edges, memories, CrossWork)
- Topology computation and queries
- Governance analysis (cycle detection, freshness, conflict detection)
- Knowledge storage management (`~/.dna/projects/{name}/`)

**Out of Scope**:
- Does not read/write project engineering files
- Does not scan source code
- Does not infer code dependencies

### 9.2 Dashboard (Frontend, Separate Project)

Web frontend + lightweight API, connecting to DNA Server to display and manage knowledge.

**Responsibilities**:
- Knowledge base visualization (topology graph, memory list, governance reports)
- Knowledge editing (via DNA Server REST API)
- Project management (optional, requires specifying local project path):
  - File tree browsing
  - Project scanning → generate nodes and dependencies → write to DNA Server
  - Scanner plugin runtime environment

### 9.3 Agent Client (AI Agent in the IDE)

Connects to DNA Server via MCP protocol.

- Conversation start → `get_context("moduleName")` (if unclear, use `search_modules` first)
- Before modifying code → get constraints from node Knowledge
- After completion → `remember` to write back knowledge
- Agent directly reads/writes project files (workspace), reads/writes knowledge via MCP (DNA Server)

---

## 10. Refactoring Roadmap

### Phase 1: Unified Node Model + Split God Class (Without Splitting the Process)

1. Unify `GraphNode` / `CrossWork` / `Department` → `KnowledgeNode`
2. Remove the `Layer` field; hierarchy is implicitly determined by tree depth
3. Remove the `EdgeKind` enum (the old five violation types); an edge is just an edge
4. Extract manifest management from `MemoryStore.Facade` → `GraphStore`
5. Create `GraphEngine : IGraphEngine`
6. Create `MemoryEngine : IMemoryEngine`
7. Create `GovernanceEngine : IGovernanceEngine` (advisor mode, not police mode)
8. `MemoryEntry.ModuleIds` → `MemoryEntry.NodeId`
9. Refactor existing MCP tool classes to inject the three engine interfaces
10. Maintain monolithic runtime; all functionality unchanged

### Phase 2: Edge as First-Class Citizen + Node-Embedded Knowledge

1. Independent Edge CRUD (no longer indirectly through modifying Dependencies)
2. Cycle detection becomes refactoring suggestion (Tarjan SCC → suggest merge/form working group)
3. Node `Knowledge` materialized view implementation
4. Auto-refresh node Knowledge after memory writes
5. Simplify `get_context` / `get_module_context` to direct node reads

### Phase 3: Server Purification (Remove File System Dependencies)

1. `ProjectRoot` degrades to an identifier on the Server side — not used for reading files
2. Move `IProjectAdapter`, `ProjectScanner`, `ProjectTreeCache` to the Dashboard project
3. Move `FileTreeEndpoints` to Dashboard
4. Simplify `FreshnessChecker` to pure time-based checks (no file change detection)
5. Server can work fully without a project path

### Phase 4: Dashboard as Separate Project

1. Create a new Dashboard frontend project (separate repo or monorepo subdirectory)
2. Knowledge base visualization (topology graph, memory list, governance reports)
3. Project management features (file tree browsing, project scanning)
4. Scanner plugin runtime environment

### Phase 5: Incremental Topology + Performance Optimization

1. Node changes trigger incremental topology updates
2. Dirty checks + lazy recomputation
3. Batch operation optimization

---

## 11. One-Sentence Summary

> **Simplicity is the ultimate sophistication. Unity in all things.**
>
> The graph has only **one node type** (KnowledgeNode), and every node carries its own knowledge.
> There are only **three relationships** between nodes: containment (tree), dependency (DAG), collaboration (CrossWork) — fully orthogonal.
> Hierarchy = tree depth, no declaration needed; dependencies freely cross organizational boundaries, with the sole prohibition being cycles;
> A cycle is not a violation but a refactoring signal — cyclically dependent nodes are fundamentally a single cohesive unit.
> All organizational forms — modules, task forces, departments, studios — are recursions of the same pattern at different scales.
>
> If two people always need to sit together to get work done, they should share the same workstation.
> If two teams frequently need to collaborate, form a task force.
> If you find yourself drawing circular dependency diagrams, the organizational boundaries are drawn wrong.
