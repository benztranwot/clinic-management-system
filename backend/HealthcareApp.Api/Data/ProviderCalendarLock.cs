using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

internal static class ProviderCalendarLock
{
    // Serialize calendar writes for one provider until the transaction ends.
    public static async Task<bool> TryAcquireAsync(
        HealthcareDbContext db,
        int providerId,
        CancellationToken cancellationToken
    )
    {
        var resource = FormattableString.Invariant($"ProviderCalendar:{providerId}");

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
                    THROW 51040, 'The provider calendar is busy.', 1;
                IF @result < 0
                    THROW 51041, 'Could not acquire the provider calendar lock.', 1;
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
