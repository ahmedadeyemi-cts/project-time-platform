using System.Globalization;
using ClosedXML.Excel;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class ContractsPrepaidModule
{
    private static readonly string[] ManagementRoleCodes =
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

    private static readonly string[] ReadOnlyRoleCodes =
    {
        "SALES",
        "ACCOUNT_EXECUTIVE",
        "ACCOUNT_MANAGER",
        "EXECUTIVE",
        "EXECUTIVE_LEADERSHIP"
    };

    private static readonly string[] WorkbookHeaders =
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
        "Credit Awarded",
        "Date Credit Awarded",
        "Credit Awarded By",
        "Pending Hours",
        "Approved Hours",
        "Total Hours",
        "Total Expenses",
        "Adjustments",
        "Total Used",
        "Remaining Balance",
        "Balance %",
        "Certinia ID",
        "SELL Quote",
        "Salesforce ID",
        "Notes"
    };

    public static WebApplication MapContractsPrepaidModule(
        this WebApplication app)
    {
        app.MapGet(
            "/api/contracts/prepaid/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);

        app.MapGet(
            "/api/contracts/prepaid/template",
            (Func<HttpContext, Task<IResult>>)DownloadTemplateAsync);

        app.MapGet(
            "/api/contracts/prepaid/export",
            (Func<HttpContext, Task<IResult>>)DownloadWorkbookAsync);

        app.MapPost(
            "/api/contracts/prepaid/{contractId:guid}/credits",
            AwardCreditAsync);

        app.MapPost(
            "/api/contracts/prepaid/{contractId:guid}/notes",
            AddNoteAsync);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(
        HttpContext context)
    {
        var actorUserId = SessionUserId(context);

        if (actorUserId is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        var access = await LoadAccessAsync(
            connection,
            actorUserId.Value);

        if (!access.CanView)
        {
            return Forbidden(
                "Contracts are available to Administrators, "
                + "Project Team Coordinators, Sales, Account Executives, "
                + "and Executive users.");
        }

        var rows = await LoadRowsAsync(connection);

        var grouped = rows
            .GroupBy(row => new
            {
                row.AccountExecutiveUserId,
                row.AccountExecutiveName
            })
            .Select(group => new
            {
                accountExecutiveUserId =
                    group.Key.AccountExecutiveUserId,
                accountExecutiveName =
                    group.Key.AccountExecutiveName,
                contractCount = group.Count(),
                fixedFeeAmount =
                    group.Sum(item => item.FixedFeeAmount),
                creditAwarded =
                    group.Sum(item => item.CreditAwarded),
                totalUsed =
                    group.Sum(item => item.TotalUsed),
                remainingBalance =
                    group.Sum(item => item.RemainingBalance),
                lowBalanceCount =
                    group.Count(item =>
                        item.BalancePercent is not null
                        && item.BalancePercent > 0
                        && item.BalancePercent <= 0.25m),
                contracts = group
            })
            .OrderBy(group => group.accountExecutiveName)
            .ToArray();

        return Results.Ok(new
        {
            status = "prepaid_contracts_loaded",
            permissions = new
            {
                canView = access.CanView,
                canManage = access.CanManage,
                canUpload = access.CanManage,
                canDownload = access.CanManage,
                canAwardCredit = access.CanManage,
                canAddNote = access.CanManage,
                canViewSchedule = access.CanManage,
                canManageSchedule = access.CanManage,
                readOnly = !access.CanManage
            },
            summary = new
            {
                contractCount = rows.Count,
                fixedFeeAmount =
                    rows.Sum(item => item.FixedFeeAmount),
                creditAwarded =
                    rows.Sum(item => item.CreditAwarded),
                totalAvailable =
                    rows.Sum(item => item.TotalAvailable),
                pendingAmount =
                    rows.Sum(item => item.PendingAmount),
                approvedAmount =
                    rows.Sum(item => item.ApprovedAmount),
                totalExpenses =
                    rows.Sum(item => item.TotalExpenses),
                totalUsed =
                    rows.Sum(item => item.TotalUsed),
                remainingBalance =
                    rows.Sum(item => item.RemainingBalance)
            },
            groups = grouped,
            rows
        });
    }

    private static async Task<IResult> DownloadTemplateAsync(
        HttpContext context)
    {
        var actorUserId = SessionUserId(context);

        if (actorUserId is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actorUserId.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or "
                + "Project Team Coordinator may download the template.");
        }

        return Results.File(
            BuildWorkbook(Array.Empty<PrepaidRow>(), true),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Module-060-Prepaid-Balance-Import-Template.xlsx");
    }

    private static async Task<IResult> DownloadWorkbookAsync(
        HttpContext context)
    {
        var actorUserId = SessionUserId(context);

        if (actorUserId is null)
        {
            return SessionRequired();
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actorUserId.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or "
                + "Project Team Coordinator may download XLSX reports.");
        }

        var rows = await LoadRowsAsync(connection);

        return Results.File(
            BuildWorkbook(rows, false),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"OneNeck-Prepaid-Balance-Summary-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    private static async Task<IResult> AwardCreditAsync(
        Guid contractId,
        CreditRequest request,
        HttpContext context)
    {
        var actorUserId = SessionUserId(context);

        if (actorUserId is null)
        {
            return SessionRequired();
        }

        if (request.Amount <= 0
            || request.AwardedOn == default
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message =
                    "A positive credit amount, award date, and reason "
                    + "are required."
            });
        }

        await using var connection =
            new NpgsqlConnection(ConnectionString());

        await connection.OpenAsync();

        if (!(await LoadAccessAsync(
                connection,
                actorUserId.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or "
                + "Project Team Coordinator may award credits.");
        }

        await using var command =
            new NpgsqlCommand("""
                INSERT INTO boh_contract_adjustments (
                    boh_contract_id,
                    adjustment_type,
                    hours,
                    amount,
                    awarded_on,
                    reason,
                    customer_satisfaction_reference,
                    source_reference,
                    created_by_user_id
                )
                VALUES (
                    @contract_id,
                    'credit_awarded',
                    @amount,
                    @amount,
                    @awarded_on,
                    @reason,
                    @reference,
                    '',
                    @actor_user_id
                )
                RETURNING boh_contract_adjustment_id;
                """,
                connection);

        command.Parameters.AddWithValue(
            "contract_id",
            contractId);
        command.Parameters.AddWithValue(
            "amount",
            request.Amount);
        command.Parameters.AddWithValue(
            "awarded_on",
            request.AwardedOn);
        command.Parameters.AddWithValue(
            "reason",
            request.Reason.Trim());
        command.Parameters.AddWithValue(
            "reference",
            request.Reference?.Trim() ?? "");
        command.Parameters.AddWithValue(
            "actor_user_id",
            actorUserId.Value);

        var adjustmentId =
            (Guid)(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException(
                    "Credit creation did not return an identifier."));

        return Results.Ok(new
        {
            status = "credit_awarded",
            adjustmentId,
            awardedOn = request.AwardedOn,
            awardedByUserId = actorUserId.Value
        });
    }

    private static async Task<IResult> AddNoteAsync(
        Guid contractId,
        NoteRequest request,
        HttpContext context)
    {
        var actorUserId = SessionUserId(context);

        if (actorUserId is null)
        {
            return SessionRequired();
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
                actorUserId.Value)).CanManage)
        {
            return Forbidden(
                "Only an Administrator, Superadmin, or "
                + "Project Team Coordinator may add notes.");
        }

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
                    @note_text,
                    @category,
                    '',
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
            "category",
            request.Category?.Trim() ?? "general");
        command.Parameters.AddWithValue(
            "actor_user_id",
            actorUserId.Value);

        await command.ExecuteNonQueryAsync();

        return Results.Ok(new
        {
            status = "note_added"
        });
    }

    private static async Task<List<PrepaidRow>>
        LoadRowsAsync(NpgsqlConnection connection)
    {
        var rows = new List<PrepaidRow>();

        await using var command =
            new NpgsqlCommand("""
                SELECT
                    boh_contract_id,
                    client_id,
                    customer_name,
                    engagement_name,
                    contract_status,
                    primary_account_executive_user_id,
                    account_executive_name,
                    project_team_coordinator_user_id,
                    contract_manager_name,
                    po_quote,
                    contract_start_date,
                    contract_end_date,
                    fixed_fee_item,
                    latest_time_text,
                    billing_date,
                    fixed_fee_amount,
                    credit_awarded,
                    latest_credit_awarded_on,
                    latest_credit_awarded_by,
                    pending_amount,
                    approved_amount,
                    total_hours_amount,
                    total_expenses,
                    adjustments,
                    total_used,
                    total_available,
                    remaining_balance,
                    balance_percent,
                    certinia_id,
                    sell_quote,
                    salesforce_id,
                    note_count,
                    latest_note
                FROM vw_boh_prepaid_balance_rows
                ORDER BY
                    account_executive_name,
                    customer_name,
                    engagement_name,
                    billing_date NULLS LAST;
                """,
                connection);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new PrepaidRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.GetGuid(7),
                reader.GetString(8),
                reader.GetString(9),
                DateOnly.FromDateTime(reader.GetDateTime(10)),
                DateOnly.FromDateTime(reader.GetDateTime(11)),
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
                reader.GetDecimal(26),
                reader.IsDBNull(27)
                    ? null
                    : reader.GetDecimal(27),
                reader.GetString(28),
                reader.GetString(29),
                reader.GetString(30),
                reader.GetInt32(31),
                reader.GetString(32)));
        }

        return rows;
    }

    private static byte[] BuildWorkbook(
        IReadOnlyCollection<PrepaidRow> rows,
        bool templateOnly)
    {
        using var workbook = new XLWorkbook();

        var worksheet =
            workbook.Worksheets.Add(
                templateOnly
                    ? "Prepaid Balance Import"
                    : "Prepaid Balance Summary");

        for (var index = 0;
            index < WorkbookHeaders.Length;
            index++)
        {
            worksheet.Cell(1, index + 1).Value =
                WorkbookHeaders[index];
        }

        var header =
            worksheet.Range(
                1,
                1,
                1,
                WorkbookHeaders.Length);

        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor =
            XLColor.FromHtml("#0B5CAB");
        header.Style.Alignment.WrapText = true;
        worksheet.Row(1).Height = 34;

        var rowNumber = 2;

        if (templateOnly)
        {
            worksheet.Cell(rowNumber, 17).FormulaA1 =
                $"=SUM(O{rowNumber}:P{rowNumber})";
            worksheet.Cell(rowNumber, 20).FormulaA1 =
                $"=SUM(Q{rowNumber}:R{rowNumber})";
            worksheet.Cell(rowNumber, 21).FormulaA1 =
                $"=K{rowNumber}+L{rowNumber}+S{rowNumber}-T{rowNumber}";
            worksheet.Cell(rowNumber, 22).FormulaA1 =
                $"=IFERROR(U{rowNumber}/(K{rowNumber}+L{rowNumber}+S{rowNumber}),\"\")";
        }
        else
        {
            foreach (var group in rows.GroupBy(item =>
                item.AccountExecutiveName))
            {
                worksheet.Cell(rowNumber, 1).Value =
                    $"Account Executive: {group.Key}";
                worksheet.Range(
                        rowNumber,
                        1,
                        rowNumber,
                        WorkbookHeaders.Length)
                    .Merge();

                worksheet.Cell(rowNumber, 1)
                    .Style.Font.Bold = true;
                worksheet.Cell(rowNumber, 1)
                    .Style.Fill.BackgroundColor =
                        XLColor.FromHtml("#D9EAF7");

                rowNumber++;

                foreach (var item in group)
                {
                    object?[] values =
                    {
                        item.AccountExecutiveName,
                        item.CustomerName,
                        item.EngagementName,
                        item.ContractManagerName,
                        item.PoQuote,
                        item.ContractStartDate.ToDateTime(TimeOnly.MinValue),
                        item.ContractEndDate.ToDateTime(TimeOnly.MinValue),
                        item.FixedFeeItem,
                        item.LatestTimeText,
                        item.BillingDate?.ToDateTime(TimeOnly.MinValue),
                        item.FixedFeeAmount,
                        item.CreditAwarded,
                        item.LatestCreditAwardedOn?.ToDateTime(TimeOnly.MinValue),
                        item.LatestCreditAwardedBy,
                        item.PendingAmount,
                        item.ApprovedAmount,
                        item.TotalHoursAmount,
                        item.TotalExpenses,
                        item.Adjustments,
                        item.TotalUsed,
                        item.RemainingBalance,
                        item.BalancePercent,
                        item.CertiniaId,
                        item.SellQuote,
                        item.SalesforceId,
                        item.LatestNote
                    };

                    for (var index = 0;
                        index < values.Length;
                        index++)
                    {
                        worksheet.Cell(
                            rowNumber,
                            index + 1).Value =
                                XLCellValue.FromObject(values[index]);
                    }

                    rowNumber++;
                }

                rowNumber++;
            }
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.SheetView.FreezeColumns(4);
        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.Columns(6, 7)
            .Style.DateFormat.Format = "mm/dd/yyyy";
        worksheet.Columns(10, 10)
            .Style.DateFormat.Format = "mm/dd/yyyy";
        worksheet.Columns(13, 13)
            .Style.DateFormat.Format = "mm/dd/yyyy";
        worksheet.Columns(11, 21)
            .Style.NumberFormat.Format =
                "$#,##0.00;[Red]-$#,##0.00";
        worksheet.Column(22)
            .Style.NumberFormat.Format = "0.00%";
        worksheet.Columns().AdjustToContents(
            1,
            Math.Max(rowNumber, 2),
            8,
            48);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<AccessResult>
        LoadAccessAsync(
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

        command.Parameters.AddWithValue(
            "user_id",
            userId);

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
            roles.Overlaps(ManagementRoleCodes)
            || profile.Contains("administrator")
            || profile.Contains("superadmin")
            || profile.Contains("project team coordinator")
            || profile.Contains("project coordinator");

        var canReadOnly =
            roles.Overlaps(ReadOnlyRoleCodes)
            || profile.Contains("account executive")
            || profile.Contains("account manager")
            || profile.Contains("sales")
            || profile.Contains("executive");

        return new AccessResult(
            canManage || canReadOnly,
            canManage);
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

    private static IResult Forbidden(
        string message)
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

    private sealed record CreditRequest(
        decimal Amount,
        DateOnly AwardedOn,
        string Reason,
        string? Reference);

    private sealed record NoteRequest(
        string NoteText,
        string? Category);

    private sealed record PrepaidRow(
        Guid BohContractId,
        Guid ClientId,
        string CustomerName,
        string EngagementName,
        string ContractStatus,
        Guid AccountExecutiveUserId,
        string AccountExecutiveName,
        Guid ProjectTeamCoordinatorUserId,
        string ContractManagerName,
        string PoQuote,
        DateOnly ContractStartDate,
        DateOnly ContractEndDate,
        string FixedFeeItem,
        string LatestTimeText,
        DateOnly? BillingDate,
        decimal FixedFeeAmount,
        decimal CreditAwarded,
        DateOnly? LatestCreditAwardedOn,
        string LatestCreditAwardedBy,
        decimal PendingAmount,
        decimal ApprovedAmount,
        decimal TotalHoursAmount,
        decimal TotalExpenses,
        decimal Adjustments,
        decimal TotalUsed,
        decimal TotalAvailable,
        decimal RemainingBalance,
        decimal? BalancePercent,
        string CertiniaId,
        string SellQuote,
        string SalesforceId,
        int NoteCount,
        string LatestNote);
}
