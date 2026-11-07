using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.Authorization;

// Register the application's permission-based authorization policies.
public static class AuthorizationServiceExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        return services.AddAuthorization(options =>
        {
            // Clinic access.
            AddPermissionPolicy(options, "ReadClinics", "read:clinics");
            AddPermissionPolicy(options, "ManageClinics", "manage:clinics");

            // Patient access.
            AddPermissionPolicy(options, "ReadPatients", "read:patients");
            AddPermissionPolicy(options, "ManagePatients", "manage:patients");

            // Provider access.
            AddPermissionPolicy(options, "ReadProviders", "read:providers");
            AddPermissionPolicy(options, "ManageProviders", "manage:providers");

            // Clinical service access.
            AddPermissionPolicy(options, "ReadClinicalServices", "read:services");
            AddPermissionPolicy(options, "ManageClinicalServices", "manage:services");

            // Provider service eligibility access.
            AddPermissionPolicy(options, "ReadProviderServices", "read:provider-services");
            AddPermissionPolicy(options, "ManageProviderServices", "manage:provider-services");

            // Referral access.
            AddPermissionPolicy(options, "ReadReferrals", "read:referrals");
            AddPermissionPolicy(options, "ManageReferrals", "manage:referrals");
        });
    }

    // Apply the same authentication and permission requirements to each policy.
    private static void AddPermissionPolicy(
        AuthorizationOptions options,
        string policyName,
        string permission
    )
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", permission);
        });
    }
}
