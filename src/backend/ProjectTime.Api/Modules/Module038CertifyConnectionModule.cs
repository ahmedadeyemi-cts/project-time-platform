using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private static async Task<IResult> GetCertifyConnectionAsync(HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        var profile = await LoadCertifyProfileAsync(connection);
        return Results.Ok(new
        {
            status = "certify_connection_loaded",
            module = "038",
            moduleName = "Certify Connection & Sync Center",
            canManage = HasRole(actor, CertifyAdminRoles) && !actor.IsViewAs,
            connection = CertifyPayload(profile, HasRole(actor, CertifyAdminRoles)),
            automation = new
            {
                enabled = profile?.AutomaticSyncEnabled == true,
                allowed = profile?.ConnectionStatus == "connected",
                note = "Automatic synchronization can be enabled only after the connection has been tested successfully."
            }
        });
    }

    private static async Task<IResult> UpdateCertifyConnectionAsync(CertifyConnectionUpdateRequest request, HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs) return ViewAsReadOnly();
        if (!HasRole(actor, CertifyAdminRoles)) return AccessDenied("Certify connection configuration requires Accounting or Super Administrator access.");

        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? DefaultCertifyBaseUrl : request.BaseUrl.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Results.BadRequest(new { status = "invalid_certify_url", message = "Certify base URL must be an absolute HTTPS URL." });
        var keyEnvironment = NormalizeEnvironmentVariable(request.ApiKeyEnvironmentName, "PROJECTPULSE_CERTIFY_API_KEY");
        var secretEnvironment = NormalizeEnvironmentVariable(request.ApiSecretEnvironmentName, "PROJECTPULSE_CERTIFY_API_SECRET");
        var cadence = request.AutomaticSyncEnabled
            ? request.SyncCadence?.Trim().ToLowerInvariant() is "hourly" ? "hourly" : "nightly"
            : "manual";

        const string sql = """
            UPDATE certify_connection_profiles
            SET environment_name=@environment, base_url=@base_url,
                api_key_environment_name=@key_environment,
                api_secret_environment_name=@secret_environment,
                company_id=@company_id,
                automatic_sync_enabled=CASE WHEN connection_status='connected' THEN @automatic ELSE FALSE END,
                sync_cadence=CASE WHEN connection_status='connected' AND @automatic THEN @cadence ELSE 'manual' END,
                connection_status=CASE
                    WHEN @key_ready AND @secret_ready THEN CASE WHEN connection_status='connected' THEN 'connected' ELSE 'configured' END
                    ELSE 'not_configured' END,
                configured_by_user_id=@actor_id, updated_at=NOW()
            WHERE profile_name='default';
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("environment", request.EnvironmentName?.Equals("production", StringComparison.OrdinalIgnoreCase) == true ? "production" : "test");
        command.Parameters.AddWithValue("base_url", EnsureTrailingSlash(uri.ToString()));
        command.Parameters.AddWithValue("key_environment", keyEnvironment);
        command.Parameters.AddWithValue("secret_environment", secretEnvironment);
        command.Parameters.AddWithValue("company_id", request.CompanyId?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("automatic", request.AutomaticSyncEnabled);
        command.Parameters.AddWithValue("cadence", cadence);
        command.Parameters.AddWithValue("key_ready", !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(keyEnvironment)));
        command.Parameters.AddWithValue("secret_ready", !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(secretEnvironment)));
        command.Parameters.AddWithValue("actor_id", actor.ActualUserId);
        await command.ExecuteNonQueryAsync();

        var profile = await LoadCertifyProfileAsync(connection);
        return Results.Ok(new
        {
            status = "certify_connection_saved",
            message = "Certify connection metadata was saved. Secret values remain in environment configuration and are never returned.",
            connection = CertifyPayload(profile, true)
        });
    }

    private static async Task<IResult> TestCertifyConnectionAsync(HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs) return ViewAsReadOnly();
        if (!HasRole(actor, CertifyAdminRoles)) return AccessDenied("Certify connection testing requires Accounting or Super Administrator access.");
        var profile = await LoadCertifyProfileAsync(connection);
        if (profile is null) return Results.Json(new { status = "certify_migration_required", message = "Apply migration 044 before configuring Certify." }, statusCode: 503);

        var result = await CallCertifyAsync(profile, "expensecategories?limit=1", context.RequestAborted);
        await using var command = new NpgsqlCommand("""
            UPDATE certify_connection_profiles
            SET connection_status=@status, last_tested_at=NOW(), last_test_result=@detail,
                automatic_sync_enabled=CASE WHEN @connected THEN automatic_sync_enabled ELSE FALSE END,
                sync_cadence=CASE WHEN @connected THEN sync_cadence ELSE 'manual' END,
                updated_at=NOW()
            WHERE profile_name='default';
            """, connection);
        command.Parameters.AddWithValue("status", result.Success ? "connected" : "failed");
        command.Parameters.AddWithValue("detail", result.Message);
        command.Parameters.AddWithValue("connected", result.Success);
        await command.ExecuteNonQueryAsync();

        return Results.Json(new
        {
            status = result.Success ? "certify_connection_connected" : "certify_connection_failed",
            message = result.Message,
            observedAt = DateTimeOffset.UtcNow,
            secretsReturned = false
        }, statusCode: result.Success ? 200 : 502);
    }

    private static async Task<IResult> ImportFromCertifyAsync(CertifyImportRequest request, HttpContext context)
    {
        if (request.ProjectId == Guid.Empty || request.ExpenseOwnerUserId == Guid.Empty)
            return Results.BadRequest(new { status = "project_and_owner_required", message = "Select a customer, project, and expense owner before importing." });
        if (string.IsNullOrWhiteSpace(request.CertifyReportId))
            return Results.BadRequest(new { status = "certify_report_required", message = "Enter a Certify expense report ID." });

        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        var project = await LoadProjectAsync(connection, request.ProjectId);
        if (project is null) return Results.NotFound(new { status = "project_not_found", message = "The selected project no longer exists." });
        var authorization = await AuthorizeUploadAsync(connection, null, actor, project, request.ExpenseOwnerUserId);
        if (authorization is not null) return authorization;
        var profile = await LoadCertifyProfileAsync(connection);
        if (profile?.ConnectionStatus != "connected")
            return Results.Conflict(new { status = "certify_not_connected", message = "Complete and test the Module 038 Certify connection before importing." });

        var runId = Guid.NewGuid();
        await using (var start = new NpgsqlCommand("""
            INSERT INTO certify_expense_import_runs (
                certify_expense_import_run_id, certify_connection_profile_id, project_id,
                expense_owner_user_id, initiated_by_user_id, import_status,
                certify_report_id, request_metadata
            ) VALUES (@run_id, @profile_id, @project_id, @owner_id, @actor_id, 'started', @report_id, @metadata::jsonb);
            """, connection))
        {
            start.Parameters.AddWithValue("run_id", runId);
            start.Parameters.AddWithValue("profile_id", profile.Id);
            start.Parameters.AddWithValue("project_id", project.ProjectId);
            start.Parameters.AddWithValue("owner_id", request.ExpenseOwnerUserId);
            start.Parameters.AddWithValue("actor_id", actor.ActualUserId);
            start.Parameters.AddWithValue("report_id", request.CertifyReportId.Trim());
            start.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new { request.PeriodStart, request.PeriodEnd }));
            await start.ExecuteNonQueryAsync();
        }

        var call = await CallCertifyAsync(profile, $"expensereports/{Uri.EscapeDataString(request.CertifyReportId.Trim())}/expenses", context.RequestAborted);
        if (!call.Success || call.Json is null)
        {
            await CompleteCertifyRunAsync(connection, runId, "failed", null, call.Message, new { call.StatusCode });
            return Results.Json(new { status = "certify_import_failed", message = call.Message, importRunId = runId }, statusCode: 502);
        }

        ParsedExpenseFile parsed;
        try { parsed = ParseCertifyResponse(call.Json.Value, request.CertifyReportId.Trim(), request.PeriodStart, request.PeriodEnd); }
        catch (Exception exception)
        {
            await CompleteCertifyRunAsync(connection, runId, "failed", null, exception.Message, new { responseKind = call.Json.Value.ValueKind.ToString() });
            return Results.BadRequest(new { status = "certify_response_unrecognized", message = exception.Message, importRunId = runId });
        }

        var bytes = Encoding.UTF8.GetBytes(call.Json.Value.GetRawText());
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var uploadId = await PersistUploadAsync(connection, actor, project, request.ExpenseOwnerUserId,
            "certify", "certify_api", request.CertifyReportId.Trim(), null,
            "application/json", bytes, hash, parsed,
            new { certifyReportId = request.CertifyReportId.Trim(), certifyImportRunId = runId });
        await CompleteCertifyRunAsync(connection, runId, "completed", uploadId, string.Empty, new { lineCount = parsed.Lines.Count, parsed.TotalAmount });
        await using (var profileCommand = new NpgsqlCommand("UPDATE certify_connection_profiles SET last_successful_sync_at=NOW(), updated_at=NOW() WHERE profile_name='default';", connection))
            await profileCommand.ExecuteNonQueryAsync();
        var notification = await DeliverExpenseNotificationAsync(connection, uploadId, actor.ActualUserId);

        return Results.Ok(new
        {
            status = "certify_expense_import_completed",
            message = $"Imported {parsed.Lines.Count} Certify expense line(s) totaling {parsed.TotalAmount:C}.",
            importRunId = runId,
            uploadId,
            lineCount = parsed.Lines.Count,
            parsed.TotalAmount,
            parsed.ReimbursableAmount,
            billingTreatment = BillingTreatment(project.ContractType),
            notification
        });
    }

    private static async Task<CertifyProfile?> LoadCertifyProfileAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT certify_connection_profile_id, environment_name, base_url,
                   api_key_environment_name, api_secret_environment_name, company_id,
                   connection_status, automatic_sync_enabled, sync_cadence,
                   last_tested_at, last_test_result, last_successful_sync_at
            FROM certify_connection_profiles WHERE profile_name='default';
            """, connection);
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return new CertifyProfile(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetBoolean(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11));
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01") { return null; }
    }

    private static object CertifyPayload(CertifyProfile? profile, bool canManage) => profile is null
        ? new { status = "migration_required", canManage, secretsReturned = false }
        : new
        {
            status = profile.ConnectionStatus,
            profile.EnvironmentName,
            profile.BaseUrl,
            profile.ApiKeyEnvironmentName,
            apiKeyConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentName)),
            profile.ApiSecretEnvironmentName,
            apiSecretConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.ApiSecretEnvironmentName)),
            profile.CompanyId,
            profile.AutomaticSyncEnabled,
            profile.SyncCadence,
            profile.LastTestedAt,
            profile.LastTestResult,
            profile.LastSuccessfulSyncAt,
            canManage,
            secretsReturned = false
        };

    private static async Task<CertifyCall> CallCertifyAsync(CertifyProfile profile, string relativePath, CancellationToken cancellationToken)
    {
        var key = Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentName);
        var secret = Environment.GetEnvironmentVariable(profile.ApiSecretEnvironmentName);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            return new CertifyCall(false, 0, "The configured Certify API key or secret environment value is missing.", null);
        using var client = new HttpClient { BaseAddress = new Uri(EnsureTrailingSlash(profile.BaseUrl)), Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}")));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Certify-API-Key", key);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Certify-API-Secret", secret);
        if (!string.IsNullOrWhiteSpace(profile.CompanyId)) client.DefaultRequestHeaders.TryAddWithoutValidation("X-Certify-Company-ID", profile.CompanyId);
        try
        {
            using var response = await client.GetAsync(relativePath, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonElement? json = null;
            try { if (!string.IsNullOrWhiteSpace(raw)) json = JsonDocument.Parse(raw).RootElement.Clone(); } catch { }
            return new CertifyCall(response.IsSuccessStatusCode, (int)response.StatusCode,
                response.IsSuccessStatusCode ? "Certify connection responded successfully." : $"Certify returned HTTP {(int)response.StatusCode}.", json);
        }
        catch (Exception exception) { return new CertifyCall(false, 0, exception.Message, null); }
    }

    private static async Task CompleteCertifyRunAsync(NpgsqlConnection connection, Guid runId, string status, Guid? uploadId, string error, object metadata)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE certify_expense_import_runs
            SET import_status=@status, imported_upload_id=@upload_id,
                error_detail=@error, response_metadata=@metadata::jsonb, completed_at=NOW()
            WHERE certify_expense_import_run_id=@run_id;
            """, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.Add(new Npgsql.NpgsqlParameter("upload_id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = uploadId is null ? DBNull.Value : uploadId.Value });
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static string NormalizeEnvironmentVariable(string? value, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
        if (!result.All(character => char.IsLetterOrDigit(character) || character == '_'))
            throw new InvalidOperationException("Secret environment names may contain only letters, numbers, and underscores.");
        return result;
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
}
