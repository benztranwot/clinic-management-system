using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register a standard error response and include a request identifier.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        // Keep internal exception details out of server-error responses.
        if (context.ProblemDetails.Status >= StatusCodes.Status500InternalServerError)
        {
            context.ProblemDetails.Title = "An unexpected error occurred.";
            context.ProblemDetails.Detail = null;
        }
    };
});

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

// Register the application's permission-based authorization policies.
builder.Services.AddApiAuthorization();

var app = builder.Build();

// Handle unexpected failures from the request pipeline.
app.UseExceptionHandler(
    new ExceptionHandlerOptions
    {
        // Preserve client-error status codes for malformed requests.
        StatusCodeSelector = exception =>
            exception is BadHttpRequestException badRequest
                ? badRequest.StatusCode
                : StatusCodes.Status500InternalServerError,

        // Keep diagnostics for unexpected failures.
        SuppressDiagnosticsCallback = context => context.Exception is BadHttpRequestException,
    }
);

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

// Register the route for the signed-in user's account.
app.MapAccountEndpoints();

// Register the clinic CRUD routes.
app.MapClinicEndpoints();

// Register the patient routes.
app.MapPatientEndpoints();

// Register the provider routes.
app.MapProviderEndpoints();

// Register the provider's weekly scheduling routes.
app.MapProviderWeeklyHoursEndpoints();
app.MapProviderTimeOffEndpoints();

// Register the clinical service routes.
app.MapClinicalServiceEndpoints();

// Register the provider eligibility routes.
app.MapProviderServiceEndpoints();

// Register the referral routes.
app.MapReferralEndpoints();

app.Run();

// Expose the API entry point to the integration test project.
public partial class Program { }
