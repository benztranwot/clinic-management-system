# Clinic Management frontend

This React and TypeScript app uses Vite, React Router, TanStack Query, Tailwind,
and Auth0. See [development setup](../docs/setup.md) for the API, local Auth0
configuration, and first-time startup.

From `frontend`, run the frontend checks with PowerShell.

```powershell
npm.cmd ci
npm.cmd exec -- playwright install chromium
npm.cmd run lint
npm.cmd run test:typecheck
npm.cmd run build
npm.cmd run test:e2e -- --forbid-only
```

Browser tests use simulated authentication and API responses. See the
[architecture guide](../docs/architecture.md#frontend) for the source layout.
