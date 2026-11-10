using System.Globalization;

namespace HealthcareApp.Api.Data;

internal static class ProviderCalendarLock
{
    // Serialize calendar writes for one provider until the transaction ends.
    public static Task<bool> TryAcquireAsync(
        HealthcareDbContext db,
        int providerId,
        CancellationToken cancellationToken
    ) => TransactionLock.TryAcquireAsync(
        db, "ProviderCalendar:" + providerId.ToString(CultureInfo.InvariantCulture), cancellationToken
    );
}
