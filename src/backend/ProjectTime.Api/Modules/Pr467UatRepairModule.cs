using Npgsql;

namespace ProjectTime.Api.Modules;

public static class Pr467UatRepairModule
{
    public static WebApplication MapPr467UatRepairEndpoints(this WebApplication app)
    {
        app.MapGet("/api/work-register/projects/{projectId:guid}/creation-receipt", GetCreationReceiptAsync);
        return app;
    }

    private static async Task<IResult> GetCreationReceiptAsync(Guid projectId, HttpContext context)
    {
        var actualUserId = ReadGuid(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (actualUserId is null)
            return Results.Json(new { status = "session_required", message = "A valid Pulse session is required." }, statusCode: 401);
        var effectiveUserId = ReadGuid(context, "ProjectPulseEffectiveUserId") ?? actualUserId.Value;

        await using var connection = await OpenConnectionAsync();
        var roles = await LoadRolesAsync(connection, effectiveUserId);
        var broad = roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR") || roles.Contains("PROJECT_TEAM_COORDINATOR");

        await using var command = new NpgsqlCommand("""
            SELECT project.project_id,
                   COALESCE(project.project_code, ''),
                   COALESCE(project.project_name, ''),
                   COALESCE(client.client_name, ''),
                   COALESCE(
                       to_jsonb(project) ->> 'work_type',
                       to_jsonb(project) ->> 'project_type',
                       to_jsonb(project) ->> 'requested_work_type',
                       'Project'
                   ),
                   COALESCE(to_jsonb(project) ->> 'created_at', '')
            FROM projects project
            LEFT JOIN clients client ON client.client_id = project.client_id
            WHERE project.project_id = @project_id
              AND (
                    @broad
                    OR project.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1 FROM project_assignments assignment
                        WHERE assignment.project_id = project.project_id
                          AND assignment.user_id = @user_id
                    )
              );
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", effectiveUserId);
        command.Parameters.AddWithValue("broad", broad);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted))
            return Results.NotFound(new { status = "project_not_found_or_unauthorized", message = "The created work record is unavailable in the current scope." });

        var workType = CanonicalWorkType(reader.GetString(4));
        var createdAt = reader.GetString(5);
        return Results.Ok(new
        {
            status = "work_creation_receipt_loaded",
            workId = reader.GetGuid(0),
            workCode = reader.GetString(1),
            workType,
            workTypeLabel = WorkTypeLabel(workType),
            createdAt = string.IsNullOrWhiteSpace(createdAt) ? DateTimeOffset.UtcNow.ToString("O") : createdAt,
            customerName = reader.GetString(3),
            workName = reader.GetString(2),
            identifierLabel = IdentifierLabel(workType),
            immutable = true
        });
    }

    private static string CanonicalWorkType(string value)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (normalized is "servicerequest" or "service" or "sr") return "service_request";
        if (normalized == "iqs") return "iqs";
        if (normalized is "internalproject" or "internal") return "internal_project";
        if (normalized is "presales" or "presale") return "pre_sales";
        return "project";
    }

    private static string WorkTypeLabel(string workType) => workType switch
    {
        "service_request" => "Service Request",
        "iqs" => "IQS",
        "internal_project" => "Internal Project",
        "pre_sales" => "Pre-Sales",
        _ => "Project"
    };

    private static string IdentifierLabel(string workType) => workType switch
    {
        "service_request" => "Service Request Number",
        "iqs" => "IQS Number",
        "internal_project" => "Internal Project Number",
        "pre_sales" => "Pre-Sales Number",
        _ => "Project Number"
    };

    private static async Task<HashSet<string>> LoadRolesAsync(NpgsqlConnection connection, Guid userId)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT upper(role.role_code)
            FROM app_user_role_assignments assignment
            JOIN app_roles role ON role.app_role_id = assignment.app_role_id
            WHERE assignment.user_id = @user_id
              AND assignment.is_active = TRUE
              AND role.is_active = TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) roles.Add(ScopedRolePolicyModule.CanonicalRole(reader.GetString(0)));
        return roles;
    }

    private static Guid? ReadGuid(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(Convert.ToString(value), out var parsed)) return parsed;
        }
        return null;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var user = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        string? connectionString = null;
        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(database) && !string.IsNullOrWhiteSpace(user))
        {
            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
                Database = database,
                Username = user,
                Password = password,
                IncludeErrorDetail = false
            }.ConnectionString;
        }
        connectionString ??= new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        }.Select(Environment.GetEnvironmentVariable).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Project database connection is not configured.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
