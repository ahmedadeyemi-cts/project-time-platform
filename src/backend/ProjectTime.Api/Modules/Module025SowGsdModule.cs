using System.Globalization;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 025 persistent SOW + GSD workspace. Solution Architects own their
/// drafts; managers receive read-only visibility to current direct-report SAs in
/// their department; administrators retain governed support access. AI output is
/// review-only and preserves suggested effort separately from SA-final effort.
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
        "SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT", "SA", "SAA", "MANAGER"
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
        "MANAGER", "ENGINEERING_MANAGER", "SOLUTIONS_ARCHITECT_MANAGER", "SOLUTION_ARCHITECT_MANAGER"
    };

    private static readonly string[] PhaseCodes = { "plan", "design", "implement", "validate", "release" };

    public static WebApplication MapModule025SowGsdEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/module025/sow-gsd/bootstrap",
            (Func<HttpContext, CancellationToken, Task<IResult>>)BootstrapAsync);
        app.MapGet(
            "/api/module025/sow-gsd",
            (Func<string?, Guid?, string?, HttpContext, CancellationToken, Task<IResult>>)ListAsync);
        app.MapPost(
            "/api/module025/sow-gsd",
            (Func<Module025SowGsdCreateRequest, HttpContext, CancellationToken, Task<IResult>>)CreateAsync);
        app.MapGet(
            "/api/module025/sow-gsd/{engagementId:guid}",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetAsync);
        app.MapPut(
            "/api/module025/sow-gsd/{engagementId:guid}",
            (Func<Guid, Module025SowGsdSaveRequest, HttpContext, CancellationToken, Task<IResult>>)SaveAsync);
        app.MapPost(
            "/api/module025/sow-gsd/{engagementId:guid}/generate",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GenerateAsync);
        app.MapPost(
            "/api/module025/sow-gsd/{engagementId:guid}/confirm",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ConfirmAsync);
        app.MapPost(
            "/api/module025/sow-gsd/{engagementId:guid}/reopen",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ReopenAsync);
        app.MapPost(
            "/api/module025/sow-gsd/{engagementId:guid}/archive",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ArchiveAsync);
        app.MapPost(
            "/api/module025/sow-gsd/{engagementId:guid}/unarchive",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)UnarchiveAsync);
        app.MapGet(
            "/api/module025/sow-gsd/{engagementId:guid}/sow.docx",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadSowAsync);
        app.MapGet(
            "/api/module025/sow-gsd/{engagementId:guid}/gsd.xlsx",
            (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadGsdAsync);
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
        var accountExecutives = await LoadPeopleByRoleAsync(
            connection,
            new[] { "ACCOUNT_EXECUTIVE", "SALES_ACCOUNT_EXECUTIVE", "ACCOUNT_EXECUTIVES" },
            cancellationToken);
        var resalePeople = await LoadPeopleByRoleAsync(
            connection,
            new[] { "RESALE", "INSIDE_SALES", "SALES", "SALES_SUPPORT", "ACCOUNT_EXECUTIVE" },
            cancellationToken);
        var solutionArchitects = await LoadVisibleSolutionArchitectsAsync(connection, access, cancellationToken);

        return Results.Ok(new
        {
            status = "module025_workspace_ready",
            module = ModuleNumber,
            migration = MigrationId,
            contract = WorkspaceContract,
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
                access.IsManager,
                access.IsViewAs,
                canCreate = access.CanCreate,
                canEditOwn = !access.IsViewAs && (access.IsSolutionArchitect || access.IsAdministrator),
                managerScopeReadOnly = access.IsManager && !access.IsAdministrator
            },
            customers,
            accountExecutives,
            resalePeople,
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

    private static async Task<IResult> ListAsync(
        string? state,
        Guid? ownerUserId,
        string? search,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();

        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        var selectedOwner = ownerUserId ?? access.EffectiveUserId;
        if (!access.CanViewOwned(selectedOwner)) return Forbidden("module025_owner_scope");
        var archived = string.Equals(state, "archived", StringComparison.OrdinalIgnoreCase);
        var normalizedSearch = Clean(search, MaximumSearchLength);

        const string sql = """
            SELECT
                engagement_id, engagement_number, owner_user_id, owner_display_name,
                customer_name, commercial_model, customer_program, gsd_template_key,
                account_executive_name, resale_name, status, is_active, revision,
                last_generated_at, confirmed_at, archived_at, created_at, updated_at,
                COALESCE((SELECT sum(final_hours) FROM module025_sow_gsd_phases phase WHERE phase.engagement_id=engagement.engagement_id),0),
                COALESCE((SELECT sum(suggested_hours) FROM module025_sow_gsd_phases phase WHERE phase.engagement_id=engagement.engagement_id),0)
            FROM module025_sow_gsd_engagements engagement
            WHERE owner_user_id=@owner_user_id
              AND is_active=@is_active
              AND (
                @search=''
                OR engagement_number ILIKE '%' || @search || '%'
                OR customer_name ILIKE '%' || @search || '%'
                OR service_overview ILIKE '%' || @search || '%'
              )
            ORDER BY updated_at DESC
            LIMIT 300;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("owner_user_id", selectedOwner);
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
                lastGeneratedAt = reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset?>(13),
                confirmedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset?>(14),
                archivedAt = reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset?>(15),
                createdAt = reader.GetFieldValue<DateTimeOffset>(16),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(17),
                finalHours = reader.GetDecimal(18),
                suggestedHours = reader.GetDecimal(19)
            });
        }

        return Results.Ok(new
        {
            status = "module025_engagements_loaded",
            state = archived ? "archived" : "active",
            ownerUserId = selectedOwner,
            count = rows.Count,
            engagements = rows,
            stateChanged = false
        });
    }

    private static async Task<IResult> CreateAsync(
        Module025SowGsdCreateRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
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

        var customerSelection = await ResolveCustomerAsync(
            connection, request.CustomerId, request.CustomerName, request.CustomerEntryMode, cancellationToken);
        if (customerSelection.Error is not null) return customerSelection.Error;
        var commercialModel = NormalizeCommercialModel(request.CommercialModel);
        var customerProgram = NormalizeCustomerProgram(request.CustomerProgram);
        var gsdTemplateKey = TemplateKey(customerProgram);
        var accountExecutive = await ResolvePersonAsync(connection, request.AccountExecutiveUserId, cancellationToken);
        var resale = await ResolvePersonAsync(connection, request.ResaleUserId, cancellationToken);
        var engagementId = Guid.NewGuid();

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string insert = """
            INSERT INTO module025_sow_gsd_engagements(
                engagement_id, owner_user_id, owner_display_name, owner_department_name,
                owner_team_name, customer_id, customer_name, customer_entry_mode,
                commercial_model, customer_program, gsd_template_key,
                account_executive_user_id, account_executive_name,
                resale_user_id, resale_name, service_overview)
            VALUES(
                @engagement_id, @owner_user_id, @owner_display_name, @department_name,
                @team_name, @customer_id, @customer_name, @customer_entry_mode,
                @commercial_model, @customer_program, @gsd_template_key,
                @account_executive_user_id, @account_executive_name,
                @resale_user_id, @resale_name, @service_overview)
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
            command.Parameters.AddWithValue("customer_id", customerSelection.CustomerId.HasValue ? customerSelection.CustomerId.Value : DBNull.Value);
            command.Parameters.AddWithValue("customer_name", customerSelection.CustomerName);
            command.Parameters.AddWithValue("customer_entry_mode", customerSelection.Mode);
            command.Parameters.AddWithValue("commercial_model", commercialModel);
            command.Parameters.AddWithValue("customer_program", customerProgram);
            command.Parameters.AddWithValue("gsd_template_key", gsdTemplateKey);
            command.Parameters.AddWithValue("account_executive_user_id", accountExecutive.UserId.HasValue ? accountExecutive.UserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("account_executive_name", accountExecutive.DisplayName);
            command.Parameters.AddWithValue("resale_user_id", resale.UserId.HasValue ? resale.UserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("resale_name", resale.DisplayName);
            command.Parameters.AddWithValue("service_overview", Clean(request.ServiceOverview, 30_000));
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
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (engagement is null) return Results.NotFound(new { status = "module025_engagement_not_found" });
        if (!access.CanViewOwned(engagement.OwnerUserId)) return Forbidden("module025_owner_scope");
        return Results.Ok(PublicEngagement(engagement, access));
    }

    private static async Task<IResult> SaveAsync(
        Guid engagementId,
        Module025SowGsdSaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        if (context.Request.ContentLength is > MaximumRequestBytes) return RequestTooLarge();
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        var current = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (current is null) return Results.NotFound(new { status = "module025_engagement_not_found" });
        if (!access.CanWriteOwned(current.OwnerUserId)) return Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_edit");
        if (!current.IsActive || current.Status == "archived") return StateConflict("archived_record", "Unarchive this SOW/GSD before editing it.");
        if (current.Status == "confirmed") return StateConflict("confirmed_record", "Reopen this confirmed SOW/GSD before editing it.");
        if (request.ExpectedRevision != current.Revision) return RevisionConflict(current.Revision);

        var customerSelection = await ResolveCustomerAsync(
            connection, request.CustomerId, request.CustomerName, request.CustomerEntryMode, cancellationToken);
        if (customerSelection.Error is not null) return customerSelection.Error;
        var accountExecutive = await ResolvePersonAsync(connection, request.AccountExecutiveUserId, cancellationToken);
        var resale = await ResolvePersonAsync(connection, request.ResaleUserId, cancellationToken);
        var commercialModel = NormalizeCommercialModel(request.CommercialModel);
        var customerProgram = NormalizeCustomerProgram(request.CustomerProgram);
        var serviceOverview = Clean(request.ServiceOverview, 30_000);
        var overviewChanged = !string.Equals(current.ServiceOverview, serviceOverview, StringComparison.Ordinal);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string update = """
            UPDATE module025_sow_gsd_engagements
            SET customer_id=@customer_id,
                customer_name=@customer_name,
                customer_entry_mode=@customer_entry_mode,
                commercial_model=@commercial_model,
                customer_program=@customer_program,
                gsd_template_key=@gsd_template_key,
                account_executive_user_id=@account_executive_user_id,
                account_executive_name=@account_executive_name,
                resale_user_id=@resale_user_id,
                resale_name=@resale_name,
                service_overview=@service_overview,
                status=CASE WHEN service_overview IS DISTINCT FROM @service_overview THEN 'draft' ELSE status END,
                last_generated_at=CASE WHEN service_overview IS DISTINCT FROM @service_overview THEN NULL ELSE last_generated_at END,
                revision=revision+1
            WHERE engagement_id=@engagement_id
              AND revision=@expected_revision
              AND is_active=TRUE
              AND status<>'confirmed'
              AND status<>'archived'
            RETURNING revision;
            """;
        int nextRevision;
        await using (var command = new NpgsqlCommand(update, connection, transaction))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            command.Parameters.AddWithValue("expected_revision", request.ExpectedRevision);
            command.Parameters.AddWithValue("customer_id", customerSelection.CustomerId.HasValue ? customerSelection.CustomerId.Value : DBNull.Value);
            command.Parameters.AddWithValue("customer_name", customerSelection.CustomerName);
            command.Parameters.AddWithValue("customer_entry_mode", customerSelection.Mode);
            command.Parameters.AddWithValue("commercial_model", commercialModel);
            command.Parameters.AddWithValue("customer_program", customerProgram);
            command.Parameters.AddWithValue("gsd_template_key", TemplateKey(customerProgram));
            command.Parameters.AddWithValue("account_executive_user_id", accountExecutive.UserId.HasValue ? accountExecutive.UserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("account_executive_name", accountExecutive.DisplayName);
            command.Parameters.AddWithValue("resale_user_id", resale.UserId.HasValue ? resale.UserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("resale_name", resale.DisplayName);
            command.Parameters.AddWithValue("service_overview", serviceOverview);
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
            if (phaseCode is null) continue;
            await SaveHumanPhaseAsync(connection, transaction, engagementId, phaseCode, phaseRequest, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        var saved = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        return Results.Ok(new
        {
            status = overviewChanged ? "module025_saved_scope_regeneration_required" : "module025_autosaved",
            revision = nextRevision,
            engagement = saved is null ? null : PublicEngagement(saved, access),
            requiresRegeneration = overviewChanged,
            stateChanged = true
        });
    }

    private static async Task<IResult> GenerateAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
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
        var current = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (current is null) return Results.NotFound(new { status = "module025_engagement_not_found" });
        if (!access.CanWriteOwned(current.OwnerUserId)) return Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_generate");
        if (!current.IsActive || current.Status == "archived") return StateConflict("archived_record", "Unarchive this SOW/GSD before generating scope.");
        if (current.Status == "confirmed") return StateConflict("confirmed_record", "Reopen this confirmed SOW/GSD before generating a new scope.");
        if (current.ServiceOverview.Trim().Length < 20)
            return Results.BadRequest(new { status = "service_overview_required", message = "Enter a meaningful Service Overview before asking Celar AI to build the detailed P/D/I/V/R scope and level of effort." });

        var enterprise = context.RequestServices.GetRequiredService<CelarAiEnterprisePlatformService>();
        var requestedOutcome = BuildGenerationPrompt(current);
        CelarAiComposeResult composition;
        try
        {
            composition = await enterprise.ComposeAsync(
                access.ActualUserId,
                access.EffectiveUserId,
                new CelarAiComposeRequest(
                    Mode: "sow_draft",
                    ProjectCode: current.EngagementNumber,
                    ProjectName: current.CustomerName,
                    RequestedOutcome: requestedOutcome,
                    DetailLevel: "comprehensive",
                    DiagramType: "flowchart",
                    AllowSanitizedExternalFallback: false,
                    CapabilityCode: CelarAiCapabilityCatalog.SowGsdPlanning),
                context,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Module025SowGsd")
                .LogWarning(exception, "Module 025 detailed SOW/GSD generation failed without logging customer or service-overview content.");
            return Results.Json(new
            {
                status = "module025_ai_temporarily_unavailable",
                message = "The governed Celar AI route did not complete. The saved SOW/GSD draft was not changed."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var draft = composition.SowDraft;
        if (draft is null || draft.WorkPackages.Count == 0)
        {
            return Results.UnprocessableEntity(new
            {
                status = "module025_ai_evidence_limited",
                message = "Celar AI did not return detailed, reviewable work packages. No generic scope or fabricated level of effort was substituted.",
                composition.Status,
                composition.Warnings,
                composition.MissingEvidence,
                composition.Conflicts,
                composition.Confidence,
                composition.ConfidenceExplanation,
                composition.CorrelationId
            });
        }

        var generated = PhaseCodes.ToDictionary(code => code, code => new GeneratedPhase(code), StringComparer.OrdinalIgnoreCase);
        foreach (var package in draft.WorkPackages)
        {
            var phaseCode = ClassifyPhase(package.Phase, package.Name, package.Description);
            var phase = generated[phaseCode];
            phase.PackageCount += 1;
            phase.SuggestedHours += Math.Max(0m, package.EstimatedHours);
            AddDistinct(phase.Objectives, package.Description);
            AddDistinct(phase.DetailedActivities, package.Name.Length > 0 ? $"{package.Name}: {package.Description}" : package.Description);
            AddDistinct(phase.TechnicalTasks, package.DetailedSteps);
            AddDistinct(phase.Deliverables, package.Outputs);
            AddDistinct(phase.CustomerResponsibilities, package.CustomerResponsibilities);
            AddDistinct(phase.UsSignalResponsibilities, package.UsSignalResponsibilities);
            AddDistinct(phase.Prerequisites, package.Prerequisites);
            AddDistinct(phase.Dependencies, package.Predecessors);
            if (package.IsAssumption) AddDistinct(phase.Assumptions, package.Description);
            AddDistinct(phase.OpenQuestions, package.OpenQuestions);
            AddDistinct(phase.AcceptanceCriteria, package.AcceptanceCriteria);
            AddDistinct(phase.ValidationSteps, package.ValidationSteps);
            AddDistinct(phase.Risks, package.Risks);
            foreach (var citationId in package.CitationIds) phase.CitationIds.Add(citationId);
        }

        var sowSections = new
        {
            executiveSummary = draft.ExecutiveSummary,
            objectives = draft.Objectives,
            inScope = draft.InScope,
            outOfScope = draft.OutOfScope,
            deliverables = draft.Deliverables,
            customerResponsibilities = draft.CustomerResponsibilities,
            usSignalResponsibilities = draft.UsSignalResponsibilities,
            assumptions = draft.Assumptions,
            dependencies = draft.Dependencies,
            acceptanceCriteria = draft.AcceptanceCriteria,
            timelineAndMilestones = draft.TimelineAndMilestones,
            risks = draft.Risks,
            openQuestions = draft.OpenQuestions,
            citationIds = draft.CitationIds,
            reviewRequired = draft.ReviewRequired,
            contractuallyBinding = draft.ContractuallyBinding
        };
        var aiMetadata = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            composition.Status,
            composition.PrimaryExecutionPath,
            composition.SelectedTarget,
            composition.AttemptedTargets,
            composition.SkippedTargets,
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
        };

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var phaseCode in PhaseCodes)
        {
            var phase = generated[phaseCode];
            var objective = phase.Objectives.Count > 0
                ? string.Join(" ", phase.Objectives)
                : $"No supported {PhaseLabel(phaseCode)} work package was returned. The Solution Architect must define and validate this phase before confirmation.";
            var rationale = phase.PackageCount > 0
                ? $"Celar AI suggested {phase.SuggestedHours:0.##} hour(s) across {phase.PackageCount} detailed {PhaseLabel(phaseCode)} work package(s). The Solution Architect must validate the estimate against customer readiness, dependencies, access, technical constraints, and the confirmed execution approach before finalizing the GSD."
                : "No evidence-supported effort was returned for this phase. The suggested effort remains 0 hours until the Solution Architect defines and validates the missing work.";
            await SaveGeneratedPhaseAsync(connection, transaction, engagementId, phase, objective, rationale, cancellationToken);
        }

        const string update = """
            UPDATE module025_sow_gsd_engagements
            SET sow_sections=@sow_sections::jsonb,
                ai_metadata=@ai_metadata::jsonb,
                status='review_ready',
                last_generated_at=NOW(),
                revision=revision+1
            WHERE engagement_id=@engagement_id
              AND revision=@expected_revision
              AND is_active=TRUE
              AND status<>'archived'
              AND status<>'confirmed'
            RETURNING revision;
            """;
        int revision;
        await using (var command = new NpgsqlCommand(update, connection, transaction))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            command.Parameters.AddWithValue("expected_revision", current.Revision);
            command.Parameters.AddWithValue("sow_sections", JsonSerializer.Serialize(sowSections));
            command.Parameters.AddWithValue("ai_metadata", JsonSerializer.Serialize(aiMetadata));
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return RevisionConflict(current.Revision);
            }
            revision = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision, "ai_generated", "Detailed P/D/I/V/R scope and suggested LOE generated for Solution Architect review.", new
        {
            composition.CorrelationId,
            composition.Confidence,
            missingPhaseCodes = generated.Values.Where(value => value.PackageCount == 0).Select(value => value.PhaseCode).ToArray()
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var saved = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        return Results.Ok(new
        {
            status = "module025_detailed_scope_generated",
            revision,
            engagement = saved is null ? null : PublicEngagement(saved, access),
            warnings = composition.Warnings,
            missingEvidence = composition.MissingEvidence,
            conflicts = composition.Conflicts,
            confidence = composition.Confidence,
            confidenceExplanation = composition.ConfidenceExplanation,
            correlationId = composition.CorrelationId,
            message = "Detailed Plan, Design, Implement, Validate, and Release scope is ready for Solution Architect review. AI-suggested hours remain separate from editable final hours.",
            stateChanged = true
        });
    }

    private static async Task<IResult> ConfirmAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var state = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var engagement = state.Engagement!;
        var access = state.Access!;
        if (engagement.Status == "confirmed") return Results.Ok(new { status = "module025_already_confirmed", engagementId, engagement.Revision, stateChanged = false });
        if (!engagement.IsActive || engagement.Status == "archived") return StateConflict("archived_record", "Unarchive this SOW/GSD before confirmation.");
        if (!engagement.LastGeneratedAt.HasValue) return StateConflict("generation_required", "Generate and review the detailed P/D/I/V/R scope before confirmation.");
        if (engagement.CustomerName.Length == 0) return StateConflict("customer_required", "Select or manually enter the customer before confirmation.");
        if (!engagement.AccountExecutiveUserId.HasValue) return StateConflict("account_executive_required", "Select the Account Executive before confirmation.");
        if (!engagement.ResaleUserId.HasValue) return StateConflict("resale_required", "Select the Resale person before confirmation.");
        if (engagement.Phases.Count != 5 || engagement.Phases.Any(phase => string.IsNullOrWhiteSpace(phase.Objective)))
            return StateConflict("phase_review_incomplete", "Review all five Plan, Design, Implement, Validate, and Release sections before confirmation.");
        if (engagement.Phases.Sum(phase => phase.FinalHours) <= 0)
            return StateConflict("level_of_effort_required", "The reviewed GSD must contain a positive total level of effort before confirmation.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE module025_sow_gsd_engagements
            SET status='confirmed', confirmed_at=NOW(), revision=revision+1
            WHERE engagement_id=@engagement_id AND revision=@revision AND is_active=TRUE
            RETURNING revision;
            """;
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return RevisionConflict(engagement.Revision);
        }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "confirmed", "Solution Architect confirmed the SOW/GSD package for document export.", new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_confirmed", engagementId, revision, canDownload = true, stateChanged = true });
    }

    private static async Task<IResult> ReopenAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var state = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var engagement = state.Engagement!;
        var access = state.Access!;
        if (!engagement.IsActive || engagement.Status == "archived") return StateConflict("archived_record", "Unarchive this SOW/GSD before reopening it.");
        if (engagement.Status != "confirmed") return Results.Ok(new { status = "module025_already_editable", engagementId, engagement.Revision, stateChanged = false });

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE module025_sow_gsd_engagements
            SET status='review_ready', revision=revision+1
            WHERE engagement_id=@engagement_id AND revision=@revision AND status='confirmed'
            RETURNING revision;
            """;
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "reopened", "Confirmed SOW/GSD reopened for Solution Architect edits and reconfirmation.", new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_reopened", engagementId, revision, stateChanged = true });
    }

    private static async Task<IResult> ArchiveAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var state = await LoadWritableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var engagement = state.Engagement!;
        var access = state.Access!;
        if (!engagement.IsActive || engagement.Status == "archived") return Results.Ok(new { status = "module025_already_archived", engagementId, engagement.Revision, stateChanged = false });

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE module025_sow_gsd_engagements
            SET status='archived', is_active=FALSE, archived_at=NOW(), revision=revision+1
            WHERE engagement_id=@engagement_id AND revision=@revision AND is_active=TRUE
            RETURNING revision;
            """;
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "archived", "SOW/GSD removed from the active work queue.", new { priorStatus = engagement.Status }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_archived", engagementId, revision, stateChanged = true });
    }

    private static async Task<IResult> UnarchiveAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (engagement is null) return Results.NotFound(new { status = "module025_engagement_not_found" });
        if (!access.CanWriteOwned(engagement.OwnerUserId)) return Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_unarchive");
        if (engagement.IsActive && engagement.Status != "archived") return Results.Ok(new { status = "module025_already_active", engagementId, engagement.Revision, stateChanged = false });

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE module025_sow_gsd_engagements
            SET status=CASE WHEN last_generated_at IS NULL THEN 'draft' ELSE 'review_ready' END,
                is_active=TRUE, archived_at=NULL, revision=revision+1
            WHERE engagement_id=@engagement_id AND revision=@revision AND is_active=FALSE
            RETURNING revision;
            """;
        var revision = await ExecuteRevisionUpdateAsync(connection, transaction, sql, engagementId, engagement.Revision, cancellationToken);
        if (!revision.HasValue) { await transaction.RollbackAsync(cancellationToken); return RevisionConflict(engagement.Revision); }
        await InsertEventAsync(connection, transaction, engagementId, access.ActualUserId, revision.Value, "unarchived", "SOW/GSD returned to the active work queue for review.", new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "module025_unarchived", engagementId, revision, stateChanged = true });
    }

    private static async Task<IResult> DownloadSowAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var engagement = state.Engagement!;
        if (engagement.Status != "confirmed") return StateConflict("confirmation_required", "Confirm the reviewed SOW/GSD before downloading customer documents.");
        var model = BuildDocumentModel(engagement);
        var bytes = Module025SowGsdDocumentExporter.CreateSowDocx(model);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{SafeFileName(engagement.EngagementNumber)}-SOW.docx");
    }

    private static async Task<IResult> DownloadGsdAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        var state = await LoadReadableStateAsync(engagementId, context, cancellationToken);
        if (state.Error is not null) return state.Error;
        await using var connection = state.Connection!;
        var engagement = state.Engagement!;
        if (engagement.Status != "confirmed") return StateConflict("confirmation_required", "Confirm the reviewed SOW/GSD before downloading customer documents.");
        var model = BuildDocumentModel(engagement);
        var bytes = Module025SowGsdDocumentExporter.CreateGsdXlsx(model);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{SafeFileName(engagement.EngagementNumber)}-GSD.xlsx");
    }

    private static Module025DocumentModel BuildDocumentModel(Module025EngagementRow engagement) => new(
        engagement,
        engagement.Phases.OrderBy(phase => phase.SortOrder).ToArray(),
        engagement.Phases.Sum(phase => phase.SuggestedHours),
        engagement.Phases.Sum(phase => phase.FinalHours));

    private static async Task SaveHumanPhaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid engagementId,
        string phaseCode,
        Module025SowGsdPhaseSaveRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE module025_sow_gsd_phases
            SET final_hours=@final_hours,
                objective=@objective,
                detailed_activities=@detailed_activities::jsonb,
                technical_tasks=@technical_tasks::jsonb,
                deliverables=@deliverables::jsonb,
                customer_responsibilities=@customer_responsibilities::jsonb,
                us_signal_responsibilities=@us_signal_responsibilities::jsonb,
                prerequisites=@prerequisites::jsonb,
                dependencies=@dependencies::jsonb,
                assumptions=@assumptions::jsonb,
                open_questions=@open_questions::jsonb,
                acceptance_criteria=@acceptance_criteria::jsonb,
                validation_steps=@validation_steps::jsonb,
                risks=@risks::jsonb,
                loe_rationale=@loe_rationale,
                updated_at=NOW()
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
    }

    private static async Task SaveGeneratedPhaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid engagementId,
        GeneratedPhase phase,
        string objective,
        string rationale,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE module025_sow_gsd_phases
            SET final_hours=CASE
                    WHEN ai_generated=FALSE OR final_hours=suggested_hours THEN @suggested_hours
                    ELSE final_hours
                END,
                suggested_hours=@suggested_hours,
                objective=@objective,
                detailed_activities=@detailed_activities::jsonb,
                technical_tasks=@technical_tasks::jsonb,
                deliverables=@deliverables::jsonb,
                customer_responsibilities=@customer_responsibilities::jsonb,
                us_signal_responsibilities=@us_signal_responsibilities::jsonb,
                prerequisites=@prerequisites::jsonb,
                dependencies=@dependencies::jsonb,
                assumptions=@assumptions::jsonb,
                open_questions=@open_questions::jsonb,
                acceptance_criteria=@acceptance_criteria::jsonb,
                validation_steps=@validation_steps::jsonb,
                risks=@risks::jsonb,
                loe_rationale=@loe_rationale,
                source_citation_ids=@source_citation_ids::jsonb,
                ai_generated=TRUE,
                updated_at=NOW()
            WHERE engagement_id=@engagement_id AND phase_code=@phase_code;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("phase_code", phase.PhaseCode);
        command.Parameters.AddWithValue("suggested_hours", phase.SuggestedHours);
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

    private static async Task InsertEmptyPhasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid engagementId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO module025_sow_gsd_phases(engagement_id, phase_code, sort_order)
            VALUES(@engagement_id, @phase_code, @sort_order)
            ON CONFLICT(engagement_id, phase_code) DO NOTHING;
            """;
        for (var index = 0; index < PhaseCodes.Length; index++)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("engagement_id", engagementId);
            command.Parameters.AddWithValue("phase_code", PhaseCodes[index]);
            command.Parameters.AddWithValue("sort_order", index + 1);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<Module025EngagementRow?> LoadEngagementAsync(
        NpgsqlConnection connection,
        Guid engagementId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                engagement_id, engagement_number, owner_user_id, owner_display_name,
                owner_department_name, owner_team_name, customer_id, customer_name,
                customer_entry_mode, commercial_model, customer_program, gsd_template_key,
                account_executive_user_id, account_executive_name,
                resale_user_id, resale_name, service_overview,
                sow_sections::text, ai_metadata::text, status, is_active, revision,
                last_generated_at, confirmed_at, archived_at, created_at, updated_at
            FROM module025_sow_gsd_engagements
            WHERE engagement_id=@engagement_id;
            """;
        Module025EngagementRow? shell = null;
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("engagement_id", engagementId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            shell = new Module025EngagementRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetGuid(12), reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetGuid(14), reader.GetString(15), reader.GetString(16),
                ParseJson(reader.GetString(17), JsonValueKind.Object), ParseJson(reader.GetString(18), JsonValueKind.Object),
                reader.GetString(19), reader.GetBoolean(20), reader.GetInt32(21),
                reader.IsDBNull(22) ? null : reader.GetFieldValue<DateTimeOffset?>(22),
                reader.IsDBNull(23) ? null : reader.GetFieldValue<DateTimeOffset?>(23),
                reader.IsDBNull(24) ? null : reader.GetFieldValue<DateTimeOffset?>(24),
                reader.GetFieldValue<DateTimeOffset>(25), reader.GetFieldValue<DateTimeOffset>(26), Array.Empty<Module025PhaseRow>());
        }
        var phases = await LoadPhasesAsync(connection, engagementId, cancellationToken);
        return shell with { Phases = phases };
    }

    private static async Task<IReadOnlyList<Module025PhaseRow>> LoadPhasesAsync(
        NpgsqlConnection connection,
        Guid engagementId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT phase_code, sort_order, suggested_hours, final_hours, objective,
                   detailed_activities::text, technical_tasks::text, deliverables::text,
                   customer_responsibilities::text, us_signal_responsibilities::text,
                   prerequisites::text, dependencies::text, assumptions::text,
                   open_questions::text, acceptance_criteria::text, validation_steps::text,
                   risks::text, loe_rationale, source_citation_ids::text, ai_generated, updated_at
            FROM module025_sow_gsd_phases
            WHERE engagement_id=@engagement_id
            ORDER BY sort_order;
            """;
        var rows = new List<Module025PhaseRow>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Module025PhaseRow(
                reader.GetString(0), reader.GetInt16(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetString(4),
                ParseStringArray(reader.GetString(5)), ParseStringArray(reader.GetString(6)), ParseStringArray(reader.GetString(7)),
                ParseStringArray(reader.GetString(8)), ParseStringArray(reader.GetString(9)), ParseStringArray(reader.GetString(10)),
                ParseStringArray(reader.GetString(11)), ParseStringArray(reader.GetString(12)), ParseStringArray(reader.GetString(13)),
                ParseStringArray(reader.GetString(14)), ParseStringArray(reader.GetString(15)), ParseStringArray(reader.GetString(16)),
                reader.GetString(17), ParseGuidArray(reader.GetString(18)), reader.GetBoolean(19), reader.GetFieldValue<DateTimeOffset>(20)));
        }
        return rows;
    }

    private static object PublicEngagement(Module025EngagementRow engagement, Module025AccessContext access) => new
    {
        status = "module025_engagement_loaded",
        contract = WorkspaceContract,
        engagement = new
        {
            engagement.EngagementId,
            engagement.EngagementNumber,
            engagement.OwnerUserId,
            engagement.OwnerDisplayName,
            engagement.OwnerDepartmentName,
            engagement.OwnerTeamName,
            engagement.CustomerId,
            engagement.CustomerName,
            engagement.CustomerEntryMode,
            engagement.CommercialModel,
            engagement.CustomerProgram,
            engagement.GsdTemplateKey,
            gsdTemplate = engagement.GsdTemplateKey == Module025SowGsdDocumentExporter.HaeaGsdTemplateKey
                ? Module025SowGsdDocumentExporter.HaeaGsdDisplayName
                : "Standard GSD",
            engagement.AccountExecutiveUserId,
            engagement.AccountExecutiveName,
            engagement.ResaleUserId,
            engagement.ResaleName,
            engagement.ServiceOverview,
            engagement.SowSections,
            engagement.AiMetadata,
            engagement.Status,
            engagement.IsActive,
            engagement.Revision,
            engagement.LastGeneratedAt,
            engagement.ConfirmedAt,
            engagement.ArchivedAt,
            engagement.CreatedAt,
            engagement.UpdatedAt,
            suggestedHours = engagement.Phases.Sum(phase => phase.SuggestedHours),
            finalHours = engagement.Phases.Sum(phase => phase.FinalHours),
            phases = engagement.Phases.Select(phase => new
            {
                phase.PhaseCode,
                label = PhaseLabel(phase.PhaseCode),
                phase.SortOrder,
                phase.SuggestedHours,
                phase.FinalHours,
                phase.Objective,
                phase.DetailedActivities,
                phase.TechnicalTasks,
                phase.Deliverables,
                phase.CustomerResponsibilities,
                phase.UsSignalResponsibilities,
                phase.Prerequisites,
                phase.Dependencies,
                phase.Assumptions,
                phase.OpenQuestions,
                phase.AcceptanceCriteria,
                phase.ValidationSteps,
                phase.Risks,
                phase.LoeRationale,
                phase.SourceCitationIds,
                phase.AiGenerated,
                phase.UpdatedAt
            }).ToArray()
        },
        access = new
        {
            canEdit = access.CanWriteOwned(engagement.OwnerUserId) && engagement.IsActive && engagement.Status != "archived",
            canConfirm = access.CanWriteOwned(engagement.OwnerUserId) && engagement.IsActive && engagement.Status != "archived",
            canArchive = access.CanWriteOwned(engagement.OwnerUserId),
            canDownload = access.CanViewOwned(engagement.OwnerUserId) && engagement.Status == "confirmed",
            readOnlyManagerView = access.IsManager && !access.IsAdministrator && engagement.OwnerUserId != access.EffectiveUserId,
            access.IsViewAs
        },
        stateChanged = false
    };

    private static async Task<Module025AccessContext?> ResolveAccessAsync(
        NpgsqlConnection connection,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId") ?? actual;
        if (!actual.HasValue || !effective.HasValue) return null;
        const string sql = """
            SELECT
                COALESCE(NULLIF(app_user.display_name,''),app_user.email,''),
                COALESCE(app_user.email,''),
                COALESCE(NULLIF(app_user.department_name,''),NULLIF(app_user.department,''),''),
                COALESCE(NULLIF(app_user.team_name,''),''),
                COALESCE(string_agg(DISTINCT upper(role.role_code),',' ORDER BY upper(role.role_code)),'')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.user_id=@user_id AND app_user.is_active=TRUE
            GROUP BY app_user.user_id, app_user.display_name, app_user.email,
                     app_user.department_name, app_user.department, app_user.team_name;
            """;
        string displayName;
        string email;
        string department;
        string team;
        IReadOnlySet<string> roles;
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("user_id", effective.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            displayName = reader.GetString(0);
            email = reader.GetString(1);
            department = reader.GetString(2);
            team = reader.GetString(3);
            roles = Split(reader.GetString(4));
        }

        var administrator = roles.Overlaps(AdministratorRoles);
        var solutionArchitect = roles.Overlaps(SolutionArchitectRoles);
        var directReports = administrator
            ? await LoadAllSolutionArchitectIdsAsync(connection, cancellationToken)
            : await LoadDirectReportSolutionArchitectIdsAsync(connection, effective.Value, department, cancellationToken);
        var manager = roles.Overlaps(ManagerRoles) || directReports.Count > 0;
        var visible = new HashSet<Guid>(directReports);
        visible.Add(effective.Value);
        return new Module025AccessContext(
            actual.Value,
            effective.Value,
            displayName,
            email,
            department,
            team,
            roles,
            ProjectPulseActualSessionAuthority.IsViewAs(context) || actual.Value != effective.Value,
            administrator,
            solutionArchitect,
            manager,
            visible);
    }

    private static async Task<IReadOnlySet<Guid>> LoadDirectReportSolutionArchitectIdsAsync(
        NpgsqlConnection connection,
        Guid managerUserId,
        string departmentName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT employee.user_id
            FROM reporting_relationships relationship
            JOIN app_users employee ON employee.user_id=relationship.employee_user_id AND employee.is_active=TRUE
            JOIN app_user_role_assignments assignment ON assignment.user_id=employee.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE (relationship.manager_user_id=@manager_user_id OR relationship.team_lead_user_id=@manager_user_id)
              AND relationship.effective_start_date<=CURRENT_DATE
              AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
              AND upper(role.role_code)=ANY(@roles)
              AND (@department_name='' OR lower(COALESCE(NULLIF(employee.department_name,''),employee.department,''))=lower(@department_name));
            """;
        var rows = new HashSet<Guid>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("manager_user_id", managerUserId);
        command.Parameters.AddWithValue("roles", SolutionArchitectRoles.ToArray());
        command.Parameters.AddWithValue("department_name", departmentName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(reader.GetGuid(0));
        return rows;
    }

    private static async Task<IReadOnlySet<Guid>> LoadAllSolutionArchitectIdsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT app_user.user_id
            FROM app_users app_user
            JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.is_active=TRUE AND upper(role.role_code)=ANY(@roles);
            """;
        var rows = new HashSet<Guid>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("roles", SolutionArchitectRoles.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(reader.GetGuid(0));
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadVisibleSolutionArchitectsAsync(
        NpgsqlConnection connection,
        Module025AccessContext access,
        CancellationToken cancellationToken)
    {
        var ids = access.VisibleSolutionArchitectIds.ToArray();
        if (ids.Length == 0) return Array.Empty<object>();
        const string sql = """
            SELECT user_id, COALESCE(NULLIF(display_name,''),email,''), COALESCE(email,''),
                   COALESCE(NULLIF(department_name,''),NULLIF(department,''),''), COALESCE(team_name,'')
            FROM app_users
            WHERE user_id=ANY(@ids) AND is_active=TRUE
            ORDER BY display_name NULLS LAST, email;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new { userId = reader.GetGuid(0), displayName = reader.GetString(1), email = reader.GetString(2), departmentName = reader.GetString(3), teamName = reader.GetString(4) });
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadCustomersAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT client_id, COALESCE(client_code,''), client_name
            FROM clients
            WHERE is_active=TRUE
            ORDER BY client_name
            LIMIT 1000;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new { customerId = reader.GetGuid(0), customerCode = reader.GetString(1), customerName = reader.GetString(2) });
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadPeopleByRoleAsync(
        NpgsqlConnection connection,
        IReadOnlyList<string> roleCodes,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT app_user.user_id,
                   COALESCE(NULLIF(app_user.display_name,''),app_user.email,''),
                   COALESCE(app_user.email,'')
            FROM app_users app_user
            JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.is_active=TRUE AND upper(role.role_code)=ANY(@roles)
            ORDER BY 2,3;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("roles", roleCodes.Select(value => value.ToUpperInvariant()).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new { userId = reader.GetGuid(0), displayName = reader.GetString(1), email = reader.GetString(2) });
        return rows;
    }

    private static async Task<CustomerSelection> ResolveCustomerAsync(
        NpgsqlConnection connection,
        Guid? customerId,
        string? customerName,
        string? requestedMode,
        CancellationToken cancellationToken)
    {
        var mode = string.Equals(requestedMode?.Trim(), "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "directory";
        if (mode == "manual")
        {
            var manualName = Clean(customerName, 500);
            return manualName.Length == 0
                ? new CustomerSelection(null, string.Empty, mode, Results.BadRequest(new { status = "manual_customer_name_required", message = "Enter the customer name when Customer not listed is selected." }))
                : new CustomerSelection(null, manualName, mode, null);
        }
        if (!customerId.HasValue) return new CustomerSelection(null, Clean(customerName, 500), mode, null);
        await using var command = new NpgsqlCommand("SELECT client_name FROM clients WHERE client_id=@client_id AND is_active=TRUE;", connection);
        command.Parameters.AddWithValue("client_id", customerId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string name
            ? new CustomerSelection(customerId, name, mode, null)
            : new CustomerSelection(null, string.Empty, mode, Results.BadRequest(new { status = "customer_not_found", message = "The selected customer is not an active customer-directory record." }));
    }

    private static async Task<PersonSelection> ResolvePersonAsync(NpgsqlConnection connection, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return new PersonSelection(null, string.Empty);
        await using var command = new NpgsqlCommand("SELECT COALESCE(NULLIF(display_name,''),email,'') FROM app_users WHERE user_id=@user_id AND is_active=TRUE;", connection);
        command.Parameters.AddWithValue("user_id", userId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName ? new PersonSelection(userId, displayName) : new PersonSelection(null, string.Empty);
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid engagementId,
        Guid actorUserId,
        int revision,
        string eventType,
        string summary,
        object evidence,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO module025_sow_gsd_events(
                engagement_id, event_type, actor_user_id, engagement_revision, summary, evidence_json)
            VALUES(@engagement_id,@event_type,@actor_user_id,@revision,@summary,@evidence_json::jsonb);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.AddWithValue("evidence_json", JsonSerializer.Serialize(evidence));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int?> ExecuteRevisionUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid engagementId,
        int revision,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("revision", revision);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<(NpgsqlConnection? Connection, Module025EngagementRow? Engagement, Module025AccessContext? Access, IResult? Error)> LoadWritableStateAsync(
        Guid engagementId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return (null, null, null, authorization);
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return (null, null, null, opened.Error);
        var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) { await connection.DisposeAsync(); return (null, null, null, MigrationRequired()); }
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) { await connection.DisposeAsync(); return (null, null, null, SessionRequired()); }
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (engagement is null) { await connection.DisposeAsync(); return (null, null, null, Results.NotFound(new { status = "module025_engagement_not_found" })); }
        if (!access.CanWriteOwned(engagement.OwnerUserId)) { await connection.DisposeAsync(); return (null, null, null, Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_edit")); }
        return (connection, engagement, access, null);
    }

    private static async Task<(NpgsqlConnection? Connection, Module025EngagementRow? Engagement, Module025AccessContext? Access, IResult? Error)> LoadReadableStateAsync(
        Guid engagementId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return (null, null, null, authorization);
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return (null, null, null, opened.Error);
        var connection = opened.Connection!;
        if (!await WorkspaceSchemaReadyAsync(connection, cancellationToken)) { await connection.DisposeAsync(); return (null, null, null, MigrationRequired()); }
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) { await connection.DisposeAsync(); return (null, null, null, SessionRequired()); }
        var engagement = await LoadEngagementAsync(connection, engagementId, cancellationToken);
        if (engagement is null) { await connection.DisposeAsync(); return (null, null, null, Results.NotFound(new { status = "module025_engagement_not_found" })); }
        if (!access.CanViewOwned(engagement.OwnerUserId)) { await connection.DisposeAsync(); return (null, null, null, Forbidden("module025_owner_scope")); }
        return (connection, engagement, access, null);
    }

    private static string BuildGenerationPrompt(Module025EngagementRow engagement) => $"""
        Create an implementation-grade Statement of Work and General Solution Design effort draft from the Service Overview below.

        Service Overview:
        {engagement.ServiceOverview}

        Commercial model: {(engagement.CommercialModel == "fixed" ? "Fixed Price" : "Time & Materials")}
        Customer program: {engagement.CustomerProgram}

        Expand the requested services into exactly the delivery lifecycle needed for Module 025:
        Plan, Design, Implement, Validate, and Release.

        For every supported work package and every applicable phase, provide detailed, specific execution content suitable for a Solution Architect, delivery engineer, project manager, customer reviewer, and commercial reviewer. Include:
        - objective and expected outcome;
        - detailed activities in execution order;
        - technical tasks and configuration activities supported by the supplied overview or authorized private evidence;
        - inputs, outputs, and named deliverables;
        - US Signal responsibilities;
        - customer responsibilities;
        - prerequisites and dependencies;
        - assumptions and explicit open questions;
        - measurable acceptance criteria;
        - validation steps;
        - risks and delivery considerations;
        - estimated engineering hours for the phase/work package.

        Do not use vague tasks such as 'implement solution', 'validate system', or 'complete design'. Describe what will actually be planned, designed, implemented, validated, and released.
        Do not fabricate products, versions, quantities, licensing, models, access, interfaces, customer decisions, dates, prices, or technical facts that are not supported. Convert unsupported material into explicit assumptions or open questions.
        Estimated hours are a reviewable AI suggestion only; never present them as approved commercial effort. The Solution Architect will review and may change every hour value before confirmation.
        """;

    private static string ClassifyPhase(string? phase, string? name, string? description)
    {
        var value = $"{phase} {name} {description}".ToLowerInvariant();
        if (value.Contains("release") || value.Contains("handoff") || value.Contains("closeout") || value.Contains("transition")) return "release";
        if (value.Contains("validat") || value.Contains("test") || value.Contains("acceptance") || value.Contains("verify")) return "validate";
        if (value.Contains("implement") || value.Contains("deploy") || value.Contains("configur") || value.Contains("migrat") || value.Contains("install")) return "implement";
        if (value.Contains("design") || value.Contains("architect") || value.Contains("solution design")) return "design";
        return "plan";
    }

    private static void AddDistinct(List<string> target, string? value)
    {
        var clean = Clean(value, 12_000);
        if (clean.Length > 0 && !target.Contains(clean, StringComparer.OrdinalIgnoreCase)) target.Add(clean);
    }

    private static void AddDistinct(List<string> target, IEnumerable<string>? values)
    {
        foreach (var value in values ?? Array.Empty<string>()) AddDistinct(target, value);
    }

    private static async Task<(NpgsqlConnection? Connection, IResult? Error)> OpenConnectionAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return (null, Results.Json(new { status = "module025_storage_unavailable", message = "The Module 025 database connection is not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable));
        try
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return (connection, null);
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Module025SowGsd")
                .LogWarning(exception, "Module 025 storage connection could not be opened.");
            return (null, Results.Json(new { status = "module025_storage_unavailable", message = "The Module 025 workspace storage is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
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
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private static async Task<bool> WorkspaceSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.module025_sow_gsd_engagements') IS NOT NULL
               AND to_regclass('public.module025_sow_gsd_phases') IS NOT NULL
               AND to_regclass('public.module025_sow_gsd_events') IS NOT NULL;
            """, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<IResult?> AuthorizeViewAsync(HttpContext context) =>
        await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ViewRoles,
            new[] { "VIEW_SOW_GSD_025", "MANAGE_SOW_GSD_025", "MANAGE_ALL" });

    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var values)) return true;
        if (!Uri.TryCreate(values.ToString(), UriKind.Absolute, out var origin)) return false;
        return string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
               && origin.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80));
    }

    private static string NormalizeCommercialModel(string? value) =>
        string.Equals(value?.Trim(), "fixed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "fixed_price", StringComparison.OrdinalIgnoreCase)
            ? "fixed"
            : "time_and_materials";

    private static string NormalizeCustomerProgram(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "toyota" or "hyundai" ? normalized : "standard";
    }

    private static string TemplateKey(string customerProgram) =>
        customerProgram is "toyota" or "hyundai"
            ? Module025SowGsdDocumentExporter.HaeaGsdTemplateKey
            : Module025SowGsdDocumentExporter.StandardGsdTemplateKey;

    private static string? NormalizePhaseCode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return PhaseCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? normalized : null;
    }

    private static string PhaseLabel(string phaseCode) => phaseCode switch
    {
        "plan" => "Plan",
        "design" => "Design",
        "implement" => "Implement",
        "validate" => "Validate",
        "release" => "Release",
        _ => phaseCode
    };

    private static IReadOnlyList<string> CleanList(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
            .Select(value => Clean(value, 12_000))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();

    private static string Clean(string? value, int maximum)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static JsonElement ParseJson(string value, JsonValueKind expected)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? (expected == JsonValueKind.Array ? "[]" : "{}") : value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var fallback = JsonDocument.Parse(expected == JsonValueKind.Array ? "[]" : "{}");
            return fallback.RootElement.Clone();
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "[]" : value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
                : Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<Guid> ParseGuidArray(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "[]" : value);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<Guid>();
            return document.RootElement.EnumerateArray()
                .Select(item => Guid.TryParse(item.ToString(), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
        }
        catch (JsonException) { return Array.Empty<Guid>(); }
    }

    private static IReadOnlySet<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string SafeFileName(string value) =>
        new(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character).ToArray());

    private static IResult SessionRequired() => Results.Unauthorized();
    private static IResult Forbidden(string capability) => Results.Json(new { status = "module025_forbidden", capability, message = "Your current Pulse role or reporting scope does not grant this Module 025 operation." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult MigrationRequired() => Results.Json(new { status = "module025_migration_required", migration = MigrationId, message = "Apply the Module 025 SOW/GSD workspace migration before using this workflow." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult OriginRejected() => Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult RequestTooLarge() => Results.Json(new { status = "request_too_large", message = $"Module 025 request bodies are limited to {MaximumRequestBytes} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge);
    private static IResult RevisionConflict(int currentRevision) => Results.Conflict(new { status = "module025_revision_conflict", currentRevision, message = "This SOW/GSD changed after it was loaded. Reload the latest revision before saving again." });
    private static IResult StateConflict(string status, string message) => Results.Conflict(new { status, message });

    private sealed record CustomerSelection(Guid? CustomerId, string CustomerName, string Mode, IResult? Error);
    private sealed record PersonSelection(Guid? UserId, string DisplayName);

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
        internal HashSet<Guid> CitationIds { get; } = new();
    }
}
