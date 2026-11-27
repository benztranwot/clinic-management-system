# Scale testing

The running app uses its existing `HealthcareApp_Dev` demo database.
`SeedScaleData.sql` adds 5 clinics, 25 providers, 20 services, 250 patients,
300 referrals, and 1,000 appointments to it. `SeedDemoData.sql` supplies the original
Northside workflow examples. Each new provider offers the four services at their clinic.
Clinic and person names are realistic synthetic examples. Services and referral
reasons match each clinic's specialty, including family medicine, rehabilitation,
cardiology, orthopedics, and behavioral health.

Referrals cover pending review, missing information, ready to schedule, scheduled,
and closed visits. Appointments include confirmed, cancelled, and completed visits.
Five provider time-off blocks exercise conflict filtering. Completed visits are in
the past, and upcoming visits fall within the next ten weekdays. All names,
addresses, and login identities are fictional.

## Run the API baseline

Use this command from the repository root on Windows with SQL Server LocalDB installed.

```powershell
dotnet test backend/HealthcareApp.Api.IntegrationTests/HealthcareApp.Api.IntegrationTests.csproj --configuration Release --filter 'Category=Scale' --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=scale-baseline.trx' --results-directory TestResults/scale
```

The test creates a uniquely named integration-test database, applies the existing
migrations, seeds it twice, and verifies stable appointment IDs and start times.
It also checks that pre-existing account links, patient records, and provider edits survive seeding.
It checks exact dataset counts, referral stages, clinic queues, every appointment
search page, combined text and date filters, descending ordering, and the provider
calendar. It deletes its own database in cleanup.

The output reports the first request, the median and p95 of ten repeated requests,
and response bytes for the following routes.

- The all-clinic and single-clinic referral queues.
- Appointment search pages 1 and 2, combined filters, and descending ordering.
- The provider's five-month calendar.

Keep the generated `TestResults/scale/scale-baseline.trx` as the measurement record.
These are sequential, in-process TestServer measurements against LocalDB. They
include API work and response reading, while database setup runs before timing.
They establish a local baseline. Browser rendering, network latency, Azure cold
starts, concurrent visitors, and sustained load need their own measurements.
The test asserts correctness without imposing a machine-specific latency limit.

## Use the larger dataset in the app

From the normal repository root with its Docker configuration, run the two seeds
against the existing demo database.

```powershell
docker compose -p clinic-management up -d --wait db
if ($LASTEXITCODE -ne 0) { throw 'The database did not start.' }
docker compose -p clinic-management exec db sh -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d HealthcareApp_Dev -i /seed/SeedDemoData.sql,/seed/SeedScaleData.sql'
```

Refresh the app and clear queue filters to see the larger dataset. With just the
original examples and the expanded seed loaded, the database contains 6 clinics,
27 providers, 22 services, 253 patients, 305 referrals, and 1,001 appointments.
Existing user-created records can increase these totals.

The script accepts the development and seed-check database names and uniquely
named integration-test databases. A transaction and database application lock
keep the larger dataset inserts atomic and serialize reruns. Stable public IDs
prevent duplicates. Rerunning adds missing records and preserves existing edits,
appointment dates, and account links. Earlier numbered or placeholder seed labels
are upgraded only when they still match their original seeded values. Existing bookings and active referrals are
checked before adding potentially conflicting rows.

Normal demo bookings use the unprivileged fictional booking identity already used
by the original seed. Automated test identities are added only inside temporary
integration-test databases. Those databases are created and removed by the tests.
