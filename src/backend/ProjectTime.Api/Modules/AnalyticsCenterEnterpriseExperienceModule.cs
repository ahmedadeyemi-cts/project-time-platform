using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Enterprise Analytics Center experience: dashboard overview, collapsible report
/// library, report-specific multi-select criteria, branded PDF/XLSX exports,
/// recent/favorite activity, and recurring Module 065 delivery.
/// </summary>
public static class AnalyticsCenterEnterpriseExperienceModule
{
    private const string ContractVersion = "030-analytics-enterprise-v2-20260801";

    public static IEndpointRouteBuilder MapAnalyticsCenterEnterpriseExperienceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/analytics/v2/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        endpoints.MapGet(
            "/api/analytics/v2/catalog",
            (Func<HttpContext, Task<IResult>>)GetCatalogAsync);
        endpoints.MapPost(
            "/api/analytics/v2/filter-options",
            (Func<AnalyticsExperienceRequest, HttpContext, Task<IResult>>)GetFilterOptionsAsync);
        endpoints.MapPost(
            "/api/analytics/v2/preview",
            (Func<AnalyticsExperienceRequest, HttpContext, Task<IResult>>)PreviewAsync);
        endpoints.MapPost(
            "/api/analytics/v2/run",
            (Func<AnalyticsExperienceRequest, HttpContext, Task<IResult>>)RunAsync);
        endpoints.MapGet(
            "/api/analytics/v2/history",
            (Func<HttpContext, Task<IResult>>)HistoryAsync);
        endpoints.MapGet(
            "/api/analytics/v2/runs/{runId:guid}/export",
            (Func<Guid, string?, HttpContext, Task<IResult>>)ExportAsync);
        endpoints.MapPost(
            "/api/analytics/v2/activity/{reportCode}/view",
            (Func<string, HttpContext, Task<IResult>>)RecordViewAsync);
        endpoints.MapPut(
            "/api/analytics/v2/activity/{reportCode}/favorite",
            (Func<string, AnalyticsActivityRequest, HttpContext, Task<IResult>>)SetFavoriteAsync);
        endpoints.MapGet(
            "/api/analytics/v2/recipient-options",
            (Func<HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.GetRecipientOptionsAsync);
        endpoints.MapGet(
            "/api/analytics/v2/schedules",
            (Func<HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.GetSchedulesAsync);
        endpoints.MapPost(
            "/api/analytics/v2/schedules",
            (Func<AnalyticsScheduleUpsertRequest, HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.SaveScheduleAsync);
        endpoints.MapPut(
            "/api/analytics/v2/schedules/{scheduleId:guid}",
            (Func<Guid, AnalyticsScheduleUpsertRequest, HttpContext, Task<IResult>>)((scheduleId, request, context) =>
                AnalyticsCenterScheduleService.SaveScheduleAsync(request with { ScheduleId = scheduleId }, context)));
        endpoints.MapDelete(
            "/api/analytics/v2/schedules/{scheduleId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.DeleteScheduleAsync);
        endpoints.MapPost(
            "/api/analytics/v2/schedules/{scheduleId:guid}/run-now",
            (Func<Guid, HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.RunScheduleNowAsync);
        endpoints.MapGet(
            "/api/analytics/v2/schedule-runs",
            (Func<HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.GetRunHistoryAsync);
        endpoints.MapGet(
            "/api/analytics/v2/schedules/readiness",
            (Func<HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.GetReadinessAsync);
        endpoints.MapPost(
            "/api/analytics/v2/schedules/run-due",
            (Func<HttpContext, Task<IResult>>)AnalyticsCenterScheduleService.RunDueAsync);

        if (endpoints is WebApplication application)
            AnalyticsCenterScheduler.Start(application);
        return endpoints;
    }

    private static async Task<IResult> GetCatalogAsync(HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(built.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        var reports = AnalyticsCenterExperienceScope.Catalog(built.Seed);
        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = reports.Length == 0 ? "analytics_catalog_empty_for_scope" : "analytics_catalog_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            categories = reports.Select(report => report.Category).Distinct().OrderBy(value => value),
            reportCount = reports.Length,
            reports,
            access = AnalyticsCenterExperienceScope.Access(built.Seed.Actor),
            capabilities = AnalyticsCenterExperienceScope.Capabilities(built.Seed.Actor),
            workflow = new[]
            {
                "Select report",
                "Set criteria",
                "Preview report",
                "Run & save",
                "Export US Signal PDF or Excel",
                "Schedule recurring delivery"
            }
        });
    }

    private static async Task<IResult> GetFilterOptionsAsync(
        AnalyticsExperienceRequest request,
        HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(built.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        var definition = EnterpriseReportingCatalog.Find(
            built.Seed.Reporting,
            request.ReportCode);
        if (definition is null)
            return AnalyticsCenterExperienceScope.ReportNotFound(built.Seed.Reporting);
        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_multiselect_filter_options_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            payload = AnalyticsCenterExperienceScope.FilterOptions(
                built.Seed,
                definition,
                request)
        });
    }

    private static async Task<IResult> PreviewAsync(
        AnalyticsExperienceRequest request,
        HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildForReportAsync(context, request);
        if (built.Failure is not null) return built.Failure;
        if (!AnalyticsCenterExperienceScope.CanRun(built.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Run Analytics authority is required.");
        var result = AnalyticsCenterExperienceScope.BuildResult(
            built.Seed,
            built.Definition!,
            request);
        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_preview_loaded",
            contractVersion = ContractVersion,
            previewOnly = true,
            persisted = false,
            definition = AnalyticsCenterExperienceScope.AdaptDefinition(built.Definition!),
            result,
            access = AnalyticsCenterExperienceScope.Access(built.Seed.Actor)
        });
    }

    private static async Task<IResult> RunAsync(
        AnalyticsExperienceRequest request,
        HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildForReportAsync(context, request);
        if (built.Failure is not null) return built.Failure;
        var actor = built.Seed!.Actor;
        if (!AnalyticsCenterExperienceScope.CanRun(actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Run Analytics authority is required.");
        if (actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("recording an Analytics run");
        var result = AnalyticsCenterExperienceScope.BuildResult(
            built.Seed,
            built.Definition!,
            request);
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return Migration055Required();
            var runId = await EnterpriseReportingRepository.SaveRunAsync(
                connection,
                built.Seed.Reporting,
                result,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                moduleName = "Analytics Center",
                status = "analytics_run_completed",
                contractVersion = ContractVersion,
                persisted = true,
                runId,
                definition = AnalyticsCenterExperienceScope.AdaptDefinition(built.Definition!),
                result,
                exportUrls = ExportUrls(runId),
                access = AnalyticsCenterExperienceScope.Access(actor)
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(
                context,
                exception,
                "The report was calculated, but its immutable run evidence could not be stored.");
        }
    }

    private static async Task<IResult> HistoryAsync(HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(built.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return Migration055Required();
            var limit = int.TryParse(context.Request.Query["limit"], out var parsed) ? parsed : 100;
            var history = await EnterpriseReportingRepository.LoadHistoryAsync(
                connection,
                built.Seed.Actor,
                limit,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                moduleName = "Analytics Center",
                status = history.Length == 0 ? "analytics_history_empty" : "analytics_history_loaded",
                contractVersion = ContractVersion,
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
                    run.CreatedAt,
                    exportUrls = ExportUrls(run.RunId)
                })
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "Analytics run history is temporarily unavailable.");
        }
    }

    private static async Task<IResult> ExportAsync(
        Guid runId,
        string? format,
        HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        var actor = built.Seed!.Actor;
        if (!AnalyticsCenterExperienceScope.CanExport(actor))
            return AnalyticsCenterExperienceScope.AccessDenied("Export Analytics authority is required.");
        if (actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("exporting Analytics results");
        var normalized = AnalyticsBrandedExportBuilder.NormalizeFormat(format);
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return Migration055Required();
            if (normalized == "pdf")
            {
                await using var scheduleConnection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
                if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(scheduleConnection, context.RequestAborted))
                    return AnalyticsCenterScheduleService.MigrationRequired();
            }
            var run = await EnterpriseReportingRepository.LoadRunAsync(
                connection,
                actor,
                runId,
                context.RequestAborted);
            if (run is null)
                return Results.NotFound(new
                {
                    module = "030",
                    status = "analytics_run_not_found_or_outside_scope"
                });
            var export = AnalyticsBrandedExportBuilder.Build(run, normalized);
            await EnterpriseReportingRepository.RecordExportAsync(
                connection,
                runId,
                actor,
                export.Format,
                run.RowCount,
                export.Content,
                context.RequestAborted);
            return Results.File(export.Content, export.ContentType, export.FileName);
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The Analytics report could not be exported.");
        }
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        var seed = built.Seed!;
        if (!AnalyticsCenterExperienceScope.CanView(seed.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        var catalog = AnalyticsCenterExperienceScope.Catalog(seed);
        var activity = new Dictionary<string, (bool Favorite, int ViewCount, DateTimeOffset? LastViewedAt)>(StringComparer.OrdinalIgnoreCase);
        EnterpriseReportRunRecord[] history = [];
        AnalyticsScheduleRun[] scheduleRuns = [];
        AnalyticsSchedule[] schedules = [];
        var scheduleReady = false;
        try
        {
            await using var reportingConnection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (await EnterpriseReportingRepository.MigrationReadyAsync(reportingConnection, context.RequestAborted))
                history = await EnterpriseReportingRepository.LoadHistoryAsync(
                    reportingConnection,
                    seed.Actor,
                    100,
                    context.RequestAborted);
        }
        catch { history = []; }
        try
        {
            await using var scheduleConnection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            scheduleReady = await AnalyticsCenterScheduleRepository.MigrationReadyAsync(
                scheduleConnection,
                context.RequestAborted);
            if (scheduleReady)
            {
                activity = await AnalyticsCenterScheduleRepository.LoadActivityAsync(
                    scheduleConnection,
                    seed.Actor.ActualUserId,
                    context.RequestAborted);
                scheduleRuns = await AnalyticsCenterScheduleRepository.LoadScheduleRunsAsync(
                    scheduleConnection,
                    seed.Actor,
                    100,
                    context.RequestAborted);
                schedules = await AnalyticsCenterScheduleRepository.LoadSchedulesAsync(
                    scheduleConnection,
                    seed.Actor,
                    includeDisabled: true,
                    context.RequestAborted);
            }
        }
        catch { scheduleReady = false; }

        var projects = seed.Projects;
        var active = projects.Count(project => !new[] { "completed", "closed", "cancelled", "canceled", "archived" }
            .Contains(project.ProjectStatus, StringComparer.OrdinalIgnoreCase));
        var contracted = SumKnown(projects.Select(project => project.ContractedValue));
        var plannedHours = projects.Sum(project => project.PlannedHours);
        var usedHours = projects.Sum(project => project.UsedHours);
        var utilization = plannedHours > 0 ? usedHours / plannedHours * 100m : (decimal?)null;
        var budgets = SumKnown(projects.Select(project =>
            project.LaborBudget.HasValue || project.ExpenseBudget.HasValue
                ? (project.LaborBudget ?? 0m) + (project.ExpenseBudget ?? 0m)
                : null));
        var variance = SumKnown(projects.Select(project => project.CurrentVariance));
        var variancePercent = budgets.HasValue && budgets.Value != 0 && variance.HasValue
            ? variance.Value / budgets.Value * 100m
            : (decimal?)null;
        var yearStart = new DateOnly(DateTime.UtcNow.Year, 1, 1);
        var newCustomers = projects.Where(project => project.StartDate.HasValue && project.StartDate.Value >= yearStart)
            .Select(project => project.ClientId?.ToString() ?? project.CustomerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var pmRatios = projects.Where(project => project.ProjectManagerUserId.HasValue)
            .GroupBy(project => project.ProjectManagerUserId!.Value)
            .Select(group =>
            {
                var planned = group.Sum(project => project.PlannedHours);
                return planned > 0 ? group.Sum(project => project.UsedHours) / planned * 100m : (decimal?)null;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        var pmWorkload = pmRatios.Length == 0 ? (decimal?)null : pmRatios.Average();
        var deliveryRecent = scheduleRuns.Where(run => run.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-30)).ToArray();
        var deliveryHealth = deliveryRecent.Length == 0
            ? (decimal?)null
            : deliveryRecent.Sum(run => run.SentCount + run.QueuedCount)
                / (decimal)Math.Max(1, deliveryRecent.Sum(run => run.RecipientCount)) * 100m;
        var metrics = new[]
        {
            Metric("portfolioValue", "Contracted value", contracted, "Visible role-scoped portfolio", "currency", null),
            new AnalyticsDashboardMetric("activeProjects", "Active projects", active.ToString("N0"), $"{projects.Length:N0} visible projects", "blue", Percentage(active, projects.Length), true),
            Metric("utilization", "Billable utilization", utilization, "Used hours compared with planned hours", "percent", utilization),
            new AnalyticsDashboardMetric("hoursUsed", "Hours used", usedHours.ToString("N1"), "Current visible project scope", "blue", Percentage(usedHours, plannedHours), true),
            Metric("forecastVariance", "Forecast variance", variancePercent, variance.HasValue ? $"{variance.Value:C0} current variance" : "Financial variance not available", "percent", variancePercent.HasValue ? Math.Abs(variancePercent.Value) : null, variancePercent.HasValue && variancePercent.Value < 0 ? "critical" : "purple"),
            new AnalyticsDashboardMetric("newCustomers", "New customers (YTD)", newCustomers.ToString("N0"), "Customers with visible projects starting this year", "cyan", null, true),
            Metric("pmWorkload", "PM workload", pmWorkload, "Average used versus planned hours", "percent", pmWorkload, "amber"),
            Metric("deliveryHealth", "Report delivery health", deliveryHealth, deliveryRecent.Length == 0 ? "No recurring delivery evidence in the last 30 days" : $"{deliveryRecent.Length} schedule run(s) in the last 30 days", "percent", deliveryHealth, "green")
        };
        var historyByCode = history.GroupBy(run => run.ReportCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(run => run.CreatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var recent = catalog.Select(report =>
        {
            activity.TryGetValue(report.Code, out var preference);
            historyByCode.TryGetValue(report.Code, out var lastRun);
            var lastViewed = preference.LastViewedAt ?? lastRun?.CreatedAt;
            return new AnalyticsRecentItem(
                report.Code,
                report.Name,
                report.Category,
                report.Description,
                preference.Favorite,
                preference.ViewCount,
                lastViewed,
                lastRun?.RunId,
                lastRun?.RowCount,
                lastRun?.ResultStatus ?? "not_run");
        })
            .Where(item => item.Favorite || item.LastViewedAt.HasValue)
            .OrderByDescending(item => item.Favorite)
            .ThenByDescending(item => item.LastViewedAt)
            .Take(12)
            .ToArray();
        if (recent.Length == 0)
        {
            recent = catalog.Take(8).Select(report => new AnalyticsRecentItem(
                report.Code,
                report.Name,
                report.Category,
                report.Description,
                false,
                0,
                null,
                null,
                null,
                "not_run")).ToArray();
        }
        var sources = seed.Reporting.Sources.Append(seed.Directory.Source)
            .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_enterprise_overview_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            dataAsOf = seed.Reporting.Truth.GeneratedAt,
            metrics,
            recentlyViewed = recent,
            favorites = recent.Where(item => item.Favorite),
            dashboards = catalog.Where(report => new[]
            {
                "project_financial_health", "engineer_utilization", "project_manager_portfolio",
                "customer_project_summary", "project_hours_consumption", "service_health_slo",
                "project_budget_forecast", "project_portfolio"
            }.Contains(report.Code, StringComparer.OrdinalIgnoreCase)).Take(8),
            schedules = new
            {
                ready = scheduleReady,
                total = schedules.Length,
                enabled = schedules.Count(schedule => schedule.Enabled),
                nextRunAt = schedules.Where(schedule => schedule.Enabled).Select(schedule => schedule.NextRunAt).Where(value => value.HasValue).Min()
            },
            sourceQuality = new
            {
                total = sources.Length,
                healthy = sources.Count(source => source.Status == "healthy"),
                degraded = sources.Count(source => source.Status is "partial" or "unavailable" or "restricted"),
                sources
            },
            access = AnalyticsCenterExperienceScope.Access(seed.Actor),
            capabilities = AnalyticsCenterExperienceScope.Capabilities(seed.Actor),
            coverage = new[]
            {
                "Financials", "Engineers", "Project Managers", "Customers", "Projects", "Teams",
                "Billing", "Time", "Utilization", "Closeout", "Service Delivery", "Operations",
                "Governance", "Customer Acceptance", "Secure Project Information", "PMO Controls"
            }
        });
    }

    private static async Task<IResult> RecordViewAsync(string reportCode, HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(built.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        if (built.Seed.Actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("recording personal report activity");
        if (EnterpriseReportingCatalog.Find(built.Seed.Reporting, reportCode) is null)
            return AnalyticsCenterExperienceScope.ReportNotFound(built.Seed.Reporting);
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return AnalyticsCenterScheduleService.MigrationRequired();
            await AnalyticsCenterScheduleRepository.UpsertActivityAsync(
                connection,
                built.Seed.Actor.ActualUserId,
                reportCode,
                incrementView: true,
                favorite: null,
                context.RequestAborted);
            return Results.Ok(new { module = "030", status = "analytics_report_view_recorded", reportCode });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The recent-report activity could not be recorded.");
        }
    }

    private static async Task<IResult> SetFavoriteAsync(
        string reportCode,
        AnalyticsActivityRequest request,
        HttpContext context)
    {
        var built = await AnalyticsCenterExperienceScope.BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!AnalyticsCenterExperienceScope.CanView(built.Seed!.Actor))
            return AnalyticsCenterExperienceScope.AccessDenied();
        if (built.Seed.Actor.IsViewAs)
            return AnalyticsCenterExperienceScope.ViewAsReadOnly("changing a personal report favorite");
        if (EnterpriseReportingCatalog.Find(built.Seed.Reporting, reportCode) is null)
            return AnalyticsCenterExperienceScope.ReportNotFound(built.Seed.Reporting);
        try
        {
            await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(context.RequestAborted);
            if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return AnalyticsCenterScheduleService.MigrationRequired();
            await AnalyticsCenterScheduleRepository.UpsertActivityAsync(
                connection,
                built.Seed.Actor.ActualUserId,
                reportCode,
                incrementView: false,
                favorite: request.Favorite ?? true,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = request.Favorite == false ? "analytics_favorite_removed" : "analytics_favorite_saved",
                reportCode
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The report favorite could not be changed.");
        }
    }

    internal static async Task<AnalyticsScheduledReportOutcome> BuildScheduledReportAsync(
        IServiceProvider services,
        Guid scopeUserId,
        string reportCode,
        JsonElement criteria,
        CancellationToken cancellationToken)
    {
        var request = criteria.ValueKind == JsonValueKind.Object
            ? criteria.Deserialize<AnalyticsExperienceRequest>()
                ?? AnalyticsExperienceRequest.Empty(reportCode)
            : AnalyticsExperienceRequest.Empty(reportCode);
        request = request with { ReportCode = reportCode };
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = Stream.Null }
        };
        context.Items["ProjectPulseActualUserId"] = scopeUserId;
        context.Items["ProjectPulseEffectiveUserId"] = scopeUserId;
        context.Items["ProjectPulseSessionUserId"] = scopeUserId;
        context.Items["ProjectPulseIsViewAs"] = false;
        var built = await AnalyticsCenterExperienceScope.BuildForReportAsync(context, request);
        if (built.Failure is not null)
            throw new InvalidOperationException("The recipient no longer has access to the scheduled report.");
        if (!AnalyticsCenterExperienceScope.CanRun(built.Seed!.Actor))
            throw new UnauthorizedAccessException("The recipient does not have current authority to run the scheduled report.");
        var result = AnalyticsCenterExperienceScope.BuildResult(
            built.Seed,
            built.Definition!,
            request);
        await using var connection = await EnterpriseReportingRepository.OpenAsync(cancellationToken);
        if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, cancellationToken))
            throw new InvalidOperationException("Analytics run persistence is not ready.");
        var runId = await EnterpriseReportingRepository.SaveRunAsync(
            connection,
            built.Seed.Reporting,
            result,
            cancellationToken);
        return new(
            built.Seed.Actor,
            built.Seed.Reporting,
            built.Definition!,
            result,
            runId);
    }

    private static object ExportUrls(Guid runId) => new
    {
        pdf = $"/api/analytics/v2/runs/{runId}/export?format=pdf",
        xlsx = $"/api/analytics/v2/runs/{runId}/export?format=xlsx",
        csv = $"/api/analytics/v2/runs/{runId}/export?format=csv",
        json = $"/api/analytics/v2/runs/{runId}/export?format=json"
    };

    private static IResult Migration055Required() => Results.Json(new
    {
        module = "030",
        status = "migration_055_required",
        migration = EnterpriseReportingRepository.MigrationId,
        message = "Analytics preview is available. Immutable run and export evidence requires migration 055."
    }, statusCode: StatusCodes.Status409Conflict);

    private static IResult RepositoryFailure(HttpContext context, Exception exception, string message)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnalyticsCenterEnterpriseExperienceModule")
            .LogWarning(
                "Analytics enterprise operation failed ({ExceptionType}) correlation {CorrelationId}.",
                exception.GetType().Name,
                context.TraceIdentifier);
        return Results.Json(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_enterprise_source_unavailable",
            message,
            correlationId = context.TraceIdentifier,
            diagnosticCode = EnterpriseReportingSourceLoader.Diagnostic(exception),
            rawExceptionReturned = false
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static AnalyticsDashboardMetric Metric(
        string key,
        string label,
        decimal? value,
        string detail,
        string format,
        decimal? progress,
        string tone = "blue")
    {
        var display = value.HasValue
            ? format == "currency"
                ? value.Value.ToString("C1", CultureInfo.CurrentCulture)
                : format == "percent"
                    ? $"{value.Value:N1}%"
                    : value.Value.ToString("N1")
            : "Not available";
        return new(
            key,
            label,
            display,
            detail,
            tone,
            progress.HasValue ? Math.Clamp(progress.Value, 0m, 100m) : null,
            value.HasValue);
    }

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static decimal? Percentage(decimal numerator, decimal denominator) =>
        denominator > 0 ? Math.Clamp(numerator / denominator * 100m, 0m, 100m) : null;
}
