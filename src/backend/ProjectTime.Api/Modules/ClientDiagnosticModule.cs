using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class ClientDiagnosticModule
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "conflict",
        "rate_limit",
        "service_failure",
        "network_failure"
    };

    public static WebApplication MapClientDiagnosticEndpoints(this WebApplication app)
    {
        app.MapPost(
            "/api/client-diagnostics",
            (Func<HttpContext, Task<IResult>>)RecordDiagnosticAsync);
        return app;
    }

    private static async Task<IResult> RecordDiagnosticAsync(HttpContext context)
    {
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (actualUserId is null)
        {
            return Results.Json(new { status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        ClientDiagnosticRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ClientDiagnosticRequest>();
        }
        catch (JsonException)
        {
            return Results.Json(new { status = "invalid_diagnostic_payload" }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null
            || !ValidReference(request.ReferenceId)
            || !AllowedCategories.Contains(request.Category ?? string.Empty)
            || request.StatusCode is < 400 or > 599
            || !ValidEndpoint(request.EndpointPath)
            || string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return Results.Json(new { status = "invalid_diagnostic_payload" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(new { status = "diagnostic_storage_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var endpointPath = Limit(request.EndpointPath, 240);
        var technicalCode = Limit(request.TechnicalCode, 100);
        var userMessage = Limit(request.UserMessage, 300);
        var activeRoute = Limit(request.ActiveRoute, 100);
        var userAgent = Limit(context.Request.Headers.UserAgent.ToString(), 500);
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;

        var diagnostic = new
        {
            referenceId = request.ReferenceId,
            category = request.Category,
            statusCode = request.StatusCode,
            endpointPath,
            technicalCode = string.IsNullOrWhiteSpace(technicalCode) ? null : technicalCode,
            userMessage,
            activeRoute,
            occurredAt,
            source = "web_client",
            sanitized = true
        };

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO audit_logs (
                    actor_user_id,
                    action,
                    entity_type,
                    entity_id,
                    old_value,
                    new_value,
                    ip_address,
                    user_agent,
                    created_at
                )
                VALUES (
                    @actor_user_id,
                    'client_api_error',
                    'client_diagnostic',
                    NULL,
                    NULL,
                    @new_value,
                    NULLIF(@ip_address, '')::inet,
                    @user_agent,
                    NOW()
                );
                """, connection);
            command.Parameters.AddWithValue("actor_user_id", actualUserId.Value);
            command.Parameters.AddWithValue("new_value", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(diagnostic));
            command.Parameters.AddWithValue("ip_address", ipAddress);
            command.Parameters.AddWithValue("user_agent", userAgent);
            await command.ExecuteNonQueryAsync();

            return Results.Json(
                new { status = "diagnostic_recorded", referenceId = request.ReferenceId },
                statusCode: StatusCodes.Status202Accepted);
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ClientDiagnosticModule")
                .LogWarning(
                    exception,
                    "Sanitized client diagnostic {ReferenceId} could not be recorded.",
                    request.ReferenceId);

            return Results.Json(new { status = "diagnostic_storage_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool ValidReference(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 6 and <= 32
        && value.All(character => char.IsLetterOrDigit(character) || character == '-');

    private static bool ValidEndpoint(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 240
        && (value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || value == "unknown");

    private static string Limit(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private sealed record ClientDiagnosticRequest(
        string? ReferenceId,
        string? Category,
        int StatusCode,
        string? EndpointPath,
        string? TechnicalCode,
        string? UserMessage,
        string? ActiveRoute,
        DateTimeOffset? OccurredAt);
}
