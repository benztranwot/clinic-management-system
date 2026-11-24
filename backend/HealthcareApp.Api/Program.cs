using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Endpoints;
using HealthcareApp.Api.Security;
using HealthcareApp.Api.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

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

// Read the email profile only from the configured identity provider.
builder.Services.AddHttpClient<Auth0EmailProfileClient>(client =>
{
    client.BaseAddress = new Uri(auth0Authority.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

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

builder.Services.AddOptions<VisitorWorkspaceOptions>()
    .BindConfiguration("VisitorWorkspaces")
    .Validate(value => value.MaximumWorkspaces is >= 1 and <= 10000, "Set the workspace limit between 1 and 10000.")
    .Validate(value => value.MaximumDailyWorkspaces is >= 1 and <= 10000, "Set the daily workspace limit between 1 and 10000.")
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<WorkspaceAccess>();
builder.Services.AddScoped<VisitorWorkspaceProvisioner>();
builder.Services.AddScoped<IAuthorizationHandler, WorkspacePermissionHandler>();

// Register the application's permission-based authorization policies.
builder.Services.AddApiAuthorization();

builder.Services.AddApiRateLimiting();

var app = builder.Build();

// Apply the privacy policy to every API response, including errors.
app.Use(
    async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }

        await next(context);
    }
);

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

// Give unmatched API paths a JSON response while preserving endpoint-generated errors.
app.UseStatusCodePages(context =>
{
    var http = context.HttpContext;
    if (
        http.Response.StatusCode != StatusCodes.Status404NotFound
        || http.GetEndpoint() is not null
        || !http.Request.Path.StartsWithSegments("/api")
    )
    {
        return Task.CompletedTask;
    }

    return Results
        .Problem(statusCode: StatusCodes.Status404NotFound, title: "API route not found.")
        .ExecuteAsync(http);
});

// Authenticate the caller before evaluating authorization requirements.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<WorkspaceSelectionMiddleware>();

// Define a simple response at the root URL.
app.MapGet("/", () => "Hello World!");

// Define a health route that shows the API is responding.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .DisableRateLimiting();

// Define a protected route for checking access-token authentication.
app.MapGet("/api/auth/check", () => Results.Ok(new { message = "Authentication succeeded." }))
    .RequireAuthorization();

// Register the route for the signed-in user's account.
app.MapAccountEndpoints();
app.MapAccountOnboardingEndpoints();

// Register the clinic CRUD routes.
app.MapClinicEndpoints();

// Register the patient routes.
app.MapPatientEndpoints();

// Register the provider routes.
app.MapProviderEndpoints();

// Register the provider's weekly scheduling routes.
app.MapProviderWeeklyHoursEndpoints();
app.MapProviderTimeOffEndpoints();
app.MapProviderAvailabilityEndpoints();
app.MapProviderCalendarEndpoints();

// Register the clinical service routes.
app.MapClinicalServiceEndpoints();

// Register the provider eligibility routes.
app.MapProviderServiceEndpoints();

// Register the referral routes.
app.MapReferralEndpoints();

// Register appointment booking and detail routes.
app.MapAppointmentEndpoints();

app.Run();

// Expose the API entry point to the integration test project.
public partial class Program { }
