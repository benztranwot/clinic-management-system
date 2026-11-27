# Development setup

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

   See [visitor template setup](../backend/database/WorkspaceIsolation.md#visitor-enrollment-and-rollout) to enable private copies of the synthetic records for new visitors.

   The seed keeps the Northside workflow examples and adds 5 clinics, 25 providers,
   20 services, 250 patients, 300 referrals, and 1,000 appointments to the same demo
   database. Rerunning it adds missing fictional rows and preserves existing edits
   and account links. See [scale testing](../backend/database/ScaleTesting.md) for the
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

Run `docker compose down` when finished. The named volume keeps the demo data and account links. The Windows development workflow with Vite, ASP.NET Core, and LocalDB is also available. See [local database setup](../backend/database/README.md).

## Run ASP.NET Core and Vite directly

Install the .NET 10 SDK, Node.js 24, and SQL Server LocalDB with `sqlcmd`.
Run the following commands from the repository root. Use the domain and API
audience from your development Auth0 tenant.

```powershell
dotnet tool restore
dotnet user-secrets set 'Auth0:Authority' 'https://your-tenant.us.auth0.com/' --project backend/HealthcareApp.Api
dotnet user-secrets set 'Auth0:Audience' 'https://clinic-management-api' --project backend/HealthcareApp.Api
dotnet user-secrets set 'ConnectionStrings:HealthcareDatabase' 'Server=(localdb)\MSSQLLocalDB;Database=HealthcareApp_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True' --project backend/HealthcareApp.Api
```

Follow the [LocalDB migration and seed steps](../backend/database/README.md).
Add `http://localhost:5173` to the Auth0 SPA callback, logout, and web origin settings.
Start the API in the first terminal.

```powershell
dotnet run --project backend/HealthcareApp.Api --launch-profile http
```

In a second terminal, copy and fill in the Vite template. Start Vite on port 5173.
The Vite proxy sends `/api` requests to the ASP.NET Core server on port 5089.

```powershell
Copy-Item -LiteralPath frontend/.env.example -Destination frontend/.env.local
npm.cmd --prefix frontend ci
npm.cmd --prefix frontend run dev -- --port 5173 --strictPort
```

Apply the demo administrator setup to the LocalDB database if you want to reuse
that walkthrough locally. Account links belong to the database where they were created.

## Development checks

Run these from the repository root on Windows with SQL Server LocalDB installed:

```powershell
.\scripts\Test-Backend.ps1
npm.cmd --prefix frontend ci
npm.cmd --prefix frontend exec -- playwright install chromium
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
