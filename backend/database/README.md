# Local database setup

From the repository root, apply the migrations before seeding the LocalDB development database.

```powershell
$connection = 'Server=(localdb)\MSSQLLocalDB;Database=HealthcareApp_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'

dotnet ef database update --project backend/HealthcareApp.Api --context HealthcareDbContext --connection $connection -- --environment Development
sqlcmd -S '(localdb)\MSSQLLocalDB' -E -d HealthcareApp_Dev -b -i backend/database/SeedDemoData.sql
```

The seed contains fictional patients, providers, referrals, and a confirmed appointment. Use it for local development.

## Upgrading referrals created before provider assignment was required

The `RequireReferralProvider` migration returns SQL error `51001` if any referral has a null receiving provider. This includes closed referrals and rows created by an older demo seed. The migration stops before changing the column, so the operator can preserve existing data and choose the intended provider.

- For a database containing only disposable demo data, stop the API, drop the local `HealthcareApp_Dev` database, and run the migration and seed commands above. This clears all local records, including account links.
- To keep the data, review the referrals returned by the query below. Assign each one to the intended provider who offers that exact clinical service, then verify that the query returns no rows before retrying the migration. If a referral has no eligible provider, resolve the provider eligibility first.

```sql
SELECT PublicId, ClinicalServiceId
FROM dbo.Referrals
WHERE AssignedProviderId IS NULL;
```
