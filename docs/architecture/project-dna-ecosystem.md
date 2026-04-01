# Project DNA Extension System

> Status: Directional
> Last Updated: 2026-04-01
> Scope: extension surfaces that still make sense under the current Client-only architecture

## 1. Current Extension Principle

Project DNA is currently a **local desktop Client runtime**.

That means extension points should first be designed to plug into the local Client runtime, not into a remote server.

Good extension targets today:

- project templates
- file scanners
- governance rules
- knowledge extractors
- role interpreters
- local storage adapters
- IDE tooling helpers

## 2. Extension Dimensions

### 2.1 Templates

Purpose:

- create a starting module tree for a project type
- prefill constraints, boundaries, and managed paths

Typical examples:

- `unity-game`
- `react-app`
- `service-backend`
- `tools-pipeline`

### 2.2 Scanners

Purpose:

- analyze project files
- generate modules and relations
- infer path ownership and dependency hints

Typical examples:

- C# scanner
- Unity asmdef scanner
- TypeScript scanner
- Python scanner
- Dockerfile scanner

### 2.3 Governance Rules

Purpose:

- run architecture health checks
- flag constraint violations
- produce actionable suggestions for humans and agents

Typical examples:

- clean architecture rules
- Unity performance rules
- module boundary rules
- naming convention rules

### 2.4 Knowledge Extractors

Purpose:

- import knowledge from existing documents
- turn external text into structured memories or module metadata

Typical examples:

- Markdown extractor
- Swagger extractor
- changelog extractor
- commit-history extractor

### 2.5 Interpreters

Purpose:

- present the same local knowledge graph from different role perspectives

Typical perspectives:

- `coder`
- `designer`
- `art`
- `qa`
- `devops`

### 2.6 Local Storage Adapters

Purpose:

- control how the Client runtime persists project-scoped data

Current preferred direction:

- project-scoped local storage under `.project.dna/`

Possible adapters:

- SQLite-backed local store
- file-backed fallback store
- import/export bridge for Git-managed snapshots

### 2.7 IDE Tooling Helpers

Purpose:

- generate or install MCP configs
- install workspace-side helper files for Cursor / Codex
- surface tool catalogs to desktop UX and automation

## 3. Example Interface Shapes

These are directional examples, not yet a frozen plugin ABI.

### 3.1 Scanner

```csharp
public interface IDnaScanner
{
    string Name { get; }
    string[] FilePatterns { get; }
    bool CanScan(string projectRoot);
    ScanResult Scan(string projectRoot);
}
```

### 3.2 Governance Rule

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

### 3.3 Knowledge Extractor

```csharp
public interface IKnowledgeExtractor
{
    string Name { get; }
    string[] SupportedSources { get; }
    Task<List<ExtractedKnowledge>> ExtractAsync(ExtractionContext context);
}
```

## 4. Current Compatibility Rule

Any future extension system should preserve these assumptions:

- the active runtime is local to the desktop Client
- project-scoped data lives under `.project.dna/`
- IDE integration continues to route through local `:5052`
- extensions should not require a remote server to be useful

## 5. Near-Term Direction

The most realistic next extension work is:

1. template packs for common project structures
2. scanners for folder-to-module bootstrap
3. governance rule packs for architecture review
4. importers for markdown and existing project notes
5. better IDE tooling installers around MCP
