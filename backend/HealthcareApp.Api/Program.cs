using HealthcareApp.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("HealthcareDatabase")
    ?? throw new InvalidOperationException("Connection string 'HealthcareDatabase' is missing.");

builder.Services.AddDbContext<HealthcareDbContext>(options =>
    options.UseSqlServer(connectionString)
);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
