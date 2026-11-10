using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

internal static class TransactionLock
{
    public static Task<bool> TryAcquireReferralAsync(
        HealthcareDbContext db,
        Guid referralPublicId,
        CancellationToken cancellationToken
    ) => TryAcquireAsync(db, $"Referral:{referralPublicId:D}", cancellationToken);

    // Hold the named lock until the active transaction ends.
    public static async Task<bool> TryAcquireAsync(
        HealthcareDbContext db,
        string resource,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = {resource},
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 5000;
                IF @result IN (-1, -2, -3)
                    THROW 51040, 'The scheduling record is busy.', 1;
                IF @result < 0
                    THROW 51041, 'Could not acquire the scheduling lock.', 1;
                """,
                cancellationToken
            );

            return true;
        }
        catch (SqlException exception) when (exception.Number == 51040)
        {
            return false;
        }
    }
}
