using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Read-only ConnectWise SELL customer-sync readiness and history.
/// The former contacts importer does not implement the CPQ quote-customer contract.
/// Fail closed until a dedicated adapter exists; never relabel or overwrite retained lineage.
/// </summary>
public static class CustomerDirectorySellSyncModule
{
    private const string ModuleNumber = "021";
    private const string ProviderModuleNumber = "026";
    private const string ProviderKey = ConnectWiseSellContract.ProviderKey;
    private const string SourceSystem = "CONNECTWISE_SELL";
    private const string MigrationId = "049_module_021_sell_customer_sync";
    private static readonly Uri SellBaseUri = new(ConnectWiseSellContract.BaseUrl);

    private static readonly string[] ViewRoles =
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "INTEGRATION_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR", "SALES",
        "ACCOUNT_EXECUTIVE", "ACCOUNT_EXECUTIVES", "INSIDE_SALES",
        "SOLUTION_ARCHITECT", "SA", "SAA"
    };

    private static readonly string[] ManageRoles =
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "INTEGRATION_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR"
    };

    public static WebApplication MapCustomerDirectorySellSyncEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/customers/sell/status",
            (Func<HttpContext, Task<IResult>>)GetStatusAsync);
        app.MapPost(
            "/api/customers/sell/preview",
            (Func<HttpContext, IHttpClientFactory, Task<IResult>>)PreviewAsync);
        app.MapPost(
            "/api/customers/sell/import",
            (Func<HttpContext, IHttpClientFactory, Task<IResult>>)ImportAsync);
        app.MapGet(
            "/api/customers/sell/runs",
            (Func<HttpContext, Task<IResult>>)GetRunsAsync);
        return app;
    }

    private static async Task<IResult> GetStatusAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var schema = await ReadSchemaStatusAsync(connection, context.RequestAborted);
        if (!schema.ProviderReady) return ProviderSchemaUnavailable();
        if (!schema.SyncReady) return SyncSchemaUnavailable();

        var provider = await ReadProviderAsync(connection, context.RequestAborted);
        var linkedCustomers = await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM customer_directory_source_links WHERE source_system = 'CONNECTWISE_SELL';",
            context.RequestAborted);
        var lastRun = await ReadLastRunAsync(connection, context.RequestAborted);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "sell_customer_sync_status_loaded",
            providerModule = ProviderModuleNumber,
            providerKey = ProviderKey,
            sourceSystem = SourceSystem,
            authoritativeCustomerSource = true,
            localContactEnrichmentPreserved = true,
            provider = provider is null
                ? new
                {
                    configured = false,
                    name = ConnectWiseSellContract.DisplayName,
                    authModel = string.Empty,
                    enabled = false,
                    availabilityStatus = "not_configured",
                    credentialConfigured = false,
                    baseUrl = SellBaseUri.GetLeftPart(UriPartial.Authority)
                }
                : new
                {
                    configured = true,
                    name = provider.ProviderName,
                    authModel = provider.AuthModel,
                    enabled = provider.IsEnabled,
                    availabilityStatus = provider.AvailabilityStatus,
                    credentialConfigured = provider.CredentialConfigured,
                    baseUrl = SellBaseUri.GetLeftPart(UriPartial.Authority)
                },
            customerSyncAvailable = false,
            customerSyncMessage = ConnectWiseSellContract.CustomerSyncMessage,
            linkedCustomers,
            lastRun,
            migration = MigrationId,
            secretValuesReturned = false
        });
    }

    private static async Task<IResult> GetRunsAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        var schema = await ReadSchemaStatusAsync(connection, context.RequestAborted);
        if (!schema.SyncReady) return SyncSchemaUnavailable();

        await using var command = new NpgsqlCommand("""
            SELECT customer_directory_sync_run_id, started_at, completed_at, status,
                   page_requested, page_size, search_text, source_records_seen,
                   organizations_seen, imported_count, updated_count, linked_count,
                   skipped_count, failed_count, error_code, message
            FROM customer_directory_sync_runs
            WHERE provider_key = @provider
            ORDER BY started_at DESC
            LIMIT 25;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        var runs = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            runs.Add(new
            {
                runId = reader.GetGuid(0),
                startedAt = reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime(),
                completedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime(),
                status = reader.GetString(3),
                page = reader.GetInt32(4),
                pageSize = reader.GetInt32(5),
                search = reader.GetString(6),
                sourceRecordsSeen = reader.GetInt32(7),
                organizationsSeen = reader.GetInt32(8),
                imported = reader.GetInt32(9),
                updated = reader.GetInt32(10),
                linked = reader.GetInt32(11),
                skipped = reader.GetInt32(12),
                failed = reader.GetInt32(13),
                errorCode = reader.GetString(14),
                message = reader.GetString(15)
            });
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "sell_customer_sync_runs_loaded",
            providerKey = ProviderKey,
            runs,
            secretValuesReturned = false
        });
    }

    private static async Task<SellProvider?> ReadProviderAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT provider_name, auth_model, base_url, api_key_header, api_key_prefix,
                   is_enabled, availability_status,
                   EXISTS (
                       SELECT 1 FROM crm_integration_credentials credential
                       WHERE credential.provider_key = provider.provider_key
                         AND credential.credential_kind = CASE
                             WHEN provider.auth_model = 'api_key' THEN 'api_key'
                             ELSE 'oauth_token'
                         END
                   ) AS credential_configured
            FROM crm_integration_providers provider
            WHERE provider.provider_key = @provider;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetBoolean(5),
            reader.GetString(6),
            reader.GetBoolean(7));
    }

    private static async Task<SchemaStatus> ReadSchemaStatusAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('public.crm_integration_providers') IS NOT NULL
                AND to_regclass('public.crm_integration_credentials') IS NOT NULL,
                to_regclass('public.clients') IS NOT NULL
                AND to_regclass('public.customer_directory_source_links') IS NOT NULL
                AND to_regclass('public.customer_directory_sync_runs') IS NOT NULL;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetBoolean(0), reader.GetBoolean(1))
            : new(false, false);
    }

    private static async Task<object?> ReadLastRunAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT customer_directory_sync_run_id, started_at, completed_at, status,
                   imported_count, updated_count, linked_count, skipped_count,
                   failed_count, error_code, message
            FROM customer_directory_sync_runs
            WHERE provider_key = @provider
            ORDER BY started_at DESC
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("provider", ProviderKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new
        {
            runId = reader.GetGuid(0),
            startedAt = reader.GetFieldValue<DateTimeOffset>(1).ToUniversalTime(),
            completedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime(),
            status = reader.GetString(3),
            imported = reader.GetInt32(4),
            updated = reader.GetInt32(5),
            linked = reader.GetInt32(6),
            skipped = reader.GetInt32(7),
            failed = reader.GetInt32(8),
            errorCode = reader.GetString(9),
            message = reader.GetString(10)
        };
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IResult?> AuthorizeViewAsync(HttpContext context) =>
        await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ViewRoles,
            ["VIEW_CUSTOMERS", "MANAGE_CUSTOMERS", "VIEW_INTEGRATIONS_026", "MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]);

    private static async Task<IResult?> AuthorizeManageAsync(HttpContext context)
    {
        if (IsViewAs(context))
            return Results.Json(new { module = ModuleNumber, status = "view_as_read_only", message = "Exit Administrator View-As before importing ConnectWise SELL customers." }, statusCode: StatusCodes.Status403Forbidden);
        return await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ManageRoles,
            ["MANAGE_CUSTOMERS", "MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]);
    }

    private static async Task<NpgsqlConnection?> OpenConnectionAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            return connection;
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "open customer synchronization storage");
            return null;
        }
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
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

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;

    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var values)) return true;
        if (!Uri.TryCreate(values.ToString(), UriKind.Absolute, out var origin)) return false;
        return string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80))
            && string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static IResult OriginRejected() => Results.Json(new { module = ModuleNumber, status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult DependencyUnavailable() => Results.Json(new { module = ModuleNumber, status = "customer_sync_storage_unavailable", message = "Customer synchronization storage is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult ProviderSchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "integration_schema_unavailable", migration = "034_module_026_crm_erp_integrations", message = "Module 026 integration storage is not installed." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult SyncSchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "customer_sync_schema_unavailable", migration = MigrationId, message = "The Module 021 ConnectWise SELL customer synchronization migration has not been applied." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CustomerDirectorySellSyncModule")
            .LogWarning("Modules 021/026 could not {Operation} ({ExceptionType}).", operation, exception.GetType().Name);
    }

    private static async Task<IResult> PreviewAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();
        return CustomerAdapterRequired();
    }

    private static async Task<IResult> ImportAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();
        return CustomerAdapterRequired();
    }

    private static IResult CustomerAdapterRequired() => Results.Json(new
    {
        module = ModuleNumber,
        status = "connectwise_sell_customer_adapter_required",
        message = ConnectWiseSellContract.CustomerSyncMessage,
        customerSyncAvailable = false
    }, statusCode: StatusCodes.Status409Conflict);

    private sealed record SchemaStatus(bool ProviderReady, bool SyncReady);
    private sealed record SellProvider(string ProviderName, string AuthModel, string BaseUrl,
        string ApiKeyHeader, string ApiKeyPrefix, bool IsEnabled, string AvailabilityStatus,
        bool CredentialConfigured);
}
