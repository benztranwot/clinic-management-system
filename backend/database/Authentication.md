# Authentication and email verification

Auth0 manages sign-in, password handling, verification codes, and email verification. The application does not generate or email its own codes. Enable email verification on signup for the database connection used by Clinic Management Web and use the One-Time Password verification method when code entry is desired. Keep the callback, logout, and web-origin URLs appropriate for each environment.

For an unlinked identity, GET /api/me/onboarding reads the configured Auth0 UserInfo endpoint using the validated access token. It requires an exact subject match and a usable email. Only the provider's email_verified=true establishes verification; browser-supplied fields are ignored. Missing or false verification returns email_confirmation_required, trusted verification returns workspace_pending, and an existing application account returns ready. Existing administrator-managed account links retain access.

Verification never creates an application account or role assignment. An unlinked identity remains forbidden from /api/me and protected clinical data, including after email verification. Workspace provisioning is a separate, explicit operation: when enabled, a verified visitor can use Create my workspace to request a private demo copy. POST /api/me/workspace rechecks the trusted provider profile; a successful copy creates a workspace-scoped owner, while failures roll back all copied data. Existing linked identities are never moved or promoted by this operation. Legacy unverified accounts should complete the provider's verification process; if unavailable, the application owner can use Auth0's verification-email facilities. Do not manually mark an email verified to bypass proof of ownership.

## Login persistence

Clinic Management Web uses the Auth0 SDK's localStorage cache and refresh token rotation. Enable the Refresh Token grant and rotation for the SPA and Allow Offline Access for the API. Tokens remain subject to expiration and revocation. SDK sign-out clears the local session. Because JavaScript can read localStorage, maintain XSS protections and review the production content-security policy before hosting.

## Email delivery

The API no longer consumes DEMO_SMTP_* or DEMO_MAX_DAILY_EMAILS. Remove those unused entries from the ignored local .env manually; do not copy credentials into source files. Auth0's built-in email provider is for testing. Configure an external provider, such as Brevo SMTP, in Auth0 before public deployment. SMTP credentials belong in Auth0's provider configuration, not the frontend or API environment.

Official setup references:

- https://auth0.com/docs/manage-users/user-accounts/verify-emails
- https://auth0.com/docs/customize/email/smtp-email-providers
- https://auth0.com/docs/secure/tokens/refresh-tokens/configure-refresh-token-rotation

## Database cleanup

The original AddEmailConfirmationChallenges migration is retained because it has already been applied locally. RemoveEmailConfirmationChallenges drops only that obsolete table and its stored confirmation challenges. Clinical records, application accounts, memberships, and the migration-history table are preserved. A fresh database runs both migrations and ends with the same clinical schema.

Apply the forward cleanup without resetting the database:

```powershell
docker compose -p clinic-management run --build --rm migrate
.\scripts\Initialize-DatabaseUser.ps1
docker compose -p clinic-management up --build -d --no-deps --wait --wait-timeout 90 api web
```

Use the existing MSSQL_APP_PASSWORD at the database-user prompt. The runtime login remains unable to change account links or memberships. Dropping the obsolete table removes its object permissions automatically.

## Validation

Run the focused EmailOnboardingTests and AuthEndpointTests, then the full backend suite. Browser tests in account-onboarding.spec.ts cover unverified-account restrictions, verification rechecks, pending access, and recoverable provider failures. They simulate provider responses and do not exercise hosted Auth0 email delivery.

For the live check, sign up through Auth0 with an inbox you control, complete its verification code, and confirm the user is marked verified in Auth0. With visitor enrollment enabled, returning to the application shows Create your private workspace. Confirm that Create my workspace opens a populated private workspace and that refresh retains it. If enrollment is disabled, the verified account remains waiting for workspace access. Existing linked accounts should still load their authorized workspace, survive refresh, and remain signed out after logout and refresh.
