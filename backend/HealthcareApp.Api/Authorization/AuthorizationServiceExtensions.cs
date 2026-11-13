using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.Authorization;

// Register the application's permission-based authorization policies.
public static class AuthorizationServiceExtensions
{
    internal const string ReadReferralReceivingOptions = nameof(ReadReferralReceivingOptions);

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

            // Patient appointment access.
            AddPermissionPolicy(options, "ReadAppointments", "read:appointments");

            // Referral access.
            AddPermissionPolicy(options, "ReadReferrals", "read:referrals");
            AddPermissionPolicy(options, "ManageReferrals", "manage:referrals");
            options.AddPolicy(ReadReferralReceivingOptions, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("permissions", "manage:referrals")
                .RequireClaim("permissions", "read:clinics")
                .RequireClaim("permissions", "read:services")
                .RequireClaim("permissions", "read:providers")
                .RequireClaim("permissions", "read:provider-services"));
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
