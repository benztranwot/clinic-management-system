# Workspace isolation

The application uses one SQL database. Every clinical record, application account, and clinic membership has a WorkspaceId. Existing records retain their IDs, relationships, and original workspace (the empty GUID). The migration adds workspace columns and replaces relationships with composite foreign keys that include workspace ownership; it does not reset or delete clinical records.

## Request selection and access rules

WorkspaceAccess resolves the exact issuer/subject from the validated token against AppAccounts. The matching account determines the workspace. Request headers, query parameters, and JSON fields cannot select a different workspace. Authorization handlers and WorkspaceSelectionMiddleware share this cached request lookup. The single unfiltered lookup remains constrained to that authenticated identity. Auth-check and anonymous health routes do not require a workspace lookup.

EF Core global query filters apply the selected workspace to every clinical table, including searches, counts, calendars, availability, and set-based updates/deletes. IsAdministrator grants roles inside the selected workspace; it does not bypass these filters. DbContext instances cannot switch workspaces. Tracked inserts receive the selected workspace automatically, and tracked changes cannot transfer ownership. Composite foreign keys reject relationships to records in another workspace.

The original global directory permissions remain available within the original dataset for existing operator accounts and explicitly authorized legacy tokens. An identity without an application account does not acquire visitor permissions or private records. Account creation and permissions remain a separate operation. Database operator tools can intentionally query all workspaces; normal API code must not disable filters for clinical queries.

Rolling back the isolation migration is blocked while private workspaces exist. Removing workspace ownership from populated private data would merge access boundaries, so an operator must resolve that data deliberately before rollback.

## Visitor enrollment and rollout

GET /api/me/onboarding only reports provider verification and enrollment availability. POST /api/me/workspace explicitly requests a private copy and rechecks the exact authenticated subject through Auth0 UserInfo. It accepts no client identity, email, role, or workspace selection. Existing linked accounts return ready without being moved or promoted.

The backend copies the original operator-managed synthetic demo records in one SQL transaction. It generates fresh internal and public IDs, remaps relationships, and never copies original Auth0 identity links or memberships. The visitor becomes owner of that private workspace and is linked to its first provider for the schedule demonstration. Completed visits retain their status. Appointment and time-off dates shift by whole weeks while preserving clinic-local times; ambiguous or invalid shifted times reject the copy. Original records remain unchanged. Use synthetic data only in the original demo dataset.

A transaction-owned database lock serializes enrollment across API instances. The identity is rechecked after the lock, making retries and simultaneous tabs idempotent. Failed copies roll back the workspace, account, membership, and clinical rows together. Template reads are capped at 10,000 records per table; larger or incomplete templates produce a safe unavailable response.

Compose enables enrollment by default. VISITOR_MAX_WORKSPACES defaults to 100 and VISITOR_MAX_DAILY_WORKSPACES to 20 (UTC day); these limits are checked under the shared lock. VISITOR_WORKSPACES_ENABLED=false stops new enrollment and private-owner capabilities without disabling data isolation. Outside Compose, set VisitorWorkspaces:Enabled explicitly; its default is false. Workspace expiry and operator cleanup are separate hosting tasks; these limits do not automatically delete records.

After tests pass, stop api and web, run the migration service, and rerun scripts/Initialize-DatabaseUser.ps1 using the existing application password. This grants SELECT/INSERT on Workspaces and EXECUTE on CreateVisitorWorkspaceAccount while retaining zero direct account/membership write permissions. Then rebuild and start api and web. Keep the existing database volume. The new migration only adds the restricted procedure and does not rewrite clinical records.

Private owners receive only the known clinical permission set from the backend, scoped to their account's workspace. UI management controls accept those server-reported capabilities as well as the existing Auth0 scopes. SQL ownership checks and all backend authorization remain authoritative.

## Validation

WorkspaceIsolationTests covers separate request workspaces, ignored client overrides, scoped searches and counts, automatic ownership on inserts, foreign public IDs, rejected writes and bookings, personal lists, calendars, availability, cross-workspace foreign keys, and context write guards. Test databases contain both private workspaces and original records so filtering cannot pass through an empty dataset. Existing authorization tests retain their token-permission semantics.

Run the focused tests and then the full Release backend suite. VisitorWorkspaceTests additionally covers trusted verification, independent copies at demo scale, permissions without global scopes, concurrency, caps, rollback, and limited SQL enrollment privileges. Browser tests in visitor-workspace.spec.ts cover explicit creation, loading feedback, server-granted controls, keyboard retry, and unverified restrictions. They simulate API responses; live Auth0 and SQL checks remain necessary. Confirm the original linked Google account still loads its records after applying the migration to the local Docker database.

References:

- https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy
- https://learn.microsoft.com/en-us/ef/core/querying/filters
- https://learn.microsoft.com/en-us/ef/core/modeling/relationships/foreign-and-principal-keys
