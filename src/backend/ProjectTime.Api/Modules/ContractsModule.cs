using Npgsql;

namespace ProjectTime.Api.Modules;

public static class ContractsModule
{
    private static readonly string[] ViewRoleCodes =
    {
        "PROJECT_TEAM_COORDINATOR",
        "SALES",
        "ACCOUNT_EXECUTIVE",
        "EXECUTIVE",
        "EXECUTIVE_LEADERSHIP",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR"
    };

    private static readonly string[] ManageRoleCodes =
    {
        "PROJECT_TEAM_COORDINATOR"
    };

    public static WebApplication MapContractsEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/contracts/overview",
            GetOverviewAsync);

        app.MapGet(
            "/api/contracts/help",
            GetHelp);

        app.MapPost(
            "/api/contracts",
            CreateContractAsync);

        app.MapPost(
            "/api/contracts/{contractId:guid}/credits",
            AddCreditAsync);

        app.MapPost(
            "/api/contracts/{contractId:guid}/extensions",
            ExtendContractAsync);

        app.MapPost(
            "/api/contracts/{contractId:guid}/notes",
            AddNoteAsync);

        app.MapPut(
            "/api/contracts/email-schedule",
            UpdateScheduleAsync);

        app.MapGet(
            "/api/contracts/report-preview",
            GetReportPreviewAsync);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Results.Json(
                new
                {
                    status = "session_required",
                    message = "A ProjectPulse session is required."
                },
                statusCode: 401);
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        var access = await LoadAccessAsync(
            connection,
            actor.Value);

        if (!access.CanView)
        {
            return Results.Json(
                new
                {
                    status = "forbidden",
                    message =
                        "Contracts are available to Sales, "
                        + "Executive, and Project Team Coordinator users."
                },
                statusCode: 403);
        }

        var contracts = await LoadContractsAsync(connection);
        var customers = await LoadCustomersAsync(connection);
        var accountExecutives = await LoadAccountExecutivesAsync(
            connection);
        var coordinators = await LoadCoordinatorsAsync(connection);
        var schedule = await LoadScheduleAsync(connection);

        return Results.Ok(new
        {
            status = "contracts_loaded",
            module = "060 Contracts / Block of Hours",
            canManage = access.CanManage,
            summary = new
            {
                activeContracts = contracts.Count(item =>
                    item.Status == "active"
                    || item.Status == "low_balance"
                    || item.Status == "expiring"),
                purchasedHours = contracts.Sum(item =>
                    item.PurchasedHours),
                creditAwarded = contracts.Sum(item =>
                    item.CreditAwarded),
                totalAvailableHours = contracts.Sum(item =>
                    item.TotalAvailableHours),
                consumedHours = contracts.Sum(item =>
                    item.ConsumedHours),
                remainingBalance = contracts.Sum(item =>
                    item.RemainingBalance),
                lowBalanceContracts = contracts.Count(item =>
                    item.BalancePercent <= 25
                    && item.RemainingBalance > 0),
                expiringContracts = contracts.Count(item =>
                    item.DaysUntilExpiration >= 0
                    && item.DaysUntilExpiration <= 90),
                expiredContracts = contracts.Count(item =>
                    item.DaysUntilExpiration < 0),
                exhaustedContracts = contracts.Count(item =>
                    item.RemainingBalance <= 0)
            },
            contracts,
            customers,
            accountExecutives,
            coordinators,
            schedule,
            formulas = FormulaDefinitions(),
            smtp = new
            {
                provider = "global_smtp",
                credentialsManagedGlobally = true,
                moduleSpecificCredentials = false
            },
            report = new
            {
                format = "xlsx",
                csvAllowed = false,
                groupedByAccountExecutive = true,
                filtersEnabled = true,
                frozenPanes = true,
                sheets = new[]
                {
                    "AE Summary",
                    "BoH Balance Detail",
                    "One worksheet per AE",
                    "Usage Detail",
                    "Credits and Extensions",
                    "Report Information"
                }
            }
        });
    }

    private static IResult GetHelp(HttpContext context)
    {
        if (SessionUserId(context) is null)
        {
            return Results.Json(
                new
                {
                    status = "session_required",
                    message = "A ProjectPulse session is required."
                },
                statusCode: 401);
        }

        return Results.Ok(new
        {
            status = "contracts_help_loaded",
            title = "Contracts / Block of Hours Help",
            sections = new object[]
            {
                new
                {
                    title = "Authoritative balance",
                    content =
                        "Approved BoH-linked labor permanently consumes "
                        + "hours. Draft and submitted hours are exposure "
                        + "only until approved."
                },
                new
                {
                    title = "Credits",
                    content =
                        "Credits increase total available hours. "
                        + "Corrections are recorded as reversals so the "
                        + "history remains auditable."
                },
                new
                {
                    title = "Expiration",
                    content =
                        "Eligibility is based on work date. The Project "
                        + "Team Coordinator may extend the effective "
                        + "expiration date with a reason."
                },
                new
                {
                    title = "Work requests",
                    content =
                        "T&M, Service Request, Fixed Price, and IQS work "
                        + "may separately select an active BoH funding "
                        + "contract."
                },
                new
                {
                    title = "Weekly workbook",
                    content =
                        "The global SMTP service sends a color-preserving "
                        + "XLSX workbook grouped by Account Executive with "
                        + "filters, frozen panes, and AE-specific sheets."
                },
                new
                {
                    title = "Permissions",
                    content =
                        "Sales and Executive users are read-only. Only "
                        + "Project Team Coordinator users may edit contract "
                        + "records or the weekly schedule."
                }
            },
            formulas = FormulaDefinitions()
        });
    }

    private static async Task<IResult> CreateContractAsync(
        ContractCreateRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Unauthorized();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        var access = await LoadAccessAsync(connection, actor.Value);

        if (!access.CanManage)
        {
            return ForbiddenManage();
        }

        if (request.ClientId == Guid.Empty
            || request.PrimaryAccountExecutiveUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ContractName)
            || request.PurchasedHours < 0
            || request.StartDate == default)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message =
                    "Customer, Account Executive, contract name, "
                    + "start date, and non-negative purchased hours "
                    + "are required."
            });
        }

        var originalExpiration =
            request.OriginalExpirationDate
            ?? request.StartDate.AddYears(1);

        const string sql = """
            INSERT INTO boh_contracts (
                client_id,
                contract_name,
                primary_account_executive_user_id,
                project_team_coordinator_user_id,
                purchased_hours,
                start_date,
                original_expiration_date,
                effective_expiration_date,
                eligible_tm,
                eligible_service_request,
                eligible_fixed_price,
                eligible_iqs,
                certinia_id,
                sell_quote,
                salesforce_id,
                purchase_order_reference,
                internal_summary,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @client_id,
                @contract_name,
                @account_executive_user_id,
                @coordinator_user_id,
                @purchased_hours,
                @start_date,
                @original_expiration_date,
                @effective_expiration_date,
                @eligible_tm,
                @eligible_service_request,
                @eligible_fixed_price,
                @eligible_iqs,
                @certinia_id,
                @sell_quote,
                @salesforce_id,
                @purchase_order_reference,
                @internal_summary,
                @actor_user_id,
                @actor_user_id
            )
            RETURNING boh_contract_id;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "client_id",
            request.ClientId);

        command.Parameters.AddWithValue(
            "contract_name",
            request.ContractName.Trim());

        command.Parameters.AddWithValue(
            "account_executive_user_id",
            request.PrimaryAccountExecutiveUserId);

        command.Parameters.AddWithValue(
            "coordinator_user_id",
            actor.Value);

        command.Parameters.AddWithValue(
            "purchased_hours",
            request.PurchasedHours);

        command.Parameters.AddWithValue(
            "start_date",
            request.StartDate);

        command.Parameters.AddWithValue(
            "original_expiration_date",
            originalExpiration);

        command.Parameters.AddWithValue(
            "effective_expiration_date",
            originalExpiration);

        command.Parameters.AddWithValue(
            "eligible_tm",
            request.EligibleTm);

        command.Parameters.AddWithValue(
            "eligible_service_request",
            request.EligibleServiceRequest);

        command.Parameters.AddWithValue(
            "eligible_fixed_price",
            request.EligibleFixedPrice);

        command.Parameters.AddWithValue(
            "eligible_iqs",
            request.EligibleIqs);

        command.Parameters.AddWithValue(
            "certinia_id",
            request.CertiniaId?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "sell_quote",
            request.SellQuote?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "salesforce_id",
            request.SalesforceId?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "purchase_order_reference",
            request.PurchaseOrderReference?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "internal_summary",
            request.InternalSummary?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "actor_user_id",
            actor.Value);

        var id =
            (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException(
                "Unable to create the contract."));

        await InsertAuditAsync(
            connection,
            actor.Value,
            "boh_contract_created",
            "boh_contract",
            id);

        return Results.Ok(new
        {
            status = "contract_created",
            bohContractId = id
        });
    }

    private static async Task<IResult> AddCreditAsync(
        Guid contractId,
        ContractCreditRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Unauthorized();
        }

        if (request.Hours <= 0
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Positive credit hours and a reason are required."
            });
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actor.Value)).CanManage)
        {
            return ForbiddenManage();
        }

        const string sql = """
            INSERT INTO boh_contract_adjustments (
                boh_contract_id,
                adjustment_type,
                hours,
                reason,
                customer_satisfaction_reference,
                created_by_user_id
            )
            VALUES (
                @contract_id,
                'credit_awarded',
                @hours,
                @reason,
                @reference,
                @actor_user_id
            );
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue(
            "contract_id",
            contractId);

        command.Parameters.AddWithValue(
            "hours",
            request.Hours);

        command.Parameters.AddWithValue(
            "reason",
            request.Reason.Trim());

        command.Parameters.AddWithValue(
            "reference",
            request.CustomerSatisfactionReference?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "actor_user_id",
            actor.Value);

        await command.ExecuteNonQueryAsync();

        await InsertAuditAsync(
            connection,
            actor.Value,
            "boh_credit_awarded",
            "boh_contract",
            contractId);

        return Results.Ok(new
        {
            status = "credit_awarded"
        });
    }

    private static async Task<IResult> ExtendContractAsync(
        Guid contractId,
        ContractExtensionRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Unauthorized();
        }

        if (request.NewExpirationDate == default
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "New expiration date and reason are required."
            });
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actor.Value)).CanManage)
        {
            return ForbiddenManage();
        }

        await using var transaction =
            await connection.BeginTransactionAsync();

        DateOnly currentExpiration;

        await using (var currentCommand =
            new NpgsqlCommand("""
                SELECT effective_expiration_date
                FROM boh_contracts
                WHERE boh_contract_id = @contract_id
                FOR UPDATE;
                """,
                connection,
                transaction))
        {
            currentCommand.Parameters.AddWithValue(
                "contract_id",
                contractId);

            var value =
                await currentCommand.ExecuteScalarAsync();

            if (value is null)
            {
                return Results.NotFound(new
                {
                    status = "not_found",
                    message = "Contract was not found."
                });
            }

            currentExpiration =
                DateOnly.FromDateTime(
                    Convert.ToDateTime(value));
        }

        if (request.NewExpirationDate <= currentExpiration)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message =
                    "The new expiration date must be later than "
                    + "the current effective expiration."
            });
        }

        await using (var insert =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_extensions (
                    boh_contract_id,
                    previous_expiration_date,
                    new_expiration_date,
                    reason,
                    created_by_user_id
                )
                VALUES (
                    @contract_id,
                    @previous_expiration,
                    @new_expiration,
                    @reason,
                    @actor_user_id
                );
                """,
                connection,
                transaction))
        {
            insert.Parameters.AddWithValue(
                "contract_id",
                contractId);

            insert.Parameters.AddWithValue(
                "previous_expiration",
                currentExpiration);

            insert.Parameters.AddWithValue(
                "new_expiration",
                request.NewExpirationDate);

            insert.Parameters.AddWithValue(
                "reason",
                request.Reason.Trim());

            insert.Parameters.AddWithValue(
                "actor_user_id",
                actor.Value);

            await insert.ExecuteNonQueryAsync();
        }

        await using (var update =
            new NpgsqlCommand("""
                UPDATE boh_contracts
                SET effective_expiration_date = @new_expiration,
                    updated_by_user_id = @actor_user_id,
                    updated_at = NOW()
                WHERE boh_contract_id = @contract_id;
                """,
                connection,
                transaction))
        {
            update.Parameters.AddWithValue(
                "new_expiration",
                request.NewExpirationDate);

            update.Parameters.AddWithValue(
                "actor_user_id",
                actor.Value);

            update.Parameters.AddWithValue(
                "contract_id",
                contractId);

            await update.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        await InsertAuditAsync(
            connection,
            actor.Value,
            "boh_contract_extended",
            "boh_contract",
            contractId);

        return Results.Ok(new
        {
            status = "contract_extended",
            effectiveExpirationDate = request.NewExpirationDate
        });
    }

    private static async Task<IResult> AddNoteAsync(
        Guid contractId,
        ContractNoteRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.NoteText))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Note text is required."
            });
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actor.Value)).CanManage)
        {
            return ForbiddenManage();
        }

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_notes (
                    boh_contract_id,
                    note_text,
                    created_by_user_id
                )
                VALUES (
                    @contract_id,
                    @note_text,
                    @actor_user_id
                );
                """,
                connection);

        command.Parameters.AddWithValue(
            "contract_id",
            contractId);

        command.Parameters.AddWithValue(
            "note_text",
            request.NoteText.Trim());

        command.Parameters.AddWithValue(
            "actor_user_id",
            actor.Value);

        await command.ExecuteNonQueryAsync();

        return Results.Ok(new
        {
            status = "note_added"
        });
    }

    private static async Task<IResult> UpdateScheduleAsync(
        EmailScheduleRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Unauthorized();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actor.Value)).CanManage)
        {
            return ForbiddenManage();
        }

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_email_schedule (
                    schedule_key,
                    is_enabled,
                    weekday_iso,
                    send_time,
                    time_zone,
                    subject_template,
                    body_introduction,
                    include_expired,
                    low_balance_threshold_percent,
                    expiration_warning_days,
                    retention_months,
                    updated_by_user_id,
                    updated_at
                )
                VALUES (
                    'weekly-balance',
                    @is_enabled,
                    @weekday_iso,
                    @send_time,
                    @time_zone,
                    @subject_template,
                    @body_introduction,
                    @include_expired,
                    @low_balance_threshold_percent,
                    @expiration_warning_days,
                    @retention_months,
                    @actor_user_id,
                    NOW()
                )
                ON CONFLICT (schedule_key)
                DO UPDATE SET
                    is_enabled = EXCLUDED.is_enabled,
                    weekday_iso = EXCLUDED.weekday_iso,
                    send_time = EXCLUDED.send_time,
                    time_zone = EXCLUDED.time_zone,
                    subject_template = EXCLUDED.subject_template,
                    body_introduction = EXCLUDED.body_introduction,
                    include_expired = EXCLUDED.include_expired,
                    low_balance_threshold_percent =
                        EXCLUDED.low_balance_threshold_percent,
                    expiration_warning_days =
                        EXCLUDED.expiration_warning_days,
                    retention_months = EXCLUDED.retention_months,
                    updated_by_user_id = EXCLUDED.updated_by_user_id,
                    updated_at = NOW();
                """,
                connection);

        command.Parameters.AddWithValue(
            "is_enabled",
            request.IsEnabled);

        command.Parameters.AddWithValue(
            "weekday_iso",
            Math.Clamp(request.WeekdayIso, 1, 7));

        command.Parameters.AddWithValue(
            "send_time",
            request.SendTime);

        command.Parameters.AddWithValue(
            "time_zone",
            request.TimeZone?.Trim() ?? "America/Chicago");

        command.Parameters.AddWithValue(
            "subject_template",
            request.SubjectTemplate?.Trim()
            ?? "Weekly Block of Hours Balance Summary");

        command.Parameters.AddWithValue(
            "body_introduction",
            request.BodyIntroduction?.Trim() ?? "");

        command.Parameters.AddWithValue(
            "include_expired",
            request.IncludeExpired);

        command.Parameters.AddWithValue(
            "low_balance_threshold_percent",
            request.LowBalanceThresholdPercent);

        command.Parameters.AddWithValue(
            "expiration_warning_days",
            request.ExpirationWarningDays);

        command.Parameters.AddWithValue(
            "retention_months",
            request.RetentionMonths);

        command.Parameters.AddWithValue(
            "actor_user_id",
            actor.Value);

        await command.ExecuteNonQueryAsync();

        return Results.Ok(new
        {
            status = "schedule_updated"
        });
    }

    private static async Task<IResult> GetReportPreviewAsync(
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return Unauthorized();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        var access =
            await LoadAccessAsync(connection, actor.Value);

        if (!access.CanView)
        {
            return Results.Json(
                new
                {
                    status = "forbidden"
                },
                statusCode: 403);
        }

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    COUNT(DISTINCT primary_account_executive_user_id),
                    COUNT(*)
                FROM boh_contracts
                WHERE contract_status <> 'cancelled';
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        await reader.ReadAsync();

        return Results.Ok(new
        {
            status = "report_preview_ready",
            format = "xlsx",
            accountExecutiveCount = reader.GetInt64(0),
            contractCount = reader.GetInt64(1),
            groupedByAccountExecutive = true,
            filtersEnabled = true,
            globalSmtp = true,
            generationImplementedInNextStage = true
        });
    }

    private static async Task<List<ContractRow>>
        LoadContractsAsync(NpgsqlConnection connection)
    {
        var rows = new List<ContractRow>();

        const string sql = """
            SELECT
                b.boh_contract_id,
                c.client_id,
                COALESCE(c.client_name, ''),
                COALESCE(
                    NULLIF(
                        concat_ws(
                            ', ',
                            NULLIF(cc.address_line1, ''),
                            NULLIF(cc.address_line2, ''),
                            NULLIF(cc.city, ''),
                            NULLIF(cc.state_province, ''),
                            NULLIF(cc.postal_code, '')
                        ),
                        ''
                    ),
                    ''
                ) AS customer_address,
                b.contract_name,
                b.contract_status,
                ae.user_id,
                COALESCE(NULLIF(ae.display_name, ''), ae.email),
                ae.email,
                ptc.user_id,
                COALESCE(NULLIF(ptc.display_name, ''), ptc.email),
                b.purchased_hours,
                b.credit_awarded,
                b.total_available_hours,
                b.entered_hours,
                b.submitted_hours,
                b.consumed_hours,
                b.overage_hours,
                b.remaining_balance,
                b.projected_remaining,
                b.start_date,
                b.original_expiration_date,
                b.effective_expiration_date,
                b.certinia_id,
                b.sell_quote,
                b.salesforce_id,
                b.purchase_order_reference,
                b.updated_at
            FROM vw_boh_contract_balances b
            JOIN clients c
                ON c.client_id = b.client_id
            JOIN app_users ae
                ON ae.user_id =
                    b.primary_account_executive_user_id
            JOIN app_users ptc
                ON ptc.user_id =
                    b.project_team_coordinator_user_id
            LEFT JOIN LATERAL (
                SELECT
                    address_line1,
                    address_line2,
                    city,
                    state_province,
                    postal_code
                FROM client_contacts
                WHERE client_id = c.client_id
                ORDER BY
                    is_primary DESC,
                    display_order,
                    created_at
                LIMIT 1
            ) cc ON TRUE
            ORDER BY
                ae.display_name,
                c.client_name,
                b.contract_name;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var total = reader.GetDecimal(13);
            var remaining = reader.GetDecimal(18);
            var balancePercent =
                total <= 0
                ? 0
                : Math.Round(
                    remaining / total * 100,
                    2);

            var effectiveExpiration =
                DateOnly.FromDateTime(
                    reader.GetDateTime(22));

            rows.Add(new ContractRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.GetDecimal(11),
                reader.GetDecimal(12),
                total,
                reader.GetDecimal(14),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                remaining,
                reader.GetDecimal(19),
                balancePercent,
                DateOnly.FromDateTime(reader.GetDateTime(20)),
                DateOnly.FromDateTime(reader.GetDateTime(21)),
                effectiveExpiration,
                effectiveExpiration.DayNumber
                    - DateOnly.FromDateTime(
                        DateTime.UtcNow).DayNumber,
                reader.GetString(23),
                reader.GetString(24),
                reader.GetString(25),
                reader.GetString(26),
                new DateTimeOffset(
                    reader.GetDateTime(27))));
        }

        return rows;
    }

    private static async Task<List<CustomerOption>>
        LoadCustomersAsync(NpgsqlConnection connection)
    {
        var rows = new List<CustomerOption>();

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    c.client_id,
                    c.client_name,
                    COALESCE(
                        NULLIF(
                            concat_ws(
                                ', ',
                                NULLIF(cc.address_line1, ''),
                                NULLIF(cc.address_line2, ''),
                                NULLIF(cc.city, ''),
                                NULLIF(cc.state_province, ''),
                                NULLIF(cc.postal_code, '')
                            ),
                            ''
                        ),
                        ''
                    ) AS customer_address
                FROM clients c
                LEFT JOIN LATERAL (
                    SELECT
                        address_line1,
                        address_line2,
                        city,
                        state_province,
                        postal_code
                    FROM client_contacts
                    WHERE client_id = c.client_id
                    ORDER BY
                        is_primary DESC,
                        display_order,
                        created_at
                    LIMIT 1
                ) cc ON TRUE
                WHERE c.is_active = TRUE
                ORDER BY c.client_name;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CustomerOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<List<UserOption>>
        LoadAccountExecutivesAsync(
            NpgsqlConnection connection)
    {
        return await LoadUsersByRoleOrProfileAsync(
            connection,
            new[]
            {
                "SALES",
                "ACCOUNT_EXECUTIVE"
            },
            new[]
            {
                "account executive",
                "sales",
                "account manager"
            });
    }

    private static async Task<List<UserOption>>
        LoadCoordinatorsAsync(
            NpgsqlConnection connection)
    {
        return await LoadUsersByRoleOrProfileAsync(
            connection,
            new[]
            {
                "PROJECT_TEAM_COORDINATOR"
            },
            new[]
            {
                "project team coordinator",
                "project coordinator"
            });
    }

    private static async Task<List<UserOption>>
        LoadUsersByRoleOrProfileAsync(
            NpgsqlConnection connection,
            string[] roleCodes,
            string[] profileTerms)
    {
        var rows = new List<UserOption>();

        await using var command =
            new NpgsqlCommand("""
                SELECT DISTINCT
                    u.user_id,
                    COALESCE(NULLIF(u.display_name, ''), u.email),
                    u.email,
                    COALESCE(
                        NULLIF(u.job_title, ''),
                        NULLIF(u.role_name, ''),
                        NULLIF(u.department_name, ''),
                        NULLIF(u.department, ''),
                        ''
                    )
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id
                   AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                   AND r.is_active = TRUE
                WHERE u.is_active = TRUE
                  AND COALESCE(u.login_enabled, TRUE) = TRUE
                  AND (
                        r.role_code = ANY(@role_codes)
                     OR EXISTS (
                            SELECT 1
                            FROM unnest(@profile_terms)
                                AS profile_term(term)
                            WHERE LOWER(
                                COALESCE(u.job_title, '') || ' '
                                || COALESCE(u.role_name, '') || ' '
                                || COALESCE(u.department_name, '') || ' '
                                || COALESCE(u.department, '') || ' '
                                || COALESCE(u.team_name, '')
                            )
                            LIKE '%' || LOWER(profile_term.term) || '%'
                        )
                  )
                ORDER BY 2, u.email;
                """,
                connection);

        command.Parameters.AddWithValue(
            "role_codes",
            roleCodes);

        command.Parameters.AddWithValue(
            "profile_terms",
            profileTerms);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new UserOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private static async Task<EmailScheduleRow>
        LoadScheduleAsync(NpgsqlConnection connection)
    {
        await using var command =
            new NpgsqlCommand("""
                SELECT
                    is_enabled,
                    weekday_iso,
                    send_time,
                    time_zone,
                    subject_template,
                    body_introduction,
                    include_expired,
                    low_balance_threshold_percent,
                    expiration_warning_days,
                    retention_months,
                    updated_at
                FROM boh_email_schedule
                WHERE schedule_key = 'weekly-balance';
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new EmailScheduleRow(
                false,
                1,
                new TimeOnly(8, 0),
                "America/Chicago",
                "Weekly Block of Hours Balance Summary",
                "",
                false,
                25,
                90,
                24,
                null);
        }

        return new EmailScheduleRow(
            reader.GetBoolean(0),
            reader.GetInt32(1),
            TimeOnly.FromTimeSpan(reader.GetTimeSpan(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetDecimal(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            new DateTimeOffset(reader.GetDateTime(10)));
    }

    private static async Task<AccessResult>
        LoadAccessAsync(
            NpgsqlConnection connection,
            Guid userId)
    {
        var roles = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        await using var command =
            new NpgsqlCommand("""
                SELECT DISTINCT r.role_code
                FROM app_user_role_assignments ura
                JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE
                  AND r.is_active = TRUE;
                """,
                connection);

        command.Parameters.AddWithValue(
            "user_id",
            userId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));
        }

        return new AccessResult(
            roles.Overlaps(ViewRoleCodes),
            roles.Overlaps(ManageRoleCodes));
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        string action,
        string entityType,
        Guid entityId)
    {
        await using var command =
            new NpgsqlCommand("""
                INSERT INTO audit_logs (
                    actor_user_id,
                    action,
                    entity_type,
                    entity_id
                )
                VALUES (
                    @actor_user_id,
                    @action,
                    @entity_type,
                    @entity_id
                );
                """,
                connection);

        command.Parameters.AddWithValue(
            "actor_user_id",
            actorUserId);

        command.Parameters.AddWithValue(
            "action",
            action);

        command.Parameters.AddWithValue(
            "entity_type",
            entityType);

        command.Parameters.AddWithValue(
            "entity_id",
            entityId);

        await command.ExecuteNonQueryAsync();
    }

    private static object[] FormulaDefinitions()
    {
        return new object[]
        {
            new
            {
                key = "purchasedHours",
                label = "Purchased Hours",
                formula =
                    "Hours purchased in the original agreement.",
                source = "Contract record",
                balanceImpact = "Increases total available hours"
            },
            new
            {
                key = "creditAwarded",
                label = "Credit Awarded",
                formula =
                    "Awarded credits minus credit reversals.",
                source = "Credit adjustment ledger",
                balanceImpact = "Increases total available hours"
            },
            new
            {
                key = "totalAvailableHours",
                label = "Total Available",
                formula =
                    "Purchased Hours + Credit Awarded - Credit Reversals",
                source = "Contract and adjustment ledger",
                balanceImpact = "Authoritative availability"
            },
            new
            {
                key = "enteredHours",
                label = "Entered Hours",
                formula =
                    "Draft BoH-linked labor hours.",
                source = "BoH usage ledger",
                balanceImpact =
                    "Projected exposure only; not permanently deducted"
            },
            new
            {
                key = "submittedHours",
                label = "Submitted Hours",
                formula =
                    "Submitted BoH-linked labor awaiting approval.",
                source = "BoH usage ledger",
                balanceImpact =
                    "Projected exposure only; not permanently deducted"
            },
            new
            {
                key = "consumedHours",
                label = "Consumed Hours",
                formula =
                    "Approved BoH-linked labor hours.",
                source = "BoH usage ledger",
                balanceImpact = "Permanently deducts from balance"
            },
            new
            {
                key = "remainingBalance",
                label = "Remaining Balance",
                formula =
                    "Total Available Hours - Consumed Hours",
                source = "Calculated",
                balanceImpact = "Authoritative remaining balance"
            },
            new
            {
                key = "projectedRemaining",
                label = "Projected Remaining",
                formula =
                    "Total Available - Entered - Submitted - Consumed",
                source = "Calculated",
                balanceImpact = "Forecast only"
            },
            new
            {
                key = "effectiveExpiration",
                label = "Effective Expiration",
                formula =
                    "Latest approved extension; otherwise original expiration",
                source = "Contract and extension ledger",
                balanceImpact = "Controls work-date eligibility"
            }
        };
    }

    private static IResult Unauthorized()
    {
        return Results.Json(
            new
            {
                status = "session_required",
                message = "A ProjectPulse session is required."
            },
            statusCode: 401);
    }

    private static IResult ForbiddenManage()
    {
        return Results.Json(
            new
            {
                status = "forbidden",
                message =
                    "Only the Project Team Coordinator "
                    + "may edit Block of Hours contracts."
            },
            statusCode: 403);
    }

    private static Guid? SessionUserId(
        HttpContext context)
    {
        if (context.Items.TryGetValue(
                "ProjectPulseSessionUserId",
                out var value)
            && value is Guid userId)
        {
            return userId;
        }

        return null;
    }

    private static string ConnectionString()
    {
        var value =
            Environment.GetEnvironmentVariable(
                "PROJECTPULSE_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable(
                "PROJECTTIME_DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Missing database connection configuration.");
        }

        return value;
    }

    private sealed record AccessResult(
        bool CanView,
        bool CanManage);

    private sealed record ContractCreateRequest(
        Guid ClientId,
        string ContractName,
        Guid PrimaryAccountExecutiveUserId,
        decimal PurchasedHours,
        DateOnly StartDate,
        DateOnly? OriginalExpirationDate,
        bool EligibleTm,
        bool EligibleServiceRequest,
        bool EligibleFixedPrice,
        bool EligibleIqs,
        string? CertiniaId,
        string? SellQuote,
        string? SalesforceId,
        string? PurchaseOrderReference,
        string? InternalSummary);

    private sealed record ContractCreditRequest(
        decimal Hours,
        string Reason,
        string? CustomerSatisfactionReference);

    private sealed record ContractExtensionRequest(
        DateOnly NewExpirationDate,
        string Reason);

    private sealed record ContractNoteRequest(
        string NoteText);

    private sealed record EmailScheduleRequest(
        bool IsEnabled,
        int WeekdayIso,
        TimeOnly SendTime,
        string? TimeZone,
        string? SubjectTemplate,
        string? BodyIntroduction,
        bool IncludeExpired,
        decimal LowBalanceThresholdPercent,
        int ExpirationWarningDays,
        int RetentionMonths);

    private sealed record ContractRow(
        Guid BohContractId,
        Guid ClientId,
        string CustomerName,
        string CustomerAddress,
        string ContractName,
        string Status,
        Guid AccountExecutiveUserId,
        string AccountExecutiveName,
        string AccountExecutiveEmail,
        Guid ProjectTeamCoordinatorUserId,
        string ProjectTeamCoordinatorName,
        decimal PurchasedHours,
        decimal CreditAwarded,
        decimal TotalAvailableHours,
        decimal EnteredHours,
        decimal SubmittedHours,
        decimal ConsumedHours,
        decimal OverageHours,
        decimal RemainingBalance,
        decimal ProjectedRemaining,
        decimal BalancePercent,
        DateOnly StartDate,
        DateOnly OriginalExpirationDate,
        DateOnly EffectiveExpirationDate,
        int DaysUntilExpiration,
        string CertiniaId,
        string SellQuote,
        string SalesforceId,
        string PurchaseOrderReference,
        DateTimeOffset UpdatedAt);

    private sealed record CustomerOption(
        Guid ClientId,
        string CustomerName,
        string CustomerAddress);

    private sealed record UserOption(
        Guid UserId,
        string DisplayName,
        string Email,
        string Profile);

    private sealed record EmailScheduleRow(
        bool IsEnabled,
        int WeekdayIso,
        TimeOnly SendTime,
        string TimeZone,
        string SubjectTemplate,
        string BodyIntroduction,
        bool IncludeExpired,
        decimal LowBalanceThresholdPercent,
        int ExpirationWarningDays,
        int RetentionMonths,
        DateTimeOffset? UpdatedAt);
}
