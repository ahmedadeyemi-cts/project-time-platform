using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class ProjectWorkspaceModule
{
    public static WebApplication MapProjectWorkspaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/project-workspace/overview", (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapGet("/api/project-workspace/view-as/users", (Func<HttpContext, Task<IResult>>)ListViewAsUsersAsync);
        app.MapGet("/api/project-workspace/documents/{documentId:guid}/download", (Func<Guid, HttpContext, Task<IResult>>)DownloadDocumentAsync);
        return app;
    }

    private static async Task<IResult> ListViewAsUsersAsync(HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);

        if (sessionUserId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var config = ProjectWorkspaceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var administratorAccess = await LoadAccessContextAsync(connection, sessionUserId.Value);

        if (!administratorAccess.IsAdministrator)
        {
            return Results.Json(new
            {
                status = "forbidden",
                message = "Only Administrators can use View As User preview."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var users = new List<ProjectWorkspaceViewAsUser>();

        const string sql = """
            SELECT
                u.user_id AS user_id,
                u.display_name AS display_name,
                u.email AS email,
                '' AS job_title,
                COALESCE(u.team_name, u.department_name, u.department, '') AS team_or_department,
                COALESCE(string_agg(DISTINCT r.role_code, ', ' ORDER BY r.role_code), '') AS role_codes,
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS assignment_count,
                COUNT(DISTINCT managed.project_id)::bigint AS managed_project_count
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            LEFT JOIN project_assignments pa
                ON pa.user_id = u.user_id
            LEFT JOIN projects managed
                ON managed.project_manager_user_id = u.user_id
            WHERE u.is_active = TRUE
              AND u.login_enabled = TRUE
            GROUP BY u.user_id, u.display_name, u.email, u.team_name, u.department_name, u.department
            ORDER BY u.display_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);

            users.Add(new ProjectWorkspaceViewAsUser(
                reader.GetGuid(O("user_id")),
                reader.GetString(O("display_name")),
                reader.GetString(O("email")),
                reader.GetString(O("job_title")),
                reader.GetString(O("team_or_department")),
                reader.GetString(O("role_codes")),
                reader.GetInt64(O("assignment_count")),
                reader.GetInt64(O("managed_project_count"))));
        }

        return Results.Ok(new
        {
            mode = "administrator_view_as_preview",
            previewMode = "read_only",
            users
        });
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);

        if (sessionUserId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var config = ProjectWorkspaceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var actualAccess = await LoadAccessContextAsync(connection, sessionUserId.Value);
        var access = await ResolveViewAsAccessContextAsync(connection, httpContext, actualAccess);

        await InsertViewAsAuditIfNeededAsync(connection, actualAccess, access, "/api/project-workspace/overview");

        var projects = await LoadProjectsAsync(connection, access);
        var documents = await LoadDocumentsAsync(connection, access);
        var assignments = await LoadAssignmentsAsync(connection, access);
        var resourceRequests = await LoadResourceRequestsAsync(connection, access);

        return Results.Ok(new
        {
            module = "019M-U Project Workspace User Experience Preview",
            mode = "role_scope_enforced",
            access = new
            {
                userId = access.UserId,
                email = access.Email,
                roles = access.RoleCodes,
                scope = access.ScopeLabel,
                actualUserId = actualAccess.UserId,
                actualEmail = actualAccess.Email,
                isViewAs = access.UserId != actualAccess.UserId
            },
            summary = new
            {
                projectCount = projects.Count,
                documentCount = documents.Count,
                engineeringVisibleDocumentCount = documents.Count(d => d.EngineeringVisible),
                aiContextReadyDocumentCount = documents.Count(d => d.AiTimesheetContextEnabled),
                assignmentCount = assignments.Count,
                openResourceRequestCount = resourceRequests.Count(r => !new[] { "assigned", "fulfilled", "cancelled" }.Contains(r.Status, StringComparer.OrdinalIgnoreCase))
            },
            projects,
            documents,
            assignments,
            resourceRequests,
            guardrails = new[]
            {
                "Project Workspace records are filtered by backend role scope.",
                "Administrator View-As preview is read-only.",
                "View-As preview records both the administrator and effective viewed user.",
                "Engineers see only directly assigned work.",
                "PMs see all documents for projects they manage.",
                "Legacy PROJECT_MANAGER and PROJECT_MANAGEMENT roles share managed-project workspace scope.",
                "Project Team Coordinators, Executives, and Administrators have broader visibility based on role."
            }
        });
    }

    private static async Task<ProjectWorkspaceAccessContext> ResolveViewAsAccessContextAsync(
        NpgsqlConnection connection,
        HttpContext httpContext,
        ProjectWorkspaceAccessContext actualAccess)
    {
        var viewAsUserId = GetViewAsUserId(httpContext);

        if (viewAsUserId is null || viewAsUserId.Value == actualAccess.UserId)
        {
            return actualAccess;
        }

        if (!actualAccess.IsAdministrator)
        {
            return actualAccess;
        }

        return await LoadAccessContextAsync(connection, viewAsUserId.Value);
    }

    private static Guid? GetViewAsUserId(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-ProjectPulse-View-As-User", out var headerValue)
            && Guid.TryParse(headerValue.ToString(), out var headerUserId))
        {
            return headerUserId;
        }

        if (httpContext.Request.Query.TryGetValue("viewAsUserId", out var queryValue)
            && Guid.TryParse(queryValue.ToString(), out var queryUserId))
        {
            return queryUserId;
        }

        return null;
    }

    private static async Task InsertViewAsAuditIfNeededAsync(
        NpgsqlConnection connection,
        ProjectWorkspaceAccessContext actualAccess,
        ProjectWorkspaceAccessContext effectiveAccess,
        string route)
    {
        if (actualAccess.UserId == effectiveAccess.UserId || !actualAccess.IsAdministrator)
        {
            return;
        }

        try
        {
            const string sql = """
                INSERT INTO projectpulse_admin_view_as_audit (
                    administrator_user_id,
                    viewed_as_user_id,
                    viewed_route,
                    preview_mode,
                    action_taken
                )
                VALUES (
                    @administrator_user_id,
                    @viewed_as_user_id,
                    @viewed_route,
                    'read_only',
                    'view_as_preview'
                );
                """;

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("administrator_user_id", actualAccess.UserId);
            command.Parameters.AddWithValue("viewed_as_user_id", effectiveAccess.UserId);
            command.Parameters.AddWithValue("viewed_route", route);
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Do not break read-only preview if audit insert fails.
        }
    }

    private static async Task<ProjectWorkspaceAccessContext> LoadAccessContextAsync(NpgsqlConnection connection, Guid userId)
    {
        const string sql = """
            SELECT
                u.user_id,
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
            GROUP BY u.user_id, u.email, u.team_name, u.department_name, u.department;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return ProjectWorkspaceAccessContext.Empty(userId);
        }

        var roleCodes = reader.GetString(5)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProjectWorkspaceAccessContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            roleCodes);
    }

    private static async Task<List<ProjectWorkspaceProject>> LoadProjectsAsync(NpgsqlConnection connection, ProjectWorkspaceAccessContext access)
    {
        var rows = new List<ProjectWorkspaceProject>();

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
                          FROM projectpulse_team_scope_assignments tsa
                          WHERE tsa.scoped_user_id = @user_id
                            AND tsa.is_active = TRUE
                            AND (
                                (tsa.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name, '')) = LOWER(tsa.team_name))
                                OR (tsa.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name, '')) = LOWER(tsa.department_name))
                            )
                      )
                  )
            )
            SELECT
                p.project_id AS id,
                p.project_code AS project_code,
                p.project_name AS project_name,
                COALESCE(c.client_name, 'No client') AS client_name,
                p.status AS status,
                p.start_date AS start_date,
                p.end_date AS end_date,
                p.billable AS billable,
                pm.display_name AS project_manager_name,
                pm.email AS project_manager_email,
                /* 053I_WORKSPACE_AE_SA_START */
                ae.display_name AS account_executive_name,
                ae.email AS account_executive_email,
                sa.display_name AS solution_architect_name,
                sa.email AS solution_architect_email,
                /* 053I_WORKSPACE_AE_SA_END */
                COUNT(DISTINCT pt.task_id)::bigint AS task_count,
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS assignment_count,
                /* 053F_SCOPED_PROJECT_DOCUMENT_COUNT_START */
                COUNT(DISTINCT d.project_intake_document_id) FILTER (
                    WHERE d.is_active = TRUE
                      AND (
                          @is_broad_scope = TRUE
                          OR (@can_view_managed_projects = TRUE AND p.project_manager_user_id = @user_id)
                          OR (
                              COALESCE(d.engineering_visible, FALSE) = TRUE
                              AND EXISTS (
                                  SELECT 1
                                  FROM project_assignments doc_self_pa
                                  WHERE doc_self_pa.project_id = p.project_id
                                    AND doc_self_pa.user_id = @user_id
                              )
                          )
                          OR (
                              @can_view_team_scope = TRUE
                              AND COALESCE(d.engineering_visible, FALSE) = TRUE
                          )
                      )
                )::bigint AS document_count
                /* 053F_SCOPED_PROJECT_DOCUMENT_COUNT_END */
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            LEFT JOIN app_users ae ON ae.user_id = p.account_executive_user_id
            LEFT JOIN app_users sa ON sa.user_id = p.solution_architect_user_id
            LEFT JOIN project_tasks pt ON pt.project_id = p.project_id AND pt.is_active = TRUE
            LEFT JOIN project_assignments pa ON pa.project_id = p.project_id
            LEFT JOIN project_intake_documents d ON d.project_id = p.project_id
            WHERE
                @is_broad_scope = TRUE
                OR (@can_view_managed_projects = TRUE AND p.project_manager_user_id = @user_id)
                OR EXISTS (
                    SELECT 1 FROM project_assignments self_pa
                    WHERE self_pa.project_id = p.project_id
                      AND self_pa.user_id = @user_id
                )
                OR (
                    @can_view_team_scope = TRUE
                    AND EXISTS (
                        SELECT 1 FROM project_assignments team_pa
                        WHERE team_pa.project_id = p.project_id
                          AND team_pa.user_id IN (SELECT user_id FROM team_members)
                    )
                )
                OR (
                    @can_view_team_scope = TRUE
                    AND p.project_manager_user_id IN (SELECT user_id FROM team_members)
                )
            GROUP BY p.project_id, p.project_code, p.project_name, c.client_name, p.status, p.start_date, p.end_date, p.billable, pm.display_name, pm.email, ae.display_name, ae.email, sa.display_name, sa.email
            ORDER BY p.created_at DESC
            LIMIT 100;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));

            rows.Add(new ProjectWorkspaceProject(
                reader.GetGuid(O("id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("client_name")),
                reader.GetString(O("status")),
                ReadDateOnlyOrNull(reader, O("start_date")),
                ReadDateOnlyOrNull(reader, O("end_date")),
                reader.GetBoolean(O("billable")),
                S("project_manager_name"),
                S("project_manager_email"),
                S("account_executive_name"),
                S("account_executive_email"),
                S("account_executive_name"),
                S("account_executive_email"),
                S("solution_architect_name"),
                S("solution_architect_email"),
                reader.GetInt64(O("task_count")),
                reader.GetInt64(O("assignment_count")),
                reader.GetInt64(O("document_count"))));
        }

        return rows;
    }

    private static async Task<List<ProjectWorkspaceDocument>> LoadDocumentsAsync(NpgsqlConnection connection, ProjectWorkspaceAccessContext access)
    {
        var rows = new List<ProjectWorkspaceDocument>();

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                  )
            )
            SELECT
                d.project_intake_document_id AS id,
                d.project_intake_request_id AS project_intake_request_id,
                d.project_id AS project_id,
                COALESCE(p.project_code, 'No project') AS project_code,
                COALESCE(p.project_name, pir.request_title, 'Unlinked document') AS project_or_intake_name,
                pir.request_number AS request_number,
                d.document_type AS document_type,
                COALESCE(d.document_category, 'supporting') AS document_category,
                d.original_file_name AS original_file_name,
                d.content_type AS content_type,
                COALESCE(d.size_bytes, 0)::bigint AS size_bytes,
                COALESCE(d.engineering_visible, FALSE) AS engineering_visible,
                COALESCE(d.ai_timesheet_context_enabled, FALSE) AS ai_timesheet_context_enabled,
                COALESCE(d.extraction_status, 'not_started') AS extraction_status,
                COALESCE(d.upload_source, 'manual') AS upload_source,
                d.uploaded_at AS uploaded_at
            FROM project_intake_documents d
            LEFT JOIN projects p ON p.project_id = d.project_id
            LEFT JOIN project_intake_requests pir ON pir.project_intake_request_id = d.project_intake_request_id
            WHERE d.is_active = TRUE
              AND (
                  @is_broad_scope = TRUE
                  OR (@can_view_managed_projects = TRUE AND (p.project_manager_user_id = @user_id OR pir.assigned_pm_user_id = @user_id))
                  OR (
                      COALESCE(d.engineering_visible, FALSE) = TRUE
                      AND EXISTS (
                          SELECT 1 FROM project_assignments self_pa
                          WHERE self_pa.project_id = d.project_id
                            AND self_pa.user_id = @user_id
                      )
                  )
                  OR (
                      @can_view_team_scope = TRUE
                      AND COALESCE(d.engineering_visible, FALSE) = TRUE
                      AND EXISTS (
                          SELECT 1 FROM project_assignments team_pa
                          WHERE team_pa.project_id = d.project_id
                            AND team_pa.user_id IN (SELECT user_id FROM team_members)
                      )
                  )
              )
            ORDER BY d.uploaded_at DESC
            LIMIT 100;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));

            var documentId = reader.GetGuid(O("id"));

            rows.Add(new ProjectWorkspaceDocument(
                documentId,
                reader.GetGuid(O("project_intake_request_id")),
                reader.IsDBNull(O("project_id")) ? null : reader.GetGuid(O("project_id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_or_intake_name")),
                S("request_number"),
                reader.GetString(O("document_type")),
                reader.GetString(O("document_category")),
                reader.GetString(O("original_file_name")),
                S("content_type"),
                reader.GetInt64(O("size_bytes")),
                reader.GetBoolean(O("engineering_visible")),
                reader.GetBoolean(O("ai_timesheet_context_enabled")),
                reader.GetString(O("extraction_status")),
                reader.GetString(O("upload_source")),
                ReadDateTimeOffset(reader, O("uploaded_at")),
                $"/api/project-workspace/documents/{documentId}/download"));
        }

        return rows;
    }

    private static async Task<List<ProjectWorkspaceAssignment>> LoadAssignmentsAsync(NpgsqlConnection connection, ProjectWorkspaceAccessContext access)
    {
        var rows = new List<ProjectWorkspaceAssignment>();

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                  )
            ),
            resource_alloc AS (
                SELECT
                    err.project_id,
                    erra.user_id,
                    SUM(erra.allocated_hours)::numeric
                        / NULLIF(COUNT(DISTINCT pa2.project_assignment_id), 0)::numeric AS allocated_hours_per_task
                FROM engineering_resource_requests err
                JOIN engineering_resource_request_assignments erra
                    ON erra.engineering_resource_request_id = err.engineering_resource_request_id
                LEFT JOIN project_assignments pa2
                    ON pa2.project_id = err.project_id
                   AND pa2.user_id = erra.user_id
                WHERE err.project_id IS NOT NULL
                GROUP BY err.project_id, erra.user_id
            ),
            used_time AS (
                SELECT
                    user_id,
                    project_id,
                    task_id,
                    SUM(hours)::numeric AS used_hours
                FROM time_entries
                WHERE status NOT IN ('voided', 'rejected')
                  AND project_id IS NOT NULL
                  AND task_id IS NOT NULL
                GROUP BY user_id, project_id, task_id
            )
            SELECT
                pa.project_assignment_id AS id,
                p.project_code AS project_code,
                p.project_name AS project_name,
                pt.task_code AS task_code,
                pt.task_name AS task_name,
                u.display_name AS engineer_name,
                u.email AS engineer_email,
                pa.effective_start_date AS effective_start_date,
                pa.effective_end_date AS effective_end_date,
                COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric AS assigned_hours,
                COALESCE(used_time.used_hours, 0)::numeric AS used_hours,
                GREATEST(
                    COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric
                    - COALESCE(used_time.used_hours, 0)::numeric,
                    0
                )::numeric AS remaining_hours,
                (
                    COALESCE(used_time.used_hours, 0)::numeric >
                    COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric
                    AND COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric > 0
                ) AS is_over_allocated,
                pa.allocation_percent AS allocation_percent
            FROM project_assignments pa
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt ON pt.task_id = pa.task_id
            JOIN app_users u ON u.user_id = pa.user_id
            LEFT JOIN resource_alloc
                ON resource_alloc.project_id = pa.project_id
               AND resource_alloc.user_id = pa.user_id
            LEFT JOIN used_time
                ON used_time.project_id = pa.project_id
               AND used_time.task_id = pa.task_id
               AND used_time.user_id = pa.user_id
            WHERE
                @is_broad_scope = TRUE
                OR pa.user_id = @user_id
                OR (@can_view_managed_projects = TRUE AND p.project_manager_user_id = @user_id)
                OR (@can_view_team_scope = TRUE AND pa.user_id IN (SELECT user_id FROM team_members))
            ORDER BY p.project_code, u.display_name, pa.effective_start_date
            LIMIT 100;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);

            rows.Add(new ProjectWorkspaceAssignment(
                reader.GetGuid(O("id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("task_code")),
                reader.GetString(O("task_name")),
                reader.GetString(O("engineer_name")),
                reader.GetString(O("engineer_email")),
                ReadDateOnly(reader, O("effective_start_date")),
                ReadDateOnlyOrNull(reader, O("effective_end_date")),
                reader.GetDecimal(O("assigned_hours")),
                reader.GetDecimal(O("used_hours")),
                reader.GetDecimal(O("remaining_hours")),
                reader.GetBoolean(O("is_over_allocated")),
                reader.IsDBNull(O("allocation_percent")) ? null : reader.GetDecimal(O("allocation_percent"))));
        }

        return rows;
    }

    private static async Task<List<ProjectWorkspaceResourceRequest>> LoadResourceRequestsAsync(NpgsqlConnection connection, ProjectWorkspaceAccessContext access)
    {
        var rows = new List<ProjectWorkspaceResourceRequest>();

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                  )
            )
            SELECT
                err.request_number AS request_number,
                COALESCE(p.project_code, 'No project') AS project_code,
                COALESCE(p.project_name, pir.request_title, 'Unlinked request') AS source_name,
                err.requested_function AS requested_function,
                err.requested_hours AS requested_hours,
                err.priority AS priority,
                err.request_status AS status,
                COALESCE(assigned.assigned_engineers, primary_engineer.display_name) AS assigned_engineers,
                COALESCE(
                    assigned.assigned_engineer_count,
                    CASE WHEN err.fulfilled_by_user_id IS NULL THEN 0::bigint ELSE 1::bigint END
                )::bigint AS assigned_engineer_count
            FROM engineering_resource_requests err
            LEFT JOIN projects p ON p.project_id = err.project_id
            LEFT JOIN project_intake_requests pir ON pir.project_intake_request_id = err.project_intake_request_id
            LEFT JOIN app_users primary_engineer ON primary_engineer.user_id = err.fulfilled_by_user_id
            LEFT JOIN (
                SELECT
                    erra.engineering_resource_request_id,
                    STRING_AGG(u.display_name, ', ' ORDER BY u.display_name) AS assigned_engineers,
                    COUNT(*)::bigint AS assigned_engineer_count
                FROM engineering_resource_request_assignments erra
                JOIN app_users u ON u.user_id = erra.user_id
                GROUP BY erra.engineering_resource_request_id
            ) assigned ON assigned.engineering_resource_request_id = err.engineering_resource_request_id
            WHERE
                @is_broad_scope = TRUE
                OR err.fulfilled_by_user_id = @user_id
                OR EXISTS (
                    SELECT 1 FROM engineering_resource_request_assignments self_erra
                    WHERE self_erra.engineering_resource_request_id = err.engineering_resource_request_id
                      AND self_erra.user_id = @user_id
                )
                OR (@can_view_managed_projects = TRUE AND (err.assigned_pm_user_id = @user_id OR p.project_manager_user_id = @user_id OR pir.assigned_pm_user_id = @user_id))
                OR (
                    @can_view_team_scope = TRUE
                    AND (
                        err.fulfilled_by_user_id IN (SELECT user_id FROM team_members)
                        OR err.assigned_pm_user_id IN (SELECT user_id FROM team_members)
                        OR EXISTS (
                            SELECT 1 FROM engineering_resource_request_assignments team_erra
                            WHERE team_erra.engineering_resource_request_id = err.engineering_resource_request_id
                              AND team_erra.user_id IN (SELECT user_id FROM team_members)
                        )
                    )
                )
            ORDER BY err.created_at DESC
            LIMIT 100;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));

            rows.Add(new ProjectWorkspaceResourceRequest(
                reader.GetString(O("request_number")),
                reader.GetString(O("project_code")),
                reader.GetString(O("source_name")),
                reader.GetString(O("requested_function")),
                reader.GetDecimal(O("requested_hours")),
                reader.GetString(O("priority")),
                reader.GetString(O("status")),
                S("assigned_engineers"),
                reader.GetInt64(O("assigned_engineer_count"))));
        }

        return rows;
    }

    private static async Task<IResult> DownloadDocumentAsync(Guid documentId, HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);

        if (sessionUserId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var config = ProjectWorkspaceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var actualAccess = await LoadAccessContextAsync(connection, sessionUserId.Value);
        var access = await ResolveViewAsAccessContextAsync(connection, httpContext, actualAccess);

        const string sql = """
            WITH team_members AS (
                SELECT member.user_id
                FROM app_users member
                WHERE member.is_active = TRUE
                  AND (
                      (COALESCE(@team_name, '') <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(@team_name))
                      OR (COALESCE(@department_name, '') <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(@department_name))
                  )
            )
            SELECT d.original_file_name, d.storage_path, d.content_type
            FROM project_intake_documents d
            LEFT JOIN projects p ON p.project_id = d.project_id
            LEFT JOIN project_intake_requests pir ON pir.project_intake_request_id = d.project_intake_request_id
            WHERE d.project_intake_document_id = @document_id
              AND d.is_active = TRUE
              AND (
                  @is_broad_scope = TRUE
                  OR (@can_view_managed_projects = TRUE AND (p.project_manager_user_id = @user_id OR pir.assigned_pm_user_id = @user_id))
                  OR (
                      COALESCE(d.engineering_visible, FALSE) = TRUE
                      AND EXISTS (
                          SELECT 1 FROM project_assignments self_pa
                          WHERE self_pa.project_id = d.project_id
                            AND self_pa.user_id = @user_id
                      )
                  )
                  OR (
                      @can_view_team_scope = TRUE
                      AND COALESCE(d.engineering_visible, FALSE) = TRUE
                      AND EXISTS (
                          SELECT 1 FROM project_assignments team_pa
                          WHERE team_pa.project_id = d.project_id
                            AND team_pa.user_id IN (SELECT user_id FROM team_members)
                      )
                  )
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("document_id", documentId);
        AddScopeParameters(command, access);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "Project document was not found or is outside your role scope."
            });
        }

        var originalFileName = reader.GetString(0);
        var storagePath = reader.GetString(1);
        var contentType = reader.IsDBNull(2) ? "application/octet-stream" : reader.GetString(2);

        if (!File.Exists(storagePath))
        {
            return Results.NotFound(new
            {
                status = "file_missing",
                message = "Document metadata exists, but the stored file was not found."
            });
        }

        return Results.File(storagePath, contentType, originalFileName);
    }

    private static void AddScopeParameters(NpgsqlCommand command, ProjectWorkspaceAccessContext access)
    {
        command.Parameters.AddWithValue("user_id", access.UserId);
        command.Parameters.AddWithValue("email", access.Email);
        command.Parameters.AddWithValue("team_name", access.TeamName ?? string.Empty);
        command.Parameters.AddWithValue("department_name", access.DepartmentName ?? string.Empty);
        command.Parameters.AddWithValue("is_broad_scope", access.IsBroadScope);
        command.Parameters.AddWithValue("can_view_managed_projects", access.CanViewManagedProjects);
        command.Parameters.AddWithValue("can_view_team_scope", access.CanViewTeamScope);
    }

    private static Guid? GetSessionUserId(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("ProjectPulseSessionUserId", out var value) && value is Guid userId)
        {
            return userId;
        }

        return null;
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

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);

        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static IResult? ValidateConfig(ProjectWorkspaceDatabaseConfig config)
    {
        if (config.Missing.Count == 0) return null;

        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }
}

internal sealed record ProjectWorkspaceViewAsUser(
    Guid UserId,
    string DisplayName,
    string Email,
    string JobTitle,
    string TeamOrDepartment,
    string RoleCodes,
    long AssignmentCount,
    long ManagedProjectCount);

internal sealed record ProjectWorkspaceAccessContext(
    Guid UserId,
    string Email,
    string TeamName,
    string DepartmentName,
    string Department,
    IReadOnlySet<string> RoleCodes)
{
    public static ProjectWorkspaceAccessContext Empty(Guid userId)
    {
        return new ProjectWorkspaceAccessContext(userId, string.Empty, string.Empty, string.Empty, string.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public bool HasRole(string roleCode) => RoleCodes.Contains(roleCode);

    public bool IsAdministrator => (HasRole("SUPER_ADMINISTRATOR") || HasRole("ADMINISTRATOR"));
    public bool IsCoordinator => HasRole("PROJECT_TEAM_COORDINATOR");
    public bool IsExecutive => HasRole("EXECUTIVE");
    public bool IsManager => HasRole("MANAGER");
    public bool IsEngineeringLead => (HasRole("ENGINEERING_LEAD") || HasRole("ENGINEERING_TEAM_LEAD"));
    public bool IsProjectManagementLead => (HasRole("PROJECT_MANAGEMENT_LEAD") || HasRole("PROJECT_MANAGEMENT_TEAM_LEAD"));
    /* 053F_PROJECT_MANAGER_DOCUMENT_SCOPE_START */
    public bool IsProjectManager => HasRole("PROJECT_MANAGEMENT") || HasRole("PROJECT_MANAGER");
    /* 053F_PROJECT_MANAGER_DOCUMENT_SCOPE_END */

    public bool IsBroadScope => IsAdministrator || IsCoordinator || IsExecutive;
    public bool CanViewManagedProjects => IsBroadScope || IsProjectManager || IsProjectManagementLead || IsCoordinator;
    public bool CanViewTeamScope => IsBroadScope || IsManager || IsEngineeringLead || IsProjectManagementLead || IsCoordinator;

    public string ScopeLabel
    {
        get
        {
            if (IsAdministrator) return "administrator_full_scope";
            if (IsCoordinator) return "project_team_coordinator_operations_scope";
            if (IsExecutive) return "executive_organization_read_scope";
            if (IsManager) return "manager_team_scope";
            if (IsEngineeringLead) return "engineering_team_lead_scope";
            if (IsProjectManagementLead) return "project_management_team_lead_scope";
            if (IsProjectManager) return "project_management_managed_projects_scope";
            return "assigned_self_scope";
        }
    }
}

internal sealed record ProjectWorkspaceProject(
    Guid Id,
    string ProjectCode,
    string ProjectName,
    string ClientName,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool Billable,
    string? ProjectManagerName,
    string? ProjectManagerEmail,
    string? SalesExecutiveName,
    string? SalesExecutiveEmail,
    string? AccountExecutiveName,
    string? AccountExecutiveEmail,
    string? SolutionArchitectName,
    string? SolutionArchitectEmail,
    long TaskCount,
    long AssignmentCount,
    long DocumentCount);

internal sealed record ProjectWorkspaceDocument(
    Guid Id,
    Guid ProjectIntakeRequestId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectOrIntakeName,
    string? RequestNumber,
    string DocumentType,
    string DocumentCategory,
    string OriginalFileName,
    string? ContentType,
    long SizeBytes,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string ExtractionStatus,
    string UploadSource,
    DateTimeOffset UploadedAt,
    string DownloadUrl);

internal sealed record ProjectWorkspaceAssignment(
    Guid Id,
    string ProjectCode,
    string ProjectName,
    string TaskCode,
    string TaskName,
    string EngineerName,
    string EngineerEmail,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    decimal AssignedHours,
    decimal UsedHours,
    decimal RemainingHours,
    bool IsOverAllocated,
    decimal? AllocationPercent);

internal sealed record ProjectWorkspaceResourceRequest(
    string RequestNumber,
    string ProjectCode,
    string SourceName,
    string RequestedFunction,
    decimal RequestedHours,
    string Priority,
    string Status,
    string? AssignedEngineers,
    long AssignedEngineerCount);

internal sealed record ProjectWorkspaceDatabaseConfig(
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

    public static ProjectWorkspaceDatabaseConfig FromEnvironment()
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

        return new ProjectWorkspaceDatabaseConfig(host, port, database, username, password, missing);
    }
}
