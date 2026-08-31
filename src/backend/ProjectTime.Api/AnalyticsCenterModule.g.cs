using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 030 Analytics Center. This is the user-facing reporting contract.
/// It consumes the existing governed reporting engine and immutable reporting
/// repository while adding customer-directory, project, engineer, PM, team,
/// and Module 055C/055D contract-type filter behavior.
/// </summary>
public static class AnalyticsCenterModule
{
    private const string ContractVersion = "030-analytics-center-v2-20260730";

    private static readonly string[] CanonicalContractTypes =
    [
        "Fixed Price",
        "Time and Material",
        "Pre-Sales",
        "Internal",
        "Non-billable",
        "Other"
    ];

    public static IEndpointRouteBuilder MapAnalyticsCenterEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSowGsdWorkspaceEndpoints();
        endpoints.MapGet(
            "/api/analytics/catalog",
            (Func<HttpContext, Task<IResult>>)GetCatalogAsync);
        endpoints.MapPost(
            "/api/analytics/filter-options",
            (Func<AnalyticsReportRequest, HttpContext, Task<IResult>>)GetFilterOptionsAsync);
        endpoints.MapPost(
            "/api/analytics/preview",
            (Func<AnalyticsReportRequest, HttpContext, Task<IResult>>)PreviewAsync);
        endpoints.MapPost(
            "/api/analytics/run",
            (Func<AnalyticsReportRequest, HttpContext, Task<IResult>>)RunAsync);
        endpoints.MapGet(
            "/api/analytics/history",
            (Func<HttpContext, Task<IResult>>)HistoryAsync);
        endpoints.MapGet(
            "/api/analytics/runs/{runId:guid}/export",
            (Guid runId, string? format) => Results.Redirect(
                $"/api/enterprise-reporting/runs/{runId}/export?format={Uri.EscapeDataString((format ?? "xlsx").Trim())}"));
        return endpoints;
    }

    private static async Task<IResult> GetCatalogAsync(HttpContext context)
    {
        var built = await BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!CanView(built.Context!.Actor)) return AccessDenied();

        var reports = EnterpriseReportingCatalog.ForContext(built.Context.Reporting)
            .Select(AdaptDefinition)
            .ToArray();

        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = reports.Length == 0 ? "analytics_catalog_empty_for_scope" : "analytics_catalog_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = Access(built.Context.Actor),
            categories = reports.Select(report => report.Category).Distinct().OrderBy(value => value),
            reportCount = reports.Length,
            reports,
            capabilities = Capabilities(built.Context.Actor),
            scope = new
            {
                visibleProjectCount = built.Context.Projects.Length,
                customerDirectoryCount = built.Context.Directory.Customers.Length,
                teamDirectoryCount = built.Context.Directory.Teams.Length,
                filterOptionsAreServerScoped = true,
                engineerReportsLockedToSelf = IsEngineerOnly(built.Context.Actor),
                projectManagerReportsLockedToOwnPortfolio = IsPmOnly(built.Context.Actor),
                financialFieldsRemainRoleAppropriate = true,
                organizationFilterPresent = false,
                fiscalPeriodFilterPresent = false
            },
            naming = new
            {
                current = "Analytics Center",
                compatibilityRoute = "reporting",
                compatibilityApi = "/api/enterprise-reporting"
            }
        });
    }

    private static async Task<IResult> GetFilterOptionsAsync(
        AnalyticsReportRequest request,
        HttpContext context)
    {
        var built = await BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!CanView(built.Context!.Actor)) return AccessDenied();

        var definition = EnterpriseReportingCatalog.Find(
            built.Context.Reporting,
            request.ReportCode);
        if (definition is null) return ReportNotFound(built.Context.Reporting);

        var scopedProjects = ScopeProjectsForOptions(
            built.Context.Projects,
            built.Context.Directory,
            request);
        var scopedTruth = built.Context.Reporting.Truth with { Projects = scopedProjects };
        var scopedReporting = new EnterpriseReportingContext(
            scopedTruth,
            built.Context.Reporting.Supplemental);
        var baseOptions = EnterpriseReportingEngine.BuildFilterOptions(
            scopedReporting,
            definition);
        var options = new Dictionary<string, EnterpriseReportOption[]>(
            baseOptions.Options,
            StringComparer.OrdinalIgnoreCase)
        {
            ["customers"] = CustomerOptions(built.Context.Directory, scopedProjects),
            ["projects"] = ProjectOptions(scopedProjects),
            ["projectManagers"] = ProjectManagerOptions(scopedProjects, baseOptions.LockedValues),
            ["engineers"] = EngineerOptions(scopedProjects, baseOptions.LockedValues),
            ["teams"] = TeamOptions(built.Context.Directory, scopedProjects),
            ["contractTypes"] = CanonicalContractTypes
                .Select(value => new EnterpriseReportOption(
                    value,
                    value,
                    false,
                    "Module 055C/055D contract type"))
                .ToArray()
        };

        var adapted = AdaptDefinition(definition);
        var effectiveFilters = adapted.Filters.Select(filter =>
        {
            var locked = baseOptions.LockedValues.TryGetValue(filter.Key, out var lockedValue);
            return filter with
            {
                Locked = locked,
                LockedReason = locked ? baseOptions.ScopeExplanation : null,
                DefaultValue = locked ? lockedValue : filter.DefaultValue
            };
        }).ToArray();

        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_filter_options_loaded",
            contractVersion = ContractVersion,
            definition = adapted with { Filters = effectiveFilters },
            options = new
            {
                options,
                lockedValues = baseOptions.LockedValues,
                scopeExplanation = baseOptions.ScopeExplanation,
                source = built.Context.Directory.Source,
                cascading = true
            },
            access = Access(built.Context.Actor)
        });
    }

    private static async Task<IResult> PreviewAsync(
        AnalyticsReportRequest request,
        HttpContext context)
    {
        var built = await BuildForReportAsync(context, request);
        if (built.Failure is not null) return built.Failure;
        if (!CanRun(built.Context!.Actor)) return AccessDenied("Run Analytics authority is required.");

        var result = BuildResult(built.Context, built.Definition!, request);
        return Results.Ok(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_preview_loaded",
            contractVersion = ContractVersion,
            previewOnly = true,
            persisted = false,
            definition = AdaptDefinition(built.Definition!),
            result,
            access = Access(built.Context.Actor)
        });
    }

    private static async Task<IResult> RunAsync(
        AnalyticsReportRequest request,
        HttpContext context)
    {
        var built = await BuildForReportAsync(context, request);
        if (built.Failure is not null) return built.Failure;
        if (!CanRun(built.Context!.Actor)) return AccessDenied("Run Analytics authority is required.");
        if (built.Context.Actor.IsViewAs) return ViewAsReadOnly();

        var result = BuildResult(built.Context, built.Definition!, request);
        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();

            var runId = await EnterpriseReportingRepository.SaveRunAsync(
                connection,
                built.Context.Reporting,
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
                definition = AdaptDefinition(built.Definition!),
                result,
                exportUrls = new
                {
                    csv = $"/api/analytics/runs/{runId}/export?format=csv",
                    xlsx = $"/api/analytics/runs/{runId}/export?format=xlsx",
                    json = $"/api/analytics/runs/{runId}/export?format=json"
                }
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(
                context,
                exception,
                "The analytics result was calculated, but its immutable run record could not be stored.");
        }
    }

    private static async Task<IResult> HistoryAsync(HttpContext context)
    {
        var built = await BuildSeedAsync(context);
        if (built.Failure is not null) return built.Failure;
        if (!CanView(built.Context!.Actor)) return AccessDenied();

        try
        {
            await using var connection = await EnterpriseReportingRepository.OpenAsync(context.RequestAborted);
            if (!await EnterpriseReportingRepository.MigrationReadyAsync(connection, context.RequestAborted))
                return MigrationRequired();

            var limit = int.TryParse(context.Request.Query["limit"], out var parsed) ? parsed : 100;
            var history = await EnterpriseReportingRepository.LoadHistoryAsync(
                connection,
                built.Context.Actor,
                limit,
                context.RequestAborted);

            return Results.Ok(new
            {
                module = "030",
                moduleName = "Analytics Center",
                status = history.Length == 0 ? "analytics_history_empty" : "analytics_history_loaded",
                count = history.Length,
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
                    exportUrls = new
                    {
                        csv = $"/api/analytics/runs/{run.RunId}/export?format=csv",
                        xlsx = $"/api/analytics/runs/{run.RunId}/export?format=xlsx",
                        json = $"/api/analytics/runs/{run.RunId}/export?format=json"
                    }
                })
            });
        }
        catch (Exception exception)
        {
            return RepositoryFailure(context, exception, "Analytics history is temporarily unavailable.");
        }
    }

    private static async Task<AnalyticsOutcome> BuildSeedAsync(HttpContext context)
    {
        var truth = await ProjectFinancialTruthModule.BuildFinancialOperationsTruthAsync(context);
        if (truth.Failure is not null) return new(null, null, truth.Failure);

        var supplemental = new EnterpriseReportingSupplemental(
            new Dictionary<string, JsonElement[]>(),
            Array.Empty<EnterpriseReportSourceState>());
        var reporting = new EnterpriseReportingContext(truth.Snapshot!, supplemental);
        var directory = await AnalyticsCenterDirectoryLoader.LoadAsync(
            truth.Snapshot!,
            context.RequestAborted);
        return new(new AnalyticsBuildContext(reporting, directory), null, null);
    }

    private static async Task<AnalyticsOutcome> BuildForReportAsync(
        HttpContext context,
        AnalyticsReportRequest request)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed;
        if (!CanView(seed.Context!.Actor)) return new(null, null, AccessDenied());

        var definition = EnterpriseReportingCatalog.Find(
            seed.Context.Reporting,
            request.ReportCode);
        if (definition is null)
            return new(seed.Context, null, ReportNotFound(seed.Context.Reporting));

        var projects = ApplyDirectoryFilters(
            seed.Context.Projects,
            seed.Context.Directory,
            request);
        var truth = seed.Context.Reporting.Truth with { Projects = projects };
        var reportingSeed = new EnterpriseReportingContext(
            truth,
            seed.Context.Reporting.Supplemental);
        var supplemental = await EnterpriseReportingSourceLoader.LoadAsync(
            reportingSeed,
            definition,
            context.RequestAborted);
        var reporting = new EnterpriseReportingContext(truth, supplemental);
        return new(
            new AnalyticsBuildContext(reporting, seed.Context.Directory),
            definition,
            null);
    }

    private static EnterpriseReportResult BuildResult(
        AnalyticsBuildContext context,
        EnterpriseReportDefinition definition,
        AnalyticsReportRequest request)
    {
        var enterpriseRequest = ToEnterpriseRequest(request);
        var result = EnterpriseReportingEngine.Build(
            context.Reporting,
            definition,
            enterpriseRequest);
        var filters = new Dictionary<string, object?>(
            result.EffectiveFilters,
            StringComparer.OrdinalIgnoreCase);

        if (request.CustomerId.HasValue)
        {
            var customer = context.Directory.Customers.FirstOrDefault(
                row => row.CustomerId == request.CustomerId.Value);
            filters["customerId"] = request.CustomerId.Value;
            filters["customer"] = customer?.CustomerName ?? "Selected customer";
        }
        if (request.TeamId.HasValue)
        {
            var team = context.Directory.Teams.FirstOrDefault(
                row => row.TeamId == request.TeamId.Value);
            filters["teamId"] = request.TeamId.Value;
            filters["team"] = team?.TeamName ?? "Selected team";
        }
        if (!string.IsNullOrWhiteSpace(request.ContractType))
            filters["contractType"] = CanonicalContractType(request.ContractType);

        var sources = result.Sources
            .Append(context.Directory.Source)
            .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(source => source.Required ? 0 : 1)
            .ThenBy(source => source.Name)
            .ToArray();

        var partial = result.ResultStatus == "complete"
            && sources.Any(source => source.Status is "partial" or "unavailable" or "restricted");
        return result with
        {
            EffectiveFilters = filters,
            Sources = sources,
            ResultStatus = partial ? "partial" : result.ResultStatus,
            Message = partial
                ? $"{result.RowCount} role-scoped row(s) loaded. A non-required directory source is degraded; report results remain usable."
                : result.Message
        };
    }

    private static EnterpriseReportRequest ToEnterpriseRequest(
        AnalyticsReportRequest request) => new(
            request.ReportCode,
            request.Search,
            request.ProjectId,
            request.CustomerId,
            null,
            request.ProjectManagerUserId,
            request.EngineerUserId,
            request.ProjectStatus,
            request.BudgetStatus,
            null,
            request.Billable,
            request.DateFrom,
            request.DateTo,
            request.WorkflowStatus,
            request.Severity,
            request.ModuleCode,
            request.SourceStatus,
            request.Limit,
            false);

    private static FinancialOperationsProject[] ScopeProjectsForOptions(
        FinancialOperationsProject[] projects,
        AnalyticsDirectorySnapshot directory,
        AnalyticsReportRequest request)
    {
        var scoped = projects.AsEnumerable();
        if (request.CustomerId.HasValue)
            scoped = scoped.Where(project => project.ClientId == request.CustomerId.Value);
        if (request.ProjectId.HasValue)
            scoped = scoped.Where(project => project.ProjectId == request.ProjectId.Value);
        if (request.TeamId.HasValue)
            scoped = FilterByTeam(scoped, directory, request.TeamId.Value);
        return scoped.ToArray();
    }

    private static FinancialOperationsProject[] ApplyDirectoryFilters(
        FinancialOperationsProject[] projects,
        AnalyticsDirectorySnapshot directory,
        AnalyticsReportRequest request)
    {
        var scoped = ScopeProjectsForOptions(projects, directory, request).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.ContractType))
        {
            var expected = CanonicalContractType(request.ContractType);
            scoped = scoped.Where(project => CanonicalContractType(project.ContractType)
                .Equals(expected, StringComparison.OrdinalIgnoreCase));
        }
        return scoped.ToArray();
    }

    private static IEnumerable<FinancialOperationsProject> FilterByTeam(
        IEnumerable<FinancialOperationsProject> projects,
        AnalyticsDirectorySnapshot directory,
        Guid teamId)
    {
        var memberIds = directory.Teams
            .FirstOrDefault(team => team.TeamId == teamId)?
            .MemberUserIds
            .ToHashSet() ?? [];
        if (memberIds.Count == 0) return Array.Empty<FinancialOperationsProject>();
        return projects.Where(project => ProjectUserIds(project).Any(memberIds.Contains));
    }

    private static IEnumerable<Guid> ProjectUserIds(FinancialOperationsProject project) =>
        project.Engineers.Select(engineer => engineer.UserId)
            .Concat(project.ProjectManagerUserId.HasValue
                ? [project.ProjectManagerUserId.Value]
                : Array.Empty<Guid>())
            .Concat(project.ProjectTeamCoordinator is null
                ? Array.Empty<Guid>()
                : [project.ProjectTeamCoordinator.UserId])
            .Concat(project.SolutionArchitect is null
                ? Array.Empty<Guid>()
                : [project.SolutionArchitect.UserId])
            .Concat(project.AccountExecutive is null
                ? Array.Empty<Guid>()
                : [project.AccountExecutive.UserId]);

    private static EnterpriseReportDefinition AdaptDefinition(
        EnterpriseReportDefinition definition)
    {
        var filters = definition.Filters
            .Where(filter => filter.Key is not ("fiscalPeriod" or "organization" or "cadence"))
            .Select(filter => filter.Key == "customer"
                ? filter with
                {
                    Key = "customerId",
                    Label = "Customer",
                    OptionSource = "customers",
                    Placeholder = null
                }
                : filter)
            .ToList();

        var hasPeopleOrProjectFilter = filters.Any(filter => filter.Key is
            "projectId" or "projectManagerUserId" or "engineerUserId" or "customerId");
        if (hasPeopleOrProjectFilter && filters.All(filter => filter.Key != "teamId"))
        {
            var insertAfter = filters.FindLastIndex(filter => filter.Key is
                "projectManagerUserId" or "engineerUserId" or "projectId" or "customerId");
            filters.Insert(
                Math.Max(0, insertAfter + 1),
                new EnterpriseReportFilterDefinition(
                    "teamId",
                    "Team",
                    "select",
                    false,
                    false,
                    null,
                    null,
                    "teams",
                    null));
        }

        return definition with { Filters = filters.ToArray() };
    }

    private static EnterpriseReportOption[] CustomerOptions(
        AnalyticsDirectorySnapshot directory,
        FinancialOperationsProject[] projects)
    {
        var visibleClientIds = projects
            .Where(project => project.ClientId.HasValue)
            .Select(project => project.ClientId!.Value)
            .ToHashSet();
        return directory.Customers
            .Where(customer => visibleClientIds.Count == 0 || visibleClientIds.Contains(customer.CustomerId))
            .Select(customer => new EnterpriseReportOption(
                customer.CustomerId.ToString(),
                customer.CustomerName,
                false,
                string.IsNullOrWhiteSpace(customer.CustomerCode)
                    ? "Customer Directory"
                    : $"Customer Directory · {customer.CustomerCode}"))
            .OrderBy(option => option.Label)
            .ToArray();
    }

    private static EnterpriseReportOption[] ProjectOptions(
        FinancialOperationsProject[] projects) => projects
        .OrderBy(project => project.CustomerName)
        .ThenBy(project => project.ProjectName)
        .Select(project => new EnterpriseReportOption(
            project.ProjectId.ToString(),
            $"{project.ProjectCode} · {project.ProjectName}",
            false,
            project.CustomerName))
        .ToArray();

    private static EnterpriseReportOption[] ProjectManagerOptions(
        FinancialOperationsProject[] projects,
        Dictionary<string, object?> lockedValues) => projects
        .Where(project => project.ProjectManagerUserId.HasValue)
        .GroupBy(project => project.ProjectManagerUserId!.Value)
        .Select(group => new EnterpriseReportOption(
            group.Key.ToString(),
            group.First().ProjectManagerName,
            lockedValues.TryGetValue("projectManagerUserId", out var value)
                && value is Guid lockedId
                && lockedId != group.Key,
            group.First().ProjectManagerEmail))
        .OrderBy(option => option.Label)
        .ToArray();

    private static EnterpriseReportOption[] EngineerOptions(
        FinancialOperationsProject[] projects,
        Dictionary<string, object?> lockedValues) => projects
        .SelectMany(project => project.Engineers)
        .GroupBy(engineer => engineer.UserId)
        .Select(group => new EnterpriseReportOption(
            group.Key.ToString(),
            group.First().DisplayName,
            lockedValues.TryGetValue("engineerUserId", out var value)
                && value is Guid lockedId
                && lockedId != group.Key,
            group.First().Email))
        .OrderBy(option => option.Label)
        .ToArray();

    private static EnterpriseReportOption[] TeamOptions(
        AnalyticsDirectorySnapshot directory,
        FinancialOperationsProject[] projects)
    {
        var visibleUsers = projects.SelectMany(ProjectUserIds).ToHashSet();
        return directory.Teams
            .Where(team => team.MemberUserIds.Any(visibleUsers.Contains))
            .Select(team => new EnterpriseReportOption(
                team.TeamId.ToString(),
                team.TeamName,
                false,
                $"{team.MemberUserIds.Count(visibleUsers.Contains)} visible member(s)"))
            .OrderBy(option => option.Label)
            .ToArray();
    }

    private static string CanonicalContractType(string? value)
    {
        var original = (value ?? string.Empty).Trim();
        var normalized = new string(original.ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return normalized switch
        {
            "tm" or "timematerial" or "timematerials" or "timeandmaterial" or "timeandmaterials" => "Time and Material",
            "fp" or "fixedprice" or "fixedfee" => "Fixed Price",
            "presales" or "presale" => "Pre-Sales",
            "internal" => "Internal",
            "nonbillable" => "Non-billable",
            "other" => "Other",
            _ => original
        };
    }

    private static bool CanView(FinancialOperationsActor actor) => actor.Broad
        || actor.HasPermission(
            "VIEW_ENTERPRISE_REPORTING",
            "VIEW_FINANCIAL_REPORT_CENTER",
            "VIEW_REPORTS",
            "VIEW_EXECUTIVE_REPORTING",
            "MANAGE_ALL",
            "SYSTEM_ADMINISTRATION")
        || actor.HasRole(
            "ENGINEER", "ENGINEERING", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
            "MANAGER", "ENGINEERING_MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
            "PROJECT_MANAGEMENT_LEAD", "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE",
            "BILLING", "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT");

    private static bool CanRun(FinancialOperationsActor actor) => CanView(actor)
        && (actor.Broad
            || actor.HasPermission(
                "RUN_ENTERPRISE_REPORTING",
                "RUN_FINANCIAL_REPORTS",
                "MANAGE_REPORTS",
                "MANAGE_ALL")
            || actor.HasRole(
                "ENGINEER", "ENGINEERING", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
                "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING",
                "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"));

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
        return !actor.Broad
            && (actor.PmLead || roles.Overlaps(["PROJECT_MANAGER", "PROJECT_MANAGEMENT"]));
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
        canPreview = CanRun(actor),
        canRun = CanRun(actor) && !actor.IsViewAs,
        canExport = CanRun(actor) && !actor.IsViewAs,
        dynamicFilters = true,
        populatedCustomerProjectEngineerPmTeamFilters = true,
        contractTypesAlignedToModules055C055D = true
    };

    private static IResult AccessDenied(
        string message = "Analytics Center access is required.") => Results.Json(new
    {
        module = "030",
        moduleName = "Analytics Center",
        status = "analytics_access_required",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsReadOnly() => Results.Json(new
    {
        module = "030",
        moduleName = "Analytics Center",
        status = "view_as_read_only",
        message = "Exit View-As before recording or exporting an analytics run. Preview remains available."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ReportNotFound(EnterpriseReportingContext context) => Results.BadRequest(new
    {
        module = "030",
        moduleName = "Analytics Center",
        status = "analytics_report_not_available",
        message = "Select an Analytics Center report available to the current role scope.",
        availableReports = EnterpriseReportingCatalog.ForContext(context).Select(report => report.Code)
    });

    private static IResult MigrationRequired() => Results.Json(new
    {
        module = "030",
        moduleName = "Analytics Center",
        status = "migration_055_required",
        migration = EnterpriseReportingRepository.MigrationId,
        message = "Analytics preview is available. Immutable run history and export evidence require migration 055."
    }, statusCode: StatusCodes.Status409Conflict);

    private static IResult RepositoryFailure(
        HttpContext context,
        Exception exception,
        string message)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AnalyticsCenterModule")
            .LogWarning(
                "Analytics Center repository operation failed ({ExceptionType}) correlation {CorrelationId}.",
                exception.GetType().Name,
                context.TraceIdentifier);
        return Results.Json(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_repository_unavailable",
            message,
            correlationId = context.TraceIdentifier,
            diagnosticCode = EnterpriseReportingSourceLoader.Diagnostic(exception),
            rawExceptionReturned = false
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private sealed record AnalyticsOutcome(
        AnalyticsBuildContext? Context,
        EnterpriseReportDefinition? Definition,
        IResult? Failure);
}
