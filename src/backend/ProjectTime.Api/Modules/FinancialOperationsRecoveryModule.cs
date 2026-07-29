using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Group 5 authoritative reporting and recovery APIs for Modules 030, 031,
/// 039, 040, 041, and 042. Module 038 is intentionally not registered or
/// modified here; it remains a regression-only dependency.
/// </summary>
public static class FinancialOperationsRecoveryModule
{
    private const string ContractVersion = "2026-07-29.1";

    public static IEndpointRouteBuilder MapFinancialOperationsRecoveryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/financial-operations/reports/catalog",
            (Func<HttpContext, Task<IResult>>)GetCatalogAsync);
        endpoints.MapPost(
            "/api/financial-operations/reports/preview",
            (Func<FinancialReportRequest, HttpContext, Task<IResult>>)PreviewReportAsync);
        endpoints.MapPost(
            "/api/financial-operations/reports/run",
            (Func<FinancialReportRequest, HttpContext, Task<IResult>>)RunReportAsync);
        endpoints.MapGet(
            "/api/financial-operations/reports/history",
            (Func<HttpContext, Task<IResult>>)GetReportHistoryAsync);
        endpoints.MapGet(
            "/api/financial-operations/reports/runs/{runId:guid}/export",
            (Func<Guid, HttpContext, Task<IResult>>)ExportReportAsync);
        endpoints.MapGet(
            "/api/financial-operations/sources",
            (Func<HttpContext, Task<IResult>>)GetSourcesAsync);
        endpoints.MapPost(
            "/api/financial-operations/sources/{sourceKey}/retry",
            (Func<string, HttpContext, Task<IResult>>)RetrySourceAsync);
        endpoints.MapGet(
            "/api/financial-operations/workbench",
            (Func<HttpContext, Task<IResult>>)GetWorkbenchAsync);
        endpoints.MapPost(
            "/api/financial-operations/workbench/refresh",
            (Func<HttpContext, Task<IResult>>)RefreshWorkbenchAsync);
        endpoints.MapPost(
            "/api/financial-operations/workbench/{workItemId:guid}/{action}",
            (Func<Guid, string, FinancialWorkItemActionRequest, HttpContext, Task<IResult>>)UpdateWorkItemAsync);
        endpoints.MapGet(
            "/api/financial-operations/modules/{moduleCode}",
            (Func<string, HttpContext, Task<IResult>>)GetModuleRecoveryAsync);
        return endpoints;
    }

    private static async Task<IResult> GetCatalogAsync(HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        if (!CanViewReports(build.Context!.Truth.Actor)) return AccessDenied("Financial report access is required.");

        return Results.Ok(new
        {
            module = "030",
            status = "financial_report_catalog_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = Access(build.Context.Truth.Actor),
            reports = FinancialOperationsReportEngine.Catalog,
            sourceSummary = SourceSummary(build.Context.AllSources),
            sources = build.Context.AllSources,
            capabilities = Capabilities(build.Context.Truth.Actor),
            noPlaceholderReports = true,
            module038 = "regression_only_unchanged"
        });
    }

    private static async Task<IResult> PreviewReportAsync(
        FinancialReportRequest request,
        HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanRunReports(actor)) return AccessDenied("Run Financial Reports authority is required.");

        var definition = FinancialOperationsReportEngine.Find(request.ReportCode);
        if (definition is null) return InvalidReport();
        var result = FinancialOperationsReportEngine.Build(definition, request, build.Context);
        return Results.Ok(new
        {
            module = "030",
            status = "financial_report_preview_loaded",
            previewOnly = true,
            persisted = false,
            contractVersion = ContractVersion,
            access = Access(actor),
            definition,
            result
        });
    }

    private static async Task<IResult> RunReportAsync(
        FinancialReportRequest request,
        HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanRunReports(actor)) return AccessDenied("Run Financial Reports authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before creating a persisted report run.");

        var definition = FinancialOperationsReportEngine.Find(request.ReportCode);
        if (definition is null) return InvalidReport();
        var result = FinancialOperationsReportEngine.Build(definition, request, build.Context);

        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var runId = await FinancialOperationsRepository.SaveReportRunAsync(
                connection, actor, result, context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = "financial_report_run_completed",
                contractVersion = ContractVersion,
                runId,
                persisted = true,
                exportUrl = $"/api/financial-operations/reports/runs/{runId}/export",
                access = Access(actor),
                definition,
                result
            });
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                "report_run_history",
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "The report result was calculated, but its run history could not be persisted. Retry after the report-history source is restored.");
        }
    }

    private static async Task<IResult> GetReportHistoryAsync(HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanViewReports(actor)) return AccessDenied("Financial report access is required.");

        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var limit = Math.Clamp(ParseInt(context.Request.Query["limit"], 50), 1, 200);
            var history = await FinancialOperationsRepository.LoadReportHistoryAsync(
                connection, actor, limit, context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = history.Count == 0 ? "financial_report_history_empty" : "financial_report_history_loaded",
                contractVersion = ContractVersion,
                access = Access(actor),
                count = history.Count,
                history = history.Select(run => new
                {
                    run.RunId,
                    run.ReportCode,
                    run.ReportName,
                    run.ResultStatus,
                    run.RowCount,
                    run.Filters,
                    run.Sources,
                    run.StartedAt,
                    run.CompletedAt,
                    run.LastExportedAt,
                    exportUrl = $"/api/financial-operations/reports/runs/{run.RunId}/export"
                })
            });
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                "report_run_history",
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "Report history is temporarily unavailable. Report previews remain usable.");
        }
    }

    private static async Task<IResult> ExportReportAsync(Guid runId, HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanExportReports(actor)) return AccessDenied("Export Financial Reports authority is required.");

        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var run = await FinancialOperationsRepository.LoadReportRunAsync(
                connection, actor, runId, context.RequestAborted);
            if (run is null)
                return Results.NotFound(new
                {
                    module = "030",
                    status = "report_run_not_found_or_outside_scope",
                    message = "The report run was not found in the current user's scope."
                });

            var definition = FinancialOperationsReportEngine.Find(run.ReportCode);
            var csv = BuildCsv(run.Results, definition);
            await FinancialOperationsRepository.MarkReportExportedAsync(
                connection, runId, context.RequestAborted);
            return Results.File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv; charset=utf-8",
                $"projectpulse-{SafeFile(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}.csv");
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                "report_run_history",
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "The persisted report could not be exported. Retry after the report-history source is restored.");
        }
    }

    private static async Task<IResult> GetSourcesAsync(HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        if (!CanViewReports(build.Context!.Truth.Actor)
            && !CanViewWorkbench(build.Context.Truth.Actor))
            return AccessDenied("Financial source visibility is required.");

        return Results.Ok(new
        {
            module = "GROUP_5",
            modules = new[] { "030", "031", "039", "040", "041", "042" },
            status = "financial_operation_sources_loaded",
            contractVersion = ContractVersion,
            access = Access(build.Context.Truth.Actor),
            summary = SourceSummary(build.Context.AllSources),
            sources = build.Context.AllSources,
            retryIsSourceSpecific = true,
            technicalDiagnosticsSanitized = true,
            rawExceptionDetailsReturned = false
        });
    }

    private static async Task<IResult> RetrySourceAsync(
        string sourceKey,
        HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanRetrySources(actor)) return AccessDenied("Retry Financial Sources authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before retrying a financial source.");

        var normalized = NormalizeSourceKey(sourceKey);
        var source = build.Context.AllSources.FirstOrDefault(item =>
            item.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return Results.NotFound(new
            {
                module = "031",
                status = "financial_source_not_found",
                source = normalized,
                message = "The requested source is not registered in the Group 5 financial-source contract."
            });
        }

        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var actionStatus = source.Status == "healthy" ? "succeeded" : "failed";
            await FinancialOperationsRepository.RecordActionAsync(
                connection,
                null,
                null,
                normalized,
                "source_retry",
                actionStatus,
                actor,
                source.DiagnosticCode,
                source.Message,
                context.TraceIdentifier,
                new { source.ObservedAt, source.RecordCount },
                context.RequestAborted);
            return Results.Ok(new
            {
                module = ModuleForSource(normalized),
                status = source.Status == "healthy"
                    ? "financial_source_retry_succeeded"
                    : "financial_source_retry_requires_attention",
                source,
                retryAt = DateTimeOffset.UtcNow,
                message = source.Status == "healthy"
                    ? $"{source.Name} loaded successfully."
                    : $"{source.Name} remains unavailable. Healthy content on the page is preserved."
            });
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                normalized,
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "The source retry could not be recorded. Other healthy content remains visible.");
        }
    }

    private static async Task<IResult> GetWorkbenchAsync(HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanViewWorkbench(actor)) return AccessDenied("Financial Operations Workbench access is required.");

        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var status = (context.Request.Query["status"].ToString() ?? string.Empty).Trim();
            var limit = Math.Clamp(ParseInt(context.Request.Query["limit"], 200), 1, 500);
            var projectIds = build.Context.Truth.Projects.Select(project => project.ProjectId).ToArray();
            var items = await FinancialOperationsRepository.LoadWorkItemsAsync(
                connection, actor, projectIds, status, limit, context.RequestAborted);
            var actions = actor.Broad
                ? await FinancialOperationsRepository.LoadActionsAsync(
                    connection, 50, context.RequestAborted)
                : new List<FinancialOperationsAction>();
            return Results.Ok(new
            {
                module = "031",
                moduleName = "Financial Operations Workbench",
                status = items.Count == 0 ? "financial_operations_queue_empty" : "financial_operations_queue_loaded",
                contractVersion = ContractVersion,
                access = Access(actor),
                capabilities = Capabilities(actor),
                summary = new
                {
                    total = items.Count,
                    open = items.Count(item => item.WorkStatus == "open"),
                    acknowledged = items.Count(item => item.WorkStatus == "acknowledged"),
                    critical = items.Count(item => item.Priority == "critical"),
                    high = items.Count(item => item.Priority == "high"),
                    sourceFailures = items.Count(item => item.ItemType == "source_failure")
                },
                items,
                recentActions = actions,
                sources = build.Context.AllSources
            });
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                "financial_operations_workbench",
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "The Financial Operations Workbench is temporarily unavailable. Module-specific financial content remains usable.");
        }
    }

    private static async Task<IResult> RefreshWorkbenchAsync(HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanManageRecovery(actor)) return AccessDenied("Manage Financial Operations Recovery authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before refreshing the recovery queue.");

        var derived = FinancialOperationsWorkItemFactory.Build(build.Context);
        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            await FinancialOperationsRepository.UpsertWorkItemsAsync(
                connection, derived, context.RequestAborted);
            await FinancialOperationsRepository.RecordActionAsync(
                connection,
                null,
                null,
                "financial_operations_workbench",
                "workbench_refresh",
                "succeeded",
                actor,
                "",
                $"{derived.Length} current recovery item(s) evaluated.",
                context.TraceIdentifier,
                new { derivedCount = derived.Length },
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "031",
                status = "financial_operations_workbench_refreshed",
                derivedCount = derived.Length,
                message = "Current source failures and project financial blockers were synchronized into the recovery queue."
            });
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                "financial_operations_workbench",
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "The recovery queue could not be refreshed. Existing queue and module content remain unchanged.");
        }
    }

    private static async Task<IResult> UpdateWorkItemAsync(
        Guid workItemId,
        string action,
        FinancialWorkItemActionRequest request,
        HttpContext context)
    {
        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanManageRecovery(actor)) return AccessDenied("Manage Financial Operations Recovery authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before changing a recovery work item.");
        var normalizedAction = action.ToLowerInvariant() is "acknowledged" or "dismissed"
            ? action.ToLowerInvariant()
            : "resolved";
        var note = (request.Note ?? string.Empty).Trim();
        if (note.Length < 5)
            return Results.BadRequest(new
            {
                module = "031",
                status = "resolution_note_required",
                message = "Enter a specific note before changing the recovery work item."
            });

        try
        {
            await using var connection = await FinancialOperationsRepository.OpenAsync(context.RequestAborted);
            if (!await FinancialOperationsRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var updated = await FinancialOperationsRepository.UpdateWorkItemAsync(
                connection, workItemId, normalizedAction, note, actor, context.RequestAborted);
            if (!updated)
                return Results.NotFound(new
                {
                    module = "031",
                    status = "financial_work_item_not_found",
                    message = "The recovery work item was not found."
                });
            await FinancialOperationsRepository.RecordActionAsync(
                connection,
                workItemId,
                null,
                "financial_operations_workbench",
                $"work_item_{normalizedAction}",
                "succeeded",
                actor,
                "",
                note,
                context.TraceIdentifier,
                new { normalizedAction },
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "031",
                status = $"financial_work_item_{normalizedAction}",
                workItemId,
                message = $"The recovery work item was {normalizedAction}."
            });
        }
        catch (Exception exception)
        {
            return SourceFailure(
                context,
                "financial_operations_workbench",
                FinancialOperationsSourceLoader.Diagnostic(exception),
                "The work item could not be updated. No recovery state was changed.");
        }
    }

    private static async Task<IResult> GetModuleRecoveryAsync(
        string moduleCode,
        HttpContext context)
    {
        var normalized = moduleCode.Trim().ToUpperInvariant();
        if (normalized is not ("030" or "031" or "039" or "040" or "041" or "042"))
            return Results.NotFound(new { status = "group_5_module_not_found" });

        var build = await BuildContextAsync(context);
        if (build.Failure is not null) return build.Failure;
        var actor = build.Context!.Truth.Actor;
        if (!CanViewModule(actor, normalized)) return AccessDenied($"Module {normalized} recovery access is required.");

        var projects = build.Context.Truth.Projects.Select(project =>
        {
            build.Context.Supplemental.ApprovedTime.TryGetValue(project.ProjectId, out var approved);
            build.Context.Supplemental.BillingReadiness.TryGetValue(project.ProjectId, out var billing);
            build.Context.Supplemental.Closeout.TryGetValue(project.ProjectId, out var closeout);
            var notifications = build.Context.Supplemental.Notifications
                .Where(item => item.ProjectId == project.ProjectId)
                .OrderByDescending(item => item.CreatedAt)
                .Take(10)
                .ToArray();
            var expenseSummary = new
            {
                count = project.Expenses.Length,
                total = project.UploadedExpenses,
                reimbursable = SumKnown(project.Expenses.Select(expense => expense.ReimbursableAmount)),
                latest = project.Expenses.OrderByDescending(expense => expense.UploadedAt)
                    .Take(5).Select(expense => new
                    {
                        expense.UploadId,
                        expense.OwnerName,
                        expense.PeriodStart,
                        expense.PeriodEnd,
                        expense.SourceMode,
                        expense.SourceFormat,
                        expense.TotalAmount,
                        expense.ReimbursableAmount,
                        expense.BillingTreatment,
                        expense.UploadedAt
                    })
            };
            return new
            {
                project.ProjectId,
                project.CustomerName,
                project.ProjectCode,
                project.ProjectName,
                project.ProjectStatus,
                project.ProjectManagerName,
                project.ContractType,
                project.VisibilityLevel,
                project.PlannedHours,
                project.UsedHours,
                approvedHours = approved?.ApprovedHours,
                approvedLineCount = approved?.ApprovedLineCount ?? 0,
                project.RemainingHours,
                project.UploadedExpenses,
                project.ForecastedFinalCost,
                project.CurrentVariance,
                project.BudgetStatus,
                project.Missing,
                billingReadiness = billing,
                closeout,
                notificationSummary = new
                {
                    count = notifications.Length,
                    failed = notifications.Count(item => item.DeliveryStatus == "failed"),
                    held = notifications.Count(item => item.DeliveryStatus == "held"),
                    latest = notifications.FirstOrDefault(),
                    items = notifications
                },
                expenseSummary
            };
        }).ToArray();

        var relevantSourceKeys = ModuleSources(normalized);
        var sources = build.Context.AllSources
            .Where(source => relevantSourceKeys.Contains(source.Key, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return Results.Ok(new
        {
            module = normalized,
            status = sources.Any(source => source.Status == "unavailable")
                ? "financial_module_loaded_with_source_failures"
                : projects.Length == 0 ? "financial_module_no_data" : "financial_module_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = Access(actor),
            projects,
            sources,
            retry = sources.ToDictionary(source => source.Key, source => source.RetryEndpoint),
            module038 = "regression_only_unchanged",
            module041MailOwner = normalized == "041" ? "Group 4 routing and Module 065 delivery" : null,
            module042ExpenseBoundary = normalized == "042"
                ? "Intentional current-expense summary and drill-down only; Module 005 is not mounted or duplicated."
                : null,
            security = new
            {
                actualSessionVerified = true,
                effectiveSessionVerified = true,
                rawDatabaseErrorsReturned = false,
                healthySourcesRemainVisible = true
            }
        });
    }

    private static async Task<BuildContextResult> BuildContextAsync(HttpContext context)
    {
        var truth = await ProjectFinancialTruthModule.BuildFinancialOperationsTruthAsync(context);
        if (truth.Failure is not null) return new(null, truth.Failure);
        var supplemental = await FinancialOperationsSourceLoader.LoadAsync(
            truth.Snapshot!, context.RequestAborted);
        return new(new FinancialOperationsContext(truth.Snapshot!, supplemental), null);
    }

    private static bool CanViewReports(FinancialOperationsActor actor) =>
        actor.Broad
        || actor.HasPermission(
            "VIEW_FINANCIAL_REPORT_CENTER", "VIEW_REPORTS",
            "VIEW_EXECUTIVE_REPORTING", "MANAGE_ALL")
        || actor.HasRole(
            "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD", "ENGINEERING",
            "ENGINEER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD", "MANAGER",
            "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT");

    private static bool CanRunReports(FinancialOperationsActor actor) =>
        CanViewReports(actor)
        && (actor.Broad
            || actor.HasPermission("RUN_FINANCIAL_REPORTS", "MANAGE_REPORTS", "MANAGE_ALL")
            || actor.HasRole(
                "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD",
                "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD", "ENGINEERING",
                "ENGINEER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD", "MANAGER",
                "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"));

    private static bool CanExportReports(FinancialOperationsActor actor) =>
        actor.Broad
        || actor.HasPermission(
            "EXPORT_FINANCIAL_REPORTS", "EXPORT_TIME_EXCEL",
            "EXPORT_TIME_PDF", "MANAGE_ALL")
        || actor.HasRole(
            "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD");

    private static bool CanViewWorkbench(FinancialOperationsActor actor) =>
        actor.Broad
        || actor.HasPermission("VIEW_FINANCIAL_OPERATIONS_WORKBENCH", "MANAGE_ALL")
        || actor.HasRole(
            "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD", "EXECUTIVE");

    private static bool CanManageRecovery(FinancialOperationsActor actor) =>
        !actor.IsViewAs
        && (actor.Broad
            || actor.HasPermission(
                "MANAGE_FINANCIAL_OPERATIONS_RECOVERY",
                "SYSTEM_ADMINISTRATION", "MANAGE_ALL"));

    private static bool CanRetrySources(FinancialOperationsActor actor) =>
        !actor.IsViewAs
        && (CanManageRecovery(actor)
            || actor.HasPermission("RETRY_FINANCIAL_SOURCES"));

    private static bool CanViewModule(
        FinancialOperationsActor actor,
        string moduleCode) => moduleCode switch
    {
        "030" => CanViewReports(actor),
        "031" => CanViewWorkbench(actor),
        "039" => actor.Broad || actor.HasPermission(
            "VIEW_ACCOUNTING_RECONCILIATION_RECOVERY", "VIEW_ACCOUNT_RECONCILIATION"),
        "040" => actor.Broad || actor.HasPermission(
            "VIEW_PROJECT_CLOSEOUT_RECOVERY", "VIEW_PROJECT_WORKSPACE")
            || actor.HasRole("PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"),
        "041" => actor.Broad || actor.HasPermission(
            "VIEW_CLOSEOUT_NOTIFICATION_RECOVERY", "VIEW_CLOSEOUT_NOTIFICATION_ROUTING")
            || actor.HasRole("PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"),
        "042" => actor.Broad || actor.HasPermission(
            "VIEW_BILLING_RECOVERY", "VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT", "VIEW_ACCOUNT_RECONCILIATION")
            || actor.HasRole("PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"),
        _ => false
    };

    private static object Access(FinancialOperationsActor actor) => new
    {
        actor.ActualUserId,
        actor.EffectiveUserId,
        actor.Email,
        actor.DisplayName,
        actor.Roles,
        actor.IsViewAs,
        readOnly = actor.IsViewAs,
        actualSessionVerified = true,
        effectiveSessionVerified = true,
        viewAsTransfersMutationAuthority = false
    };

    private static object Capabilities(FinancialOperationsActor actor) => new
    {
        canViewReports = CanViewReports(actor),
        canRunReports = CanRunReports(actor) && !actor.IsViewAs,
        canExportReports = CanExportReports(actor),
        canViewWorkbench = CanViewWorkbench(actor),
        canManageRecovery = CanManageRecovery(actor),
        canRetrySources = CanRetrySources(actor)
    };

    private static object SourceSummary(FinancialOperationsSourceState[] sources) => new
    {
        total = sources.Length,
        healthy = sources.Count(source => source.Status == "healthy"),
        partial = sources.Count(source => source.Status == "partial"),
        unavailable = sources.Count(source => source.Status == "unavailable"),
        requiredUnavailable = sources.Count(source =>
            source.Required && source.Status == "unavailable")
    };

    private static string[] ModuleSources(string moduleCode) => moduleCode switch
    {
        "039" => ["projects", "assignments", "time_entries", "approved_time_entries", "project_expenses", "billing_readiness_reviews", "sell_commercial_model"],
        "040" => ["projects", "approved_time_entries", "billing_readiness_reviews", "project_closeout_records", "cost_alerts"],
        "041" => ["projects", "project_closeout_records", "project_notification_dispatches"],
        "042" => ["projects", "approved_time_entries", "project_expenses", "billing_readiness_reviews", "sell_commercial_model"],
        _ => ["projects", "assignments", "time_entries", "project_expenses", "project_metadata", "sell_commercial_model"]
    };

    private static string ModuleForSource(string source) => source switch
    {
        "billing_readiness_reviews" => "039",
        "project_closeout_records" => "040",
        "project_notification_dispatches" => "041",
        "approved_time_entries" or "project_expenses" => "042",
        _ => "030"
    };

    private static string NormalizeSourceKey(string value)
    {
        var clean = new string((value ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .ToArray());
        return clean.Length <= 120 ? clean : clean[..120];
    }

    private static IResult InvalidReport() => Results.BadRequest(new
    {
        module = "030",
        status = "financial_report_not_found",
        message = "Select a registered financial report from the catalog."
    });

    private static IResult AccessDenied(string message) => Results.Json(new
    {
        module = "GROUP_5",
        status = "financial_operations_access_required",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsReadOnly(string message) => Results.Json(new
    {
        module = "GROUP_5",
        status = "view_as_read_only",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult MigrationRequired() => Results.Json(new
    {
        module = "GROUP_5",
        status = "migration_051_required",
        migration = "051_financial_operations_reporting_recovery",
        message = "Group 5 source is installed, but its durable report-history and recovery-workbench schema has not been applied."
    }, statusCode: StatusCodes.Status409Conflict);

    private static IResult SourceFailure(
        HttpContext context,
        string source,
        string diagnosticCode,
        string message) => Results.Json(new
    {
        module = ModuleForSource(source),
        status = "financial_source_unavailable",
        source,
        diagnosticCode,
        correlationId = context.TraceIdentifier,
        message,
        retry = $"/api/financial-operations/sources/{Uri.EscapeDataString(source)}/retry",
        technicalDetailsAvailableInDiagnostics = true,
        rawExceptionReturned = false
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string BuildCsv(
        JsonElement results,
        FinancialReportDefinition? definition)
    {
        if (results.ValueKind != JsonValueKind.Array) return string.Empty;
        var rows = results.EnumerateArray().ToArray();
        var keys = definition?.Columns.Select(column => column.Key).ToList()
            ?? new List<string>();
        if (keys.Count == 0 && rows.Length > 0 && rows[0].ValueKind == JsonValueKind.Object)
            keys.AddRange(rows[0].EnumerateObject().Select(property => property.Name));
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', keys.Select(Csv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', keys.Select(key =>
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !row.TryGetProperty(key, out var value)) return Csv(string.Empty);
                return Csv(JsonCsvValue(value));
            })));
        }
        return builder.ToString();
    }

    private static string JsonCsvValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.ToString()
    };

    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    private static string SafeFile(string value)
    {
        var safe = new string((value ?? "financial-report")
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        return safe.Trim('-') is { Length: > 0 } result ? result : "financial-report";
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private sealed record BuildContextResult(
        FinancialOperationsContext? Context,
        IResult? Failure);
}
