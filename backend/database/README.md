# Local database setup

For the larger fictional demo dataset and automated API timing baseline,
see [scale testing](ScaleTesting.md).

From the repository root, apply the migrations before seeding the LocalDB development database.

```powershell
$connection = 'Server=(localdb)\MSSQLLocalDB;Database=HealthcareApp_Dev;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'

dotnet ef database update --project backend/HealthcareApp.Infrastructure --startup-project backend/HealthcareApp.Api --context HealthcareDbContext --connection $connection -- --environment Development
sqlcmd -S '(localdb)\MSSQLLocalDB' -E -C -b -I -d HealthcareApp_Dev -i backend/database/SeedDemoData.sql,backend/database/SeedScaleData.sql
```

The seed keeps the original Northside workflow examples and adds 5 clinics, 25
providers, 20 services, 250 patients, 300 referrals, and 1,000 appointments to the
same development database. Existing account links and edited records are preserved.

## Upgrading referrals created before provider assignment was required

The `RequireReferralProvider` migration returns SQL error `51001` if any referral has a null receiving provider. This includes closed referrals and rows created by an older demo seed. The migration stops before changing the column, so the operator can preserve existing data and choose the intended provider.

- For a database containing only disposable demo data, stop the API, drop the local `HealthcareApp_Dev` database, and run the migration and seed commands above. This clears all local records, including account links.
- To keep the data, review the referrals returned by the query below. Assign each one to the intended provider who offers that exact clinical service, then verify that the query returns no rows before retrying the migration. If a referral has no eligible provider, resolve the provider eligibility first.

```sql
SELECT PublicId, ClinicalServiceId
FROM dbo.Referrals
WHERE AssignedProviderId IS NULL;
```
