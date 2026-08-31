using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 025 persistent SOW/GSD authoring workspace. Solution Architects own
/// editable workspaces; their manager chain receives read visibility; final
/// document export remains gated by explicit Solution Architect confirmation.
/// AI-suggested phase effort is retained separately from the reviewed final LOE.
/// </summary>
public static class SowGsdWorkspaceModule
{
    private const string MigrationFile = "098_module_025_sow_gsd_workspace.sql";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        "MANAGER", "SOLUTION_ARCHITECT_MANAGER", "SOLUTIONS_ARCHITECT_MANAGER",
        "SOLUTION_ARCHITECTURE_MANAGER", "ENGINEERING_MANAGER"
    };
    private static readonly string[] RequiredPhases = ["plan", "design", "implement", "validate", "release"];

    public static WebApplication MapSowGsdWorkspaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/sow-gsd/v1/options", (Func<HttpContext, CancellationToken, Task<IResult>>)GetOptionsAsync);
        app.MapGet("/api/sow-gsd/v1/workspaces", (Func<string?, Guid?, string?, HttpContext, CancellationToken, Task<IResult>>)ListAsync);
        app.MapGet("/api/sow-gsd/v1/workspaces/{workspaceId:guid}", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetAsync);
        app.MapPost("/api/sow-gsd/v1/workspaces", (Func<WorkspaceSaveRequest, HttpContext, CancellationToken, Task<IResult>>)CreateAsync);
        app.MapPut("/api/sow-gsd/v1/workspaces/{workspaceId:guid}", (Func<Guid, WorkspaceSaveRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateAsync);
        app.MapPost("/api/sow-gsd/v1/workspaces/{workspaceId:guid}/confirm", (Func<Guid, WorkspaceActionRequest, HttpContext, CancellationToken, Task<IResult>>)ConfirmAsync);
        app.MapPost("/api/sow-gsd/v1/workspaces/{workspaceId:guid}/archive", (Func<Guid, WorkspaceActionRequest, HttpContext, CancellationToken, Task<IResult>>)ArchiveAsync);
        app.MapPost("/api/sow-gsd/v1/workspaces/{workspaceId:guid}/restore", (Func<Guid, WorkspaceActionRequest, HttpContext, CancellationToken, Task<IResult>>)RestoreAsync);
        app.MapGet("/api/sow-gsd/v1/workspaces/{workspaceId:guid}/sow.docx", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadSowAsync);
        app.MapGet("/api/sow-gsd/v1/workspaces/{workspaceId:guid}/gsd.xlsx", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadGsdAsync);
        return app;
    }

    private static async Task<IResult> GetOptionsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;

        try
        {
            var customers = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT client_id, COALESCE(client_code,''), COALESCE(client_name,'')
                FROM clients
                WHERE COALESCE(client_name,'') <> ''
                ORDER BY lower(client_name)
                LIMIT 1000;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    customers.Add(new { clientId = reader.GetGuid(0), clientCode = reader.GetString(1), clientName = reader.GetString(2) });
            }

            var accountExecutives = await LoadPeopleByRolesAsync(
                connection,
                ["ACCOUNT_EXECUTIVE", "SALES_ACCOUNT_EXECUTIVE", "AE"],
                cancellationToken);
            var resalePeople = await LoadPeopleByRolesAsync(
                connection,
                ["INSIDE_SALES", "RESALE", "RESALE_SPECIALIST", "SALES_SUPPORT", "ISR", "RSA"],
                cancellationToken);
            var solutionArchitects = await LoadVisibleSolutionArchitectsAsync(connection, access.Context!, cancellationToken);

            return Results.Ok(new
            {
                status = "sow_gsd_options_loaded",
                module = "025",
                customers,
                accountExecutives,
                resalePeople,
                solutionArchitects,
                access = new
                {
                    access.Context!.EffectiveUserId,
                    access.Context.DisplayName,
                    access.Context.Email,
                    access.Context.IsAdministrator,
                    access.Context.IsSolutionArchitect,
                    access.Context.IsManager,
                    access.Context.IsViewAs,
                    canCreate = !access.Context.IsViewAs && (access.Context.IsSolutionArchitect || access.Context.IsAdministrator),
                    canSelectSolutionArchitect = access.Context.IsManager || access.Context.IsAdministrator
                },
                contractTypes = new[]
                {
                    new { value = "T_AND_M", label = "Time & Materials" },
                    new { value = "FIXED", label = "Fixed Price" }
                },
                customerTypes = new[]
                {
                    new { value = "STANDARD", label = "Standard" },
                    new { value = "TOYOTA", label = "Toyota" },
                    new { value = "HYUNDAI", label = "Hyundai" }
                },
                templates = new[]
                {
                    new { code = "STANDARD", label = "Standard GSD" },
                    new { code = "HAEA_STAFF_AUG_KUS_UVO", label = "HAEA Staff Aug GSD KUS UVO Telematics 1" }
                },
                phases = RequiredPhases.Select(value => value.ToUpperInvariant()).ToArray()
            });
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> ListAsync(
        string? status,
        Guid? ownerUserId,
        string? search,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;

        var requestedOwner = ownerUserId;
        if (requestedOwner.HasValue
            && !await CanViewOwnerAsync(connection, access.Context!, requestedOwner.Value, cancellationToken))
            return Forbidden("sow_gsd_owner_scope");

        var archived = string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase);
        var query = Clean(search, 200);
        var rows = new List<object>();

        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    workspace.sow_gsd_workspace_id,
                    workspace.sow_gsd_reference,
                    workspace.owner_solution_architect_user_id,
                    COALESCE(NULLIF(owner.display_name,''), owner.email, ''),
                    workspace.customer_id,
                    workspace.customer_name,
                    workspace.project_code,
                    workspace.project_name,
                    workspace.contract_type,
                    workspace.oem_customer_type,
                    workspace.gsd_template_code,
                    workspace.status,
                    workspace.revision_number,
                    workspace.updated_at,
                    workspace.last_autosaved_at,
                    workspace.review_confirmed_at,
                    workspace.archived_at,
                    workspace.final_plan_hours,
                    workspace.final_design_hours,
                    workspace.final_implement_hours,
                    workspace.final_validate_hours,
                    workspace.final_release_hours
                FROM sow_gsd_workspaces workspace
                JOIN app_users owner ON owner.user_id=workspace.owner_solution_architect_user_id
                WHERE ((@archived AND workspace.status='ARCHIVED') OR (NOT @archived AND workspace.status<>'ARCHIVED'))
                  AND (@owner_id IS NULL OR workspace.owner_solution_architect_user_id=@owner_id)
                  AND (
                    workspace.owner_solution_architect_user_id=@viewer_id
                    OR @is_admin
                    OR (@is_manager AND (
                        EXISTS (
                            SELECT 1 FROM reporting_relationships relationship
                            WHERE relationship.employee_user_id=workspace.owner_solution_architect_user_id
                              AND (relationship.manager_user_id=@viewer_id OR relationship.team_lead_user_id=@viewer_id)
                              AND relationship.effective_start_date<=CURRENT_DATE
                              AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                        )
                        OR EXISTS (
                            SELECT 1
                            FROM projectpulse_team_scope_assignments scope
                            JOIN app_users scoped_owner ON scoped_owner.user_id=workspace.owner_solution_architect_user_id
                            WHERE scope.scoped_user_id=@viewer_id AND scope.is_active=TRUE
                              AND (
                                (scope.team_name IS NOT NULL AND lower(COALESCE(scoped_owner.team_name,''))=lower(scope.team_name))
                                OR (scope.department_name IS NOT NULL AND lower(COALESCE(scoped_owner.department_name,scoped_owner.department,''))=lower(scope.department_name))
                              )
                        )
                        OR (@viewer_department<>'' AND lower(COALESCE(owner.department_name,owner.department,''))=lower(@viewer_department))
                    ))
                  )
                  AND (
                    @search='' OR lower(workspace.sow_gsd_reference) LIKE '%' || lower(@search) || '%'
                    OR lower(workspace.customer_name) LIKE '%' || lower(@search) || '%'
                    OR lower(workspace.project_name) LIKE '%' || lower(@search) || '%'
                    OR lower(COALESCE(workspace.project_code,'')) LIKE '%' || lower(@search) || '%'
                  )
                ORDER BY workspace.updated_at DESC
                LIMIT 500;
                """, connection);
            command.Parameters.AddWithValue("archived", archived);
            AddNullableUuid(command, "owner_id", requestedOwner);
            command.Parameters.AddWithValue("viewer_id", access.Context!.EffectiveUserId);
            command.Parameters.AddWithValue("is_admin", access.Context.IsAdministrator);
            command.Parameters.AddWithValue("is_manager", access.Context.IsManager);
            command.Parameters.AddWithValue("viewer_department", access.Context.DepartmentName);
            command.Parameters.AddWithValue("search", query);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ownerId = reader.GetGuid(2);
                rows.Add(new
                {
                    workspaceId = reader.GetGuid(0),
                    reference = reader.GetString(1),
                    ownerSolutionArchitectUserId = ownerId,
                    ownerSolutionArchitectName = reader.GetString(3),
                    customerId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                    customerName = reader.GetString(5),
                    projectCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                    projectName = reader.GetString(7),
                    contractType = reader.GetString(8),
                    oemCustomerType = reader.GetString(9),
                    gsdTemplateCode = reader.GetString(10),
                    status = reader.GetString(11),
                    revisionNumber = reader.GetInt32(12),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(13),
                    lastAutosavedAt = reader.IsDBNull(14) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(14),
                    reviewConfirmedAt = reader.IsDBNull(15) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(15),
                    archivedAt = reader.IsDBNull(16) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(16),
                    totalFinalHours = SumHours(reader, 17, 18, 19, 20, 21),
                    canEdit = CanEditOwner(access.Context!, ownerId)
                });
            }

            return Results.Ok(new { status = "sow_gsd_workspaces_loaded", archived, count = rows.Count, workspaces = rows });
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> GetAsync(Guid workspaceId, HttpContext context, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;

        var workspace = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        if (workspace is null) return Results.NotFound(new { status = "sow_gsd_workspace_not_found" });
        if (!await CanViewOwnerAsync(connection, access.Context!, workspace.OwnerSolutionArchitectUserId, cancellationToken))
            return Forbidden("sow_gsd_owner_scope");
        return Results.Ok(ToWorkspaceResponse(workspace, CanEditOwner(access.Context!, workspace.OwnerSolutionArchitectUserId)));
    }

    private static async Task<IResult> CreateAsync(WorkspaceSaveRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;
        if (access.Context!.IsViewAs) return ViewAsWriteBlocked();
        if (!access.Context.IsSolutionArchitect && !access.Context.IsAdministrator)
            return Forbidden("solution_architect_create");

        var ownerId = request.OwnerSolutionArchitectUserId ?? access.Context.EffectiveUserId;
        if (ownerId != access.Context.EffectiveUserId && !access.Context.IsAdministrator)
            return Forbidden("create_for_another_solution_architect");
        var validation = ValidateSaveRequest(request, isCreate: true);
        if (validation is not null) return validation;

        try
        {
            var customer = await ResolveCustomerAsync(connection, request, cancellationToken);
            if (customer.Error is not null) return customer.Error;
            var ae = await ResolvePersonAsync(connection, request.AccountExecutiveUserId, cancellationToken);
            var resale = await ResolvePersonAsync(connection, request.ResaleUserId, cancellationToken);
            var template = TemplateFor(request.OemCustomerType);

            await using var command = new NpgsqlCommand("""
                INSERT INTO sow_gsd_workspaces(
                    owner_solution_architect_user_id, customer_id, customer_name, customer_source,
                    opportunity_reference, project_code, project_name, service_overview, contract_type,
                    account_executive_user_id, account_executive_name, resale_user_id, resale_name,
                    oem_customer_type, gsd_template_code, status, phase_details,
                    created_by_user_id, updated_by_user_id, last_autosaved_at)
                VALUES(
                    @owner_id, @customer_id, @customer_name, @customer_source,
                    @opportunity_reference, @project_code, @project_name, @service_overview, @contract_type,
                    @ae_id, @ae_name, @resale_id, @resale_name,
                    @oem_type, @template_code, 'DRAFT', @phase_details, @actor_id, @actor_id, now())
                RETURNING sow_gsd_workspace_id;
                """, connection);
            command.Parameters.AddWithValue("owner_id", ownerId);
            AddNullableUuid(command, "customer_id", customer.CustomerId);
            command.Parameters.AddWithValue("customer_name", customer.CustomerName);
            command.Parameters.AddWithValue("customer_source", customer.Source);
            command.Parameters.AddWithValue("opportunity_reference", Clean(request.OpportunityReference, 200));
            command.Parameters.AddWithValue("project_code", Clean(request.ProjectCode, 120));
            command.Parameters.AddWithValue("project_name", Clean(request.ProjectName, 300));
            command.Parameters.AddWithValue("service_overview", Clean(request.ServiceOverview, 30000));
            command.Parameters.AddWithValue("contract_type", NormalizeContractType(request.ContractType));
            AddNullableUuid(command, "ae_id", request.AccountExecutiveUserId);
            command.Parameters.AddWithValue("ae_name", ae?.DisplayName ?? string.Empty);
            AddNullableUuid(command, "resale_id", request.ResaleUserId);
            command.Parameters.AddWithValue("resale_name", resale?.DisplayName ?? string.Empty);
            command.Parameters.AddWithValue("oem_type", NormalizeOemType(request.OemCustomerType));
            command.Parameters.AddWithValue("template_code", template.Code);
            AddJson(command, "phase_details", request.PhaseDetails, "{}");
            command.Parameters.AddWithValue("actor_id", access.Context.EffectiveUserId);
            var workspaceId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Module 025 did not return a workspace identifier."));
            await WriteEventAsync(connection, workspaceId, 1, "CREATED", access.Context.EffectiveUserId, new
            {
                contractType = NormalizeContractType(request.ContractType),
                oemCustomerType = NormalizeOemType(request.OemCustomerType),
                template = template.Code
            }, cancellationToken);
            var workspace = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
            return Results.Created($"/api/sow-gsd/v1/workspaces/{workspaceId}", ToWorkspaceResponse(workspace!, true));
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid workspaceId,
        WorkspaceSaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;
        if (access.Context!.IsViewAs) return ViewAsWriteBlocked();

        var existing = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        if (existing is null) return Results.NotFound(new { status = "sow_gsd_workspace_not_found" });
        if (!CanEditOwner(access.Context, existing.OwnerSolutionArchitectUserId))
            return Forbidden("sow_gsd_edit");
        if (existing.Status == "ARCHIVED")
            return Results.Conflict(new { status = "sow_gsd_workspace_archived", message = "Restore the workspace before editing it." });
        var validation = ValidateSaveRequest(request, isCreate: false);
        if (validation is not null) return validation;
        if (!request.ExpectedRevision.HasValue)
            return Results.BadRequest(new { status = "expected_revision_required" });

        try
        {
            var customer = await ResolveCustomerAsync(connection, request, cancellationToken);
            if (customer.Error is not null) return customer.Error;
            var ae = await ResolvePersonAsync(connection, request.AccountExecutiveUserId, cancellationToken);
            var resale = await ResolvePersonAsync(connection, request.ResaleUserId, cancellationToken);
            var template = TemplateFor(request.OemCustomerType);
            var nextStatus = NormalizeEditableStatus(request.Status);

            await using var command = new NpgsqlCommand("""
                UPDATE sow_gsd_workspaces
                SET customer_id=@customer_id,
                    customer_name=@customer_name,
                    customer_source=@customer_source,
                    opportunity_reference=@opportunity_reference,
                    project_code=@project_code,
                    project_name=@project_name,
                    service_overview=@service_overview,
                    contract_type=@contract_type,
                    account_executive_user_id=@ae_id,
                    account_executive_name=@ae_name,
                    resale_user_id=@resale_id,
                    resale_name=@resale_name,
                    oem_customer_type=@oem_type,
                    gsd_template_code=@template_code,
                    status=@status,
                    ai_draft=@ai_draft,
                    phase_details=@phase_details,
                    suggested_plan_hours=@suggested_plan_hours,
                    suggested_design_hours=@suggested_design_hours,
                    suggested_implement_hours=@suggested_implement_hours,
                    suggested_validate_hours=@suggested_validate_hours,
                    suggested_release_hours=@suggested_release_hours,
                    final_plan_hours=@final_plan_hours,
                    final_design_hours=@final_design_hours,
                    final_implement_hours=@final_implement_hours,
                    final_validate_hours=@final_validate_hours,
                    final_release_hours=@final_release_hours,
                    generation_provider=@generation_provider,
                    generation_citations=@generation_citations,
                    generation_warnings=@generation_warnings,
                    generation_missing_evidence=@generation_missing_evidence,
                    generation_confidence=@generation_confidence,
                    review_confirmed_at=NULL,
                    review_confirmed_by_user_id=NULL,
                    updated_by_user_id=@actor_id,
                    last_autosaved_at=now(),
                    revision_number=revision_number+1
                WHERE sow_gsd_workspace_id=@workspace_id
                  AND revision_number=@expected_revision
                RETURNING revision_number;
                """, connection);
            AddWorkspaceParameters(command, request, customer, ae, resale, template, access.Context.EffectiveUserId);
            command.Parameters.AddWithValue("status", nextStatus);
            command.Parameters.AddWithValue("workspace_id", workspaceId);
            command.Parameters.AddWithValue("expected_revision", request.ExpectedRevision.Value);
            var nextRevision = await command.ExecuteScalarAsync(cancellationToken);
            if (nextRevision is null)
            {
                var latest = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
                return Results.Conflict(new
                {
                    status = "sow_gsd_revision_conflict",
                    expectedRevision = request.ExpectedRevision,
                    currentRevision = latest?.RevisionNumber,
                    message = "This SOW/GSD changed after it was opened. Reload before saving so another edit is not overwritten."
                });
            }

            if (request.GenerationCompleted == true)
                await WriteEventAsync(connection, workspaceId, Convert.ToInt32(nextRevision), "GENERATED", access.Context.EffectiveUserId, new
                {
                    provider = Clean(request.GenerationProvider, 100),
                    confidence = request.GenerationConfidence,
                    detailedOutputRequired = true
                }, cancellationToken);

            var workspace = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
            return Results.Ok(ToWorkspaceResponse(workspace!, true));
        }
        catch (PostgresException exception) when (IsMigrationError(exception))
        {
            return MigrationRequired();
        }
    }

    private static async Task<IResult> ConfirmAsync(Guid workspaceId, WorkspaceActionRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;
        if (access.Context!.IsViewAs) return ViewAsWriteBlocked();
        var workspace = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        if (workspace is null) return Results.NotFound(new { status = "sow_gsd_workspace_not_found" });
        if (!CanEditOwner(access.Context, workspace.OwnerSolutionArchitectUserId)) return Forbidden("sow_gsd_confirm");
        if (workspace.Status == "ARCHIVED") return Results.Conflict(new { status = "sow_gsd_workspace_archived" });
        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != workspace.RevisionNumber)
            return RevisionConflict(request.ExpectedRevision.Value, workspace.RevisionNumber);

        var readiness = ValidateConfirmation(workspace);
        if (readiness.Count > 0)
            return Results.BadRequest(new
            {
                status = "sow_gsd_confirmation_incomplete",
                message = "Complete the detailed SOW/GSD review before confirming the output.",
                missing = readiness
            });

        await using var command = new NpgsqlCommand("""
            UPDATE sow_gsd_workspaces
            SET status='CONFIRMED', review_confirmed_at=now(), review_confirmed_by_user_id=@actor_id,
                updated_by_user_id=@actor_id, revision_number=revision_number+1
            WHERE sow_gsd_workspace_id=@workspace_id
            RETURNING revision_number;
            """, connection);
        command.Parameters.AddWithValue("actor_id", access.Context.EffectiveUserId);
        command.Parameters.AddWithValue("workspace_id", workspaceId);
        var revision = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await WriteEventAsync(connection, workspaceId, revision, "CONFIRMED", access.Context.EffectiveUserId, new { request.Reason }, cancellationToken);
        var updated = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        return Results.Ok(ToWorkspaceResponse(updated!, true));
    }

    private static async Task<IResult> ArchiveAsync(Guid workspaceId, WorkspaceActionRequest request, HttpContext context, CancellationToken cancellationToken) =>
        await ChangeArchiveStateAsync(workspaceId, request, archive: true, context, cancellationToken);

    private static async Task<IResult> RestoreAsync(Guid workspaceId, WorkspaceActionRequest request, HttpContext context, CancellationToken cancellationToken) =>
        await ChangeArchiveStateAsync(workspaceId, request, archive: false, context, cancellationToken);

    private static async Task<IResult> ChangeArchiveStateAsync(
        Guid workspaceId,
        WorkspaceActionRequest request,
        bool archive,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return access.Error;
        if (access.Context!.IsViewAs) return ViewAsWriteBlocked();
        var workspace = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        if (workspace is null) return Results.NotFound(new { status = "sow_gsd_workspace_not_found" });
        if (!CanEditOwner(access.Context, workspace.OwnerSolutionArchitectUserId)) return Forbidden("sow_gsd_archive");
        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != workspace.RevisionNumber)
            return RevisionConflict(request.ExpectedRevision.Value, workspace.RevisionNumber);

        var eventType = archive ? "ARCHIVED" : "RESTORED";
        await using var command = new NpgsqlCommand("""
            UPDATE sow_gsd_workspaces
            SET status=CASE
                    WHEN @archive THEN 'ARCHIVED'
                    WHEN review_confirmed_at IS NOT NULL THEN 'CONFIRMED'
                    ELSE 'DRAFT'
                END,
                archived_at=CASE WHEN @archive THEN now() ELSE NULL END,
                archived_by_user_id=CASE WHEN @archive THEN @actor_id ELSE NULL END,
                updated_by_user_id=@actor_id,
                revision_number=revision_number+1
            WHERE sow_gsd_workspace_id=@workspace_id
            RETURNING revision_number;
            """, connection);
        command.Parameters.AddWithValue("archive", archive);
        command.Parameters.AddWithValue("actor_id", access.Context.EffectiveUserId);
        command.Parameters.AddWithValue("workspace_id", workspaceId);
        var revision = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        await WriteEventAsync(connection, workspaceId, revision, eventType, access.Context.EffectiveUserId, new { request.Reason }, cancellationToken);
        var updated = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        return Results.Ok(ToWorkspaceResponse(updated!, true));
    }

    private static async Task<IResult> DownloadSowAsync(Guid workspaceId, HttpContext context, CancellationToken cancellationToken)
    {
        var loaded = await LoadExportWorkspaceAsync(workspaceId, context, cancellationToken);
        if (loaded.Error is not null) return loaded.Error;
        var bytes = BuildSowDocx(loaded.Workspace!);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{SafeFileName(loaded.Workspace!.Reference)}-SOW.docx");
    }

    private static async Task<IResult> DownloadGsdAsync(Guid workspaceId, HttpContext context, CancellationToken cancellationToken)
    {
        var loaded = await LoadExportWorkspaceAsync(workspaceId, context, cancellationToken);
        if (loaded.Error is not null) return loaded.Error;
        var bytes = BuildGsdWorkbook(loaded.Workspace!);
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{SafeFileName(loaded.Workspace!.Reference)}-GSD.xlsx");
    }

    private static async Task<ExportOutcome> LoadExportWorkspaceAsync(Guid workspaceId, HttpContext context, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(context, cancellationToken);
        if (opened.Error is not null) return new(null, opened.Error);
        await using var connection = opened.Connection!;
        var access = await LoadAccessAsync(connection, context, cancellationToken);
        if (access.Error is not null) return new(null, access.Error);
        var workspace = await LoadWorkspaceAsync(connection, workspaceId, cancellationToken);
        if (workspace is null) return new(null, Results.NotFound(new { status = "sow_gsd_workspace_not_found" }));
        if (!await CanViewOwnerAsync(connection, access.Context!, workspace.OwnerSolutionArchitectUserId, cancellationToken))
            return new(null, Forbidden("sow_gsd_owner_scope"));
        if (workspace.Status != "CONFIRMED" && !(workspace.Status == "ARCHIVED" && workspace.ReviewConfirmedAt.HasValue))
            return new(null, Results.Conflict(new
            {
                status = "sow_gsd_confirmation_required",
                message = "Confirm the reviewed SOW/GSD before downloading the final documents. Archived records remain downloadable only when they were confirmed before archival."
            }));
        return new(workspace, null);
    }

    private static IResult? ValidateSaveRequest(WorkspaceSaveRequest request, bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName) && !request.CustomerId.HasValue)
            return Results.BadRequest(new { status = "customer_required" });
        if (string.IsNullOrWhiteSpace(request.ProjectName))
            return Results.BadRequest(new { status = "project_name_required" });
        if (string.IsNullOrWhiteSpace(request.ServiceOverview))
            return Results.BadRequest(new { status = "service_overview_required", message = "Enter the Service Overview that Celar AI will expand into delivery-ready scope." });
        if (NormalizeContractType(request.ContractType) == string.Empty)
            return Results.BadRequest(new { status = "invalid_contract_type", allowed = new[] { "T_AND_M", "FIXED" } });
        if (NormalizeOemType(request.OemCustomerType) == string.Empty)
            return Results.BadRequest(new { status = "invalid_customer_type", allowed = new[] { "STANDARD", "TOYOTA", "HYUNDAI" } });
        if (!isCreate && request.ExpectedRevision.GetValueOrDefault() < 1)
            return Results.BadRequest(new { status = "expected_revision_required" });
        return null;
    }

    private static List<string> ValidateConfirmation(WorkspaceRow workspace)
    {
        var missing = new List<string>();
        if (workspace.AccountExecutiveUserId is null) missing.Add("Account Executive");
        if (workspace.ResaleUserId is null) missing.Add("Resale person");
        if (string.IsNullOrWhiteSpace(workspace.CustomerName)) missing.Add("Customer");
        if (string.IsNullOrWhiteSpace(workspace.ServiceOverview)) missing.Add("Service Overview");
        if (workspace.FinalPlanHours.GetValueOrDefault() <= 0) missing.Add("Plan final hours greater than zero");
        if (workspace.FinalDesignHours.GetValueOrDefault() <= 0) missing.Add("Design final hours greater than zero");
        if (workspace.FinalImplementHours.GetValueOrDefault() <= 0) missing.Add("Implement final hours greater than zero");
        if (workspace.FinalValidateHours.GetValueOrDefault() <= 0) missing.Add("Validate final hours greater than zero");
        if (workspace.FinalReleaseHours.GetValueOrDefault() <= 0) missing.Add("Release final hours greater than zero");

        if (workspace.PhaseDetails.ValueKind != JsonValueKind.Object)
        {
            missing.Add("Detailed Plan/Design/Implement/Validate/Release scope");
            return missing;
        }

        foreach (var phase in RequiredPhases)
        {
            var label = PhaseLabel(phase);
            if (!TryGetPropertyIgnoreCase(workspace.PhaseDetails, phase, out var phaseNode)
                || phaseNode.ValueKind != JsonValueKind.Object)
            {
                missing.Add($"{label} detailed scope");
                continue;
            }
            if (ReadText(phaseNode, "objective").Length < 40)
                missing.Add($"{label} phase objective");
            if (ReadText(phaseNode, "loeRationale").Length < 40)
                missing.Add($"{label} LOE rationale");
            if (!TryGetPropertyIgnoreCase(phaseNode, "activities", out var activities)
                || activities.ValueKind != JsonValueKind.Array
                || activities.GetArrayLength() == 0)
            {
                missing.Add($"{label} detailed activities");
                continue;
            }

            var activityNumber = 0;
            foreach (var activity in activities.EnumerateArray())
            {
                activityNumber++;
                var prefix = $"{label} activity {activityNumber}";
                if (ReadText(activity, "name").Length < 5) missing.Add($"{prefix} name");
                if (ReadText(activity, "description").Length < 80) missing.Add($"{prefix} detailed description");
                var detailedSteps = ReadStringArray(activity, "detailedSteps");
                if (detailedSteps.Count < 2 || detailedSteps.Sum(value => value.Length) < 120)
                    missing.Add($"{prefix} ordered execution steps");
                if (ReadStringArray(activity, "inputs").Count == 0) missing.Add($"{prefix} inputs");
                if (ReadStringArray(activity, "prerequisites").Count == 0) missing.Add($"{prefix} prerequisites/dependencies");
                if (ReadStringArray(activity, "usSignalResponsibilities").Count == 0) missing.Add($"{prefix} US Signal responsibilities");
                if (ReadStringArray(activity, "customerResponsibilities").Count == 0) missing.Add($"{prefix} customer responsibilities");
                if (ReadStringArray(activity, "outputs").Count == 0) missing.Add($"{prefix} outputs/deliverables");
                if (ReadStringArray(activity, "acceptanceCriteria").Count == 0) missing.Add($"{prefix} acceptance criteria");
                if (ReadStringArray(activity, "validationSteps").Count == 0) missing.Add($"{prefix} validation steps");
                if (ReadStringArray(activity, "risks").Count == 0) missing.Add($"{prefix} risks / explicit none identified");
                if (ReadStringArray(activity, "openQuestions").Count == 0) missing.Add($"{prefix} open questions / explicit none identified");
                if (ReadStringArray(activity, "requiredRoles").Count == 0) missing.Add($"{prefix} required roles");
                if (ReadDecimal(activity, "estimatedHours").GetValueOrDefault() <= 0) missing.Add($"{prefix} estimated hours");
            }
        }
        return missing;
    }

    private static void AddWorkspaceParameters(
        NpgsqlCommand command,
        WorkspaceSaveRequest request,
        CustomerResolution customer,
        PersonOption? ae,
        PersonOption? resale,
        TemplateOption template,
        Guid actorUserId)
    {
        AddNullableUuid(command, "customer_id", customer.CustomerId);
        command.Parameters.AddWithValue("customer_name", customer.CustomerName);
        command.Parameters.AddWithValue("customer_source", customer.Source);
        command.Parameters.AddWithValue("opportunity_reference", Clean(request.OpportunityReference, 200));
        command.Parameters.AddWithValue("project_code", Clean(request.ProjectCode, 120));
        command.Parameters.AddWithValue("project_name", Clean(request.ProjectName, 300));
        command.Parameters.AddWithValue("service_overview", Clean(request.ServiceOverview, 30000));
        command.Parameters.AddWithValue("contract_type", NormalizeContractType(request.ContractType));
        AddNullableUuid(command, "ae_id", request.AccountExecutiveUserId);
        command.Parameters.AddWithValue("ae_name", ae?.DisplayName ?? string.Empty);
        AddNullableUuid(command, "resale_id", request.ResaleUserId);
        command.Parameters.AddWithValue("resale_name", resale?.DisplayName ?? string.Empty);
        command.Parameters.AddWithValue("oem_type", NormalizeOemType(request.OemCustomerType));
        command.Parameters.AddWithValue("template_code", template.Code);
        AddJson(command, "ai_draft", request.AiDraft, "{}");
        AddJson(command, "phase_details", request.PhaseDetails, "{}");
        AddNullableDecimal(command, "suggested_plan_hours", request.SuggestedPlanHours);
        AddNullableDecimal(command, "suggested_design_hours", request.SuggestedDesignHours);
        AddNullableDecimal(command, "suggested_implement_hours", request.SuggestedImplementHours);
        AddNullableDecimal(command, "suggested_validate_hours", request.SuggestedValidateHours);
        AddNullableDecimal(command, "suggested_release_hours", request.SuggestedReleaseHours);
        AddNullableDecimal(command, "final_plan_hours", request.FinalPlanHours);
        AddNullableDecimal(command, "final_design_hours", request.FinalDesignHours);
        AddNullableDecimal(command, "final_implement_hours", request.FinalImplementHours);
        AddNullableDecimal(command, "final_validate_hours", request.FinalValidateHours);
        AddNullableDecimal(command, "final_release_hours", request.FinalReleaseHours);
        command.Parameters.AddWithValue("generation_provider", Clean(request.GenerationProvider, 100));
        AddJson(command, "generation_citations", request.GenerationCitations, "[]");
        AddJson(command, "generation_warnings", request.GenerationWarnings, "[]");
        AddJson(command, "generation_missing_evidence", request.GenerationMissingEvidence, "[]");
        AddNullableDecimal(command, "generation_confidence", request.GenerationConfidence);
        command.Parameters.AddWithValue("actor_id", actorUserId);
    }

    private static async Task<CustomerResolution> ResolveCustomerAsync(NpgsqlConnection connection, WorkspaceSaveRequest request, CancellationToken cancellationToken)
    {
        var source = request.CustomerId.HasValue ? "DIRECTORY" : "MANUAL";
        if (!request.CustomerId.HasValue)
        {
            var manualName = Clean(request.CustomerName, 300);
            return manualName.Length > 0
                ? new(null, manualName, source, null)
                : new(null, string.Empty, source, Results.BadRequest(new { status = "manual_customer_name_required" }));
        }

        await using var command = new NpgsqlCommand("SELECT COALESCE(client_name,'') FROM clients WHERE client_id=@client_id;", connection);
        command.Parameters.AddWithValue("client_id", request.CustomerId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string name && !string.IsNullOrWhiteSpace(name)
            ? new(request.CustomerId.Value, name.Trim(), source, null)
            : new(null, string.Empty, source, Results.BadRequest(new { status = "customer_not_found" }));
    }

    private static async Task<PersonOption?> ResolvePersonAsync(NpgsqlConnection connection, Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return null;
        await using var command = new NpgsqlCommand("""
            SELECT app_user.user_id,
                   COALESCE(NULLIF(app_user.display_name,''), NULLIF(concat_ws(' ',app_user.first_name,app_user.last_name),''), app_user.email, ''),
                   COALESCE(app_user.email,'')
            FROM app_users app_user
            WHERE app_user.user_id=@user_id AND app_user.is_active=TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PersonOption(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<List<PersonOption>> LoadPeopleByRolesAsync(NpgsqlConnection connection, string[] roles, CancellationToken cancellationToken)
    {
        var rows = new List<PersonOption>();
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT app_user.user_id,
                   COALESCE(NULLIF(app_user.display_name,''), NULLIF(concat_ws(' ',app_user.first_name,app_user.last_name),''), app_user.email, ''),
                   COALESCE(app_user.email,'')
            FROM app_users app_user
            JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.is_active=TRUE AND upper(role.role_code)=ANY(@roles)
            ORDER BY 2;
            """, connection);
        command.Parameters.AddWithValue("roles", roles.Select(value => value.ToUpperInvariant()).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new PersonOption(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    private static async Task<List<PersonOption>> LoadVisibleSolutionArchitectsAsync(NpgsqlConnection connection, AccessContext access, CancellationToken cancellationToken)
    {
        var rows = new List<PersonOption>();
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT app_user.user_id,
                   COALESCE(NULLIF(app_user.display_name,''), NULLIF(concat_ws(' ',app_user.first_name,app_user.last_name),''), app_user.email, ''),
                   COALESCE(app_user.email,'')
            FROM app_users app_user
            JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.is_active=TRUE
              AND upper(role.role_code)=ANY(@sa_roles)
              AND (
                app_user.user_id=@viewer_id
                OR @is_admin
                OR EXISTS (
                    SELECT 1 FROM reporting_relationships relationship
                    WHERE relationship.employee_user_id=app_user.user_id
                      AND (relationship.manager_user_id=@viewer_id OR relationship.team_lead_user_id=@viewer_id)
                      AND relationship.effective_start_date<=CURRENT_DATE
                      AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                )
                OR EXISTS (
                    SELECT 1 FROM projectpulse_team_scope_assignments scope
                    WHERE scope.scoped_user_id=@viewer_id AND scope.is_active=TRUE
                      AND (
                        (scope.team_name IS NOT NULL AND lower(COALESCE(app_user.team_name,''))=lower(scope.team_name))
                        OR (scope.department_name IS NOT NULL AND lower(COALESCE(app_user.department_name,app_user.department,''))=lower(scope.department_name))
                      )
                )
                OR (@is_manager AND @viewer_department<>''
                    AND lower(COALESCE(app_user.department_name,app_user.department,''))=lower(@viewer_department))
              )
            ORDER BY 2;
            """, connection);
        command.Parameters.AddWithValue("sa_roles", SolutionArchitectRoles.Select(value => value.ToUpperInvariant()).ToArray());
        command.Parameters.AddWithValue("viewer_id", access.EffectiveUserId);
        command.Parameters.AddWithValue("is_admin", access.IsAdministrator);
        command.Parameters.AddWithValue("is_manager", access.IsManager);
        command.Parameters.AddWithValue("viewer_department", access.DepartmentName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new PersonOption(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return rows;
    }

    private static async Task<AccessOutcome> LoadAccessAsync(NpgsqlConnection connection, HttpContext context, CancellationToken cancellationToken)
    {
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId") ?? actualUserId;
        if (!actualUserId.HasValue || !effectiveUserId.HasValue)
            return new(null, Results.Json(new { status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized));

        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(NULLIF(app_user.display_name,''), NULLIF(concat_ws(' ',app_user.first_name,app_user.last_name),''), app_user.email, ''),
                COALESCE(app_user.email,''),
                COALESCE(NULLIF(app_user.team_name,''),''),
                COALESCE(NULLIF(app_user.department_name,''),NULLIF(app_user.department,''),''),
                COALESCE(array_agg(DISTINCT upper(role.role_code)) FILTER (WHERE role.role_code IS NOT NULL), ARRAY[]::text[]),
                EXISTS (
                    SELECT 1 FROM reporting_relationships relationship
                    WHERE (relationship.manager_user_id=app_user.user_id OR relationship.team_lead_user_id=app_user.user_id)
                      AND relationship.effective_start_date<=CURRENT_DATE
                      AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                )
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            LEFT JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            WHERE app_user.user_id=@user_id AND app_user.is_active=TRUE
            GROUP BY app_user.user_id, app_user.display_name, app_user.first_name, app_user.last_name,
                     app_user.email, app_user.team_name, app_user.department_name, app_user.department;
            """, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new(null, Results.Json(new { status = "inactive_identity" }, statusCode: StatusCodes.Status403Forbidden));
        var roles = reader.GetFieldValue<string[]>(4).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isViewAs = actualUserId != effectiveUserId
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true);
        var access = new AccessContext(
            actualUserId.Value,
            effectiveUserId.Value,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            roles,
            roles.Overlaps(AdministratorRoles),
            roles.Overlaps(SolutionArchitectRoles),
            roles.Overlaps(ManagerRoles) || reader.GetBoolean(5),
            isViewAs);
        return new(access, null);
    }

    private static async Task<bool> CanViewOwnerAsync(NpgsqlConnection connection, AccessContext access, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId == access.EffectiveUserId || access.IsAdministrator) return true;
        if (!access.IsManager) return false;
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(
                SELECT 1
                FROM app_users owner
                WHERE owner.user_id=@owner_id AND owner.is_active=TRUE
                  AND (
                    EXISTS (
                        SELECT 1 FROM reporting_relationships relationship
                        WHERE relationship.employee_user_id=owner.user_id
                          AND (relationship.manager_user_id=@viewer_id OR relationship.team_lead_user_id=@viewer_id)
                          AND relationship.effective_start_date<=CURRENT_DATE
                          AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                    )
                    OR EXISTS (
                        SELECT 1 FROM projectpulse_team_scope_assignments scope
                        WHERE scope.scoped_user_id=@viewer_id AND scope.is_active=TRUE
                          AND (
                            (scope.team_name IS NOT NULL AND lower(COALESCE(owner.team_name,''))=lower(scope.team_name))
                            OR (scope.department_name IS NOT NULL AND lower(COALESCE(owner.department_name,owner.department,''))=lower(scope.department_name))
                          )
                    )
                    OR (@viewer_department<>'' AND lower(COALESCE(owner.department_name,owner.department,''))=lower(@viewer_department))
                  )
            );
            """, connection);
        command.Parameters.AddWithValue("owner_id", ownerUserId);
        command.Parameters.AddWithValue("viewer_id", access.EffectiveUserId);
        command.Parameters.AddWithValue("viewer_department", access.DepartmentName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static bool CanEditOwner(AccessContext access, Guid ownerUserId) =>
        !access.IsViewAs && (access.IsAdministrator || ownerUserId == access.EffectiveUserId);

    private static async Task<WorkspaceRow?> LoadWorkspaceAsync(NpgsqlConnection connection, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                workspace.sow_gsd_workspace_id, workspace.sow_gsd_reference,
                workspace.owner_solution_architect_user_id,
                COALESCE(NULLIF(owner.display_name,''), owner.email, ''),
                workspace.customer_id, workspace.customer_name, workspace.customer_source,
                workspace.opportunity_reference, workspace.project_code, workspace.project_name,
                workspace.service_overview, workspace.contract_type,
                workspace.account_executive_user_id, workspace.account_executive_name,
                workspace.resale_user_id, workspace.resale_name,
                workspace.oem_customer_type, workspace.gsd_template_code, workspace.status,
                workspace.ai_draft::text, workspace.phase_details::text,
                workspace.suggested_plan_hours, workspace.suggested_design_hours,
                workspace.suggested_implement_hours, workspace.suggested_validate_hours, workspace.suggested_release_hours,
                workspace.final_plan_hours, workspace.final_design_hours,
                workspace.final_implement_hours, workspace.final_validate_hours, workspace.final_release_hours,
                workspace.generation_provider, workspace.generation_citations::text,
                workspace.generation_warnings::text, workspace.generation_missing_evidence::text,
                workspace.generation_confidence, workspace.review_confirmed_at,
                workspace.archived_at, workspace.last_autosaved_at, workspace.revision_number,
                workspace.created_at, workspace.updated_at
            FROM sow_gsd_workspaces workspace
            JOIN app_users owner ON owner.user_id=workspace.owner_solution_architect_user_id
            WHERE workspace.sow_gsd_workspace_id=@workspace_id;
            """, connection);
        command.Parameters.AddWithValue("workspace_id", workspaceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new WorkspaceRow(
            reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
            TextOrNull(reader, 7), TextOrNull(reader, 8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
            GuidOrNull(reader, 12), TextOrNull(reader, 13), GuidOrNull(reader, 14), TextOrNull(reader, 15),
            reader.GetString(16), reader.GetString(17), reader.GetString(18), ParseJson(reader.GetString(19)), ParseJson(reader.GetString(20)),
            DecimalOrNull(reader, 21), DecimalOrNull(reader, 22), DecimalOrNull(reader, 23), DecimalOrNull(reader, 24), DecimalOrNull(reader, 25),
            DecimalOrNull(reader, 26), DecimalOrNull(reader, 27), DecimalOrNull(reader, 28), DecimalOrNull(reader, 29), DecimalOrNull(reader, 30),
            TextOrNull(reader, 31), ParseJson(reader.GetString(32)), ParseJson(reader.GetString(33)), ParseJson(reader.GetString(34)),
            DecimalOrNull(reader, 35), DateTimeOrNull(reader, 36), DateTimeOrNull(reader, 37), DateTimeOrNull(reader, 38),
            reader.GetInt32(39), reader.GetFieldValue<DateTimeOffset>(40), reader.GetFieldValue<DateTimeOffset>(41));
    }

    private static object ToWorkspaceResponse(WorkspaceRow row, bool canEdit) => new
    {
        status = "sow_gsd_workspace_loaded",
        workspaceId = row.WorkspaceId,
        reference = row.Reference,
        ownerSolutionArchitectUserId = row.OwnerSolutionArchitectUserId,
        ownerSolutionArchitectName = row.OwnerSolutionArchitectName,
        customerId = row.CustomerId,
        customerName = row.CustomerName,
        customerSource = row.CustomerSource,
        opportunityReference = row.OpportunityReference,
        projectCode = row.ProjectCode,
        projectName = row.ProjectName,
        serviceOverview = row.ServiceOverview,
        contractType = row.ContractType,
        accountExecutiveUserId = row.AccountExecutiveUserId,
        accountExecutiveName = row.AccountExecutiveName,
        resaleUserId = row.ResaleUserId,
        resaleName = row.ResaleName,
        oemCustomerType = row.OemCustomerType,
        gsdTemplateCode = row.GsdTemplateCode,
        gsdTemplateLabel = TemplateLabel(row.GsdTemplateCode),
        statusCode = row.Status,
        aiDraft = row.AiDraft,
        phaseDetails = row.PhaseDetails,
        suggestedHours = new { plan = row.SuggestedPlanHours, design = row.SuggestedDesignHours, implement = row.SuggestedImplementHours, validate = row.SuggestedValidateHours, release = row.SuggestedReleaseHours },
        finalHours = new { plan = row.FinalPlanHours, design = row.FinalDesignHours, implement = row.FinalImplementHours, validate = row.FinalValidateHours, release = row.FinalReleaseHours },
        generation = new
        {
            provider = row.GenerationProvider,
            citations = row.GenerationCitations,
            warnings = row.GenerationWarnings,
            missingEvidence = row.GenerationMissingEvidence,
            confidence = row.GenerationConfidence
        },
        row.ReviewConfirmedAt,
        row.ArchivedAt,
        row.LastAutosavedAt,
        row.RevisionNumber,
        row.CreatedAt,
        row.UpdatedAt,
        canEdit,
        canDownload = row.Status == "CONFIRMED" || (row.Status == "ARCHIVED" && row.ReviewConfirmedAt.HasValue)
    };

    private static byte[] BuildGsdWorkbook(WorkspaceRow workspace)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("GSD");
        sheet.Cell("A1").Value = "General Solution Design / Level of Effort";
        sheet.Cell("A3").Value = "SOW/GSD Reference"; sheet.Cell("B3").Value = workspace.Reference;
        sheet.Cell("A4").Value = "Customer"; sheet.Cell("B4").Value = workspace.CustomerName;
        sheet.Cell("A5").Value = "Project"; sheet.Cell("B5").Value = workspace.ProjectName;
        sheet.Cell("A6").Value = "Contract Type"; sheet.Cell("B6").Value = workspace.ContractType == "FIXED" ? "Fixed Price" : "Time & Materials";
        sheet.Cell("A7").Value = "Solution Architect"; sheet.Cell("B7").Value = workspace.OwnerSolutionArchitectName;
        sheet.Cell("A8").Value = "Account Executive"; sheet.Cell("B8").Value = workspace.AccountExecutiveName ?? string.Empty;
        sheet.Cell("A9").Value = "Resale"; sheet.Cell("B9").Value = workspace.ResaleName ?? string.Empty;
        sheet.Cell("A10").Value = "Customer / OEM Type"; sheet.Cell("B10").Value = workspace.OemCustomerType;
        sheet.Cell("A11").Value = "GSD Template"; sheet.Cell("B11").Value = TemplateLabel(workspace.GsdTemplateCode);
        sheet.Cell("A13").Value = "Service Overview"; sheet.Cell("B13").Value = workspace.ServiceOverview;
        sheet.Range("B13:E16").Merge();
        sheet.Cell("B13").Style.Alignment.WrapText = true;
        sheet.Cell("A18").Value = "Phase";
        sheet.Cell("B18").Value = "AI Suggested Hours";
        sheet.Cell("C18").Value = "SA Final Hours";
        sheet.Cell("D18").Value = "LOE Rationale";
        sheet.Cell("E18").Value = "Detailed Scope Summary";
        var phaseRows = new[]
        {
            ("Plan", workspace.SuggestedPlanHours, workspace.FinalPlanHours, "plan"),
            ("Design", workspace.SuggestedDesignHours, workspace.FinalDesignHours, "design"),
            ("Implement", workspace.SuggestedImplementHours, workspace.FinalImplementHours, "implement"),
            ("Validate", workspace.SuggestedValidateHours, workspace.FinalValidateHours, "validate"),
            ("Release", workspace.SuggestedReleaseHours, workspace.FinalReleaseHours, "release")
        };
        var row = 19;
        foreach (var phase in phaseRows)
        {
            sheet.Cell(row, 1).Value = phase.Item1;
            sheet.Cell(row, 2).Value = phase.Item2 ?? 0;
            sheet.Cell(row, 3).Value = phase.Item3 ?? 0;
            if (TryGetPropertyIgnoreCase(workspace.PhaseDetails, phase.Item4, out var phaseNode))
            {
                sheet.Cell(row, 4).Value = ReadText(phaseNode, "loeRationale");
                sheet.Cell(row, 5).Value = PhaseSummary(phaseNode);
            }
            row++;
        }
        sheet.Cell(row + 1, 1).Value = "Total";
        sheet.Cell(row + 1, 2).FormulaA1 = $"SUM(B19:B{row - 1})";
        sheet.Cell(row + 1, 3).FormulaA1 = $"SUM(C19:C{row - 1})";
        sheet.Columns(1, 5).AdjustToContents();
        sheet.Column(4).Width = 55;
        sheet.Column(5).Width = 85;
        sheet.Rows(18, row + 1).Style.Alignment.WrapText = true;
        sheet.Row(13).Height = 75;
        sheet.SheetView.FreezeRows(18);

        var detail = workbook.AddWorksheet("Detailed Scope");
        var headers = new[] { "Phase", "WBS", "Activity", "Description", "Detailed Steps", "Inputs", "Outputs / Deliverables", "Prerequisites / Dependencies", "US Signal Responsibilities", "Customer Responsibilities", "Acceptance Criteria", "Validation Steps", "Risks", "Open Questions", "Required Roles", "Predecessors", "Source Citations", "Assumption", "Estimated Hours" };
        for (var index = 0; index < headers.Length; index++) detail.Cell(1, index + 1).Value = headers[index];
        var detailRow = 2;
        foreach (var phase in RequiredPhases)
        {
            if (!TryGetPropertyIgnoreCase(workspace.PhaseDetails, phase, out var phaseNode)
                || !TryGetPropertyIgnoreCase(phaseNode, "activities", out var activities)
                || activities.ValueKind != JsonValueKind.Array) continue;
            foreach (var activity in activities.EnumerateArray())
            {
                detail.Cell(detailRow, 1).Value = PhaseLabel(phase);
                detail.Cell(detailRow, 2).Value = ReadText(activity, "wbs");
                detail.Cell(detailRow, 3).Value = ReadText(activity, "name");
                detail.Cell(detailRow, 4).Value = ReadText(activity, "description");
                detail.Cell(detailRow, 5).Value = JoinLines(ReadStringArray(activity, "detailedSteps"), numbered: true);
                detail.Cell(detailRow, 6).Value = JoinLines(ReadStringArray(activity, "inputs"));
                detail.Cell(detailRow, 7).Value = JoinLines(ReadStringArray(activity, "outputs"));
                detail.Cell(detailRow, 8).Value = JoinLines(ReadStringArray(activity, "prerequisites"));
                detail.Cell(detailRow, 9).Value = JoinLines(ReadStringArray(activity, "usSignalResponsibilities"));
                detail.Cell(detailRow, 10).Value = JoinLines(ReadStringArray(activity, "customerResponsibilities"));
                detail.Cell(detailRow, 11).Value = JoinLines(ReadStringArray(activity, "acceptanceCriteria"));
                detail.Cell(detailRow, 12).Value = JoinLines(ReadStringArray(activity, "validationSteps"));
                detail.Cell(detailRow, 13).Value = JoinLines(ReadStringArray(activity, "risks"));
                detail.Cell(detailRow, 14).Value = JoinLines(ReadStringArray(activity, "openQuestions"));
                detail.Cell(detailRow, 15).Value = JoinLines(ReadStringArray(activity, "requiredRoles"));
                detail.Cell(detailRow, 16).Value = JoinLines(ReadStringArray(activity, "predecessors"));
                detail.Cell(detailRow, 17).Value = JoinLines(ReadStringArray(activity, "citationIds"));
                detail.Cell(detailRow, 18).Value = ReadBoolean(activity, "isAssumption") ? "Yes" : "No";
                detail.Cell(detailRow, 19).Value = ReadDecimal(activity, "estimatedHours") ?? 0;
                detailRow++;
            }
        }
        detail.SheetView.FreezeRows(1);
        detail.Rows().Style.Alignment.WrapText = true;
        for (var column = 1; column <= headers.Length; column++)
            detail.Column(column).Width = column is 4 or 5 ? 55 : column is >= 6 and <= 17 ? 38 : 18;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildSowDocx(WorkspaceRow workspace)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = new XElement(w + "body");
        void AddParagraph(string text, bool bold = false, int size = 22)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var properties = new XElement(w + "rPr");
            if (bold) properties.Add(new XElement(w + "b"));
            properties.Add(new XElement(w + "sz", new XAttribute(w + "val", size.ToString())));
            body.Add(new XElement(w + "p", new XElement(w + "r", properties, new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text))));
        }
        void AddHeading(string text, int level = 1) => AddParagraph(text, true, level == 1 ? 30 : 26);
        void AddList(string heading, IReadOnlyList<string> values)
        {
            if (values.Count == 0) return;
            AddParagraph(heading, true, 22);
            foreach (var value in values) AddParagraph($"• {value}", false, 21);
        }

        AddHeading($"Statement of Work — {workspace.ProjectName}", 1);
        AddParagraph($"Reference: {workspace.Reference}");
        AddParagraph($"Customer: {workspace.CustomerName}");
        AddParagraph($"Contract Type: {(workspace.ContractType == "FIXED" ? "Fixed Price" : "Time & Materials")}");
        AddParagraph($"Solution Architect: {workspace.OwnerSolutionArchitectName}");
        AddParagraph($"Account Executive: {workspace.AccountExecutiveName}");
        AddParagraph($"Resale: {workspace.ResaleName}");
        AddHeading("2.1 Services Overview");
        AddParagraph(workspace.ServiceOverview, false, 22);
        AddHeading("2.2 Services Description");
        AddParagraph("The following delivery scope is organized into Plan, Design, Implement, Validate, and Release. Each activity is a review-confirmed delivery task and the associated level of effort is retained in the companion GSD.");

        foreach (var phase in RequiredPhases)
        {
            if (!TryGetPropertyIgnoreCase(workspace.PhaseDetails, phase, out var phaseNode)) continue;
            AddHeading(PhaseLabel(phase));
            var objective = ReadText(phaseNode, "objective");
            if (objective.Length > 0) AddParagraph(objective);
            if (TryGetPropertyIgnoreCase(phaseNode, "activities", out var activities) && activities.ValueKind == JsonValueKind.Array)
            {
                var activityNumber = 1;
                foreach (var activity in activities.EnumerateArray())
                {
                    AddParagraph($"{activityNumber}. {ReadText(activity, "name")}", true, 23);
                    AddParagraph(ReadText(activity, "description"));
                    AddList("Detailed execution steps", ReadStringArray(activity, "detailedSteps"));
                    AddList("Inputs", ReadStringArray(activity, "inputs"));
                    AddList("Prerequisites and dependencies", ReadStringArray(activity, "prerequisites"));
                    AddList("US Signal responsibilities", ReadStringArray(activity, "usSignalResponsibilities"));
                    AddList("Customer responsibilities", ReadStringArray(activity, "customerResponsibilities"));
                    AddList("Outputs and deliverables", ReadStringArray(activity, "outputs"));
                    AddList("Acceptance criteria", ReadStringArray(activity, "acceptanceCriteria"));
                    AddList("Validation steps", ReadStringArray(activity, "validationSteps"));
                    AddList("Risks", ReadStringArray(activity, "risks"));
                    AddList("Open questions", ReadStringArray(activity, "openQuestions"));
                    AddList("Required roles", ReadStringArray(activity, "requiredRoles"));
                    AddList("Predecessor relationships", ReadStringArray(activity, "predecessors"));
                    AddList("Source citations", ReadStringArray(activity, "citationIds"));
                    if (ReadBoolean(activity, "isAssumption"))
                        AddParagraph("Assumption flag: This activity includes one or more assumptions that require Solution Architect validation.", true, 21);
                    var estimatedHours = ReadDecimal(activity, "estimatedHours");
                    if (estimatedHours.HasValue) AddParagraph($"Estimated effort: {estimatedHours:0.##} hour(s).");
                    activityNumber++;
                }
            }
            var loe = ReadText(phaseNode, "loeRationale");
            if (loe.Length > 0) AddParagraph($"LOE rationale: {loe}", true, 21);
            AddParagraph($"Reviewed final {PhaseLabel(phase)} effort: {FinalHoursFor(workspace, phase):0.##} hour(s).", true, 21);
        }

        AddHeading("2.3 Deliverables");
        AddList("Project deliverables", ReadStringArray(workspace.AiDraft, "deliverables"));
        AddHeading("2.4 Detailed Exclusions");
        AddList("Out of scope", ReadStringArray(workspace.AiDraft, "outOfScope"));
        AddHeading("2.5 Client Involvement");
        AddList("Customer responsibilities", ReadStringArray(workspace.AiDraft, "customerResponsibilities"));
        AddHeading("US Signal Responsibilities");
        AddList("US Signal responsibilities", ReadStringArray(workspace.AiDraft, "usSignalResponsibilities"));
        AddHeading("Assumptions and Dependencies");
        AddList("Assumptions", ReadStringArray(workspace.AiDraft, "assumptions"));
        AddList("Dependencies", ReadStringArray(workspace.AiDraft, "dependencies"));
        AddHeading("Acceptance Criteria");
        AddList("Acceptance criteria", ReadStringArray(workspace.AiDraft, "acceptanceCriteria"));
        AddHeading("Open Questions and Risks");
        AddList("Open questions", ReadStringArray(workspace.AiDraft, "openQuestions"));
        AddList("Risks", ReadStringArray(workspace.AiDraft, "risks"));
        body.Add(new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", "12240"), new XAttribute(w + "h", "15840")), new XElement(w + "pgMar", new XAttribute(w + "top", "720"), new XAttribute(w + "right", "720"), new XAttribute(w + "bottom", "720"), new XAttribute(w + "left", "720"))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(w + "document", body));
        var contentTypes = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""";
        var relationships = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""";

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml", contentTypes);
            WriteZipEntry(archive, "_rels/.rels", relationships);
            WriteZipEntry(archive, "word/document.xml", document.ToString(SaveOptions.DisableFormatting));
        }
        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string PhaseSummary(JsonElement phaseNode)
    {
        if (!TryGetPropertyIgnoreCase(phaseNode, "activities", out var activities) || activities.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Join("\n", activities.EnumerateArray().Select(activity =>
        {
            var name = ReadText(activity, "name");
            var description = ReadText(activity, "description");
            return string.IsNullOrWhiteSpace(name) ? description : $"{name}: {description}";
        }).Where(value => value.Length > 0));
    }

    private static decimal FinalHoursFor(WorkspaceRow workspace, string phase) => phase switch
    {
        "plan" => workspace.FinalPlanHours ?? 0,
        "design" => workspace.FinalDesignHours ?? 0,
        "implement" => workspace.FinalImplementHours ?? 0,
        "validate" => workspace.FinalValidateHours ?? 0,
        "release" => workspace.FinalReleaseHours ?? 0,
        _ => 0
    };

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var property)) return [];
        if (property.ValueKind == JsonValueKind.Array)
            return property.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : value.ToString().Trim()).Where(value => value.Length > 0).ToArray();
        if (property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()))
            return [property.GetString()!.Trim()];
        return [];
    }

    private static string ReadText(JsonElement element, string name) =>
        TryGetPropertyIgnoreCase(element, name, out var property)
            ? property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() ?? string.Empty : property.ToString().Trim()
            : string.Empty;

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(property.ToString(), out var parsed) ? parsed : null;
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var property)) return false;
        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False) return property.GetBoolean();
        return bool.TryParse(property.ToString(), out var parsed) && parsed;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string JoinLines(IReadOnlyList<string> values, bool numbered = false) =>
        string.Join("\n", values.Select((value, index) => numbered ? $"{index + 1}. {value}" : $"• {value}"));

    private static string PhaseLabel(string phase) => phase.ToLowerInvariant() switch
    {
        "plan" => "Plan",
        "design" => "Design",
        "implement" => "Implement",
        "validate" => "Validate",
        "release" => "Release",
        _ => phase
    };

    private static string NormalizeContractType(string? value)
    {
        var clean = (value ?? string.Empty).Trim().ToUpperInvariant().Replace("&", "AND").Replace(" ", "_");
        return clean switch
        {
            "T_AND_M" or "TIME_AND_MATERIALS" or "TIME_AND_MATERIAL" or "TM" => "T_AND_M",
            "FIXED" or "FIXED_PRICE" => "FIXED",
            _ => string.Empty
        };
    }

    private static string NormalizeOemType(string? value)
    {
        var clean = (value ?? "STANDARD").Trim().ToUpperInvariant();
        return clean is "STANDARD" or "TOYOTA" or "HYUNDAI" ? clean : string.Empty;
    }

    private static string NormalizeEditableStatus(string? value)
    {
        var clean = (value ?? "DRAFT").Trim().ToUpperInvariant();
        return clean is "READY_FOR_REVIEW" ? clean : "DRAFT";
    }

    private static TemplateOption TemplateFor(string? oemCustomerType) =>
        NormalizeOemType(oemCustomerType) is "TOYOTA" or "HYUNDAI"
            ? new("HAEA_STAFF_AUG_KUS_UVO", "HAEA Staff Aug GSD KUS UVO Telematics 1")
            : new("STANDARD", "Standard GSD");

    private static string TemplateLabel(string code) =>
        string.Equals(code, "HAEA_STAFF_AUG_KUS_UVO", StringComparison.OrdinalIgnoreCase)
            ? "HAEA Staff Aug GSD KUS UVO Telematics 1"
            : "Standard GSD";

    private static async Task WriteEventAsync(NpgsqlConnection connection, Guid workspaceId, int revision, string eventType, Guid actorId, object detail, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO sow_gsd_workspace_events(sow_gsd_workspace_id, revision_number, event_type, event_detail, actor_user_id)
            VALUES(@workspace_id, @revision, @event_type, @detail, @actor_id);
            """, connection);
        command.Parameters.AddWithValue("workspace_id", workspaceId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(detail, JsonOptions) });
        command.Parameters.AddWithValue("actor_id", actorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static async Task<OpenOutcome> OpenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return new(null, DependencyUnavailable());
        try
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using (var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='098_module_025_sow_gsd_workspace');", connection))
            {
                var installed = Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
                if (!installed)
                {
                    await connection.DisposeAsync();
                    return new(null, MigrationRequired());
                }
            }
            return new(connection, null);
        }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SowGsdWorkspaceModule")
                .LogWarning(exception, "Module 025 database connection was unavailable.");
            return new(null, DependencyUnavailable());
        }
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
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

    private static bool IsMigrationError(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedFunction;

    private static IResult MigrationRequired() => Results.Json(new
    {
        status = "sow_gsd_workspace_migration_required",
        module = "025",
        migration = MigrationFile,
        message = "Module 025 persistent SOW/GSD storage is not installed."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult DependencyUnavailable() => Results.Json(new
    {
        status = "sow_gsd_workspace_unavailable",
        module = "025",
        migration = MigrationFile
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult Forbidden(string capability) => Results.Json(new
    {
        status = "forbidden",
        module = "025",
        capability
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsWriteBlocked() => Results.Json(new
    {
        status = "actual_session_required",
        module = "025",
        message = "Exit Administrator View-As before changing a SOW/GSD workspace."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult RevisionConflict(int expected, int current) => Results.Conflict(new
    {
        status = "sow_gsd_revision_conflict",
        expectedRevision = expected,
        currentRevision = current
    });

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = value.HasValue ? (object)value.Value : DBNull.Value });

    private static void AddNullableDecimal(NpgsqlCommand command, string name, decimal? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Numeric) { Value = value.HasValue ? (object)value.Value : DBNull.Value });

    private static void AddJson(NpgsqlCommand command, string name, JsonElement? value, string fallback) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = value.HasValue && value.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null
                ? value.Value.GetRawText()
                : fallback
        });

    private static string Clean(string? value, int maximum)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(character => invalid.Contains(character) ? '-' : character).ToArray();
        return new string(chars).Trim();
    }

    private static JsonElement ParseJson(string value) => JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value).RootElement.Clone();
    private static string? TextOrNull(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? GuidOrNull(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static decimal? DecimalOrNull(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static DateTimeOffset? DateTimeOrNull(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    private static decimal SumHours(NpgsqlDataReader reader, params int[] ordinals) => ordinals.Sum(ordinal => reader.IsDBNull(ordinal) ? 0 : reader.GetDecimal(ordinal));

    public sealed record WorkspaceSaveRequest(
        Guid? OwnerSolutionArchitectUserId,
        Guid? CustomerId,
        string? CustomerName,
        string? OpportunityReference,
        string? ProjectCode,
        string? ProjectName,
        string? ServiceOverview,
        string? ContractType,
        Guid? AccountExecutiveUserId,
        Guid? ResaleUserId,
        string? OemCustomerType,
        string? Status,
        JsonElement? AiDraft,
        JsonElement? PhaseDetails,
        decimal? SuggestedPlanHours,
        decimal? SuggestedDesignHours,
        decimal? SuggestedImplementHours,
        decimal? SuggestedValidateHours,
        decimal? SuggestedReleaseHours,
        decimal? FinalPlanHours,
        decimal? FinalDesignHours,
        decimal? FinalImplementHours,
        decimal? FinalValidateHours,
        decimal? FinalReleaseHours,
        string? GenerationProvider,
        JsonElement? GenerationCitations,
        JsonElement? GenerationWarnings,
        JsonElement? GenerationMissingEvidence,
        decimal? GenerationConfidence,
        bool? GenerationCompleted,
        int? ExpectedRevision);

    public sealed record WorkspaceActionRequest(int? ExpectedRevision, string? Reason);
    private sealed record OpenOutcome(NpgsqlConnection? Connection, IResult? Error);
    private sealed record AccessOutcome(AccessContext? Context, IResult? Error);
    private sealed record ExportOutcome(WorkspaceRow? Workspace, IResult? Error);
    private sealed record CustomerResolution(Guid? CustomerId, string CustomerName, string Source, IResult? Error);
    private sealed record PersonOption(Guid UserId, string DisplayName, string Email);
    private sealed record TemplateOption(string Code, string Label);
    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string DisplayName,
        string Email,
        string TeamName,
        string DepartmentName,
        HashSet<string> Roles,
        bool IsAdministrator,
        bool IsSolutionArchitect,
        bool IsManager,
        bool IsViewAs);

    private sealed record WorkspaceRow(
        Guid WorkspaceId,
        string Reference,
        Guid OwnerSolutionArchitectUserId,
        string OwnerSolutionArchitectName,
        Guid? CustomerId,
        string CustomerName,
        string CustomerSource,
        string? OpportunityReference,
        string? ProjectCode,
        string ProjectName,
        string ServiceOverview,
        string ContractType,
        Guid? AccountExecutiveUserId,
        string? AccountExecutiveName,
        Guid? ResaleUserId,
        string? ResaleName,
        string OemCustomerType,
        string GsdTemplateCode,
        string Status,
        JsonElement AiDraft,
        JsonElement PhaseDetails,
        decimal? SuggestedPlanHours,
        decimal? SuggestedDesignHours,
        decimal? SuggestedImplementHours,
        decimal? SuggestedValidateHours,
        decimal? SuggestedReleaseHours,
        decimal? FinalPlanHours,
        decimal? FinalDesignHours,
        decimal? FinalImplementHours,
        decimal? FinalValidateHours,
        decimal? FinalReleaseHours,
        string? GenerationProvider,
        JsonElement GenerationCitations,
        JsonElement GenerationWarnings,
        JsonElement GenerationMissingEvidence,
        decimal? GenerationConfidence,
        DateTimeOffset? ReviewConfirmedAt,
        DateTimeOffset? ArchivedAt,
        DateTimeOffset? LastAutosavedAt,
        int RevisionNumber,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
