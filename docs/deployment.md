# Deployment checks

The [Compose setup](setup.md#run-locally-with-docker-compose) serves the local
demo at `http://localhost:8080`. Use these checks when preparing a hosted demo.

1. Configure HTTPS for the public origin. The web container serves HTTP on port 80, so terminate TLS at the host's load balancer or a reverse proxy in front of it, and have that proxy send `X-Forwarded-Proto: https` and `X-Forwarded-For`. Set `TRUSTED_PROXY_CIDR` to the proxy's address range. Set the exact HTTPS origin in the Auth0 SPA callback, logout, and web origin settings.
2. Set server-side SQL and Auth0 configuration through the host's secret and environment settings. The API has no built-in Auth0 tenant and stops at startup unless `Auth0__Authority` and `Auth0__Audience` are set. The web container needs `AUTH0_DOMAIN` at runtime for its Content Security Policy. Build the frontend with the public Auth0 SPA configuration for that origin.
3. Use a SQL Server edition licensed for production. The Compose file defaults to `MSSQL_PID=Developer`, which is for development only; set `MSSQL_PID=Express` (free, with size limits), use a licensed edition, or use a managed SQL service. With a managed service, remove `TrustServerCertificate=True` from the connection strings so the server certificate is validated.
4. Back up the database before an upgrade. Apply the Infrastructure migrations using the migration identity, then run the API with the restricted database login.
5. Point the host's health probe at `/api/health/ready`, which returns 503 when the database is unreachable. `/api/health` reports only that the API process is responding.
6. Smoke-test `/api/health/ready`, sign-in and sign-out, referral booking, rescheduling, and patient access. Check that a visitor can access their own synthetic workspace and that it stays isolated from other workspaces.
7. Open the deployed site with the browser developer tools and confirm there are no Content Security Policy violations, then follow the remaining checks in the [security guide](../backend/database/Security.md).

Outside the repository, also check that `main` requires pull requests with passing CI, that no Auth0 default role or Action grants API permissions to new sign-ups, and that Auth0 Attack Protection (bot detection and brute-force protection) is enabled.

## Security controls

The API uses a restricted database login, validates Auth0 access tokens, and applies permission and account/clinic checks on protected routes. API responses use `Cache-Control: no-store`. nginx supplies a Content Security Policy, HSTS over HTTPS, other security headers, and a 64 KiB request-body limit. Authenticated API traffic is limited to 120 requests per minute per issuer/subject pair, anonymous API traffic to 60 per minute per client address, and visitor workspace creation to 10 attempts per hour per client address, per API instance; health checks are exempt. See [local security controls](../backend/database/Security.md) for grants, validation commands, and deployment limitations.

## Authentication and email confirmation

Sign-in uses Auth0 with rotating refresh tokens and the SDK's persistent browser cache so refreshes and other tabs can restore the session. Signing out clears the application's cached records and the Auth0 SDK cache. Browser storage is accessible to page JavaScript, so the deployed script-source policy and XSS protections remain part of hosting verification.

Existing administrator-managed account links retain access. For an unlinked sign-in, the server reads the matching Auth0 UserInfo profile. Auth0 handles verification codes; the application accepts trusted verified emails and keeps workspace access separate from verification. Unverified accounts receive provider-verification guidance, while confirmed accounts wait for workspace provisioning. See [authentication setup](../backend/database/Authentication.md) for configuration and validation.
