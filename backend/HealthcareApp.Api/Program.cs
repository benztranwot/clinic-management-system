var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet(
    "/api/health",
    () =>
    {
        return Results.Ok(new { status = "ok" });
    }
);

app.Run();
