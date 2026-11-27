# Deployment checks

The [Compose setup](setup.md#run-locally-with-docker-compose) serves the local
demo at `http://localhost:8080`. Use these checks when preparing a hosted demo.

1. Configure HTTPS for the public origin. Terminate TLS at the public proxy and configure it to replace incoming `X-Forwarded-Proto` and `X-Forwarded-For` with values from its trusted request context. At nginx, set `TRUSTED_PROXY_CIDR` to the immediate proxy address or dedicated subnet as seen from the web container. A host proxy sending traffic through Docker may appear as the bridge gateway. Keep the web container's published port bound to loopback and keep the API and database ports private. Set the exact HTTPS origin in the Auth0 SPA callback, logout, and web origin settings.
2. Set server-side SQL and Auth0 configuration through the host's secret and environment settings. The API has no built-in Auth0 tenant and stops at startup unless `Auth0__Authority` and `Auth0__Audience` are set. The web container needs `AUTH0_DOMAIN` at runtime for its Content Security Policy. Build the frontend with the public Auth0 SPA configuration for that origin.
3. Use a SQL Server edition licensed for production. The Compose file defaults to `MSSQL_PID=Developer`, which is for development only; set `MSSQL_PID=Express` (free, with size limits), use a licensed edition, or use a managed SQL service. With a managed service, remove `TrustServerCertificate=True` from the connection strings so the server certificate is validated.
4. Back up the database before an upgrade. Apply the Infrastructure migrations using the migration identity, then run the API with the restricted database login.
5. Use `/api/health` for public uptime checks. The web container's Docker health check calls `http://127.0.0.1:8081/api/health/ready` inside the container and fails when the database is unreachable. Keep port 8081 unpublished. The public web listener returns 404 for the readiness route.
6. Check that Docker marks the web container healthy and that the public HTTPS response includes HSTS. In the web container's access logs, check that requests from two different networks show different client addresses. Smoke-test sign-in and sign-out, referral booking, rescheduling, and patient access. Check that a visitor can access their own synthetic workspace and that it stays isolated from other workspaces.
7. Open the deployed site with the browser developer tools and confirm there are no Content Security Policy violations, then follow the remaining checks in the [security guide](../backend/database/Security.md).

Outside the repository, also check that `main` requires pull requests with passing CI, that no Auth0 default role or Action grants API permissions to new sign-ups, and that Auth0 Attack Protection (bot detection and brute-force protection) is enabled.

## Security controls

The API uses a restricted database login, validates Auth0 access tokens, and applies permission and account/clinic checks on protected routes. API responses use `Cache-Control: no-store`. nginx supplies a Content Security Policy, HSTS over HTTPS, other security headers, and a 64 KiB request-body limit. Authenticated API traffic is limited to 120 requests per minute per issuer/subject pair, anonymous API traffic to 60 per minute per client address, and visitor workspace creation to 10 attempts per hour per client address, per API instance. Health checks are exempt, and database readiness is internal. See [local security controls](../backend/database/Security.md) for grants, validation commands, and deployment limitations.

## SQL Server image updates

Check the [SQL Server release list](https://learn.microsoft.com/en-us/troubleshoot/sql/releases/download-and-install-latest-updates) and [Microsoft container tags](https://mcr.microsoft.com/product/mssql/server/tags) monthly and when Microsoft publishes a SQL Server security update. Dependabot can miss changes to the cumulative-update suffix. When a newer SQL Server 2022 build for Ubuntu 22.04 is available, update the exact `db.image` tag in `compose.yaml` in a PR. Pull the new tag and verify startup, migrations, and the web container's readiness check against a test database before deploying it.

## Authentication and email confirmation

Sign-in uses Auth0 with rotating refresh tokens and the SDK's persistent browser cache so refreshes and other tabs can restore the session. Signing out clears the application's cached records and the Auth0 SDK cache. Browser storage is accessible to page JavaScript, so the deployed script-source policy and XSS protections remain part of hosting verification.

Existing administrator-managed account links retain access. For an unlinked sign-in, the server reads the matching Auth0 UserInfo profile. Auth0 handles verification codes; the application accepts trusted verified emails and keeps workspace access separate from verification. Unverified accounts receive provider-verification guidance, while confirmed accounts wait for workspace provisioning. See [authentication setup](../backend/database/Authentication.md) for configuration and validation.
