# Clinic Management System

Clinic Management System is a full-stack demo for clinic directories, referral review, and appointment scheduling. Patients can view their own referrals and appointments. The app uses React and TypeScript, ASP.NET Core, SQL Server, and Auth0.

## Run locally with Docker Compose

Use Docker Desktop with Linux containers on an Intel or AMD x86-64 machine. The web app is published at `http://localhost:8080`. SQL Server and the API are reachable through the Compose network, and the named SQL volume persists across restarts. This setup uses fictional data and local development credentials.

1. From the repository root, copy the configuration template.

   ```powershell
   Copy-Item -LiteralPath .env.example -Destination .env
   ```

   Fill in the Auth0 SPA's domain and client ID, the custom API's audience, and different unique passwords for `MSSQL_SA_PASSWORD` and `MSSQL_APP_PASSWORD`. The migration service uses the administrator password; the API uses the restricted application login. Put each SQL password in single quotes in the file if it contains a dollar sign or spaces. Avoid single and double quotes within the password itself. Use the SPA's public configuration only. The root `.env` is ignored by Git.

2. In the Auth0 SPA's settings, add `http://localhost:8080` to **Allowed Callback URLs**, **Allowed Logout URLs**, and **Allowed Web Origins**. Keep any existing Vite entries. For the demo API, enable RBAC and **Add Permissions in the Access Token**. Use an account with your app's administrator permissions for the walkthrough.

3. On a fresh database, start SQL Server, apply migrations, and create the restricted application login before starting the API.

   ```powershell
   docker compose up -d --wait --wait-timeout 180 db
   docker compose run --build --rm migrate
   .\scripts\Initialize-DatabaseUser.ps1
   ```

   Check that each command succeeds before continuing. At the password prompt, enter the same `MSSQL_APP_PASSWORD` saved in `.env`. The helper creates or updates `clinic_app`, grants its table permissions, and displays effective-permission checks. It does not save the password in a SQL file or change application account links. If you intentionally use another Compose project name, pass it with `-ProjectName`.

4. Start the full stack, check the API through the web server, and optionally load the demo records.

   ```powershell
   docker compose up --build -d --wait --wait-timeout 180
   docker compose ps -a
   Invoke-RestMethod http://localhost:8080/api/health
   docker compose exec db sh -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -x -d HealthcareApp_Dev -i /seed/SeedDemoData.sql,/seed/SeedScaleData.sql'
   ```

   Compose runs EF Core migrations after SQL Server becomes ready. The `migrate` service should exit with code 0. If startup fails, inspect `docker compose logs db migrate api web`. Rebuild the web image when you change the Auth0 settings in `.env`. The named SQL volume preserves the application login; routine starts do not need the setup helper. After changing `MSSQL_APP_PASSWORD`, rerun the helper with the matching value and recreate the API container.

   The seed keeps the Northside workflow examples and adds 5 clinics, 25 providers,
   20 services, 250 patients, 300 referrals, and 1,000 appointments to the same demo
   database. Rerunning it adds missing fictional rows and preserves existing edits
   and account links. See [scale testing](backend/database/ScaleTesting.md) for the
   automated API baseline.

### Link a demo administrator

Auth0 permissions control which API operations the user can request. The app also checks its own database for account and clinic access. The Docker database starts fresh, so account links from LocalDB need to be recreated here.

In your development Auth0 tenant, open **User Management → Users** and copy the User ID of your test administrator. From the repository root, open a SQL prompt inside the Docker database.

```powershell
docker compose exec db sh -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d HealthcareApp_Dev'
```

Replace the two example values with your test tenant's issuer URL (including its trailing slash) and the Auth0 User ID. Run this once for the fresh database.

```sql
INSERT INTO dbo.AppAccounts (Issuer, Subject, IsAdministrator)
VALUES (N'https://your-tenant.us.auth0.com/', N'auth0|your-test-user-id', 1);
GO
QUIT
```

Sign in at `http://localhost:8080` and check that the referral queue and Northside Family Clinic load. A request to `/api/me` without a token should return 401.

Run `docker compose down` when finished. The named volume keeps the demo data and account links. The Windows development workflow with Vite, ASP.NET Core, and LocalDB is also available. See [local database setup](backend/database/README.md).

## Security controls

The API uses a restricted database login, validates Auth0 access tokens, and applies permission and account/clinic checks on protected routes. API responses use `Cache-Control: no-store`. nginx supplies baseline security headers and a 64 KiB request-body limit. Authenticated API traffic is limited to 120 requests per minute per issuer/subject pair, per API instance; health checks are exempt. See [local security controls](backend/database/Security.md) for grants, validation commands, and deployment limitations.

## Authentication and email confirmation

Sign-in uses Auth0 with rotating refresh tokens and the SDK's persistent browser cache so refreshes and other tabs can restore the session. Signing out clears the application's cached records and the Auth0 SDK cache. Browser storage is accessible to page JavaScript, so the deployed script-source policy and XSS protections remain part of hosting verification.

Existing administrator-managed account links retain access. For an unlinked sign-in, the server reads the matching Auth0 UserInfo profile. Auth0 handles verification codes; the application accepts trusted verified emails and keeps workspace access separate from verification. Unverified accounts receive provider-verification guidance, while confirmed accounts wait for workspace provisioning. See [authentication setup](backend/database/Authentication.md) for configuration and validation.

## Workspace lists

Referrals load 10 records per page, clinics and patients load 16, and upcoming appointments and availability conflicts load 8. Filters run on the server before paging. Each search returns `{ items, page, pageSize, totalCount, hasMore }` and uses `Cache-Control: no-store`.

| POST route | Optional filters |
| --- | --- |
| `/api/referrals/search` | `patient`, `service`, `clinic`, `stage` |
| `/api/clinics/{clinicPublicId}/referrals/search` | Same referral filters, scoped to the staff member's clinic |
| `/api/clinics/search` | `name`, `address`, `timeZone` |
| `/api/patients/search` | `firstName`, `lastName`, `dateOfBirth` (YYYY-MM-DD) |
| `/api/appointments/upcoming/search` | `patient`, `clinic`, `service`, `provider`, `fromUtc`, `untilUtc`, `latestFirst` |
| `/api/appointments/needs-attention/search` | Same appointment filters |

All search bodies accept a one-based `page` (default 1). Text filters match substrings, use the database's collation, and accept up to 200 characters. Appointment dates use an inclusive start and exclusive end; the interface converts the selected local dates to UTC. Patient search text stays in request bodies and navigation state rather than URLs. Results use a unique tie-breaker for stable ordering. Existing GET lists remain available to lookup forms and API consumers. The navigation badge uses `/api/appointments/needs-attention/count` instead of downloading conflict records.

The signed-in desktop layout uses a sidebar and compact tables or two card columns, with a collapsible menu and one card column on smaller screens. Long details, forms, and narrow or zoomed views can scroll so records and controls remain accessible. Appointment details automatically recheck completion eligibility around the start time and every 15 seconds afterward for administrators and the assigned clinician, stopping when eligible or no longer confirmed. The server capability remains authoritative.

Upcoming appointments includes a Create appointment flow. `/api/appointments/booking-options` exposes eligible provider/service combinations only within the administrator's or front-desk member's scheduling clinics, and only for services that allow direct booking. The patient picker uses the existing protected `/api/patients/search` endpoint, so the caller still needs patient-read permission. Availability and creation reuse the existing provider-availability and appointment-booking routes; referral requirements, clinic access, and slot conflicts remain server-enforced. Upcoming and conflict lists and the conflict badge update every 30 seconds while visible, and after writes; error states retain a retry action.

Page and data loading states share a spinner with a readable status message. Pending form actions use the same decorative spinner with their action label. Spinner animation stops for reduced-motion preferences, and errors retain explicit recovery actions where retry is appropriate.

Main list panels fill the available content height only when the current page contains its full page size (10 referrals, 16 clinics/patients, or 8 appointments/conflicts). Partial, filtered, empty, loading, and error pages stay at their natural content height. This uses the displayed item count rather than the overall result count, so the last page also stays compact when incomplete. Detail and creation forms retain natural height.

## Development checks

Run these from the repository root on Windows with SQL Server LocalDB installed:

```powershell
.\scripts\Test-Backend.ps1
npm.cmd --prefix frontend run lint
npm.cmd --prefix frontend run test:typecheck
npm.cmd --prefix frontend run build
npm.cmd --prefix frontend run test:e2e -- --forbid-only
```

The backend helper builds Release binaries once, then runs the full suite with four
concurrent test collections. Each test factory retains its own temporary database.
The scale measurement collection runs exclusively, without other tests competing
for LocalDB. The full suite still includes the scale correctness checks.
Direct `dotnet test` runs and CI retain a two-collection default; the local helper overrides it to four.

For shorter feedback while editing, use `.\scripts\Test-Backend.ps1 -Mode Fast`
to exclude the scale test, or use `dotnet test` with a focused `--filter`.
Use `-Mode Scale` for just the scale check. Run the full suite before merging.
For an unchanged build, `dotnet test backend/HealthcareApp.Api.IntegrationTests
-c Release --no-build --no-restore` avoids another build. Rebuild after changing code.

To compare backend execution on your machine, run:

```powershell
.\scripts\Test-Backend.ps1 -Mode Benchmark -Repeat 2
```

This performs six complete runs using the same binaries: one, two, and four workers,
then the reverse order. It prints elapsed times and the slowest tests, and saves
TRX reports plus `timings.csv` under the ignored `TestResults/backend/` directory.
The build is outside the measured runs. Use `-Workers 1` or `-Workers 2` on
Full/Fast/Scale mode to reduce concurrency when diagnosing contention.
A local comparison passed all 157 tests at each worker count and measured 117.83,
66.94, and 46.57 seconds for one, two, and four workers respectively. This is one
comparison, not a repeatability guarantee. Four workers is the local helper default;
CI keeps two. Timings vary with machine resources, LocalDB warm-up, and other work.

Browser tests use two workers locally and one in CI; override with `--workers=1`
or `--workers=4` for comparison. Each test has an isolated browser context and
mocked API/authentication data. They cover frontend workflows and recovery states;
backend integration tests verify the real API/database behavior. Browser checks
do not exercise live Auth0, email delivery, or a complete deployed stack.
If the default test port is occupied, set `$env:PLAYWRIGHT_PORT = '14204'`.

## Faster Docker iteration

Starting existing images does not need a rebuild:

```powershell
docker compose -p clinic-management up -d --wait
```

For frontend-only changes, with the database and API already running:

```powershell
docker compose -p clinic-management up -d --build --no-deps web
```

Use the full `docker compose up --build -d --wait` workflow when backend or
migration changes also need applying. Dependency manifests precede source files
in the Docker build so normal source edits reuse restore layers. npm keeps a
BuildKit download cache, and migration tools restore before application sources.
Keep build caches for repeat builds; deleting them makes the next build colder.
These commands preserve the existing SQL volume and account links.
