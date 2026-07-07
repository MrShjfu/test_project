# Domain

Aggregates owned by the CRM module. `Customer` is a `CompanyOwnedEntity` — every row carries
`company_id` and is scoped automatically by `ModuleDbContext`'s global query filter (ADR-003).

This module has no cross-module entity references: cross-module relationships are represented as
plain application-level ids (e.g. a future `Customer.AssignedSalesRepId`), never as EF navigation
properties or foreign keys into another module's schema (ADR-001/002).
