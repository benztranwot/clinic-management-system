using HealthcareApp.Api.Data;
using HealthcareApp.Api.Endpoints;
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

var app = builder.Build();

// Define a simple response at the root URL.
app.MapGet("/", () => "Hello World!");

// Define a health route that shows the API is responding.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Register the clinic CRUD routes.
app.MapClinicEndpoints();

// Enable the referral routes after the supporting workflows are ready.
// app.MapReferralEndpoints();

app.Run();
