namespace HealthcareApp.Api.Dtos;

// Require explicit UTC timestamps so the server never guesses a time zone.
public record CreateProviderTimeOffRequest(string? StartUtc, string? EndUtc);
