# Project DNA Local Transport And Access Decision

> Status: Active
> Last Updated: 2026-04-01
> Scope: current supported Client-only runtime

## 1. Current Effective Decision

The active runtime decision is:

- **Local REST + MCP**

More specifically:

- desktop UI talks to the embedded local runtime through local HTTP APIs
- local CLI talks to the same embedded local runtime
- IDE agents talk to the same embedded local runtime through `/mcp`

There is no supported JWT, SSE, or remote team-auth path in the current documented product architecture.

## 2. Current Transport Topology

```text
Desktop UI -> local REST (:5052)
Local CLI  -> local REST (:5052)
IDE Agent  -> local MCP  (:5052/mcp)
```

Important boundary:

- all supported surfaces converge on the same local runtime
- that runtime lives inside the desktop Client process
- it is not a second standalone service

## 3. Local Interface Layers

The embedded runtime can be understood in these layers:

### 3.1 Runtime status layer

Examples:

- `/api/status`
- `/api/client/status`
- `/api/connection/access`

Purpose:

- expose runtime health
- expose current project identity
- expose local access profile
- support desktop overview and CLI status

### 3.2 Topology and knowledge layer

Examples:

- `/api/topology`
- `/api/memory/*`
- `/mcp` graph tools
- `/mcp` memory tools

Purpose:

- read and maintain local project knowledge
- support knowledge graph preview
- support memory query and condensation flows
- provide structured project cognition to IDE agents

### 3.3 Desktop host support layer

Examples:

- `/api/client/workspaces/*`
- `/api/client/tooling/*`
- `/api/client/mcp/tools`

Purpose:

- manage local workspace state
- expose IDE tooling installation helpers
- expose MCP catalog metadata to the desktop UI and automation

### 3.4 Local agent shell layer

Examples:

- `/agent/providers`
- `/agent/sessions/*`
- `/agent/chat`

Purpose:

- support the lightweight local agent-shell flow
- keep future orchestration work inside the same local runtime boundary

## 4. Current Access Boundary

The current access model is intentionally local and simple:

- the local desktop process is the trust boundary
- `/api/connection/access` reports the local runtime as allowed
- the current local runtime resolves itself as `admin`
- there is no active multi-user authorization model in the documented runtime

This is acceptable because the current supported product shape is:

- single machine
- single desktop Client
- project-scoped local runtime

## 5. Why This Is The Current Choice

This narrowing is intentional:

- it preserves the single-client mental model
- it avoids reintroducing remote complexity before the local product loop is stable
- it keeps desktop UX, CLI, and MCP consistent
- it lets topology, memory, and agent features converge on one runtime surface

## 6. Non-Goals For The Current Docs

The following are explicitly outside the current documented runtime:

- JWT-based role propagation
- SSE-based multi-user streaming updates
- remote shared server authority
- allowlist-driven team access management
- browser-first admin console

Legacy code for some of these ideas may still exist temporarily in the repository, but it is not part of the active documented architecture.

## 7. Review Checklist

When changing transport or access behavior, verify:

1. Does the change still keep desktop UI, CLI, and MCP on the same local runtime?
2. Does the change avoid creating a second long-lived Client-side service?
3. Does the change keep the runtime local to the desktop process?
4. Does the change avoid silently reintroducing remote team-auth assumptions into the current product?
