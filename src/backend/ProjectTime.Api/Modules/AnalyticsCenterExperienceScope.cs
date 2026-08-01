namespace ProjectTime.Api.Modules;

internal sealed record AnalyticsExperienceSeed(
    EnterpriseReportingContext Reporting,
    AnalyticsDirectorySnapshot Directory)
{
    internal FinancialOperationsActor Actor => Reporting.Actor;
    internal FinancialOperationsProject[] Projects => Reporting.Projects;
}

internal sealed record AnalyticsExperienceBuild(
    AnalyticsExperienceSeed? Seed,
    EnterpriseReportDefinition? Definition,
    IResult? Failure);

internal static class AnalyticsCenterExperienceScope
{
    internal static readonly string[] CanonicalContractTypes =
    [
        "Fixed Price",
        "Time and Material",
        "Pre-Sales",
        "Internal",
        "Non-billable",
        "Other"
    ];

    internal static async Task<AnalyticsExperienceBuild> BuildSeedAsync(HttpContext context)
    {
        var truth = await ProjectFinancialTruthModule.BuildFinancialOperationsTruthAsync(context);
        if (truth.Failure is not null) return new(null, null, truth.Failure);
        var reporting = new EnterpriseReportingContext(
            truth.Snapshot!,
            new EnterpriseReportingSupplemental(
                new Dictionary<string, System.Text.Json.JsonElement[]>(),
                Array.Empty<EnterpriseReportSourceState>()));
        var directory = await AnalyticsCenterDirectoryLoader.LoadAsync(
            truth.Snapshot!,
            context.RequestAborted);
        return new(new AnalyticsExperienceSeed(reporting, directory), null, null);
    }

    internal static async Task<AnalyticsExperienceBuild> BuildForReportAsync(
        HttpContext context,
        AnalyticsExperienceRequest request)
    {
        var seed = await BuildSeedAsync(context);
        if (seed.Failure is not null) return seed;
        if (!CanView(seed.Seed!.Actor)) return new(null, null, AccessDenied());
        var definition = EnterpriseReportingCatalog.Find(
            seed.Seed.Reporting,
            request.ReportCode);
        if (definition is null)
            return new(seed.Seed, null, ReportNotFound(seed.Seed.Reporting));

        var normalized = NormalizeSelections(seed.Seed, request);
        var projects = ApplyProjectFilters(seed.Seed.Projects, seed.Seed.Directory, normalized);
        var truth = seed.Seed.Reporting.Truth with { Projects = projects };
        var reportingSeed = new EnterpriseReportingContext(
            truth,
            seed.Seed.Reporting.Supplemental);
        var supplemental = await EnterpriseReportingSourceLoader.LoadAsync(
            reportingSeed,
            definition,
            context.RequestAborted);
        return new(
            new AnalyticsExperienceSeed(
                new EnterpriseReportingContext(truth, supplemental),
                seed.Seed.Directory),
            definition,
            null);
    }

    internal static EnterpriseReportDefinition[] Catalog(AnalyticsExperienceSeed seed) =>
        EnterpriseReportingCatalog.ForContext(seed.Reporting)
            .Select(AdaptDefinition)
            .ToArray();

    internal static object FilterOptions(
        AnalyticsExperienceSeed seed,
        EnterpriseReportDefinition definition,
        AnalyticsExperienceRequest request)
    {
        var normalized = NormalizeSelections(seed, request);
        var baseOptions = EnterpriseReportingEngine.BuildFilterOptions(
            seed.Reporting,
            definition);
        var customerProjects = ApplyProjectFilters(seed.Projects, seed.Directory, normalized with
        {
            CustomerIds = Array.Empty<Guid>(),
            CustomerId = null
        });
        var projectProjects = ApplyProjectFilters(seed.Projects, seed.Directory, normalized with
        {
            ProjectIds = Array.Empty<Guid>(),
            ProjectId = null
        });
        var pmProjects = ApplyProjectFilters(seed.Projects, seed.Directory, normalized with
        {
            ProjectManagerUserIds = Array.Empty<Guid>(),
            ProjectManagerUserId = null
        });
        var engineerProjects = ApplyProjectFilters(seed.Projects, seed.Directory, normalized with
        {
            EngineerUserIds = Array.Empty<Guid>(),
            EngineerUserId = null
        });
        var teamProjects = ApplyProjectFilters(seed.Projects, seed.Directory, normalized with
        {
            TeamIds = Array.Empty<Guid>(),
            TeamId = null
        });

        var options = new Dictionary<string, EnterpriseReportOption[]>(
            baseOptions.Options,
            StringComparer.OrdinalIgnoreCase)
        {
            ["customers"] = CustomerOptions(seed.Directory, customerProjects),
            ["projects"] = ProjectOptions(projectProjects),
            ["projectManagers"] = ProjectManagerOptions(pmProjects, normalized.ProjectManagerUserIds ?? []),
            ["engineers"] = EngineerOptions(engineerProjects, normalized.EngineerUserIds ?? []),
            ["teams"] = TeamOptions(seed.Directory, teamProjects),
            ["contractTypes"] = CanonicalContractTypes.Select(value =>
                new EnterpriseReportOption(value, value, false, "Modules 055C/055D contract type"))
                .ToArray()
        };

        var adapted = AdaptDefinition(definition);
        var lockedValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (IsEngineerOnly(seed.Actor))
            lockedValues["engineerUserIds"] = new[] { seed.Actor.EffectiveUserId };
        if (IsPmOnly(seed.Actor))
            lockedValues["projectManagerUserIds"] = new[] { seed.Actor.EffectiveUserId };
        var filters = adapted.Filters.Select(filter =>
        {
            var locked = lockedValues.TryGetValue(filter.Key, out var value);
            return filter with
            {
                Locked = locked,
                LockedReason = locked ? ScopeExplanation(seed.Actor) : null,
                DefaultValue = locked ? value : filter.DefaultValue
            };
        }).ToArray();

        return new
        {
            definition = adapted with { Filters = filters },
            options = new
            {
                options,
                lockedValues,
                scopeExplanation = ScopeExplanation(seed.Actor),
                source = seed.Directory.Source,
                cascading = true,
                multipleSelection = true
            },
            access = Access(seed.Actor)
        };
    }

    internal static EnterpriseReportResult BuildResult(
        AnalyticsExperienceSeed seed,
        EnterpriseReportDefinition definition,
        AnalyticsExperienceRequest request)
    {
        var normalized = NormalizeSelections(seed, request);
        var enterpriseRequest = ToEnterpriseRequest(normalized);
        var result = EnterpriseReportingEngine.Build(
            seed.Reporting,
            definition,
            enterpriseRequest);
        var rows = ApplyRowFilters(result.Rows, seed, definition, normalized);
        var filters = new Dictionary<string, object?>(
            result.EffectiveFilters,
            StringComparer.OrdinalIgnoreCase)
        {
            ["customerIds"] = normalized.CustomerIds ?? [],
            ["projectIds"] = normalized.ProjectIds ?? [],
            ["projectManagerUserIds"] = normalized.ProjectManagerUserIds ?? [],
            ["engineerUserIds"] = normalized.EngineerUserIds ?? [],
            ["teamIds"] = normalized.TeamIds ?? [],
            ["contractTypes"] = normalized.ContractTypes ?? []
        };
        filters["customers"] = Labels(
            normalized.CustomerIds,
            id => seed.Directory.Customers.FirstOrDefault(row => row.CustomerId == id)?.CustomerName);
        filters["projects"] = Labels(
            normalized.ProjectIds,
            id => seed.Projects.FirstOrDefault(row => row.ProjectId == id) is { } project
                ? $"{project.ProjectCode} · {project.ProjectName}"
                : null);
        filters["projectManagers"] = Labels(
            normalized.ProjectManagerUserIds,
            id => seed.Projects.FirstOrDefault(row => row.ProjectManagerUserId == id)?.ProjectManagerName);
        filters["engineers"] = Labels(
            normalized.EngineerUserIds,
            id => seed.Projects.SelectMany(project => project.Engineers)
                .FirstOrDefault(engineer => engineer.UserId == id)?.DisplayName);
        filters["teams"] = Labels(
            normalized.TeamIds,
            id => seed.Directory.Teams.FirstOrDefault(row => row.TeamId == id)?.TeamName);

        var sources = result.Sources
            .Append(seed.Directory.Source)
            .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(source => source.Required ? 0 : 1)
            .ThenBy(source => source.Name)
            .ToArray();
        var requiredUnavailable = sources.Any(source => source.Required
            && source.Status is "unavailable" or "restricted");
        var degraded = sources.Any(source => source.Status is "partial" or "unavailable" or "restricted");
        var status = rows.Length == 0
            ? requiredUnavailable ? "source_unavailable" : "no_data"
            : degraded ? "partial" : "complete";
        var message = status switch
        {
            "complete" => $"{rows.Length} role-scoped analytics row(s) loaded.",
            "partial" => $"{rows.Length} role-scoped row(s) loaded. One or more independent sources are degraded; healthy results remain visible.",
            "source_unavailable" => "A required source is unavailable or outside the current scope. Other Analytics Center workspaces remain usable.",
            _ => "No data matched the current role scope and selected report criteria."
        };
        return result with
        {
            EffectiveFilters = filters,
            Rows = rows,
            Sources = sources,
            ResultStatus = status,
            Message = message,
            ScopeEvidence = new
            {
                result.ScopeEvidence,
                multipleSelection = true,
                engineerSelfScope = IsEngineerOnly(seed.Actor),
                projectManagerOwnPortfolio = IsPmOnly(seed.Actor),
                serverAuthorized = true
            }
        };
    }

    internal static AnalyticsExperienceRequest NormalizeSelections(
        AnalyticsExperienceSeed seed,
        AnalyticsExperienceRequest request)
    {
        var engineerIds = Ids(request.EngineerUserIds, request.EngineerUserId);
        var pmIds = Ids(request.ProjectManagerUserIds, request.ProjectManagerUserId);
        if (IsEngineerOnly(seed.Actor)) engineerIds = [seed.Actor.EffectiveUserId];
        if (IsPmOnly(seed.Actor)) pmIds = [seed.Actor.EffectiveUserId];
        return request with
        {
            ReportCode = (request.ReportCode ?? string.Empty).Trim(),
            CustomerIds = Ids(request.CustomerIds, request.CustomerId),
            ProjectIds = Ids(request.ProjectIds, request.ProjectId),
            ProjectManagerUserIds = pmIds,
            EngineerUserIds = engineerIds,
            TeamIds = Ids(request.TeamIds, request.TeamId),
            ContractTypes = Strings(request.ContractTypes, request.ContractType)
                .Select(CanonicalContractType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Search = Clean(request.Search, 300),
            ProjectStatus = Clean(request.ProjectStatus, 80),
            BudgetStatus = Clean(request.BudgetStatus, 80),
            WorkflowStatus = Clean(request.WorkflowStatus, 80),
            Severity = Clean(request.Severity, 40),
            ModuleCode = Clean(request.ModuleCode, 20),
            SourceStatus = Clean(request.SourceStatus, 40),
            Limit = Math.Clamp(request.Limit ?? 500, 1, 5000)
        };
    }

    internal static FinancialOperationsProject[] ApplyProjectFilters(
        FinancialOperationsProject[] projects,
        AnalyticsDirectorySnapshot directory,
        AnalyticsExperienceRequest request)
    {
        var customerIds = Ids(request.CustomerIds, request.CustomerId).ToHashSet();
        var projectIds = Ids(request.ProjectIds, request.ProjectId).ToHashSet();
        var pmIds = Ids(request.ProjectManagerUserIds, request.ProjectManagerUserId).ToHashSet();
        var engineerIds = Ids(request.EngineerUserIds, request.EngineerUserId).ToHashSet();
        var teamIds = Ids(request.TeamIds, request.TeamId).ToHashSet();
        var contractTypes = Strings(request.ContractTypes, request.ContractType)
            .Select(CanonicalContractType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teamMembers = directory.Teams
            .Where(team => teamIds.Contains(team.TeamId))
            .SelectMany(team => team.MemberUserIds)
            .ToHashSet();

        return projects.Where(project =>
                (customerIds.Count == 0 || project.ClientId.HasValue && customerIds.Contains(project.ClientId.Value))
                && (projectIds.Count == 0 || projectIds.Contains(project.ProjectId))
                && (pmIds.Count == 0 || project.ProjectManagerUserId.HasValue && pmIds.Contains(project.ProjectManagerUserId.Value))
                && (engineerIds.Count == 0 || project.Engineers.Any(engineer => engineerIds.Contains(engineer.UserId)))
                && (teamIds.Count == 0 || ProjectUserIds(project).Any(teamMembers.Contains))
                && (contractTypes.Count == 0 || contractTypes.Contains(CanonicalContractType(project.ContractType))))
            .ToArray();
    }

    internal static EnterpriseReportDefinition AdaptDefinition(EnterpriseReportDefinition definition)
    {
        var filters = new List<EnterpriseReportFilterDefinition>();
        foreach (var filter in definition.Filters.Where(filter => filter.Key is not
                     ("fiscalPeriod" or "organization" or "cadence")))
        {
            filters.Add(filter.Key switch
            {
                "customer" => Multi(filter, "customerIds", "Customers", "customers"),
                "projectId" => Multi(filter, "projectIds", "Projects", "projects"),
                "projectManagerUserId" => Multi(filter, "projectManagerUserIds", "Project Managers", "projectManagers"),
                "engineerUserId" => Multi(filter, "engineerUserIds", "Engineers", "engineers"),
                "contractType" => Multi(filter, "contractTypes", "Contract types", "contractTypes"),
                _ => filter
            });
        }
        var hasPeopleOrProject = filters.Any(filter => filter.Key is
            "customerIds" or "projectIds" or "projectManagerUserIds" or "engineerUserIds");
        if (hasPeopleOrProject && filters.All(filter => filter.Key != "teamIds"))
        {
            var position = filters.FindLastIndex(filter => filter.Key is
                "customerIds" or "projectIds" or "projectManagerUserIds" or "engineerUserIds");
            filters.Insert(position + 1, new EnterpriseReportFilterDefinition(
                "teamIds", "Teams", "multiselect", false, false, null, null, "teams", Array.Empty<Guid>()));
        }
        return definition with { Filters = filters.ToArray() };
    }

    internal static bool CanView(FinancialOperationsActor actor) => actor.Broad
        || actor.HasPermission(
            "VIEW_ENTERPRISE_REPORTING", "VIEW_FINANCIAL_REPORT_CENTER", "VIEW_REPORTS",
            "VIEW_EXECUTIVE_REPORTING", "VIEW_ANALYTICS_DASHBOARDS", "MANAGE_ALL", "SYSTEM_ADMINISTRATION")
        || actor.HasRole(
            "ENGINEER", "ENGINEERING", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
            "MANAGER", "ENGINEERING_MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
            "PROJECT_MANAGEMENT_LEAD", "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE",
            "BILLING", "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT");

    internal static bool CanRun(FinancialOperationsActor actor) => CanView(actor)
        && (actor.Broad
            || actor.HasPermission(
                "RUN_ENTERPRISE_REPORTING", "RUN_FINANCIAL_REPORTS", "MANAGE_REPORTS",
                "MANAGE_ANALYTICS_SCHEDULES", "MANAGE_ALL")
            || actor.HasRole(
                "ENGINEER", "ENGINEERING", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
                "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING",
                "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"));

    internal static bool CanExport(FinancialOperationsActor actor) => CanRun(actor)
        && (actor.Broad
            || actor.HasPermission(
                "EXPORT_ENTERPRISE_REPORTING", "EXPORT_FINANCIAL_REPORTS", "MANAGE_ALL")
            || actor.HasRole(
                "ENGINEER", "ENGINEERING", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
                "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING",
                "EXECUTIVE", "SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"));

    internal static bool CanManageSchedules(FinancialOperationsActor actor) => !actor.IsViewAs
        && CanRun(actor)
        && (actor.Broad
            || actor.HasPermission("MANAGE_ANALYTICS_SCHEDULES", "MANAGE_ALL")
            || actor.HasRole("ENGINEER", "ENGINEERING", "PROJECT_MANAGER", "PROJECT_MANAGEMENT"));

    internal static bool CanDeliverMultiple(FinancialOperationsActor actor) => !actor.IsViewAs
        && (actor.Broad
            || actor.HasPermission("DELIVER_ANALYTICS_SCHEDULES", "MANAGE_ALL")
            || actor.HasRole(
                "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING", "EXECUTIVE",
                "MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD",
                "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD"));

    internal static bool IsEngineerOnly(FinancialOperationsActor actor)
    {
        var roles = actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roles.Overlaps(["ENGINEER", "ENGINEERING"])
            && !actor.PmLead
            && !roles.Overlaps(["MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD"]);
    }

    internal static bool IsPmOnly(FinancialOperationsActor actor)
    {
        var roles = actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return !actor.Broad
            && (actor.PmLead || roles.Overlaps(["PROJECT_MANAGER", "PROJECT_MANAGEMENT"]));
    }

    internal static object Access(FinancialOperationsActor actor) => new
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

    internal static object Capabilities(FinancialOperationsActor actor) => new
    {
        canView = CanView(actor),
        canPreview = CanRun(actor),
        canRun = CanRun(actor) && !actor.IsViewAs,
        canExport = CanExport(actor) && !actor.IsViewAs,
        canViewSchedules = CanView(actor),
        canManageSchedules = CanManageSchedules(actor),
        canDeliverMultipleRecipients = CanDeliverMultiple(actor),
        brandedPdf = true,
        brandedExcel = true,
        multipleSelection = true,
        dynamicFilters = true
    };

    internal static string ScopeExplanation(FinancialOperationsActor actor) =>
        IsEngineerOnly(actor)
            ? "Engineer scope: person-level reports and filters are locked to the signed-in Engineer and assigned projects."
            : IsPmOnly(actor)
                ? "Project Manager scope: reports and PM filters are locked to projects managed by the effective user."
                : actor.Broad
                    ? "Organization-authorized scope: records and sensitive financial fields still follow existing permission boundaries."
                    : "Role-scoped reporting: choices contain only server-authorized records.";

    internal static IResult AccessDenied(string message = "Analytics Center access is required.") =>
        Results.Json(new
        {
            module = "030",
            moduleName = "Analytics Center",
            status = "analytics_access_required",
            message
        }, statusCode: StatusCodes.Status403Forbidden);

    internal static IResult ViewAsReadOnly(string action) => Results.Json(new
    {
        module = "030",
        moduleName = "Analytics Center",
        status = "view_as_read_only",
        message = $"Exit View-As before {action}. Preview remains available."
    }, statusCode: StatusCodes.Status403Forbidden);

    internal static IResult ReportNotFound(EnterpriseReportingContext context) => Results.BadRequest(new
    {
        module = "030",
        moduleName = "Analytics Center",
        status = "analytics_report_not_available",
        message = "Select an Analytics Center report available to the current role scope.",
        availableReports = EnterpriseReportingCatalog.ForContext(context).Select(report => report.Code)
    });

    private static EnterpriseReportRequest ToEnterpriseRequest(AnalyticsExperienceRequest request) => new(
        request.ReportCode,
        request.Search,
        One(request.ProjectIds, request.ProjectId),
        One(request.CustomerIds, request.CustomerId),
        null,
        One(request.ProjectManagerUserIds, request.ProjectManagerUserId),
        One(request.EngineerUserIds, request.EngineerUserId),
        request.ProjectStatus,
        request.BudgetStatus,
        One(request.ContractTypes, request.ContractType),
        request.Billable,
        request.DateFrom,
        request.DateTo,
        request.WorkflowStatus,
        request.Severity,
        request.ModuleCode,
        request.SourceStatus,
        request.Limit,
        false);

    private static Dictionary<string, object?>[] ApplyRowFilters(
        Dictionary<string, object?>[] rows,
        AnalyticsExperienceSeed seed,
        EnterpriseReportDefinition definition,
        AnalyticsExperienceRequest request)
    {
        var engineerIds = Ids(request.EngineerUserIds, request.EngineerUserId).ToHashSet();
        if (engineerIds.Count <= 1) return rows;
        if (definition.Code is not (
            "time_entry_detail" or "engineer_workload" or "engineer_utilization"
            or "project_team_assignments" or "expense_detail"))
            return rows;
        var engineerNames = seed.Projects.SelectMany(project => project.Engineers)
            .Where(engineer => engineerIds.Contains(engineer.UserId))
            .Select(engineer => engineer.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rows.Where(row =>
        {
            var id = RowGuid(row, "engineerUserId", "userId", "ownerUserId");
            if (id.HasValue) return engineerIds.Contains(id.Value);
            var name = RowText(row, "engineer", "owner");
            return string.IsNullOrWhiteSpace(name) || engineerNames.Contains(name);
        }).ToArray();
    }

    private static EnterpriseReportFilterDefinition Multi(
        EnterpriseReportFilterDefinition source,
        string key,
        string label,
        string optionSource) => source with
    {
        Key = key,
        Label = label,
        Type = "multiselect",
        OptionSource = optionSource,
        Placeholder = null,
        DefaultValue = Array.Empty<string>()
    };

    private static EnterpriseReportOption[] CustomerOptions(
        AnalyticsDirectorySnapshot directory,
        FinancialOperationsProject[] projects)
    {
        var visible = projects.Where(project => project.ClientId.HasValue)
            .Select(project => project.ClientId!.Value)
            .ToHashSet();
        return directory.Customers
            .Where(customer => visible.Count == 0 || visible.Contains(customer.CustomerId))
            .OrderBy(customer => customer.CustomerName)
            .Select(customer => new EnterpriseReportOption(
                customer.CustomerId.ToString(),
                customer.CustomerName,
                false,
                string.IsNullOrWhiteSpace(customer.CustomerCode)
                    ? "Customer Directory"
                    : $"Customer Directory · {customer.CustomerCode}"))
            .ToArray();
    }

    private static EnterpriseReportOption[] ProjectOptions(FinancialOperationsProject[] projects) =>
        projects.OrderBy(project => project.CustomerName)
            .ThenBy(project => project.ProjectName)
            .Select(project => new EnterpriseReportOption(
                project.ProjectId.ToString(),
                $"{project.ProjectCode} · {project.ProjectName}",
                false,
                project.CustomerName))
            .ToArray();

    private static EnterpriseReportOption[] ProjectManagerOptions(
        FinancialOperationsProject[] projects,
        Guid[] selected) => projects
        .Where(project => project.ProjectManagerUserId.HasValue)
        .GroupBy(project => project.ProjectManagerUserId!.Value)
        .OrderBy(group => group.First().ProjectManagerName)
        .Select(group => new EnterpriseReportOption(
            group.Key.ToString(),
            group.First().ProjectManagerName,
            false,
            selected.Contains(group.Key) ? "Selected" : group.First().ProjectManagerEmail))
        .ToArray();

    private static EnterpriseReportOption[] EngineerOptions(
        FinancialOperationsProject[] projects,
        Guid[] selected) => projects
        .SelectMany(project => project.Engineers)
        .GroupBy(engineer => engineer.UserId)
        .OrderBy(group => group.First().DisplayName)
        .Select(group => new EnterpriseReportOption(
            group.Key.ToString(),
            group.First().DisplayName,
            false,
            selected.Contains(group.Key) ? "Selected" : group.First().Email))
        .ToArray();

    private static EnterpriseReportOption[] TeamOptions(
        AnalyticsDirectorySnapshot directory,
        FinancialOperationsProject[] projects)
    {
        var visibleUsers = projects.SelectMany(ProjectUserIds).ToHashSet();
        return directory.Teams
            .Where(team => team.MemberUserIds.Any(visibleUsers.Contains))
            .OrderBy(team => team.TeamName)
            .Select(team => new EnterpriseReportOption(
                team.TeamId.ToString(),
                team.TeamName,
                false,
                $"{team.MemberUserIds.Count(visibleUsers.Contains)} visible member(s)"))
            .ToArray();
    }

    private static IEnumerable<Guid> ProjectUserIds(FinancialOperationsProject project) =>
        project.Engineers.Select(engineer => engineer.UserId)
            .Concat(project.ProjectManagerUserId.HasValue ? [project.ProjectManagerUserId.Value] : [])
            .Concat(project.ProjectTeamCoordinator is null ? [] : [project.ProjectTeamCoordinator.UserId])
            .Concat(project.SolutionArchitect is null ? [] : [project.SolutionArchitect.UserId])
            .Concat(project.AccountExecutive is null ? [] : [project.AccountExecutive.UserId]);

    private static Guid[] Ids(Guid[]? values, Guid? singular) =>
        (values ?? Array.Empty<Guid>())
            .Concat(singular.HasValue ? [singular.Value] : [])
            .Where(value => value != Guid.Empty)
            .Distinct()
            .ToArray();

    private static string[] Strings(string[]? values, string? singular) =>
        (values ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(singular) ? [] : [singular!])
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static Guid? One(Guid[]? values, Guid? singular)
    {
        var ids = Ids(values, singular);
        return ids.Length == 1 ? ids[0] : null;
    }

    private static string? One(string[]? values, string? singular)
    {
        var rows = Strings(values, singular);
        return rows.Length == 1 ? rows[0] : null;
    }

    private static string CanonicalContractType(string? value)
    {
        var original = (value ?? string.Empty).Trim();
        var normalized = new string(original.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
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

    private static string? Clean(string? value, int maximum)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        if (clean.Length == 0) return null;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string[] Labels(Guid[]? values, Func<Guid, string?> selector) =>
        (values ?? Array.Empty<Guid>()).Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

    private static Guid? RowGuid(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetValue(key, out var value) || value is null) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string RowText(Dictionary<string, object?> row, params string[] keys)
    {
        foreach (var key in keys)
            if (row.TryGetValue(key, out var value) && value is not null)
                return value.ToString() ?? string.Empty;
        return string.Empty;
    }
}
