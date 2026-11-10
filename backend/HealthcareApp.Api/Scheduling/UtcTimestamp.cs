using System.Globalization;

namespace HealthcareApp.Api.Scheduling;

internal static class UtcTimestamp
{
    private static readonly string[] UtcFormats =
        ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    public static bool TryParse(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is not null
            && (value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal))
            && DateTimeOffset.TryParseExact(
                value, UtcFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out timestamp
            );
    }
}
