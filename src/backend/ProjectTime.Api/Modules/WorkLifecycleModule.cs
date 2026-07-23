using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class WorkLifecycleModule
{
    private static readonly string[] LifecycleApprovedTimeStatuses =
    [
        "pm_approved",
        "manager_approved",
        "project_approved",
        "project_validated",
        "accounting_ready",
        "reconciled",
        "locked"
    ];

    private static readonly HashSet<string> ReadinessKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "timeApproved",
        "certifyReviewed",
        "customerMapped",
        "exceptionsCleared",
        "billingTreatment",
        "evidenceReady",
        "customerNotesReady",
        "accountingReady"
    };

    private static readonly HashSet<string> BillingRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACCOUNTING",
        "ACCOUNTING_BILLING",
        "BILLING",
        "FINANCE"
    };

    private static readonly HashSet<string> BroadReadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXECUTIVE",
        "SALES",
        "INSIDE_SALES",
        "ACCOUNT_EXECUTIVE",
        "SALES_MANAGER"
    };

    private static readonly HashSet<string> TimeEntryExcludedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANAGER",
        "PEOPLE_MANAGER",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD",
        "SALES",
        "INSIDE_SALES",
        "ACCOUNT_EXECUTIVE",
        "SALES_MANAGER",
        "EXECUTIVE",
        "PROJECT_TEAM_COORDINATOR"
    };

    private static readonly HashSet<string> TimeEntryRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENGINEER",
        "ENGINEERING",
        "SOLUTION_ARCHITECT",
        "ARCHITECT",
        "SA",
        "SAA"
    };

    public static void MapWorkLifecycleEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/work-lifecycle/dashboard",
            (Func<HttpContext, Task<IResult>>)GetDashboardAsync);
        app.MapGet("/api/work-lifecycle/projects/{projectId:guid}", GetProjectLifecycleAsync);
        app.MapPost(
            "/api/work-lifecycle/projects/{projectId:guid}/billing-readiness",
            SaveBillingReadinessAsync);
        app.MapPost(
            "/api/work-lifecycle/projects/{projectId:guid}/closeout/request",
            RequestCloseoutAsync);
        app.MapPost(
            "/api/work-lifecycle/projects/{projectId:guid}/closeout/complete",
            CompleteCloseoutAsync);
        app.MapPost(
            "/api/work-lifecycle/projects/{projectId:guid}/closeout/reopen",
            ReopenProjectAsync);
    }

    private static async Task<IResult> GetDashboardAsync(HttpContext context)
    {
        await using var connection = await OpenAsync(context.RequestAborted);
        var access = await WorkRegisterAuthorization.GetAccessAsync(
            connection,
            context,
            cancellationToken: context.RequestAborted);

        if (access.ActualUserId == Guid.Empty)
        {
            return Results.Json(
                new { status = "session_required", message = "A valid ProjectPulse session is required." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var roleCodes = access.RoleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var showTimeEntry = roleCodes.Any(TimeEntryRoles.Contains)
            && !roleCodes.Any(TimeEntryExcludedRoles.Contains);
        var broadScope = access.CanEditAll || roleCodes.Any(BillingRoles.Contains) || roleCodes.Any(BroadReadRoles.Contains);

        var week = await LoadWeekSummaryAsync(connection, access.ActualUserId, showTimeEntry, context.RequestAborted);
        var attention = await LoadAttentionSummaryAsync(connection, access, broadScope, context.RequestAborted);
        var projectHealth = await LoadProjectHealthAsync(connection, access, broadScope, context.RequestAborted);
        var projects = await LoadDashboardProjectsAsync(connection, access, broadScope, context.RequestAborted);
        var billing = await LoadBillingSnapshotAsync(connection, access, broadScope, context.RequestAborted);
        var recent = await LoadRecentItemsAsync(connection, access, broadScope, context.RequestAborted);

        return Results.Ok(new
        {
            status = "work_lifecycle_dashboard_loaded",
            roleCodes = access.RoleCodes,
            showTimeEntry,
            scope = broadScope ? "portfolio" : "assigned",
            week,
            attention,
            projectHealth,
            projects,
            billing,
            recent
        });
    }

    private static async Task<IResult> GetProjectLifecycleAsync(Guid projectId, HttpContext context)
    {
        await using var connection = await OpenAsync(context.RequestAborted);
        var access = await WorkRegisterAuthorization.GetAccessAsync(
            connection,
            context,
            cancellationToken: context.RequestAborted);
        var project = await LoadProjectAsync(connection, null, projectId, context.RequestAborted);

        if (project is null)
        {
            return Results.NotFound(new { status = "project_not_found", message = "Project was not found." });
        }

        var capabilities = BuildCapabilities(access, project);
        if (!capabilities.CanView)
        {
            return Results.Json(
                new { status = "access_denied", message = "This project is outside the current user's Work-to-Cash scope." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var readiness = await LoadLatestReadinessAsync(connection, null, projectId, context.RequestAborted);
        var billingReadinessBlockers = await BuildBillingReadinessBlockersAsync(
            connection,
            null,
            project,
            context.RequestAborted);
        var closeout = await LoadCloseoutAsync(connection, null, projectId, context.RequestAborted);
        var blockers = await BuildCloseoutBlockersAsync(
            connection,
            null,
            project,
            readiness,
            closeout,
            context.RequestAborted);
        var invoiceSummary = await LoadInvoiceSummaryAsync(connection, null, projectId, context.RequestAborted);
        var audit = await LoadAuditAsync(connection, projectId, context.RequestAborted);

        return Results.Ok(new
        {
            status = "work_lifecycle_loaded",
            project,
            capabilities,
            billingReadiness = readiness,
            billingReadinessBlockers,
            closeout,
            closeoutBlockers = blockers,
            invoiceSummary,
            audit
        });
    }

    private static async Task<IResult> SaveBillingReadinessAsync(
        Guid projectId,
        BillingReadinessSaveRequest request,
        HttpContext context)
    {
        if (request.BillingPeriodStart == default
            || request.BillingPeriodEnd == default
            || request.BillingPeriodEnd < request.BillingPeriodStart)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Billing period start and end are required, and the end date cannot precede the start date."
            });
        }

        var packageType = Clean(request.PackageType);
        if (string.IsNullOrWhiteSpace(packageType) || packageType.Length > 120)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Select a valid billing package type."
            });
        }

        var requestedStatus = Clean(request.ReviewStatus).ToLowerInvariant();
        if (requestedStatus is not ("draft" or "blocked" or "ready"))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Billing readiness status must be draft, blocked, or ready."
            });
        }

        var checklist = request.Checklist ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var normalizedChecklist = ReadinessKeys.ToDictionary(
            key => key,
            key => checklist.TryGetValue(key, out var value) && value,
            StringComparer.OrdinalIgnoreCase);
        var allRequiredConfirmed = normalizedChecklist.Values.All(value => value);

        if (requestedStatus == "ready" && !allRequiredConfirmed)
        {
            return Results.BadRequest(new
            {
                status = "billing_readiness_incomplete",
                message = "Every required readiness check must be confirmed before the package can be marked ready.",
                missingChecks = normalizedChecklist.Where(item => !item.Value).Select(item => item.Key).ToArray()
            });
        }

        await using var connection = await OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        var access = await WorkRegisterAuthorization.GetAccessAsync(
            connection,
            context,
            transaction,
            context.RequestAborted);
        var project = await LoadProjectAsync(connection, transaction, projectId, context.RequestAborted, lockRow: true);

        if (project is null)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.NotFound(new { status = "project_not_found", message = "Project was not found." });
        }

        var capabilities = BuildCapabilities(access, project);
        if (!capabilities.CanManageBillingReadiness || project.IsArchived)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Json(new
            {
                status = "access_denied",
                message = project.IsArchived
                    ? "Archived projects are read-only."
                    : "Billing readiness may be updated only by the assigned PM, PTC, Accounting/Billing, Administrator, or Super Administrator. View-As is read-only."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var serverBlockers = await BuildBillingReadinessBlockersAsync(
            connection,
            transaction,
            project,
            context.RequestAborted);
        if (requestedStatus == "ready" && serverBlockers.Count > 0)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Conflict(new
            {
                status = "billing_readiness_blocked",
                message = "The billing package cannot be marked ready until every server-validated blocker is resolved.",
                blockers = serverBlockers
            });
        }

        var prior = await LoadLatestReadinessAsync(connection, transaction, projectId, context.RequestAborted);
        var reviewId = Guid.NewGuid();
        var checklistJson = JsonSerializer.Serialize(normalizedChecklist);

        await using (var command = new NpgsqlCommand("""
            INSERT INTO work_billing_readiness_reviews (
                work_billing_readiness_review_id,
                project_id,
                billing_period_start,
                billing_period_end,
                package_type,
                review_status,
                checklist_json,
                notes,
                reviewed_by_user_id,
                created_at,
                updated_at
            )
            VALUES (
                @review_id,
                @project_id,
                @period_start,
                @period_end,
                @package_type,
                @review_status,
                @checklist,
                @notes,
                @actor_user_id,
                NOW(),
                NOW()
            )
            ON CONFLICT (project_id, billing_period_start, billing_period_end, package_type)
            DO UPDATE SET
                review_status = EXCLUDED.review_status,
                checklist_json = EXCLUDED.checklist_json,
                notes = EXCLUDED.notes,
                reviewed_by_user_id = EXCLUDED.reviewed_by_user_id,
                updated_at = NOW()
            RETURNING work_billing_readiness_review_id;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("review_id", reviewId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.Add("period_start", NpgsqlDbType.Date).Value = request.BillingPeriodStart;
            command.Parameters.Add("period_end", NpgsqlDbType.Date).Value = request.BillingPeriodEnd;
            command.Parameters.AddWithValue("package_type", packageType);
            command.Parameters.AddWithValue("review_status", requestedStatus);
            command.Parameters.Add("checklist", NpgsqlDbType.Jsonb).Value = checklistJson;
            command.Parameters.AddWithValue("notes", Clean(request.Notes));
            command.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            reviewId = (Guid)(await command.ExecuteScalarAsync(context.RequestAborted) ?? reviewId);
        }

        await InsertAuditAsync(
            connection,
            transaction,
            projectId,
            "billing_readiness",
            "billing_readiness_saved",
            prior?.ReviewStatus ?? string.Empty,
            requestedStatus,
            $"Billing readiness package saved as {requestedStatus}.",
            Clean(request.Reason),
            access.ActualUserId,
            "billing_readiness_review",
            reviewId,
            new
            {
                request.BillingPeriodStart,
                request.BillingPeriodEnd,
                packageType,
                checklist = normalizedChecklist,
                notes = Clean(request.Notes)
            },
            context.RequestAborted);

        await transaction.CommitAsync(context.RequestAborted);
        var saved = await LoadLatestReadinessAsync(connection, null, projectId, context.RequestAborted);

        return Results.Ok(new
        {
            status = "billing_readiness_saved",
            message = requestedStatus == "ready"
                ? "Billing package is ready for invoice review."
                : "Billing readiness progress was saved.",
            billingReadiness = saved,
            blockers = serverBlockers
        });
    }

    private static Task<IResult> RequestCloseoutAsync(
        Guid projectId,
        CloseoutSaveRequest request,
        HttpContext context) =>
        SaveCloseoutAsync(projectId, request, context, "request");

    private static Task<IResult> CompleteCloseoutAsync(
        Guid projectId,
        CloseoutSaveRequest request,
        HttpContext context) =>
        SaveCloseoutAsync(projectId, request, context, "complete");

    private static async Task<IResult> ReopenProjectAsync(
        Guid projectId,
        CloseoutReopenRequest request,
        HttpContext context)
    {
        var reason = Clean(request.Reason);
        if (reason.Length < 5)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "A specific reopen reason is required for audit history."
            });
        }

        await using var connection = await OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        var access = await WorkRegisterAuthorization.GetAccessAsync(
            connection,
            context,
            transaction,
            context.RequestAborted);
        var project = await LoadProjectAsync(connection, transaction, projectId, context.RequestAborted, lockRow: true);

        if (project is null)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.NotFound(new { status = "project_not_found", message = "Project was not found." });
        }

        var capabilities = BuildCapabilities(access, project);
        if (!capabilities.CanReopenProject || project.IsArchived)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Json(new
            {
                status = "access_denied",
                message = project.IsArchived
                    ? "Archived projects are read-only."
                    : "Only a PTC, Administrator, or Super Administrator can reopen a closed project. View-As is read-only."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var closeout = await LoadCloseoutAsync(connection, transaction, projectId, context.RequestAborted);
        if (closeout is null || closeout.CloseoutStatus != "closed")
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Conflict(new
            {
                status = "project_not_closed",
                message = "Only a project closed through the governed closeout workflow can be reopened."
            });
        }

        var restoredStatus = string.IsNullOrWhiteSpace(closeout.PriorProjectStatus)
            || string.Equals(closeout.PriorProjectStatus, "closed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(closeout.PriorProjectStatus, "completed", StringComparison.OrdinalIgnoreCase)
                ? "active"
                : closeout.PriorProjectStatus;

        await using (var command = new NpgsqlCommand("""
            UPDATE projects
            SET status = @status,
                updated_at = NOW()
            WHERE project_id = @project_id;

            UPDATE work_closeout_records
            SET closeout_status = 'reopened',
                reason = @reason,
                reopened_by_user_id = @actor_user_id,
                reopened_at = NOW(),
                updated_at = NOW()
            WHERE project_id = @project_id;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("status", restoredStatus);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }

        await InsertAuditAsync(
            connection,
            transaction,
            projectId,
            "closeout",
            "project_reopened",
            "closed",
            "reopened",
            $"Project reopened with status {restoredStatus}.",
            reason,
            access.ActualUserId,
            "closeout_record",
            projectId,
            new { restoredStatus },
            context.RequestAborted);

        await transaction.CommitAsync(context.RequestAborted);
        return Results.Ok(new
        {
            status = "project_reopened",
            message = "Project reopened and the reason was added to the unified audit history.",
            projectId,
            projectStatus = restoredStatus
        });
    }

    private static async Task<IResult> SaveCloseoutAsync(
        Guid projectId,
        CloseoutSaveRequest request,
        HttpContext context,
        string operation)
    {
        var reason = Clean(request.Reason);
        if (reason.Length < 5)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "A specific closeout reason is required for audit history."
            });
        }

        var disposition = Clean(request.BillingDisposition).ToLowerInvariant();
        var allowedDispositions = new[]
        {
            "final_invoice_complete",
            "no_further_billing",
            "non_billable",
            "write_off_approved"
        };
        if (!allowedDispositions.Contains(disposition))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Select the final billing disposition before requesting or completing closeout."
            });
        }

        await using var connection = await OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        var access = await WorkRegisterAuthorization.GetAccessAsync(
            connection,
            context,
            transaction,
            context.RequestAborted);
        var project = await LoadProjectAsync(connection, transaction, projectId, context.RequestAborted, lockRow: true);

        if (project is null)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.NotFound(new { status = "project_not_found", message = "Project was not found." });
        }

        var capabilities = BuildCapabilities(access, project);
        var allowed = operation == "complete"
            ? capabilities.CanCompleteCloseout
            : capabilities.CanRequestCloseout;
        if (!allowed || project.IsArchived)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Json(new
            {
                status = "access_denied",
                message = project.IsArchived
                    ? "Archived projects are read-only."
                    : operation == "complete"
                        ? "Only a PTC, Administrator, or Super Administrator can complete closeout. View-As is read-only."
                        : "Only the assigned PM, PTC, Administrator, or Super Administrator can request closeout. View-As is read-only."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (disposition == "write_off_approved" && !access.CanEditAll)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Json(new
            {
                status = "access_denied",
                message = "A write-off disposition requires PTC or administrator authority."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var prior = await LoadCloseoutAsync(connection, transaction, projectId, context.RequestAborted);
        var proposed = new WorkCloseoutSnapshot(
            operation == "complete" ? "ready" : "requested",
            disposition,
            request.DeliveryComplete,
            request.CustomerAcceptanceComplete,
            request.TimeExpenseComplete,
            request.BillingComplete,
            reason,
            Clean(request.Notes),
            project.Status);
        var readiness = await LoadLatestReadinessAsync(connection, transaction, projectId, context.RequestAborted);
        var blockers = await BuildCloseoutBlockersAsync(
            connection,
            transaction,
            project,
            readiness,
            proposed,
            context.RequestAborted);

        if (operation == "complete" && blockers.Count > 0)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Conflict(new
            {
                status = "closeout_blocked",
                message = "Closeout cannot be completed until every server-validated blocker is resolved.",
                blockers
            });
        }

        var closeoutStatus = operation == "complete" ? "closed" : blockers.Count == 0 ? "ready" : "requested";

        await using (var command = new NpgsqlCommand("""
            INSERT INTO work_closeout_records (
                project_id,
                closeout_status,
                billing_disposition,
                delivery_complete,
                customer_acceptance_complete,
                time_expense_complete,
                billing_complete,
                reason,
                notes,
                prior_project_status,
                requested_by_user_id,
                requested_at,
                closed_by_user_id,
                closed_at,
                created_at,
                updated_at
            )
            VALUES (
                @project_id,
                @closeout_status,
                @billing_disposition,
                @delivery_complete,
                @customer_acceptance_complete,
                @time_expense_complete,
                @billing_complete,
                @reason,
                @notes,
                @prior_project_status,
                @actor_user_id,
                NOW(),
                CASE WHEN @closeout_status = 'closed' THEN @actor_user_id ELSE NULL END,
                CASE WHEN @closeout_status = 'closed' THEN NOW() ELSE NULL END,
                NOW(),
                NOW()
            )
            ON CONFLICT (project_id)
            DO UPDATE SET
                closeout_status = EXCLUDED.closeout_status,
                billing_disposition = EXCLUDED.billing_disposition,
                delivery_complete = EXCLUDED.delivery_complete,
                customer_acceptance_complete = EXCLUDED.customer_acceptance_complete,
                time_expense_complete = EXCLUDED.time_expense_complete,
                billing_complete = EXCLUDED.billing_complete,
                reason = EXCLUDED.reason,
                notes = EXCLUDED.notes,
                prior_project_status = CASE
                    WHEN work_closeout_records.prior_project_status = ''
                        THEN EXCLUDED.prior_project_status
                    ELSE work_closeout_records.prior_project_status
                END,
                requested_by_user_id = EXCLUDED.requested_by_user_id,
                requested_at = EXCLUDED.requested_at,
                closed_by_user_id = EXCLUDED.closed_by_user_id,
                closed_at = EXCLUDED.closed_at,
                reopened_by_user_id = NULL,
                reopened_at = NULL,
                updated_at = NOW();
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("closeout_status", closeoutStatus);
            command.Parameters.AddWithValue("billing_disposition", disposition);
            command.Parameters.AddWithValue("delivery_complete", request.DeliveryComplete);
            command.Parameters.AddWithValue("customer_acceptance_complete", request.CustomerAcceptanceComplete);
            command.Parameters.AddWithValue("time_expense_complete", request.TimeExpenseComplete);
            command.Parameters.AddWithValue("billing_complete", request.BillingComplete);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("notes", Clean(request.Notes));
            command.Parameters.AddWithValue("prior_project_status", prior?.PriorProjectStatus ?? project.Status);
            command.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }

        if (operation == "complete")
        {
            await using var projectCommand = new NpgsqlCommand("""
                UPDATE projects
                SET status = 'completed',
                    updated_at = NOW()
                WHERE project_id = @project_id;
                """, connection, transaction);
            projectCommand.Parameters.AddWithValue("project_id", projectId);
            await projectCommand.ExecuteNonQueryAsync(context.RequestAborted);
        }

        await InsertAuditAsync(
            connection,
            transaction,
            projectId,
            "closeout",
            operation == "complete" ? "project_closed" : "closeout_requested",
            prior?.CloseoutStatus ?? "not_started",
            closeoutStatus,
            operation == "complete"
                ? "Project closeout completed after all server validations passed."
                : blockers.Count == 0
                    ? "Project is ready for PTC or administrator closeout."
                    : $"Closeout requested with {blockers.Count} remaining blocker(s).",
            reason,
            access.ActualUserId,
            "closeout_record",
            projectId,
            new
            {
                disposition,
                request.DeliveryComplete,
                request.CustomerAcceptanceComplete,
                request.TimeExpenseComplete,
                request.BillingComplete,
                blockers
            },
            context.RequestAborted);

        await transaction.CommitAsync(context.RequestAborted);
        return Results.Ok(new
        {
            status = operation == "complete" ? "project_closed" : "closeout_saved",
            message = operation == "complete"
                ? "Project closed and the complete decision trail was recorded."
                : closeoutStatus == "ready"
                    ? "Closeout request saved and ready for final approval."
                    : "Closeout request saved with remaining blockers.",
            closeoutStatus,
            blockers
        });
    }

    private static WorkLifecycleCapabilities BuildCapabilities(
        WorkRegisterAccess access,
        WorkLifecycleProject project)
    {
        var assigned = project.ProjectManagerUserId == access.ActualUserId;
        var billingRole = access.RoleCodes.Any(BillingRoles.Contains);
        var broadRead = access.RoleCodes.Any(BroadReadRoles.Contains);
        var canView = access.CanEditAll || assigned || billingRole || broadRead;

        return new WorkLifecycleCapabilities(
            CanView: canView,
            CanManageBillingReadiness: !access.IsViewAs && (access.CanEditAll || assigned || billingRole),
            CanRequestCloseout: !access.IsViewAs && (access.CanEditAll || assigned),
            CanCompleteCloseout: !access.IsViewAs && access.CanEditAll,
            CanReopenProject: !access.IsViewAs && access.CanEditAll,
            IsAssignedProjectManager: assigned,
            IsViewAs: access.IsViewAs);
    }

    private static async Task<WorkLifecycleProject?> LoadProjectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken,
        bool lockRow = false)
    {
        var sql = """
            SELECT
                project.project_id,
                COALESCE(project.project_code, ''),
                COALESCE(project.project_name, ''),
                COALESCE(project.status, ''),
                COALESCE(project.contract_type, ''),
                project.project_manager_user_id,
                COALESCE(manager.display_name, manager.email, ''),
                COALESCE(client.client_name, ''),
                COALESCE(lifecycle.is_archived, FALSE)
            FROM projects project
            LEFT JOIN app_users manager
              ON manager.user_id = project.project_manager_user_id
            LEFT JOIN clients client
              ON client.client_id = project.client_id
            LEFT JOIN work_register_project_lifecycle lifecycle
              ON lifecycle.project_id = project.project_id
            WHERE project.project_id = @project_id
            """;
        if (lockRow) sql += "\nFOR UPDATE OF project;";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new WorkLifecycleProject(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetBoolean(8));
    }

    private static async Task<WorkBillingReadinessSnapshot?> LoadLatestReadinessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                review.work_billing_readiness_review_id,
                review.billing_period_start,
                review.billing_period_end,
                review.package_type,
                review.review_status,
                review.checklist_json::text,
                review.notes,
                COALESCE(actor.display_name, actor.email, ''),
                review.updated_at
            FROM work_billing_readiness_reviews review
            LEFT JOIN app_users actor ON actor.user_id = review.reviewed_by_user_id
            WHERE review.project_id = @project_id
            ORDER BY review.updated_at DESC
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new WorkBillingReadinessSnapshot(
            reader.GetGuid(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.GetString(3),
            reader.GetString(4),
            JsonSerializer.Deserialize<Dictionary<string, bool>>(reader.GetString(5))
                ?? new Dictionary<string, bool>(),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    private static async Task<WorkCloseoutSnapshot?> LoadCloseoutAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                closeout.closeout_status,
                closeout.billing_disposition,
                closeout.delivery_complete,
                closeout.customer_acceptance_complete,
                closeout.time_expense_complete,
                closeout.billing_complete,
                closeout.reason,
                closeout.notes,
                closeout.prior_project_status
            FROM work_closeout_records closeout
            WHERE closeout.project_id = @project_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new WorkCloseoutSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8));
    }

    private static async Task<List<string>> BuildCloseoutBlockersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        WorkLifecycleProject project,
        WorkBillingReadinessSnapshot? readiness,
        WorkCloseoutSnapshot? closeout,
        CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        var requiresInvoiceReadiness = string.Equals(
            closeout?.BillingDisposition,
            "final_invoice_complete",
            StringComparison.OrdinalIgnoreCase);

        if (requiresInvoiceReadiness)
        {
            await using var command = new NpgsqlCommand("""
                WITH scoped_entries AS (
                    SELECT
                        entry.billable,
                        entry.hours,
                        entry.status,
                        EXISTS (
                            SELECT 1
                            FROM billing_invoice_lines line
                            JOIN billing_invoices invoice
                              ON invoice.billing_invoice_id = line.billing_invoice_id
                            WHERE line.time_entry_id = entry.time_entry_id
                              AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                        ) AS has_live_invoice
                    FROM time_entries entry
                    WHERE entry.project_id = @project_id
                )
                SELECT
                    COUNT(*) FILTER (
                        WHERE entry.billable = TRUE
                          AND entry.hours > 0
                          AND entry.status = ANY(@approved_statuses)
                          AND entry.has_live_invoice = FALSE
                    ),
                    COUNT(*) FILTER (
                        WHERE entry.billable = TRUE
                          AND entry.hours > 0
                          AND NOT (entry.status = ANY(@approved_statuses))
                          AND entry.has_live_invoice = FALSE
                    )
                FROM scoped_entries entry;
                """, connection, transaction);
            command.Parameters.AddWithValue("project_id", project.ProjectId);
            command.Parameters.AddWithValue("approved_statuses", LifecycleApprovedTimeStatuses);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var eligible = reader.GetInt64(0);
                var pending = reader.GetInt64(1);
                if (eligible > 0) blockers.Add($"{eligible} approved billable time entr{(eligible == 1 ? "y is" : "ies are")} not invoiced.");
                if (pending > 0) blockers.Add($"{pending} billable time entr{(pending == 1 ? "y still requires" : "ies still require")} approval or disposition.");
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM project_tasks task
            WHERE task.project_id = @project_id
              AND lower(COALESCE(task.status, '')) NOT IN (
                  'complete', 'completed', 'closed', 'cancelled', 'canceled', 'done'
              );
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", project.ProjectId);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (count > 0) blockers.Add($"{count} project task{(count == 1 ? " remains" : "s remain")} open.");
        }

        if (requiresInvoiceReadiness && readiness?.ReviewStatus != "ready")
        {
            blockers.Add("The latest billing readiness package is not marked ready.");
        }

        if (closeout is null)
        {
            blockers.Add("Closeout confirmations and billing disposition are not saved.");
            return blockers;
        }

        if (!closeout.DeliveryComplete) blockers.Add("Delivery completion is not confirmed.");
        if (!closeout.CustomerAcceptanceComplete) blockers.Add("Customer acceptance is not confirmed.");
        if (!closeout.TimeExpenseComplete) blockers.Add("Final time and expense review is not confirmed.");
        if (!closeout.BillingComplete) blockers.Add("Billing completion is not confirmed.");

        switch (closeout.BillingDisposition)
        {
            case "final_invoice_complete":
                await using (var command = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1
                        FROM billing_invoices invoice
                        WHERE invoice.project_id = @project_id
                          AND invoice.invoice_type = 'final'
                          AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                    );
                    """, connection, transaction))
                {
                    command.Parameters.AddWithValue("project_id", project.ProjectId);
                    var hasFinalInvoice = Convert.ToBoolean(
                        await command.ExecuteScalarAsync(cancellationToken) ?? false);
                    if (!hasFinalInvoice) blockers.Add("Final invoice disposition was selected, but no final invoice exists.");
                }
                break;
            case "no_further_billing":
            case "non_billable":
            case "write_off_approved":
                break;
            default:
                blockers.Add("A final billing disposition is required.");
                break;
        }

        return blockers;
    }

    private static async Task<List<string>> BuildBillingReadinessBlockersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        WorkLifecycleProject project,
        CancellationToken cancellationToken)
    {
        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(project.CustomerName))
        {
            blockers.Add("Customer is not linked.");
        }
        if (string.IsNullOrWhiteSpace(project.ProjectCode))
        {
            blockers.Add("Project code is missing.");
        }
        if (string.IsNullOrWhiteSpace(project.ProjectName))
        {
            blockers.Add("Project name is missing.");
        }
        if (string.IsNullOrWhiteSpace(project.ContractType))
        {
            blockers.Add("Contract type is missing.");
        }
        if (project.ContractType.Contains("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("Fixed Price invoice dollars require a governed milestone or approved no-further-billing disposition; hourly time remains utilization evidence only.");
        }

        await using (var command = new NpgsqlCommand("""
            SELECT
                COALESCE(profile.purchase_order_required, FALSE),
                EXISTS (
                    SELECT 1
                    FROM project_purchase_orders purchase_order
                    WHERE purchase_order.project_id = @project_id
                      AND purchase_order.is_primary = TRUE
                      AND purchase_order.po_status = 'active'
                      AND (
                          purchase_order.effective_start_date IS NULL
                          OR purchase_order.effective_start_date <= CURRENT_DATE
                      )
                      AND (
                          purchase_order.effective_end_date IS NULL
                          OR purchase_order.effective_end_date >= CURRENT_DATE
                      )
                )
            FROM projects project
            LEFT JOIN project_billing_profiles profile
              ON profile.project_id = project.project_id
            WHERE project.project_id = @project_id;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", project.ProjectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)
                && reader.GetBoolean(0)
                && !reader.GetBoolean(1))
            {
                blockers.Add("A primary active purchase order is required.");
            }
        }

        await using (var command = new NpgsqlCommand("""
            WITH eligible AS (
                SELECT
                    entry.time_entry_id,
                    entry.work_date,
                    entry.time_type,
                    project.client_id
                FROM time_entries entry
                JOIN projects project ON project.project_id = entry.project_id
                WHERE entry.project_id = @project_id
                  AND entry.billable = TRUE
                  AND entry.hours > 0
                  AND entry.status = ANY(@approved_statuses)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM billing_invoice_lines invoiced
                      JOIN billing_invoices invoice
                        ON invoice.billing_invoice_id = invoiced.billing_invoice_id
                      WHERE invoiced.time_entry_id = entry.time_entry_id
                        AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                  )
            )
            SELECT
                COUNT(*),
                COUNT(*) FILTER (
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM work_rate_cards card
                        JOIN work_rate_card_lines line
                          ON line.rate_card_id = card.rate_card_id
                         AND line.is_active = TRUE
                         AND line.billable_default = TRUE
                         AND COALESCE(line.rate_amount, 0) > 0
                         AND lower(line.unit_type) = 'hour'
                         AND lower(line.time_type) = lower(COALESCE(eligible.time_type, 'normal'))
                        LEFT JOIN project_billing_profiles profile
                          ON profile.project_id = @project_id
                        WHERE lower(card.status) IN ('active', 'published', 'approved')
                          AND card.effective_start_date <= eligible.work_date
                          AND (card.effective_end_date IS NULL OR card.effective_end_date >= eligible.work_date)
                          AND (
                              (
                                  profile.default_rate_card_id IS NOT NULL
                                  AND card.rate_card_id = profile.default_rate_card_id
                              )
                              OR (
                                  profile.default_rate_card_id IS NULL
                                  AND (card.client_id = eligible.client_id OR card.client_id IS NULL)
                              )
                          )
                    )
                )
            FROM eligible;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", project.ProjectId);
            command.Parameters.Add("approved_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
                LifecycleApprovedTimeStatuses;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var eligibleCount = reader.GetInt64(0);
                var missingRateCount = reader.GetInt64(1);
                if (eligibleCount == 0)
                {
                    blockers.Add("No approved uninvoiced billable time is available.");
                }
                if (missingRateCount > 0)
                {
                    blockers.Add($"{missingRateCount} approved time entr{(missingRateCount == 1 ? "y has" : "ies have")} no effective stored hourly rate.");
                }
            }
        }

        return blockers;
    }

    private static async Task<object> LoadInvoiceSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*),
                COUNT(*) FILTER (WHERE invoice.invoice_type = 'partial'),
                COUNT(*) FILTER (
                    WHERE invoice.invoice_type = 'final'
                      AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                ),
                COALESCE(SUM(invoice.total_amount) FILTER (
                    WHERE lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                ), 0),
                MAX(invoice.created_at)
            FROM billing_invoices invoice
            WHERE invoice.project_id = @project_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new
        {
            invoiceCount = reader.GetInt64(0),
            partialInvoiceCount = reader.GetInt64(1),
            finalInvoiceCount = reader.GetInt64(2),
            invoicedAmount = reader.GetDecimal(3),
            lastInvoiceAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4)
        };
    }

    private static async Task<List<object>> LoadAuditAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var audit = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                event.work_lifecycle_audit_event_id,
                event.process_area,
                event.event_type,
                event.prior_state,
                event.new_state,
                event.summary,
                event.reason,
                COALESCE(actor.display_name, actor.email, 'System'),
                event.related_entity_type,
                event.related_entity_id,
                event.event_json::text,
                event.created_at
            FROM work_lifecycle_audit_events event
            LEFT JOIN app_users actor ON actor.user_id = event.actor_user_id
            WHERE event.project_id = @project_id
            ORDER BY event.created_at DESC, event.work_lifecycle_audit_event_id DESC
            LIMIT 500;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var details = JsonSerializer.Deserialize<JsonElement>(reader.GetString(10));
            var changedFields = details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("changedFields", out var changedFieldsElement)
                    ? changedFieldsElement.ValueKind == JsonValueKind.String
                        ? changedFieldsElement.GetString() ?? string.Empty
                        : changedFieldsElement.ToString()
                    : string.Empty;
            audit.Add(new
            {
                eventId = reader.GetGuid(0),
                processArea = reader.GetString(1),
                action = reader.GetString(2),
                priorState = reader.GetString(3),
                newState = reader.GetString(4),
                changeSummary = reader.GetString(5),
                reason = reader.GetString(6),
                changedBy = reader.GetString(7),
                relatedEntityType = reader.GetString(8),
                relatedEntityId = reader.IsDBNull(9) ? (Guid?)null : reader.GetGuid(9),
                details,
                changedFields,
                changedAt = reader.GetFieldValue<DateTimeOffset>(11)
            });
        }
        return audit;
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        string processArea,
        string eventType,
        string priorState,
        string newState,
        string summary,
        string reason,
        Guid actorUserId,
        string relatedEntityType,
        Guid relatedEntityId,
        object details,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO work_lifecycle_audit_events (
                project_id,
                process_area,
                event_type,
                prior_state,
                new_state,
                summary,
                reason,
                actor_user_id,
                related_entity_type,
                related_entity_id,
                event_json
            )
            VALUES (
                @project_id,
                @process_area,
                @event_type,
                @prior_state,
                @new_state,
                @summary,
                @reason,
                @actor_user_id,
                @related_entity_type,
                @related_entity_id,
                @event_json
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("process_area", processArea);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("prior_state", priorState);
        command.Parameters.AddWithValue("new_state", newState);
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("related_entity_type", relatedEntityType);
        command.Parameters.AddWithValue("related_entity_id", relatedEntityId);
        command.Parameters.Add("event_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(details);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object> LoadWeekSummaryAsync(
        NpgsqlConnection connection,
        Guid userId,
        bool showTimeEntry,
        CancellationToken cancellationToken)
    {
        if (!showTimeEntry)
        {
            return new { applicable = false, enteredHours = 0m, targetHours = 0m, days = Array.Empty<object>() };
        }

        var days = new List<object>();
        await using var command = new NpgsqlCommand("""
            WITH week AS (
                SELECT (
                    CURRENT_DATE - (EXTRACT(ISODOW FROM CURRENT_DATE)::integer - 1)
                )::date AS week_start
            ),
            dates AS (
                SELECT (
                    (SELECT week_start FROM week) + weekday_offset
                )::date AS work_date
                FROM generate_series(0, 4) AS weekday_offset
            )
            SELECT
                dates.work_date,
                COALESCE(SUM(entry.hours), 0)
            FROM dates
            LEFT JOIN time_entries entry
              ON entry.work_date = dates.work_date
             AND entry.user_id = @user_id
            GROUP BY dates.work_date
            ORDER BY dates.work_date;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        decimal entered = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var hours = reader.GetDecimal(1);
            entered += hours;
            days.Add(new { date = reader.GetFieldValue<DateOnly>(0), hours });
        }

        return new { applicable = true, enteredHours = entered, targetHours = 40m, days };
    }

    private static async Task<object> LoadAttentionSummaryAsync(
        NpgsqlConnection connection,
        WorkRegisterAccess access,
        bool broadScope,
        CancellationToken cancellationToken)
    {
        var roleCodes = access.RoleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canViewAllApprovals = access.CanEditAll;
        var isManager = roleCodes.Contains("MANAGER") || roleCodes.Contains("PEOPLE_MANAGER");
        var isProjectManager = roleCodes.Contains("PROJECT_MANAGER")
            || roleCodes.Contains("PROJECT_MANAGEMENT")
            || roleCodes.Contains("PROJECT_MANAGEMENT_LEAD")
            || roleCodes.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
            || roleCodes.Contains("PM_TEAM_LEAD");

        await using var command = new NpgsqlCommand("""
            SELECT
                (
                    SELECT COUNT(*)
                    FROM timesheet_day_statuses day_status
                    JOIN app_users submitter ON submitter.user_id = day_status.user_id
                    WHERE day_status.user_id <> @user_id
                      AND day_status.status = 'submitted'
                      AND (
                            @can_view_all_approvals
                         OR (
                                @is_manager
                            AND lower(COALESCE(submitter.manager_email, '')) = lower(COALESCE((
                                SELECT actor.email
                                FROM app_users actor
                                WHERE actor.user_id = @user_id
                            ), ''))
                         )
                         OR (
                                @is_project_manager
                            AND EXISTS (
                                SELECT 1
                                FROM time_entries scope_entry
                                JOIN projects scope_project
                                  ON scope_project.project_id = scope_entry.project_id
                                WHERE scope_entry.timesheet_id = day_status.timesheet_id
                                  AND scope_entry.work_date = day_status.work_date
                                  AND scope_project.project_manager_user_id = @user_id
                            )
                         )
                      )
                ),
                (
                    SELECT COUNT(*)
                    FROM time_entries entry
                    WHERE entry.user_id = @user_id
                      AND entry.status IN ('manager_declined', 'pm_declined')
                ),
                (
                    SELECT COUNT(*)
                    FROM projects project
                    LEFT JOIN work_register_project_lifecycle lifecycle
                      ON lifecycle.project_id = project.project_id
                    WHERE COALESCE(lifecycle.is_archived, FALSE) = FALSE
                      AND lower(COALESCE(project.status, '')) IN ('at risk', 'at_risk', 'blocked', 'needs review', 'needs_review')
                      AND (@broad_scope OR project.project_manager_user_id = @user_id)
                ),
                (
                    SELECT COUNT(*)
                    FROM work_closeout_records closeout
                    JOIN projects project ON project.project_id = closeout.project_id
                    WHERE closeout.closeout_status IN ('requested', 'ready')
                      AND (@broad_scope OR project.project_manager_user_id = @user_id)
                );
            """, connection);
        command.Parameters.AddWithValue("broad_scope", broadScope);
        command.Parameters.AddWithValue("user_id", access.ActualUserId);
        command.Parameters.AddWithValue("can_view_all_approvals", canViewAllApprovals);
        command.Parameters.AddWithValue("is_manager", isManager);
        command.Parameters.AddWithValue("is_project_manager", isProjectManager);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new
        {
            timeApprovals = reader.GetInt64(0),
            rejectedEntries = reader.GetInt64(1),
            projectAlerts = reader.GetInt64(2),
            closeoutPending = reader.GetInt64(3)
        };
    }

    private static async Task<object> LoadProjectHealthAsync(
        NpgsqlConnection connection,
        WorkRegisterAccess access,
        bool broadScope,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (
                    WHERE lower(COALESCE(project.status, '')) IN ('active', 'in progress', 'in_progress', 'healthy', 'on track', 'on_track')
                ),
                COUNT(*) FILTER (
                    WHERE lower(COALESCE(project.status, '')) IN ('needs review', 'needs_review', 'pending', 'hold', 'on hold', 'on_hold')
                ),
                COUNT(*) FILTER (
                    WHERE lower(COALESCE(project.status, '')) IN ('at risk', 'at_risk', 'blocked')
                )
            FROM projects project
            LEFT JOIN work_register_project_lifecycle lifecycle
              ON lifecycle.project_id = project.project_id
            WHERE COALESCE(lifecycle.is_archived, FALSE) = FALSE
              AND (@broad_scope OR project.project_manager_user_id = @user_id);
            """, connection);
        command.Parameters.AddWithValue("broad_scope", broadScope);
        command.Parameters.AddWithValue("user_id", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new
        {
            healthy = reader.GetInt64(0),
            needsReview = reader.GetInt64(1),
            atRisk = reader.GetInt64(2)
        };
    }

    private static async Task<List<object>> LoadDashboardProjectsAsync(
        NpgsqlConnection connection,
        WorkRegisterAccess access,
        bool broadScope,
        CancellationToken cancellationToken)
    {
        var projects = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                project.project_id,
                COALESCE(project.project_code, ''),
                COALESCE(project.project_name, ''),
                COALESCE(client.client_name, ''),
                COALESCE(project.status, ''),
                COALESCE(
                    ROUND(
                        100.0 * COUNT(task.task_id) FILTER (
                            WHERE lower(COALESCE(task.status, '')) IN ('complete', 'completed', 'closed', 'done')
                        ) / NULLIF(COUNT(task.task_id), 0)
                    ),
                    0
                )::integer AS completion_percent
            FROM projects project
            LEFT JOIN clients client ON client.client_id = project.client_id
            LEFT JOIN project_tasks task ON task.project_id = project.project_id
            LEFT JOIN work_register_project_lifecycle lifecycle
              ON lifecycle.project_id = project.project_id
            WHERE COALESCE(lifecycle.is_archived, FALSE) = FALSE
              AND (@broad_scope OR project.project_manager_user_id = @user_id)
            GROUP BY project.project_id, client.client_name
            ORDER BY
                CASE
                    WHEN lower(COALESCE(project.status, '')) IN ('at risk', 'at_risk', 'blocked') THEN 0
                    WHEN lower(COALESCE(project.status, '')) IN ('needs review', 'needs_review') THEN 1
                    ELSE 2
                END,
                project.updated_at DESC
            LIMIT 6;
            """, connection);
        command.Parameters.AddWithValue("broad_scope", broadScope);
        command.Parameters.AddWithValue("user_id", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(new
            {
                projectId = reader.GetGuid(0),
                projectCode = reader.GetString(1),
                projectName = reader.GetString(2),
                customerName = reader.GetString(3),
                status = reader.GetString(4),
                completionPercent = reader.GetInt32(5)
            });
        }
        return projects;
    }

    private static async Task<object> LoadBillingSnapshotAsync(
        NpgsqlConnection connection,
        WorkRegisterAccess access,
        bool broadScope,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(SUM(entry.hours) FILTER (
                    WHERE entry.billable = TRUE
                      AND entry.status = ANY(@approved_statuses)
                      AND NOT EXISTS (
                          SELECT 1
                          FROM billing_invoice_lines line
                          JOIN billing_invoices invoice
                            ON invoice.billing_invoice_id = line.billing_invoice_id
                          WHERE line.time_entry_id = entry.time_entry_id
                            AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                      )
                ), 0),
                (
                    SELECT COUNT(*)
                    FROM work_billing_readiness_reviews review
                    JOIN projects project ON project.project_id = review.project_id
                    WHERE review.review_status = 'ready'
                      AND (@broad_scope OR project.project_manager_user_id = @user_id)
                ),
                (
                    SELECT COUNT(*)
                    FROM billing_invoices invoice
                    JOIN projects project ON project.project_id = invoice.project_id
                    WHERE invoice.invoice_status IN ('draft', 'ready_for_pm', 'ready_for_accounting', 'approved', 'finalized')
                      AND (@broad_scope OR project.project_manager_user_id = @user_id)
                )
            FROM time_entries entry
            JOIN projects project ON project.project_id = entry.project_id
            WHERE (@broad_scope OR project.project_manager_user_id = @user_id);
            """, connection);
        command.Parameters.AddWithValue("approved_statuses", LifecycleApprovedTimeStatuses);
        command.Parameters.AddWithValue("broad_scope", broadScope);
        command.Parameters.AddWithValue("user_id", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new
        {
            unbilledHours = reader.GetDecimal(0),
            readyToInvoice = reader.GetInt64(1),
            openInvoices = reader.GetInt64(2)
        };
    }

    private static async Task<List<object>> LoadRecentItemsAsync(
        NpgsqlConnection connection,
        WorkRegisterAccess access,
        bool broadScope,
        CancellationToken cancellationToken)
    {
        var items = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                event.process_area,
                event.event_type,
                event.summary,
                event.project_id,
                COALESCE(project.project_code, ''),
                event.created_at
            FROM work_lifecycle_audit_events event
            JOIN projects project ON project.project_id = event.project_id
            WHERE (
                @broad_scope
                OR project.project_manager_user_id = @user_id
                OR event.actor_user_id = @user_id
            )
            ORDER BY event.created_at DESC
            LIMIT 8;
            """, connection);
        command.Parameters.AddWithValue("broad_scope", broadScope);
        command.Parameters.AddWithValue("user_id", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                processArea = reader.GetString(0),
                eventType = reader.GetString(1),
                summary = reader.GetString(2),
                projectId = reader.GetGuid(3),
                projectCode = reader.GetString(4),
                createdAt = reader.GetFieldValue<DateTimeOffset>(5)
            });
        }
        return items;
    }

    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString()
            ?? throw new InvalidOperationException("ProjectPulse database configuration is missing.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
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
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

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

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
}

public sealed record BillingReadinessSaveRequest(
    DateOnly BillingPeriodStart,
    DateOnly BillingPeriodEnd,
    string? PackageType,
    string? ReviewStatus,
    Dictionary<string, bool>? Checklist,
    string? Notes,
    string? Reason);

public sealed record CloseoutSaveRequest(
    string? BillingDisposition,
    bool DeliveryComplete,
    bool CustomerAcceptanceComplete,
    bool TimeExpenseComplete,
    bool BillingComplete,
    string? Reason,
    string? Notes);

public sealed record CloseoutReopenRequest(string? Reason);

public sealed record WorkLifecycleProject(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string Status,
    string ContractType,
    Guid? ProjectManagerUserId,
    string ProjectManagerName,
    string CustomerName,
    bool IsArchived);

public sealed record WorkLifecycleCapabilities(
    bool CanView,
    bool CanManageBillingReadiness,
    bool CanRequestCloseout,
    bool CanCompleteCloseout,
    bool CanReopenProject,
    bool IsAssignedProjectManager,
    bool IsViewAs);

public sealed record WorkBillingReadinessSnapshot(
    Guid ReviewId,
    DateOnly BillingPeriodStart,
    DateOnly BillingPeriodEnd,
    string PackageType,
    string ReviewStatus,
    Dictionary<string, bool> Checklist,
    string Notes,
    string ReviewedBy,
    DateTimeOffset UpdatedAt);

public sealed record WorkCloseoutSnapshot(
    string CloseoutStatus,
    string BillingDisposition,
    bool DeliveryComplete,
    bool CustomerAcceptanceComplete,
    bool TimeExpenseComplete,
    bool BillingComplete,
    string Reason,
    string Notes,
    string PriorProjectStatus);
