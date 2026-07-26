using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private static async Task<IResult> GetContextAsync(HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (!HasRole(actor, SelfRoles)) return AccessDenied("Project Expense Upload is available to Engineering and Project Management roles and their leads.");

        var projects = await LoadAccessibleProjectsAsync(connection, actor, false);
        var rows = new List<object>();
        foreach (var project in projects)
        {
            var owners = await LoadEligibleOwnersAsync(connection, actor, project);
            rows.Add(new
            {
                projectId = project.ProjectId,
                clientId = project.ClientId,
                customerName = project.CustomerName,
                projectCode = project.ProjectCode,
                projectName = project.ProjectName,
                contractType = project.ContractType,
                status = project.Status,
                projectManagerUserId = project.ProjectManagerUserId,
                projectManagerName = project.ProjectManagerName,
                billingTreatment = BillingTreatment(project.ContractType),
                eligibleOwners = owners.Select(owner => new { userId = owner.UserId, displayName = owner.DisplayName, email = owner.Email, roleCodes = owner.RoleCodes })
            });
        }

        var profile = await LoadCertifyProfileAsync(connection);
        return Results.Ok(new
        {
            status = "project_expense_context_loaded",
            module = "005",
            moduleName = "Project Expense Upload",
            actor = new
            {
                actualUserId = actor.ActualUserId,
                effectiveUserId = actor.EffectiveUserId,
                actor.DisplayName,
                actor.Email,
                actor.RoleCodes,
                actor.IsViewAs,
                canUploadSelf = !actor.IsViewAs,
                canUploadOnBehalf = !actor.IsViewAs && HasRole(actor, OnBehalfRoles),
                canDelete = !actor.IsViewAs
            },
            customers = projects.Select(project => project.CustomerName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value),
            projects = rows,
            importMethods = new[]
            {
                new { code = "excel_csv", label = "Upload CSV / Excel", enabled = true },
                new { code = "certify", label = "Import from Certify", enabled = profile?.ConnectionStatus == "connected" }
            },
            certify = CertifyPayload(profile, HasRole(actor, CertifyAdminRoles)),
            globalMail = GlobalMailState(),
            billingRules = new
            {
                timeAndMaterials = "Customer pass-through expense and invoice eligible.",
                fixedPrice = "Tracked project cost already included in the fixed price; not added as a separate charge."
            }
        });
    }

    private static async Task<IResult> GetUploadsAsync(HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (!HasRole(actor, SelfRoles) && !HasRole(actor, BillingRoles)) return AccessDenied("The current role cannot view project expense uploads.");

        Guid? projectFilter = Guid.TryParse(context.Request.Query["projectId"], out var projectId) ? projectId : null;
        Guid? ownerFilter = Guid.TryParse(context.Request.Query["ownerUserId"], out var ownerId) ? ownerId : null;
        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var allowed = projects.Select(project => project.ProjectId).ToHashSet();
        if (projectFilter is not null && !allowed.Contains(projectFilter.Value)) return AccessDenied("The selected project is outside the current role scope.");

        var rows = new List<object>();
        const string sql = """
            SELECT upload.project_expense_upload_id, upload.project_id, upload.customer_name,
                   upload.project_code, upload.project_name, upload.expense_owner_user_id,
                   COALESCE(owner_user.display_name, owner_user.email, ''), COALESCE(owner_user.email, ''),
                   upload.uploaded_by_user_id, COALESCE(uploader.display_name, uploader.email, ''),
                   upload.source_mode, upload.source_format, upload.source_report_id, upload.original_file_name,
                   upload.period_start, upload.period_end, upload.currency, upload.line_count,
                   upload.total_amount, upload.reimbursable_amount, upload.contract_type_snapshot,
                   upload.billing_treatment, upload.version_number, upload.is_current, upload.uploaded_at,
                   upload.deleted_at, upload.deletion_reason, upload.notification_status, upload.notification_detail,
                   COALESCE((SELECT jsonb_agg(jsonb_build_object(
                       'category', category.expense_category,
                       'amount', category.amount,
                       'reimbursableAmount', category.reimbursable_amount
                   ) ORDER BY category.expense_category)
                   FROM (SELECT expense_category, SUM(amount) amount, SUM(reimbursable_amount) reimbursable_amount
                         FROM project_expense_lines line
                         WHERE line.project_expense_upload_id=upload.project_expense_upload_id
                         GROUP BY expense_category) category), '[]'::jsonb)::text
            FROM project_expense_uploads upload
            JOIN app_users owner_user ON owner_user.user_id=upload.expense_owner_user_id
            JOIN app_users uploader ON uploader.user_id=upload.uploaded_by_user_id
            WHERE (@project_id::uuid IS NULL OR upload.project_id=@project_id)
              AND (@owner_id::uuid IS NULL OR upload.expense_owner_user_id=@owner_id)
            ORDER BY upload.uploaded_at DESC, upload.version_number DESC;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter("project_id", NpgsqlDbType.Uuid) { Value = projectFilter is null ? DBNull.Value : projectFilter.Value });
        command.Parameters.Add(new NpgsqlParameter("owner_id", NpgsqlDbType.Uuid) { Value = ownerFilter is null ? DBNull.Value : ownerFilter.Value });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var rowProjectId = reader.GetGuid(1);
            if (!allowed.Contains(rowProjectId)) continue;
            rows.Add(new
            {
                uploadId = reader.GetGuid(0), projectId = rowProjectId,
                customerName = reader.GetString(2), projectCode = reader.GetString(3), projectName = reader.GetString(4),
                expenseOwnerUserId = reader.GetGuid(5), expenseOwnerName = reader.GetString(6), expenseOwnerEmail = reader.GetString(7),
                uploadedByUserId = reader.GetGuid(8), uploadedByName = reader.GetString(9),
                sourceMode = reader.GetString(10), sourceFormat = reader.GetString(11),
                sourceReportId = reader.IsDBNull(12) ? null : reader.GetString(12),
                originalFileName = reader.IsDBNull(13) ? null : reader.GetString(13),
                periodStart = ReadDate(reader, 14), periodEnd = ReadDate(reader, 15), currency = reader.GetString(16),
                lineCount = reader.GetInt32(17), totalAmount = reader.GetDecimal(18), reimbursableAmount = reader.GetDecimal(19),
                contractType = reader.GetString(20), billingTreatment = reader.GetString(21), versionNumber = reader.GetInt32(22),
                isCurrent = reader.GetBoolean(23), uploadedAt = reader.GetFieldValue<DateTimeOffset>(24),
                deletedAt = reader.IsDBNull(25) ? null : reader.GetFieldValue<DateTimeOffset>(25),
                deletionReason = reader.IsDBNull(26) ? null : reader.GetString(26),
                notificationStatus = reader.GetString(27), notificationDetail = reader.GetString(28),
                categoryTotals = JsonSerializer.Deserialize<JsonElement>(reader.GetString(29))
            });
        }
        return Results.Ok(new { status = "project_expense_uploads_loaded", count = rows.Count, uploads = rows });
    }

    private static async Task<IResult> GetProjectSummaryAsync(Guid projectId, HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var project = projects.FirstOrDefault(item => item.ProjectId == projectId);
        if (project is null) return AccessDenied("The selected project is outside the current role scope.");

        var uploads = new List<object>();
        decimal tracked = 0m, invoiceEligible = 0m;
        const string sql = """
            SELECT upload.project_expense_upload_id, upload.expense_owner_user_id,
                   COALESCE(owner_user.display_name, owner_user.email, ''), upload.source_mode,
                   upload.source_format, upload.period_start, upload.period_end, upload.line_count,
                   upload.total_amount, upload.reimbursable_amount, upload.currency,
                   upload.billing_treatment, upload.version_number, upload.uploaded_at, upload.notification_status
            FROM project_expense_uploads upload
            JOIN app_users owner_user ON owner_user.user_id=upload.expense_owner_user_id
            WHERE upload.project_id=@project_id AND upload.is_current=TRUE AND upload.deleted_at IS NULL
            ORDER BY upload.period_end DESC NULLS LAST, upload.uploaded_at DESC;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var amount = reader.GetDecimal(8);
            var reimbursable = reader.GetDecimal(9);
            var treatment = reader.GetString(11);
            tracked += amount;
            if (treatment == "pass_through_invoice") invoiceEligible += reimbursable;
            uploads.Add(new
            {
                uploadId = reader.GetGuid(0), expenseOwnerUserId = reader.GetGuid(1), expenseOwnerName = reader.GetString(2),
                sourceMode = reader.GetString(3), sourceFormat = reader.GetString(4),
                periodStart = ReadDate(reader, 5), periodEnd = ReadDate(reader, 6), lineCount = reader.GetInt32(7),
                totalAmount = amount, reimbursableAmount = reimbursable, currency = reader.GetString(10),
                billingTreatment = treatment, versionNumber = reader.GetInt32(12),
                uploadedAt = reader.GetFieldValue<DateTimeOffset>(13), notificationStatus = reader.GetString(14)
            });
        }
        return Results.Ok(new
        {
            status = "project_expense_summary_loaded",
            project = new { projectId = project.ProjectId, project.ClientId, project.CustomerName, project.ProjectCode, project.ProjectName, project.ContractType, billingTreatment = BillingTreatment(project.ContractType) },
            currentUploadCount = uploads.Count,
            trackedExpenseTotal = tracked,
            invoiceEligibleExpenseTotal = invoiceEligible,
            fixedPriceIncludedCostTotal = tracked - invoiceEligible,
            uploads
        });
    }

    private static async Task<List<ExpenseProject>> LoadAccessibleProjectsAsync(NpgsqlConnection connection, ExpenseActor actor, bool includeBillingRoles)
    {
        var allProjects = actor.RoleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase)
            || actor.RoleCodes.Contains("PROJECT_MANAGEMENT_LEAD", StringComparer.OrdinalIgnoreCase)
            || (includeBillingRoles && actor.RoleCodes.Any(role => role is "ACCOUNTING" or "PROJECT_TEAM_COORDINATOR"));
        const string sql = """
            SELECT p.project_id, p.client_id, COALESCE(c.client_name, ''), COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''), COALESCE(p.contract_type, ''), COALESCE(p.status, ''),
                   p.project_manager_user_id, COALESCE(pm.display_name, pm.email, '')
            FROM projects p
            LEFT JOIN clients c ON c.client_id=p.client_id
            LEFT JOIN app_users pm ON pm.user_id=p.project_manager_user_id
            WHERE lower(COALESCE(p.status, '')) NOT IN ('cancelled', 'deleted')
              AND (@all_projects OR p.project_manager_user_id=@user_id
                   OR EXISTS (SELECT 1 FROM project_assignments pa
                              WHERE pa.project_id=p.project_id AND pa.user_id=@user_id
                                AND (pa.effective_end_date IS NULL OR pa.effective_end_date>=CURRENT_DATE)))
            ORDER BY c.client_name, p.project_name;
            """;
        var rows = new List<ExpenseProject>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("all_projects", allProjects);
        command.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new ExpenseProject(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetString(8)));
        return rows;
    }

    private static async Task<List<ExpenseOwner>> LoadEligibleOwnersAsync(NpgsqlConnection connection, ExpenseActor actor, ExpenseProject project)
    {
        var allowOnBehalf = HasRole(actor, OnBehalfRoles);
        const string sql = """
            SELECT u.user_id, COALESCE(u.display_name, u.email, ''), COALESCE(u.email, ''),
                   COALESCE(array_agg(DISTINCT upper(r.role_code)) FILTER (WHERE r.role_code IS NOT NULL), ARRAY[]::text[])
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
            LEFT JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
            WHERE u.is_active=TRUE AND (
                u.user_id=@current_user
                OR (@allow_on_behalf AND EXISTS (SELECT 1 FROM project_assignments pa
                    WHERE pa.project_id=@project_id AND pa.user_id=u.user_id
                      AND (pa.effective_end_date IS NULL OR pa.effective_end_date>=CURRENT_DATE)))
            )
            GROUP BY u.user_id
            ORDER BY COALESCE(u.display_name, u.email);
            """;
        var owners = new List<ExpenseOwner>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("current_user", actor.EffectiveUserId);
        command.Parameters.AddWithValue("allow_on_behalf", allowOnBehalf);
        command.Parameters.AddWithValue("project_id", project.ProjectId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roles = reader.GetFieldValue<string[]>(3).Select(ScopedRolePolicyModule.CanonicalRole).Distinct().ToArray();
            if (reader.GetGuid(0) != actor.EffectiveUserId && !roles.Any(role => role is "ENGINEERING" or "ENGINEERING_LEAD")) continue;
            owners.Add(new ExpenseOwner(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), roles));
        }
        return owners;
    }

    private static async Task<ExpenseProject?> LoadProjectAsync(NpgsqlConnection connection, Guid projectId, NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT p.project_id, p.client_id, COALESCE(c.client_name, ''), COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''), COALESCE(p.contract_type, ''), COALESCE(p.status, ''),
                   p.project_manager_user_id, COALESCE(pm.display_name, pm.email, '')
            FROM projects p LEFT JOIN clients c ON c.client_id=p.client_id
            LEFT JOIN app_users pm ON pm.user_id=p.project_manager_user_id
            WHERE p.project_id=@project_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ExpenseProject(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetString(8));
    }

    private static async Task<IResult?> AuthorizeUploadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, ExpenseActor actor, ExpenseProject project, Guid ownerId)
    {
        if (actor.IsViewAs) return ViewAsReadOnly();
        var accessible = await LoadAccessibleProjectsAsync(connection, actor, false);
        if (!accessible.Any(item => item.ProjectId == project.ProjectId)) return AccessDenied("The selected project is outside the current role scope.");
        if (ownerId == actor.EffectiveUserId) return null;
        if (!HasRole(actor, OnBehalfRoles)) return AccessDenied("Only Project Management, PM Leads, and Super Administrators may upload on behalf of another user.");
        var owners = await LoadEligibleOwnersAsync(connection, actor, project);
        return owners.Any(owner => owner.UserId == ownerId)
            ? null
            : AccessDenied("The selected expense owner is not an Engineer or Engineering Lead assigned to this project.");
    }
}
