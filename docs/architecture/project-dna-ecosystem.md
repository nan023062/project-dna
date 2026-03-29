# Project DNA Extension System

> **Author**: nave
> **Prerequisite**: Read [project-dna-design.md](./project-dna-design.md) for the core model
---

## Extension Dimensions

Project DNA is designed as an extensible platform. Community members can contribute extensions in six dimensions:

```
┌──────────────────────────────────────────────────────────┐
│                     DNA Registry                          │
│                                                          │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌───────────┐  │
│  │ Templates │ │ Scanners │ │ Gov Rules │ │Extractors │  │
│  │          │ │          │ │           │ │           │  │
│  │unity-game│ │csharp    │ │clean-arch │ │confluence │  │
│  │react-app │ │typescript│ │unity-perf │ │jira       │  │
│  │spring    │ │swagger   │ │game-team  │ │markdown   │  │
│  └──────────┘ └──────────┘ └───────────┘ └───────────┘  │
│                                                          │
│  ┌──────────────┐ ┌──────────────────┐                   │
│  │ Interpreters │ │ Storage Backends │                   │
│  │ qa / devops  │ │ postgres / s3    │                   │
│  └──────────────┘ └──────────────────┘                   │
└──────────────────────────────────────────────────────────┘
```

---

## 1. DNA Templates

Pre-built DNA structures for specific project types.

**Format**: A JSON file describing the default node tree, dependencies, and knowledge.

```json
{
  "name": "unity-game",
  "description": "Unity game project standard DNA template",
  "version": "1.0.0",
  "nodes": [
    {
      "name": "Root",
      "type": "Root",
      "knowledge": {
        "identity": "Unity game project",
        "constraints": ["Unity 2022+ LTS", "Target: iOS/Android"]
      }
    },
    {
      "name": "Engineering",
      "type": "Department",
      "parent": "Root"
    },
    {
      "name": "Framework",
      "type": "Module",
      "parent": "Engineering",
      "pathPattern": "Assets/Scripts/Framework/**",
      "knowledge": {
        "constraints": ["No heap allocation in Update", "Use PoolManager for object pooling"],
        "contract": "IFrameworkService: Init/Tick/Dispose lifecycle"
      }
    }
  ]
}
```

**Usage**:

```bash
dna init --template=unity-game
```

---

## 2. Scanners

Auto-analyze project files to generate DNA nodes and edges.

**Interface**:

```csharp
public interface IDnaScanner
{
    string Name { get; }
    string[] FilePatterns { get; }
    bool CanScan(string projectRoot);
    ScanResult Scan(string projectRoot);
}

public class ScanResult
{
    public List<ScannedNode> Nodes { get; set; } = [];
    public List<ScannedEdge> Edges { get; set; } = [];
    public List<ScannedKnowledge> Knowledge { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
```

**Examples**: CSharpScanner, UnityAsmdefScanner, TypeScriptScanner, PythonScanner, SwaggerScanner, DockerfileScanner.

---

## 3. Governance Rules

Pluggable architecture checks.

**Interface**:

```csharp
public interface IGovernanceRule
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    RuleSeverity Severity { get; }
    GovernanceSuggestion? Check(KnowledgeNode node, TopologySnapshot topology);
}
```

**Examples**: `unity-performance-rules`, `clean-architecture-rules`, `react-architecture-rules`, `microservice-rules`.

---

## 4. Knowledge Extractors

Import knowledge from existing documents and external systems.

**Interface**:

```csharp
public interface IKnowledgeExtractor
{
    string Name { get; }
    string[] SupportedSources { get; }
    Task<List<ExtractedKnowledge>> ExtractAsync(ExtractionContext context);
}
```

**Examples**: MarkdownExtractor, ConfluenceExtractor, JiraExtractor, SwaggerExtractor, GitHistoryExtractor.

---

## 5. Context Interpreters

Same DNA, different perspectives per role.

| Interpreter | Perspective |
|-------------|------------|
| `coder` | Code constraints, API contracts, GC rules |
| `designer` | Formulas, config fields, gameplay logic |
| `art` | Polygon budget, texture specs, naming conventions |
| `qa` | Test cases, regression checklists, known issues |
| `devops` | Deploy dependencies, environment requirements |

---

## 6. Storage Backends

| Backend | Use Case |
|---------|----------|
| `SqliteBackend` (default) | Single machine, small teams |
| `PostgresBackend` | Large teams, enterprise |
| `GitNativeBackend` | Offline, file-based fallback |
| `S3Backend` | Cloud-native |
