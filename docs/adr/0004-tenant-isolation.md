# 0004. Tenant isolation in the application: scope filter + EF query filter + write guard

- Status: Accepted
- Date: 2026-10-29 (Phase 1 Step 9)

## Context

Every customer is an organization (tenant). Most future tables — watches, alert channels, deliveries, audit log — belong to exactly one organization. The most common real-world multi-tenant bug is IDOR: changing an id in a request and reading another tenant's data. A single forgotten `WHERE organization_id = …` must not leak data.

## Decision

Three layers, all in the application:

1. **`OrganizationScopeFilter`** (endpoint filter on organization-scoped routes): resolves the current organization from the optional `X-Organization-Id` header (default: the caller's first organization) and requires a **membership**; otherwise **403** before the endpoint runs. Sets the scoped `CurrentOrganizationContext` (organization + role).
2. **EF Core global query filter** on every `ITenantOwned` entity: `WHERE organization_id = <current>`. With no current organization it compares with `NULL` and returns **nothing** (fail closed). `IgnoreQueryFilters()` is reserved for deliberate admin/maintenance code.
3. **Write guard** in `SaveChanges`: adding/changing/deleting another organization's `ITenantOwned` row throws.

Roles (Owner/Admin/Member) gate actions on top of membership (e.g. only Owner/Admin can rename). Security-relevant actions are written to an append-only, tenant-owned `audit_entries` table.

## Alternatives considered

- **Manual `Where` clauses everywhere** — one omission = a data leak.
- **Database per tenant / schema per tenant** — strong isolation, but heavy operations and migrations for a small SaaS.
- **Postgres Row-Level Security only** — strong, but harder to reason about and test at this stage; kept as an optional fourth layer for Phase 4.

## Consequences

- New tenant-owned tables are protected by default just by implementing `ITenantOwned`.
- Tests prove: IDOR blocked, audit isolation, members read but can't rename, filter fails closed, write guard throws.
- `ExecuteUpdate/ExecuteDelete` respect the query filter but bypass `SaveChanges` (and the write guard) — bulk operations must run inside a correctly scoped context.
- Background jobs (Phase 3) have no HTTP request: they must set the current organization explicitly per unit of work.
