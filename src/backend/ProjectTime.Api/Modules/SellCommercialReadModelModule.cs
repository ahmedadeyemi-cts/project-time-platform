using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class SellCommercialReadModelModule
{
    public static WebApplication MapSellCommercialReadModelEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/commercial/sell/readiness",
            (Func<HttpContext, Task<IResult>>)GetReadinessAsync);

        app.MapGet(
            "/api/commercial/sell/projects/{projectId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)GetProjectAsync);

        return app;
    }

    internal static async Task<SellCommercialProjectSummary> LoadProjectCommercialSummaryAsync(
        NpgsqlConnection connection,
        Guid projectId,
        NpgsqlTransaction? transaction = null)
    {
        SellCommercialProjectSource? project = null;

        await using (var command = new NpgsqlCommand("""
            SELECT
                p.project_id,
                p.client_id,
                COALESCE(c.client_name, ''),
                COALESCE(p.project_code, ''),
                COALESCE(p.project_name, ''),
                COALESCE(p.sell_quote_number, ''),
                COALESCE(p.contract_type, ''),
                profile.default_rate_card_id
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN project_billing_profiles profile ON profile.project_id = p.project_id
            WHERE p.project_id = @project_id;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                project = new SellCommercialProjectSource(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetGuid(7));
            }
        }

        if (project is null)
        {
            return SellCommercialProjectSummary.Missing(projectId);
        }

        var connector = await LoadConnectorAsync(connection, transaction);
        var card = await LoadCommercialRateCardAsync(connection, project, transaction);
        var rates = card is null
            ? []
            : await LoadCommercialRatesAsync(connection, card.RateCardId, transaction);

        var liveSyncEnabled = ReadFlag("PROJECTPULSE_SELL_LIVE_SYNC_ENABLED");
        var cutoverEnabled = ReadFlag("PROJECTPULSE_SELL_COMMERCIAL_READ_MODEL_ACTIVE");
        var connectorReady = connector is not null
            && connector.InboundEnabled
            && connector.ConnectionStatus is "configured" or "connected"
            && connector.LastSuccessfulSyncAt is not null;
        var quoteReady = !string.IsNullOrWhiteSpace(project.SellQuoteNumber);
        var rateReady = card is not null && rates.Count > 0;
        var cutoverReady = connectorReady && quoteReady && rateReady;
        var source = cutoverEnabled && cutoverReady
            ? "SELL"
            : "current_stored_rates";
        var readiness = !connectorReady
            ? "sell_connector_not_ready"
            : !quoteReady
                ? "sell_quote_missing"
                : !rateReady
                    ? "commercial_rate_missing"
                    : cutoverEnabled
                        ? "sell_active"
                        : "sell_ready_for_guarded_cutover";
        var billingMethod = BillingMethod(project.ContractType);
        var milestoneReadiness = billingMethod is "fixed_fee_milestone" or "hybrid_time_and_milestone"
            ? "milestone_schedule_required_not_configured"
            : "not_required_for_time_and_materials";

        return new SellCommercialProjectSummary(
            project.ProjectId,
            project.CustomerName,
            project.ProjectCode,
            project.ProjectName,
            project.SellQuoteNumber,
            project.ContractType,
            billingMethod,
            source,
            readiness,
            connectorReady,
            liveSyncEnabled,
            cutoverEnabled,
            cutoverReady,
            connector?.LastSuccessfulSyncAt,
            card,
            rates,
            milestoneReadiness);
    }

    private static async Task<IResult> GetReadinessAsync(HttpContext context)
    {
        if (SessionUserId(context) is null) return SessionRequired();
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
        {
            return Results.BadRequest(new { status = "configuration_missing", missing = config.Missing });
        }

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        var connector = await LoadConnectorAsync(connection);
        var liveSyncEnabled = ReadFlag("PROJECTPULSE_SELL_LIVE_SYNC_ENABLED");
        var cutoverEnabled = ReadFlag("PROJECTPULSE_SELL_COMMERCIAL_READ_MODEL_ACTIVE");

        return Results.Ok(new
        {
            status = "sell_commercial_read_model_ready",
            implementation = "existing_project_and_rate_card_tables",
            databaseSchemaChanged = false,
            connector = connector ?? SellConnectorSummary.NotRegistered,
            liveSyncEnabled,
            commercialCutoverEnabled = cutoverEnabled,
            currentInvoiceRateSource = cutoverEnabled ? "SELL_when_project_ready" : "current_stored_rates",
            cutoverRequirements = new[]
            {
                "SELL connector configured or connected",
                "inbound synchronization enabled",
                "successful synchronization timestamp present",
                "SELL quote number present on project",
                "active effective commercial rate card with billable rate lines"
            },
            milestoneBilling = "deferred_to_structured_milestone_module"
        });
    }

    private static async Task<IResult> GetProjectAsync(Guid projectId, HttpContext context)
    {
        var userId = SessionUserId(context);
        if (userId is null) return SessionRequired();
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
        {
            return Results.BadRequest(new { status = "configuration_missing", missing = config.Missing });
        }

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        if (!await CanAccessProjectAsync(connection, projectId, userId.Value))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "The requested project is outside the current user's commercial-data scope."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var commercial = await LoadProjectCommercialSummaryAsync(connection, projectId);
        return commercial.Exists
            ? Results.Ok(new { status = "sell_commercial_project_loaded", commercial })
            : Results.NotFound(new { status = "project_not_found" });
    }

    private static async Task<SellConnectorSummary?> LoadConnectorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                system_code,
                display_name,
                connection_status,
                inbound_enabled,
                outbound_enabled,
                last_connection_test_status,
                last_connection_test_at,
                last_successful_sync_at
            FROM external_integration_connections
            WHERE lower(system_code) = 'sell'
               OR lower(display_name) LIKE '%sell%'
            ORDER BY CASE WHEN lower(system_code) = 'sell' THEN 0 ELSE 1 END
            LIMIT 1;
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new SellConnectorSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : ReadDateTimeOffset(reader, 6),
            reader.IsDBNull(7) ? null : ReadDateTimeOffset(reader, 7));
    }

    private static async Task<SellCommercialRateCardSummary?> LoadCommercialRateCardAsync(
        NpgsqlConnection connection,
        SellCommercialProjectSource project,
        NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                card.rate_card_id,
                card.rate_card_code,
                card.rate_card_name,
                card.status,
                card.effective_start_date,
                card.effective_end_date,
                COALESCE(card.client_id = @client_id, FALSE)
            FROM work_rate_cards card
            WHERE lower(card.status) IN ('active', 'published', 'approved')
              AND card.effective_start_date <= CURRENT_DATE
              AND (card.effective_end_date IS NULL OR card.effective_end_date >= CURRENT_DATE)
              AND (
                    card.rate_card_id = @default_rate_card_id
                    OR (
                        @default_rate_card_id IS NULL
                        AND (card.client_id = @client_id OR card.client_id IS NULL)
                    )
              )
            ORDER BY
                (card.rate_card_id = @default_rate_card_id) DESC,
                COALESCE(card.client_id = @client_id, FALSE) DESC,
                card.effective_start_date DESC,
                card.rate_card_name
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.Add("client_id", NpgsqlDbType.Uuid).Value =
            project.ClientId is null ? DBNull.Value : project.ClientId.Value;
        command.Parameters.Add("default_rate_card_id", NpgsqlDbType.Uuid).Value =
            project.DefaultRateCardId is null ? DBNull.Value : project.DefaultRateCardId.Value;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new SellCommercialRateCardSummary(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ReadDateOnly(reader, 4),
            reader.IsDBNull(5) ? null : ReadDateOnly(reader, 5),
            reader.GetBoolean(6));
    }

    private static async Task<List<SellCommercialRateSummary>> LoadCommercialRatesAsync(
        NpgsqlConnection connection,
        Guid rateCardId,
        NpgsqlTransaction? transaction)
    {
        var rates = new List<SellCommercialRateSummary>();
        await using var command = new NpgsqlCommand("""
            SELECT
                rate_line_id,
                sku_code,
                display_name,
                description,
                labor_category,
                time_type,
                unit_type,
                rate_amount,
                billable_default
            FROM work_rate_card_lines
            WHERE rate_card_id = @rate_card_id
              AND is_active = TRUE
            ORDER BY display_order, display_name;
            """, connection, transaction);
        command.Parameters.AddWithValue("rate_card_id", rateCardId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rates.Add(new SellCommercialRateSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetBoolean(8)));
        }
        return rates;
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
                          SELECT 1 FROM project_assignments assignment
                          WHERE assignment.project_id = project.project_id
                            AND assignment.user_id = @user_id
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM app_user_role_assignments assignment
                          JOIN app_roles role ON role.app_role_id = assignment.app_role_id
                          WHERE assignment.user_id = @user_id
                            AND assignment.is_active = TRUE
                            AND role.is_active = TRUE
                            AND upper(role.role_code) IN (
                                'SUPER_ADMINISTRATOR','ADMINISTRATOR','PROJECT_TEAM_COORDINATOR',
                                'ACCOUNTING','ACCOUNTING_BILLING','BILLING','FINANCE','EXECUTIVE'
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
        context.Items.TryGetValue("ProjectPulseSessionUserId", out var value) && value is Guid userId
            ? userId
            : null;

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid ProjectPulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static bool ReadFlag(string name) =>
        (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim().ToLowerInvariant()
            is "true" or "1" or "yes" or "on";

    private static string BillingMethod(string contractType)
    {
        var value = (contractType ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Contains("hybrid")) return "hybrid_time_and_milestone";
        if (value.Contains("fixed") || value.Contains("milestone")) return "fixed_fee_milestone";
        if (value.Contains("non-bill") || value.Contains("non bill")) return "non_billable";
        return "time_and_materials";
    }

    private static DateOnly ReadDateOnly(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }
}

internal sealed record SellCommercialProjectSource(
    Guid ProjectId,
    Guid? ClientId,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string SellQuoteNumber,
    string ContractType,
    Guid? DefaultRateCardId);

internal sealed record SellConnectorSummary(
    string SystemCode,
    string DisplayName,
    string ConnectionStatus,
    bool InboundEnabled,
    bool OutboundEnabled,
    string LastConnectionTestStatus,
    DateTimeOffset? LastConnectionTestAt,
    DateTimeOffset? LastSuccessfulSyncAt)
{
    public static SellConnectorSummary NotRegistered => new(
        "SELL", "SELL", "not_registered", false, false, "not_tested", null, null);
}

internal sealed record SellCommercialRateCardSummary(
    Guid RateCardId,
    string RateCardCode,
    string RateCardName,
    string Status,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    bool IsCustomerSpecific);

internal sealed record SellCommercialRateSummary(
    Guid RateLineId,
    string SkuCode,
    string DisplayName,
    string Description,
    string LaborCategory,
    string TimeType,
    string UnitType,
    decimal UnitRate,
    bool BillableDefault);

internal sealed record SellCommercialProjectSummary(
    Guid ProjectId,
    string CustomerName,
    string ProjectCode,
    string CustomerFacingProjectName,
    string SellQuoteNumber,
    string ContractType,
    string BillingMethod,
    string CommercialSource,
    string ReadinessStatus,
    bool ConnectorReady,
    bool LiveSyncEnabled,
    bool CommercialCutoverEnabled,
    bool CutoverReady,
    DateTimeOffset? LastSuccessfulSyncAt,
    SellCommercialRateCardSummary? RateCard,
    IReadOnlyList<SellCommercialRateSummary> Rates,
    string MilestoneReadiness)
{
    public bool Exists => !string.IsNullOrWhiteSpace(ProjectCode)
        || !string.IsNullOrWhiteSpace(CustomerFacingProjectName);

    public static SellCommercialProjectSummary Missing(Guid projectId) => new(
        projectId, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        "unknown", "current_stored_rates", "project_not_found", false, false, false,
        false, null, null, [], "unknown");
}
