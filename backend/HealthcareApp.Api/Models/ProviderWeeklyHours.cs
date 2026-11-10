namespace HealthcareApp.Api.Models;

// Store one clinic-local working window for a provider on a day of the week.
public class ProviderWeeklyHours
{
    public int ProviderId { get; set; }

    // Use the .NET day numbering, where Sunday is 0 and Saturday is 6.
    public int DayOfWeek { get; set; }

    // Minutes since local midnight. Null in both fields marks a closed day.
    public int? StartMinute { get; set; }
    public int? EndMinute { get; set; }

    // An optional recurring break inside the working window.
    public int? BreakStartMinute { get; set; }
    public int? BreakEndMinute { get; set; }
}
