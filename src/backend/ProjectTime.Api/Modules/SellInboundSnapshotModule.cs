using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class SellInboundSnapshotModule
{
    private const string SystemCode = "SELL";
    private const string SyncMode = "commercial_snapshot_comparison";

    public static WebApplication MapSellInboundSnapshotEndpoints(this WebApplication app)
    {
        app.MapGet("/api/commercial/sell/snapshots/readiness",
            (Func<HttpContext, Task<IResult>>)GetReadinessAsync);
        app.MapGet("/api/commercial/sell/snapshots/projects/{projectId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)GetHistoryAsync);
        app.MapGet("/api/commercial/sell/snapshots/projects/{projectId:guid}/comparison",
            (Func<Guid, HttpContext, Task<IResult>>)GetComparisonAsync);
        app.MapPost("/api/commercial/sell/snapshots/projects/{projectId:guid}/import",
            (Func<Guid, SellSnapshotImportRequest, HttpContext, Task<IResult>>)ImportAsync);
        return app;
    }

    private static Task<IResult> GetReadinessAsync(HttpContext context)
    {
        if (SessionUserId(context) is null) return Task.FromResult(SessionRequired());
        IResult result = Results.Ok(new
        {
            status = "sell_snapshot_comparison_ready",
            snapshotImportEnabled = Flag("PROJECTPULSE_SELL_SNAPSHOT_IMPORT_ENABLED"),
            liveSyncEnabled = Flag("PROJECTPULSE_SELL_LIVE_SYNC_ENABLED"),
            commercialCutoverEnabled = Flag("PROJECTPULSE_SELL_COMMERCIAL_READ_MODEL_ACTIVE"),
            comparisonModeOnly = true,
            persistence = "external_integration_sync_runs.run_metadata_json",
            databaseSchemaChanged = false,
            invoiceCommercialSource = "current_stored_rates"
        });
        return Task.FromResult(result);
    }

    private static async Task<IResult> ImportAsync(
        Guid projectId,
        SellSnapshotImportRequest request,
        HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();
        if (!Flag("PROJECTPULSE_SELL_SNAPSHOT_IMPORT_ENABLED"))
            return Results.Json(new { status = "snapshot_import_disabled" }, statusCode: 409);
        if (Flag("PROJECTPULSE_SELL_COMMERCIAL_READ_MODEL_ACTIVE"))
            return Results.Json(new { status = "guard_violation", message = "SELL cutover must remain disabled." }, statusCode: 409);
        if (request.ProjectId != Guid.Empty && request.ProjectId != projectId)
            return Results.BadRequest(new { status = "project_mismatch" });
        if (string.IsNullOrWhiteSpace(request.QuoteNumber))
            return Results.BadRequest(new { status = "quote_number_required" });
        if ((request.RateLines?.Count ?? 0) == 0)
            return Results.BadRequest(new { status = "rate_lines_required" });

        await using var connection = await OpenAsync();
        if (!await CanAccessProjectAsync(connection, projectId, userId.Value))
            return AccessDenied();

        var current = await SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync(connection, projectId);
        if (!current.Exists) return Results.NotFound(new { status = "project_not_found" });

        var normalized = request with
        {
            ProjectId = projectId,
            QuoteNumber = Clean(request.QuoteNumber),
            QuoteVersion = Clean(request.QuoteVersion),
            QuoteRevision = Clean(request.QuoteRevision),
            CustomerFacingProjectName = Clean(request.CustomerFacingProjectName),
            BillingMethod = Clean(request.BillingMethod),
            Currency = Clean(request.Currency, "USD"),
            RateLines = request.RateLines ?? [],
            Milestones = request.Milestones ?? [],
            SourcePayloadJson = request.SourcePayloadJson ?? string.Empty
        };

        var normalizedJson = JsonSerializer.Serialize(normalized);
        var hashInput = string.IsNullOrWhiteSpace(normalized.SourcePayloadJson)
            ? normalizedJson
            : normalized.SourcePayloadJson;
        var sourceHash = Hash(hashInput);
        var correlationId = $"sell-snapshot-{projectId:N}-{sourceHash[..16]}";
        var comparison = BuildComparison(current, normalized);
        var metadata = JsonSerializer.Serialize(new
        {
            schemaVersion = "projectpulse-sell-commercial-snapshot-v1",
            projectId,
            importedByUserId = userId.Value,
            importedAt = DateTimeOffset.UtcNow,
            sourceHashSha256 = sourceHash,
            snapshot = normalized,
            comparison,
            safeguards = new
            {
                liveSyncEnabled = false,
                commercialCutoverEnabled = false,
                invoiceCommercialSource = "current_stored_rates"
            }
        });

        await using (var exists = new NpgsqlCommand("""
            SELECT external_integration_sync_run_id
            FROM external_integration_sync_runs
            WHERE system_code = @system
              AND correlation_id = @correlation
            LIMIT 1;
            """, connection))
        {
            exists.Parameters.AddWithValue("system", SystemCode);
            exists.Parameters.AddWithValue("correlation", correlationId);
            var prior = await exists.ExecuteScalarAsync();
            if (prior is Guid priorId)
            {
                return Results.Ok(new
                {
                    status = "sell_snapshot_already_imported",
                    syncRunId = priorId,
                    correlationId,
                    sourceHashSha256 = sourceHash,
                    comparisonModeOnly = true,
                    comparison
                });
            }
        }

        Guid runId;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO external_integration_sync_runs (
                system_code,
                sync_direction,
                sync_mode,
                correlation_id,
                run_status,
                records_read,
                records_created,
                records_updated,
                records_failed,
                started_at,
                completed_at,
                error_summary,
                run_metadata_json
            )
            VALUES (
                @system,
                'inbound',
                @mode,
                @correlation,
                'succeeded',
                @records_read,
                1,
                0,
                0,
                NOW(),
                NOW(),
                '',
                @metadata::jsonb
            )
            RETURNING external_integration_sync_run_id;
            """, connection))
        {
            insert.Parameters.AddWithValue("system", SystemCode);
            insert.Parameters.AddWithValue("mode", SyncMode);
            insert.Parameters.AddWithValue("correlation", correlationId);
            insert.Parameters.AddWithValue(
                "records_read",
                normalized.RateLines.Count + normalized.Milestones.Count + 1);
            insert.Parameters.AddWithValue("metadata", metadata);
            runId = (Guid)(await insert.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("SELL snapshot persistence failed."));
        }

        return Results.Ok(new
        {
            status = "sell_snapshot_imported_for_comparison",
            syncRunId = runId,
            correlationId,
            sourceHashSha256 = sourceHash,
            comparisonModeOnly = true,
            commercialCutoverEnabled = false,
            invoiceCommercialSource = "current_stored_rates",
            comparison
        });
    }

    private static async Task<IResult> GetHistoryAsync(Guid projectId, HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();
        await using var connection = await OpenAsync();
        if (!await CanAccessProjectAsync(connection, projectId, userId.Value))
            return AccessDenied();

        var rows = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                external_integration_sync_run_id,
                correlation_id,
                started_at,
                completed_at,
                run_metadata_json::text
            FROM external_integration_sync_runs
            WHERE system_code = @system
              AND sync_mode = @mode
              AND run_metadata_json->>'projectId' = @project
            ORDER BY started_at DESC
            LIMIT 50;
            """, connection);
        command.Parameters.AddWithValue("system", SystemCode);
        command.Parameters.AddWithValue("mode", SyncMode);
        command.Parameters.AddWithValue("project", projectId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            using var document = JsonDocument.Parse(reader.GetString(4));
            rows.Add(new
            {
                syncRunId = reader.GetGuid(0),
                correlationId = reader.GetString(1),
                startedAt = reader.GetFieldValue<DateTimeOffset>(2),
                completedAt = reader.IsDBNull(3)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset?>(3),
                metadata = document.RootElement.Clone()
            });
        }

        return Results.Ok(new
        {
            status = "sell_snapshot_history_loaded",
            projectId,
            count = rows.Count,
            snapshots = rows
        });
    }

    private static async Task<IResult> GetComparisonAsync(Guid projectId, HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();
        await using var connection = await OpenAsync();
        if (!await CanAccessProjectAsync(connection, projectId, userId.Value))
            return AccessDenied();

        var current = await SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync(connection, projectId);
        if (!current.Exists) return Results.NotFound(new { status = "project_not_found" });

        await using var command = new NpgsqlCommand("""
            SELECT
                external_integration_sync_run_id,
                correlation_id,
                started_at,
                run_metadata_json::text
            FROM external_integration_sync_runs
            WHERE system_code = @system
              AND sync_mode = @mode
              AND run_metadata_json->>'projectId' = @project
            ORDER BY started_at DESC
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("system", SystemCode);
        command.Parameters.AddWithValue("mode", SyncMode);
        command.Parameters.AddWithValue("project", projectId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Ok(new
            {
                status = "sell_snapshot_not_available",
                projectId,
                comparisonModeOnly = true,
                invoiceCommercialSource = "current_stored_rates",
                current = new
                {
                    current.SellQuoteNumber,
                    current.CustomerFacingProjectName,
                    current.BillingMethod,
                    current.Rates
                }
            });
        }

        using var document = JsonDocument.Parse(reader.GetString(3));
        return Results.Ok(new
        {
            status = "sell_snapshot_comparison_loaded",
            projectId,
            syncRunId = reader.GetGuid(0),
            correlationId = reader.GetString(1),
            importedAt = reader.GetFieldValue<DateTimeOffset>(2),
            comparisonModeOnly = true,
            commercialCutoverEnabled = false,
            invoiceCommercialSource = "current_stored_rates",
            metadata = document.RootElement.Clone()
        });
    }

    private static object BuildComparison(
        SellCommercialProjectSummary current,
        SellSnapshotImportRequest snapshot)
    {
        var currentRates = current.Rates
            .GroupBy(rate => Clean(rate.SkuCode).ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.First());
        var sellRates = snapshot.RateLines
            .GroupBy(rate => Clean(rate.RateCode).ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.First());

        var rateComparisons = sellRates.Values
            .OrderBy(rate => rate.RateCode)
            .Select(sell =>
            {
                currentRates.TryGetValue(
                    Clean(sell.RateCode).ToUpperInvariant(),
                    out var stored);
                return new
                {
                    rateCode = sell.RateCode,
                    sellDescription = sell.Description,
                    storedDescription = stored?.Description ?? string.Empty,
                    sellUnitRate = sell.UnitRate,
                    storedUnitRate = stored?.UnitRate,
                    difference = stored is null
                        ? (decimal?)null
                        : sell.UnitRate - stored.UnitRate,
                    status = stored is null
                        ? "missing_from_stored_rates"
                        : sell.UnitRate == stored.UnitRate
                            ? "match"
                            : "rate_difference"
                };
            })
            .ToList();

        var missingFromSell = current.Rates
            .Where(rate => !sellRates.ContainsKey(Clean(rate.SkuCode).ToUpperInvariant()))
            .Select(rate => new
            {
                rateCode = rate.SkuCode,
                rate.Description,
                rate.UnitRate
            })
            .ToList();

        var milestoneTotal = snapshot.Milestones.Sum(milestone => milestone.Amount);
        return new
        {
            quoteNumber = new
            {
                stored = current.SellQuoteNumber,
                sell = snapshot.QuoteNumber,
                matches = string.Equals(
                    current.SellQuoteNumber,
                    snapshot.QuoteNumber,
                    StringComparison.OrdinalIgnoreCase)
            },
            projectName = new
            {
                stored = current.CustomerFacingProjectName,
                sell = snapshot.CustomerFacingProjectName,
                matches = string.Equals(
                    current.CustomerFacingProjectName,
                    snapshot.CustomerFacingProjectName,
                    StringComparison.OrdinalIgnoreCase)
            },
            billingMethod = new
            {
                stored = current.BillingMethod,
                sell = snapshot.BillingMethod,
                matches = string.Equals(
                    current.BillingMethod,
                    snapshot.BillingMethod,
                    StringComparison.OrdinalIgnoreCase)
            },
            snapshot.ContractedAmount,
            milestoneTotal,
            milestoneTotalMatchesContract =
                snapshot.ContractedAmount is null
                || milestoneTotal == snapshot.ContractedAmount,
            rateComparisons,
            missingFromSell,
            differenceCount =
                rateComparisons.Count(rate => rate.status != "match")
                + missingFromSell.Count
        };
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
            throw new InvalidOperationException(
                "ProjectPulse database configuration is missing: "
                + string.Join(", ", config.Missing));
        var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        Guid projectId,
        Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM projects project
                WHERE project.project_id = @project_id
                  AND (
                    project.project_manager_user_id = @user_id
                    OR project.project_coordinator_user_id = @user_id
                    OR EXISTS (
                        SELECT 1
                        FROM project_assignments assignment
                        WHERE assignment.project_id = project.project_id
                          AND assignment.user_id = @user_id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments assignment
                        JOIN app_roles role
                          ON role.app_role_id = assignment.app_role_id
                        WHERE assignment.user_id = @user_id
                          AND assignment.is_active = TRUE
                          AND role.is_active = TRUE
                          AND upper(role.role_code) IN (
                            'SUPER_ADMINISTRATOR',
                            'ADMINISTRATOR',
                            'PROJECT_TEAM_COORDINATOR',
                            'ACCOUNTING',
                            'ACCOUNTING_BILLING',
                            'BILLING',
                            'FINANCE',
                            'EXECUTIVE',
                            'PROJECT_MANAGER',
                            'PROJECT_MANAGEMENT_MANAGER'
                          )
                    )
                  )
            );
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", userId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static Guid? SessionUserId(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseSessionUserId", out var stored)
        && stored is Guid userId
            ? userId
            : null;

    private static IResult SessionRequired() => Results.Json(
        new
        {
            status = "session_required",
            message = "A valid ProjectPulse session is required."
        },
        statusCode: StatusCodes.Status401Unauthorized);

    private static IResult AccessDenied() => Results.Json(
        new
        {
            status = "access_denied",
            message = "The project is outside the current user's commercial-data scope."
        },
        statusCode: StatusCodes.Status403Forbidden);

    private static bool Flag(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? string.Empty)
            .Trim()
            .ToLowerInvariant() is "true" or "1" or "yes" or "on";

    private static string Clean(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

public sealed record SellSnapshotImportRequest(
    Guid ProjectId,
    string QuoteNumber,
    string QuoteVersion,
    string QuoteRevision,
    string CustomerFacingProjectName,
    string BillingMethod,
    string Currency,
    decimal? ContractedAmount,
    DateOnly? EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    List<SellSnapshotRateLine>? RateLines,
    List<SellSnapshotMilestone>? Milestones,
    string? SourcePayloadJson);

public sealed record SellSnapshotRateLine(
    string RateCode,
    string Description,
    string LaborCategory,
    string TimeType,
    string UnitType,
    decimal UnitRate,
    bool Billable,
    DateOnly? EffectiveStartDate,
    DateOnly? EffectiveEndDate);

public sealed record SellSnapshotMilestone(
    string MilestoneCode,
    string Name,
    string Description,
    decimal Amount,
    DateOnly? PlannedDate,
    string AcceptanceCriteria);
