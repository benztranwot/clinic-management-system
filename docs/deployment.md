# Deployment checks

The [Compose setup](setup.md#run-locally-with-docker-compose) serves the local
demo at `http://localhost:8080`. Use these checks when preparing a hosted demo.

1. Configure HTTPS for the public origin. Set the exact HTTPS origin in the Auth0 SPA callback, logout, and web origin settings.
2. Set server-side SQL and Auth0 configuration through the host's secret and environment settings. Build the frontend with the public Auth0 SPA configuration for that origin.
3. Back up the database before an upgrade. Apply the Infrastructure migrations using the migration identity, then run the API with the restricted database login.
4. Smoke-test `/api/health`, sign-in and sign-out, referral booking, rescheduling, and patient access. Check that a visitor can access their own synthetic workspace and that it stays isolated from other workspaces.
5. Verify the deployed content security policy and follow the remaining checks in the [security guide](../backend/database/Security.md).

## Security controls

The API uses a restricted database login, validates Auth0 access tokens, and applies permission and account/clinic checks on protected routes. API responses use `Cache-Control: no-store`. nginx supplies baseline security headers and a 64 KiB request-body limit. Authenticated API traffic is limited to 120 requests per minute per issuer/subject pair, per API instance; health checks are exempt. See [local security controls](../backend/database/Security.md) for grants, validation commands, and deployment limitations.

## Authentication and email confirmation

Sign-in uses Auth0 with rotating refresh tokens and the SDK's persistent browser cache so refreshes and other tabs can restore the session. Signing out clears the application's cached records and the Auth0 SDK cache. Browser storage is accessible to page JavaScript, so the deployed script-source policy and XSS protections remain part of hosting verification.

Existing administrator-managed account links retain access. For an unlinked sign-in, the server reads the matching Auth0 UserInfo profile. Auth0 handles verification codes; the application accepts trusted verified emails and keeps workspace access separate from verification. Unverified accounts receive provider-verification guidance, while confirmed accounts wait for workspace provisioning. See [authentication setup](../backend/database/Authentication.md) for configuration and validation.
