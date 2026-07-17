using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class ContractsPrepaidManagementModule
{
    private static readonly string[] ManageRoles =
    {
        "SUPER_ADMINISTRATOR",
        "SUPERADMIN",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR",
        "ADMIN",
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_COORDINATOR",
        "PTC"
    };

    private static readonly string[] SalesRoles =
    {
        "SALES",
        "ACCOUNT_EXECUTIVE",
        "ACCOUNT_MANAGER"
    };

    private static readonly string[] CoordinatorRoles =
    {
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_COORDINATOR",
        "PTC"
    };

    private static readonly string[] RequiredHeaders =
    {
        "Account Executive",
        "Customer",
        "Engagement Name",
        "Contract Manager",
        "PO/Quote",
        "Contract Start Date",
        "Contract End Date",
        "Fixed Fee Item",
        "Latest Time Text",
        "Billing Date",
        "FF Amount",
        "Pending Hours",
        "Approved Hours",
        "Total Hours",
        "Total Expenses",
        "Adjustments",
        "Total Used",
        "Remaining Balance",
        "Balance %"
    };

    public static WebApplication MapContractsPrepaidManagementModule(
        this WebApplication app)
    {
        app.MapGet(
            "/api/contracts/prepaid/options",
            (Func<HttpContext, Task<IResult>>)GetOptionsAsync);

        app.MapPost(
            "/api/contracts/prepaid/contracts",
            (Func<CreateRequest, HttpContext, Task<IResult>>)CreateAsync);

        app.MapGet(
            "/api/contracts/prepaid/contracts/{contractId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)GetDetailsAsync);

        app.MapPost(
            "/api/contracts/prepaid/credits/{adjustmentId:guid}/reverse",
            (Func<Guid, ReverseCreditRequest, HttpContext, Task<IResult>>)
                ReverseCreditAsync);

        app.MapPost(
            "/api/contracts/prepaid/import-preview",
            (Func<HttpContext, Task<IResult>>)PreviewImportAsync);

        app.MapPost(
            "/api/contracts/prepaid/imports/{batchId:guid}/confirm",
            (Func<Guid, HttpContext, Task<IResult>>)ConfirmImportAsync);

        app.MapGet(
            "/api/contracts/prepaid/email-schedule",
            (Func<HttpContext, Task<IResult>>)GetScheduleAsync);

        app.MapPut(
            "/api/contracts/prepaid/email-schedule",
            (Func<ScheduleRequest, HttpContext, Task<IResult>>)
                UpdateScheduleAsync);

        app.MapGet(
            "/api/contracts/prepaid/eligible",
            (Func<HttpContext, Task<IResult>>)GetEligibleAsync);

        app.MapPost(
            "/api/contracts/prepaid/time-usage",
            (Func<TimeUsageRequest, HttpContext, Task<IResult>>)
                RecordUsageAsync);

        return app;
    }

    private static async Task<IResult> GetOptionsAsync(
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "Management options are limited to Administrators, "
                + "Superadmins, and Project Team Coordinators.");
        }

        return Results.Ok(new
        {
            status = "prepaid_management_options_loaded",
            customers = await CustomersAsync(connection),
            accountExecutives = await UsersAsync(
                connection,
                SalesRoles,
                new[] { "account executive", "account manager", "sales" }),
            coordinators = await UsersAsync(
                connection,
                CoordinatorRoles,
                new[] { "project team coordinator", "project coordinator" }),
            schedule = await ScheduleAsync(connection)
        });
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or Project Team "
                + "Coordinator may create contracts.");
        }

        if (request.ClientId == Guid.Empty
            || request.AccountExecutiveUserId == Guid.Empty
            || request.ProjectTeamCoordinatorUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.EngagementName)
            || request.ContractStartDate == default
            || request.ContractEndDate == default
            || request.ContractEndDate < request.ContractStartDate
            || request.FixedFeeAmount < 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message =
                    "Customer, Account Executive, Contract Manager, "
                    + "Engagement Name, valid dates, and a non-negative "
                    + "FF Amount are required."
            });
        }

        if (!await IsActiveCustomerAsync(connection, request.ClientId)
            || !await IsMatchingUserAsync(
                connection,
                request.AccountExecutiveUserId,
                SalesRoles,
                new[] { "account executive", "account manager", "sales" })
            || !await IsMatchingUserAsync(
                connection,
                request.ProjectTeamCoordinatorUserId,
                CoordinatorRoles,
                new[] { "project team coordinator", "project coordinator" }))
        {
            return Results.BadRequest(new
            {
                status = "system_reference_validation_failed",
                message =
                    "Account Executive, Customer, and Contract Manager "
                    + "must be existing active ProjectPulse records."
            });
        }

        var sourceKey = SourceKey(
            request.ClientId.ToString(),
            request.EngagementName,
            request.PoQuote ?? "",
            request.FixedFeeItem ?? "",
            request.BillingDate?.ToString("yyyy-MM-dd") ?? "");

        var available = request.FixedFeeAmount + request.Adjustments;
        var used = request.PendingAmount
            + request.ApprovedAmount
            + request.TotalExpenses;

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contracts (
                    client_id,
                    contract_name,
                    contract_status,
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
                    balance_unit,
                    fixed_fee_item,
                    latest_time_text,
                    billing_date,
                    fixed_fee_amount,
                    imported_pending_amount,
                    imported_approved_amount,
                    total_expenses,
                    manual_adjustments,
                    import_source_key,
                    import_snapshot_at,
                    created_by_user_id,
                    updated_by_user_id
                )
                VALUES (
                    @client_id,
                    @engagement_name,
                    @status,
                    @ae_id,
                    @ptc_id,
                    0,
                    @start_date,
                    @end_date,
                    @end_date,
                    TRUE,
                    TRUE,
                    TRUE,
                    TRUE,
                    @certinia_id,
                    @sell_quote,
                    @salesforce_id,
                    @po_quote,
                    @notes,
                    'currency',
                    @fixed_fee_item,
                    @latest_time_text,
                    @billing_date,
                    @fixed_fee_amount,
                    @pending_amount,
                    @approved_amount,
                    @total_expenses,
                    @adjustments,
                    @source_key,
                    NOW(),
                    @actor_id,
                    @actor_id
                )
                RETURNING boh_contract_id;
                """,
                connection);

        command.Parameters.AddWithValue("client_id", request.ClientId);
        command.Parameters.AddWithValue(
            "engagement_name",
            request.EngagementName.Trim());
        command.Parameters.AddWithValue(
            "status",
            Status(
                request.ContractEndDate,
                available - used,
                available));
        command.Parameters.AddWithValue(
            "ae_id",
            request.AccountExecutiveUserId);
        command.Parameters.AddWithValue(
            "ptc_id",
            request.ProjectTeamCoordinatorUserId);
        command.Parameters.AddWithValue(
            "start_date",
            request.ContractStartDate);
        command.Parameters.AddWithValue(
            "end_date",
            request.ContractEndDate);
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
            "po_quote",
            request.PoQuote?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "notes",
            request.Notes?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "fixed_fee_item",
            request.FixedFeeItem?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "latest_time_text",
            request.LatestTimeText?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "billing_date",
            (object?)request.BillingDate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "fixed_fee_amount",
            request.FixedFeeAmount);
        command.Parameters.AddWithValue(
            "pending_amount",
            request.PendingAmount);
        command.Parameters.AddWithValue(
            "approved_amount",
            request.ApprovedAmount);
        command.Parameters.AddWithValue(
            "total_expenses",
            request.TotalExpenses);
        command.Parameters.AddWithValue(
            "adjustments",
            request.Adjustments);
        command.Parameters.AddWithValue(
            "source_key",
            sourceKey);
        command.Parameters.AddWithValue(
            "actor_id",
            actor.Value);

        var contractId =
            (Guid)(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException(
                    "Unable to create the contract."));

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            await InsertNoteAsync(
                connection,
                contractId,
                actor.Value,
                "general",
                request.Notes.Trim(),
                "contract-create");
        }

        return Results.Ok(new
        {
            status = "prepaid_contract_created",
            bohContractId = contractId
        });
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid contractId,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        var access = await AccessAsync(connection, actor.Value);

        if (!access.CanView)
        {
            return Forbidden("Contract details are not available.");
        }

        object? contract = null;

        await using (var command =
            new NpgsqlCommand("""
                SELECT
                    boh_contract_id,
                    customer_name,
                    engagement_name,
                    account_executive_name,
                    contract_manager_name,
                    po_quote,
                    contract_start_date,
                    contract_end_date,
                    fixed_fee_amount,
                    credit_awarded,
                    total_used,
                    total_available,
                    remaining_balance,
                    balance_percent,
                    certinia_id,
                    sell_quote,
                    salesforce_id,
                    contract_status
                FROM vw_boh_prepaid_balance_rows
                WHERE boh_contract_id = @contract_id;
                """,
                connection))
        {
            command.Parameters.AddWithValue(
                "contract_id",
                contractId);

            await using var reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                contract = new
                {
                    bohContractId = reader.GetGuid(0),
                    customerName = reader.GetString(1),
                    engagementName = reader.GetString(2),
                    accountExecutiveName = reader.GetString(3),
                    contractManagerName = reader.GetString(4),
                    poQuote = reader.GetString(5),
                    contractStartDate =
                        DateOnly.FromDateTime(reader.GetDateTime(6)),
                    contractEndDate =
                        DateOnly.FromDateTime(reader.GetDateTime(7)),
                    fixedFeeAmount = reader.GetDecimal(8),
                    creditAwarded = reader.GetDecimal(9),
                    totalUsed = reader.GetDecimal(10),
                    totalAvailable = reader.GetDecimal(11),
                    remainingBalance = reader.GetDecimal(12),
                    balancePercent = reader.IsDBNull(13)
                        ? (decimal?)null
                        : reader.GetDecimal(13),
                    certiniaId = reader.GetString(14),
                    sellQuote = reader.GetString(15),
                    salesforceId = reader.GetString(16),
                    contractStatus = reader.GetString(17)
                };
            }
        }

        if (contract is null)
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "The contract was not found."
            });
        }

        var credits = new List<object>();

        await using (var command =
            new NpgsqlCommand("""
                SELECT
                    a.boh_contract_adjustment_id,
                    a.adjustment_type,
                    COALESCE(a.amount, a.hours),
                    COALESCE(a.awarded_on, a.created_at::DATE),
                    a.reason,
                    a.customer_satisfaction_reference,
                    a.reverses_adjustment_id,
                    a.created_at,
                    COALESCE(NULLIF(u.display_name, ''), u.email)
                FROM boh_contract_adjustments a
                JOIN app_users u
                    ON u.user_id = a.created_by_user_id
                WHERE a.boh_contract_id = @contract_id
                ORDER BY
                    COALESCE(a.awarded_on, a.created_at::DATE) DESC,
                    a.created_at DESC;
                """,
                connection))
        {
            command.Parameters.AddWithValue(
                "contract_id",
                contractId);

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                credits.Add(new
                {
                    adjustmentId = reader.GetGuid(0),
                    adjustmentType = reader.GetString(1),
                    amount = reader.GetDecimal(2),
                    awardedOn =
                        DateOnly.FromDateTime(reader.GetDateTime(3)),
                    reason = reader.GetString(4),
                    reference = reader.GetString(5),
                    reversesAdjustmentId = reader.IsDBNull(6)
                        ? (Guid?)null
                        : reader.GetGuid(6),
                    createdAt =
                        new DateTimeOffset(reader.GetDateTime(7)),
                    awardedBy = reader.GetString(8)
                });
            }
        }

        var notes = new List<object>();

        await using (var command =
            new NpgsqlCommand("""
                SELECT
                    n.boh_contract_note_id,
                    n.note_category,
                    n.note_text,
                    n.created_at,
                    COALESCE(NULLIF(u.display_name, ''), u.email)
                FROM boh_contract_notes n
                JOIN app_users u
                    ON u.user_id = n.created_by_user_id
                WHERE n.boh_contract_id = @contract_id
                ORDER BY n.created_at DESC;
                """,
                connection))
        {
            command.Parameters.AddWithValue(
                "contract_id",
                contractId);

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                notes.Add(new
                {
                    noteId = reader.GetGuid(0),
                    category = reader.GetString(1),
                    noteText = reader.GetString(2),
                    createdAt =
                        new DateTimeOffset(reader.GetDateTime(3)),
                    author = reader.GetString(4)
                });
            }
        }

        return Results.Ok(new
        {
            status = "prepaid_contract_loaded",
            contract,
            credits,
            notes,
            permissions = new
            {
                canManage = access.CanManage,
                canAwardCredit = access.CanManage,
                canAddNote = access.CanManage
            }
        });
    }

    private static async Task<IResult> ReverseCreditAsync(
        Guid adjustmentId,
        ReverseCreditRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "A reversal reason is required."
            });
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or Project Team "
                + "Coordinator may reverse credits.");
        }

        await using var transaction =
            await connection.BeginTransactionAsync();

        Guid contractId;
        decimal amount;

        await using (var command =
            new NpgsqlCommand("""
                SELECT
                    boh_contract_id,
                    COALESCE(amount, hours)
                FROM boh_contract_adjustments
                WHERE boh_contract_adjustment_id = @adjustment_id
                  AND adjustment_type = 'credit_awarded'
                FOR UPDATE;
                """,
                connection,
                transaction))
        {
            command.Parameters.AddWithValue(
                "adjustment_id",
                adjustmentId);

            await using var reader =
                await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    status = "not_found",
                    message = "The credit award was not found."
                });
            }

            contractId = reader.GetGuid(0);
            amount = reader.GetDecimal(1);
        }

        await using (var duplicate =
            new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM boh_contract_adjustments
                    WHERE reverses_adjustment_id = @adjustment_id
                      AND adjustment_type = 'credit_reversal'
                );
                """,
                connection,
                transaction))
        {
            duplicate.Parameters.AddWithValue(
                "adjustment_id",
                adjustmentId);

            if ((bool)(await duplicate.ExecuteScalarAsync() ?? false))
            {
                return Results.Conflict(new
                {
                    status = "already_reversed",
                    message = "This credit has already been reversed."
                });
            }
        }

        await using (var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_adjustments (
                    boh_contract_id,
                    adjustment_type,
                    hours,
                    amount,
                    awarded_on,
                    reason,
                    reverses_adjustment_id,
                    created_by_user_id
                )
                VALUES (
                    @contract_id,
                    'credit_reversal',
                    @amount,
                    @amount,
                    @reversed_on,
                    @reason,
                    @adjustment_id,
                    @actor_id
                );
                """,
                connection,
                transaction))
        {
            command.Parameters.AddWithValue(
                "contract_id",
                contractId);
            command.Parameters.AddWithValue("amount", amount);
            command.Parameters.AddWithValue(
                "reversed_on",
                request.ReversedOn == default
                    ? DateOnly.FromDateTime(DateTime.UtcNow)
                    : request.ReversedOn);
            command.Parameters.AddWithValue(
                "reason",
                request.Reason.Trim());
            command.Parameters.AddWithValue(
                "adjustment_id",
                adjustmentId);
            command.Parameters.AddWithValue(
                "actor_id",
                actor.Value);

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "credit_reversed",
            adjustmentId,
            contractId,
            amount
        });
    }

    private static async Task<IResult> PreviewImportAsync(
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or Project Team "
                + "Coordinator may upload XLSX files.");
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "An XLSX multipart upload is required."
            });
        }

        var form = await context.Request.ReadFormAsync();
        var file = form.Files.GetFile("file")
            ?? form.Files.FirstOrDefault();

        if (file is null
            || file.Length == 0
            || !file.FileName.EndsWith(
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Select a non-empty .xlsx workbook."
            });
        }

        if (file.Length > 15 * 1024 * 1024)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "The workbook must be 15 MB or smaller."
            });
        }

        byte[] bytes;

        await using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream);
            bytes = stream.ToArray();
        }

        using var workbook =
            new XLWorkbook(new MemoryStream(bytes));

        var worksheet =
            workbook.Worksheets.FirstOrDefault(item =>
                item.LastRowUsed() is not null)
            ?? workbook.Worksheet(1);

        var headerMap = BuildHeaderMap(worksheet);

        var missingHeaders = RequiredHeaders
            .Where(header =>
                !headerMap.ContainsKey(NormalizeHeader(header)))
            .ToArray();

        if (missingHeaders.Length > 0)
        {
            return Results.BadRequest(new
            {
                status = "header_validation_failed",
                message = "The workbook is missing required headers.",
                missingHeaders
            });
        }

        var customers = await CustomersAsync(connection);
        var accountExecutives = await UsersAsync(
            connection,
            SalesRoles,
            new[] { "account executive", "account manager", "sales" });
        var coordinators = await UsersAsync(
            connection,
            CoordinatorRoles,
            new[] { "project team coordinator", "project coordinator" });
        var users = await AllUsersAsync(connection);
        var existingSourceKeys =
            await ExistingSourceKeysAsync(connection);

        var rows = ParseRows(
            worksheet,
            headerMap,
            customers,
            accountExecutives,
            coordinators,
            users,
            existingSourceKeys);

        var batchId = Guid.NewGuid();
        var sha256 = Convert.ToHexString(
                SHA256.HashData(bytes))
            .ToLowerInvariant();

        await using var transaction =
            await connection.BeginTransactionAsync();

        await using (var command =
            new NpgsqlCommand("""
                INSERT INTO boh_balance_import_batches (
                    boh_balance_import_batch_id,
                    source_filename,
                    source_sha256,
                    worksheet_name,
                    import_status,
                    uploaded_by_user_id,
                    total_rows,
                    valid_rows,
                    invalid_rows,
                    new_rows,
                    changed_rows,
                    duplicate_rows,
                    header_json,
                    validation_summary_json
                )
                VALUES (
                    @batch_id,
                    @filename,
                    @sha256,
                    @worksheet_name,
                    'preview',
                    @actor_id,
                    @total_rows,
                    @valid_rows,
                    @invalid_rows,
                    @new_rows,
                    @changed_rows,
                    @duplicate_rows,
                    @headers,
                    @summary
                );
                """,
                connection,
                transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue(
                "filename",
                Path.GetFileName(file.FileName));
            command.Parameters.AddWithValue("sha256", sha256);
            command.Parameters.AddWithValue(
                "worksheet_name",
                worksheet.Name);
            command.Parameters.AddWithValue(
                "actor_id",
                actor.Value);
            command.Parameters.AddWithValue(
                "total_rows",
                rows.Count);
            command.Parameters.AddWithValue(
                "valid_rows",
                rows.Count(item => item.RowStatus == "valid"));
            command.Parameters.AddWithValue(
                "invalid_rows",
                rows.Count(item => item.RowStatus == "invalid"));
            command.Parameters.AddWithValue(
                "new_rows",
                rows.Count(item => item.ChangeType == "new"));
            command.Parameters.AddWithValue(
                "changed_rows",
                rows.Count(item => item.ChangeType == "changed"));
            command.Parameters.AddWithValue(
                "duplicate_rows",
                rows.Count(item => item.ChangeType == "duplicate"));
            command.Parameters.AddWithValue(
                "headers",
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(headerMap.Keys));
            command.Parameters.AddWithValue(
                "summary",
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(new
                {
                    file.FileName,
                    worksheet = worksheet.Name
                }));

            await command.ExecuteNonQueryAsync();
        }

        foreach (var row in rows)
        {
            await InsertPreviewRowAsync(
                connection,
                transaction,
                batchId,
                row);
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "import_preview_ready",
            batchId,
            filename = file.FileName,
            worksheet = worksheet.Name,
            summary = new
            {
                totalRows = rows.Count,
                validRows = rows.Count(item =>
                    item.RowStatus == "valid"),
                invalidRows = rows.Count(item =>
                    item.RowStatus == "invalid"),
                newRows = rows.Count(item =>
                    item.ChangeType == "new"),
                changedRows = rows.Count(item =>
                    item.ChangeType == "changed"),
                duplicateRows = rows.Count(item =>
                    item.ChangeType == "duplicate")
            },
            rows = rows.Take(250).Select(item => new
            {
                item.SourceRowNumber,
                item.RowStatus,
                item.ChangeType,
                item.AccountExecutiveText,
                item.CustomerText,
                item.EngagementName,
                item.ContractManagerText,
                item.ValidationMessages
            })
        });
    }

    private static async Task<IResult> ConfirmImportAsync(
        Guid batchId,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or Project Team "
                + "Coordinator may confirm imports.");
        }

        await using var transaction =
            await connection.BeginTransactionAsync();

        await using (var command =
            new NpgsqlCommand("""
                SELECT import_status, invalid_rows
                FROM boh_balance_import_batches
                WHERE boh_balance_import_batch_id = @batch_id
                FOR UPDATE;
                """,
                connection,
                transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);

            await using var reader =
                await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    status = "not_found",
                    message = "The import preview was not found."
                });
            }

            if (reader.GetString(0) != "preview")
            {
                return Results.Conflict(new
                {
                    status = "invalid_import_state",
                    message = "Only a preview batch can be confirmed."
                });
            }

            if (reader.GetInt32(1) > 0)
            {
                return Results.BadRequest(new
                {
                    status = "invalid_rows_present",
                    message =
                        "Correct all invalid rows before confirming "
                        + "the import."
                });
            }
        }

        var rows =
            await LoadConfirmRowsAsync(
                connection,
                transaction,
                batchId);

        foreach (var row in rows)
        {
            var contractId =
                await UpsertContractAsync(
                    connection,
                    transaction,
                    batchId,
                    actor.Value,
                    row);

            await ReconcileImportedCreditAsync(
                connection,
                transaction,
                batchId,
                contractId,
                actor.Value,
                row);

            if (!string.IsNullOrWhiteSpace(row.Notes))
            {
                await UpsertNoteAsync(
                    connection,
                    transaction,
                    batchId,
                    contractId,
                    actor.Value,
                    row);
            }
        }

        await using (var closeMissingContracts =
            new NpgsqlCommand("""
                UPDATE boh_contracts c
                SET contract_status = 'closed',
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE c.import_batch_id IS NOT NULL
                  AND c.import_source_key <> ''
                  AND NOT EXISTS (
                      SELECT 1
                      FROM boh_balance_import_rows r
                      WHERE r.boh_balance_import_batch_id = @batch_id
                        AND r.row_status = 'valid'
                        AND r.change_type <> 'duplicate'
                        AND r.source_key = c.import_source_key
                  );
                """,
                connection,
                transaction))
        {
            closeMissingContracts.Parameters.AddWithValue(
                "batch_id",
                batchId);
            closeMissingContracts.Parameters.AddWithValue(
                "actor_id",
                actor.Value);

            await closeMissingContracts.ExecuteNonQueryAsync();
        }

        await using (var command =
            new NpgsqlCommand("""
                UPDATE boh_balance_import_batches
                SET is_active = FALSE,
                    import_status = CASE
                        WHEN import_status = 'confirmed'
                            THEN 'superseded'
                        ELSE import_status
                    END
                WHERE is_active = TRUE
                  AND boh_balance_import_batch_id <> @batch_id;

                UPDATE boh_balance_import_batches
                SET import_status = 'confirmed',
                    is_active = TRUE,
                    confirmed_by_user_id = @actor_id,
                    confirmed_at = NOW()
                WHERE boh_balance_import_batch_id = @batch_id;
                """,
                connection,
                transaction))
        {
            command.Parameters.AddWithValue("batch_id", batchId);
            command.Parameters.AddWithValue(
                "actor_id",
                actor.Value);

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "import_confirmed",
            batchId,
            importedRows = rows.Count
        });
    }

    private static async Task<IResult> GetScheduleAsync(
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "The email schedule is available only to Administrators, "
                + "Superadmins, and Project Team Coordinators.");
        }

        return Results.Ok(new
        {
            status = "email_schedule_loaded",
            schedule = await ScheduleAsync(connection)
        });
    }

    private static async Task<IResult> UpdateScheduleAsync(
        ScheduleRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await AccessAsync(connection, actor.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or Project Team "
                + "Coordinator may manage the email schedule.");
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
                    @threshold,
                    @warning_days,
                    @retention_months,
                    @actor_id,
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
                ?? "Weekly Prepaid Balance Summary");
        command.Parameters.AddWithValue(
            "body_introduction",
            request.BodyIntroduction?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "include_expired",
            request.IncludeExpired);
        command.Parameters.AddWithValue(
            "threshold",
            request.LowBalanceThresholdPercent);
        command.Parameters.AddWithValue(
            "warning_days",
            request.ExpirationWarningDays);
        command.Parameters.AddWithValue(
            "retention_months",
            request.RetentionMonths);
        command.Parameters.AddWithValue(
            "actor_id",
            actor.Value);

        await command.ExecuteNonQueryAsync();

        return Results.Ok(new
        {
            status = "email_schedule_updated"
        });
    }

    private static async Task<IResult> GetEligibleAsync(
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        if (!Guid.TryParse(
                context.Request.Query["clientId"],
                out var clientId))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "clientId is required."
            });
        }

        var workDate =
            DateOnly.TryParse(
                context.Request.Query["workDate"],
                out var parsed)
                ? parsed
                : DateOnly.FromDateTime(DateTime.UtcNow);

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        var rows = new List<object>();

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    boh_contract_id,
                    engagement_name,
                    po_quote,
                    certinia_id,
                    sell_quote,
                    salesforce_id,
                    contract_end_date,
                    total_available,
                    total_used,
                    remaining_balance,
                    balance_percent
                FROM vw_boh_prepaid_balance_rows
                WHERE client_id = @client_id
                  AND contract_status NOT IN ('cancelled', 'closed')
                  AND contract_start_date <= @work_date
                  AND contract_end_date >= @work_date
                ORDER BY remaining_balance DESC, engagement_name;
                """,
                connection);

        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("work_date", workDate);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new
            {
                contractId = reader.GetGuid(0),
                engagementName = reader.GetString(1),
                poQuote = reader.GetString(2),
                certiniaId = reader.GetString(3),
                sellQuote = reader.GetString(4),
                salesforceId = reader.GetString(5),
                contractEndDate =
                    DateOnly.FromDateTime(reader.GetDateTime(6)),
                totalAvailable = reader.GetDecimal(7),
                totalUsed = reader.GetDecimal(8),
                remainingBalance = reader.GetDecimal(9),
                balancePercent = reader.IsDBNull(10)
                    ? (decimal?)null
                    : reader.GetDecimal(10)
            });
        }

        return Results.Ok(new
        {
            status = "eligible_contracts_loaded",
            clientId,
            workDate,
            contracts = rows
        });
    }

    private static async Task<IResult> RecordUsageAsync(
        TimeUsageRequest request,
        HttpContext context)
    {
        var actor = SessionUserId(context);

        if (actor is null)
        {
            return SessionRequired();
        }

        if (request.TimeEntryId == Guid.Empty
            || request.ContractId == Guid.Empty
            || request.WorkDate == default
            || request.Hours <= 0
            || request.BillingRate < 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message =
                    "Time entry, contract, work date, positive hours, "
                    + "and non-negative billing rate are required."
            });
        }

        var usageStatus = UsageStatus(request.SourceStatus);
        var usageAmount = Math.Round(
            request.Hours * request.BillingRate,
            2,
            MidpointRounding.AwayFromZero);

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_usage_ledger (
                    boh_contract_id,
                    time_entry_id,
                    project_id,
                    task_id,
                    user_id,
                    work_date,
                    hours,
                    usage_status,
                    billing_classification,
                    is_overage,
                    source_status,
                    source_reference,
                    billing_rate,
                    usage_amount,
                    financial_status,
                    updated_at
                )
                VALUES (
                    @contract_id,
                    @time_entry_id,
                    @project_id,
                    @task_id,
                    @user_id,
                    @work_date,
                    @hours,
                    @usage_status,
                    @billing_classification,
                    FALSE,
                    @source_status,
                    @source_reference,
                    @billing_rate,
                    @usage_amount,
                    @usage_status,
                    NOW()
                )
                ON CONFLICT (time_entry_id)
                WHERE time_entry_id IS NOT NULL
                  AND usage_status NOT IN ('reversed', 'voided')
                DO UPDATE SET
                    boh_contract_id = EXCLUDED.boh_contract_id,
                    project_id = EXCLUDED.project_id,
                    task_id = EXCLUDED.task_id,
                    user_id = EXCLUDED.user_id,
                    work_date = EXCLUDED.work_date,
                    hours = EXCLUDED.hours,
                    usage_status = EXCLUDED.usage_status,
                    billing_classification =
                        EXCLUDED.billing_classification,
                    source_status = EXCLUDED.source_status,
                    source_reference = EXCLUDED.source_reference,
                    billing_rate = EXCLUDED.billing_rate,
                    usage_amount = EXCLUDED.usage_amount,
                    financial_status = EXCLUDED.financial_status,
                    updated_at = NOW()
                RETURNING boh_usage_ledger_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "contract_id",
            request.ContractId);
        command.Parameters.AddWithValue(
            "time_entry_id",
            request.TimeEntryId);
        command.Parameters.AddWithValue(
            "project_id",
            (object?)request.ProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "task_id",
            (object?)request.TaskId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "user_id",
            (object?)request.UserId ?? actor.Value);
        command.Parameters.AddWithValue(
            "work_date",
            request.WorkDate);
        command.Parameters.AddWithValue("hours", request.Hours);
        command.Parameters.AddWithValue(
            "usage_status",
            usageStatus);
        command.Parameters.AddWithValue(
            "billing_classification",
            request.BillingClassification?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "source_status",
            request.SourceStatus?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "source_reference",
            request.SourceReference?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "billing_rate",
            request.BillingRate);
        command.Parameters.AddWithValue(
            "usage_amount",
            usageAmount);

        var ledgerId =
            (Guid)(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException(
                    "Unable to record contract usage."));

        return Results.Ok(new
        {
            status = "time_usage_recorded",
            ledgerId,
            usageStatus,
            usageAmount,
            immediateBalanceImpact =
                usageStatus is "entered"
                    or "submitted"
                    or "consumed"
                    or "overage"
        });
    }

    private static Dictionary<string, int> BuildHeaderMap(
        IXLWorksheet worksheet)
    {
        var map = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        var lastColumn =
            worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var column = 1; column <= lastColumn; column++)
        {
            var text = worksheet
                .Cell(1, column)
                .GetFormattedString()
                .Trim();

            if (text.Length > 0)
            {
                map[NormalizeHeader(text)] = column;
            }
        }

        return map;
    }

    private static List<ImportRow> ParseRows(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, int> headers,
        IReadOnlyCollection<CustomerOption> customers,
        IReadOnlyCollection<UserOption> accountExecutives,
        IReadOnlyCollection<UserOption> coordinators,
        IReadOnlyCollection<UserOption> users,
        IReadOnlySet<string> existingSourceKeys)
    {
        var rows = new List<ImportRow>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            string Text(string header)
            {
                return headers.TryGetValue(
                        NormalizeHeader(header),
                        out var column)
                    ? worksheet.Cell(rowNumber, column)
                        .GetFormattedString()
                        .Trim()
                    : "";
            }

            decimal Number(string header)
            {
                if (!headers.TryGetValue(
                        NormalizeHeader(header),
                        out var column))
                {
                    return 0;
                }

                var cell = worksheet.Cell(rowNumber, column);

                if (cell.TryGetValue<decimal>(out var number))
                {
                    return number;
                }

                var text = cell.GetFormattedString()
                    .Replace("$", "")
                    .Replace(",", "")
                    .Replace("%", "")
                    .Trim();

                if (!decimal.TryParse(
                        text,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    return 0;
                }

                return header == "Balance %" && parsed > 1
                    ? parsed / 100
                    : parsed;
            }

            DateOnly? Date(string header)
            {
                if (!headers.TryGetValue(
                        NormalizeHeader(header),
                        out var column))
                {
                    return null;
                }

                var cell = worksheet.Cell(rowNumber, column);

                if (cell.TryGetValue<DateTime>(out var date))
                {
                    return DateOnly.FromDateTime(date);
                }

                return DateOnly.TryParse(
                        cell.GetFormattedString(),
                        out var parsed)
                    ? parsed
                    : null;
            }

            var aeText = Text("Account Executive");
            var customerText = Text("Customer");
            var engagement = Text("Engagement Name");
            var managerText = Text("Contract Manager");

            if (string.IsNullOrWhiteSpace(aeText)
                && string.IsNullOrWhiteSpace(customerText)
                && string.IsNullOrWhiteSpace(engagement)
                && string.IsNullOrWhiteSpace(managerText))
            {
                continue;
            }

            if (engagement.StartsWith(
                "TEMPLATE - BLOCK OF HOURS",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var poQuote = Text("PO/Quote");
            var fixedFeeItem = Text("Fixed Fee Item");
            var billingDate = Date("Billing Date");
            var sourceKey = SourceKey(
                customerText,
                engagement,
                poQuote,
                fixedFeeItem,
                billingDate?.ToString("yyyy-MM-dd") ?? "");

            var messages = new List<string>();
            var ae = MatchUser(accountExecutives, aeText);
            var customer = MatchCustomer(customers, customerText);
            var coordinator = MatchUser(coordinators, managerText);
            var creditByText = Text("Credit Awarded By");
            var creditBy = string.IsNullOrWhiteSpace(creditByText)
                ? null
                : MatchUser(users, creditByText);
            var startDate = Date("Contract Start Date");
            var endDate = Date("Contract End Date");
            var credit = Number("Credit Awarded");
            var creditDate = Date("Date Credit Awarded");

            if (ae is null)
            {
                messages.Add(
                    "Account Executive must match one active AE/Sales user.");
            }

            if (customer is null)
            {
                messages.Add(
                    "Customer must match one active Customer Directory record.");
            }

            if (coordinator is null)
            {
                messages.Add(
                    "Contract Manager must match one active Project Team Coordinator.");
            }

            if (string.IsNullOrWhiteSpace(engagement))
            {
                messages.Add("Engagement Name is required.");
            }

            if (startDate is null
                || endDate is null
                || endDate < startDate)
            {
                messages.Add(
                    "Valid Contract Start Date and Contract End Date are required.");
            }

            if (credit > 0 && creditDate is null)
            {
                messages.Add(
                    "Date Credit Awarded is required when credit is supplied.");
            }

            if (credit > 0 && creditBy is null)
            {
                messages.Add(
                    "Credit Awarded By must match one active ProjectPulse user.");
            }

            var changeType = "new";

            if (!seen.Add(sourceKey))
            {
                changeType = "duplicate";
                messages.Add("This row duplicates another workbook row.");
            }
            else if (existingSourceKeys.Contains(sourceKey))
            {
                changeType = "changed";
            }

            var pending = Number("Pending Hours");
            var approved = Number("Approved Hours");
            var totalHours = Number("Total Hours");

            if (totalHours == 0)
            {
                totalHours = pending + approved;
            }

            var expenses = Number("Total Expenses");
            var totalUsed = Number("Total Used");

            if (totalUsed == 0)
            {
                totalUsed = totalHours + expenses;
            }

            rows.Add(new ImportRow(
                rowNumber,
                sourceKey,
                messages.Count == 0 ? "valid" : "invalid",
                changeType,
                messages,
                aeText,
                customerText,
                engagement,
                managerText,
                poQuote,
                startDate,
                endDate,
                fixedFeeItem,
                Text("Latest Time Text"),
                billingDate,
                Number("FF Amount"),
                credit,
                creditDate,
                creditByText,
                pending,
                approved,
                totalHours,
                expenses,
                Number("Adjustments"),
                totalUsed,
                Number("Remaining Balance"),
                Number("Balance %"),
                Text("Certinia ID"),
                Text("SELL Quote"),
                Text("Salesforce ID"),
                Text("Notes"),
                ae?.UserId,
                customer?.ClientId,
                coordinator?.UserId,
                creditBy?.UserId));
        }

        return rows;
    }

    private static async Task InsertPreviewRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        ImportRow row)
    {
        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_balance_import_rows (
                    boh_balance_import_batch_id,
                    source_row_number,
                    source_key,
                    row_status,
                    change_type,
                    validation_messages_json,
                    account_executive_text,
                    customer_text,
                    engagement_name,
                    contract_manager_text,
                    po_quote,
                    contract_start_date,
                    contract_end_date,
                    fixed_fee_item,
                    latest_time_text,
                    billing_date,
                    fixed_fee_amount,
                    credit_awarded,
                    credit_awarded_on,
                    credit_awarded_by_text,
                    pending_amount,
                    approved_amount,
                    total_hours_amount,
                    total_expenses,
                    adjustments,
                    total_used,
                    remaining_balance,
                    balance_percent,
                    certinia_id,
                    sell_quote,
                    salesforce_id,
                    notes,
                    matched_account_executive_user_id,
                    matched_client_id,
                    matched_project_team_coordinator_user_id,
                    matched_credit_awarded_by_user_id
                )
                VALUES (
                    @batch_id,
                    @row_number,
                    @source_key,
                    @row_status,
                    @change_type,
                    @validation,
                    @ae_text,
                    @customer_text,
                    @engagement,
                    @manager_text,
                    @po_quote,
                    @start_date,
                    @end_date,
                    @fixed_fee_item,
                    @latest_time_text,
                    @billing_date,
                    @fixed_fee_amount,
                    @credit_awarded,
                    @credit_date,
                    @credit_by_text,
                    @pending,
                    @approved,
                    @total_hours,
                    @expenses,
                    @adjustments,
                    @total_used,
                    @remaining,
                    @balance_percent,
                    @certinia_id,
                    @sell_quote,
                    @salesforce_id,
                    @notes,
                    @ae_id,
                    @client_id,
                    @ptc_id,
                    @credit_by_id
                );
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue(
            "row_number",
            row.SourceRowNumber);
        command.Parameters.AddWithValue(
            "source_key",
            row.SourceKey);
        command.Parameters.AddWithValue(
            "row_status",
            row.RowStatus);
        command.Parameters.AddWithValue(
            "change_type",
            row.ChangeType);
        command.Parameters.AddWithValue(
            "validation",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(row.ValidationMessages));
        command.Parameters.AddWithValue(
            "ae_text",
            row.AccountExecutiveText);
        command.Parameters.AddWithValue(
            "customer_text",
            row.CustomerText);
        command.Parameters.AddWithValue(
            "engagement",
            row.EngagementName);
        command.Parameters.AddWithValue(
            "manager_text",
            row.ContractManagerText);
        command.Parameters.AddWithValue(
            "po_quote",
            row.PoQuote);
        command.Parameters.AddWithValue(
            "start_date",
            (object?)row.ContractStartDate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "end_date",
            (object?)row.ContractEndDate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "fixed_fee_item",
            row.FixedFeeItem);
        command.Parameters.AddWithValue(
            "latest_time_text",
            row.LatestTimeText);
        command.Parameters.AddWithValue(
            "billing_date",
            (object?)row.BillingDate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "fixed_fee_amount",
            row.FixedFeeAmount);
        command.Parameters.AddWithValue(
            "credit_awarded",
            row.CreditAwarded);
        command.Parameters.AddWithValue(
            "credit_date",
            (object?)row.CreditAwardedOn ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "credit_by_text",
            row.CreditAwardedByText);
        command.Parameters.AddWithValue("pending", row.PendingAmount);
        command.Parameters.AddWithValue("approved", row.ApprovedAmount);
        command.Parameters.AddWithValue(
            "total_hours",
            row.TotalHoursAmount);
        command.Parameters.AddWithValue("expenses", row.TotalExpenses);
        command.Parameters.AddWithValue(
            "adjustments",
            row.Adjustments);
        command.Parameters.AddWithValue(
            "total_used",
            row.TotalUsed);
        command.Parameters.AddWithValue(
            "remaining",
            row.RemainingBalance);
        command.Parameters.AddWithValue(
            "balance_percent",
            (object?)row.BalancePercent ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "certinia_id",
            row.CertiniaId);
        command.Parameters.AddWithValue(
            "sell_quote",
            row.SellQuote);
        command.Parameters.AddWithValue(
            "salesforce_id",
            row.SalesforceId);
        command.Parameters.AddWithValue("notes", row.Notes);
        command.Parameters.AddWithValue(
            "ae_id",
            (object?)row.AccountExecutiveUserId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "client_id",
            (object?)row.ClientId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "ptc_id",
            (object?)row.ProjectTeamCoordinatorUserId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "credit_by_id",
            (object?)row.CreditAwardedByUserId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<ImportRow>> LoadConfirmRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId)
    {
        var rows = new List<ImportRow>();

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    source_row_number,
                    source_key,
                    row_status,
                    change_type,
                    validation_messages_json,
                    account_executive_text,
                    customer_text,
                    engagement_name,
                    contract_manager_text,
                    po_quote,
                    contract_start_date,
                    contract_end_date,
                    fixed_fee_item,
                    latest_time_text,
                    billing_date,
                    fixed_fee_amount,
                    credit_awarded,
                    credit_awarded_on,
                    credit_awarded_by_text,
                    pending_amount,
                    approved_amount,
                    total_hours_amount,
                    total_expenses,
                    adjustments,
                    total_used,
                    remaining_balance,
                    balance_percent,
                    certinia_id,
                    sell_quote,
                    salesforce_id,
                    notes,
                    matched_account_executive_user_id,
                    matched_client_id,
                    matched_project_team_coordinator_user_id,
                    matched_credit_awarded_by_user_id
                FROM boh_balance_import_rows
                WHERE boh_balance_import_batch_id = @batch_id
                  AND row_status = 'valid'
                  AND change_type <> 'duplicate'
                ORDER BY source_row_number;
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue("batch_id", batchId);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ImportRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                JsonSerializer.Deserialize<List<string>>(
                    reader.GetString(4)) ?? new List<string>(),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.IsDBNull(10)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(10)),
                reader.IsDBNull(11)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(11)),
                reader.GetString(12),
                reader.GetString(13),
                reader.IsDBNull(14)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(14)),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                reader.IsDBNull(17)
                    ? null
                    : DateOnly.FromDateTime(reader.GetDateTime(17)),
                reader.GetString(18),
                reader.GetDecimal(19),
                reader.GetDecimal(20),
                reader.GetDecimal(21),
                reader.GetDecimal(22),
                reader.GetDecimal(23),
                reader.GetDecimal(24),
                reader.GetDecimal(25),
                reader.IsDBNull(26)
                    ? null
                    : reader.GetDecimal(26),
                reader.GetString(27),
                reader.GetString(28),
                reader.GetString(29),
                reader.GetString(30),
                reader.IsDBNull(31)
                    ? null
                    : reader.GetGuid(31),
                reader.IsDBNull(32)
                    ? null
                    : reader.GetGuid(32),
                reader.IsDBNull(33)
                    ? null
                    : reader.GetGuid(33),
                reader.IsDBNull(34)
                    ? null
                    : reader.GetGuid(34)));
        }

        return rows;
    }

    private static async Task<Guid> UpsertContractAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        Guid actorId,
        ImportRow row)
    {
        if (row.ClientId is null
            || row.AccountExecutiveUserId is null
            || row.ProjectTeamCoordinatorUserId is null
            || row.ContractStartDate is null
            || row.ContractEndDate is null)
        {
            throw new InvalidOperationException(
                "A valid import row is missing system references.");
        }

        var available =
            row.FixedFeeAmount
            + row.CreditAwarded
            + row.Adjustments;

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contracts (
                    client_id,
                    contract_name,
                    contract_status,
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
                    balance_unit,
                    fixed_fee_item,
                    latest_time_text,
                    billing_date,
                    fixed_fee_amount,
                    imported_pending_amount,
                    imported_approved_amount,
                    total_expenses,
                    manual_adjustments,
                    import_source_key,
                    import_snapshot_at,
                    import_batch_id,
                    created_by_user_id,
                    updated_by_user_id,
                    updated_at
                )
                VALUES (
                    @client_id,
                    @engagement,
                    @status,
                    @ae_id,
                    @ptc_id,
                    0,
                    @start_date,
                    @end_date,
                    @end_date,
                    TRUE,
                    TRUE,
                    TRUE,
                    TRUE,
                    @certinia_id,
                    @sell_quote,
                    @salesforce_id,
                    @po_quote,
                    'currency',
                    @fixed_fee_item,
                    @latest_time_text,
                    @billing_date,
                    @fixed_fee_amount,
                    @pending,
                    @approved,
                    @expenses,
                    @adjustments,
                    @source_key,
                    NOW(),
                    @batch_id,
                    @actor_id,
                    @actor_id,
                    NOW()
                )
                ON CONFLICT (import_source_key)
                WHERE import_source_key <> ''
                DO UPDATE SET
                    client_id = EXCLUDED.client_id,
                    contract_name = EXCLUDED.contract_name,
                    contract_status = EXCLUDED.contract_status,
                    primary_account_executive_user_id =
                        EXCLUDED.primary_account_executive_user_id,
                    project_team_coordinator_user_id =
                        EXCLUDED.project_team_coordinator_user_id,
                    start_date = EXCLUDED.start_date,
                    original_expiration_date =
                        EXCLUDED.original_expiration_date,
                    effective_expiration_date =
                        EXCLUDED.effective_expiration_date,
                    certinia_id = EXCLUDED.certinia_id,
                    sell_quote = EXCLUDED.sell_quote,
                    salesforce_id = EXCLUDED.salesforce_id,
                    purchase_order_reference =
                        EXCLUDED.purchase_order_reference,
                    fixed_fee_item = EXCLUDED.fixed_fee_item,
                    latest_time_text = EXCLUDED.latest_time_text,
                    billing_date = EXCLUDED.billing_date,
                    fixed_fee_amount = EXCLUDED.fixed_fee_amount,
                    imported_pending_amount =
                        EXCLUDED.imported_pending_amount,
                    imported_approved_amount =
                        EXCLUDED.imported_approved_amount,
                    total_expenses = EXCLUDED.total_expenses,
                    manual_adjustments = EXCLUDED.manual_adjustments,
                    import_snapshot_at = NOW(),
                    import_batch_id = EXCLUDED.import_batch_id,
                    updated_by_user_id = EXCLUDED.updated_by_user_id,
                    updated_at = NOW()
                RETURNING boh_contract_id;
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "client_id",
            row.ClientId.Value);
        command.Parameters.AddWithValue(
            "engagement",
            row.EngagementName);
        command.Parameters.AddWithValue(
            "status",
            Status(
                row.ContractEndDate.Value,
                available - row.TotalUsed,
                available));
        command.Parameters.AddWithValue(
            "ae_id",
            row.AccountExecutiveUserId.Value);
        command.Parameters.AddWithValue(
            "ptc_id",
            row.ProjectTeamCoordinatorUserId.Value);
        command.Parameters.AddWithValue(
            "start_date",
            row.ContractStartDate.Value);
        command.Parameters.AddWithValue(
            "end_date",
            row.ContractEndDate.Value);
        command.Parameters.AddWithValue(
            "certinia_id",
            row.CertiniaId);
        command.Parameters.AddWithValue(
            "sell_quote",
            row.SellQuote);
        command.Parameters.AddWithValue(
            "salesforce_id",
            row.SalesforceId);
        command.Parameters.AddWithValue("po_quote", row.PoQuote);
        command.Parameters.AddWithValue(
            "fixed_fee_item",
            row.FixedFeeItem);
        command.Parameters.AddWithValue(
            "latest_time_text",
            row.LatestTimeText);
        command.Parameters.AddWithValue(
            "billing_date",
            (object?)row.BillingDate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "fixed_fee_amount",
            row.FixedFeeAmount);
        command.Parameters.AddWithValue("pending", row.PendingAmount);
        command.Parameters.AddWithValue("approved", row.ApprovedAmount);
        command.Parameters.AddWithValue("expenses", row.TotalExpenses);
        command.Parameters.AddWithValue(
            "adjustments",
            row.Adjustments);
        command.Parameters.AddWithValue(
            "source_key",
            row.SourceKey);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("actor_id", actorId);

        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException(
                "Unable to import the contract row."));
    }

    private static async Task ReconcileImportedCreditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        Guid contractId,
        Guid actorId,
        ImportRow row)
    {
        decimal currentNetCredit;

        await using (var current =
            new NpgsqlCommand("""
                SELECT COALESCE(
                    SUM(
                        CASE
                            WHEN adjustment_type = 'credit_awarded'
                                THEN COALESCE(amount, hours)
                            WHEN adjustment_type = 'credit_reversal'
                                THEN -COALESCE(amount, hours)
                            ELSE 0
                        END
                    ),
                    0
                )
                FROM boh_contract_adjustments
                WHERE boh_contract_id = @contract_id;
                """,
                connection,
                transaction))
        {
            current.Parameters.AddWithValue(
                "contract_id",
                contractId);

            currentNetCredit = Convert.ToDecimal(
                await current.ExecuteScalarAsync() ?? 0,
                CultureInfo.InvariantCulture);
        }

        var difference =
            Math.Round(
                row.CreditAwarded - currentNetCredit,
                2,
                MidpointRounding.AwayFromZero);

        if (difference == 0)
        {
            return;
        }

        var adjustmentType =
            difference > 0
                ? "credit_awarded"
                : "credit_reversal";

        var amount = Math.Abs(difference);

        var source =
            $"import:{batchId}:{row.SourceRowNumber}:credit-reconciliation";

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_adjustments (
                    boh_contract_id,
                    adjustment_type,
                    hours,
                    amount,
                    awarded_on,
                    reason,
                    source_reference,
                    created_by_user_id
                )
                SELECT
                    @contract_id,
                    @adjustment_type,
                    @amount,
                    @amount,
                    @awarded_on,
                    @reason,
                    @source,
                    @awarded_by
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM boh_contract_adjustments
                    WHERE source_reference = @source
                );
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "contract_id",
            contractId);
        command.Parameters.AddWithValue(
            "adjustment_type",
            adjustmentType);
        command.Parameters.AddWithValue(
            "amount",
            amount);
        command.Parameters.AddWithValue(
            "awarded_on",
            row.CreditAwardedOn
                ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue(
            "reason",
            adjustmentType == "credit_awarded"
                ? "Imported credit reconciliation increase"
                : "Imported credit reconciliation reversal");
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue(
            "awarded_by",
            row.CreditAwardedByUserId ?? actorId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertNoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        Guid contractId,
        Guid actorId,
        ImportRow row)
    {
        var source =
            $"import:{batchId}:{row.SourceRowNumber}:note";

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_notes (
                    boh_contract_id,
                    note_text,
                    note_category,
                    source_reference,
                    created_by_user_id
                )
                SELECT
                    @contract_id,
                    @note,
                    'import',
                    @source,
                    @actor_id
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM boh_contract_notes
                    WHERE source_reference = @source
                );
                """,
                connection,
                transaction);

        command.Parameters.AddWithValue(
            "contract_id",
            contractId);
        command.Parameters.AddWithValue("note", row.Notes);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("actor_id", actorId);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<HashSet<string>>
        ExistingSourceKeysAsync(
            NpgsqlConnection connection)
    {
        var keys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        await using var command =
            new NpgsqlCommand("""
                SELECT import_source_key
                FROM boh_contracts
                WHERE import_source_key <> '';
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    private static async Task<List<CustomerOption>>
        CustomersAsync(NpgsqlConnection connection)
    {
        var rows = new List<CustomerOption>();

        await using var command =
            new NpgsqlCommand("""
                SELECT client_id, client_name
                FROM clients
                WHERE is_active = TRUE
                ORDER BY client_name;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new CustomerOption(
                reader.GetGuid(0),
                reader.GetString(1)));
        }

        return rows;
    }

    private static async Task<List<UserOption>> UsersAsync(
        NpgsqlConnection connection,
        string[] roles,
        string[] terms)
    {
        var rows = new List<UserOption>();

        await using var command =
            new NpgsqlCommand("""
                SELECT DISTINCT
                    u.user_id,
                    COALESCE(NULLIF(u.display_name, ''), u.email),
                    u.email
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
                        r.role_code = ANY(@roles)
                     OR EXISTS (
                            SELECT 1
                            FROM UNNEST(@terms) AS t(term)
                            WHERE LOWER(
                                COALESCE(u.job_title, '') || ' '
                                || COALESCE(u.department_name, '') || ' '
                                || COALESCE(u.department, '') || ' '
                                || COALESCE(u.team_name, '')
                            )
                            LIKE '%' || LOWER(t.term) || '%'
                        )
                  )
                ORDER BY 2, u.email;
                """,
                connection);

        command.Parameters.AddWithValue("roles", roles);
        command.Parameters.AddWithValue("terms", terms);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new UserOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<List<UserOption>>
        AllUsersAsync(NpgsqlConnection connection)
    {
        var rows = new List<UserOption>();

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    user_id,
                    COALESCE(NULLIF(display_name, ''), email),
                    email
                FROM app_users
                WHERE is_active = TRUE
                  AND COALESCE(login_enabled, TRUE) = TRUE
                ORDER BY 2, email;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new UserOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static async Task<object> ScheduleAsync(
        NpgsqlConnection connection)
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
                    retention_months
                FROM boh_email_schedule
                WHERE schedule_key = 'weekly-balance';
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new
            {
                isEnabled = false,
                weekdayIso = 1,
                sendTime = "08:00",
                timeZone = "America/Chicago",
                subjectTemplate = "Weekly Prepaid Balance Summary",
                bodyIntroduction = "",
                includeExpired = false,
                lowBalanceThresholdPercent = 25m,
                expirationWarningDays = 90,
                retentionMonths = 24
            };
        }

        return new
        {
            isEnabled = reader.GetBoolean(0),
            weekdayIso = reader.GetInt32(1),
            sendTime =
                TimeOnly.FromTimeSpan(reader.GetTimeSpan(2))
                    .ToString("HH:mm"),
            timeZone = reader.GetString(3),
            subjectTemplate = reader.GetString(4),
            bodyIntroduction = reader.GetString(5),
            includeExpired = reader.GetBoolean(6),
            lowBalanceThresholdPercent = reader.GetDecimal(7),
            expirationWarningDays = reader.GetInt32(8),
            retentionMonths = reader.GetInt32(9)
        };
    }

    private static async Task<AccessResult> AccessAsync(
        NpgsqlConnection connection,
        Guid userId)
    {
        var roles =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    COALESCE(u.job_title, ''),
                    COALESCE(u.department_name, ''),
                    COALESCE(u.department, ''),
                    COALESCE(u.team_name, ''),
                    COALESCE(
                        STRING_AGG(
                            DISTINCT r.role_code,
                            ',' ORDER BY r.role_code
                        ) FILTER (
                            WHERE ura.is_active = TRUE
                              AND r.is_active = TRUE
                        ),
                        ''
                    )
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                WHERE u.user_id = @user_id
                  AND u.is_active = TRUE
                  AND COALESCE(u.login_enabled, TRUE) = TRUE
                GROUP BY
                    u.user_id,
                    u.job_title,
                    u.department_name,
                    u.department,
                    u.team_name;
                """,
                connection);

        command.Parameters.AddWithValue("user_id", userId);

        await using var reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new AccessResult(false, false);
        }

        var profile = string.Join(
            " ",
            Enumerable.Range(0, 4)
                .Select(reader.GetString))
            .ToLowerInvariant();

        foreach (var role in reader.GetString(4)
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries))
        {
            roles.Add(role);
        }

        var canManage =
            roles.Overlaps(ManageRoles)
            || profile.Contains("administrator")
            || profile.Contains("superadmin")
            || profile.Contains("project team coordinator")
            || profile.Contains("project coordinator");

        var canView =
            canManage
            || roles.Overlaps(new[]
            {
                "SALES",
                "ACCOUNT_EXECUTIVE",
                "ACCOUNT_MANAGER",
                "EXECUTIVE",
                "EXECUTIVE_LEADERSHIP"
            })
            || profile.Contains("account executive")
            || profile.Contains("account manager")
            || profile.Contains("sales")
            || profile.Contains("executive");

        return new AccessResult(canView, canManage);
    }

    private static UserOption? MatchUser(
        IReadOnlyCollection<UserOption> users,
        string value)
    {
        var normalized = Normalize(value);
        var matches = users.Where(item =>
                Normalize(item.DisplayName) == normalized
                || Normalize(item.Email) == normalized)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static CustomerOption? MatchCustomer(
        IReadOnlyCollection<CustomerOption> customers,
        string value)
    {
        var normalized = Normalize(value);
        var matches = customers.Where(item =>
                Normalize(item.CustomerName) == normalized)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static async Task<bool> IsMatchingUserAsync(
        NpgsqlConnection connection,
        Guid userId,
        string[] roles,
        string[] terms)
    {
        return (await UsersAsync(connection, roles, terms))
            .Any(item => item.UserId == userId);
    }

    private static async Task<bool> IsActiveCustomerAsync(
        NpgsqlConnection connection,
        Guid clientId)
    {
        await using var command =
            new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM clients
                    WHERE client_id = @client_id
                      AND is_active = TRUE
                );
                """,
                connection);

        command.Parameters.AddWithValue("client_id", clientId);

        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task InsertNoteAsync(
        NpgsqlConnection connection,
        Guid contractId,
        Guid userId,
        string category,
        string note,
        string source)
    {
        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_notes (
                    boh_contract_id,
                    note_text,
                    note_category,
                    source_reference,
                    created_by_user_id
                )
                VALUES (
                    @contract_id,
                    @note,
                    @category,
                    @source,
                    @user_id
                );
                """,
                connection);

        command.Parameters.AddWithValue("contract_id", contractId);
        command.Parameters.AddWithValue("note", note);
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("user_id", userId);

        await command.ExecuteNonQueryAsync();
    }

    private static string Status(
        DateOnly endDate,
        decimal remaining,
        decimal available)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (endDate < today)
        {
            return "expired";
        }

        if (remaining <= 0)
        {
            return "exhausted";
        }

        if (endDate.DayNumber - today.DayNumber <= 90)
        {
            return "expiring";
        }

        if (available > 0
            && remaining / available <= 0.25m)
        {
            return "low_balance";
        }

        return "active";
    }

    private static string UsageStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "draft" => "entered",
            "entered" => "entered",
            "submitted" => "submitted",
            "approved" => "consumed",
            "consumed" => "consumed",
            "rejected" => "rejected",
            "declined" => "declined",
            "deleted" => "voided",
            "voided" => "voided",
            "reversed" => "reversed",
            "overage" => "overage",
            _ => "entered"
        };
    }

    private static string NormalizeHeader(string value)
    {
        return string.Concat(
            value.Where(char.IsLetterOrDigit))
            .ToLowerInvariant();
    }

    private static string Normalize(string value)
    {
        return string.Join(
            " ",
            value.Trim()
                .ToLowerInvariant()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    private static string SourceKey(params string[] values)
    {
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        string.Join("|", values.Select(Normalize)))))
            .ToLowerInvariant();
    }

    private static Guid? SessionUserId(HttpContext context)
    {
        return context.Items.TryGetValue(
                "ProjectPulseSessionUserId",
                out var value)
            && value is Guid userId
                ? userId
                : null;
    }

    private static IResult SessionRequired()
    {
        return Results.Json(
            new
            {
                status = "session_required",
                message = "A ProjectPulse session is required."
            },
            statusCode: 401);
    }

    private static IResult Forbidden(string message)
    {
        return Results.Json(
            new
            {
                status = "forbidden",
                message
            },
            statusCode: 403);
    }

    private static string ConnectionString()
    {
        var host =
            Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port =
            Environment.GetEnvironmentVariable("PTP_DB_PORT")
            ?? "5432";
        var database =
            Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username =
            Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password =
            Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        if (!string.IsNullOrWhiteSpace(host)
            && !string.IsNullOrWhiteSpace(database)
            && !string.IsNullOrWhiteSpace(username)
            && !string.IsNullOrWhiteSpace(password))
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = int.TryParse(
                    port,
                    out var parsedPort)
                        ? parsedPort
                        : 5432,
                Database = database,
                Username = username,
                Password = password,
                SslMode = SslMode.Require
            }.ConnectionString;
        }

        throw new InvalidOperationException(
            "Missing database connection configuration.");
    }

    private sealed record AccessResult(
        bool CanView,
        bool CanManage);

    private sealed record CustomerOption(
        Guid ClientId,
        string CustomerName);

    private sealed record UserOption(
        Guid UserId,
        string DisplayName,
        string Email);

    private sealed record CreateRequest(
        Guid ClientId,
        Guid AccountExecutiveUserId,
        Guid ProjectTeamCoordinatorUserId,
        string EngagementName,
        string? PoQuote,
        DateOnly ContractStartDate,
        DateOnly ContractEndDate,
        string? FixedFeeItem,
        string? LatestTimeText,
        DateOnly? BillingDate,
        decimal FixedFeeAmount,
        decimal PendingAmount,
        decimal ApprovedAmount,
        decimal TotalExpenses,
        decimal Adjustments,
        string? CertiniaId,
        string? SellQuote,
        string? SalesforceId,
        string? Notes);

    private sealed record ReverseCreditRequest(
        DateOnly ReversedOn,
        string Reason);

    private sealed record ScheduleRequest(
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

    private sealed record TimeUsageRequest(
        Guid TimeEntryId,
        Guid ContractId,
        Guid? ProjectId,
        Guid? TaskId,
        Guid? UserId,
        DateOnly WorkDate,
        decimal Hours,
        decimal BillingRate,
        string? SourceStatus,
        string? BillingClassification,
        string? SourceReference);

    private sealed record ImportRow(
        int SourceRowNumber,
        string SourceKey,
        string RowStatus,
        string ChangeType,
        List<string> ValidationMessages,
        string AccountExecutiveText,
        string CustomerText,
        string EngagementName,
        string ContractManagerText,
        string PoQuote,
        DateOnly? ContractStartDate,
        DateOnly? ContractEndDate,
        string FixedFeeItem,
        string LatestTimeText,
        DateOnly? BillingDate,
        decimal FixedFeeAmount,
        decimal CreditAwarded,
        DateOnly? CreditAwardedOn,
        string CreditAwardedByText,
        decimal PendingAmount,
        decimal ApprovedAmount,
        decimal TotalHoursAmount,
        decimal TotalExpenses,
        decimal Adjustments,
        decimal TotalUsed,
        decimal RemainingBalance,
        decimal? BalancePercent,
        string CertiniaId,
        string SellQuote,
        string SalesforceId,
        string Notes,
        Guid? AccountExecutiveUserId,
        Guid? ClientId,
        Guid? ProjectTeamCoordinatorUserId,
        Guid? CreditAwardedByUserId);
}
