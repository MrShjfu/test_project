<!-- Source: https://ntg-sailmaking.atlassian.net/wiki/spaces/NTGHELM/pages/2458051/ADR-010+Hosting+on+Azure+Container+Apps+Not+Kubernetes+AKS+Day+1 (v2, exported 2026-07-06) -->

# ADR-010: Hosting on Azure Container Apps (Not Kubernetes/AKS Day 1)

**Status**: Ready for Review
**Proposed by**: Vu Lam · 2026-06-21
**Contributors**: Vu Lam
**Approved by**: — · pending
**Links**: [Architecture Overview §10–§11](../architecture/overview.md), [ADR-001](ADR-001-modular-monolith.md), [ADR-011](ADR-011-scaling-and-multi-region.md)

---

## Context

The backend is a single containerized .NET modular monolith (ADR-001); the frontends are static React bundles. We need a hosting platform that supports autoscaling, blue-green/canary releases, and **per-PR ephemeral environments** (valuable for a stakeholder-heavy project — Anna, Andrew, Mike review features live), without imposing operational burden on a 2–3 engineer team.

The realistic options on Azure are: App Service, Azure Container Apps (ACA), or Azure Kubernetes Service (AKS). The SoW also calls for multiple environments (Local, Dev, [Sandbox], [Staging/UAT], Prod). An Azure subscription restructure is expected, so everything must be reproducible via IaC (Bicep).

## Decision

Host the backend API + BFFs on **Azure Container Apps**; host the three React frontends on **Azure Static Web Apps**. **Do not adopt Kubernetes/AKS for the initial architecture.**

Migrate to AKS **only if** a module is extracted to a separate service and genuinely needs fine-grained networking/scheduling — i.e. when the ADR-001/ADR-011 extraction triggers fire. The container images built for ACA run unchanged on AKS later, so this is not a one-way door.

## Rationale

ACA provides exactly the capabilities we need as managed features: container hosting, KEDA-based autoscale (including scale-to-zero for non-prod), revisions with traffic splitting for blue-green/canary, and cheap ephemeral per-PR apps/revisions. AKS would deliver the same only after the team builds and operates cluster networking, node upgrades, autoscaler config, and a progressive-delivery stack (e.g. Argo/Flagger) — months of ops work with no business value for a single monolith at NTG’s scale (tens–hundreds of users).

Static Web Apps gives the frontends global CDN delivery and **free per-PR preview environments** out of the box, matching the PR-environment strategy. We do not use its bundled serverless-API feature — the .NET backend on ACA owns the API.

## Consequences

**Good:**

- Autoscale, revisions/blue-green, and PR environments without cluster ops.
- Low operational surface for a small team; aligns with ADR-001’s “operational simplicity.”
- IaC-friendly (Bicep modules per environment); survives the subscription restructure.
- Not a lock-in: ACA images run on AKS later if extraction demands it.

**Bad / watch out for:**

- ACA is less configurable than raw Kubernetes (networking, sidecars) — acceptable for a monolith, revisit on extraction.
- Background jobs must be single-execution across autoscaled replicas — addressed by Hangfire (ADR-005) / a worker app split (ADR-011).
- Per-PR ephemeral backends need a throwaway database strategy (containerized or cheap Flexible Server DB) — define in the pipeline.

## Alternatives Considered

- **AKS / Kubernetes from day 1** — rejected: heavy ops burden (cluster upgrades, networking, progressive-delivery tooling) for no benefit at current scale; premature.
- **Azure App Service** — rejected: weaker container/revision story and less flexible autoscale than ACA; ACA is the modern container-first successor for this profile.
- **Static Web Apps’ built-in managed API for the backend** — rejected: insufficient for a full .NET modular monolith; we use SWA for static frontends only.
