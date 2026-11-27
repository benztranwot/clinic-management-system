# Use EF Core for SQL Server persistence

**Status** Accepted

## Context

The application stores related clinic records in SQL Server and needs repeatable
schema updates for existing development databases. Workspace ownership and
appointment conflicts also require checks at the data layer.

## Decision

Use EF Core for SQL Server queries, entity mappings, workspace filters, and
schema migrations. Keep the context, migrations, and SQL-specific locks in
`HealthcareApp.Infrastructure`. Scheduling rules in `HealthcareApp.Application`
read availability through an interface implemented by Infrastructure.

## Consequences

The API stays the migration startup project and supplies the connection
configuration. Keep previously applied migration IDs stable. Existing SQL
fixtures remain under `backend/database`, and SQL Server integration tests cover
the behavior of provider transactions and workspace boundaries.
