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

API responses use `Cache-Control: no-store`, including application error responses. nginx adds `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy` (camera, microphone, geolocation, payment, and USB disabled), and `Cross-Origin-Opener-Policy: same-origin`.

The Content Security Policy allows scripts, styles, and fonts from the site's own origin only, images from the origin and `data:` URLs, and Auth0 requests and the silent sign-in frame only for the configured tenant:

```text
default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' https://<AUTH0_DOMAIN>; frame-src https://<AUTH0_DOMAIN>; base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'
```

The web container renders this policy from `AUTH0_DOMAIN` at startup and refuses to start when the value is missing or is not a plain host name. Check the browser console for CSP violations after changing the frontend or the Auth0 domain, for example when moving to an Auth0 custom domain.

`Strict-Transport-Security: max-age=31536000; includeSubDomains` is sent only when the request reached the site over HTTPS, either directly or through a proxy that sets `X-Forwarded-Proto: https`. `includeSubDomains` applies to every subdomain of the public host; remove it in `frontend/nginx/default.conf.template` if any subdomain must stay on HTTP.

nginx forwards one client address to the API. It reads `X-Forwarded-For` only from `TRUSTED_PROXY_CIDR`, which defaults to loopback, so a visitor cannot choose their own address. The API accepts forwarded headers only from private network ranges by default (the Compose network). Override them with `ForwardedHeaders__KnownNetworks__0`, `__1`, and so on when the API runs on a different network.

Static files are gzip-compressed. Fingerprinted files under `/assets/` are cached for a year; `index.html` is revalidated on each load so a deployment takes effect immediately. API responses are not compressed.

nginx limits request bodies to 64 KiB. Oversized requests return HTTP 413 before reaching the API. No file-upload routes are currently supported.

Authenticated API traffic is limited to 120 requests per minute for each issuer/subject pair, per API instance. Tabs and refreshed access tokens share the quota. API requests without a valid token are limited to 60 per minute per client address. Visitor workspace creation (`POST /api/me/workspace`) is also limited to 10 attempts per hour per client address, so one visitor cannot use up the daily workspace quota with several sign-ins. Excess requests return HTTP 429 with `Retry-After` and a problem response. Health checks are exempt. Counters reset with the API process and are not shared between instances. Visitors behind one shared address (an office network, for example) share the address-based limits. These limits are not denial-of-service protection; use the host's edge protection and Auth0 Attack Protection for that.

## Verification

Run the full Release backend suite with `scripts/Test-Backend.ps1 -Mode Full` and the frontend browser suite with `npm.cmd --prefix frontend run test:e2e -- --forbid-only`. Integration tests use isolated LocalDB databases; browser tests use mocked API and authentication data. Test counts change as cases are added.

Check dependencies from the repository root:

```powershell
dotnet package list --project .\HealthcareApp.slnx --vulnerable --include-transitive
npm.cmd --prefix frontend audit --omit=dev
npm.cmd --prefix frontend audit --include=dev
```

These scans check known package advisories at the time they run. They do not scan container operating-system packages or replace deployment verification. An unavailable advisory feed leaves the corresponding check incomplete.
