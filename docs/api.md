# API overview

The React frontend calls the ASP.NET Core API under `/api` on the same origin.
Protected routes require an Auth0 access token for the configured API audience.
The backend checks app accounts, workspace boundaries, clinic memberships, and
appointment ownership as appropriate to each route.

| Workflow | Entry points |
| --- | --- |
| Health and session | `GET /api/health`, `GET /api/me`. The web container's internal listener serves `GET /api/health/ready` for database readiness. |
| Patient records | `GET /api/me/appointments`, `GET /api/me/referrals`. Append the record's public ID to open a detail. |
| Clinic directories | `/api/clinics`, `/api/patients`, and the provider and service routes under each clinic. |
| Referrals | `/api/referrals` for referral review and intake. `POST /api/referrals/{referralPublicId}/appointments` books an eligible referral. |
| Direct booking | `GET /api/appointments/booking-options` and `GET /api/appointments/booking-availability` prepare `POST /api/appointments`. |
| Appointment actions | The `/api/appointments/{publicId}` routes expose details, reschedule availability, rescheduling, cancellation, and completion. |
| My schedule | `GET /api/me/calendar` returns the linked clinician's calendar. Weekly hours and time off use `/api/me/weekly-hours` and `/api/me/time-off`. |

The [endpoint mappings](../backend/HealthcareApp.Api/Endpoints) define the exact
route parameters and authorization requirements. See the
[local security guide](../backend/database/Security.md) for database and HTTP controls.

## Workspace lists

Referrals load 10 records per page, clinics and patients load 16, and upcoming appointments and availability conflicts load 14. Filters run on the server before paging. Each search returns `{ items, page, pageSize, totalCount, hasMore }` and uses `Cache-Control: no-store`.

| POST route | Optional filters |
| --- | --- |
| `/api/referrals/search` | `patient`, `service`, `clinic`, `stage` |
| `/api/clinics/{clinicPublicId}/referrals/search` | Same referral filters, scoped to the staff member's clinic |
| `/api/clinics/search` | `name`, `address`, `timeZone` |
| `/api/patients/search` | `firstName`, `lastName`, `dateOfBirth` (YYYY-MM-DD) |
| `/api/appointments/upcoming/search` | `patient`, `clinic`, `service`, `provider`, `fromUtc`, `untilUtc`, `latestFirst` |
| `/api/appointments/needs-attention/search` | Same appointment filters. Clinicians can see conflicts on their own schedules. |

All search bodies accept a one-based `page` (default 1). Text filters match substrings, use the database's collation, and accept up to 200 characters. Appointment dates use an inclusive start and exclusive end; the interface converts the selected local dates to UTC. Patient search text stays in request bodies and navigation state rather than URLs. Results use a unique tie-breaker for stable ordering. Existing GET lists remain available to lookup forms and API consumers. The navigation badge uses `/api/appointments/needs-attention/count` instead of downloading conflict records.
