using Npgsql;
using System.Runtime.InteropServices;

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
    version = "0.2.1",
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/db-config-check", () =>
{
    var config = DatabaseConfig.FromEnvironment();
    return Results.Ok(new
    {
        configured = config.Missing.Count == 0,
        missing = config.Missing,
        database = config.Database,
        user = config.Username,
        host = config.Host,
        port = config.Port,
        passwordConfigured = !string.IsNullOrWhiteSpace(config.Password)
    });
});

app.MapGet("/api/db-health", async () =>
{
    var config = DatabaseConfig.FromEnvironment();

    if (config.Missing.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, now();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Results.Ok(new
        {
            status = "database_connected",
            database = reader.GetString(0),
            user = reader.GetString(1),
            timestamp = reader.GetDateTime(2)
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database connection failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/schema/tables", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var tables = new List<string>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = 'public'
        ORDER BY table_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        tables.Add(reader.GetString(0));
    }

    return Results.Ok(new
    {
        count = tables.Count,
        tables
    });
});

app.MapGet("/api/non-project-time-categories", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var categories = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            non_project_time_category_id,
            category_code,
            category_name,
            category_description,
            utilization_classification,
            utilization_bucket,
            requires_approval,
            is_active,
            display_order
        FROM non_project_time_categories
        WHERE is_active = TRUE
        ORDER BY display_order, category_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        categories.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            utilizationClassification = reader.GetString(4),
            utilizationBucket = reader.GetString(5),
            requiresApproval = reader.GetBoolean(6),
            isActive = reader.GetBoolean(7),
            displayOrder = reader.GetInt32(8)
        });
    }

    return Results.Ok(new
    {
        count = categories.Count,
        categories
    });
});

app.MapGet("/api/work-location-groups", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var groups = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT work_location_group_id, group_code, group_name, group_description, is_active, display_order
        FROM work_location_groups
        WHERE is_active = TRUE
        ORDER BY display_order, group_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        groups.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            isActive = reader.GetBoolean(4),
            displayOrder = reader.GetInt32(5)
        });
    }

    return Results.Ok(new
    {
        count = groups.Count,
        groups
    });
});

app.MapGet("/api/work-locations", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var locations = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            wl.work_location_id,
            wl.location_code,
            wl.location_name,
            wl.city,
            wl.state_region,
            wl.country,
            wl.time_zone,
            wlg.group_code,
            wlg.group_name,
            wl.display_order
        FROM work_locations wl
        LEFT JOIN work_location_groups wlg ON wlg.work_location_group_id = wl.work_location_group_id
        WHERE wl.is_active = TRUE
        ORDER BY wl.display_order, wl.location_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        locations.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            city = reader.IsDBNull(3) ? null : reader.GetString(3),
            stateRegion = reader.IsDBNull(4) ? null : reader.GetString(4),
            country = reader.GetString(5),
            timeZone = reader.IsDBNull(6) ? null : reader.GetString(6),
            groupCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            groupName = reader.IsDBNull(8) ? null : reader.GetString(8),
            displayOrder = reader.GetInt32(9)
        });
    }

    return Results.Ok(new
    {
        count = locations.Count,
        locations
    });
});

app.MapGet("/api/utilization/policies", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var policies = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            utilization_policy_id,
            policy_name,
            period_type,
            standard_period_hours,
            default_target_percent,
            presales_training_requires_approval,
            is_active
        FROM utilization_policies
        ORDER BY is_active DESC, policy_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        policies.Add(new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            periodType = reader.GetString(2),
            standardPeriodHours = reader.GetDecimal(3),
            defaultTargetPercent = reader.GetDecimal(4),
            presalesTrainingRequiresApproval = reader.GetBoolean(5),
            isActive = reader.GetBoolean(6)
        });
    }

    return Results.Ok(new
    {
        count = policies.Count,
        policies
    });
});

app.MapGet("/api/utilization/targets", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targets = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            up.policy_name,
            upt.target_percent,
            upt.target_hours,
            upt.bonus_reference_amount,
            upt.display_order
        FROM utilization_policy_targets upt
        INNER JOIN utilization_policies up ON up.utilization_policy_id = upt.utilization_policy_id
        WHERE up.is_active = TRUE
        ORDER BY upt.display_order, upt.target_percent;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        targets.Add(new
        {
            policyName = reader.GetString(0),
            targetPercent = reader.GetDecimal(1),
            targetHours = reader.GetDecimal(2),
            bonusReferenceAmount = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
            displayOrder = reader.GetInt32(4)
        });
    }

    return Results.Ok(new
    {
        count = targets.Count,
        targets
    });
});

app.MapGet("/api/timesheets/week", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var days = Enumerable.Range(0, 7)
        .Select(offset => start.AddDays(offset))
        .Select(date => new
        {
            date,
            dayName = date.DayOfWeek.ToString(),
            normalHours = 0.00m,
            afterhours = 0.00m
        })
        .ToList();

    var categories = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string categorySql = """
        SELECT category_code, category_name, category_description, utilization_bucket, requires_approval
        FROM non_project_time_categories
        WHERE is_active = TRUE
        ORDER BY display_order, category_name;
        """;

    await using var command = new NpgsqlCommand(categorySql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        categories.Add(new
        {
            code = reader.GetString(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            utilizationBucket = reader.GetString(3),
            requiresApproval = reader.GetBoolean(4)
        });
    }

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = start.AddDays(6),
        days,
        timeTypes = new[] { "normal", "afterhours" },
        nonProjectCategories = categories,
        note = "This is the initial weekly shell. User-specific saved time entries will be added in the next API phase."
    });
});

app.Run();

static IResult? ValidateConfig(DatabaseConfig config)
{
    if (config.Missing.Count == 0)
    {
        return null;
    }

    return Results.BadRequest(new
    {
        status = "configuration_missing",
        missing = config.Missing
    });
}

static DateOnly GetSundayForDate(DateOnly date)
{
    var offset = (int)date.DayOfWeek;
    return date.AddDays(-offset);
}

internal sealed record DatabaseConfig(
    string? Host,
    string? Port,
    string? Database,
    string? Username,
    string? Password,
    IReadOnlyList<string> Missing)
{
    public string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = int.TryParse(Port, out var parsedPort) ? parsedPort : 5432,
                Database = Database,
                Username = Username,
                Password = Password,
                IncludeErrorDetail = false,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 5
            };

            return builder.ConnectionString;
        }
    }

    public static DatabaseConfig FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(host)) missing.Add("PTP_DB_HOST");
        if (string.IsNullOrWhiteSpace(port)) missing.Add("PTP_DB_PORT");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("PTP_DB_NAME");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("PTP_DB_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("PTP_DB_PASSWORD");

        return new DatabaseConfig(host, port, database, username, password, missing);
    }
}
