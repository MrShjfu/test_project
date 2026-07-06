<!-- Source: https://ntg-sailmaking.atlassian.net/wiki/spaces/NTGHELM/pages/2458035/ADR-008+Backend-for-Frontend+BFF+per+Frontend+Not+an+API+Gateway (v4, exported 2026-07-06) -->

# ADR-008: Backend-for-Frontend (BFF) per Frontend, Not an API Gateway

Status: Ready for ReviewBlue

## Document Details

| **Detail** | **Value** |
| --- | --- |
| Proposed by | Vu Lam · |
| Contributors | Vu Lam |
| Approved by | — · pendingYellow |
| Links | Architecture Overview §1, ADR-001, ADR-003 |

---

## Context

Helm serves three distinct frontends, each with materially different needs:

- **Internal Platform** — data-rich B2B app for staff (Sales, Designers, Finance), spanning all ~10 domain modules.
- **Customer Portal** — customer-facing, B2C identity, a small read-mostly data surface (order tracking, documents, boat lifecycle).
- **Factory Kiosk** — a PWA on factory tablets that must work offline / on low bandwidth, needing small, denormalized payloads.

A single screen almost always spans several backend modules (e.g. a production dashboard reads CRM + Design + Inventory + Manufacturing). Something must compose across modules and shape responses per client. The question is **what** — a generic API gateway, or a thin composition layer per frontend.

This is purely about request/response composition for *our own* frontends. It is distinct from external API management (rate limiting, API keys for partners like Shopify).

## Decision

Each frontend gets its own **Backend-for-Frontend (BFF)** — a thin .NET layer that authenticates its audience, composes calls across the modular-monolith modules, and shapes payloads for that specific client. Three frontends → three BFFs. The BFFs are hosted within the same deployment as the monolith (`apps/api`) during the monolith phase.

We do **not** introduce an API gateway. If external API management is later needed for third-party consumers (e.g. Shopify order intake), **Azure API Management** is added in front for *external* traffic only — a separate concern from the BFFs.

## Rationale

In a monolith there is nothing for a gateway to route *between* — a gateway would add a network hop with no composition value. A BFF earns its place by tailoring each client: the Kiosk gets slim offline-friendly payloads; the Portal gets a customer-safe, B2C-authenticated slice; the Internal app gets rich aggregates. Per-audience auth (Entra B2B for staff, Entra External ID/B2C for customers — see ADR-003) maps cleanly onto separate BFFs.

Frontends and BFFs scale by **audience**, not by domain module. Domain modules remain the backend’s slicing axis (ADR-001); they appear on the frontend as folders/libs inside each app, never as their own apps or BFFs.

## Consequences

**Good:**

- Each frontend gets exactly the payload shape it needs — critical for the offline Kiosk.
- Per-audience authentication isolation (B2B vs B2C).
- No premature gateway/service-mesh infrastructure.
- Composition lives server-side, so frontends avoid chatty multi-call screens.

**Bad / watch out for:**

- Three BFFs mean some duplicated composition code — keep shared logic in module Contracts, not copied across BFFs.
- A BFF must not become a place where business rules leak out of modules — it composes and shapes only.
- When external API management is eventually needed, that is a new decision (APIM) — don’t retrofit the BFFs into a general gateway.

## Alternatives Considered

- **Single API gateway in front of the monolith** — rejected: nothing to route between in a monolith; adds a hop without composition value; forces generic, lowest-common-denominator payloads on all clients.
- **One shared BFF for all three frontends** — rejected: the Kiosk (offline, slim) and Internal (rich) needs conflict; mixing B2B and B2C auth in one layer is messy.
- **No BFF; frontends call module APIs directly** — rejected: pushes cross-module composition into the browser → many round-trips per screen, each frontend re-implementing aggregation.
- **Micro-frontend per module (frontend + BFF per module)** — rejected: module federation, shell app, and shared-auth plumbing buy independent frontend deploys a lean team does not need.
