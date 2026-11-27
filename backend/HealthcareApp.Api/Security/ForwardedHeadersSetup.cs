using Microsoft.AspNetCore.HttpOverrides;

namespace HealthcareApp.Api.Security;

public static class ForwardedHeadersSetup
{
    // Private ranges cover the Compose network, where nginx is the only caller.
    private static readonly string[] DefaultKnownNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    // Read the client address and scheme only from proxies on trusted networks.
    public static IServiceCollection AddTrustedForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
        var networks = configured is { Length: > 0 } ? configured : DefaultKnownNetworks;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Use only the address added by the nearest trusted proxy.
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            foreach (var network in networks)
            {
                if (!System.Net.IPNetwork.TryParse(network, out var parsed))
                {
                    throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains an invalid CIDR range: {network}");
                }

                options.KnownIPNetworks.Add(parsed);
            }
        });

        return services;
    }
}
