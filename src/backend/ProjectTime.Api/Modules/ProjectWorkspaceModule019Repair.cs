using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 019 role-scoped workspace repair.
///
/// Direct, active project assignments and direct service-request assignments
/// authorize the effective engineer to list and download the related documents.
/// Broader manager/team scopes continue to honor engineering_visible.
/// </summary>
internal static class ProjectWorkspaceModule019Repair
{
    internal static WebApplication MapEndpoints(WebApplication app)
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

        var config = ProjectWorkspace019DatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(httpContext.RequestAborted);

        var administratorAccess = await LoadAccessContextAsync(connection, sessionUserId.Value, httpContext.RequestAborted);
        if (!administratorAccess.IsAdministrator)
        {
            return Results.Json(new
            {
                status = "forbidden",
                message = "Only Administrators can use View As User preview."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var users = new List<ProjectWorkspace019ViewAsUser>();
        const string sql = """
            SELECT
                u.user_id,
                u.display_name,
                u.email,
                '' AS job_title,
                COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), '') AS team_or_department,
                COALESCE(string_agg(DISTINCT r.role_code, ', ' ORDER BY r.role_code), '') AS role_codes,
                COUNT(DISTINCT pa.project_assignment_id) FILTER (
                    WHERE pa.effective_start_date <= CURRENT_DATE
                      AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= CURRENT_DATE)
                )::bigint AS assignment_count,
                COUNT(DISTINCT managed.project_id)::bigint AS managed_project_count
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            LEFT JOIN project_assignments pa ON pa.user_id = u.user_id
            LEFT JOIN projects managed ON managed.project_manager_user_id = u.user_id
            WHERE u.is_active = TRUE
              AND u.login_enabled = TRUE
            GROUP BY u.user_id, u.display_name, u.email, u.team_name, u.department_name, u.department
            ORDER BY u.display_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(httpContext.RequestAborted);
        while (await reader.ReadAsync(httpContext.RequestAborted))
        {
            int O(string name) => reader.GetOrdinal(name);
            users.Add(new ProjectWorkspace019ViewAsUser(
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

        var config = ProjectWorkspace019DatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(httpContext.RequestAborted);

        var actualAccess = await LoadAccessContextAsync(connection, sessionUserId.Value, httpContext.RequestAborted);
        var access = await ResolveViewAsAccessContextAsync(connection, httpContext, actualAccess);
        await InsertViewAsAuditIfNeededAsync(
            connection,
            actualAccess,
            access,
            "/api/project-workspace/overview",
            httpContext.RequestAborted);

        var projects = await LoadProjectsAsync(connection, access, httpContext.RequestAborted);
        var documents = await LoadDocumentsAsync(connection, access, httpContext.RequestAborted);
        var assignments = await LoadAssignmentsAsync(connection, access, httpContext.RequestAborted);
        var resourceRequests = await LoadResourceRequestsAsync(connection, access, httpContext.RequestAborted);

        return Results.Ok(new
        {
            module = "019",
            mode = "assignment_and_role_scope_enforced",
            access = new
            {
                userId = access.UserId,
                email = access.Email,
                teamName = access.TeamName,
                departmentName = access.DepartmentName,
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
                engineeringVisibleDocumentCount = documents.Count(document => document.EngineeringVisible),
                aiContextReadyDocumentCount = documents.Count(document => document.AiTimesheetContextEnabled),
                assignmentCount = assignments.Count,
                openResourceRequestCount = resourceRequests.Count(request =>
                    !new[] { "assigned", "fulfilled", "cancelled", "canceled", "closed", "archived" }
                        .Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            },
            projects,
            documents,
            assignments,
            resourceRequests,
            guardrails = new[]
            {
                "Project Workspace records are filtered by backend role and assignment scope.",
                "Engineers see only projects or service requests assigned directly to them.",
                "A direct active assignment grants access to all documents related to that assigned project or service request.",
                "Broader manager and engineering-lead scopes continue to require engineering-visible documents.",
                "Document listing and document download use the same authorization predicates.",
                "Closed, completed, cancelled, and archived projects are removed from assigned engineering workspaces.",
                "Administrator View-As preview is read-only and audited."
            }
        });
    }

    private static async Task<ProjectWorkspace019AccessContext> ResolveViewAsAccessContextAsync(
        NpgsqlConnection connection,
        HttpContext httpContext,
        ProjectWorkspace019AccessContext actualAccess)
    {
        var viewAsUserId = GetViewAsUserId(httpContext);
        if (viewAsUserId is null || viewAsUserId.Value == actualAccess.UserId || !actualAccess.IsAdministrator)
        {
            return actualAccess;
        }

        return await LoadAccessContextAsync(connection, viewAsUserId.Value, httpContext.RequestAborted);
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
        ProjectWorkspace019AccessContext actualAccess,
        ProjectWorkspace019AccessContext effectiveAccess,
        string route,
        CancellationToken cancellationToken)
    {
        if (actualAccess.UserId == effectiveAccess.UserId || !actualAccess.IsAdministrator) return;

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
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Read-only preview must not fail because the supplemental audit insert is unavailable.
        }
    }

    private static async Task<ProjectWorkspace019AccessContext> LoadAccessContextAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
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
              AND u.is_active = TRUE
            GROUP BY u.user_id, u.email, u.team_name, u.department_name, u.department;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return ProjectWorkspace019AccessContext.Empty(userId);
        }

        var roleCodes = reader.GetString(5)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ProjectWorkspace019AccessContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            roleCodes);
    }

    private static async Task<List<ProjectWorkspace019Project>> LoadProjectsAsync(
        NpgsqlConnection connection,
        ProjectWorkspace019AccessContext access,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectWorkspace019Project>();
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
                p.project_code,
                p.project_name,
                COALESCE(c.client_name, 'No client') AS client_name,
                p.status,
                p.start_date,
                p.end_date,
                p.billable,
                pm.display_name AS project_manager_name,
                pm.email AS project_manager_email,
                ae.display_name AS account_executive_name,
                ae.email AS account_executive_email,
                sa.display_name AS solution_architect_name,
                sa.email AS solution_architect_email,
                (
                    SELECT COUNT(*)::bigint
                    FROM project_tasks task
                    WHERE task.project_id = p.project_id
                      AND task.is_active = TRUE
                ) AS task_count,
                (
                    SELECT COUNT(*)::bigint
                    FROM project_assignments assignment
                    WHERE assignment.project_id = p.project_id
                      AND assignment.effective_start_date <= CURRENT_DATE
                      AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
                ) AS assignment_count,
                (
                    SELECT COUNT(*)::bigint
                    FROM project_intake_documents document
                    LEFT JOIN project_intake_requests intake
                      ON intake.project_intake_request_id = document.project_intake_request_id
                    WHERE document.project_id = p.project_id
                      AND document.is_active = TRUE
                      AND COALESCE(document.upload_source, '') <> 'celar_ai_chat_attachment'
                      AND (
                          @is_broad_scope = TRUE
                          OR (@can_view_managed_projects = TRUE
                              AND (p.project_manager_user_id = @user_id OR intake.assigned_pm_user_id = @user_id))
                          OR EXISTS (
                              SELECT 1
                              FROM project_assignments self_assignment
                              WHERE self_assignment.project_id = p.project_id
                                AND self_assignment.user_id = @user_id
                                AND self_assignment.effective_start_date <= CURRENT_DATE
                                AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date >= CURRENT_DATE)
                          )
                          OR EXISTS (
                              SELECT 1
                              FROM engineering_resource_requests self_request
                              WHERE LOWER(COALESCE(self_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                                AND (
                                    self_request.project_id = p.project_id
                                    OR (
                                        document.project_intake_request_id IS NOT NULL
                                        AND self_request.project_intake_request_id = document.project_intake_request_id
                                    )
                                )
                                AND (
                                    self_request.fulfilled_by_user_id = @user_id
                                    OR EXISTS (
                                        SELECT 1
                                        FROM engineering_resource_request_assignments self_request_assignment
                                        WHERE self_request_assignment.engineering_resource_request_id = self_request.engineering_resource_request_id
                                          AND self_request_assignment.user_id = @user_id
                                    )
                                )
                          )
                          OR (
                              @can_view_team_scope = TRUE
                              AND COALESCE(document.engineering_visible, FALSE) = TRUE
                              AND (
                                  EXISTS (
                                      SELECT 1
                                      FROM project_assignments team_assignment
                                      WHERE team_assignment.project_id = p.project_id
                                        AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                                        AND team_assignment.effective_start_date <= CURRENT_DATE
                                        AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date >= CURRENT_DATE)
                                  )
                                  OR EXISTS (
                                      SELECT 1
                                      FROM engineering_resource_requests team_request
                                      WHERE LOWER(COALESCE(team_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                                        AND team_request.project_id = p.project_id
                                        AND (
                                            team_request.fulfilled_by_user_id IN (SELECT user_id FROM team_members)
                                            OR EXISTS (
                                                SELECT 1
                                                FROM engineering_resource_request_assignments team_request_assignment
                                                WHERE team_request_assignment.engineering_resource_request_id = team_request.engineering_resource_request_id
                                                  AND team_request_assignment.user_id IN (SELECT user_id FROM team_members)
                                            )
                                        )
                                  )
                              )
                          )
                      )
                ) AS document_count
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            LEFT JOIN app_users ae ON ae.user_id = p.account_executive_user_id
            LEFT JOIN app_users sa ON sa.user_id = p.solution_architect_user_id
            WHERE LOWER(COALESCE(p.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
              AND (
                  @is_broad_scope = TRUE
                  OR (@can_view_managed_projects = TRUE AND p.project_manager_user_id = @user_id)
                  OR EXISTS (
                      SELECT 1
                      FROM project_assignments self_assignment
                      WHERE self_assignment.project_id = p.project_id
                        AND self_assignment.user_id = @user_id
                        AND self_assignment.effective_start_date <= CURRENT_DATE
                        AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date >= CURRENT_DATE)
                  )
                  OR EXISTS (
                      SELECT 1
                      FROM engineering_resource_requests self_request
                      WHERE self_request.project_id = p.project_id
                        AND LOWER(COALESCE(self_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                        AND (
                            self_request.fulfilled_by_user_id = @user_id
                            OR EXISTS (
                                SELECT 1
                                FROM engineering_resource_request_assignments self_request_assignment
                                WHERE self_request_assignment.engineering_resource_request_id = self_request.engineering_resource_request_id
                                  AND self_request_assignment.user_id = @user_id
                            )
                        )
                  )
                  OR (
                      @can_view_team_scope = TRUE
                      AND (
                          EXISTS (
                              SELECT 1
                              FROM project_assignments team_assignment
                              WHERE team_assignment.project_id = p.project_id
                                AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                                AND team_assignment.effective_start_date <= CURRENT_DATE
                                AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date >= CURRENT_DATE)
                          )
                          OR p.project_manager_user_id IN (SELECT user_id FROM team_members)
                          OR EXISTS (
                              SELECT 1
                              FROM engineering_resource_requests team_request
                              WHERE team_request.project_id = p.project_id
                                AND LOWER(COALESCE(team_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                                AND (
                                    team_request.fulfilled_by_user_id IN (SELECT user_id FROM team_members)
                                    OR EXISTS (
                                        SELECT 1
                                        FROM engineering_resource_request_assignments team_request_assignment
                                        WHERE team_request_assignment.engineering_resource_request_id = team_request.engineering_resource_request_id
                                          AND team_request_assignment.user_id IN (SELECT user_id FROM team_members)
                                    )
                                )
                          )
                      )
                  )
              )
            ORDER BY p.created_at DESC
            LIMIT 100;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));
            rows.Add(new ProjectWorkspace019Project(
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

    private static async Task<List<ProjectWorkspace019Document>> LoadDocumentsAsync(
        NpgsqlConnection connection,
        ProjectWorkspace019AccessContext access,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectWorkspace019Document>();
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
            scoped_documents AS (
                SELECT
                    document.project_intake_document_id,
                    EXISTS (
                        SELECT 1
                        FROM project_assignments self_assignment
                        WHERE self_assignment.project_id = document.project_id
                          AND self_assignment.user_id = @user_id
                          AND self_assignment.effective_start_date <= CURRENT_DATE
                          AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date >= CURRENT_DATE)
                    ) AS direct_project_assignment,
                    EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests self_request
                        WHERE LOWER(COALESCE(self_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                          AND (
                              (document.project_id IS NOT NULL AND self_request.project_id = document.project_id)
                              OR (
                                  document.project_intake_request_id IS NOT NULL
                                  AND self_request.project_intake_request_id = document.project_intake_request_id
                              )
                          )
                          AND (
                              self_request.fulfilled_by_user_id = @user_id
                              OR EXISTS (
                                  SELECT 1
                                  FROM engineering_resource_request_assignments self_request_assignment
                                  WHERE self_request_assignment.engineering_resource_request_id = self_request.engineering_resource_request_id
                                    AND self_request_assignment.user_id = @user_id
                              )
                          )
                    ) AS direct_service_request_assignment,
                    EXISTS (
                        SELECT 1
                        FROM project_assignments team_assignment
                        WHERE team_assignment.project_id = document.project_id
                          AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                          AND team_assignment.effective_start_date <= CURRENT_DATE
                          AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date >= CURRENT_DATE)
                    ) AS team_project_assignment,
                    EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests team_request
                        WHERE LOWER(COALESCE(team_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                          AND (
                              (document.project_id IS NOT NULL AND team_request.project_id = document.project_id)
                              OR (
                                  document.project_intake_request_id IS NOT NULL
                                  AND team_request.project_intake_request_id = document.project_intake_request_id
                              )
                          )
                          AND (
                              team_request.fulfilled_by_user_id IN (SELECT user_id FROM team_members)
                              OR EXISTS (
                                  SELECT 1
                                  FROM engineering_resource_request_assignments team_request_assignment
                                  WHERE team_request_assignment.engineering_resource_request_id = team_request.engineering_resource_request_id
                                    AND team_request_assignment.user_id IN (SELECT user_id FROM team_members)
                              )
                          )
                    ) AS team_service_request_assignment
                FROM project_intake_documents document
            )
            SELECT
                document.project_intake_document_id AS id,
                document.project_intake_request_id,
                document.project_id,
                COALESCE(project.project_code, request.request_number, 'No project') AS project_code,
                COALESCE(project.project_name, request.request_title, 'Unlinked document') AS project_or_intake_name,
                request.request_number,
                document.document_type,
                COALESCE(document.document_category, 'supporting') AS document_category,
                document.original_file_name,
                document.content_type,
                COALESCE(document.size_bytes, 0)::bigint AS size_bytes,
                (
                    COALESCE(document.engineering_visible, FALSE)
                    OR scope.direct_project_assignment
                    OR scope.direct_service_request_assignment
                ) AS engineering_visible,
                COALESCE(document.ai_timesheet_context_enabled, FALSE) AS ai_timesheet_context_enabled,
                COALESCE(document.extraction_status, 'not_started') AS extraction_status,
                COALESCE(document.upload_source, 'manual') AS upload_source,
                document.uploaded_at
            FROM project_intake_documents document
            JOIN scoped_documents scope
              ON scope.project_intake_document_id = document.project_intake_document_id
            LEFT JOIN projects project ON project.project_id = document.project_id
            LEFT JOIN project_intake_requests request
              ON request.project_intake_request_id = document.project_intake_request_id
            WHERE document.is_active = TRUE
              AND COALESCE(document.upload_source, '') <> 'celar_ai_chat_attachment'
              AND (
                  @hide_closed_projects = FALSE
                  OR project.project_id IS NULL
                  OR LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
              )
              AND (
                  @is_broad_scope = TRUE
                  OR (
                      @can_view_managed_projects = TRUE
                      AND (project.project_manager_user_id = @user_id OR request.assigned_pm_user_id = @user_id)
                  )
                  OR scope.direct_project_assignment
                  OR scope.direct_service_request_assignment
                  OR (
                      @can_view_team_scope = TRUE
                      AND COALESCE(document.engineering_visible, FALSE) = TRUE
                      AND (scope.team_project_assignment OR scope.team_service_request_assignment)
                  )
              )
            ORDER BY document.uploaded_at DESC
            LIMIT 250;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));
            var documentId = reader.GetGuid(O("id"));
            rows.Add(new ProjectWorkspace019Document(
                documentId,
                reader.IsDBNull(O("project_intake_request_id")) ? null : reader.GetGuid(O("project_intake_request_id")),
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

    private static async Task<List<ProjectWorkspace019Assignment>> LoadAssignmentsAsync(
        NpgsqlConnection connection,
        ProjectWorkspace019AccessContext access,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectWorkspace019Assignment>();
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
                    request.project_id,
                    assignment.user_id,
                    SUM(assignment.allocated_hours)::numeric
                        / NULLIF(COUNT(DISTINCT project_assignment.project_assignment_id), 0)::numeric AS allocated_hours_per_task
                FROM engineering_resource_requests request
                JOIN engineering_resource_request_assignments assignment
                  ON assignment.engineering_resource_request_id = request.engineering_resource_request_id
                LEFT JOIN project_assignments project_assignment
                  ON project_assignment.project_id = request.project_id
                 AND project_assignment.user_id = assignment.user_id
                WHERE request.project_id IS NOT NULL
                GROUP BY request.project_id, assignment.user_id
            ),
            used_time AS (
                SELECT user_id, project_id, task_id, SUM(hours)::numeric AS used_hours
                FROM time_entries
                WHERE status NOT IN ('voided', 'rejected')
                  AND project_id IS NOT NULL
                  AND task_id IS NOT NULL
                GROUP BY user_id, project_id, task_id
            )
            SELECT
                assignment.project_assignment_id AS id,
                project.project_code,
                project.project_name,
                task.task_code,
                task.task_name,
                engineer.display_name AS engineer_name,
                engineer.email AS engineer_email,
                COALESCE(NULLIF(engineer.team_name, ''), NULLIF(engineer.department_name, ''), NULLIF(engineer.department, ''), '') AS engineer_team,
                assignment.effective_start_date,
                assignment.effective_end_date,
                COALESCE(NULLIF(assignment.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric AS assigned_hours,
                COALESCE(used_time.used_hours, 0)::numeric AS used_hours,
                GREATEST(
                    COALESCE(NULLIF(assignment.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric
                    - COALESCE(used_time.used_hours, 0)::numeric,
                    0
                )::numeric AS remaining_hours,
                (
                    COALESCE(used_time.used_hours, 0)::numeric >
                    COALESCE(NULLIF(assignment.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric
                    AND COALESCE(NULLIF(assignment.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric > 0
                ) AS is_over_allocated,
                assignment.allocation_percent
            FROM project_assignments assignment
            JOIN projects project ON project.project_id = assignment.project_id
            JOIN project_tasks task ON task.task_id = assignment.task_id
            JOIN app_users engineer ON engineer.user_id = assignment.user_id
            LEFT JOIN resource_alloc
              ON resource_alloc.project_id = assignment.project_id
             AND resource_alloc.user_id = assignment.user_id
            LEFT JOIN used_time
              ON used_time.project_id = assignment.project_id
             AND used_time.task_id = assignment.task_id
             AND used_time.user_id = assignment.user_id
            WHERE LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
              AND assignment.effective_start_date <= CURRENT_DATE
              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
              AND (
                  @is_broad_scope = TRUE
                  OR assignment.user_id = @user_id
                  OR (@can_view_managed_projects = TRUE AND project.project_manager_user_id = @user_id)
                  OR (@can_view_team_scope = TRUE AND assignment.user_id IN (SELECT user_id FROM team_members))
              )
            ORDER BY project.project_code, engineer.display_name, assignment.effective_start_date
            LIMIT 250;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int O(string name) => reader.GetOrdinal(name);
            rows.Add(new ProjectWorkspace019Assignment(
                reader.GetGuid(O("id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("task_code")),
                reader.GetString(O("task_name")),
                reader.GetString(O("engineer_name")),
                reader.GetString(O("engineer_email")),
                reader.GetString(O("engineer_team")),
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

    private static async Task<List<ProjectWorkspace019ResourceRequest>> LoadResourceRequestsAsync(
        NpgsqlConnection connection,
        ProjectWorkspace019AccessContext access,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectWorkspace019ResourceRequest>();
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
                request.request_number,
                COALESCE(project.project_code, intake.request_number, 'No project') AS project_code,
                COALESCE(project.project_name, intake.request_title, 'Unlinked request') AS source_name,
                request.requested_function,
                request.requested_hours,
                request.priority,
                request.request_status AS status,
                COALESCE(assigned.assigned_engineers, primary_engineer.display_name) AS assigned_engineers,
                COALESCE(
                    assigned.assigned_engineer_count,
                    CASE WHEN request.fulfilled_by_user_id IS NULL THEN 0::bigint ELSE 1::bigint END
                )::bigint AS assigned_engineer_count
            FROM engineering_resource_requests request
            LEFT JOIN projects project ON project.project_id = request.project_id
            LEFT JOIN project_intake_requests intake
              ON intake.project_intake_request_id = request.project_intake_request_id
            LEFT JOIN app_users primary_engineer ON primary_engineer.user_id = request.fulfilled_by_user_id
            LEFT JOIN (
                SELECT
                    request_assignment.engineering_resource_request_id,
                    STRING_AGG(engineer.display_name, ', ' ORDER BY engineer.display_name) AS assigned_engineers,
                    COUNT(*)::bigint AS assigned_engineer_count
                FROM engineering_resource_request_assignments request_assignment
                JOIN app_users engineer ON engineer.user_id = request_assignment.user_id
                GROUP BY request_assignment.engineering_resource_request_id
            ) assigned ON assigned.engineering_resource_request_id = request.engineering_resource_request_id
            WHERE (
                    @hide_closed_projects = FALSE
                    OR project.project_id IS NULL
                    OR LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
                  )
              AND (
                  @is_broad_scope = TRUE
                  OR request.fulfilled_by_user_id = @user_id
                  OR EXISTS (
                      SELECT 1
                      FROM engineering_resource_request_assignments self_assignment
                      WHERE self_assignment.engineering_resource_request_id = request.engineering_resource_request_id
                        AND self_assignment.user_id = @user_id
                  )
                  OR (
                      @can_view_managed_projects = TRUE
                      AND (
                          request.assigned_pm_user_id = @user_id
                          OR project.project_manager_user_id = @user_id
                          OR intake.assigned_pm_user_id = @user_id
                      )
                  )
                  OR (
                      @can_view_team_scope = TRUE
                      AND (
                          request.fulfilled_by_user_id IN (SELECT user_id FROM team_members)
                          OR request.assigned_pm_user_id IN (SELECT user_id FROM team_members)
                          OR EXISTS (
                              SELECT 1
                              FROM engineering_resource_request_assignments team_assignment
                              WHERE team_assignment.engineering_resource_request_id = request.engineering_resource_request_id
                                AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                          )
                      )
                  )
              )
            ORDER BY request.created_at DESC
            LIMIT 250;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));
            rows.Add(new ProjectWorkspace019ResourceRequest(
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

        var config = ProjectWorkspace019DatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(httpContext.RequestAborted);
        var actualAccess = await LoadAccessContextAsync(connection, sessionUserId.Value, httpContext.RequestAborted);
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
            ),
            scoped_document AS (
                SELECT
                    document.project_intake_document_id,
                    EXISTS (
                        SELECT 1
                        FROM project_assignments self_assignment
                        WHERE self_assignment.project_id = document.project_id
                          AND self_assignment.user_id = @user_id
                          AND self_assignment.effective_start_date <= CURRENT_DATE
                          AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date >= CURRENT_DATE)
                    ) AS direct_project_assignment,
                    EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests self_request
                        WHERE LOWER(COALESCE(self_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                          AND (
                              (document.project_id IS NOT NULL AND self_request.project_id = document.project_id)
                              OR (
                                  document.project_intake_request_id IS NOT NULL
                                  AND self_request.project_intake_request_id = document.project_intake_request_id
                              )
                          )
                          AND (
                              self_request.fulfilled_by_user_id = @user_id
                              OR EXISTS (
                                  SELECT 1
                                  FROM engineering_resource_request_assignments self_request_assignment
                                  WHERE self_request_assignment.engineering_resource_request_id = self_request.engineering_resource_request_id
                                    AND self_request_assignment.user_id = @user_id
                              )
                          )
                    ) AS direct_service_request_assignment,
                    EXISTS (
                        SELECT 1
                        FROM project_assignments team_assignment
                        WHERE team_assignment.project_id = document.project_id
                          AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                          AND team_assignment.effective_start_date <= CURRENT_DATE
                          AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date >= CURRENT_DATE)
                    ) AS team_project_assignment,
                    EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests team_request
                        WHERE LOWER(COALESCE(team_request.request_status, '')) NOT IN ('cancelled', 'canceled', 'closed', 'archived')
                          AND (
                              (document.project_id IS NOT NULL AND team_request.project_id = document.project_id)
                              OR (
                                  document.project_intake_request_id IS NOT NULL
                                  AND team_request.project_intake_request_id = document.project_intake_request_id
                              )
                          )
                          AND (
                              team_request.fulfilled_by_user_id IN (SELECT user_id FROM team_members)
                              OR EXISTS (
                                  SELECT 1
                                  FROM engineering_resource_request_assignments team_request_assignment
                                  WHERE team_request_assignment.engineering_resource_request_id = team_request.engineering_resource_request_id
                                    AND team_request_assignment.user_id IN (SELECT user_id FROM team_members)
                              )
                          )
                    ) AS team_service_request_assignment
                FROM project_intake_documents document
                WHERE document.project_intake_document_id = @document_id
            )
            SELECT
                document.original_file_name,
                document.storage_path,
                document.content_type
            FROM project_intake_documents document
            JOIN scoped_document scope
              ON scope.project_intake_document_id = document.project_intake_document_id
            LEFT JOIN projects project ON project.project_id = document.project_id
            LEFT JOIN project_intake_requests request
              ON request.project_intake_request_id = document.project_intake_request_id
            WHERE document.project_intake_document_id = @document_id
              AND document.is_active = TRUE
              AND COALESCE(document.upload_source, '') <> 'celar_ai_chat_attachment'
              AND (
                  @hide_closed_projects = FALSE
                  OR project.project_id IS NULL
                  OR LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
              )
              AND (
                  @is_broad_scope = TRUE
                  OR (
                      @can_view_managed_projects = TRUE
                      AND (project.project_manager_user_id = @user_id OR request.assigned_pm_user_id = @user_id)
                  )
                  OR scope.direct_project_assignment
                  OR scope.direct_service_request_assignment
                  OR (
                      @can_view_team_scope = TRUE
                      AND COALESCE(document.engineering_visible, FALSE) = TRUE
                      AND (scope.team_project_assignment OR scope.team_service_request_assignment)
                  )
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("document_id", documentId);
        AddScopeParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(httpContext.RequestAborted);
        if (!await reader.ReadAsync(httpContext.RequestAborted))
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "Project document was not found or is outside your role scope."
            });
        }

        var originalFileName = Path.GetFileName(reader.GetString(0));
        var storagePath = reader.GetString(1);
        var contentType = reader.IsDBNull(2) ? "application/octet-stream" : reader.GetString(2);
        await reader.DisposeAsync();

        await InsertViewAsAuditIfNeededAsync(
            connection,
            actualAccess,
            access,
            $"/api/project-workspace/documents/{documentId:D}/download",
            httpContext.RequestAborted);

        var resolvedStoragePath = ResolveProjectDocumentStoragePath(storagePath);
        if (resolvedStoragePath is null)
        {
            return Results.NotFound(new
            {
                status = "file_reconciliation_required",
                message = "Document metadata is available, but the stored file must be reconciled with the persistent upload volume before it can be downloaded."
            });
        }

        return Results.File(
            resolvedStoragePath,
            contentType,
            string.IsNullOrWhiteSpace(originalFileName) ? "project-document" : originalFileName,
            enableRangeProcessing: true);
    }

    internal static string? ResolveProjectDocumentStoragePath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        try
        {
            var root = Path.GetFullPath(ProjectTime.Api.Ai.ProjectPulseUploadStorage.ResolveRoot())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var relativePath = NormalizeStoredPath(storedPath, root);
            if (relativePath is null) return null;

            var candidate = Path.GetFullPath(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrChild(candidate, root) || candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!File.Exists(candidate) || HasReparsePoint(root, candidate)) return null;
            return candidate;
        }
        catch
        {
            return null;
        }
    }

    internal static string? NormalizeStoredPath(string? storedPath, string currentRoot)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var raw = storedPath.Trim();
        if (raw.IndexOf('\0') >= 0) return null;
        var normalized = raw.Replace('\\', '/');
        if (normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalizedRoot = Path.GetFullPath(currentRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var looksLikeWindowsAbsolute = normalized.Length >= 3
            && char.IsLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '/';

        if (Path.IsPathFullyQualified(raw) || looksLikeWindowsAbsolute)
        {
            if (!looksLikeWindowsAbsolute)
            {
                var absolute = Path.GetFullPath(raw);
                if (IsSameOrChild(absolute, normalizedRoot))
                {
                    normalized = Path.GetRelativePath(normalizedRoot, absolute).Replace('\\', '/');
                }
                else
                {
                    var marker = normalized.LastIndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
                    if (marker < 0) return null;
                    normalized = normalized[(marker + "/uploads/".Length)..];
                }
            }
            else
            {
                var marker = normalized.LastIndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
                if (marker < 0) return null;
                normalized = normalized[(marker + "/uploads/".Length)..];
            }
        }
        else
        {
            while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
            if (normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["uploads/".Length..];
            }
        }

        normalized = normalized.TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or "..")
            || segments.Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return null;
        }

        return string.Join('/', segments);
    }

    private static bool HasReparsePoint(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;
        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
        }

        return false;
    }

    private static bool IsSameOrChild(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddScopeParameters(NpgsqlCommand command, ProjectWorkspace019AccessContext access)
    {
        command.Parameters.AddWithValue("user_id", access.UserId);
        command.Parameters.AddWithValue("email", access.Email);
        command.Parameters.AddWithValue("team_name", access.TeamName ?? string.Empty);
        command.Parameters.AddWithValue("department_name", access.DepartmentName ?? string.Empty);
        command.Parameters.AddWithValue("is_broad_scope", access.IsBroadScope);
        command.Parameters.AddWithValue("can_view_managed_projects", access.CanViewManagedProjects);
        command.Parameters.AddWithValue("can_view_team_scope", access.CanViewTeamScope);
        command.Parameters.AddWithValue("hide_closed_projects", access.HideClosedProjects);
    }

    private static Guid? GetSessionUserId(HttpContext httpContext)
    {
        if (!httpContext.Items.TryGetValue("ProjectPulseSessionUserId", out var value)) return null;
        if (value is Guid userId) return userId;
        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
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

    private static IResult? ValidateConfig(ProjectWorkspace019DatabaseConfig config)
    {
        if (config.Missing.Count == 0) return null;
        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }
}

internal sealed record ProjectWorkspace019ViewAsUser(
    Guid UserId,
    string DisplayName,
    string Email,
    string JobTitle,
    string TeamOrDepartment,
    string RoleCodes,
    long AssignmentCount,
    long ManagedProjectCount);

internal sealed record ProjectWorkspace019AccessContext(
    Guid UserId,
    string Email,
    string TeamName,
    string DepartmentName,
    string Department,
    IReadOnlySet<string> RoleCodes)
{
    internal static ProjectWorkspace019AccessContext Empty(Guid userId) =>
        new(userId, string.Empty, string.Empty, string.Empty, string.Empty,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private bool HasRole(string roleCode) => RoleCodes.Contains(roleCode);

    internal bool IsAdministrator => HasRole("SUPER_ADMINISTRATOR") || HasRole("ADMINISTRATOR");
    private bool IsCoordinator => HasRole("PROJECT_TEAM_COORDINATOR");
    private bool IsExecutive => HasRole("EXECUTIVE");
    private bool IsManager => HasRole("MANAGER") || HasRole("ENGINEERING_MANAGER");
    private bool IsEngineeringLead => HasRole("ENGINEERING_LEAD") || HasRole("ENGINEERING_TEAM_LEAD");
    private bool IsProjectManagementLead => HasRole("PROJECT_MANAGEMENT_LEAD") || HasRole("PROJECT_MANAGEMENT_TEAM_LEAD") || HasRole("PM_TEAM_LEAD");
    private bool IsProjectManager => HasRole("PROJECT_MANAGEMENT") || HasRole("PROJECT_MANAGER");

    internal bool IsBroadScope => IsAdministrator || IsCoordinator || IsExecutive;
    internal bool CanViewManagedProjects => IsBroadScope || IsProjectManager || IsProjectManagementLead;
    internal bool CanViewTeamScope => IsBroadScope || IsManager || IsEngineeringLead || IsProjectManagementLead;
    internal bool HideClosedProjects => !IsBroadScope && !IsProjectManager && !IsProjectManagementLead;

    internal string ScopeLabel
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

internal sealed record ProjectWorkspace019Project(
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

internal sealed record ProjectWorkspace019Document(
    Guid Id,
    Guid? ProjectIntakeRequestId,
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

internal sealed record ProjectWorkspace019Assignment(
    Guid Id,
    string ProjectCode,
    string ProjectName,
    string TaskCode,
    string TaskName,
    string EngineerName,
    string EngineerEmail,
    string EngineerTeam,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    decimal AssignedHours,
    decimal UsedHours,
    decimal RemainingHours,
    bool IsOverAllocated,
    decimal? AllocationPercent);

internal sealed record ProjectWorkspace019ResourceRequest(
    string RequestNumber,
    string ProjectCode,
    string SourceName,
    string RequestedFunction,
    decimal RequestedHours,
    string Priority,
    string Status,
    string? AssignedEngineers,
    long AssignedEngineerCount);

internal sealed record ProjectWorkspace019DatabaseConfig(
    string? Host,
    string? Port,
    string? Database,
    string? Username,
    string? Password,
    IReadOnlyList<string> Missing)
{
    internal string ConnectionString
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

    internal static ProjectWorkspace019DatabaseConfig FromEnvironment()
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
        return new ProjectWorkspace019DatabaseConfig(host, port, database, username, password, missing);
    }
}
