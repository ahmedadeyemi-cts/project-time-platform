using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 030 enterprise reporting. Report availability, filter options, rows,
/// exports, and history are all generated from server-scoped sources. Report
/// permissions never widen project, person, customer, or financial-field scope.
/// </summary>
public static class EnterpriseReportingModule
{
    private const string ContractVersion = "030-enterprise-reporting-v1-20260730";

    public static IEndpointRouteBuilder MapEnterpriseReportingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/enterprise-reporting/catalog",
            (Func<HttpContext, Task<IResult>>)GetCatalogAsync);
        endpoints.MapPost(
            "/api/enterprise-reporting/filter-options",
            (Func<EnterpriseReportRequest, HttpContext, Task<IResult>>)GetFilterOptionsAsync);
        endpoints.MapPost(
            "/api/enterprise-reporting/preview",
            (Func<EnterpriseReportRequest, HttpContext, Task<IResult>>)PreviewAsync);
        endpoints.MapPost(
            "/api/enterprise-reporting/run",
            (Func<EnterpriseReportRequest, HttpContext, Task<IResult>>)RunAsync);
        endpoints.MapGet(
            "/api/enterprise-reporting/history",
            (Func<HttpContext, Task<IResult>>)HistoryAsync);
        endpoints.MapGet(
            "/api/enterprise-reporting/runs/{runId:guid}/export",
            (Func<Guid, string?, HttpContext, Task<IResult>>)ExportAsync);
        endpoints.MapGet(
            "/api/enterprise-reporting/saved-views",
            (Func<HttpContext, Task<IResult>>)GetSavedViewsAsync);
        endpoints.MapPost(
            "/api/enterprise-reporting/saved-views",
            (Func<EnterpriseSavedViewRequest, HttpContext, Task<IResult>>)SaveViewAsync);
        endpoints.MapDelete(
            "/api/enterprise-reporting/saved-views/{savedViewId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)DeleteViewAsync);
        return endpoints;
    }

    private static async Task<IResult> GetCatalogAsync(HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!CanView(seed.Context!.Actor)) return AccessDenied("Enterprise reporting access is required.");
        var reports = EnterpriseReportingCatalog.ForContext(seed.Context);
        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center Center",
            status = reports.Length == 0 ? "report_catalog_empty_for_scope" : "report_catalog_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = Access(seed.Context.Actor),
            categories = reports.Select(report => report.Category).Distinct().OrderBy(value => value),
            reportCount = reports.Length,
            reports,
            capabilities = Capabilities(seed.Context.Actor),
            scope = new
            {
                visibleProjectCount = seed.Context.Projects.Length,
                filterOptionsAreServerScoped = true,
                engineerReportsLockedToSelf = IsEngineerOnly(seed.Context.Actor),
                projectManagerReportsLockedToOwnPortfolio = IsPmOnly(seed.Context.Actor),
                financialFieldsRemainRoleAppropriate = true
            }
        });
    }

    private static async Task<IResult> GetFilterOptionsAsync(
        EnterpriseReportRequest request,
        HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!CanView(seed.Context!.Actor)) return AccessDenied("Enterprise reporting access is required.");
        var definition = EnterpriseReportingCatalog.Find(seed.Context, request.ReportCode);
        if (definition is null) return ReportNotFound(seed.Context);
        var options = EnterpriseReportingEngine.BuildFilterOptions(seed.Context, definition);
        var effectiveFilters = definition.Filters.Select(filter =>
        {
            var locked = options.LockedValues.TryGetValue(filter.Key, out var lockedValue);
            return filter with
            {
                Locked = locked,
                LockedReason = locked ? options.ScopeExplanation : null,
                DefaultValue = locked ? lockedValue : filter.DefaultValue
            };
        }).ToArray();
        return Results.Ok(new
        {
            module = "030",
            status = "report_filter_options_loaded",
            contractVersion = ContractVersion,
            definition = definition with { Filters = effectiveFilters },
            options,
            access = Access(seed.Context.Actor)
        });
    }

    private static async Task<IResult> PreviewAsync(
        EnterpriseReportRequest request,
        HttpContext context)
    {
        var built = await BuildForReportAsync(context, request.ReportCode);
        if (built.Failure is not null) return built.Failure;
        if (!CanRun(built.Context!.Actor)) return AccessDenied("Run Enterprise Reports authority is required.");
        var result = EnterpriseReportingEngine.Build(built.Context, built.Definition!, request);
        return Results.Ok(new
        {
            module = "030",
            status = "enterprise_report_preview_loaded",
            contractVersion = ContractVersion,
            previewOnly = true,
            persisted = false,
            definition = built.Definition,
            result,
            access = Access(built.Context.Actor)
        });
    }

    private static async Task<IResult> RunAsync(
        EnterpriseReportRequest request,
        HttpContext context)
    {
        var built = await BuildForReportAsync(context, request.ReportCode);
        if (built.Failure is not null) return built.Failure;
        var actor = built.Context!.Actor;
        if (!CanRun(actor)) return AccessDenied("Run Enterprise Reports authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before recording a report run.");
        var result = EnterpriseReportingEngine.Build(built.Context, built.Definition!, request);
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var runId = await EnterpriseReportingRepository.SaveRunAsync(
                connection, built.Context, result, context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = "enterprise_report_run_completed",
                contractVersion = ContractVersion,
                persisted = true,
                runId,
                definition = built.Definition,
                result,
                exportUrls = new
                {
                    csv = $"/api/enterprise-reporting/runs/{runId}/export?format=csv",
                    xlsx = $"/api/enterprise-reporting/runs/{runId}/export?format=xlsx",
                    json = $"/api/enterprise-reporting/runs/{runId}/export?format=json"
                }
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The report was calculated, but its immutable run record could not be stored.");
        }
    }

    private static async Task<IResult> HistoryAsync(HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!CanView(seed.Context!.Actor)) return AccessDenied("Enterprise reporting access is required.");
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var limit = int.TryParse(context.Request.Query["limit"], out var parsed) ? parsed : 50;
            var history = await EnterpriseReportingRepository.LoadHistoryAsync(
                connection, seed.Context.Actor, limit, context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = history.Length == 0 ? "enterprise_report_history_empty" : "enterprise_report_history_loaded",
                count = history.Length,
                history = history.Select(run => new
                {
                    run.RunId, run.ReportCode, run.ReportName, run.ResultStatus,
                    run.RowCount, run.Filters, run.Sources, run.StartedAt,
                    run.CompletedAt, run.CreatedAt,
                    exportUrls = new
                    {
                        csv = $"/api/enterprise-reporting/runs/{run.RunId}/export?format=csv",
                        xlsx = $"/api/enterprise-reporting/runs/{run.RunId}/export?format=xlsx",
                        json = $"/api/enterprise-reporting/runs/{run.RunId}/export?format=json"
                    }
                })
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "Enterprise report history is temporarily unavailable.");
        }
    }

    private static async Task<IResult> ExportAsync(
        Guid runId,
        string? format,
        HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Context!.Actor;
        if (!CanExport(actor)) return AccessDenied("Export Enterprise Reports authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before creating an export record.");
        var normalizedFormat = (format ?? "xlsx").Trim().ToLowerInvariant();
        if (normalizedFormat is not ("csv" or "xlsx" or "json"))
            return Results.BadRequest(new
            {
                module = "030",
                status = "unsupported_export_format",
                supported = new[] { "csv", "xlsx", "json" }
            });
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var run = await EnterpriseReportingRepository.LoadRunAsync(
                connection, actor, runId, context.RequestAborted);
            if (run is null)
                return Results.NotFound(new
                {
                    module = "030",
                    status = "report_run_not_found_or_outside_scope",
                    message = "The report run was not found in the current user's scope."
                });

            var export = normalizedFormat switch
            {
                "csv" => ExportPayload.Csv(BuildCsv(run), $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}.csv"),
                "json" => ExportPayload.Json(BuildJson(run), $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}.json"),
                _ => ExportPayload.Excel(BuildExcel(run), $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}.xlsx")
            };
            await EnterpriseReportingRepository.RecordExportAsync(
                connection, runId, actor, normalizedFormat, run.RowCount,
                export.Content, context.RequestAborted);
            return Results.File(export.Content, export.ContentType, export.FileName);
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The persisted report could not be exported.");
        }
    }

    private static async Task<IResult> GetSavedViewsAsync(HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        if (!CanView(seed.Context!.Actor)) return AccessDenied("Enterprise reporting access is required.");
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var views = await EnterpriseReportingRepository.LoadSavedViewsAsync(
                connection, seed.Context.Actor, context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = views.Length == 0 ? "saved_report_views_empty" : "saved_report_views_loaded",
                count = views.Length,
                views
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "Saved report views are temporarily unavailable.");
        }
    }

    private static async Task<IResult> SaveViewAsync(
        EnterpriseSavedViewRequest request,
        HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Context!.Actor;
        if (!CanRun(actor)) return AccessDenied("Run Enterprise Reports authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before saving a report view.");
        if (EnterpriseReportingCatalog.Find(seed.Context, request.ReportCode) is null)
            return ReportNotFound(seed.Context);
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var id = await EnterpriseReportingRepository.SaveViewAsync(
                connection, actor, request, context.RequestAborted);
            return Results.Ok(new
            {
                module = "030",
                status = "saved_report_view_stored",
                savedViewId = id,
                message = "The report view was saved for the effective user."
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { module = "030", status = "saved_view_invalid", message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Json(new { module = "030", status = "saved_view_scope_required", message = exception.Message }, statusCode: 403);
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The report view could not be saved.");
        }
    }

    private static async Task<IResult> DeleteViewAsync(Guid savedViewId, HttpContext context)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed.Failure;
        var actor = seed.Context!.Actor;
        if (!CanRun(actor)) return AccessDenied("Run Enterprise Reports authority is required.");
        if (actor.IsViewAs) return ViewAsReadOnly("Exit View-As before deleting a report view.");
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();
            var deleted = await EnterpriseReportingRepository.DeleteSavedViewAsync(
                connection, actor, savedViewId, context.RequestAborted);
            return deleted
                ? Results.Ok(new { module = "030", status = "saved_report_view_deleted", savedViewId })
                : Results.NotFound(new { module = "030", status = "saved_report_view_not_found" });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "The report view could not be deleted.");
        }
    }

    private static async Task<BuildOutcome> BuildSeedAsync(HttpContext context)
    {
        var truth = await ProjectFinancialTruthModule.BuildFinancialOperationsTruthAsync(context);
        if (truth.Failure is not null) return new(null, null, truth.Failure);
        var supplemental = new EnterpriseReportingSupplemental(
            new Dictionary<string, JsonElement[]>(), Array.Empty<EnterpriseReportSourceState>());
        return new(new EnterpriseReportingContext(truth.Snapshot!, supplemental), null, null);
    }

    private static async Task<BuildOutcome> BuildForReportAsync(
        HttpContext context,
        string? reportCode)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed;
        if (!CanView(seed.Context!.Actor)) return new(null, null, AccessDenied("Enterprise reporting access is required."));
        var definition = EnterpriseReportingCatalog.Find(seed.Context, reportCode);
        if (definition is null) return new(seed.Context, null, ReportNotFound(seed.Context));
        var supplemental = await EnterpriseReportingSourceLoader.LoadAsync(
            seed.Context, definition, context.RequestAborted);
        return new(new EnterpriseReportingContext(seed.Context.Truth, supplemental), definition, null);
    }

    private static bool CanView(FinancialOperationsActor actor) => actor.Broad
        || actor.HasPermission(
            "VIEW_ENTERPRISE_REPORTING", "VIEW_FINANCIAL_REPORT_CENTER", "VIEW_REPORTS",
            "VIEW_EXECUTIVE_REPORTING", "MANAGE_ALL", "SYSTEM_ADMINISTRATION")
        || actor.HasRole(
            "ENGINEER", "ENGINEERING", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
            "MANAGER", "ENGINEERING_MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
            "PROJECT_MANAGEMENT_LEAD", "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE",
            "BILLING", "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT");

    private static bool CanRun(FinancialOperationsActor actor) => CanView(actor)
        && (actor.Broad
            || actor.HasPermission("RUN_ENTERPRISE_REPORTING", "RUN_FINANCIAL_REPORTS", "MANAGE_REPORTS", "MANAGE_ALL")
            || actor.HasRole(
                "ENGINEER", "ENGINEERING", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
                "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING",
                "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"));

    private static bool CanExport(FinancialOperationsActor actor) => CanRun(actor)
        && (actor.Broad
            || actor.HasPermission(
                "EXPORT_ENTERPRISE_REPORTING", "EXPORT_FINANCIAL_REPORTS",
                "EXPORT_TIME_EXCEL", "EXPORT_TIME_PDF", "MANAGE_ALL")
            || actor.HasRole(
                "ENGINEER", "ENGINEERING", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
                "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING"));

    private static bool IsEngineerOnly(FinancialOperationsActor actor)
    {
        var roles = actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles.Overlaps(["ENGINEER", "ENGINEERING"])
            && !actor.PmLead
            && !roles.Overlaps(["MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD"]);
    }

    private static bool IsPmOnly(FinancialOperationsActor actor)
    {
        var roles = actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return !actor.Broad && (actor.PmLead || roles.Overlaps(["PROJECT_MANAGER", "PROJECT_MANAGEMENT"]));
    }

    private static object Access(FinancialOperationsActor actor) => new
    {
        actor.ActualUserId,
        actor.EffectiveUserId,
        actor.Email,
        actor.DisplayName,
        actor.Roles,
        actor.IsViewAs,
        readOnly = actor.IsViewAs,
        actor.Broad,
        engineerSelfScope = IsEngineerOnly(actor),
        projectManagerOwnPortfolio = IsPmOnly(actor),
        serverAuthorized = true
    };

    private static object Capabilities(FinancialOperationsActor actor) => new
    {
        canView = CanView(actor),
        canRun = CanRun(actor) && !actor.IsViewAs,
        canExport = CanExport(actor) && !actor.IsViewAs,
        canSaveViews = CanRun(actor) && !actor.IsViewAs,
        canManageCatalog = actor.Broad || actor.HasPermission("MANAGE_ENTERPRISE_REPORTING", "MANAGE_ALL")
    };

    private static IResult ReportNotFound(EnterpriseReportingContext context) => Results.BadRequest(new
    {
        module = "030",
        status = "enterprise_report_not_available",
        message = "Select a report available to the current role scope.",
        availableReports = EnterpriseReportingCatalog.ForContext(context).Select(report => report.Code)
    });

    private static IResult AccessDenied(string message) => Results.Json(new
    {
        module = "030",
        status = "enterprise_reporting_access_required",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsReadOnly(string message) => Results.Json(new
    {
        module = "030",
        status = "view_as_read_only",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult MigrationRequired() => Results.Json(new
    {
        module = "030",
        status = "migration_055_required",
        migration = EnterpriseReportingRepository.MigrationId,
        message = "The Analytics Center source is installed, but immutable run history and saved-view persistence require migration 055. Preview remains available."
    }, statusCode: StatusCodes.Status409Conflict);

    private static IResult RepositoryFailure(HttpContext context, Exception exception, string message)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("EnterpriseReportingModule")
            .LogWarning("Enterprise reporting repository operation failed ({ExceptionType}) correlation {CorrelationId}.",
                exception.GetType().Name, context.TraceIdentifier);
        return Results.Json(new
        {
            module = "030",
            status = "enterprise_reporting_repository_unavailable",
            message,
            correlationId = context.TraceIdentifier,
            diagnosticCode = EnterpriseReportingSourceLoader.Diagnostic(exception),
            rawExceptionReturned = false
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static byte[] BuildCsv(EnterpriseReportRunRecord run)
    {
        var columns = Columns(run.Columns);
        var rows = Rows(run.Results);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', columns.Select(column => Csv(column.Label))));
        foreach (var row in rows)
            builder.AppendLine(string.Join(',', columns.Select(column => Csv(Value(row, column.Key)))));
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildJson(EnterpriseReportRunRecord run) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        run.RunId,
        run.ReportCode,
        run.ReportName,
        run.ResultStatus,
        run.RowCount,
        run.ScopeSnapshot,
        run.Filters,
        columns = run.Columns,
        sources = run.Sources,
        results = run.Results,
        run.StartedAt,
        run.CompletedAt
    }, new JsonSerializerOptions { WriteIndented = true });

    private static byte[] BuildExcel(EnterpriseReportRunRecord run)
    {
        var columns = Columns(run.Columns);
        var rows = Rows(run.Results);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        sheet.Cell(1, 1).Value = "US Signal";
        sheet.Cell(1, 2).Value = run.ReportName;
        sheet.Cell(2, 1).Value = "Run ID";
        sheet.Cell(2, 2).Value = run.RunId.ToString();
        sheet.Cell(3, 1).Value = "Generated";
        sheet.Cell(3, 2).Value = run.CompletedAt.UtcDateTime;
        sheet.Cell(4, 1).Value = "Result status";
        sheet.Cell(4, 2).Value = run.ResultStatus;
        var headerRow = 6;
        for (var index = 0; index < columns.Length; index++)
        {
            sheet.Cell(headerRow, index + 1).Value = columns[index].Label;
            sheet.Cell(headerRow, index + 1).Style.Font.Bold = true;
            sheet.Cell(headerRow, index + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2F52");
            sheet.Cell(headerRow, index + 1).Style.Font.FontColor = XLColor.White;
        }
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                var text = Value(rows[rowIndex], columns[columnIndex].Key);
                sheet.Cell(headerRow + rowIndex + 1, columnIndex + 1).Value = text;
            }
        }
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents(8, 48);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static ExportColumn[] Columns(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return Array.Empty<ExportColumn>();
        return element.EnumerateArray().Select(item => new ExportColumn(
            Property(item, "Key", "key") ?? string.Empty,
            Property(item, "Label", "label") ?? string.Empty)).Where(column => column.Key.Length > 0).ToArray();
    }

    private static JsonElement[] Rows(JsonElement element) => element.ValueKind == JsonValueKind.Array
        ? element.EnumerateArray().Select(item => item.Clone()).ToArray()
        : Array.Empty<JsonElement>();

    private static string Value(JsonElement row, string key)
    {
        if (row.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in row.EnumerateObject())
        {
            if (!property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(", ", property.Value.EnumerateArray().Select(item => item.ToString())),
                _ => property.Value.ToString()
            };
        }
        return string.Empty;
    }

    private static string? Property(JsonElement row, params string[] names)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in row.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) return property.Value.ToString();
        return null;
    }

    private static string Csv(string value)
    {
        var safe = value.Replace('\0', ' ');
        return safe.Contains(',') || safe.Contains('"') || safe.Contains('\n')
            ? $"\"{safe.Replace("\"", "\"\"")}\""
            : safe;
    }

    private static string Safe(string value)
    {
        var safe = new string((value ?? "enterprise-report").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        return safe.Length == 0 ? "enterprise-report" : safe[..Math.Min(80, safe.Length)];
    }

    private sealed record BuildOutcome(
        EnterpriseReportingContext? Context,
        EnterpriseReportDefinition? Definition,
        IResult? Failure);

    private sealed record ExportColumn(string Key, string Label);

    private sealed record ExportPayload(byte[] Content, string ContentType, string FileName)
    {
        internal static ExportPayload Csv(byte[] content, string name) => new(content, "text/csv; charset=utf-8", name);
        internal static ExportPayload Json(byte[] content, string name) => new(content, "application/json", name);
        internal static ExportPayload Excel(byte[] content, string name) => new(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }
}
