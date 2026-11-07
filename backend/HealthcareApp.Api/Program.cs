using HealthcareApp.Api.Data;
using HealthcareApp.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Read the database connection string and stop startup if it is missing.
var connectionString =
    builder.Configuration.GetConnectionString("HealthcareDatabase")
    ?? throw new InvalidOperationException("Connection string 'HealthcareDatabase' is missing.");

// Register the EF Core context to use SQL Server.
builder.Services.AddDbContext<HealthcareDbContext>(options =>
    options.UseSqlServer(connectionString)
);

// Read the Auth0 settings required to validate incoming access tokens.
var auth0Authority =
    builder.Configuration["Auth0:Authority"]
    ?? throw new InvalidOperationException("Auth0 authority is missing.");

var auth0Audience =
    builder.Configuration["Auth0:Audience"]
    ?? throw new InvalidOperationException("Auth0 audience is missing.");

// Validate bearer tokens issued by Auth0 for this API.
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = auth0Authority;
        options.Audience = auth0Audience;

        // Preserve original claim names such as sub.
        options.MapInboundClaims = false;

        // Accept the signing algorithm selected for our Auth0 API.
        options.TokenValidationParameters.ValidAlgorithms = ["RS256"];
    });

// Define the permissions required to access API resources.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "ReadClinics",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", "read:clinics");
        }
    );

    options.AddPolicy(
        "ManageClinics",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", "manage:clinics");
        }
    );
    // Require permission to view patient records.
    options.AddPolicy(
        "ReadPatients",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", "read:patients");
        }
    );

    // Require permission to create, update, or delete patient records.
    options.AddPolicy(
        "ManagePatients",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", "manage:patients");
        }
    );
    // Require permission to view providers.
    options.AddPolicy(
        "ReadProviders",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", "read:providers");
        }
    );

    // Require permission to create, update, or delete providers.
    options.AddPolicy(
        "ManageProviders",
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permissions", "manage:providers");
        }
    );
});

var app = builder.Build();

// Authenticate the caller before evaluating authorization requirements.
app.UseAuthentication();
app.UseAuthorization();

// Define a simple response at the root URL.
app.MapGet("/", () => "Hello World!");

// Define a health route that shows the API is responding.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Define a protected route for checking access-token authentication.
app.MapGet("/api/auth/check", () => Results.Ok(new { message = "Authentication succeeded." }))
    .RequireAuthorization();

// Register the clinic CRUD routes.
app.MapClinicEndpoints();

// Register the patient routes.
app.MapPatientEndpoints();

// Register the provider routes.
app.MapProviderEndpoints();

// Register the clinical service routes.
app.MapClinicalServiceEndpoints();

// Register the provider eligibility routes.
app.MapProviderServiceEndpoints();

// Register the referral routes.
app.MapReferralEndpoints();

app.Run();

// Expose the API entry point to the integration test project.
public partial class Program { }
