# Domain

Aggregates owned by the Manufacturing module. Every company-owned row is a `CompanyOwnedEntity`
carrying `company_id`, scoped automatically by `ModuleDbContext`'s global query filter (ADR-003);
cross-module relationships are plain application-level ids, never EF navigations or FKs into
another module's schema (ADR-001/002).
