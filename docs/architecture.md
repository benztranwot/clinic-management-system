# Architecture

The React client calls the ASP.NET Core API through nginx in Docker and through
the Vite proxy during frontend development. Auth0 issues access tokens, and SQL
Server stores clinic records, workspace memberships, and appointments.

## Backend

| Project | Responsibility |
| --- | --- |
| `HealthcareApp.Api` | Minimal API endpoints, HTTP request models, dependency registration, authorization, and workspace middleware. |
| `HealthcareApp.Application` | Appointment availability, including clinic-local booking windows and slot rules. It depends on Domain and defines the interface for availability reads. |
| `HealthcareApp.Domain` | Entities and enums shared by the backend projects. |
| `HealthcareApp.Infrastructure` | EF Core context, migrations, SQL locks, and the implementation of availability reads. |
| `HealthcareApp.Api.IntegrationTests` | HTTP workflow and permission tests with isolated SQL Server LocalDB databases. |

On an authenticated clinical request, the API selects the caller's workspace.
EF Core query filters and write guards use that workspace, and composite
foreign keys protect relationships between records. The availability reader and
appointment endpoint share one scoped context, so availability checks during
booking and rescheduling use the same transaction and workspace.

Existing migrations live in Infrastructure and retain their IDs. The API
remains the EF Core startup project and loads the connection settings.
See [the EF Core decision](decisions/0001-use-ef-core.md) and
[workspace isolation](../backend/database/WorkspaceIsolation.md).

## Frontend

`frontend/src/pages` contains route components. `features` groups components and
hooks for appointments, patients, clinics, providers, and referrals. Shared UI
lives in `components`, reusable React hooks in `hooks`, date and pagination helpers
in `utils`, API calls in `api`, and session and onboarding code in `auth`.

The signed-in desktop layout uses a sidebar and compact tables or two card columns, with a collapsible menu and one card column on smaller screens. Long details, forms, and narrow or zoomed views can scroll so records and controls remain accessible. Appointment details automatically recheck completion eligibility around the start time and every 15 seconds afterward for administrators and the assigned clinician, stopping when eligible or no longer confirmed. The server capability remains authoritative.

Upcoming appointments includes a Create appointment flow. `/api/appointments/booking-options` and `/api/appointments/booking-availability` expose eligible provider/service combinations and dates within the caller's scheduling clinics for services that allow direct booking. The patient picker uses the protected `/api/patients/search` endpoint to find matches by name or exact date of birth. Create appointment and Reschedule use the same calendar and slot rules. The server checks availability again when staff save. Upcoming and conflict lists and the conflict badge update every 30 seconds while visible and after writes.

Page and data loading states share a spinner with a readable status message. Pending form actions use the same decorative spinner with their action label. Spinner animation stops for reduced-motion preferences, and errors retain explicit recovery actions where retry is appropriate.

Main list panels fill the available content height only when the current page contains its full page size (10 referrals, 16 clinics/patients, or 14 appointments/conflicts). Partial, filtered, empty, loading, and error pages stay at their natural content height. This uses the displayed item count rather than the overall result count, so the last page also stays compact when incomplete. Detail and creation forms retain natural height.
