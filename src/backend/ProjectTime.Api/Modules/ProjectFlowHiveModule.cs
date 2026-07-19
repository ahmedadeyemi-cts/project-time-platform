using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 066 exposes the module-owned Project FlowHive source contract on top
/// of canonical ProjectPulse projects, tasks, and assignments. Deterministic
/// planning previews are enabled; persistence, provider calls, customer links,
/// and external delivery remain locked until their governed integrations are
/// separately authorized and registered.
/// </summary>
public static class ProjectFlowHiveModule
{
    public static WebApplication MapProjectFlowHiveEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/project-flowhive/capabilities",
            (Func<HttpContext, IResult>)GetCapabilities);
        app.MapGet(
            "/api/project-flowhive/portfolio",
            (Func<HttpContext, Task<IResult>>)GetPortfolioAsync);
        app.MapGet(
            "/api/project-flowhive/readiness",
            (Func<HttpContext, IResult>)GetReadiness);
        app.MapPost(
            "/api/project-flowhive/planning/validate",
            (Func<ProjectFlowHivePlanRequest, HttpContext, IResult>)ValidatePlan);
        app.MapPost(
            "/api/project-flowhive/schedule/calculate",
            (Func<ProjectFlowHivePlanRequest, HttpContext, IResult>)CalculateSchedule);
        app.MapPost(
            "/api/project-flowhive/plans/drafts",
            (Func<ProjectFlowHivePlanRequest, HttpContext, IResult>)PersistenceLocked);
        app.MapPost(
            "/api/project-flowhive/plans/{planId:guid}/baseline",
            (Func<Guid, HttpContext, IResult>)BaselineLocked);
        app.MapPost(
            "/api/project-flowhive/ai/request-preview",
            (Func<ProjectFlowHiveAiDraftPreviewRequest, HttpContext, IResult>)PreviewAiRequest);
        app.MapGet(
            "/api/project-flowhive/artifacts/readiness",
            (Func<HttpContext, IResult>)GetArtifactReadiness);
        app.MapPost(
            "/api/project-flowhive/artifacts/pdf-preview",
            (Func<ProjectFlowHiveArtifactRequest, HttpContext, IResult>)BuildPdfPreview);
        app.MapPost(
            "/api/project-flowhive/artifacts/excel-preview",
            (Func<ProjectFlowHiveArtifactRequest, HttpContext, IResult>)BuildExcelPreview);

        return app;
    }

    private static IResult GetCapabilities(HttpContext httpContext)
    {
        var effectiveUserId = EffectiveSessionUserId(httpContext);

        if (effectiveUserId is null)
        {
            return SessionRequired();
        }

        return Results.Ok(new
        {
            module = "066",
            moduleName = "Project FlowHive",
            phase = "066A.1-066E",
            status = "release_train_source_registered_external_locks_preserved",
            route = "project-flowhive",
            databaseMutationEnabled = false,
            aiExecutionEnabled = false,
            deterministicPlanningPreviewEnabled = true,
            internalDraftArtifactEnabled = true,
            customerExportEnabled = false,
            customerSharingEnabled = false,
            capabilities = CapabilityRows(),
            integration = new
            {
                canonicalProjects = "available_read_only",
                canonicalTasks = "available_read_only",
                canonicalAssignments = "available_read_only",
                workRegister = "dependency_identified",
                timesheet = "dependency_identified",
                calendarCapacity = "weekday_preview_only_module_057_authority_required",
                aiProvider = "module_064_shared_router_registered_flowhive_execution_locked",
                aiProviderOrder = new[] { "claude", "openai", "local_template" },
                identityProfile = "module_062_available",
                approvalCenter = "module_002_preserved_on_current_main",
                brandedPdfAndExcel = "internal_draft_preview_source_ready",
                logoSha256 = ProjectFlowHiveBrandAssets.LogoSha256,
                sharedRegistration = "integrated_uncommitted_source"
            }
        });
    }

    private static IResult GetReadiness(HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();

        return Results.Ok(new
        {
            module = "066",
            sourceBaseline = "main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4",
            module002 = new
            {
                sourceCommit = "f5ede8f6717b01c8f4bf7905b433fead38210007",
                mergeCommit = "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4",
                preserved = true
            },
            phases = new object[]
            {
                new { phase = "066A.1", capability = "shared registration", status = "source_integrated_uncommitted" },
                new { phase = "066B", capability = "governed planning contracts", status = "source_ready_persistence_locked" },
                new { phase = "066C", capability = "schedule and portfolio experience", status = "deterministic_preview_source_ready" },
                new { phase = "066D", capability = "automation and AI", status = "module_064_request_ready_execution_locked" },
                new { phase = "066E", capability = "customer artifacts and sharing", status = "internal_branded_preview_ready_external_sharing_locked" }
            },
            sharedFilesChanged = true,
            databaseApplied = false,
            azureChanged = false,
            entraChanged = false,
            deploymentPerformed = false,
            blockers = new[]
            {
                "Complete release-train validation and obtain publication authority before commit or deployment.",
                "Keep FlowHive provider execution locked until its reviewed adapter calls the registered Module 064 router.",
                "Authorize and review a persistence schema before registering a write repository.",
                "Authorize external customer sharing before creating any customer link."
            }
        });
    }

    private static IResult ValidatePlan(
        ProjectFlowHivePlanRequest request,
        HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();
        return Results.Ok(ProjectFlowHiveScheduleEngine.Validate(request));
    }

    private static IResult CalculateSchedule(
        ProjectFlowHivePlanRequest request,
        HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();
        var result = ProjectFlowHiveScheduleEngine.Calculate(request);
        return result.Valid ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static IResult PersistenceLocked(
        ProjectFlowHivePlanRequest request,
        HttpContext httpContext)
    {
        if (ActualSessionUserId(httpContext) is null) return SessionRequired();
        return LockedPhase(
            "066B",
            "persistence_locked",
            "Plan drafts cannot be stored until a separately approved persistence adapter is registered.");
    }

    private static IResult BaselineLocked(Guid planId, HttpContext httpContext)
    {
        if (ActualSessionUserId(httpContext) is null) return SessionRequired();
        return LockedPhase(
            "066B",
            "baseline_locked",
            "A Project FlowHive baseline cannot be established without approved persistence, authorization, and Module 002 approval integration.");
    }

    private static IResult PreviewAiRequest(
        ProjectFlowHiveAiDraftPreviewRequest request,
        HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();
        return Results.Ok(ProjectFlowHiveAiRequestFactory.Preview(request));
    }

    private static IResult GetArtifactReadiness(HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();
        return Results.Ok(new
        {
            module = "066",
            phase = "066E",
            status = "internal_preview_ready_customer_sharing_locked",
            formats = new[] { "pdf", "xlsx" },
            branding = new
            {
                asset = "repository_owned_us_signal_logo",
                sha256 = ProjectFlowHiveBrandAssets.LogoSha256,
                embeddedInPdf = true,
                embeddedInExcel = true
            },
            restrictions = new[]
            {
                "Artifacts are marked as internal drafts.",
                "No external link is created.",
                "Customer baseline export remains locked.",
                "Delivery and customer access require separate authorization."
            }
        });
    }

    private static IResult BuildPdfPreview(
        ProjectFlowHiveArtifactRequest request,
        HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();
        var prepared = PrepareArtifact(request);
        if (prepared.Error is not null) return prepared.Error;
        var bytes = ProjectFlowHiveArtifactRenderer.BuildPdf(request, prepared.Schedule!);
        return Results.File(bytes, "application/pdf", $"{SafeFileName(request.Plan?.PlanName)}-internal-draft.pdf");
    }

    private static IResult BuildExcelPreview(
        ProjectFlowHiveArtifactRequest request,
        HttpContext httpContext)
    {
        if (EffectiveSessionUserId(httpContext) is null) return SessionRequired();
        var prepared = PrepareArtifact(request);
        if (prepared.Error is not null) return prepared.Error;
        var bytes = ProjectFlowHiveArtifactRenderer.BuildExcel(request, prepared.Schedule!);
        return Results.File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{SafeFileName(request.Plan?.PlanName)}-internal-draft.xlsx");
    }

    private static (ProjectFlowHiveScheduleResult? Schedule, IResult? Error) PrepareArtifact(
        ProjectFlowHiveArtifactRequest request)
    {
        if (!request.AcknowledgeInternalDraft)
        {
            return (null, Results.BadRequest(new
            {
                status = "internal_draft_acknowledgement_required",
                message = "Acknowledge that this artifact is an internal draft and not an approved customer baseline."
            }));
        }
        if (!string.Equals(request.Audience?.Trim(), "internal", StringComparison.OrdinalIgnoreCase))
        {
            return (null, LockedPhase(
                "066E",
                "customer_export_locked",
                "Only internal draft previews are available. Customer exports and sharing links are not authorized."));
        }

        var schedule = ProjectFlowHiveScheduleEngine.Calculate(request.Plan);
        return schedule.Valid
            ? (schedule, null)
            : (null, Results.BadRequest(schedule));
    }

    private static IResult LockedPhase(string phase, string status, string message)
    {
        return Results.Json(new
        {
            module = "066",
            phase,
            status,
            message,
            stateChanged = false
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static string SafeFileName(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "project-flowhive-plan" : value.Trim();
        var safe = new string(source.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        return safe.Trim('-').ToLowerInvariant() is { Length: > 0 } result
            ? result[..Math.Min(result.Length, 80)]
            : "project-flowhive-plan";
    }

    private static async Task<IResult> GetPortfolioAsync(HttpContext httpContext)
    {
        var effectiveUserId = EffectiveSessionUserId(httpContext);

        if (effectiveUserId is null)
        {
            return SessionRequired();
        }

        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();

        if (config.Missing.Count > 0)
        {
            return Results.Json(new
            {
                status = "configuration_missing",
                message = "Project FlowHive cannot read canonical project data because database configuration is incomplete.",
                missing = config.Missing
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync();

            var access = await LoadAccessContextAsync(connection, effectiveUserId.Value);

            if (!access.IsActiveUser)
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "The active ProjectPulse user could not be resolved for Project FlowHive."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var projects = await LoadProjectsAsync(connection, access);
            var tasks = await LoadTasksAsync(connection, access);
            var assignments = await LoadAssignmentsAsync(connection, access);
            var actualUserId = ActualSessionUserId(httpContext) ?? effectiveUserId.Value;
            var isViewAs = actualUserId != effectiveUserId.Value
                || (httpContext.Items.TryGetValue("ProjectPulseIsViewAs", out var viewAsValue)
                    && viewAsValue is bool activeViewAs
                    && activeViewAs);

            return Results.Ok(new
            {
                module = "066",
                moduleName = "Project FlowHive",
                phase = "066A.1-066E",
                status = "portfolio_loaded",
                mode = "read_only_canonical_source",
                access = new
                {
                    actualUserId,
                    effectiveUserId = access.UserId,
                    access.DisplayName,
                    access.Email,
                    roles = access.RoleCodes.OrderBy(value => value).ToArray(),
                    scope = access.ScopeLabel,
                    isViewAs,
                    serverAuthorized = true
                },
                summary = new
                {
                    projectCount = projects.Count,
                    taskCount = tasks.Count,
                    assignmentCount = assignments.Count,
                    assignedHours = assignments.Sum(row => row.AssignedHours),
                    usedHours = tasks.Sum(row => row.UsedHours),
                    remainingHours = tasks.Sum(row => row.RemainingHours),
                    controlledBaselineCount = 0,
                    dependencyCount = 0,
                    planningPreviewAvailable = true,
                    persistenceAvailable = false
                },
                projects,
                tasks,
                assignments,
                planningState = new
                {
                    canonicalTaskCodeAvailable = true,
                    controlledWbsPreviewAvailable = true,
                    dependencyNetworkPreviewAvailable = true,
                    scheduleEnginePreviewAvailable = true,
                    baselineVersioningAvailable = false,
                    collaborationHistoryAvailable = false,
                    aiExecutionAvailable = false,
                    internalBrandedArtifactsAvailable = true,
                    customerSharingAvailable = false,
                    explanation = "Canonical records remain read only. Deterministic plan and artifact previews do not persist or share data."
                },
                guardrails = new[]
                {
                    "All portfolio rows are filtered by backend assignment and role scope.",
                    "Project Managers see managed projects; engineers see assigned projects and tasks.",
                    "Project Team Coordinators and authorized leadership retain their broader business scope.",
                    "No Project FlowHive endpoint creates, updates, deletes, approves, or baselines database data.",
                    "Task codes remain canonical references until a validated local planning preview assigns controlled draft WBS values.",
                    "Schedule calculations are weekday-only previews until Module 057 calendar authority is integrated.",
                    "AI execution is locked to Module 064; direct provider clients are prohibited.",
                    "PDF and Excel previews are US Signal branded internal drafts; customer sharing remains disabled."
                }
            });
        }
        catch (Exception exception)
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectFlowHiveModule");

            logger.LogError(exception, "Module 066A failed to load its read-only portfolio.");

            return Results.Problem(
                title: "Project FlowHive portfolio unavailable",
                detail: "The read-only Project FlowHive portfolio could not be loaded.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static object[] CapabilityRows()
    {
        return
        [
            new { code = "portfolio", priority = "P0", status = "source_ready", evidence = "Role-scoped canonical project summary" },
            new { code = "task_grid", priority = "P0", status = "source_ready", evidence = "Read-only canonical task grid plus local controlled-draft editor" },
            new { code = "resource_assignments", priority = "P0", status = "source_ready", evidence = "Module 062 identity-backed assignment references" },
            new { code = "controlled_wbs", priority = "P0", status = "preview_ready", evidence = "Numeric hierarchy validation; persistence locked" },
            new { code = "dependencies", priority = "P0", status = "preview_ready", evidence = "FS/SS/FF/SF, lead/lag, duplicate, and cycle validation" },
            new { code = "gantt_timeline", priority = "P0", status = "preview_ready", evidence = "Deterministic weekday schedule, float, and critical path preview" },
            new { code = "baselines", priority = "P0", status = "locked", evidence = "Contract present; persistence and approval integration not authorized" },
            new { code = "collaboration", priority = "P0", status = "locked", evidence = "History design present; persistence not authorized" },
            new { code = "ai_plan_generation", priority = "P1", status = "request_ready", evidence = "Module 064 request preview with Claude, OpenAI, local routing contract; execution locked" },
            new { code = "internal_exports", priority = "P1", status = "preview_ready", evidence = "US Signal logo embedded in internal draft PDF and Excel source" },
            new { code = "customer_sharing", priority = "P1", status = "locked", evidence = "No customer link, token, delivery, or external state change" }
        ];
    }

    private static async Task<ProjectFlowHiveAccessContext> LoadAccessContextAsync(
        NpgsqlConnection connection,
        Guid userId)
    {
        const string sql = """
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email,
                COALESCE(u.team_name, '') AS team_name,
                COALESCE(u.department_name, '') AS department_name,
                COALESCE(u.department, '') AS department,
                COALESCE(string_agg(DISTINCT r.role_code, ',' ORDER BY r.role_code), '') AS role_codes
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE
            GROUP BY
                u.user_id,
                u.display_name,
                u.email,
                u.team_name,
                u.department_name,
                u.department;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return ProjectFlowHiveAccessContext.Empty(userId);
        }

        var roleCodes = reader.GetString(6)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProjectFlowHiveAccessContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            roleCodes,
            true);
    }

    private static async Task<List<ProjectFlowHiveProject>> LoadProjectsAsync(
        NpgsqlConnection connection,
        ProjectFlowHiveAccessContext access)
    {
        var rows = new List<ProjectFlowHiveProject>();

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                      OR EXISTS (
                          SELECT 1
                          FROM reporting_relationships relationship
                          WHERE relationship.employee_user_id = member.user_id
                            AND (relationship.manager_user_id = @user_id OR relationship.team_lead_user_id = @user_id)
                            AND relationship.effective_start_date <= CURRENT_DATE
                            AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date >= CURRENT_DATE)
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM projectpulse_team_scope_assignments scope_assignment
                          WHERE scope_assignment.scoped_user_id = @user_id
                            AND scope_assignment.is_active = TRUE
                            AND (
                                (scope_assignment.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name, '')) = LOWER(scope_assignment.team_name))
                                OR (scope_assignment.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name, '')) = LOWER(scope_assignment.department_name))
                            )
                      )
                  )
            )
            SELECT
                p.project_id,
                p.project_code,
                p.project_name,
                COALESCE(c.client_name, 'No customer') AS customer_name,
                p.status,
                p.start_date,
                p.end_date,
                COALESCE(pm.display_name, pm.email, 'Unassigned') AS project_manager_name,
                COUNT(DISTINCT task.task_id)::bigint AS task_count,
                COUNT(DISTINCT assignment.project_assignment_id)::bigint AS assignment_count
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            LEFT JOIN project_tasks task
                ON task.project_id = p.project_id
               AND task.is_active = TRUE
            LEFT JOIN project_assignments assignment
                ON assignment.project_id = p.project_id
            WHERE
                @is_broad_scope = TRUE
                OR p.project_manager_user_id = @user_id
                OR EXISTS (
                    SELECT 1
                    FROM project_assignments self_assignment
                    WHERE self_assignment.project_id = p.project_id
                      AND self_assignment.user_id = @user_id
                )
                OR (
                    @can_view_team_scope = TRUE
                    AND (
                        p.project_manager_user_id IN (SELECT user_id FROM team_members)
                        OR EXISTS (
                            SELECT 1
                            FROM project_assignments team_assignment
                            WHERE team_assignment.project_id = p.project_id
                              AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                        )
                    )
                )
            GROUP BY
                p.project_id,
                p.project_code,
                p.project_name,
                c.client_name,
                p.status,
                p.start_date,
                p.end_date,
                pm.display_name,
                pm.email,
                p.created_at
            ORDER BY p.created_at DESC
            LIMIT 200;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);

            rows.Add(new ProjectFlowHiveProject(
                reader.GetGuid(O("project_id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("customer_name")),
                reader.GetString(O("status")),
                ReadDateOnlyOrNull(reader, O("start_date")),
                ReadDateOnlyOrNull(reader, O("end_date")),
                reader.GetString(O("project_manager_name")),
                reader.GetInt64(O("task_count")),
                reader.GetInt64(O("assignment_count")),
                "canonical_project"));
        }

        return rows;
    }

    private static async Task<List<ProjectFlowHiveTask>> LoadTasksAsync(
        NpgsqlConnection connection,
        ProjectFlowHiveAccessContext access)
    {
        var rows = new List<ProjectFlowHiveTask>();

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                      OR EXISTS (
                          SELECT 1
                          FROM reporting_relationships relationship
                          WHERE relationship.employee_user_id = member.user_id
                            AND (relationship.manager_user_id = @user_id OR relationship.team_lead_user_id = @user_id)
                            AND relationship.effective_start_date <= CURRENT_DATE
                            AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date >= CURRENT_DATE)
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM projectpulse_team_scope_assignments scope_assignment
                          WHERE scope_assignment.scoped_user_id = @user_id
                            AND scope_assignment.is_active = TRUE
                            AND (
                                (scope_assignment.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name, '')) = LOWER(scope_assignment.team_name))
                                OR (scope_assignment.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name, '')) = LOWER(scope_assignment.department_name))
                            )
                      )
                  )
            ),
            scoped_projects AS (
                SELECT p.project_id, p.project_manager_user_id
                FROM projects p
                WHERE
                    @is_broad_scope = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1
                        FROM project_assignments self_assignment
                        WHERE self_assignment.project_id = p.project_id
                          AND self_assignment.user_id = @user_id
                    )
                    OR (
                        @can_view_team_scope = TRUE
                        AND (
                            p.project_manager_user_id IN (SELECT user_id FROM team_members)
                            OR EXISTS (
                                SELECT 1
                                FROM project_assignments team_assignment
                                WHERE team_assignment.project_id = p.project_id
                                  AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                            )
                        )
                    )
            ),
            assignment_summary AS (
                SELECT
                    assignment.task_id,
                    COUNT(*)::bigint AS assignee_count,
                    COALESCE(SUM(assignment.assigned_hours), 0)::numeric AS assigned_hours
                FROM project_assignments assignment
                WHERE assignment.task_id IS NOT NULL
                GROUP BY assignment.task_id
            ),
            time_summary AS (
                SELECT
                    entry.task_id,
                    COALESCE(SUM(entry.hours), 0)::numeric AS used_hours
                FROM time_entries entry
                WHERE entry.task_id IS NOT NULL
                  AND entry.status NOT IN ('voided', 'rejected')
                GROUP BY entry.task_id
            )
            SELECT
                task.task_id,
                task.project_id,
                project.project_code,
                project.project_name,
                task.task_code,
                task.task_name,
                COALESCE(task.task_description, '') AS task_description,
                task.billable,
                COALESCE(assignment_summary.assignee_count, 0)::bigint AS assignee_count,
                COALESCE(assignment_summary.assigned_hours, 0)::numeric AS assigned_hours,
                COALESCE(time_summary.used_hours, 0)::numeric AS used_hours,
                GREATEST(
                    COALESCE(assignment_summary.assigned_hours, 0)::numeric
                    - COALESCE(time_summary.used_hours, 0)::numeric,
                    0
                )::numeric AS remaining_hours
            FROM project_tasks task
            JOIN projects project ON project.project_id = task.project_id
            JOIN scoped_projects scope ON scope.project_id = task.project_id
            LEFT JOIN assignment_summary ON assignment_summary.task_id = task.task_id
            LEFT JOIN time_summary ON time_summary.task_id = task.task_id
            WHERE task.is_active = TRUE
              AND (
                  @can_view_all_scoped_tasks = TRUE
                  OR project.project_manager_user_id = @user_id
                  OR EXISTS (
                      SELECT 1
                      FROM project_assignments self_task_assignment
                      WHERE self_task_assignment.project_id = task.project_id
                        AND self_task_assignment.user_id = @user_id
                        AND (
                            self_task_assignment.task_id = task.task_id
                            OR self_task_assignment.task_id IS NULL
                        )
                  )
              )
            ORDER BY project.project_code, task.task_code, task.task_name
            LIMIT 1000;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);

            rows.Add(new ProjectFlowHiveTask(
                reader.GetGuid(O("task_id")),
                reader.GetGuid(O("project_id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("task_code")),
                reader.GetString(O("task_name")),
                reader.GetString(O("task_description")),
                reader.GetBoolean(O("billable")),
                reader.GetInt64(O("assignee_count")),
                reader.GetDecimal(O("assigned_hours")),
                reader.GetDecimal(O("used_hours")),
                reader.GetDecimal(O("remaining_hours")),
                "canonical_task_code",
                false));
        }

        return rows;
    }

    private static async Task<List<ProjectFlowHiveAssignment>> LoadAssignmentsAsync(
        NpgsqlConnection connection,
        ProjectFlowHiveAccessContext access)
    {
        var rows = new List<ProjectFlowHiveAssignment>();

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                      OR EXISTS (
                          SELECT 1
                          FROM reporting_relationships relationship
                          WHERE relationship.employee_user_id = member.user_id
                            AND (relationship.manager_user_id = @user_id OR relationship.team_lead_user_id = @user_id)
                            AND relationship.effective_start_date <= CURRENT_DATE
                            AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date >= CURRENT_DATE)
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM projectpulse_team_scope_assignments scope_assignment
                          WHERE scope_assignment.scoped_user_id = @user_id
                            AND scope_assignment.is_active = TRUE
                            AND (
                                (scope_assignment.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name, '')) = LOWER(scope_assignment.team_name))
                                OR (scope_assignment.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name, '')) = LOWER(scope_assignment.department_name))
                            )
                      )
                  )
            )
            SELECT
                assignment.project_assignment_id,
                assignment.project_id,
                assignment.task_id,
                project.project_code,
                project.project_name,
                COALESCE(task.task_code, 'PROJECT') AS task_code,
                COALESCE(task.task_name, 'Project-level assignment') AS task_name,
                resource.user_id AS resource_user_id,
                COALESCE(NULLIF(resource.display_name, ''), resource.email) AS resource_name,
                resource.email AS resource_email,
                assignment.effective_start_date,
                assignment.effective_end_date,
                assignment.allocation_percent,
                COALESCE(assignment.assigned_hours, 0)::numeric AS assigned_hours
            FROM project_assignments assignment
            JOIN projects project ON project.project_id = assignment.project_id
            LEFT JOIN project_tasks task ON task.task_id = assignment.task_id
            JOIN app_users resource ON resource.user_id = assignment.user_id
            WHERE
                @is_broad_scope = TRUE
                OR assignment.user_id = @user_id
                OR project.project_manager_user_id = @user_id
                OR (
                    @can_view_team_scope = TRUE
                    AND (
                        assignment.user_id IN (SELECT user_id FROM team_members)
                        OR project.project_manager_user_id IN (SELECT user_id FROM team_members)
                    )
                )
            ORDER BY project.project_code, task.task_code, resource.display_name
            LIMIT 1000;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);

            rows.Add(new ProjectFlowHiveAssignment(
                reader.GetGuid(O("project_assignment_id")),
                reader.GetGuid(O("project_id")),
                reader.IsDBNull(O("task_id")) ? null : reader.GetGuid(O("task_id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("task_code")),
                reader.GetString(O("task_name")),
                reader.GetGuid(O("resource_user_id")),
                reader.GetString(O("resource_name")),
                reader.GetString(O("resource_email")),
                ReadDateOnly(reader, O("effective_start_date")),
                ReadDateOnlyOrNull(reader, O("effective_end_date")),
                reader.IsDBNull(O("allocation_percent")) ? null : reader.GetDecimal(O("allocation_percent")),
                reader.GetDecimal(O("assigned_hours"))));
        }

        return rows;
    }

    private static void AddScopeParameters(
        NpgsqlCommand command,
        ProjectFlowHiveAccessContext access)
    {
        command.Parameters.AddWithValue("user_id", access.UserId);
        command.Parameters.AddWithValue("team_name", access.TeamName);
        command.Parameters.AddWithValue("department_name", access.DepartmentName);
        command.Parameters.AddWithValue("is_broad_scope", access.IsBroadBusinessScope);
        command.Parameters.AddWithValue("can_view_team_scope", access.CanViewTeamScope);
        command.Parameters.AddWithValue("can_view_all_scoped_tasks", access.CanViewAllScopedTasks);
    }

    private static Guid? EffectiveSessionUserId(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("ProjectPulseEffectiveUserId", out var effectiveValue)
            && effectiveValue is Guid effectiveUserId)
        {
            return effectiveUserId;
        }

        if (httpContext.Items.TryGetValue("ProjectPulseSessionUserId", out var sessionValue)
            && sessionValue is Guid sessionUserId)
        {
            return sessionUserId;
        }

        return null;
    }

    private static Guid? ActualSessionUserId(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("ProjectPulseActualUserId", out var actualValue)
            && actualValue is Guid actualUserId)
        {
            return actualUserId;
        }

        if (httpContext.Items.TryGetValue("ProjectPulseSessionUserId", out var sessionValue)
            && sessionValue is Guid sessionUserId)
        {
            return sessionUserId;
        }

        return null;
    }

    private static IResult SessionRequired()
    {
        return Results.Json(new
        {
            status = "session_required",
            message = "A valid ProjectPulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static DateOnly? ReadDateOnlyOrNull(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;

        var value = reader.GetValue(ordinal);

        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static DateOnly ReadDateOnly(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);

        return value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty)
        };
    }
}

internal sealed record ProjectFlowHiveAccessContext(
    Guid UserId,
    string DisplayName,
    string Email,
    string TeamName,
    string DepartmentName,
    string Department,
    IReadOnlySet<string> RoleCodes,
    bool IsActiveUser)
{
    public static ProjectFlowHiveAccessContext Empty(Guid userId)
    {
        return new ProjectFlowHiveAccessContext(
            userId,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            false);
    }

    public bool HasRole(params string[] roleCodes)
    {
        return roleCodes.Any(RoleCodes.Contains);
    }

    public bool IsAdministrator => HasRole(
        "SUPER_ADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR");

    public bool IsProjectTeamCoordinator => HasRole(
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_COORDINATOR");

    public bool IsProjectManager => HasRole(
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT");

    public bool IsProjectManagementLead => HasRole(
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD");

    public bool IsPeopleManager => HasRole("MANAGER");

    public bool IsEngineeringLead => HasRole(
        "ENGINEERING_LEAD",
        "ENGINEERING_TEAM_LEAD");

    public bool IsExecutive => HasRole(
        "EXECUTIVE",
        "EXECUTIVE_LEADERSHIP");

    public bool IsBroadBusinessScope =>
        IsAdministrator
        || IsProjectTeamCoordinator
        || IsExecutive;

    public bool CanViewTeamScope =>
        IsBroadBusinessScope
        || IsProjectManagementLead
        || IsPeopleManager
        || IsEngineeringLead;

    public bool CanViewAllScopedTasks =>
        IsBroadBusinessScope
        || IsProjectManager
        || IsProjectManagementLead
        || IsPeopleManager
        || IsEngineeringLead;

    public string ScopeLabel
    {
        get
        {
            if (IsAdministrator) return "administrator_full_scope";
            if (IsProjectTeamCoordinator) return "project_team_coordinator_business_scope";
            if (IsExecutive) return "executive_read_scope";
            if (IsProjectManagementLead) return "project_management_team_scope";
            if (IsPeopleManager) return "manager_team_scope";
            if (IsEngineeringLead) return "engineering_team_scope";
            if (IsProjectManager) return "managed_projects_scope";
            return "assigned_projects_and_tasks_scope";
        }
    }
}

internal sealed record ProjectFlowHiveProject(
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string ProjectManagerName,
    long TaskCount,
    long AssignmentCount,
    string Source);

internal sealed record ProjectFlowHiveTask(
    Guid TaskId,
    Guid ProjectId,
    string ProjectCode,
    string ProjectName,
    string TaskCode,
    string TaskName,
    string TaskDescription,
    bool Billable,
    long AssigneeCount,
    decimal AssignedHours,
    decimal UsedHours,
    decimal RemainingHours,
    string StructureSource,
    bool IsControlledWbs);

internal sealed record ProjectFlowHiveAssignment(
    Guid AssignmentId,
    Guid ProjectId,
    Guid? TaskId,
    string ProjectCode,
    string ProjectName,
    string TaskCode,
    string TaskName,
    Guid ResourceUserId,
    string ResourceName,
    string ResourceEmail,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    decimal? AllocationPercent,
    decimal AssignedHours);

internal sealed record ProjectFlowHiveDatabaseConfig(
    string? Host,
    string? Port,
    string? Database,
    string? Username,
    string? Password,
    IReadOnlyList<string> Missing)
{
    public string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = int.TryParse(Port, out var parsedPort) ? parsedPort : 5432,
                Database = Database,
                Username = Username,
                Password = Password,
                IncludeErrorDetail = false,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 5
            };

            return builder.ConnectionString;
        }
    }

    public static ProjectFlowHiveDatabaseConfig FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(host)) missing.Add("PTP_DB_HOST");
        if (string.IsNullOrWhiteSpace(port)) missing.Add("PTP_DB_PORT");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("PTP_DB_NAME");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("PTP_DB_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("PTP_DB_PASSWORD");

        return new ProjectFlowHiveDatabaseConfig(
            host,
            port,
            database,
            username,
            password,
            missing);
    }
}
