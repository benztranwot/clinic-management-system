# Clinic Management System

Clinic Management System is a full-stack demo for clinic directories, referral review, and appointment scheduling. Patients can view their own referrals and appointments. The app uses React and TypeScript, ASP.NET Core, SQL Server, and Auth0.

## Run locally with Docker Compose

Use Docker Desktop with Linux containers on an Intel or AMD x86-64 machine. The web app is published at `http://localhost:8080`. SQL Server and the API are reachable through the Compose network, and the named SQL volume persists across restarts. This setup uses fictional data and local development credentials.

1. From the repository root, copy the configuration template.

   ```powershell
   Copy-Item -LiteralPath .env.example -Destination .env
   ```

   Fill in the Auth0 SPA's domain and client ID, the custom API's audience, and a unique SQL Server password. Put the SQL password in single quotes in the file if it contains a dollar sign or spaces. Avoid single and double quotes in the password. Use the SPA's public configuration only. The root `.env` is ignored by Git.

2. In the Auth0 SPA's settings, add `http://localhost:8080` to **Allowed Callback URLs**, **Allowed Logout URLs**, and **Allowed Web Origins**. Keep any existing Vite entries. For the demo API, enable RBAC and **Add Permissions in the Access Token**. Use an account with your app's administrator permissions for the walkthrough.

3. Start the stack, check the API through the web server, and optionally load the demo records.

   ```powershell
   docker compose up --build -d --wait --wait-timeout 180
   docker compose ps -a
   Invoke-RestMethod http://localhost:8080/api/health
   docker compose exec db sh -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d HealthcareApp_Dev -i /seed/SeedDemoData.sql'
   ```

   Compose runs EF Core migrations after SQL Server becomes ready. The `migrate` service should exit with code 0. If startup fails, inspect `docker compose logs db migrate api web`. Rebuild the web image when you change the Auth0 settings in `.env`.

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

Sign in at `http://localhost:8080` and check that the referral queue and Northside Demo Clinic load. A request to `/api/me` without a token should return 401.

Run `docker compose down` when finished. The named volume keeps the demo data and account links. The Windows development workflow with Vite, ASP.NET Core, and LocalDB is also available. See [local database setup](backend/database/README.md).

## Test public demo onboarding

The hosted demo is optional. Each verified account gets its own fictional referral and scheduling data and can change records in that workspace. Existing accounts keep their roles. Use a separate demo deployment and database when you put this feature online.

For Compose, add the following settings to the root `.env` and supply an SMTP relay accessible from the API container. Keep relay credentials in your environment or secret store.

| Setting | Value |
| --- | --- |
| `DEMO_WORKSPACES_ENABLED` | `true` |
| `DEMO_SMTP_HOST` | SMTP relay hostname |
| `DEMO_SMTP_FROM` | Sender address authorized by the relay |
| `DEMO_SMTP_PORT` | Optional. Defaults to 587. |
| `DEMO_SMTP_REQUIRE_TLS` | Defaults to `true`. Set to `false` only for a local mail catcher. |
| `DEMO_SMTP_USERNAME`, `DEMO_SMTP_PASSWORD` | Set if the relay requires credentials. |
| `DEMO_AUTH0_EMAIL_CONNECTION` | Optional name of an enabled, email-only Auth0 database connection. Shows the combined email sign-in and sign-up button. |

Run `docker compose up --build`. Sign in with a test identity that has no existing app account and check the inbox and Spam folder for an eight-digit code at the address on its Auth0 profile. Enter the code in the original app tab and confirm that the referral queue shows the seeded records. Repeat with a second identity and confirm that edits from the first account do not appear there.

The API defaults to 25 demo workspaces and 40 confirmation email attempts per rolling day. Set `DEMO_MAX_WORKSPACES` and `DEMO_MAX_DAILY_EMAILS` in the root `.env` to override those defaults. These limits apply after sign-in. Monitor Auth0 usage and hosting spend separately.
