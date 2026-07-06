<!-- Source: https://ntg-sailmaking.atlassian.net/wiki/spaces/NTGHELM/pages/2261407/ADR-009+Single+Monorepo+Orchestrated+by+Nx+nx-dotnet (v3, exported 2026-07-06) -->

# ADR-009: Single Monorepo Orchestrated by Nx + nx-dotnet

This document is Ready for reviewBlue.

## Document Details

| **Attribute** | **Value** |
| --- | --- |
| Proposed by | Vu Lam · |
| Contributors | Vu Lam |
| Approved by | — · pendingYellow |
| Links | Monorepo Tooling Design *(not exported — not in the NTGHELM Confluence space)*, [Architecture Overview §3](../architecture/overview.md), [ADR-001](ADR-001-modular-monolith.md) |

---

## Context

Helm is a greenfield codebase spanning three technology layers that must evolve together: the .NET modular-monolith backend, the React/TypeScript frontends, and Azure Bicep infrastructure. Decisions needed: do these live in one repository or several, and what orchestrates builds, tests, and dependency enforcement across languages?

A multi-repo split would force cross-repo coordination for changes that cross the FE/BE boundary (e.g. a contract change) and complicate atomic PRs. A monorepo needs an orchestrator that understands a cross-language dependency graph, or CI rebuilds everything on every change.

The detailed tooling design is already worked out and approved in Monorepo Tooling Design (2026-06-18) *(not exported — not in the NTGHELM Confluence space)*; this ADR records the decision of record.

## Decision

Use a **single monorepo** orchestrated by **Nx**, with the `@nx-dotnet/core` plugin providing C# project support. One workspace covers backend (`libs/api`, `apps/api`), frontends (`apps/web-*`, `libs/web/*`), and infra (`infra/`). The `.NET` solution file is retained for Visual Studio / `dotnet` CLI use — Nx sits additively on top.

Frontend/backend contracts are shared via a **TypeScript client generated from the backend OpenAPI spec** into `libs/web/api-client`, making backend contracts the single source of truth (a contract change surfaces as a TypeScript compile error).

## Rationale

Nx gives a unified dependency graph and `nx affected` so CI runs only what a change actually touches — essential to keep pipelines fast as the repo grows across ~10 modules and three apps. A monorepo lets a single PR change a backend contract and the consuming frontend together, reviewed atomically. `@nx-dotnet/core` brings .NET into that graph without giving up native .NET tooling.

This directly supports the modular-monolith boundaries (ADR-001): Nx module-boundary lint rules enforce that frontend feature libs and backend modules don’t cross-import, complementing the backend’s NetArchTest checks.

## Consequences

**Good:**

- One clone, one set of commands; atomic cross-layer PRs.
- `nx affected` keeps CI proportional to the change, not the repo size.
- Generated TS client eliminates hand-maintained DTOs and catches breaking changes at compile time.
- Boundary rules enforceable at lint time across both stacks.

**Bad / watch out for:**

- `@nx-dotnet/core` is a community plugin — a dependency to monitor for .NET 10 compatibility.
- Monorepo CI needs correct affected-detection config or it silently over- or under-builds.
- Contributors must learn Nx conventions on top of `dotnet`/`npm`.

## Alternatives Considered

- **Multi-repo (separate FE / BE / infra repos)** — rejected: cross-cutting changes need coordinated multi-repo PRs; no shared dependency graph; contract drift between FE and BE.
- **Monorepo with Nx (TypeScript only) + Taskfile for .NET** — rejected: loses the unified cross-language graph and affected detection (see Monorepo Tooling Design).
- **Monorepo with no orchestrator (raw** `dotnet`**/**`npm` **scripts)** — rejected: CI rebuilds everything on every change; no boundary enforcement.
