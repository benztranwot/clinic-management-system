using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HealthcareApp.Api.IntegrationTests;

public class HealthcareApiFactory : WebApplicationFactory<Program>
{
    // Give each test host a separate database.
    public string DatabaseName { get; } = $"HealthcareApp_IntegrationTests_{Guid.NewGuid():N}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Supply the test connection before Program.cs reads its configuration.
        builder.UseEnvironment("Testing");
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:HealthcareDatabase"] =
                        $@"Server=(localdb)\MSSQLLocalDB;Database={DatabaseName};Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
                }
            );
        });

        // Keep framework warnings and errors while reducing routine test output.
        builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("Microsoft", LogLevel.Warning);
        });

        return base.CreateHost(builder);
    }
}
