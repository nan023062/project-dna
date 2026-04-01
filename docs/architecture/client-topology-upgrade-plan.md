# Client Topology Upgrade Plan

> Status: In Progress
> Last Updated: 2026-04-01
> Scope: single-user local Client topology graph under the current C# desktop architecture

## 0. Current Implementation Snapshot

The current branch has already completed the first structural extraction pass.

Implemented now:

- `TopologyScene` for hierarchy reconstruction, scope trail resolution and visible-graph collapsing
- `TopologyViewState` for selection, hover, filter and viewport state
- `TopologyLayoutService` with `ScopedTopologyLayoutEngine` and `LayeredTopologyLayoutEngine`
- `TopologySpatialIndex` and `TopologyHitTester` for interaction lookup
- `TopologyRenderListBuilder` for scene-to-render flattening
- `TopologyRenderListCache` for separating structure rebuilds from interaction-only redraws
- `AvaloniaTopologyRenderer` plus `TopologyTheme` as the active renderer backend seam
- `TopologyFormattedTextCache`, `TopologyEdgeRouteCache` and `TopologyViewportCuller` for first-pass render optimisation seams
- `TopologyVisualDetailPolicy` for zoom-based level-of-detail rendering
- a thinner [TopologyGraphControl.cs](../../src/Client/Desktop/TopologyGraphControl.cs) that now coordinates scene, layout, interaction and renderer instead of owning all details directly

Not implemented yet:

- `SkiaSharp` backend
- advanced large-graph level-of-detail
- async or incremental layout

## 1. Decision

For the current product stage, the topology graph should continue on the **C# desktop Client** path and be upgraded around a **high-performance custom graph renderer**.

We should **not** switch the Client workbench to Electron just to improve graph performance.

Chosen direction:

- keep the current C# desktop host and local runtime
- keep the current single-process / single-window / single-lifecycle model
- evolve the topology graph from a monolithic control into layered graph subsystems
- introduce `SkiaSharp` only when the current Avalonia drawing path becomes the actual bottleneck

This is the most compatible direction with the current branch goal:

- local-only Client
- local knowledge graph / memory / MCP / CLI
- one runtime surface on `:5052`
- one project-scoped `.project.dna/`

## 2. Why Not Electron First

Electron is stronger at:

- IDE-like panel ecosystems
- web UI component richness
- rapid assembly of complex workbench layouts

But the current graph problem is not a "web workbench shortage" problem. It is a "graph architecture and rendering pipeline" problem.

Right now the repo already has the important C#-side assets:

- local runtime host in [EmbeddedClientHost.cs](../../src/Client/Desktop/EmbeddedClientHost.cs)
- local graph / memory / MCP composition in [ClientHostComposition.cs](../../src/Client/Desktop/ClientHostComposition.cs)
- project-scoped state in [DesktopProjectConfig.cs](../../src/Client/Desktop/DesktopProjectConfig.cs)
- custom topology control in [TopologyGraphControl.cs](../../src/Client/Desktop/TopologyGraphControl.cs)

Changing to Electron would replace the host and UI stack, but would not remove the need to solve:

- graph layout
- graph interaction
- graph hit testing
- graph virtualisation
- graph rendering invalidation
- parent-child scoped navigation

So the better sequence is:

1. fix the graph architecture in C#
2. prove the graph and local runtime are stable
3. only reconsider Electron if the whole desktop workbench later needs VS Code-class shell features

## 3. Current Control Assessment

The current [TopologyGraphControl.cs](../../src/Client/Desktop/TopologyGraphControl.cs) is already a solid first-generation custom graph control, but it is doing too many jobs inside one file.

Today it mixes:

- graph scene data
- hierarchy reconstruction
- scoped node filtering
- layout calculation
- viewport transform
- hit testing
- edge routing
- node rendering
- interaction state
- selection / hover / panning / zooming

Examples in the current file:

- rendering entry: `Render(...)`
- edge rendering: `DrawEdges(...)`
- node rendering: `DrawNodes(...)`
- scoped and layered layout: `RebuildLayout(...)`, `TryLayoutScopedParentCenter(...)`
- hit testing: `HitTestNode(...)`
- input handling: `OnPointerPressed(...)`, `OnPointerMoved(...)`, `OnPointerWheelChanged(...)`

This is fine for an MVP, but it creates three long-term problems:

1. performance tuning becomes risky because data, render and interaction are coupled
2. introducing `SkiaSharp` later becomes harder because drawing logic is not isolated
3. advanced features like large-graph virtualisation and incremental layout have no clean insertion point

## 4. Upgrade Goal

The topology graph should become a dedicated graph subsystem with these properties:

- supports medium and large module graphs without obvious frame drops
- keeps Unity-style parent-child scoped navigation
- preserves non-parent relations such as dependency, aggregation, composition, collaboration
- supports future manual editing overlays without turning the renderer into form code
- can later switch between Avalonia drawing and `SkiaSharp` drawing without rewriting graph semantics

## 5. Target Architecture

The graph layer should be split into six sublayers.

```text
Topology API data
  ->
Topology Scene Model
  ->
Topology Layout Engine
  ->
Topology View State
  ->
Topology Render List
  ->
Renderer Backend (Avalonia now, SkiaSharp optional later)
```

### 5.1 Scene Model

Responsibility:

- immutable in-memory graph snapshot
- node / edge dictionaries
- hierarchy index
- relation index
- scoped navigation helpers

Suggested types:

- `TopologyScene`
- `TopologySceneNode`
- `TopologySceneEdge`
- `TopologyHierarchyIndex`
- `TopologyRelationIndex`

This layer should know:

- parent-child relationships
- visible scope roots
- child counts
- discipline / type / summary / metadata

This layer should not know:

- pixel positions
- zoom
- drag state
- paint colors

### 5.2 Layout Engine

Responsibility:

- convert scene model into graph-space positions
- support two layout modes:
  - global layered layout
  - scoped parent-center layout

Suggested types:

- `ITopologyLayoutEngine`
- `LayeredTopologyLayoutEngine`
- `ScopedTopologyLayoutEngine`
- `TopologyLayoutResult`

Input:

- visible scene snapshot
- layout mode
- layout options

Output:

- graph-space node bounds or centers
- routed edge anchor hints

This engine should be deterministic and testable without UI.

### 5.3 View State

Responsibility:

- selection
- hover
- zoom
- pan offset
- current scope root
- active relation filters

Suggested types:

- `TopologyViewState`
- `TopologyViewportState`
- `TopologyFilterState`

This isolates "what the user is looking at" from "what the graph is".

### 5.4 Render List

Responsibility:

- flatten scene + layout + view state into renderable primitives

Suggested primitive families:

- `NodeCardPrimitive`
- `NodeBadgePrimitive`
- `TextPrimitive`
- `EdgePolylinePrimitive`
- `ArrowPrimitive`
- `DiamondPrimitive`
- `BackgroundDotsPrimitive`

This is the key seam that makes later `SkiaSharp` adoption cheap.

The render list should already encode:

- z-order
- alpha
- colors
- line thickness
- text content
- corner radius
- relation style

### 5.5 Renderer Backend

Responsibility:

- consume render primitives and draw them

Stage 1 backend:

- Avalonia `DrawingContext`

Stage 2 backend:

- `SkiaSharp` renderer over `SKCanvas`

By keeping render primitives backend-neutral, we avoid rewriting:

- hierarchy logic
- layout logic
- node semantics
- relation semantics

### 5.6 Interaction Layer

Responsibility:

- pointer handling
- drag / pan / zoom state transitions
- node hit testing
- double-click navigation

Suggested types:

- `TopologyInteractionController`
- `TopologyHitTester`
- `TopologySpatialIndex`

This layer should not directly draw.

## 6. Recommended File Split

The current [TopologyGraphControl.cs](../../src/Client/Desktop/TopologyGraphControl.cs) should eventually be reduced to a thin view shell.

Suggested target structure:

```text
src/Client/Desktop/Topology/
  TopologyGraphControl.cs
  TopologyViewState.cs
  TopologyScene.cs
  TopologySceneBuilder.cs
  TopologyLayoutOptions.cs
  TopologyLayoutResult.cs
  ITopologyLayoutEngine.cs
  LayeredTopologyLayoutEngine.cs
  ScopedTopologyLayoutEngine.cs
  TopologyRenderListBuilder.cs
  TopologyRenderPrimitives.cs
  TopologyInteractionController.cs
  TopologyHitTester.cs
  TopologySpatialIndex.cs
  TopologyTheme.cs
  AvaloniaTopologyRenderer.cs
  SkiaTopologyRenderer.cs
```

## 7. SkiaSharp Strategy

`SkiaSharp` should be introduced as a renderer backend, not as a full architecture reset.

### 7.1 Stage 0

Do not add `SkiaSharp` yet.

First extract:

- scene model
- layout engine
- render list
- interaction logic

If this split alone already fixes maintainability and enough performance, we may not need deeper rendering changes immediately.

### 7.2 Stage 1

Keep Avalonia as the active backend.

Use the render list to draw through `DrawingContext`, but stop mixing business logic with drawing logic.

This stage should already deliver:

- cleaner code
- easier testing
- lower regression risk

### 7.3 Stage 2

Add optional `SkiaSharp` backend for the graph area only.

Good candidates for `SkiaSharp` payoff:

- thousands of visible edges
- heavy text and badge counts
- large zoom / pan invalidation areas
- future minimap / overview rendering

Important:

- `SkiaSharp` should render the graph canvas
- normal forms, panels and window chrome should remain standard Avalonia controls

This keeps the graph fast without turning the whole app into a bespoke graphics engine.

## 8. Performance Plan

### 8.1 Immediate Improvements

These should be done before `SkiaSharp`:

- cache formatted text per node and zoom bucket
- cache edge routes until topology or layout changes
- separate scene rebuild from viewport redraw
- avoid rebuilding visible graph when only hover changes
- avoid full layout recomputation when only selection changes

### 8.2 Medium Graph Optimisations

- build a spatial index for node hit testing
- cull off-screen nodes and edges
- cull labels when zoom is below threshold
- collapse secondary relation detail when zoomed out
- use level-of-detail rendering for badges and footer text

### 8.3 Large Graph Optimisations

- incremental layout instead of full recompute
- async layout preparation with UI-thread swap-in
- dirty-region redraw strategy
- minimap rendered from simplified scene
- relation bundling or lane routing for dense modules

## 9. UX Rules for the Graph

The renderer must continue to follow the current product semantics:

- parent-child hierarchy is the primary navigation structure
- double-click enters the current module scope
- the scoped parent, if any, stays pinned as the center node
- double-clicking the scoped parent returns upward
- non-parent relations remain visible:
  - dependency
  - composition
  - aggregation
  - collaboration

Visual style target:

- Unity-like rounded cards
- Figma-like clean spacing
- orthogonal lines with rounded corners
- near-clean background with very light dots
- no accidental line intersections where avoidable

## 10. Testing Strategy

We should add tests at three levels.

### 10.1 Pure Model Tests

- hierarchy resolution
- visible node computation under scope changes
- relation collapsing rules
- parent center selection rules

### 10.2 Layout Tests

- scoped layout keeps parent pinned
- layered layout remains stable for equal inputs
- sibling ordering is deterministic

### 10.3 Render/Interaction Tests

- hit testing maps to correct node
- zoom and pan preserve graph-space mapping
- selection and hover do not force full scene rebuild

## 11. Delivery Phases

### Phase 1: Structural Extraction

Goal:

- split current monolithic control into scene, layout and view state

Current status:

- completed on the current branch

Deliverables:

- `TopologyScene`
- `TopologyViewState`
- `ITopologyLayoutEngine`
- thinner `TopologyGraphControl`

### Phase 2: Render List and Caching

Goal:

- isolate rendering primitives and add cache seams

Current status:

- partially completed
- render-list seam and `AvaloniaTopologyRenderer` are in place
- first-pass text and edge-route caches are in place
- zoom-based level-of-detail rendering is in place
- deeper cache invalidation and reuse strategy is still pending

Deliverables:

- render list primitives
- `AvaloniaTopologyRenderer`
- text/route caching

### Phase 3: Interaction and Spatial Index

Goal:

- make interaction cheaper and more scalable

Current status:

- partially completed
- spatial index, hit testing and viewport culling are extracted
- redraw flow now distinguishes structure rebuilds from interaction-only redraws
- deeper dirty-region redraw optimisation is still pending

Deliverables:

- dedicated hit tester
- spatial index
- reduced redraw churn

### Phase 4: Optional SkiaSharp Backend

Goal:

- improve graph canvas throughput only if needed

Current status:

- not started

Deliverables:

- `SkiaTopologyRenderer`
- backend switch behind an option
- performance comparison report

## 12. Acceptance Criteria

This upgrade is successful when:

- the graph code is no longer dominated by one monolithic control file
- layout logic can be tested without UI
- renderer backend can be swapped without rewriting scene semantics
- scoped hierarchy navigation remains unchanged to the user
- medium and large graphs stay smooth enough for daily work
- future manual editing overlays do not require touching low-level edge drawing code

## 13. Recommended Next Step

The next implementation step should be:

1. extract `TopologyViewState`
2. extract `TopologyScene` + hierarchy helpers
3. move `RebuildLayout(...)` and `TryLayoutScopedParentCenter(...)` into dedicated layout engine classes
4. keep the current Avalonia drawing path for now

Do not start with `SkiaSharp` first.

The highest-value first move is architectural decoupling, not renderer replacement.
