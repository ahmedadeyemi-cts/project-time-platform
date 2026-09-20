using System.Globalization;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 025 persistent SOW + GSD workspace. Solution Architects author their
/// own packages, managers receive read-only reporting-scope visibility, and
/// administrators retain governed support access. Celar AI output is always a
/// review artifact: suggested effort is preserved separately from SA-final LOE.
/// </summary>
public static class Module025SowGsdModule
{
    internal const string ModuleNumber = "025";
    internal const string MigrationId = "099_module025_sow_gsd_workspace";
    internal const string WorkspaceContract = "module025-sow-gsd-workspace-v1-20260830";
    private const int MaximumRequestBytes = 512 * 1024;
    private const int MaximumSearchLength = 200;

    private static readonly string[] ViewRoles =
    {
        "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR",
        "SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT", "SA", "SAA",
        "MANAGER", "ENGINEERING_MANAGER", "SOLUTIONS_ARCHITECT_MANAGER",
        "SOLUTION_ARCHITECT_MANAGER", "TEAM_LEAD", "ENGINEERING_TEAM_LEAD"
    };

    private static readonly HashSet<string> AdministratorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR"
    };

    private static readonly HashSet<string> SolutionArchitectRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT", "SA", "SAA"
    };

    private static readonly HashSet<string> ManagerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANAGER", "ENGINEERING_MANAGER", "SOLUTIONS_ARCHITECT_MANAGER",
        "SOLUTION_ARCHITECT_MANAGER", "TEAM_LEAD", "ENGINEERING_TEAM_LEAD"
    };

    private static readonly string[] PhaseCodes = { "plan", "design", "implement", "validate", "release" };
    private static readonly string[] AccountExecutiveRoles =
    [
        "SALES",
        "ACCOUNT_EXECUTIVE",
        "SALES_ACCOUNT_EXECUTIVE",
        "ACCOUNT_EXECUTIVES"
    ];
    private static readonly string[] InsideSalesRepresentativeRoles =
    [
        "INSIDE_SALES",
        "INSIDE_SALES_REPRESENTATIVE",
        "SALES_SUPPORT",
        "RESALE"
    ];

    public static WebApplication MapModule025SowGsdEndpoints(this WebApplication app)
    {
        MapModule025TemplateCatalogEndpoints(app);
        MapModule025WorkTrackingEndpoints(app);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/handoff-notifications", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)HandoffNotificationStatusAsync);
        app.MapGet("/api/module025/sow-gsd/team-work", (Func<string?, string?, HttpContext, CancellationToken, Task<IResult>>)TeamWorkAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/transfer-options", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)TransferOptionsAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/transfer", (Func<Guid, Module025SowGsdTransferRequest, HttpContext, CancellationToken, Task<IResult>>)TransferAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/handoff/acknowledge", (Func<Guid, Module025HandoffAcknowledgeRequest, HttpContext, CancellationToken, Task<IResult>>)AcknowledgeHandoffAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/handoff/return", (Func<Guid, Module025HandoffReturnRequest, HttpContext, CancellationToken, Task<IResult>>)ReturnHandoffAsync);
        app.MapGet("/api/module025/sow-gsd/bootstrap", (Func<HttpContext, CancellationToken, Task<IResult>>)BootstrapAsync);
        app.MapGet("/api/module025/sow-gsd", (Func<string?, Guid?, string?, HttpContext, CancellationToken, Task<IResult>>)ListAsync);
        app.MapPost("/api/module025/sow-gsd", (Func<Module025SowGsdCreateRequest, HttpContext, CancellationToken, Task<IResult>>)CreateAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetAsync);
        app.MapPut("/api/module025/sow-gsd/{engagementId:guid}", (Func<Guid, Module025SowGsdSaveRequest, HttpContext, CancellationToken, Task<IResult>>)SaveAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/generate", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GenerateAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/generations/latest", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetLatestGenerationAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/generations/{generationId:guid}", (Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)GetGenerationAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/confirm", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ConfirmAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/reopen", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ReopenAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/archive", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ArchiveAsync);
        app.MapPost("/api/module025/sow-gsd/{engagementId:guid}/unarchive", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)UnarchiveAsync);
        app.MapDelete("/api/module025/sow-gsd/{engagementId:guid}", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DeleteDraftAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/sow.docx", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadSowAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/gsd.xlsx", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadGsdAsync);
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/draft-sow.docx", (Guid engagementId, HttpContext context, CancellationToken cancellationToken) => DownloadDraftAsync(engagementId, true, context, cancellationToken));
        app.MapGet("/api/module025/sow-gsd/{engagementId:guid}/draft-gsd.xlsx", (Guid engagementId, HttpContext context, CancellationToken cancellationToken) => DownloadDraftAsync(engagementId, false, context, cancellationToken));
        return app;
    }

    private static async Task<IResult> BootstrapAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();

        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (!(access.IsSolutionArchitect || access.IsManager || access.IsAdministrator)) return Forbidden("module025_view");

        var customers = await LoadCustomersAsync(connection, cancellationToken);
        var accountExecutives = await LoadPeopleByRoleAsync(connection, AccountExecutiveRoles, cancellationToken);
        var insideSalesRepresentatives = await LoadPeopleByRoleAsync(
            connection,
            InsideSalesRepresentativeRoles,
            cancellationToken);
        var solutionArchitects = await LoadVisibleSolutionArchitectsAsync(connection, access, cancellationToken);

        return Results.Ok(new
        {
            status = "module025_workspace_ready",
            module = ModuleNumber,
            migration = MigrationId,
            contract = WorkspaceContract,
            capabilities = new { workTracking = true, temporaryCoverage = true, handoffNotifications = true },
            currentUser = new
            {
                userId = access.EffectiveUserId,
                access.DisplayName,
                access.Email,
                access.DepartmentName,
                access.TeamName
            },
            access = new
            {
                access.IsAdministrator,
                access.IsSolutionArchitect,
                protectedTestUatRoleFixture = access.IsProtectedTestUatRoleFixture,
                access.IsManager,
                access.IsViewAs,
                canCreate = access.CanCreate,
                canEditOwn = !access.IsViewAs && (access.IsSolutionArchitect || access.IsAdministrator),
                managerScopeReadOnly = access.IsManager && !access.IsAdministrator
            },
            customers,
            accountExecutives,
            insideSalesRepresentatives,
            // Preserve the stored/API field name while existing clients move to
            // the customer-facing Inside Sales Representative terminology.
            resalePeople = insideSalesRepresentatives,
            solutionArchitects,
            commercialModels = new[]
            {
                new { key = "time_and_materials", label = "Time & Materials (T&M)" },
                new { key = "fixed", label = "Fixed Price" }
            },
            customerPrograms = new[]
            {
                new { key = "standard", label = "Standard", gsdTemplateKey = Module025SowGsdDocumentExporter.StandardGsdTemplateKey, gsdTemplate = "Standard GSD" },
                new { key = "toyota", label = "Toyota", gsdTemplateKey = Module025SowGsdDocumentExporter.HaeaGsdTemplateKey, gsdTemplate = Module025SowGsdDocumentExporter.HaeaGsdDisplayName },
                new { key = "hyundai", label = "Hyundai", gsdTemplateKey = Module025SowGsdDocumentExporter.HaeaGsdTemplateKey, gsdTemplate = Module025SowGsdDocumentExporter.HaeaGsdDisplayName }
            },
            phases = PhaseCodes.Select((code, index) => new { code, label = PhaseLabel(code), sortOrder = index + 1 }).ToArray(),
            autosave = new { enabled = true, recommendedDebounceMilliseconds = 900, optimisticRevision = true },
            stateChanged = false
        });
    }

    private static Task<IResult> ListAsync(string? state, Guid? ownerUserId, string? search, HttpContext context, CancellationToken cancellationToken) =>
        LoadEngagementListAsync(state, ownerUserId, search, context, cancellationToken, false);

    private static Task<IResult> TeamWorkAsync(string? state, string? search, HttpContext context, CancellationToken cancellationToken) =>
        LoadEngagementListAsync(state, null, search, context, cancellationToken, true);

    private static async Task<IResult> LoadEngagementListAsync(string? state, Guid? ownerUserId, string? search, HttpContext context, CancellationToken cancellationToken, bool teamWork)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();

        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (teamWork && !(access.IsManager || access.IsAdministrator)) return Forbidden("module025_team_work");
        var selectedOwner = ownerUserId ?? access.EffectiveUserId;
        if (!teamWork && !access.CanViewOwned(selectedOwner)) return Forbidden("module025_owner_scope");
        var selectedOwners = teamWork ? access.VisibleSolutionArchitectIds.ToArray() : new[] { selectedOwner };
        var archived = string.Equals(state, "archived", StringComparison.OrdinalIgnoreCase);
        var normalizedSearch = Clean(search, MaximumSearchLength);

        const string sql = """
            SELECT engagement_id, engagement_number, owner_user_id, owner_display_name,
                   customer_name, commercial_model, customer_program, gsd_template_key,
                   account_executive_name, resale_name, status, is_active, revision,
                   last_generated_at, confirmed_at, archived_at, created_at, updated_at,
                   COALESCE((SELECT sum(final_hours) FROM module025_sow_gsd_phases phase WHERE phase.engagement_id=engagement.engagement_id),0),
                   COALESCE((SELECT sum(suggested_hours) FROM module025_sow_gsd_phases phase WHERE phase.engagement_id=engagement.engagement_id),0),
                   project_name, owner_department_name, owner_team_name, count(*) OVER()
            FROM module025_sow_gsd_engagements engagement
            WHERE owner_user_id=ANY(@owner_user_ids)
              AND is_active=@is_active
              AND (@search='' OR engagement_number ILIKE '%' || @search || '%' OR customer_name ILIKE '%' || @search || '%' OR project_name ILIKE '%' || @search || '%' OR service_overview ILIKE '%' || @search || '%')
            ORDER BY updated_at DESC, engagement_id
            LIMIT 300;
            """;
        var rows = new List<object>();
        long totalCount = 0;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("owner_user_ids", selectedOwners);
        command.Parameters.AddWithValue("is_active", !archived);
        command.Parameters.AddWithValue("search", normalizedSearch);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                engagementId = reader.GetGuid(0),
                engagementNumber = reader.GetString(1),
                ownerUserId = reader.GetGuid(2),
                ownerDisplayName = reader.GetString(3),
                customerName = reader.GetString(4),
                commercialModel = reader.GetString(5),
                customerProgram = reader.GetString(6),
                gsdTemplateKey = reader.GetString(7),
                accountExecutiveName = reader.GetString(8),
                resaleName = reader.GetString(9),
                status = reader.GetString(10),
                isActive = reader.GetBoolean(11),
                revision = reader.GetInt32(12),
                lastGeneratedAt = NullableTimestamp(reader, 13),
                confirmedAt = NullableTimestamp(reader, 14),
                archivedAt = NullableTimestamp(reader, 15),
                createdAt = reader.GetFieldValue<DateTimeOffset>(16),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(17),
                finalHours = reader.GetDecimal(18),
                suggestedHours = reader.GetDecimal(19),
                projectName = reader.GetString(20),
                ownerDepartmentName = reader.GetString(21), ownerTeamName = reader.GetString(22)
            });
            totalCount = reader.GetInt64(23);
        }
        return Results.Ok(new { status = "module025_engagements_loaded", state = archived ? "archived" : "active", ownerUserId = teamWork ? (Guid?)null : selectedOwner,
            scope = teamWork ? "reporting_team" : "owner", count = rows.Count, totalCount, truncated = totalCount > rows.Count, engagements = rows, stateChanged = false });
    }

    private static async Task<IResult> CreateAsync(Module025SowGsdCreateRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (!access.CanCreate) return Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_create");

        var customer = await ResolveCustomerAsync(connection, request.CustomerId, request.CustomerName, request.CustomerEntryMode, cancellationToken);
        if (customer.Error is not null) return customer.Error;
        var accountExecutive = await ResolvePersonAsync(connection, request.AccountExecutiveUserId, AccountExecutiveRoles, cancellationToken);
        if (request.AccountExecutiveUserId.HasValue && !accountExecutive.UserId.HasValue) return Results.BadRequest(new { status = "account_executive_not_found", message = "Select an active Account Executive." });
        var resale = await ResolvePersonAsync(connection, request.ResaleUserId, InsideSalesRepresentativeRoles, cancellationToken);
        if (request.ResaleUserId.HasValue && !resale.UserId.HasValue) return Results.BadRequest(new { status = "resale_not_found", message = "Select an active Inside Sales Representative." });

        var commercialModel = NormalizeCommercialModel(request.CommercialModel);
        var customerProgram = NormalizeCustomerProgram(request.CustomerProgram);
        var engagementId = Guid.NewGuid();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string insert = """
            INSERT INTO module025_sow_gsd_engagements(
                engagement_id, owner_user_id, owner_display_name, owner_department_name, owner_team_name,
                customer_id, customer_name, customer_entry_mode, commercial_model, customer_program, gsd_template_key,
                account_executive_user_id, account_executive_name, resale_user_id, resale_name, service_overview, project_name)
            VALUES(@engagement_id,@owner_user_id,@owner_display_name,@department_name,@team_name,@customer_id,@customer_name,@customer_entry_mode,
                @commercial_model,@customer_program,@gsd_template_key,@account_executive_user_id,@account_executive_name,@resale_user_id,@resale_name,@service_overview,@project_name)
            RETURNING engagement_number;
            """;
        string engagementNumber;
        await using (var command = new NpgsqlCommand(insert, connection, transaction))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            command.Parameters.AddWithValue("owner_user_id", access.EffectiveUserId);
            command.Parameters.AddWithValue("owner_display_name", access.DisplayName);
            command.Parameters.AddWithValue("department_name", access.DepartmentName);
            command.Parameters.AddWithValue("team_name", access.TeamName);
            AddNullableGuid(command, "customer_id", customer.CustomerId);
            command.Parameters.AddWithValue("customer_name", customer.CustomerName);
            command.Parameters.AddWithValue("customer_entry_mode", customer.Mode);
            command.Parameters.AddWithValue("commercial_model", commercialModel);
            command.Parameters.AddWithValue("customer_program", customerProgram);
            command.Parameters.AddWithValue("gsd_template_key", TemplateKey(customerProgram));
            AddNullableGuid(command, "account_executive_user_id", accountExecutive.UserId);
            command.Parameters.AddWithValue("account_executive_name", accountExecutive.DisplayName);
            AddNullableGuid(command, "resale_user_id", resale.UserId);
            command.Parameters.AddWithValue("resale_name", resale.DisplayName);
            command.Parameters.AddWithValue("service_overview", Clean(request.ServiceOverview, 30_000));
            command.Parameters.AddWithValue("project_name", Clean(request.ProjectName, 500));
            engagementNumber = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        await InsertEmptyPhasesAsync(connection, transaction, engagementId, cancellationToken);
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, 1, "created", "SOW/GSD workspace created.", new { engagementNumber }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var created = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        return created is null
            ? Results.Json(new { status = "module025_create_readback_failed", engagementId, engagementNumber }, statusCode: StatusCodes.Status500InternalServerError)
            : Results.Created($"/api/module025/sow-gsd/{engagementId:D}", PublicEngagement(created, access));
    }

    private static async Task<IResult> GetAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var readable = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (readable.Error is not null) return readable.Error;
        await using var connection = readable.Connection!;
        return Results.Ok(PublicEngagement(readable.Engagement!, readable.Access!));
    }

    private static async Task<IResult> SaveAsync(Guid engagementId, Module025SowGsdSaveRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        if (context.Request.ContentLength is > MaximumRequestBytes) return RequestTooLarge();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        var current = writable.Engagement!;
        var access = writable.Access!;
        if (current.Status == "confirmed") return StateConflict("confirmed_record", "Reopen this confirmed SOW/GSD before editing it.");
        if (request.ExpectedRevision != current.Revision) return RevisionConflict(current.Revision);

        foreach (var phaseRequest in request.Phases ?? Array.Empty<Module025SowGsdPhaseSaveRequest>())
        {
            var taskError = Module025TaskEstimates.Validate(phaseRequest.Tasks);
            if (taskError is not null) return Results.BadRequest(new { status = "invalid_task_estimates", message = taskError });
        }
        if ((request.Phases ?? Array.Empty<Module025SowGsdPhaseSaveRequest>()).GroupBy(p => NormalizePhaseCode(p.PhaseCode)).Any(g => g.Key is null || g.Count() > 1))
            return Results.BadRequest(new { status = "invalid_phases", message = "Provide each recognized phase at most once." });

        var customer = await ResolveCustomerAsync(connection, request.CustomerId, request.CustomerName, request.CustomerEntryMode, cancellationToken);
        if (customer.Error is not null) return customer.Error;
        var accountExecutive = await ResolvePersonAsync(connection, request.AccountExecutiveUserId, AccountExecutiveRoles, cancellationToken);
        if (request.AccountExecutiveUserId.HasValue && !accountExecutive.UserId.HasValue) return Results.BadRequest(new { status = "account_executive_not_found", message = "Select an active Account Executive." });
        var resale = await ResolvePersonAsync(connection, request.ResaleUserId, InsideSalesRepresentativeRoles, cancellationToken);
        if (request.ResaleUserId.HasValue && !resale.UserId.HasValue) return Results.BadRequest(new { status = "resale_not_found", message = "Select an active Inside Sales Representative." });

        var serviceOverview = Clean(request.ServiceOverview, 30_000);
        var overviewChanged = !string.Equals(current.ServiceOverview, serviceOverview, StringComparison.Ordinal);
        var customerProgram = NormalizeCustomerProgram(request.CustomerProgram);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string update = """
            UPDATE module025_sow_gsd_engagements
            SET customer_id=@customer_id, customer_name=@customer_name, customer_entry_mode=@customer_entry_mode,
                commercial_model=@commercial_model, customer_program=@customer_program, gsd_template_key=@gsd_template_key,
                account_executive_user_id=@account_executive_user_id, account_executive_name=@account_executive_name,
                resale_user_id=@resale_user_id, resale_name=@resale_name, service_overview=@service_overview, project_name=@project_name,
                status=CASE WHEN service_overview IS DISTINCT FROM @service_overview THEN 'draft' ELSE status END,
                last_generated_at=CASE WHEN service_overview IS DISTINCT FROM @service_overview THEN NULL ELSE last_generated_at END,
                revision=revision+1
            WHERE engagement_id=@engagement_id AND revision=@expected_revision AND is_active=TRUE AND status NOT IN ('confirmed','archived')
            RETURNING revision;
            """;
        int nextRevision;
        await using (var command = new NpgsqlCommand(update, connection, transaction))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            command.Parameters.AddWithValue("expected_revision", request.ExpectedRevision);
            AddNullableGuid(command, "customer_id", customer.CustomerId);
            command.Parameters.AddWithValue("customer_name", customer.CustomerName);
            command.Parameters.AddWithValue("customer_entry_mode", customer.Mode);
            command.Parameters.AddWithValue("commercial_model", NormalizeCommercialModel(request.CommercialModel));
            command.Parameters.AddWithValue("customer_program", customerProgram);
            command.Parameters.AddWithValue("gsd_template_key", TemplateKey(customerProgram));
            AddNullableGuid(command, "account_executive_user_id", accountExecutive.UserId);
            command.Parameters.AddWithValue("account_executive_name", accountExecutive.DisplayName);
            AddNullableGuid(command, "resale_user_id", resale.UserId);
            command.Parameters.AddWithValue("resale_name", resale.DisplayName);
            command.Parameters.AddWithValue("service_overview", serviceOverview);
            command.Parameters.AddWithValue("project_name", Clean(request.ProjectName, 500));
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return RevisionConflict(current.Revision);
            }
            nextRevision = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        foreach (var phaseRequest in request.Phases ?? Array.Empty<Module025SowGsdPhaseSaveRequest>())
        {
            var phaseCode = NormalizePhaseCode(phaseRequest.PhaseCode);
            if (phaseCode is not null)
            {
                var tasks = phaseRequest.Tasks ?? current.Phases.First(p => p.PhaseCode == phaseCode).Tasks;
                var reviewed = phaseRequest with
                {
                    Tasks = tasks,
                    FinalHours = Module025TaskEstimates.Complete(tasks) ? tasks!.Sum(t => t.Hours!.Value) : phaseRequest.FinalHours
                };
                await SaveHumanPhaseAsync(connection, transaction, engagementId, phaseCode, reviewed, cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
        var saved = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        return Results.Ok(new { status = overviewChanged ? "module025_saved_scope_regeneration_required" : "module025_autosaved", revision = nextRevision, engagement = saved is null ? null : PublicEngagement(saved, access), requiresRegeneration = overviewChanged, stateChanged = true });
    }

    private static async Task<IResult> GenerateAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        var current = writable.Engagement!;
        var access = writable.Access!;
        var protectedTestUatGrant = access.IsProtectedTestUatRoleFixture
            ? Module025ProtectedTestUatAccess.CurrentWorkerGrant() : null;
        if (access.IsProtectedTestUatRoleFixture && protectedTestUatGrant is null)
            return Forbidden("generate");
        if (current.Status == "confirmed") return StateConflict("confirmed_record", "Reopen this confirmed SOW/GSD before generating a new scope.");
        if (current.CustomerName.Trim().Length == 0) return Results.BadRequest(new { status = "customer_required_for_generation", message = "Select or enter the customer before generating detailed scope." });
        if (!MeaningfulServiceOverview(current.ServiceOverview)) return Results.BadRequest(new
        {
            status = "service_overview_required",
            message = "Enter a meaningful multi-word Service Overview that identifies the requested technical work, expected outcome, and any known platform/version details before generating scope."
        });

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var generationLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@engagement_id::text,725));",
            connection,
            transaction))
        {
            generationLock.Parameters.AddWithValue("engagement_id", engagementId);
            await generationLock.ExecuteNonQueryAsync(cancellationToken);
        }

        // A transfer can complete while this request waits for the generation lock.
        // Revalidate the current owner and revision under the same row lock used
        // by transfers before recording any work on the former owner's behalf.
        await using (var rowLock = new NpgsqlCommand("SELECT revision FROM module025_sow_gsd_engagements WHERE engagement_id=@id FOR UPDATE", connection, transaction))
        {
            rowLock.Parameters.AddWithValue("id", engagementId);
            await rowLock.ExecuteScalarAsync(cancellationToken);
        }
        var lockedEngagement = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (lockedEngagement is null) return Results.NotFound();
        if (!access.CanWriteOwned(lockedEngagement.OwnerUserId)) return Forbidden("module025_edit");
        if (lockedEngagement.Revision != current.Revision) return RevisionConflict(lockedEngagement.Revision);
        if (!lockedEngagement.IsActive || lockedEngagement.Status is "confirmed" or "archived")
            return StateConflict("record_not_editable", "Reopen this SOW/GSD before generating scope.");

        const string activeGenerationSql = """
            SELECT queued.evidence_json->>'generationId'
            FROM module025_sow_gsd_events queued
            WHERE queued.engagement_id=@engagement_id
              AND queued.engagement_revision=@revision
              AND queued.event_type='ai_generation_queued'
              AND NOT EXISTS (
                  SELECT 1
                  FROM module025_sow_gsd_events terminal
                  WHERE terminal.engagement_id=queued.engagement_id
                    AND terminal.event_type IN ('ai_generation_completed','ai_generation_failed','ai_generation_obsolete')
                    AND terminal.evidence_json->>'generationId'=queued.evidence_json->>'generationId')
            ORDER BY queued.event_id DESC
            LIMIT 1;
            """;
        Guid? activeGenerationId = null;
        await using (var activeGeneration = new NpgsqlCommand(activeGenerationSql, connection, transaction))
        {
            activeGeneration.Parameters.AddWithValue("engagement_id", engagementId);
            activeGeneration.Parameters.AddWithValue("revision", current.Revision);
            var value = await activeGeneration.ExecuteScalarAsync(cancellationToken);
            if (value is string candidate && Guid.TryParse(candidate, out var parsed)) activeGenerationId = parsed;
        }

        if (activeGenerationId.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Json(new
            {
                status = "module025_detailed_scope_generation_queued",
                generationId = activeGenerationId.Value,
                engagementId,
                revision = current.Revision,
                terminal = false,
                stateChanged = false,
                message = "Detailed scope generation is already queued or running. This page will continue checking its durable status."
            }, statusCode: StatusCodes.Status202Accepted);
        }

        var generationId = Guid.NewGuid();
        var sourceHash = GenerationSourceHash(current);
        var correlationId = Clean(context.TraceIdentifier, 160);
        if (correlationId.Length == 0) correlationId = Guid.NewGuid().ToString("N");
        await InsertEventAsync(
            connection,
            transaction,
            engagementId,
            access.ActualUserId,
            current.Revision,
            "ai_generation_queued",
            "Detailed P/D/I/V/R scope generation queued for governed background processing.",
            new
            {
                generationId,
                sourceHash,
                contractVersion = Module025GenerationEngine.ContractVersion,
                actualUserId = access.ActualUserId,
                effectiveUserId = access.EffectiveUserId,
                access.IsAdministrator,
                access.IsSolutionArchitect,
                access.IsProtectedTestUatRoleFixture,
                protectedTestUatGrant,
                access.IsManager,
                expectedRevision = current.Revision,
                correlationId,
                queuedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Json(new
        {
            status = "module025_detailed_scope_generation_queued",
            generationId,
            engagementId,
            revision = current.Revision,
            terminal = false,
            stateChanged = true,
            correlationId,
            message = "Detailed scope generation is queued. You may keep this page open while Celar AI prepares the review draft."
        }, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> GetLatestGenerationAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var readable = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (readable.Error is not null) return readable.Error;
        await using var connection = readable.Connection!;
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var snapshot = await ReadGenerationSnapshotAsync(connection, engagementId, context, cancellationToken);
        if (snapshot.Error is not null) return snapshot.Error;
        // Discover only after the same ownership/reporting-scope authorization
        // used by direct status reads. Discovery never queues or retries work.
        await using var command = new NpgsqlCommand("""
            SELECT evidence_json->>'generationId' FROM module025_sow_gsd_events
            WHERE engagement_id=@engagement_id AND event_type='ai_generation_queued'
            ORDER BY event_id DESC LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not string candidate || !Guid.TryParse(candidate, out var generationId))
            return Results.Ok(new { status = "module025_no_generation", generationId = (Guid?)null,
                engagementId, currentRevision = snapshot.Engagement!.Revision, terminal = true,
                phaseTimeline = Array.Empty<Module025PhaseTimeline>() });
        return await GenerationStatusResultAsync(connection, snapshot.Engagement!, snapshot.Access!, generationId, cancellationToken);
    }

    private static async Task<IResult> GetGenerationAsync(Guid engagementId, Guid generationId, HttpContext context, CancellationToken cancellationToken)
    {
        var readable = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (readable.Error is not null) return readable.Error;
        await using var connection = readable.Connection!;
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var snapshot = await ReadGenerationSnapshotAsync(connection, engagementId, context, cancellationToken);
        if (snapshot.Error is not null) return snapshot.Error;
        return await GenerationStatusResultAsync(connection, snapshot.Engagement!, snapshot.Access!, generationId, cancellationToken);
    }

    private static async Task<(Module025EngagementRow? Engagement, Module025AccessContext? Access, IResult? Error)> ReadGenerationSnapshotAsync(
        NpgsqlConnection connection, Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        // The engagement and its events must share one snapshot. Publication
        // increments the revision atomically with the completed event; mixing an
        // earlier engagement read with that event falsely marks success obsolete.
        // Refresh ownership/reporting authority in this same snapshot as well.
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return (null, null, SessionRequired());
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (engagement is null) return (null, null, Results.NotFound());
        if (!access.CanViewOwned(engagement.OwnerUserId)) return (null, null, Forbidden("module025_owner_scope"));
        return (engagement, access, null);
    }

    private static string GenerationSourceHash(Module025EngagementRow engagement) =>
        PulseAiPrivateRagService.CreateModule025AuthoritativeScopeSource(new CelarAiAuthoritativeScopeEvidence(
            engagement.EngagementId, engagement.Revision, engagement.EngagementNumber,
            engagement.CustomerName, engagement.ServiceOverview, engagement.UpdatedAt))?.SourceSha256 ?? string.Empty;

    private static async Task<IResult> GenerationStatusResultAsync(NpgsqlConnection connection,
        Module025EngagementRow engagement, Module025AccessContext access, Guid generationId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_type,engagement_revision,evidence_json::text,created_at
            FROM module025_sow_gsd_events
            WHERE engagement_id=@engagement_id
              AND evidence_json->>'generationId'=@generation_id
            ORDER BY event_id;
            """;
        var events = new List<Module025GenerationEvent>();
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("engagement_id", engagement.EngagementId);
            command.Parameters.AddWithValue("generation_id", generationId.ToString());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                events.Add(new Module025GenerationEvent(reader.GetString(0), reader.GetInt32(1),
                    ParseJson(reader.GetString(2), JsonValueKind.Object), reader.GetFieldValue<DateTimeOffset>(3)));
            }
        }
        if (events.Count == 0) return Results.NotFound(new { status = "module025_generation_not_found", generationId, engagementId = engagement.EngagementId });
        var first = events[0];
        var latest = events[^1];
        var editable = engagement.IsActive && engagement.Status is not ("confirmed" or "archived") && access.CanWriteOwned(engagement.OwnerUserId);
        var projected = Module025GenerationStatus.Project(events, engagement.Revision, editable, GenerationSourceHash(engagement), DateTimeOffset.UtcNow);
        var finalEvidence = projected.TerminalEvent?.Evidence ?? latest.Evidence;
        var completedPhases = projected.PhaseTimeline.Where(item => item.Status is "completed" or "resumed").Select(item => item.Phase).ToArray();
        var currentProgress = projected.Progress.LastOrDefault()?.Progress;
        var apiStatus = JsonString(finalEvidence, "apiStatus");
        var status = projected.PreviousGenerationCompleted ? "module025_previous_generation_completed"
            : projected.Stage == "obsolete" ? "module025_generation_obsolete"
            : projected.Terminal && apiStatus.Length > 0 ? apiStatus
            : projected.StartedAt.HasValue ? "module025_detailed_scope_generation_running"
            : "module025_detailed_scope_generation_queued";
        var message = projected.Stage switch
        {
            "obsolete" when projected.PreviousGenerationCompleted => "The previous generation completed successfully. This workspace has since been edited; review the current draft or generate again if the project scope changed.",
            "obsolete" => "This generation belongs to an earlier saved revision. Generate again from the current scope; the existing draft is preserved.",
            "queued" => "Waiting for the background worker. Your saved scope will be used for all five phases.",
            "preparing" => "Preparing the saved scope for Plan, Design, Implement, Validate, and Release.",
            "assembly" => "5/5 phases saved. Checking and assembling the SOW and detailed GSD for review.",
            "phase" or "retry" => $"{completedPhases.Length}/5 phases saved. {(projected.Stage == "retry" ? "Retrying" : "Generating")} {currentProgress?.Phase}.",
            _ => JsonString(finalEvidence, "message")
        };
        if (message.Length == 0) message = projected.Completed
            ? "Detailed P/D/I/V/R scope is ready for Solution Architect review."
            : "Detailed scope generation did not complete. The existing SOW/GSD draft was preserved.";
        if (projected.CanResume) message += $" {projected.ResumablePhases.Length}/5 validated phases can be reused; resume to generate the remaining work.";
        var correlationId = JsonString(finalEvidence, "correlationId");
        if (correlationId.Length == 0) correlationId = JsonString(first.Evidence, "correlationId");
        return Results.Ok(new
        {
            status, generationId, engagementId = engagement.EngagementId,
            phase = projected.Completed ? "completed" : projected.Stage == "obsolete" ? "obsolete" : projected.Terminal ? "failed" : projected.StartedAt.HasValue ? "generating" : "queued",
            projected.Stage, projected.Terminal, stateChanged = projected.Completed,
            revision = latest.Revision, currentRevision = engagement.Revision,
            correlationId, diagnosticCode = projected.Terminal ? JsonString(finalEvidence, "diagnosticCode") : string.Empty,
            targetDecisions = JsonArray(finalEvidence, "targetDecisions").ToArray(),
            failureStage = projected.Terminal ? JsonString(finalEvidence, "failureStage") : string.Empty,
            message, completedPhases, currentPhase = currentProgress?.Phase ?? string.Empty,
            currentProvider = projected.Progress.LastOrDefault(item => item.Progress.Provider.Length > 0)?.Progress.Provider ?? string.Empty,
            deadlineAt = projected.QueuedAt.AddSeconds(Module025GenerationEngine.DeadlineSeconds),
            projected.ElapsedSeconds, projected.ServerNow, projected.StartedAt, projected.CompletedAt,
            projected.PhaseTimeline, projected.ScopeMatches, projected.CanResume, projected.CanRetry,
            projected.ResumablePhases, projected.PreviousGenerationCompleted,
            maximumProviderAttempts = Module025GenerationEngine.AttemptsPerPhase * 5,
            maximumOutputTokensPerAttempt = Module025GenerationEngine.MaximumOutputTokens,
            progress = projected.Progress.Select(timed => new
            {
                timed.Progress.Stage, timed.Progress.Phase, timed.Progress.Provider, timed.Progress.Attempt, timed.Progress.DiagnosticCode,
                timed.Progress.Model, timed.Progress.InputCharacters, timed.Progress.OutputCharacters, timed.Progress.ElapsedMilliseconds,
                timed.Progress.InputTokens, timed.Progress.OutputTokens, timed.Progress.RequestedModel, timed.Progress.ReasoningTokens, timed.Progress.SowDiagnostics,
                timed.Progress.TargetDecisions, timed.RecordedAt
            }).ToArray(),
            projected.QueuedAt, updatedAt = latest.CreatedAt
        });
    }

    internal static bool HasGenerationAuthority(Module025AccessContext access, PulseAiPrivateRagAccess currentAccess,
        Module025ProtectedTestUatAccess.WorkerGrant? protectedTestUatGrant)
    {
        return !access.IsViewAs && access.ActualUserId == access.EffectiveUserId
                && currentAccess.UserId == access.EffectiveUserId
                && currentAccess.IsActive && currentAccess.CanSowPlanning
                && (currentAccess.RoleCodes.Overlaps(SolutionArchitectRoles)
                    || currentAccess.RoleCodes.Overlaps(AdministratorRoles)
                    || (access.IsProtectedTestUatRoleFixture && currentAccess.RoleCodes.Contains("MANAGER")
                        && Module025ProtectedTestUatAccess.MatchesWorkerGrant(protectedTestUatGrant)));
    }

    private static async Task<Module025GenerationExecutionOutcome> ExecuteGenerationAsync(Guid engagementId, int expectedRevision, Guid generationId, Module025AccessContext access, Module025ProtectedTestUatAccess.WorkerGrant? protectedTestUatGrant, HttpContext context, CancellationToken cancellationToken)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Module025SowGsd");
        var queueCorrelationId = Clean(context.TraceIdentifier, 160);

        Module025EngagementRow current;
        try
        {
            var opened = await OpenConnectionAsync(context, cancellationToken);
            if (opened.Error is not null)
            {
                return new(
                    StatusCodes.Status503ServiceUnavailable,
                    "module025_storage_unavailable",
                    "The Module 025 workspace storage is temporarily unavailable. The saved draft was not changed.",
                    queueCorrelationId,
                    false);
            }
            await using (var snapshotConnection = opened.Connection!)
            {
                if (!await WorkspaceSchemaReadyAsync(snapshotConnection, cancellationToken))
                {
                    return new(
                        StatusCodes.Status503ServiceUnavailable,
                        "module025_migration_required",
                        "Apply the Module 025 SOW/GSD workspace migration before using this workflow.",
                        queueCorrelationId,
                        false);
                }
                current = await LoadEngagementAsync(snapshotConnection, engagementId, cancellationToken)
                    ?? throw new InvalidOperationException("The queued Module 025 engagement no longer exists.");
                if (!access.CanWriteOwned(current.OwnerUserId))
                {
                    return new(
                        StatusCodes.Status403Forbidden,
                        "module025_forbidden",
                        "The queued identity no longer has authority to generate this SOW/GSD.",
                        queueCorrelationId,
                        false);
                }
                if (!current.IsActive || current.Status == "archived")
                {
                    return new(
                        StatusCodes.Status409Conflict,
                        "archived_record",
                        "Unarchive this SOW/GSD before changing it.",
                        queueCorrelationId,
                        false);
                }
                if (current.Revision != expectedRevision)
                {
                    return new(
                        StatusCodes.Status409Conflict,
                        "module025_revision_conflict",
                        "This SOW/GSD changed after generation was queued. Reload the latest revision before generating again.",
                        queueCorrelationId,
                        false);
                }
                if (current.Status == "confirmed")
                {
                    return new(
                        StatusCodes.Status409Conflict,
                        "confirmed_record",
                        "Reopen this confirmed SOW/GSD before generating a new scope.",
                        queueCorrelationId,
                        false);
                }
                if (current.CustomerName.Trim().Length == 0)
                {
                    return new(
                        StatusCodes.Status400BadRequest,
                        "customer_required_for_generation",
                        "Select or enter the customer before generating detailed scope.",
                        queueCorrelationId,
                        false);
                }
                if (!MeaningfulServiceOverview(current.ServiceOverview))
                {
                    return new(
                        StatusCodes.Status400BadRequest,
                        "service_overview_required",
                        "Enter a meaningful multi-word Service Overview that identifies the requested technical work, expected outcome, and any known platform/version details before generating scope.",
                        queueCorrelationId,
                        false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 generation state could not be loaded. EngagementId={EngagementId} Diagnostic={Diagnostic}", engagementId, exception.GetType().Name.ToLowerInvariant());
            return new(
                StatusCodes.Status503ServiceUnavailable,
                "module025_generation_state_unavailable",
                "The SOW/GSD generation state is temporarily unavailable. The saved draft was not changed.",
                queueCorrelationId,
                false,
                exception.GetType().Name.ToLowerInvariant());
        }

        async Task<bool> HasCurrentGenerationAuthorityAsync(CancellationToken token)
        {
            var currentAccess = await context.RequestServices.GetRequiredService<PulseAiPrivateRagRepository>()
                .LoadAccessAsync(access.EffectiveUserId, token);
            return HasGenerationAuthority(access, currentAccess, protectedTestUatGrant);
        }

        CelarAiComposeResult composition;
        using var generationDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadlineAt = context.Items["Module025Deadline"] is DateTimeOffset deadline
            ? deadline : DateTimeOffset.UtcNow.AddSeconds(Module025GenerationEngine.DeadlineSeconds);
        var remaining = deadlineAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return new(StatusCodes.Status504GatewayTimeout, "module025_ai_temporarily_unavailable",
                "The generation deadline expired. Validated phases remain available for an explicit retry.",
                queueCorrelationId, false, "module025_generation_deadline_exceeded");
        generationDeadline.CancelAfter(remaining);
        try
        {
            var enterprise = context.RequestServices.GetRequiredService<CelarAiEnterprisePlatformService>();
            var evidence = new CelarAiAuthoritativeScopeEvidence(
                current.EngagementId, current.Revision, current.EngagementNumber,
                current.CustomerName, current.ServiceOverview, current.UpdatedAt);
            var source = PulseAiPrivateRagService.CreateModule025AuthoritativeScopeSource(evidence)
                ?? throw new InvalidOperationException("module025_source_invalid");
            var journal = new Module025GenerationJournal(BuildConnectionString()!, engagementId,
                access.ActualUserId, expectedRevision, generationId, source.SourceSha256);
            var saved = await journal.LoadAsync(generationDeadline.Token);
            composition = await Module025GenerationEngine.RunAsync(evidence, saved.Checkpoints, saved.Attempts,
                (phaseEvidence, phaseToken) => enterprise.ComposeModule025SowAsync(
                access.ActualUserId,
                access.EffectiveUserId,
                new CelarAiComposeRequest(
                    Mode: "sow_draft", ProjectCode: current.EngagementNumber,
                    ProjectName: current.CustomerName, StartDate: null,
                    RequestedOutcome: BuildGenerationPrompt(current), DetailLevel: "comprehensive",
                    DiagramType: "flowchart", AllowSanitizedExternalFallback: false,
                    ProjectId: null, CapabilityCode: CelarAiCapabilityCatalog.SowGsdPlanning),
                phaseEvidence, context, phaseToken), async (progress, token) =>
                {
                    if (!await HasCurrentGenerationAuthorityAsync(token))
                        throw new UnauthorizedAccessException("module025_generation_authority_revoked");
                    await journal.PersistAsync(progress, token);
                },
                generationDeadline.Token).WaitAsync(generationDeadline.Token);
        }
        catch (Module025GenerationSourceChangedException)
        {
            return new(StatusCodes.Status409Conflict, "module025_generation_obsolete",
                "The saved scope changed or was archived. This generation stopped without changing the draft.",
                queueCorrelationId, false, "module025_generation_source_changed");
        }
        catch (UnauthorizedAccessException exception) when (exception.Message == "module025_generation_authority_revoked")
        {
            return new(StatusCodes.Status403Forbidden, "module025_forbidden",
                "Generation authority changed. The saved draft was preserved.",
                queueCorrelationId, false, "module025_generation_authority_revoked");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (generationDeadline.IsCancellationRequested)
        {
            return new(StatusCodes.Status504GatewayTimeout, "module025_ai_temporarily_unavailable",
                "Detailed scope generation exceeded its time limit. The saved SOW/GSD draft was not changed.",
                queueCorrelationId, false, "private_module025_generation_deadline_exceeded");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 detailed SOW/GSD generation failed without logging customer or Service Overview content. EngagementId={EngagementId} Diagnostic={Diagnostic}", engagementId, exception.GetType().Name.ToLowerInvariant());
            return new(
                StatusCodes.Status503ServiceUnavailable,
                "module025_ai_temporarily_unavailable",
                "The governed Celar AI route did not complete. The saved SOW/GSD draft was not changed.",
                queueCorrelationId,
                false,
                exception.GetType().Name.ToLowerInvariant());
        }

        if (!await HasCurrentGenerationAuthorityAsync(cancellationToken))
            return new(StatusCodes.Status403Forbidden, "module025_forbidden",
                "Generation authority changed. The saved draft was preserved.", queueCorrelationId, false,
                "module025_generation_authority_revoked");
        if (generationDeadline.IsCancellationRequested)
            return new(StatusCodes.Status504GatewayTimeout, "module025_ai_temporarily_unavailable",
                "The generation deadline expired before publication. Validated phases remain saved.",
                queueCorrelationId, false, "module025_generation_deadline_exceeded");

        if (composition.SowDraft is null)
            return GenerationFailureOutcome(CompositionDiagnosticCode(composition), Clean(composition.CorrelationId, 160))
                with { TargetDecisions = composition.TargetDecisions ?? [] };

        Dictionary<string, GeneratedPhase> generated;
        Dictionary<string, IReadOnlyList<Module025TaskEstimate>> generatedTasks;
        Dictionary<string, IReadOnlyList<Module025TaskEstimate>> workingTasks;
        JsonElement sowSections;
        JsonElement aiMetadata;
        try
        {
            var sowDraft = JsonSerializer.SerializeToElement(composition.SowDraft);
            var workPackages = JsonArray(sowDraft, "WorkPackages").ToArray();
            if (workPackages.Length == 0)
            {
                return new(
                    StatusCodes.Status422UnprocessableEntity,
                    "module025_ai_evidence_limited",
                    "Celar AI returned no detailed work packages. No generic P/D/I/V/R scope was substituted.",
                    Clean(composition.CorrelationId, 160),
                    false,
                    "private_sow_work_packages_missing");
            }

            generated = PhaseCodes.ToDictionary(code => code, code => new GeneratedPhase(code), StringComparer.OrdinalIgnoreCase);
            foreach (var package in workPackages)
            {
                var phaseCode = ClassifyPhase(JsonString(package, "Phase"), JsonString(package, "Name"), JsonString(package, "Description"));
                var phase = generated[phaseCode];
                phase.PackageCount += 1;
                phase.SuggestedHours += Math.Max(0m, JsonDecimal(package, "EstimatedHours") ?? 0m);
                AddDistinct(phase.Objectives, JsonString(package, "Description"));
                var packageName = JsonString(package, "Name");
                var packageDescription = JsonString(package, "Description");
                AddDistinct(phase.DetailedActivities, packageName.Length > 0 ? $"{packageName}: {packageDescription}" : packageDescription);
                AddDistinct(phase.TechnicalTasks, JsonStrings(package, "DetailedSteps"));
                AddDistinct(phase.Deliverables, JsonStrings(package, "Outputs"));
                AddDistinct(phase.CustomerResponsibilities, JsonStrings(package, "CustomerResponsibilities"));
                AddDistinct(phase.UsSignalResponsibilities, JsonStrings(package, "UsSignalResponsibilities"));
                AddDistinct(phase.Prerequisites, JsonStrings(package, "Prerequisites"));
                AddDistinct(phase.Dependencies, JsonStrings(package, "Predecessors"));
                if (JsonBoolean(package, "IsAssumption")) AddDistinct(phase.Assumptions, packageDescription);
                AddDistinct(phase.OpenQuestions, JsonStrings(package, "OpenQuestions"));
                AddDistinct(phase.AcceptanceCriteria, JsonStrings(package, "AcceptanceCriteria"));
                AddDistinct(phase.ValidationSteps, JsonStrings(package, "ValidationSteps"));
                AddDistinct(phase.Risks, JsonStrings(package, "Risks"));
                foreach (var citationId in JsonIntegers(package, "CitationIds")) phase.CitationIds.Add(citationId);
            }

            var missingPhaseCodes = generated
                .Where(item => item.Value.PackageCount == 0)
                .Select(item => item.Key)
                .ToArray();
            if (missingPhaseCodes.Length > 0)
            {
                var missingPhases = string.Join(", ", missingPhaseCodes.Select(PhaseLabel));
                return new(
                    StatusCodes.Status422UnprocessableEntity,
                    "module025_ai_evidence_limited",
                    $"Celar AI did not return complete Plan, Design, Implement, Validate, and Release coverage. Missing phase coverage: {missingPhases}. The saved SOW/GSD draft was not changed.",
                    Clean(composition.CorrelationId, 160),
                    false,
                    "private_sow_phase_coverage_incomplete");
            }

            generatedTasks = PhaseCodes.ToDictionary(code => code, code =>
                Module025TaskDrafts.FromWorkPackages(workPackages.Where(package =>
                    ClassifyPhase(JsonString(package, "Phase"), JsonString(package, "Name"), JsonString(package, "Description")) == code), code));
            foreach (var proposal in generatedTasks.Values)
                if (Module025TaskEstimates.Validate(proposal) is { } issue)
                    throw new InvalidOperationException(issue);
            workingTasks = PhaseCodes.ToDictionary(code => code, code =>
                (IReadOnlyList<Module025TaskEstimate>)Module025TaskDrafts.PreserveExisting(
                    current.Phases.FirstOrDefault(phase => phase.PhaseCode == code)?.Tasks, generatedTasks[code])
                    .Select(task => task with { Reviewed = false }).ToArray());

            sowSections = JsonSerializer.SerializeToElement(new
            {
                reviewedTasks = workingTasks,
                taskProposals = generatedTasks,
                executiveSummary = JsonString(sowDraft, "ExecutiveSummary"),
                objectives = JsonStrings(sowDraft, "Objectives"),
                inScope = JsonStrings(sowDraft, "InScope"),
                outOfScope = JsonStrings(sowDraft, "OutOfScope"),
                deliverables = JsonStrings(sowDraft, "Deliverables"),
                customerResponsibilities = JsonStrings(sowDraft, "CustomerResponsibilities"),
                usSignalResponsibilities = JsonStrings(sowDraft, "UsSignalResponsibilities"),
                assumptions = JsonStrings(sowDraft, "Assumptions"),
                dependencies = JsonStrings(sowDraft, "Dependencies"),
                acceptanceCriteria = JsonStrings(sowDraft, "AcceptanceCriteria"),
                timelineAndMilestones = JsonStrings(sowDraft, "TimelineAndMilestones"),
                risks = JsonStrings(sowDraft, "Risks"),
                openQuestions = JsonStrings(sowDraft, "OpenQuestions"),
                citationIds = JsonIntegers(sowDraft, "CitationIds"),
                reviewRequired = true,
                contractuallyBinding = false
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            aiMetadata = JsonSerializer.SerializeToElement(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                composition.Status,
                composition.PrimaryExecutionPath,
                composition.SelectedTarget,
                composition.AttemptedTargets,
                composition.SkippedTargets,
                composition.TargetDecisions,
                composition.Warnings,
                composition.MissingEvidence,
                composition.Conflicts,
                composition.CoverageScore,
                composition.Confidence,
                composition.ConfidenceExplanation,
                composition.CorrelationId,
                source = "service_overview_and_governed_celar_ai",
                humanReviewRequired = true,
                suggestedHoursPreservedSeparately = true
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 Celar AI output could not be materialized. EngagementId={EngagementId} CorrelationId={CorrelationId} Diagnostic={Diagnostic}", engagementId, composition.CorrelationId, exception.GetType().Name.ToLowerInvariant());
            return new(
                StatusCodes.Status502BadGateway,
                "module025_ai_output_unavailable",
                "Celar AI returned an output that could not be safely prepared for review. The saved SOW/GSD draft was not changed.",
                Clean(composition.CorrelationId, 160),
                false,
                exception.GetType().Name.ToLowerInvariant());
        }

        try
        {
            var opened = await OpenConnectionAsync(context, cancellationToken);
            if (opened.Error is not null)
            {
                return new(
                    StatusCodes.Status503ServiceUnavailable,
                    "module025_storage_unavailable",
                    "The Module 025 workspace storage is temporarily unavailable. The generated scope was not saved.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }
            await using var connection = opened.Connection!;
            if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken))
            {
                return new(
                    StatusCodes.Status503ServiceUnavailable,
                    "module025_migration_required",
                    "Apply the Module 025 SOW/GSD workspace migration before using this workflow.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }
            var latest = await LoadEngagementAsync(connection, engagementId, cancellationToken);
            if (latest is null)
            {
                return new(
                    StatusCodes.Status404NotFound,
                    "module025_engagement_not_found",
                    "The queued SOW/GSD no longer exists.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }
            if (!access.CanWriteOwned(latest.OwnerUserId))
            {
                return new(
                    StatusCodes.Status403Forbidden,
                    "module025_forbidden",
                    "The queued identity no longer has authority to generate this SOW/GSD.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }
            if (!latest.IsActive || latest.Status == "archived")
            {
                return new(
                    StatusCodes.Status409Conflict,
                    "archived_record",
                    "Unarchive this SOW/GSD before changing it.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }

            if (latest.Revision != current.Revision)
            {
                return new(
                    StatusCodes.Status409Conflict,
                    "module025_revision_conflict",
                    "This SOW/GSD changed while Celar AI was generating. Reload the latest revision before generating again.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }
            if (latest.Status == "confirmed")
            {
                return new(
                    StatusCodes.Status409Conflict,
                    "confirmed_record",
                    "This SOW/GSD was confirmed while Celar AI was generating. Reopen it before generating a new scope.",
                    Clean(composition.CorrelationId, 160),
                    false);
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var phaseCode in PhaseCodes)
            {
                var phase = generated[phaseCode];
                var objective = string.Join("\n\n", phase.DetailedActivities);
                var rationale = BuildGeneratedEffortRationale(JsonArray(JsonSerializer.SerializeToElement(composition.SowDraft), "WorkPackages"), phaseCode);
                var existingPhase = current.Phases.FirstOrDefault(item => item.PhaseCode == phaseCode);
                var phaseTasks = workingTasks[phaseCode];
                var finalHours = existingPhase?.Tasks is { Count: > 0 }
                    ? existingPhase.FinalHours
                    : phaseTasks.Count > 0 && phaseTasks.All(task => task.Hours.HasValue)
                        ? phaseTasks.Sum(task => task.Hours!.Value)
                        : decimal.Round(phase.SuggestedHours, 2, MidpointRounding.AwayFromZero);
                await SaveGeneratedPhaseAsync(connection, transaction, engagementId, phase, objective, rationale, finalHours, cancellationToken);
            }
            const string update = """
                UPDATE module025_sow_gsd_engagements
                SET sow_sections=@sow_sections::jsonb, ai_metadata=@ai_metadata::jsonb, status='review_ready', last_generated_at=NOW(), revision=revision+1
                WHERE engagement_id=@engagement_id AND revision=@expected_revision AND is_active=TRUE AND status NOT IN ('archived','confirmed')
                RETURNING revision;
                """;
            int revision;
            await using (var command = new NpgsqlCommand(update, connection, transaction))
            {
                command.Parameters.AddWithValue("engagement_id", engagementId);
                command.Parameters.AddWithValue("expected_revision", current.Revision);
                command.Parameters.AddWithValue("sow_sections", sowSections.GetRawText());
                command.Parameters.AddWithValue("ai_metadata", aiMetadata.GetRawText());
                var value = await command.ExecuteScalarAsync(cancellationToken);
                if (value is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new(
                        StatusCodes.Status409Conflict,
                        "module025_revision_conflict",
                        "This SOW/GSD changed while Celar AI was generating. Reload the latest revision before generating again.",
                        Clean(composition.CorrelationId, 160),
                        false);
                }
                revision = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision, "ai_generated", "Detailed P/D/I/V/R scope and suggested LOE generated for Solution Architect review.", new { composition.CorrelationId, composition.Confidence, missingPhaseCodes = generated.Values.Where(value => value.PackageCount == 0).Select(value => value.PhaseCode).ToArray() }, cancellationToken);
            var completionMessage = "Detailed Plan, Design, Implement, Validate, and Release scope is ready for Solution Architect review. AI-suggested hours remain separate from editable final hours.";
            await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision, "ai_generation_completed", "Durable detailed-scope generation completed and committed.", new
            {
                generationId,
                apiStatus = "module025_detailed_scope_generated",
                httpStatus = StatusCodes.Status200OK,
                revision,
                correlationId = composition.CorrelationId,
                message = completionMessage,
                targetDecisions = composition.TargetDecisions ?? [],
                completedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            Module025EngagementRow? saved = null;
            try
            {
                saved = await LoadEngagementAsync(connection, engagementId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Module 025 generated scope committed but readback failed. EngagementId={EngagementId} CorrelationId={CorrelationId} Diagnostic={Diagnostic}", engagementId, composition.CorrelationId, exception.GetType().Name.ToLowerInvariant());
            }

            return new(
                StatusCodes.Status200OK,
                "module025_detailed_scope_generated",
                saved is null
                    ? "Detailed scope was generated and saved. Reload this SOW/GSD to view the latest revision."
                    : completionMessage,
                Clean(composition.CorrelationId, 160),
                true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 generated scope could not be persisted. EngagementId={EngagementId} CorrelationId={CorrelationId} Diagnostic={Diagnostic}", engagementId, composition.CorrelationId, exception.GetType().Name.ToLowerInvariant());
            return new(
                StatusCodes.Status503ServiceUnavailable,
                "module025_generation_persistence_unavailable",
                "Celar AI completed, but the generated scope could not be saved. The existing SOW/GSD draft was preserved. Retry generation.",
                Clean(composition.CorrelationId, 160),
                false,
                exception.GetType().Name.ToLowerInvariant());
        }
    }

    private static async Task<IResult> ConfirmAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        var engagement = writable.Engagement!;
        var access = writable.Access!;
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();
        if (engagement.Status == "confirmed") return Results.Ok(new { status = "module025_already_confirmed", engagementId, engagement.Revision, stateChanged = false });
        if (!engagement.LastGeneratedAt.HasValue) return StateConflict("generation_required", "Generate and review the detailed P/D/I/V/R scope before confirmation.");
        if (engagement.CustomerName.Length == 0) return StateConflict("customer_required", "Select or manually enter the customer before confirmation.");
        if (!engagement.AccountExecutiveUserId.HasValue) return StateConflict("account_executive_required", "Select the Account Executive before confirmation.");
        if (!engagement.ResaleUserId.HasValue) return StateConflict("resale_required", "Select the Inside Sales Representative before confirmation.");
        if (engagement.Phases.Count != 5 || engagement.Phases.Any(phase => string.IsNullOrWhiteSpace(phase.Objective))) return StateConflict("phase_review_incomplete", "Review all five Plan, Design, Implement, Validate, and Release sections before confirmation.");
        if (engagement.Phases.Sum(phase => phase.FinalHours) <= 0) return StateConflict("level_of_effort_required", "The reviewed GSD must contain a positive total level of effort before confirmation.");
        var exportReadiness = Module025TaskEstimates.Readiness(engagement);
        if (exportReadiness is not null) return StateConflict("export_review_incomplete", exportReadiness);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = "UPDATE module025_sow_gsd_engagements SET status='confirmed', confirmed_at=NOW(), revision=revision+1 WHERE engagement_id=@engagement_id AND revision=@revision AND is_active=TRUE RETURNING revision;";
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        var released = await CaptureConfirmedSowVersionAsync(connection, transaction, engagementId, access.ActualUserId, cancellationToken);
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "confirmed", "Solution Architect confirmed the SOW/GSD package and retained its immutable SOW/GSD version.", new { versionId = released.Version.VersionId, versionNumber = released.Version.VersionNumber, sowSha256 = released.Version.SowSha256, gsdSha256 = released.Version.GsdSha256 }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_confirmed", engagementId, revision, canDownload = true, version = released.Version, stateChanged = true });
    }

    private static async Task<IResult> ReopenAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        var engagement = writable.Engagement!;
        var access = writable.Access!;
        if (engagement.Status != "confirmed") return Results.Ok(new { status = "module025_already_editable", engagementId, engagement.Revision, stateChanged = false });
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = "UPDATE module025_sow_gsd_engagements SET status='review_ready', revision=revision+1 WHERE engagement_id=@engagement_id AND revision=@revision AND status='confirmed' RETURNING revision;";
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "reopened", "Confirmed SOW/GSD reopened for Solution Architect edits and reconfirmation.", new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_reopened", engagementId, revision, stateChanged = true });
    }

    private static async Task<IResult> DeleteDraftAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        return await DeleteDraftOwnershipCheckedAsync(connection, engagementId, writable.Engagement!.Revision, writable.Access!, cancellationToken);
    }

    private static async Task<IResult> DeleteDraftOwnershipCheckedAsync(NpgsqlConnection connection, Guid engagementId,
        int expectedRevision, Module025AccessContext access, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var generationLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@id::text,725));", connection, transaction))
        {
            generationLock.Parameters.AddWithValue("id", engagementId);
            await generationLock.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var recordLock = new NpgsqlCommand("SELECT engagement_id FROM module025_sow_gsd_engagements WHERE engagement_id=@id FOR UPDATE;", connection, transaction))
        {
            recordLock.Parameters.AddWithValue("id", engagementId);
            if (await recordLock.ExecuteScalarAsync(cancellationToken) is null) return Results.NotFound();
        }
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken, transaction);
        if (engagement is null) return Results.NotFound();
        if (!access.CanWriteOwned(engagement.OwnerUserId)) return Forbidden("module025_edit");
        if (engagement.Revision != expectedRevision) return RevisionConflict(engagement.Revision);
        if (!engagement.IsActive || engagement.Status != "draft" || engagement.LastGeneratedAt.HasValue)
            return StateConflict("draft_delete_not_allowed",
                "Only an active draft that has never completed detailed-scope generation can be deleted. Archive generated or reviewed records instead.");
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return SowSellMigrationRequired();

        const string eligibilitySql = """
            SELECT
                EXISTS(
                    SELECT 1 FROM module025_sow_gsd_generation_snapshots
                    WHERE engagement_id=@engagement_id
                )
                OR EXISTS(
                    SELECT 1 FROM module025_sow_gsd_versions
                    WHERE engagement_id=@engagement_id
                )
                OR EXISTS(
                    SELECT 1 FROM module025_sow_sell_submissions
                    WHERE engagement_id=@engagement_id
                )
                OR EXISTS(
                    SELECT 1 FROM module025_sow_gsd_events
                    WHERE engagement_id=@engagement_id AND event_type='ownership_transferred'
                ) AS has_retained_evidence,
                EXISTS(
                    SELECT 1
                    FROM module025_sow_gsd_events queued
                    WHERE queued.engagement_id=@engagement_id
                      AND queued.event_type='ai_generation_queued'
                      AND NOT EXISTS (
                          SELECT 1
                          FROM module025_sow_gsd_events terminal
                          WHERE terminal.engagement_id=queued.engagement_id
                            AND terminal.event_type IN ('ai_generation_completed','ai_generation_failed','ai_generation_obsolete')
                            AND terminal.evidence_json->>'generationId'=queued.evidence_json->>'generationId'
                      )
                ) AS generation_active;
            """;
        bool hasEvidence;
        bool generationActive;
        await using (var command = new NpgsqlCommand(eligibilitySql, connection, transaction))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return Results.Json(new { status = "module025_delete_eligibility_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            hasEvidence = reader.GetBoolean(0);
            generationActive = reader.GetBoolean(1);
        }

        if (generationActive)
            return StateConflict("generation_in_progress", "Wait for the current detailed-scope generation to stop before deleting this draft.");
        if (hasEvidence)
            return StateConflict("retained_evidence_exists",
                "This SOW/GSD has retained generation, release, or ownership-transfer evidence and cannot be deleted. Archive it instead.");

        await using (var guard = new NpgsqlCommand(
            "SELECT set_config('projectpulse.module025_allow_draft_delete','on',true);",
            connection, transaction))
        {
            await guard.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var events = new NpgsqlCommand(
            "DELETE FROM module025_sow_gsd_events WHERE engagement_id=@engagement_id;",
            connection, transaction))
        {
            events.Parameters.AddWithValue("engagement_id", engagementId);
            await events.ExecuteNonQueryAsync(cancellationToken);
        }
        int deleted;
        await using (var root = new NpgsqlCommand(
            "DELETE FROM module025_sow_gsd_engagements WHERE engagement_id=@engagement_id AND status='draft' AND is_active=TRUE AND last_generated_at IS NULL;",
            connection, transaction))
        {
            root.Parameters.AddWithValue("engagement_id", engagementId);
            deleted = await root.ExecuteNonQueryAsync(cancellationToken);
        }
        if (deleted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StateConflict("draft_delete_conflict", "The draft changed before it could be deleted. Reload and try again.");
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            status = "module025_draft_deleted",
            engagementId,
            engagementNumber = engagement.EngagementNumber,
            stateChanged = true
        });
    }

    private static async Task<IResult> ArchiveAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        var engagement = writable.Engagement!;
        var access = writable.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = "UPDATE module025_sow_gsd_engagements SET status='archived', is_active=FALSE, archived_at=NOW(), revision=revision+1 WHERE engagement_id=@engagement_id AND revision=@revision AND is_active=TRUE RETURNING revision;";
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "archived", "SOW/GSD removed from the active work queue.", new { priorStatus = engagement.Status }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_archived", engagementId, revision, stateChanged = true });
    }

    private static async Task<IResult> UnarchiveAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken, allowArchived: true);
        if (writable.Error is not null) return writable.Error;
        await using var connection = writable.Connection!;
        var engagement = writable.Engagement!;
        var access = writable.Access!;
        if (engagement.IsActive && engagement.Status != "archived") return Results.Ok(new { status = "module025_already_active", engagementId, engagement.Revision, stateChanged = false });
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = "UPDATE module025_sow_gsd_engagements SET status=CASE WHEN last_generated_at IS NULL THEN 'draft' ELSE 'review_ready' END, is_active=TRUE, archived_at=NULL, revision=revision+1 WHERE engagement_id=@engagement_id AND revision=@revision AND is_active=FALSE RETURNING revision;";
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "unarchived", "SOW/GSD returned to the active work queue for review.", new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_unarchived", engagementId, revision, stateChanged = true });
    }

    private static async Task<IResult> DownloadSowAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var readable = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (readable.Error is not null) return readable.Error;
        await using var connection = readable.Connection!;
        var engagement = readable.Engagement!;
        if (engagement.Status != "confirmed") return StateConflict("confirmation_required", "Confirm the reviewed SOW/GSD before downloading customer documents.");
        return Results.File(Module025SowGsdDocumentExporter.CreateSowDocx(BuildDocumentModel(engagement)), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", DocumentFileName(engagement, "SOW", ".docx"));
    }

    private static async Task<IResult> DownloadGsdAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var readable = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (readable.Error is not null) return readable.Error;
        await using var connection = readable.Connection!;
        var engagement = readable.Engagement!;
        if (engagement.Status != "confirmed") return StateConflict("confirmation_required", "Confirm the reviewed SOW/GSD before downloading customer documents.");
        return Results.File(Module025SowGsdDocumentExporter.CreateGsdXlsx(BuildDocumentModel(engagement)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", DocumentFileName(engagement, "GSD", ".xlsx"));
    }

    private static async Task<IResult> DownloadDraftAsync(Guid engagementId, bool sow, HttpContext context, CancellationToken cancellationToken)
    {
        var readable = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (readable.Error is not null) return readable.Error;
        await using var connection = readable.Connection!;
        var engagement = readable.Engagement!;
        if (!engagement.IsActive || engagement.Status is "confirmed" or "archived")
            return StateConflict("draft_required", "Use the retained version downloads for confirmed or archived records.");
        context.Response.Headers.CacheControl = "no-store";
        var model = BuildDocumentModel(engagement);
        return Results.File(sow ? Module025SowGsdDocumentExporter.CreateSowDocx(model, draft: true) : Module025SowGsdDocumentExporter.CreateGsdXlsx(model, draft: true),
            sow ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            DocumentFileName(engagement, sow ? "DRAFT_SOW" : "DRAFT_GSD", sow ? ".docx" : ".xlsx"));
    }

    internal static string DocumentFileName(Module025EngagementRow engagement, string artifact, string extension)
    {
        var sowNumber = engagement.EngagementNumber.StartsWith("SOW-", StringComparison.OrdinalIgnoreCase)
            ? engagement.EngagementNumber[4..]
            : engagement.EngagementNumber;
        var project = SafeFileName(engagement.ProjectName ?? string.Empty);
        if (project.Length == 0) project = "Project";
        return $"SOW#{SafeFileName(sowNumber)}_{project}_{artifact}{extension}";
    }

    private static Module025DocumentModel BuildDocumentModel(Module025EngagementRow engagement) => new(engagement, engagement.Phases.OrderBy(phase => phase.SortOrder).ToArray(), engagement.Phases.Sum(phase => phase.SuggestedHours), engagement.Phases.Sum(phase => phase.FinalHours));

    private static async Task SaveHumanPhaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, string phaseCode, Module025SowGsdPhaseSaveRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE module025_sow_gsd_phases
            SET final_hours=@final_hours, objective=@objective, detailed_activities=@detailed_activities::jsonb,
                technical_tasks=@technical_tasks::jsonb, deliverables=@deliverables::jsonb,
                customer_responsibilities=@customer_responsibilities::jsonb, us_signal_responsibilities=@us_signal_responsibilities::jsonb,
                prerequisites=@prerequisites::jsonb, dependencies=@dependencies::jsonb, assumptions=@assumptions::jsonb,
                open_questions=@open_questions::jsonb, acceptance_criteria=@acceptance_criteria::jsonb,
                validation_steps=@validation_steps::jsonb, risks=@risks::jsonb, loe_rationale=@loe_rationale, updated_at=NOW()
            WHERE engagement_id=@engagement_id AND phase_code=@phase_code;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("phase_code", phaseCode);
        command.Parameters.AddWithValue("final_hours", Math.Max(0m, request.FinalHours ?? 0m));
        command.Parameters.AddWithValue("objective", Clean(request.Objective, 12_000));
        command.Parameters.AddWithValue("detailed_activities", JsonSerializer.Serialize(CleanList(request.DetailedActivities)));
        command.Parameters.AddWithValue("technical_tasks", JsonSerializer.Serialize(CleanList(request.TechnicalTasks)));
        command.Parameters.AddWithValue("deliverables", JsonSerializer.Serialize(CleanList(request.Deliverables)));
        command.Parameters.AddWithValue("customer_responsibilities", JsonSerializer.Serialize(CleanList(request.CustomerResponsibilities)));
        command.Parameters.AddWithValue("us_signal_responsibilities", JsonSerializer.Serialize(CleanList(request.UsSignalResponsibilities)));
        command.Parameters.AddWithValue("prerequisites", JsonSerializer.Serialize(CleanList(request.Prerequisites)));
        command.Parameters.AddWithValue("dependencies", JsonSerializer.Serialize(CleanList(request.Dependencies)));
        command.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(CleanList(request.Assumptions)));
        command.Parameters.AddWithValue("open_questions", JsonSerializer.Serialize(CleanList(request.OpenQuestions)));
        command.Parameters.AddWithValue("acceptance_criteria", JsonSerializer.Serialize(CleanList(request.AcceptanceCriteria)));
        command.Parameters.AddWithValue("validation_steps", JsonSerializer.Serialize(CleanList(request.ValidationSteps)));
        command.Parameters.AddWithValue("risks", JsonSerializer.Serialize(CleanList(request.Risks)));
        command.Parameters.AddWithValue("loe_rationale", Clean(request.LoeRationale, 12_000));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (request.Tasks is not null)
        {
            const string tasksSql = """
                UPDATE module025_sow_gsd_engagements
                SET sow_sections=jsonb_set(sow_sections, '{reviewedTasks}',
                    COALESCE(sow_sections->'reviewedTasks','{}'::jsonb) || jsonb_build_object(@phase_code,@tasks::jsonb),true)
                WHERE engagement_id=@engagement_id;
                """;
            await using var taskCommand = new NpgsqlCommand(tasksSql, connection, transaction);
            taskCommand.Parameters.AddWithValue("engagement_id", engagementId);
            taskCommand.Parameters.AddWithValue("phase_code", phaseCode);
            taskCommand.Parameters.AddWithValue("tasks", JsonSerializer.Serialize(request.Tasks, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await taskCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static string BuildGeneratedEffortRationale(IEnumerable<JsonElement> workPackages, string phaseCode)
    {
        var packages = workPackages.Where(package => ClassifyPhase(JsonString(package, "Phase"),
            JsonString(package, "Name"), JsonString(package, "Description")) == phaseCode).ToArray();
        if (packages.Length == 0) return "No work-package estimate is available for this phase.";
        var total = packages.Sum(package => Math.Max(0m, JsonDecimal(package, "EstimatedHours") ?? 0m));
        var parts = new List<string> { $"{PhaseLabel(phaseCode)} estimate: {total.ToString("0.##", CultureInfo.InvariantCulture)} hours across {packages.Length} work packages." };
        foreach (var package in packages)
        {
            var hours = Math.Max(0m, JsonDecimal(package, "EstimatedHours") ?? 0m);
            // The phase prompt requests the effort basis in Description. Preserve
            // that explanation alongside the exact hours instead of replacing it
            // with a generic provider-branded disclaimer or an invented formula.
            var detail = $"{JsonString(package, "Wbs")} {JsonString(package, "Name")} ({hours.ToString("0.##", CultureInfo.InvariantCulture)} hours): {JsonString(package, "Description")}";
            var steps = JsonStrings(package, "DetailedSteps");
            if (steps.Count > 0) detail += "\nIncluded work: " + string.Join("; ", steps);
            var prerequisites = JsonStrings(package, "Prerequisites");
            if (prerequisites.Count > 0) detail += "\nPrerequisites: " + string.Join("; ", prerequisites);
            var questions = JsonStrings(package, "OpenQuestions");
            if (questions.Count > 0) detail += "\nUnresolved inputs affecting effort: " + string.Join("; ", questions);
            parts.Add(detail);
        }
        parts.Add("These are proposed labor hours, not elapsed duration or a customer commitment. Validate the work-package estimates and unresolved inputs before approving final hours.");
        return string.Join("\n\n", parts);
    }

    private static async Task SaveGeneratedPhaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, GeneratedPhase phase, string objective, string rationale, decimal finalHours, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE module025_sow_gsd_phases
            SET final_hours=@final_hours,
                suggested_hours=@suggested_hours, objective=@objective, detailed_activities=@detailed_activities::jsonb,
                technical_tasks=@technical_tasks::jsonb, deliverables=@deliverables::jsonb,
                customer_responsibilities=@customer_responsibilities::jsonb, us_signal_responsibilities=@us_signal_responsibilities::jsonb,
                prerequisites=@prerequisites::jsonb, dependencies=@dependencies::jsonb, assumptions=@assumptions::jsonb,
                open_questions=@open_questions::jsonb, acceptance_criteria=@acceptance_criteria::jsonb,
                validation_steps=@validation_steps::jsonb, risks=@risks::jsonb, loe_rationale=@loe_rationale,
                source_citation_ids=@source_citation_ids::jsonb, ai_generated=TRUE, updated_at=NOW()
            WHERE engagement_id=@engagement_id AND phase_code=@phase_code;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("phase_code", phase.PhaseCode);
        command.Parameters.AddWithValue("suggested_hours", phase.SuggestedHours);
        command.Parameters.AddWithValue("final_hours", finalHours);
        command.Parameters.AddWithValue("objective", Clean(objective, 12_000));
        command.Parameters.AddWithValue("detailed_activities", JsonSerializer.Serialize(phase.DetailedActivities));
        command.Parameters.AddWithValue("technical_tasks", JsonSerializer.Serialize(phase.TechnicalTasks));
        command.Parameters.AddWithValue("deliverables", JsonSerializer.Serialize(phase.Deliverables));
        command.Parameters.AddWithValue("customer_responsibilities", JsonSerializer.Serialize(phase.CustomerResponsibilities));
        command.Parameters.AddWithValue("us_signal_responsibilities", JsonSerializer.Serialize(phase.UsSignalResponsibilities));
        command.Parameters.AddWithValue("prerequisites", JsonSerializer.Serialize(phase.Prerequisites));
        command.Parameters.AddWithValue("dependencies", JsonSerializer.Serialize(phase.Dependencies));
        command.Parameters.AddWithValue("assumptions", JsonSerializer.Serialize(phase.Assumptions));
        command.Parameters.AddWithValue("open_questions", JsonSerializer.Serialize(phase.OpenQuestions));
        command.Parameters.AddWithValue("acceptance_criteria", JsonSerializer.Serialize(phase.AcceptanceCriteria));
        command.Parameters.AddWithValue("validation_steps", JsonSerializer.Serialize(phase.ValidationSteps));
        command.Parameters.AddWithValue("risks", JsonSerializer.Serialize(phase.Risks));
        command.Parameters.AddWithValue("loe_rationale", rationale);
        command.Parameters.AddWithValue("source_citation_ids", JsonSerializer.Serialize(phase.CitationIds));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEmptyPhasesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO module025_sow_gsd_phases(engagement_id,phase_code,sort_order) VALUES(@engagement_id,@phase_code,@sort_order) ON CONFLICT(engagement_id,phase_code) DO NOTHING;";
        for (var index = 0; index < PhaseCodes.Length; index++)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("engagement_id", engagementId);
            command.Parameters.AddWithValue("phase_code", PhaseCodes[index]);
            command.Parameters.AddWithValue("sort_order", index + 1);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<Module025EngagementRow?> LoadEngagementAsync(NpgsqlConnection connection, Guid engagementId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT engagement_id, engagement_number, owner_user_id, owner_display_name, owner_department_name, owner_team_name,
                   customer_id, customer_name, customer_entry_mode, commercial_model, customer_program, gsd_template_key,
                   account_executive_user_id, account_executive_name, resale_user_id, resale_name, service_overview, project_name,
                   sow_sections::text, ai_metadata::text, status, is_active, revision, last_generated_at, confirmed_at, archived_at, created_at, updated_at
            FROM module025_sow_gsd_engagements WHERE engagement_id=@engagement_id;
            """;
        Module025EngagementRow? shell;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            shell = new Module025EngagementRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetGuid(12), reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetGuid(14), reader.GetString(15), reader.GetString(16), reader.GetString(17),
                ParseJson(reader.GetString(18), JsonValueKind.Object), ParseJson(reader.GetString(19), JsonValueKind.Object), reader.GetString(20), reader.GetBoolean(21), reader.GetInt32(22),
                NullableTimestamp(reader, 23), NullableTimestamp(reader, 24), NullableTimestamp(reader, 25), reader.GetFieldValue<DateTimeOffset>(26), reader.GetFieldValue<DateTimeOffset>(27), Array.Empty<Module025PhaseRow>());
        }
        var phases = await LoadPhasesAsync(connection, engagementId, cancellationToken, transaction);
        return shell with { Phases = phases.Select(p => p with { Tasks = Module025TaskEstimates.Read(shell.SowSections, p.PhaseCode) }).ToArray() };
    }

    private static async Task<IReadOnlyList<Module025PhaseRow>> LoadPhasesAsync(NpgsqlConnection connection, Guid engagementId, CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT phase_code, sort_order, suggested_hours, final_hours, objective, detailed_activities::text, technical_tasks::text,
                   deliverables::text, customer_responsibilities::text, us_signal_responsibilities::text, prerequisites::text,
                   dependencies::text, assumptions::text, open_questions::text, acceptance_criteria::text, validation_steps::text,
                   risks::text, loe_rationale, source_citation_ids::text, ai_generated, updated_at
            FROM module025_sow_gsd_phases WHERE engagement_id=@engagement_id ORDER BY sort_order;
            """;
        var rows = new List<Module025PhaseRow>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Module025PhaseRow(
                reader.GetString(0), reader.GetInt16(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetString(4),
                ParseStringArray(reader.GetString(5)), ParseStringArray(reader.GetString(6)), ParseStringArray(reader.GetString(7)), ParseStringArray(reader.GetString(8)),
                ParseStringArray(reader.GetString(9)), ParseStringArray(reader.GetString(10)), ParseStringArray(reader.GetString(11)), ParseStringArray(reader.GetString(12)),
                ParseStringArray(reader.GetString(13)), ParseStringArray(reader.GetString(14)), ParseStringArray(reader.GetString(15)), ParseStringArray(reader.GetString(16)),
                reader.GetString(17), ParseIntArray(reader.GetString(18)), reader.GetBoolean(19), reader.GetFieldValue<DateTimeOffset>(20)));
        }
        return rows;
    }

    private static object PublicEngagement(Module025EngagementRow engagement, Module025AccessContext access) => new
    {
        status = "module025_engagement_loaded",
        contract = WorkspaceContract,
        engagement = new
        {
            engagement.EngagementId, engagement.EngagementNumber, engagement.OwnerUserId, engagement.OwnerDisplayName, engagement.OwnerDepartmentName, engagement.OwnerTeamName,
            engagement.CustomerId, engagement.CustomerName, engagement.CustomerEntryMode, engagement.CommercialModel, engagement.CustomerProgram, engagement.GsdTemplateKey,
            gsdTemplate = engagement.GsdTemplateKey == Module025SowGsdDocumentExporter.HaeaGsdTemplateKey ? Module025SowGsdDocumentExporter.HaeaGsdDisplayName : "Standard GSD",
            engagement.AccountExecutiveUserId, engagement.AccountExecutiveName, engagement.ResaleUserId, engagement.ResaleName, engagement.ServiceOverview, engagement.ProjectName,
            engagement.SowSections, engagement.AiMetadata, engagement.Status, engagement.IsActive, engagement.Revision, engagement.LastGeneratedAt, engagement.ConfirmedAt,
            engagement.ArchivedAt, engagement.CreatedAt, engagement.UpdatedAt,
            suggestedHours = engagement.Phases.Sum(phase => phase.SuggestedHours), finalHours = engagement.Phases.Sum(phase => phase.FinalHours),
            phases = engagement.Phases.Select(phase => new
            {
                phase.PhaseCode, label = PhaseLabel(phase.PhaseCode), phase.SortOrder, phase.SuggestedHours, phase.FinalHours, phase.Objective,
                phase.DetailedActivities, phase.TechnicalTasks, phase.Deliverables, phase.CustomerResponsibilities, phase.UsSignalResponsibilities,
                phase.Prerequisites, phase.Dependencies, phase.Assumptions, phase.OpenQuestions, phase.AcceptanceCriteria, phase.ValidationSteps,
                phase.Risks, phase.LoeRationale, phase.SourceCitationIds, phase.AiGenerated, phase.UpdatedAt, phase.Tasks
            }).ToArray()
        },
        access = new
        {
            canEdit = access.CanWriteOwned(engagement.OwnerUserId) && engagement.IsActive && engagement.Status != "archived",
            canConfirm = access.CanWriteOwned(engagement.OwnerUserId) && engagement.IsActive && engagement.Status != "archived",
            canArchive = access.CanWriteOwned(engagement.OwnerUserId),
            canDownload = access.CanViewOwned(engagement.OwnerUserId) && engagement.Status == "confirmed",
            readOnlyManagerView = access.IsManager && !access.IsAdministrator && engagement.OwnerUserId != access.EffectiveUserId,
            protectedTestUatRoleFixture = access.IsProtectedTestUatRoleFixture,
            access.IsViewAs
        },
        stateChanged = false
    };

    private static async Task<Module025AccessContext?> ResolveAccessAsync(NpgsqlConnection connection, HttpContext context, CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId") ?? actual;
        if (!actual.HasValue || !effective.HasValue) return null;
        const string sql = """
            SELECT COALESCE(NULLIF(app_user.display_name,''),app_user.email,''), COALESCE(app_user.email,''),
                   COALESCE(NULLIF(app_user.department_name,''),NULLIF(app_user.department,''),''), COALESCE(NULLIF(app_user.team_name,''),''),
                   COALESCE(string_agg(DISTINCT upper(role.role_code),',' ORDER BY upper(role.role_code)),'')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            LEFT JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.user_id=@user_id AND app_user.is_active=TRUE
            GROUP BY app_user.user_id,app_user.display_name,app_user.email,app_user.department_name,app_user.department,app_user.team_name;
            """;
        string displayName; string email; string department; string team; IReadOnlySet<string> roles;
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("user_id", effective.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            displayName = reader.GetString(0); email = reader.GetString(1); department = reader.GetString(2); team = reader.GetString(3); roles = Split(reader.GetString(4));
        }
        var administrator = roles.Overlaps(AdministratorRoles);
        var protectedTestUatRoleFixture = Module025ProtectedTestUatAccess.Authorizes(
            context, actual.Value, effective.Value, email, roles);
        var solutionArchitect = roles.Overlaps(SolutionArchitectRoles) || protectedTestUatRoleFixture;
        var visibleIds = administrator ? await LoadAllSolutionArchitectIdsAsync(connection, cancellationToken) : await LoadDirectReportSolutionArchitectIdsAsync(connection, effective.Value, department, cancellationToken);
        var manager = roles.Overlaps(ManagerRoles) || visibleIds.Count > 0;
        var visible = new HashSet<Guid>(visibleIds);
        if (solutionArchitect) visible.Add(effective.Value);
        return new Module025AccessContext(actual.Value, effective.Value, displayName, email, department, team, roles, ProjectPulseActualSessionAuthority.IsViewAs(context) || actual.Value != effective.Value, administrator, solutionArchitect, protectedTestUatRoleFixture, manager, visible);
    }

    private static async Task<IReadOnlySet<Guid>> LoadDirectReportSolutionArchitectIdsAsync(NpgsqlConnection connection, Guid managerUserId, string departmentName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT employee.user_id
            FROM reporting_relationships relationship
            JOIN app_users employee ON employee.user_id=relationship.employee_user_id AND employee.is_active=TRUE
            JOIN app_user_role_assignments assignment ON assignment.user_id=employee.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE (relationship.manager_user_id=@manager_user_id OR relationship.team_lead_user_id=@manager_user_id)
              AND relationship.effective_start_date<=CURRENT_DATE AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
              AND upper(role.role_code)=ANY(@roles)
              AND (@department_name='' OR lower(COALESCE(NULLIF(employee.department_name,''),employee.department,''))=lower(@department_name));
            """;
        var rows = new HashSet<Guid>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("manager_user_id", managerUserId); command.Parameters.AddWithValue("roles", SolutionArchitectRoles.ToArray()); command.Parameters.AddWithValue("department_name", departmentName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(reader.GetGuid(0));
        return rows;
    }

    private static async Task<IReadOnlySet<Guid>> LoadAllSolutionArchitectIdsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT DISTINCT app_user.user_id FROM app_users app_user JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE WHERE app_user.is_active=TRUE AND upper(role.role_code)=ANY(@roles);";
        var rows = new HashSet<Guid>();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("roles", SolutionArchitectRoles.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) rows.Add(reader.GetGuid(0));
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadVisibleSolutionArchitectsAsync(NpgsqlConnection connection, Module025AccessContext access, CancellationToken cancellationToken)
    {
        var ids = access.VisibleSolutionArchitectIds.ToArray();
        if (ids.Length == 0) return Array.Empty<object>();
        const string sql = """
            SELECT DISTINCT app_user.user_id, COALESCE(NULLIF(app_user.display_name,''),app_user.email,''), COALESCE(app_user.email,''),
                   COALESCE(NULLIF(app_user.department_name,''),NULLIF(app_user.department,''),''), COALESCE(app_user.team_name,'')
            FROM app_users app_user
            JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.user_id=ANY(@ids) AND app_user.is_active=TRUE AND upper(role.role_code)=ANY(@roles)
            ORDER BY 2,3;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("ids", ids); command.Parameters.AddWithValue("roles", SolutionArchitectRoles.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new { userId = reader.GetGuid(0), displayName = reader.GetString(1), email = reader.GetString(2), departmentName = reader.GetString(3), teamName = reader.GetString(4) });
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadCustomersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<object>();
        await using var command = new NpgsqlCommand("SELECT client_id,COALESCE(client_code,''),client_name FROM clients WHERE is_active=TRUE ORDER BY client_name LIMIT 1000;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new { customerId = reader.GetGuid(0), customerCode = reader.GetString(1), customerName = reader.GetString(2) });
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadPeopleByRoleAsync(NpgsqlConnection connection, IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT app_user.user_id, COALESCE(NULLIF(app_user.display_name,''),app_user.email,''), COALESCE(app_user.email,'')
            FROM app_users app_user JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.is_active=TRUE AND upper(role.role_code)=ANY(@roles) ORDER BY 2,3;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("roles", roleCodes.Select(value => value.ToUpperInvariant()).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) rows.Add(new { userId = reader.GetGuid(0), displayName = reader.GetString(1), email = reader.GetString(2) });
        return rows;
    }

    private static async Task<CustomerSelection> ResolveCustomerAsync(NpgsqlConnection connection, Guid? customerId, string? customerName, string? requestedMode, CancellationToken cancellationToken)
    {
        var mode = string.Equals(requestedMode?.Trim(), "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "directory";
        if (mode == "manual")
        {
            var manualName = Clean(customerName, 500);
            return manualName.Length == 0 ? new CustomerSelection(null, string.Empty, mode, Results.BadRequest(new { status = "manual_customer_name_required", message = "Enter the customer name when Customer not listed is selected." })) : new CustomerSelection(null, manualName, mode, null);
        }
        if (!customerId.HasValue) return new CustomerSelection(null, Clean(customerName, 500), mode, null);
        await using var command = new NpgsqlCommand("SELECT client_name FROM clients WHERE client_id=@client_id AND is_active=TRUE;", connection); command.Parameters.AddWithValue("client_id", customerId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string name ? new CustomerSelection(customerId, name, mode, null) : new CustomerSelection(null, string.Empty, mode, Results.BadRequest(new { status = "customer_not_found", message = "The selected customer is not an active customer-directory record." }));
    }

    private static async Task<PersonSelection> ResolvePersonAsync(NpgsqlConnection connection, Guid? userId, IReadOnlyList<string> roleCodes, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return new PersonSelection(null, string.Empty);
        const string sql = """
            SELECT COALESCE(NULLIF(app_user.display_name,''),app_user.email,'')
            FROM app_users app_user
            WHERE app_user.user_id=@user_id AND app_user.is_active=TRUE AND EXISTS(
                SELECT 1 FROM app_user_role_assignments assignment JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                WHERE assignment.user_id=app_user.user_id AND assignment.is_active=TRUE AND upper(role.role_code)=ANY(@roles));
            """;
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("user_id", userId.Value); command.Parameters.AddWithValue("roles", roleCodes.Select(value => value.ToUpperInvariant()).ToArray());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName ? new PersonSelection(userId, displayName) : new PersonSelection(null, string.Empty);
    }

    internal const string GenerationCandidateSql = """
            SELECT queued.event_id,queued.engagement_id,queued.actor_user_id,
                   queued.engagement_revision,queued.evidence_json::text
            FROM module025_sow_gsd_events queued
            WHERE queued.event_type='ai_generation_queued'
              AND (
                  COALESCE(queued.evidence_json->>'IsProtectedTestUatRoleFixture',
                           queued.evidence_json->>'isProtectedTestUatRoleFixture','false') <> 'true'
                  OR (@uat_grant <> 'null' AND queued.evidence_json->'protectedTestUatGrant' = @uat_grant::jsonb))
              AND NOT EXISTS (
                  SELECT 1
                  FROM module025_sow_gsd_events terminal
                  WHERE terminal.engagement_id=queued.engagement_id
                    AND terminal.event_type IN ('ai_generation_completed','ai_generation_failed','ai_generation_obsolete')
                    AND terminal.evidence_json->>'generationId'=queued.evidence_json->>'generationId')
            ORDER BY queued.event_id
            LIMIT 12;
            """;

    internal static async Task<bool> ProcessNextQueuedGenerationAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        // The advisory lock connection intentionally lives for the complete
        // private-model call. Keep it alive across that idle interval, but use
        // short-lived connections for every durable event write.
        await using var lockConnection = new NpgsqlConnection(WorkerLockConnectionString(connectionString));
        await lockConnection.OpenAsync(cancellationToken);
        if (!await WorkspaceSchemaReadyAsync(lockConnection, cancellationToken)) return false;

        var candidates = new List<Module025QueuedGeneration>();
        await using (var command = new NpgsqlCommand(GenerationCandidateSql, lockConnection))
        {
            // Filter before LIMIT so stale fixture jobs cannot starve ordinary
            // Solution Architect work on a shared database during revision rollout.
            command.Parameters.AddWithValue("uat_grant", JsonSerializer.Serialize(Module025ProtectedTestUatAccess.CurrentWorkerGrant()));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new Module025QueuedGeneration(
                    reader.GetInt64(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetInt32(3),
                    ParseJson(reader.GetString(4), JsonValueKind.Object)));
            }
        }

        foreach (var candidate in candidates)
        {
            Module025ProtectedTestUatAccess.WorkerGrant? protectedTestUatGrant = null;
            if (JsonBoolean(candidate.Evidence, "isProtectedTestUatRoleFixture"))
            {
                if (!TryJsonProperty(candidate.Evidence, "protectedTestUatGrant", out var grantJson)) continue;
                try { protectedTestUatGrant = grantJson.Deserialize<Module025ProtectedTestUatAccess.WorkerGrant>(); }
                catch (JsonException) { continue; }
                if (!Module025ProtectedTestUatAccess.MatchesWorkerGrant(protectedTestUatGrant)) continue;
            }
            var generationIdText = JsonString(candidate.Evidence, "generationId");
            if (!Guid.TryParse(generationIdText, out var generationId)) continue;
            if (!await TryLockGenerationAsync(lockConnection, generationId, cancellationToken)) continue;
            var failureStage = "generation_lock_acquired";

            try
            {
                if (await HasGenerationTerminalAsync(lockConnection, candidate.EngagementId, generationId, cancellationToken)) return true;

                var effectiveUserIdText = JsonString(candidate.Evidence, "effectiveUserId");
                if (!Guid.TryParse(effectiveUserIdText, out var effectiveUserId))
                {
                    await RecordGenerationTerminalAsync(
                        connectionString,
                        candidate,
                        generationId,
                        "ai_generation_failed",
                        "module025_generation_queue_invalid",
                        StatusCodes.Status422UnprocessableEntity,
                        "The queued generation identity is invalid. The existing SOW/GSD draft was preserved.",
                        string.Empty,
                        "invalid_effective_user_id",
                        "validate_queued_identity",
                        cancellationToken);
                    return true;
                }

                var correlationId = Clean(JsonString(candidate.Evidence, "correlationId"), 160);
                if (correlationId.Length == 0) correlationId = $"module025-{generationId:N}";
                var access = new Module025AccessContext(
                    candidate.ActorUserId,
                    effectiveUserId,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    IsViewAs: false,
                    IsAdministrator: JsonBoolean(candidate.Evidence, "isAdministrator"),
                    IsSolutionArchitect: JsonBoolean(candidate.Evidence, "isSolutionArchitect"),
                    IsProtectedTestUatRoleFixture: JsonBoolean(candidate.Evidence, "isProtectedTestUatRoleFixture"),
                    IsManager: JsonBoolean(candidate.Evidence, "isManager"),
                    VisibleSolutionArchitectIds: new HashSet<Guid> { effectiveUserId });

                failureStage = "record_generation_started";
                await RecordGenerationStartedAsync(connectionString, candidate, generationId, correlationId, cancellationToken);

                failureStage = "execute_generation";
                var workerContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    TraceIdentifier = correlationId
                };
                // Use the original durable queue time, including after a restart.
                var hasQueuedAt = DateTimeOffset.TryParse(JsonString(candidate.Evidence, "queuedAt"), out var queuedAt);
                workerContext.Items["Module025Deadline"] = hasQueuedAt
                    ? queuedAt.AddSeconds(Module025GenerationEngine.DeadlineSeconds)
                    : DateTimeOffset.MinValue;
                workerContext.Request.Scheme = "https";
                workerContext.Request.Host = new HostString("module025-background-worker");
                var outcome = await ExecuteGenerationAsync(
                    candidate.EngagementId,
                    candidate.ExpectedRevision,
                    generationId,
                    access,
                    protectedTestUatGrant,
                    workerContext,
                    cancellationToken);

                if (outcome.Completed
                    && outcome.HttpStatus == StatusCodes.Status200OK
                    && string.Equals(outcome.ApiStatus, "module025_detailed_scope_generated", StringComparison.Ordinal))
                {
                    // The generated scope and completed event commit atomically in ExecuteGenerationAsync.
                    return true;
                }

                failureStage = "record_generation_terminal";
                await RecordGenerationTerminalAsync(
                    connectionString,
                    candidate,
                    generationId,
                    outcome.HttpStatus == StatusCodes.Status409Conflict ? "ai_generation_obsolete" : "ai_generation_failed",
                    outcome.ApiStatus,
                    outcome.HttpStatus,
                    outcome.Message,
                    outcome.CorrelationId.Length > 0 ? outcome.CorrelationId : correlationId,
                    outcome.DiagnosticCode,
                    "execute_generation",
                    cancellationToken,
                    outcome.TargetDecisions);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Module025SowGsdGenerationWorker")
                    .LogWarning(
                        exception,
                        "Module 025 durable generation encountered a bounded failure. EngagementId={EngagementId} GenerationId={GenerationId} Diagnostic={Diagnostic}. No customer or Service Overview content was logged.",
                        candidate.EngagementId,
                        generationId,
                        exception.GetType().Name.ToLowerInvariant());
                await RecordGenerationTerminalAsync(
                    connectionString,
                    candidate,
                    generationId,
                    "ai_generation_failed",
                    "module025_generation_worker_failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "Detailed scope generation encountered a temporary failure. The existing SOW/GSD draft was preserved; retry generation.",
                    Clean(JsonString(candidate.Evidence, "correlationId"), 160),
                    exception.GetType().Name.ToLowerInvariant(),
                    failureStage,
                    cancellationToken);
                return true;
            }
            finally
            {
                await UnlockGenerationAsync(lockConnection, generationId);
            }
        }

        return false;
    }

    private static async Task<bool> TryLockGenerationAsync(NpgsqlConnection connection, Guid generationId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtextextended(@generation_id::text,725));",
            connection);
        command.Parameters.AddWithValue("generation_id", generationId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task UnlockGenerationAsync(NpgsqlConnection connection, Guid generationId)
    {
        if (connection.State != System.Data.ConnectionState.Open) return;
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended(@generation_id::text,725));",
                connection);
            command.Parameters.AddWithValue("generation_id", generationId);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Closing the connection also releases the session advisory lock.
        }
    }

    private static async Task<bool> HasGenerationTerminalAsync(NpgsqlConnection connection, Guid engagementId, Guid generationId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM module025_sow_gsd_events
                WHERE engagement_id=@engagement_id
                  AND event_type IN ('ai_generation_completed','ai_generation_failed','ai_generation_obsolete')
                  AND evidence_json->>'generationId'=@generation_id);
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("generation_id", generationId.ToString());
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task RecordGenerationStartedAsync(string connectionString, Module025QueuedGeneration candidate, Guid generationId, string correlationId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            candidate.EngagementId,
            candidate.ActorUserId,
            candidate.ExpectedRevision,
            "ai_generation_started",
            "Governed background processing started for the detailed P/D/I/V/R scope.",
            new
            {
                generationId,
                apiStatus = "module025_detailed_scope_generation_running",
                correlationId,
                message = "Celar AI is preparing the detailed P/D/I/V/R review draft.",
                startedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RecordGenerationTerminalAsync(string connectionString, Module025QueuedGeneration candidate, Guid generationId, string eventType, string apiStatus, int httpStatus, string message, string correlationId, string diagnosticCode, string failureStage, CancellationToken cancellationToken, IReadOnlyList<ProjectPulseAiTargetDecision>? targetDecisions = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            candidate.EngagementId,
            candidate.ActorUserId,
            candidate.ExpectedRevision,
            eventType,
            "Durable detailed-scope generation stopped without changing the saved draft.",
            new
            {
                generationId,
                apiStatus,
                httpStatus,
                correlationId,
                message,
                diagnosticCode = Clean(diagnosticCode, 160),
                targetDecisions = targetDecisions ?? [],
                failureStage = Clean(failureStage, 160),
                completedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid engagementId, Guid actorUserId, int revision, string eventType, string summary, object evidence, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json) VALUES(@engagement_id,@event_type,@actor_user_id,@revision,@summary,@evidence_json::jsonb);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId); command.Parameters.AddWithValue("event_type", eventType); command.Parameters.AddWithValue("actor_user_id", actorUserId); command.Parameters.AddWithValue("revision", revision); command.Parameters.AddWithValue("summary", summary); command.Parameters.AddWithValue("evidence_json", JsonSerializer.Serialize(evidence));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int?> ExecuteRevisionUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid engagementId, int revision, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("engagement_id", engagementId); command.Parameters.AddWithValue("revision", revision);
        var value = await command.ExecuteScalarAsync(cancellationToken); return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<(NpgsqlConnection? Connection, Module025EngagementRow? Engagement, Module025AccessContext? Access, IResult? Error)> LoadWritableStateAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken, bool allowArchived = false)
    {
        var authorization = await AuthorizeViewAsync(context); if (authorization is not null) return (null, null, null, authorization);
        var opened = await OpenConnectionAsync(context, cancellationToken); if (opened.Error is not null) return (null, null, null, opened.Error);
        var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) { await connection.DisposeAsync(); return (null, null, null, MigrationRequired()); }
        var access = await ResolveAccessAsync(connection, context, cancellationToken); if (access is null) { await connection.DisposeAsync(); return (null, null, null, SessionRequired()); }
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken); if (engagement is null) { await connection.DisposeAsync(); return (null, null, null, Results.NotFound(new { status = "module025_engagement_not_found" })); }
        if (!access.CanWriteOwned(engagement.OwnerUserId)) { await connection.DisposeAsync(); return (null, null, null, Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_edit")); }
        if (!allowArchived && (!engagement.IsActive || engagement.Status == "archived")) { await connection.DisposeAsync(); return (null, null, null, StateConflict("archived_record", "Unarchive this SOW/GSD before changing it.")); }
        return (connection, engagement, access, null);
    }

    private static async Task<(NpgsqlConnection? Connection, Module025EngagementRow? Engagement, Module025AccessContext? Access, IResult? Error)> LoadReadableStateAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context); if (authorization is not null) return (null, null, null, authorization);
        var opened = await OpenConnectionAsync(context, cancellationToken); if (opened.Error is not null) return (null, null, null, opened.Error);
        var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) { await connection.DisposeAsync(); return (null, null, null, MigrationRequired()); }
        var access = await ResolveAccessAsync(connection, context, cancellationToken); if (access is null) { await connection.DisposeAsync(); return (null, null, null, SessionRequired()); }
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken); if (engagement is null) { await connection.DisposeAsync(); return (null, null, null, Results.NotFound(new { status = "module025_engagement_not_found" })); }
        if (!access.CanViewOwned(engagement.OwnerUserId)) { await connection.DisposeAsync(); return (null, null, null, Forbidden("module025_owner_scope")); }
        return (connection, engagement, access, null);
    }

    private static bool MeaningfulServiceOverview(string? value)
    {
        var text = Clean(value, 30_000).Trim();
        if (text.Length < 20 || !text.Any(char.IsWhiteSpace)) return false;
        var words = System.Text.RegularExpressions.Regex.Matches(
            text,
            @"\b[\p{L}][\p{L}\p{N}/+_.-]{2,}\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200));
        return words.Count >= 4;
    }

    private static string BuildGenerationPrompt(Module025EngagementRow engagement) => $"""
        Use the single saved project scope below for the current assigned delivery phase. The same scope is reused for Plan, Design, Implement, Validate, and Release in that order.
        Saved project scope (Service Overview): {engagement.ServiceOverview}
        Commercial model: {(engagement.CommercialModel == "fixed" ? "Fixed Price" : "Time & Materials")}
        Customer program: {engagement.CustomerProgram}
        Follow the current phase instruction and generate only that phase, not all five phases in one response. Explain what this phase requires to deliver the saved scope. Keep the requested technologies, source and target versions, outcomes, and constraints consistent across phases.
        Produce structured work that supports both a customer-facing SOW with high-level execution steps and a detailed GSD task and effort breakdown. For every supported work package include the objective, detailed execution activities, technical tasks/configuration, inputs, outputs, deliverables, US Signal responsibilities, customer responsibilities, prerequisites, dependencies, assumptions, open questions, measurable acceptance criteria, validation steps, risks, and estimated engineering hours.
        Do not use vague tasks such as 'implement solution' or 'validate system'. Describe the work that will actually be planned, designed, implemented, validated, and released.
        Do not fabricate products, versions, quantities, licensing, models, access, interfaces, customer decisions, dates, prices, or technical facts. Convert unsupported material into explicit assumptions or open questions.
        Estimated hours are a reviewable AI suggestion only. The Solution Architect must review and may change every hour value before confirmation.
        """;

    private static string ClassifyPhase(string? phase, string? name, string? description)
    {
        var normalizedPhase = phase?.Trim().ToLowerInvariant() ?? string.Empty;
        if (PhaseCodes.Contains(normalizedPhase, StringComparer.OrdinalIgnoreCase)) return normalizedPhase;

        // The model's governed phase field is authoritative when valid. Text
        // heuristics apply only to legacy or malformed output; otherwise a Plan
        // description that mentions validation could be reassigned incorrectly.
        var value = $"{phase} {name} {description}".ToLowerInvariant();
        if (value.Contains("release") || value.Contains("handoff") || value.Contains("closeout") || value.Contains("transition")) return "release";
        if (value.Contains("validat") || value.Contains("test") || value.Contains("acceptance") || value.Contains("verify")) return "validate";
        if (value.Contains("implement") || value.Contains("deploy") || value.Contains("configur") || value.Contains("migrat") || value.Contains("install")) return "implement";
        if (value.Contains("design") || value.Contains("architect") || value.Contains("solution design")) return "design";
        return "plan";
    }

    private static void AddDistinct(List<string> target, string? value) { var clean = Clean(value, 12_000); if (clean.Length > 0 && !target.Contains(clean, StringComparer.OrdinalIgnoreCase)) target.Add(clean); }
    private static void AddDistinct(List<string> target, IEnumerable<string>? values) { foreach (var value in values ?? Array.Empty<string>()) AddDistinct(target, value); }

    private static async Task<(NpgsqlConnection? Connection, IResult? Error)> OpenConnectionAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return (null, Results.Json(new { status = "module025_storage_unavailable", message = "The Module 025 database connection is not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable));
        try { var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken); return (connection, null); }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Module025SowGsd").LogWarning(exception, "Module 025 storage connection could not be opened.");
            return (null, Results.Json(new { status = "module025_storage_unavailable", message = "The Module 025 workspace storage is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION" })
        {
            var configured = Environment.GetEnvironmentVariable(name); if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST"); var database = Environment.GetEnvironmentVariable("PTP_DB_NAME"); var username = Environment.GetEnvironmentVariable("PTP_DB_USER"); var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
        return new NpgsqlConnectionStringBuilder { Host = host, Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432, Database = database, Username = username, Password = password, IncludeErrorDetail = false, Pooling = true, MaxPoolSize = 10 }.ConnectionString;
    }

    private static string WorkerLockConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            // Session advisory locks are held while private inference runs. A
            // bounded keepalive prevents an infrastructure idle timeout from
            // silently releasing the lock before the worker records its result.
            KeepAlive = 30
        };
        return builder.ConnectionString;
    }

    private static async Task<bool> WorkspaceSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.module025_sow_gsd_engagements') IS NOT NULL AND to_regclass('public.module025_sow_gsd_phases') IS NOT NULL AND to_regclass('public.module025_sow_gsd_events') IS NOT NULL;", connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<IResult?> AuthorizeViewAsync(HttpContext context) => await GovernedOperationsReadModule.AuthorizeAsync(context, ModuleNumber, ViewRoles, new[] { "VIEW_SOW_GSD_025", "MANAGE_SOW_GSD_025", "MANAGE_ALL" });
    private static bool SameOrigin(HttpContext context) { if (!context.Request.Headers.TryGetValue("Origin", out var values)) return true; if (!Uri.TryCreate(values.ToString(), UriKind.Absolute, out var origin)) return false; return string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase) && origin.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80)); }
    private static string NormalizeCommercialModel(string? value) => string.Equals(value?.Trim(), "fixed", StringComparison.OrdinalIgnoreCase) || string.Equals(value?.Trim(), "fixed_price", StringComparison.OrdinalIgnoreCase) ? "fixed" : "time_and_materials";
    private static string NormalizeCustomerProgram(string? value) { var normalized = value?.Trim().ToLowerInvariant(); return normalized is "toyota" or "hyundai" ? normalized : "standard"; }
    private static string TemplateKey(string customerProgram) => customerProgram is "toyota" or "hyundai" ? Module025SowGsdDocumentExporter.HaeaGsdTemplateKey : Module025SowGsdDocumentExporter.StandardGsdTemplateKey;
    private static string? NormalizePhaseCode(string? value) { var normalized = value?.Trim().ToLowerInvariant(); return PhaseCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : null; }
    private static string PhaseLabel(string phaseCode) => phaseCode switch { "plan" => "Plan", "design" => "Design", "implement" => "Implement", "validate" => "Validate", "release" => "Release", _ => phaseCode };
    private static IReadOnlyList<string> CleanList(IEnumerable<string>? values) => (values ?? Array.Empty<string>()).Select(value => Clean(value, 12_000)).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
    private static string Clean(string? value, int maximum) { var clean = value?.Trim() ?? string.Empty; return clean.Length <= maximum ? clean : clean[..maximum]; }
    private static Module025GenerationExecutionOutcome GenerationFailureOutcome(string diagnostic, string correlationId)
    {
        // Infrastructure failures are not evidence deficiencies. Preserve the
        // real closed diagnostic and the unchanged draft, without masking it as 422.
        var runtimeFailure = new[] {
            "private_model_http_500", "private_model_http_502", "private_model_http_503", "private_model_http_504",
            "private_model_timeout", "private_model_transport_failure", "provider_deadline_exceeded", "provider_timeout", "provider_network_error",
            "deepseek_timeout", "deepseek_connection_failed", "deepseek_queue_busy",
            "deepseek_queue_unavailable", "deepseek_http_429", "deepseek_http_500", "deepseek_http_502",
            "deepseek_http_503", "deepseek_http_504",
            "private_module025_generation_deadline_exceeded", "private_module025_phase_deadline_exceeded"
        }.Any(code => string.Equals(diagnostic, code, StringComparison.Ordinal)
            || diagnostic.StartsWith(code + "_phase_", StringComparison.Ordinal)
            || diagnostic.StartsWith(code + "_private_runtime_", StringComparison.Ordinal));
        return new(runtimeFailure ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
            runtimeFailure ? "module025_ai_temporarily_unavailable" : "module025_ai_evidence_limited",
            runtimeFailure
                ? "The private inference service could not complete SOW generation. The saved draft was not changed."
                : "Celar AI did not return a reviewable SOW draft. No generic scope or fabricated level of effort was substituted.",
            correlationId, false, diagnostic);
    }

    private static string CompositionDiagnosticCode(CelarAiComposeResult composition)
    {
        return PrivateGenerationDiagnostic(composition.TargetDecisions ?? [], composition.Status);
    }

    private static string PrivateGenerationDiagnostic(IReadOnlyList<ProjectPulseAiTargetDecision> decisions, string status)
    {
        // Report the last actual generation failure, including structured cloud
        // adapters. A refusal is terminal and must not be masked by fallback.
        var privateDecisions = decisions.Where(decision => CelarAiCapabilityTargets.IsPrivate(decision.Target)
            || (decision.Target is CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi or CelarAiCapabilityTargets.Gemini or CelarAiCapabilityTargets.Copilot
                && decision.ReasonCode.StartsWith("module025_", StringComparison.Ordinal)));
        var decision = privateDecisions.LastOrDefault(value => value.Outcome == "refused")
            ?? privateDecisions.LastOrDefault(value => value.Outcome == "failed");
        return Clean(string.IsNullOrWhiteSpace(decision?.ReasonCode) ? status : decision.ReasonCode, 160);
    }
    private static DateTimeOffset? NullableTimestamp(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) => command.Parameters.AddWithValue(name, value.HasValue ? (object)value.Value : DBNull.Value);

    private static JsonElement ParseJson(string value, JsonValueKind expected)
    {
        try { using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? (expected == JsonValueKind.Array ? "[]" : "{}") : value); return document.RootElement.Clone(); }
        catch (JsonException) { using var fallback = JsonDocument.Parse(expected == JsonValueKind.Array ? "[]" : "{}"); return fallback.RootElement.Clone(); }
    }

    private static IReadOnlyList<string> ParseStringArray(string value)
    {
        try { using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "[]" : value); return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray() : Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<int> ParseIntArray(string value)
    {
        try { using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "[]" : value); if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<int>(); return document.RootElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _)).Select(item => item.GetInt32()).Distinct().ToArray(); }
        catch (JsonException) { return Array.Empty<int>(); }
    }

    private static bool TryJsonProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject()) if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        }
        value = default; return false;
    }

    private static IEnumerable<JsonElement> JsonArray(JsonElement element, string name) => TryJsonProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Array ? property.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
    private static string JsonString(JsonElement element, string name) => TryJsonProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() ?? string.Empty : string.Empty;
    private static IReadOnlyList<string> JsonStrings(JsonElement element, string name) => JsonArray(element, name).Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()?.Trim() ?? string.Empty).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static IReadOnlyList<int> JsonIntegers(JsonElement element, string name) => JsonArray(element, name).Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _)).Select(item => item.GetInt32()).Distinct().ToArray();
    private static decimal? JsonDecimal(JsonElement element, string name) => TryJsonProperty(element, name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var value) ? value : null;
    private static bool JsonBoolean(JsonElement element, string name) => TryJsonProperty(element, name, out var property) && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False) && property.GetBoolean();
    private static IReadOnlySet<string> Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static string SafeFileName(string value) => new(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character).ToArray());

    private static IResult SessionRequired() => Results.Unauthorized();
    private static IResult Forbidden(string capability) => Results.Json(new { status = "module025_forbidden", capability, message = "Your current Pulse role or reporting scope does not grant this Module 025 operation." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult MigrationRequired() => Results.Json(new { status = "module025_migration_required", migration = MigrationId, message = "Apply the Module 025 SOW/GSD workspace migration before using this workflow." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult OriginRejected() => Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult RequestTooLarge() => Results.Json(new { status = "request_too_large", message = $"Module 025 request bodies are limited to {MaximumRequestBytes} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge);
    private static IResult RevisionConflict(int currentRevision) => Results.Conflict(new { status = "module025_revision_conflict", currentRevision, message = "This SOW/GSD changed after it was loaded. Reload the latest revision before saving again." });
    private static IResult StateConflict(string status, string message) => Results.Conflict(new { status, message });

    private sealed record CustomerSelection(Guid? CustomerId, string CustomerName, string Mode, IResult? Error);
    private sealed record PersonSelection(Guid? UserId, string DisplayName);
    private sealed record Module025QueuedGeneration(long EventId, Guid EngagementId, Guid ActorUserId, int ExpectedRevision, JsonElement Evidence);
    private sealed record Module025GenerationExecutionOutcome(int HttpStatus, string ApiStatus, string Message, string CorrelationId, bool Completed, string DiagnosticCode = "", IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions = null);

    private sealed class GeneratedPhase
    {
        internal GeneratedPhase(string phaseCode) => PhaseCode = phaseCode;
        internal string PhaseCode { get; }
        internal int PackageCount { get; set; }
        internal decimal SuggestedHours { get; set; }
        internal List<string> Objectives { get; } = new();
        internal List<string> DetailedActivities { get; } = new();
        internal List<string> TechnicalTasks { get; } = new();
        internal List<string> Deliverables { get; } = new();
        internal List<string> CustomerResponsibilities { get; } = new();
        internal List<string> UsSignalResponsibilities { get; } = new();
        internal List<string> Prerequisites { get; } = new();
        internal List<string> Dependencies { get; } = new();
        internal List<string> Assumptions { get; } = new();
        internal List<string> OpenQuestions { get; } = new();
        internal List<string> AcceptanceCriteria { get; } = new();
        internal List<string> ValidationSteps { get; } = new();
        internal List<string> Risks { get; } = new();
        internal HashSet<int> CitationIds { get; } = new();
    }
}

/// <summary>
/// Advances durable Module 025 generation requests outside the inbound HTTP
/// lifetime. The event row is the queue and a PostgreSQL advisory lock prevents
/// multiple API replicas from processing the same generation concurrently.
/// </summary>
internal sealed class Module025SowGsdGenerationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<Module025SowGsdGenerationWorker> _logger;

    public Module025SowGsdGenerationWorker(
        IServiceProvider services,
        ILogger<Module025SowGsdGenerationWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var processed = await Module025SowGsdModule.ProcessNextQueuedGenerationAsync(
                    scope.ServiceProvider,
                    stoppingToken);
                await DelayAsync(processed ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Module 025 durable generation worker encountered a bounded failure. No customer or Service Overview content was logged.");
                await DelayAsync(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private static async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown.
        }
    }
}
