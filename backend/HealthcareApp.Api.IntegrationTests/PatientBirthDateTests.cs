using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class PatientBirthDateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatientWrite_BirthDateAfterUtcToday_IsRejected(bool update)
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var signedFactory = tokens.CreateFactory(factory);
        await using var configuredFactory = signedFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new BirthDateClock())
            )
        );

        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client, "manage:patients");

        using var request = new HttpRequestMessage(
            update ? HttpMethod.Put : HttpMethod.Post,
            update ? $"/api/patients/{Guid.NewGuid()}" : "/api/patients"
        )
        {
            Content = JsonContent.Create(new
            {
                firstName = "Taylor",
                lastName = "Demo",
                dateOfBirth = new DateOnly(2000, 1, 2),
            }),
        };

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Date of birth is required and cannot be in the future.",
            body.GetProperty("message").GetString()
        );
    }

    private sealed class BirthDateClock : TimeProvider
    {
        // It is already January 2 in this time zone, but still January 1 in UTC.
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
            "Test UTC+02:00", TimeSpan.FromHours(2), "Test UTC+02:00", "Test UTC+02:00"
        );

        public override DateTimeOffset GetUtcNow() => new(2000, 1, 1, 23, 59, 0, TimeSpan.Zero);
    }
}
