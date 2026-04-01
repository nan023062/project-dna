# Project DNA Transport And Access Decision

> Status: Active for current MVP
> Last Updated: 2026-04-01

## 1. Current Effective Decision

The original team-scale design explored `REST + JWT + SSE`.

For the **current MVP**, the effective runtime decision is narrower:

- main business access uses `REST`
- `Client` exposes local `MCP` only for IDE / agent access
- Server-side access control currently centers on **allowlist + role resolution**
- team-scale `JWT + review + SSE` remains a planned evolution, not the main runtime path today

## 2. Current Transport Topology

```text
Browser -> Server REST / dashboard
Desktop Client -> Server REST
IDE Agent -> Client MCP (:5052)
```

Important boundary:

- IDE agents attach to the desktop Client, not directly to the Server
- the Client `:5052` surface is embedded inside the desktop process
- the Client is not a second team-shared service

## 3. Server Interface Layers

Current Server interfaces can be understood in these layers:

### 3.1 Service / status layer

Examples:

- `/api/status`
- `/api/connection/access`

Purpose:

- expose current runtime state
- show current permission profile
- support dashboard and desktop connection checks

### 3.2 Formal knowledge read layer

Examples:

- `/api/topology`
- `/api/graph/*`
- `/api/memory/query`
- `/api/memory/{id}`
- `/api/memory/recall`
- `/api/memory/stats`

Purpose:

- read formal knowledge
- power topology preview and knowledge preview

### 3.3 Admin / governance layer

Examples:

- allowlist management
- review queue pages and APIs
- direct formal knowledge maintenance

Purpose:

- keep operational and administrative responsibilities on the Server side

## 4. Current Authorization Boundary

Current effective boundary:

- `Server` decides whether a caller is allowed
- `Server` resolves the caller role
- `Client` only displays the current permission state
- `Client` must not become the authority for access decisions

Current role interpretation in the MVP:

- `viewer`: browse formal knowledge and use local MCP access
- `editor`: reserved for future review submission flow
- `admin`: may directly maintain formal knowledge and admin pages

## 5. Why This Is The Current MVP Choice

This narrowing is intentional:

- it closes the single-user local admin loop first
- it keeps the runtime simple enough to verify quickly
- it avoids mixing unfinished team collaboration flows into the main path
- it preserves room to reintroduce `JWT + review + SSE` after the MVP is stable

## 6. Future Convergence Direction

After the local-admin MVP is stable, the intended convergence remains:

- `REST` for normal business operations
- `JWT` for team-scale identity and role propagation
- `SSE` for review queue refresh, agent streaming output, and long-running task progress

That future path must still preserve the current product boundary:

- `Server` is the authority for shared knowledge and admin operations
- `Client` remains the local desktop host and local MCP gateway
