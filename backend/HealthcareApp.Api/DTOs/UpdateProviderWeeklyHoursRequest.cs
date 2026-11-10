namespace HealthcareApp.Api.Dtos;

// Replace the provider's entire week with one entry per day.
public record UpdateProviderWeeklyHoursRequest(WeeklyHoursDay?[]? Days);

public record WeeklyHoursDay(int? DayOfWeek, int? StartMinute, int? EndMinute);
