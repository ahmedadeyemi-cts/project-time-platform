using Npgsql;

namespace ProjectTime.Api.Modules;

public static class ProjectIntakeModule
{
    public static WebApplication MapProjectIntakeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/project-intake/overview", GetOverviewAsync);
        app.MapPost("/api/project-intake/requests", CreateIntakeRequestAsync);
        app.MapPost("/api/project-intake/resource-requests", CreateResourceRequestAsync);
        app.MapPost("/api/project-intake/requests/{requestId:guid}/documents", UploadIntakeDocumentAsync).DisableAntiforgery();
        app.MapGet("/api/project-intake/documents/{documentId:guid}/download", DownloadIntakeDocumentAsync);
        app.MapPost("/api/project-intake/resource-requests/{requestId:guid}/assign", AssignResourceRequestAsync);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync()
    {
        var config = ProjectIntakeDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var intakes = await LoadIntakeRequestsAsync(connection);
        var projects = await LoadProjectsAsync(connection);
        var resourceRequests = await LoadResourceRequestsAsync(connection);
        var capacity = await LoadResourceCapacityAsync(connection);
        var projectManagers = await LoadUsersByRoleAsync(connection, "PROJECT_MANAGEMENT");
        var engineers = await LoadUsersByRoleAsync(connection, "ENGINEER");

        return Results.Ok(new
        {
            module = "019M-P Project Intake + Engineering Resource Request",
            mode = "workflow_foundation",
            summary = new
            {
                intakeCount = intakes.Count,
                openIntakeCount = intakes.Count(item => !new[] { "closed", "cancelled", "converted" }.Contains(item.Status, StringComparer.OrdinalIgnoreCase)),
                resourceRequestCount = resourceRequests.Count,
                openResourceRequestCount = resourceRequests.Count(item => !new[] { "assigned", "fulfilled", "cancelled" }.Contains(item.Status, StringComparer.OrdinalIgnoreCase)),
                activeProjectCount = projects.Count(item => item.Status.Equals("active", StringComparison.OrdinalIgnoreCase)),
                engineerCount = engineers.Count
            },
            intakes,
            projects,
            resourceRequests,
            capacity,
            projectManagers,
            engineers,
            guardrails = new[]
            {
                "Workflow is production-shaped; integrations are enabled only after approval.",
                "Salesforce sync is intentionally out of scope.",
                "Outlook calendar sync is intentionally out of scope.",
                "Resource assignment approval will be added before production enforcement."
            }
        });
    }

    private static async Task<IResult> CreateIntakeRequestAsync(ProjectIntakeCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientName) || string.IsNullOrWhiteSpace(request.RequestTitle))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Client name and request title are required."
            });
        }

        var config = ProjectIntakeDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var requestNumber = $"INTAKE-{DateTime.UtcNow:yyyyMMddHHmmss}";

        const string sql = """
            INSERT INTO project_intake_requests (
                request_number,
                client_name,
                opportunity_reference,
                request_title,
                request_description,
                assigned_pm_user_id,
                intake_status,
                priority,
                target_start_date,
                target_completion_date,
                estimated_hours,
                intake_source,
                source_system,
                external_reference_id,
                external_record_type,
                external_record_url,
                source_received_at,
                source_document_required,
                intake_source_notes
            )
            VALUES (
                @request_number,
                @client_name,
                @opportunity_reference,
                @request_title,
                @request_description,
                @assigned_pm_user_id,
                'new',
                @priority,
                @target_start_date,
                @target_completion_date,
                @estimated_hours,
                @intake_source,
                @source_system,
                @external_reference_id,
                @external_record_type,
                @external_record_url,
                NOW(),
                @source_document_required,
                @intake_source_notes
            )
            RETURNING project_intake_request_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("request_number", requestNumber);
        command.Parameters.AddWithValue("client_name", request.ClientName.Trim());
        command.Parameters.AddWithValue("opportunity_reference", string.IsNullOrWhiteSpace(request.OpportunityReference) ? DBNull.Value : request.OpportunityReference.Trim());
        command.Parameters.AddWithValue("request_title", request.RequestTitle.Trim());
        command.Parameters.AddWithValue("request_description", string.IsNullOrWhiteSpace(request.RequestDescription) ? DBNull.Value : request.RequestDescription.Trim());
        command.Parameters.AddWithValue("assigned_pm_user_id", request.AssignedPmUserId is null ? DBNull.Value : request.AssignedPmUserId);
        command.Parameters.AddWithValue("priority", string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim());
        command.Parameters.AddWithValue("target_start_date", request.TargetStartDate is null ? DBNull.Value : request.TargetStartDate);
        command.Parameters.AddWithValue("target_completion_date", request.TargetCompletionDate is null ? DBNull.Value : request.TargetCompletionDate);
        command.Parameters.AddWithValue("estimated_hours", request.EstimatedHours ?? 0);
        command.Parameters.AddWithValue("intake_source", string.IsNullOrWhiteSpace(request.IntakeSource) ? "manual_entry" : request.IntakeSource.Trim());
        command.Parameters.AddWithValue("source_system", string.IsNullOrWhiteSpace(request.SourceSystem) ? DBNull.Value : request.SourceSystem.Trim());
        command.Parameters.AddWithValue("external_reference_id", string.IsNullOrWhiteSpace(request.ExternalReferenceId) ? DBNull.Value : request.ExternalReferenceId.Trim());
        command.Parameters.AddWithValue("external_record_type", string.IsNullOrWhiteSpace(request.ExternalRecordType) ? DBNull.Value : request.ExternalRecordType.Trim());
        command.Parameters.AddWithValue("external_record_url", string.IsNullOrWhiteSpace(request.ExternalRecordUrl) ? DBNull.Value : request.ExternalRecordUrl.Trim());
        command.Parameters.AddWithValue("source_document_required", request.SourceDocumentRequired);
        command.Parameters.AddWithValue("intake_source_notes", string.IsNullOrWhiteSpace(request.IntakeSourceNotes) ? DBNull.Value : request.IntakeSourceNotes.Trim());

        var id = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create intake request."));

        await InsertAuditLogAsync(connection, "project_intake_request_created", "project_intake_request", id);

        return Results.Ok(new
        {
            status = "created",
            requestNumber,
            projectIntakeRequestId = id,
            message = "Project intake request created."
        });
    }


    private static async Task<IResult> UploadIntakeDocumentAsync(Guid requestId, HttpRequest request)
    {
        var config = ProjectIntakeDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Upload must be sent as multipart/form-data."
            });
        }

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");

        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "A non-empty file is required."
            });
        }

        var uploadRoot = Environment.GetEnvironmentVariable("PROJECTPULSE_UPLOAD_ROOT");
        if (string.IsNullOrWhiteSpace(uploadRoot))
        {
            uploadRoot = "/opt/project-time-platform/app/uploads";
        }

        var documentType = string.IsNullOrWhiteSpace(form["documentType"])
            ? "other"
            : form["documentType"].ToString().Trim().ToLowerInvariant();

        var documentCategory = documentType switch
        {
            "sow" => "sow",
            "gsd" => "gsd",
            "quote" => "quote",
            "proposal" => "proposal",
            "order_form" => "order_form",
            "architecture" => "architecture",
            _ => "other"
        };

        var engineeringVisible = !string.Equals(form["engineeringVisible"], "false", StringComparison.OrdinalIgnoreCase);
        var aiTimesheetContextEnabled =
            string.Equals(form["aiTimesheetContextEnabled"], "true", StringComparison.OrdinalIgnoreCase) ||
            documentCategory is "sow" or "gsd";

        var safeOriginalFileName = Path.GetFileName(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}";
        var requestFolder = Path.Combine(uploadRoot, "project-intake", requestId.ToString("N"));
        Directory.CreateDirectory(requestFolder);

        var storedPath = Path.Combine(requestFolder, storedFileName);

        await using (var stream = File.Create(storedPath))
        {
            await file.CopyToAsync(stream);
        }

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        const string insertSql = """
            INSERT INTO project_intake_documents (
                project_intake_request_id,
                document_type,
                document_category,
                original_file_name,
                stored_file_name,
                storage_path,
                content_type,
                size_bytes,
                upload_source,
                engineering_visible,
                ai_timesheet_context_enabled,
                extraction_status
            )
            VALUES (
                @project_intake_request_id,
                @document_type,
                @document_category,
                @original_file_name,
                @stored_file_name,
                @storage_path,
                @content_type,
                @size_bytes,
                'manual_upload',
                @engineering_visible,
                @ai_timesheet_context_enabled,
                'not_started'
            )
            RETURNING project_intake_document_id;
            """;

        await using var command = new NpgsqlCommand(insertSql, connection);
        command.Parameters.AddWithValue("project_intake_request_id", requestId);
        command.Parameters.AddWithValue("document_type", documentType);
        command.Parameters.AddWithValue("document_category", documentCategory);
        command.Parameters.AddWithValue("original_file_name", safeOriginalFileName);
        command.Parameters.AddWithValue("stored_file_name", storedFileName);
        command.Parameters.AddWithValue("storage_path", storedPath);
        command.Parameters.AddWithValue("content_type", string.IsNullOrWhiteSpace(file.ContentType) ? DBNull.Value : file.ContentType);
        command.Parameters.AddWithValue("size_bytes", file.Length);
        command.Parameters.AddWithValue("engineering_visible", engineeringVisible);
        command.Parameters.AddWithValue("ai_timesheet_context_enabled", aiTimesheetContextEnabled);

        var documentId = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to save intake document."));

        const string updateSql = """
            UPDATE project_intake_requests
            SET source_document_received = TRUE,
                updated_at = NOW()
            WHERE project_intake_request_id = @project_intake_request_id;
            """;

        await using var updateCommand = new NpgsqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("project_intake_request_id", requestId);
        await updateCommand.ExecuteNonQueryAsync();

        await InsertAuditLogAsync(connection, "project_intake_document_uploaded", "project_intake_request", requestId);

        return Results.Ok(new
        {
            status = "uploaded",
            projectIntakeRequestId = requestId,
            projectIntakeDocumentId = documentId,
            documentType,
            documentCategory,
            engineeringVisible,
            aiTimesheetContextEnabled,
            originalFileName = safeOriginalFileName,
            sizeBytes = file.Length
        });
    }

    private static async Task<IResult> DownloadIntakeDocumentAsync(Guid documentId)
    {
        var config = ProjectIntakeDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT original_file_name, storage_path, content_type
            FROM project_intake_documents
            WHERE project_intake_document_id = @document_id
              AND is_active = TRUE;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("document_id", documentId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "Project intake document was not found."
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

    private static async Task<IResult> CreateResourceRequestAsync(EngineeringResourceRequestCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedFunction) || request.RequestedHours <= 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Requested function and requested hours greater than zero are required."
            });
        }

        var config = ProjectIntakeDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var requestNumber = $"ERR-{DateTime.UtcNow:yyyyMMddHHmmss}";

        const string sql = """
            INSERT INTO engineering_resource_requests (
                request_number,
                project_intake_request_id,
                project_id,
                assigned_pm_user_id,
                requested_function,
                skill_requirements,
                requested_hours,
                target_start_date,
                target_end_date,
                priority,
                request_status,
                assignment_notes
            )
            VALUES (
                @request_number,
                @project_intake_request_id,
                @project_id,
                @assigned_pm_user_id,
                @requested_function,
                @skill_requirements,
                @requested_hours,
                @target_start_date,
                @target_end_date,
                @priority,
                'requested',
                @assignment_notes
            )
            RETURNING engineering_resource_request_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("request_number", requestNumber);
        command.Parameters.AddWithValue("project_intake_request_id", request.ProjectIntakeRequestId is null ? DBNull.Value : request.ProjectIntakeRequestId);
        command.Parameters.AddWithValue("project_id", request.ProjectId is null ? DBNull.Value : request.ProjectId);
        command.Parameters.AddWithValue("assigned_pm_user_id", request.AssignedPmUserId is null ? DBNull.Value : request.AssignedPmUserId);
        command.Parameters.AddWithValue("requested_function", request.RequestedFunction.Trim());
        command.Parameters.AddWithValue("skill_requirements", string.IsNullOrWhiteSpace(request.SkillRequirements) ? DBNull.Value : request.SkillRequirements.Trim());
        command.Parameters.AddWithValue("requested_hours", request.RequestedHours);
        command.Parameters.AddWithValue("target_start_date", request.TargetStartDate is null ? DBNull.Value : request.TargetStartDate);
        command.Parameters.AddWithValue("target_end_date", request.TargetEndDate is null ? DBNull.Value : request.TargetEndDate);
        command.Parameters.AddWithValue("priority", string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim());
        command.Parameters.AddWithValue("assignment_notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());

        var id = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create resource request."));

        await InsertAuditLogAsync(connection, "engineering_resource_request_created", "engineering_resource_request", id);

        return Results.Ok(new
        {
            status = "created",
            requestNumber,
            engineeringResourceRequestId = id,
            message = "Engineering resource request created."
        });
    }

    private static async Task<IResult> AssignResourceRequestAsync(Guid requestId, EngineeringResourceAssignmentRequest request)
    {
        var config = ProjectIntakeDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE engineering_resource_requests
            SET fulfilled_by_user_id = @fulfilled_by_user_id,
                request_status = 'assigned',
                assignment_notes = COALESCE(NULLIF(@assignment_notes, ''), assignment_notes),
                updated_at = NOW()
            WHERE engineering_resource_request_id = @request_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("fulfilled_by_user_id", request.UserId);
        command.Parameters.AddWithValue("assignment_notes", request.Notes ?? string.Empty);

        var affected = await command.ExecuteNonQueryAsync();
        if (affected == 0)
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "Engineering resource request not found."
            });
        }

        const string assignmentSql = """
            INSERT INTO engineering_resource_request_assignments (
                engineering_resource_request_id,
                user_id,
                assignment_status,
                allocated_hours,
                assignment_notes
            )
            SELECT
                @request_id,
                @user_id,
                'assigned',
                COALESCE(requested_hours, 0),
                NULLIF(@assignment_notes, '')
            FROM engineering_resource_requests
            WHERE engineering_resource_request_id = @request_id
            ON CONFLICT (engineering_resource_request_id, user_id) DO UPDATE
            SET assignment_status = 'assigned',
                assignment_notes = COALESCE(NULLIF(EXCLUDED.assignment_notes, ''), engineering_resource_request_assignments.assignment_notes),
                updated_at = NOW();
            """;

        await using var assignmentCommand = new NpgsqlCommand(assignmentSql, connection);
        assignmentCommand.Parameters.AddWithValue("request_id", requestId);
        assignmentCommand.Parameters.AddWithValue("user_id", request.UserId);
        assignmentCommand.Parameters.AddWithValue("assignment_notes", request.Notes ?? string.Empty);
        await assignmentCommand.ExecuteNonQueryAsync();

        await InsertAuditLogAsync(connection, "engineering_resource_request_assigned", "engineering_resource_request", requestId);

        return Results.Ok(new
        {
            status = "assigned",
            requestId,
            assignedUserId = request.UserId,
            maxEngineersPerRequest = 15
        });
    }

    private static async Task<List<IntakeSummary>> LoadIntakeRequestsAsync(NpgsqlConnection connection)
    {
        var rows = new List<IntakeSummary>();

        const string sql = """
            SELECT
                pir.project_intake_request_id AS id,
                pir.request_number AS request_number,
                pir.client_name AS client_name,
                pir.opportunity_reference AS opportunity_reference,
                pir.request_title AS request_title,
                pir.intake_status AS status,
                pir.priority AS priority,
                pir.target_start_date AS target_start_date,
                pir.target_completion_date AS target_completion_date,
                pir.estimated_hours AS estimated_hours,
                pm.display_name AS assigned_pm_name,
                pm.email AS assigned_pm_email,
                pir.created_at AS created_at,
                COALESCE(pir.intake_source, 'manual_entry') AS intake_source,
                pir.source_system AS source_system,
                pir.external_reference_id AS external_reference_id,
                pir.external_record_type AS external_record_type,
                pir.external_record_url AS external_record_url,
                COALESCE(pir.source_document_required, FALSE) AS source_document_required,
                COALESCE(pir.source_document_received, FALSE) AS source_document_received,
                COALESCE(docs.document_count, 0)::bigint AS document_count
            FROM project_intake_requests pir
            LEFT JOIN app_users pm ON pm.user_id = pir.assigned_pm_user_id
            LEFT JOIN (
                SELECT project_intake_request_id, COUNT(*)::bigint AS document_count
                FROM project_intake_documents
                WHERE is_active = TRUE
                GROUP BY project_intake_request_id
            ) docs ON docs.project_intake_request_id = pir.project_intake_request_id
            ORDER BY pir.created_at DESC
            LIMIT 50;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));

            rows.Add(new IntakeSummary(
                reader.GetGuid(O("id")),
                reader.GetString(O("request_number")),
                reader.GetString(O("client_name")),
                S("opportunity_reference"),
                reader.GetString(O("request_title")),
                reader.GetString(O("status")),
                reader.GetString(O("priority")),
                ReadDateOnlyOrNull(reader, O("target_start_date")),
                ReadDateOnlyOrNull(reader, O("target_completion_date")),
                reader.IsDBNull(O("estimated_hours")) ? null : reader.GetDecimal(O("estimated_hours")),
                S("assigned_pm_name"),
                S("assigned_pm_email"),
                ReadDateTimeOffset(reader, O("created_at")),
                reader.GetString(O("intake_source")),
                S("source_system"),
                S("external_reference_id"),
                S("external_record_type"),
                S("external_record_url"),
                !reader.IsDBNull(O("source_document_required")) && reader.GetBoolean(O("source_document_required")),
                !reader.IsDBNull(O("source_document_received")) && reader.GetBoolean(O("source_document_received")),
                reader.GetInt64(O("document_count"))));
        }

        return rows;
    }


    private static async Task<List<ProjectSummary>> LoadProjectsAsync(NpgsqlConnection connection)
    {
        var rows = new List<ProjectSummary>();

        const string sql = """
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
                COUNT(DISTINCT pt.task_id)::bigint AS task_count,
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS assignment_count
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            LEFT JOIN project_tasks pt ON pt.project_id = p.project_id AND pt.is_active = TRUE
            LEFT JOIN project_assignments pa ON pa.project_id = p.project_id
            GROUP BY p.project_id, p.project_code, p.project_name, c.client_name, p.status, p.start_date, p.end_date, p.billable, pm.display_name
            ORDER BY p.created_at DESC
            LIMIT 50;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));

            rows.Add(new ProjectSummary(
                reader.GetGuid(O("id")),
                reader.GetString(O("project_code")),
                reader.GetString(O("project_name")),
                reader.GetString(O("client_name")),
                reader.GetString(O("status")),
                ReadDateOnlyOrNull(reader, O("start_date")),
                ReadDateOnlyOrNull(reader, O("end_date")),
                reader.GetBoolean(O("billable")),
                S("project_manager_name"),
                reader.GetInt64(O("task_count")),
                reader.GetInt64(O("assignment_count"))));
        }

        return rows;
    }


    private static async Task<List<ResourceRequestSummary>> LoadResourceRequestsAsync(NpgsqlConnection connection)
    {
        var rows = new List<ResourceRequestSummary>();

        const string sql = """
            SELECT
                err.engineering_resource_request_id AS id,
                err.request_number AS request_number,
                COALESCE(p.project_name, pir.request_title, 'Unlinked request') AS source_name,
                err.requested_function AS requested_function,
                err.skill_requirements AS skill_requirements,
                err.requested_hours AS requested_hours,
                err.target_start_date AS target_start_date,
                err.target_end_date AS target_end_date,
                err.priority AS priority,
                err.request_status AS status,
                pm.display_name AS assigned_pm_name,
                COALESCE(assigned.assigned_engineers, primary_engineer.display_name) AS assigned_engineers,
                err.assignment_notes AS assignment_notes,
                err.created_at AS created_at,
                COALESCE(
                    assigned.assigned_engineer_count,
                    CASE WHEN err.fulfilled_by_user_id IS NULL THEN 0::bigint ELSE 1::bigint END
                )::bigint AS assigned_engineer_count,
                COALESCE(
                    assigned.allocated_hours,
                    CASE WHEN err.fulfilled_by_user_id IS NULL THEN 0::numeric ELSE err.requested_hours END
                )::numeric AS allocated_hours,
                COALESCE(
                    assigned.allocation_percent,
                    CASE WHEN err.fulfilled_by_user_id IS NULL THEN 0::numeric ELSE 100::numeric END
                )::numeric AS allocation_percent
            FROM engineering_resource_requests err
            LEFT JOIN projects p
                ON p.project_id = err.project_id
            LEFT JOIN project_intake_requests pir
                ON pir.project_intake_request_id = err.project_intake_request_id
            LEFT JOIN app_users pm
                ON pm.user_id = err.assigned_pm_user_id
            LEFT JOIN app_users primary_engineer
                ON primary_engineer.user_id = err.fulfilled_by_user_id
            LEFT JOIN (
                SELECT
                    erra.engineering_resource_request_id,
                    STRING_AGG(u.display_name, ', ' ORDER BY u.display_name) AS assigned_engineers,
                    COUNT(*)::bigint AS assigned_engineer_count,
                    COALESCE(SUM(erra.allocated_hours), 0::numeric)::numeric AS allocated_hours,
                    COALESCE(SUM(COALESCE(erra.allocation_percent, 0::numeric)), 0::numeric)::numeric AS allocation_percent
                FROM engineering_resource_request_assignments erra
                JOIN app_users u
                    ON u.user_id = erra.user_id
                GROUP BY erra.engineering_resource_request_id
            ) assigned
                ON assigned.engineering_resource_request_id = err.engineering_resource_request_id
            ORDER BY err.created_at DESC
            LIMIT 50;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);
            string? S(string name) => reader.IsDBNull(O(name)) ? null : reader.GetString(O(name));

            rows.Add(new ResourceRequestSummary(
                reader.GetGuid(O("id")),
                reader.GetString(O("request_number")),
                reader.GetString(O("source_name")),
                reader.GetString(O("requested_function")),
                S("skill_requirements"),
                reader.GetDecimal(O("requested_hours")),
                ReadDateOnlyOrNull(reader, O("target_start_date")),
                ReadDateOnlyOrNull(reader, O("target_end_date")),
                reader.GetString(O("priority")),
                reader.GetString(O("status")),
                S("assigned_pm_name"),
                S("assigned_engineers"),
                S("assignment_notes"),
                ReadDateTimeOffset(reader, O("created_at")),
                reader.GetInt64(O("assigned_engineer_count")),
                reader.GetDecimal(O("allocated_hours")),
                reader.GetDecimal(O("allocation_percent"))));
        }

        return rows;
    }


    private static async Task<List<ResourceCapacitySummary>> LoadResourceCapacityAsync(NpgsqlConnection connection)
    {
        var rows = new List<ResourceCapacitySummary>();

        const string sql = """
            SELECT
                u.user_id AS user_id,
                u.display_name AS display_name,
                u.email AS email,
                COALESCE(rp.primary_function, u.team_name, u.department_name, u.department, 'Unassigned') AS primary_function,
                COALESCE(rcp.week_start_date, DATE '2026-07-13') AS week_start_date,
                COALESCE(rcp.available_hours, 40.00)::numeric AS available_hours,
                COALESCE(rcp.assigned_hours, 0.00)::numeric AS assigned_hours,
                COALESCE(rcp.planned_utilization_percent, 0.00)::numeric AS planned_utilization_percent,
                COALESCE(rcp.capacity_status, 'available') AS capacity_status,
                COALESCE(string_agg(DISTINCT rq.qualification_name, ', '), 'No qualifications recorded') AS qualifications
            FROM app_users u
            INNER JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            INNER JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.role_code = 'ENGINEER'
            LEFT JOIN resource_profiles rp
                ON rp.user_id = u.user_id
            LEFT JOIN resource_capacity_plans rcp
                ON rcp.user_id = u.user_id
               AND rcp.week_start_date = DATE '2026-07-13'
            LEFT JOIN resource_qualifications rq
                ON rq.user_id = u.user_id
            WHERE u.is_active = TRUE
              AND u.login_enabled = TRUE
            GROUP BY
                u.user_id,
                u.display_name,
                u.email,
                rp.primary_function,
                u.team_name,
                u.department_name,
                u.department,
                rcp.week_start_date,
                rcp.available_hours,
                rcp.assigned_hours,
                rcp.planned_utilization_percent,
                rcp.capacity_status
            ORDER BY u.display_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int O(string name) => reader.GetOrdinal(name);

            rows.Add(new ResourceCapacitySummary(
                reader.GetGuid(O("user_id")),
                reader.GetString(O("display_name")),
                reader.GetString(O("email")),
                reader.GetString(O("primary_function")),
                ReadDateOnly(reader, O("week_start_date")),
                reader.GetDecimal(O("available_hours")),
                reader.GetDecimal(O("assigned_hours")),
                reader.GetDecimal(O("planned_utilization_percent")),
                reader.GetString(O("capacity_status")),
                reader.GetString(O("qualifications"))));
        }

        return rows;
    }

    private static async Task<List<UserOption>> LoadUsersByRoleAsync(NpgsqlConnection connection, string roleCode)
    {
        var rows = new List<UserOption>();

        const string sql = """
            SELECT DISTINCT u.user_id, u.display_name, u.email, COALESCE(u.job_title, '')
            FROM app_users u
            INNER JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
            INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE r.role_code = @role_code
              AND u.is_active = TRUE
              AND u.login_enabled = TRUE
            ORDER BY u.display_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("role_code", roleCode);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new UserOption(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    private static async Task InsertAuditLogAsync(NpgsqlConnection connection, string action, string entityType, Guid entityId)
    {
        const string sql = """
            INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
            VALUES (NULL, @action, @entity_type, @entity_id);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);

        await command.ExecuteNonQueryAsync();
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

    private static IResult? ValidateConfig(ProjectIntakeDatabaseConfig config)
    {
        if (config.Missing.Count == 0) return null;

        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }
}

internal sealed record ProjectIntakeCreateRequest(
    string ClientName,
    string? OpportunityReference,
    string RequestTitle,
    string? RequestDescription,
    Guid? AssignedPmUserId,
    string? Priority,
    DateOnly? TargetStartDate,
    DateOnly? TargetCompletionDate,
    decimal? EstimatedHours,
    string? IntakeSource,
    string? SourceSystem,
    string? ExternalReferenceId,
    string? ExternalRecordType,
    string? ExternalRecordUrl,
    bool SourceDocumentRequired,
    string? IntakeSourceNotes);

internal sealed record EngineeringResourceRequestCreateRequest(
    Guid? ProjectIntakeRequestId,
    Guid? ProjectId,
    Guid? AssignedPmUserId,
    string RequestedFunction,
    string? SkillRequirements,
    decimal RequestedHours,
    DateOnly? TargetStartDate,
    DateOnly? TargetEndDate,
    string? Priority,
    string? Notes);

internal sealed record EngineeringResourceAssignmentRequest(Guid UserId, string? Notes);

internal sealed record IntakeSummary(
    Guid Id,
    string RequestNumber,
    string ClientName,
    string? OpportunityReference,
    string RequestTitle,
    string Status,
    string Priority,
    DateOnly? TargetStartDate,
    DateOnly? TargetCompletionDate,
    decimal? EstimatedHours,
    string? AssignedPmName,
    string? AssignedPmEmail,
    DateTimeOffset CreatedAt,
    string IntakeSource,
    string? SourceSystem,
    string? ExternalReferenceId,
    string? ExternalRecordType,
    string? ExternalRecordUrl,
    bool SourceDocumentRequired,
    bool SourceDocumentReceived,
    long DocumentCount);

internal sealed record ProjectSummary(
    Guid Id,
    string ProjectCode,
    string ProjectName,
    string ClientName,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool Billable,
    string? ProjectManagerName,
    long TaskCount,
    long AssignmentCount);

internal sealed record ResourceRequestSummary(
    Guid Id,
    string RequestNumber,
    string SourceName,
    string RequestedFunction,
    string? SkillRequirements,
    decimal RequestedHours,
    DateOnly? TargetStartDate,
    DateOnly? TargetEndDate,
    string Priority,
    string Status,
    string? AssignedPmName,
    string? FulfilledByName,
    string? AssignmentNotes,
    DateTimeOffset CreatedAt,
    long AssignedEngineerCount,
    decimal AllocatedHours,
    decimal AllocationPercent);

internal sealed record ResourceCapacitySummary(
    Guid UserId,
    string DisplayName,
    string Email,
    string PrimaryFunction,
    DateOnly WeekStartDate,
    decimal AvailableHours,
    decimal AssignedHours,
    decimal PlannedUtilizationPercent,
    string CapacityStatus,
    string Qualifications);

internal sealed record UserOption(Guid UserId, string DisplayName, string Email, string JobTitle);

internal sealed record ProjectIntakeDatabaseConfig(
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

    public static ProjectIntakeDatabaseConfig FromEnvironment()
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

        return new ProjectIntakeDatabaseConfig(host, port, database, username, password, missing);
    }
}
