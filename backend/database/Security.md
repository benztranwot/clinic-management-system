# Local security controls

This document describes the current local demo setup. It is not a claim of production or healthcare-regulatory compliance. The seeded records are synthetic.

## Database access

The migration service connects as `sa` to apply schema changes. The API connects as `clinic_app`, with object-level permissions for its existing operations. Direct writes to account links and clinic memberships are denied to the API. Existing-account administration uses an administrator connection. Visitor enrollment has EXECUTE access to one ownership-chained procedure that inserts an owner and clinician membership only into a new non-original workspace, inside the backend copy transaction. It cannot change existing accounts or original-workspace roles.

After migrations, run `scripts/Initialize-DatabaseUser.ps1` from PowerShell. Enter the same password as `MSSQL_APP_PASSWORD` in the root `.env`. The helper creates or updates the login and its database user, applies the grants, and checks effective permissions. Rerunning it sets the login password to the value entered. It does not create another database or change application account links. Unexpected role memberships stop setup for manual review; the helper does not remove roles or revoke arbitrary pre-existing grants.

The helper sends setup SQL through standard input. It does not write the password into a project file, temporary SQL file, or command-line argument. The root `.env` remains private and ignored by Git. Do not enable PowerShell debug tracing while running password-handling commands.

The API has these database permissions:

| Tables | Permissions |
| --- | --- |
| AppAccounts, ClinicMemberships | SELECT |
| Clinics, Patients, Providers, ClinicalServices | SELECT, INSERT, UPDATE, DELETE |
| ProviderServices, ProviderWeeklyHours, ProviderTimeOff | SELECT, INSERT, DELETE |
| Referrals, Appointments | SELECT, INSERT, UPDATE |

## Authentication and authorization

The API validates signed Auth0 access tokens, including issuer, audience, expiration, and the RS256 algorithm. An Auth0 login does not automatically create an application account. `/api/me` requires a matching issuer/subject pair in `AppAccounts`.

Original-workspace directory and referral routes use explicit token permissions. An enabled private workspace owner receives the corresponding clinical capabilities only inside its selected workspace; this grants no Auth0 global roles or access to the original dataset. These are global permissions, not clinic membership limits. Clinic referral routes check the receiving-clinic membership. Appointment scheduling routes check administrator or front-desk access to the clinic. A clinician's ability to complete a visit also depends on the linked provider. Patient routes derive the patient ID from the signed-in account and do not accept another patient's ID as a scope override.

Paginated searches retain these rules. Counts are calculated from the access-filtered query. Direct-booking options are restricted to scheduling clinics and services allowing direct booking.

Auth0 handles email verification. For unlinked identities, the API reads the matching Auth0 UserInfo profile and accepts only a trusted verified email. Verification alone does not create accounts or grant workspace roles. The separate visitor-enrollment operation rechecks verification before creating a private workspace owner. See [authentication](Authentication.md) for setup and checks.

## Workspace ownership

Clinical queries and writes are scoped to the workspace selected from the authenticated account. Composite foreign keys prevent cross-workspace relationships, while backend filters and write guards enforce ownership on API operations. Existing records belong to the original workspace. See [workspace isolation](WorkspaceIsolation.md) for the current rollout and tests.

## HTTP protections

API responses use `Cache-Control: no-store`, including application error responses. nginx adds `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: no-referrer`.

The current Content Security Policy is a baseline: `base-uri 'self'; object-src 'none'; frame-ancestors 'none'`. It does not yet restrict script or connection sources. An Auth0-compatible source policy and HTTPS configuration must be verified against the deployed origin during hosting preparation.

nginx limits request bodies to 64 KiB. Oversized requests return HTTP 413 before reaching the API. No file-upload routes are currently supported.

Authenticated API traffic is limited to 120 requests per minute for each issuer/subject pair, per API instance. Tabs and refreshed access tokens share the quota. Excess requests return HTTP 429 with `Retry-After` and a problem response. Health checks are exempt. Counters reset with the API process and are not shared between instances. This limiter does not throttle anonymous traffic or provide denial-of-service protection at the hosting edge.

## Verification

Run the full Release backend suite with `scripts/Test-Backend.ps1 -Mode Full` and the frontend browser suite with `npm.cmd --prefix frontend run test:e2e -- --forbid-only`. Integration tests use isolated LocalDB databases; browser tests use mocked API and authentication data. Test counts change as cases are added.

Check dependencies from the repository root:

```powershell
dotnet package list --project .\HealthcareApp.slnx --vulnerable --include-transitive
npm.cmd --prefix frontend audit --omit=dev
npm.cmd --prefix frontend audit --include=dev
```

These scans check known package advisories at the time they run. They do not scan container operating-system packages or replace deployment verification. An unavailable advisory feed leaves the corresponding check incomplete.
