using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Project Time Platform API",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/version", () => Results.Ok(new
{
    application = "Project Time Platform",
    component = "ProjectTime.Api",
    version = "0.1.0",
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/db-config-check", () =>
{
    var dbName = Environment.GetEnvironmentVariable("PTP_DB_NAME");
    var dbUser = Environment.GetEnvironmentVariable("PTP_DB_USER");
    var dbHost = Environment.GetEnvironmentVariable("PTP_DB_HOST");
    var dbPort = Environment.GetEnvironmentVariable("PTP_DB_PORT");
    var dbPassword = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

    var missing = new List<string>();

    if (string.IsNullOrWhiteSpace(dbName)) missing.Add("PTP_DB_NAME");
    if (string.IsNullOrWhiteSpace(dbUser)) missing.Add("PTP_DB_USER");
    if (string.IsNullOrWhiteSpace(dbHost)) missing.Add("PTP_DB_HOST");
    if (string.IsNullOrWhiteSpace(dbPort)) missing.Add("PTP_DB_PORT");
    if (string.IsNullOrWhiteSpace(dbPassword)) missing.Add("PTP_DB_PASSWORD");

    return Results.Ok(new
    {
        configured = missing.Count == 0,
        missing,
        database = dbName,
        user = dbUser,
        host = dbHost,
        port = dbPort,
        passwordConfigured = !string.IsNullOrWhiteSpace(dbPassword)
    });
});

app.MapGet("/api/schema/tables", () => Results.Ok(new
{
    note = "Database table lookup will be enabled after PostgreSQL connectivity is added to the API.",
    expectedTables = new[]
    {
        "accounting_periods",
        "accounting_reconciliations",
        "app_users",
        "approval_records",
        "audit_logs",
        "clients",
        "notification_log",
        "notification_preferences",
        "project_assignments",
        "project_tasks",
        "projects",
        "reporting_relationships",
        "roles",
        "schema_migrations",
        "team_memberships",
        "teams",
        "time_entries",
        "timesheets",
        "user_roles",
        "utilization_snapshots"
    }
}));

app.Run();
