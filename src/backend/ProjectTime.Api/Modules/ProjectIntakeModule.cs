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
        app.MapPost("/api/project-intake/requests/{requestId:guid}/signed-handoff", SubmitSignedHandoffAsync);
        app.MapPost("/api/project-intake/resource-requests/{requestId:guid}/assign", AssignResourceRequestAsync);

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var authorized = await ProjectNotificationRepository.OpenAuthorizedAsync(context, CanViewIntake);
        if (authorized.Failure is not null) return authorized.Failure;
        await using var connection = authorized.Connection!;
        var actor = authorized.Actor!;

        var intakesLoad = await LoadSourceAsync("project_intake_requests", () => LoadIntakeRequestsAsync(connection), new List<IntakeSummary>());
        var projectsLoad = await LoadSourceAsync("projects", () => LoadProjectsAsync(connection), new List<ProjectSummary>());
        var resourceLoad = await LoadSourceAsync("engineering_resource_requests", () => LoadResourceRequestsAsync(connection), new List<ResourceRequestSummary>());
        var capacityLoad = await LoadSourceAsync("engineering_capacity", () => LoadResourceCapacityAsync(connection), new List<ResourceCapacitySummary>());
        var managerLoad = await LoadSourceAsync("project_management_directory", () => LoadUsersByRoleAsync(connection, "PROJECT_MANAGEMENT"), new List<UserOption>());
        var engineerLoad = await LoadSourceAsync("engineering_directory", () => LoadUsersByRoleAsync(connection, "ENGINEERING"), new List<UserOption>());
        var accountExecutiveLoad = await LoadSourceAsync(
            "account_executive_directory",
            () => LoadUsersByRoleOrProfileAsync(
                connection,
                new[] { "SALES" },
                new[] { "account executive", "sales", "account manager" }),
            new List<UserOption>());
        var solutionArchitectLoad = await LoadSourceAsync(
            "solution_architect_directory",
            () => LoadUsersByRoleOrProfileAsync(
                connection,
                new[] { "SOLUTION_ARCHITECT" },
                new[] { "solution architect", "architect" }),
            new List<UserOption>());

        var loads = new ISourceLoad[]
        {
            intakesLoad, projectsLoad, resourceLoad, capacityLoad,
            managerLoad, engineerLoad, accountExecutiveLoad, solutionArchitectLoad
        };
        var degraded = loads.Where(load => !load.Succeeded).ToArray();
        if (degraded.Length > 0)
        {
            try
            {
                await AdminExperienceCommon.WriteAuditAsync(
                    connection,
                    null,
                    "system",
                    "degraded",
                    "project_intake_source_degraded",
                    actor.ActualUserId,
                    actor.Email,
                    "module",
                    "055D",
                    "Project Intake",
                    "055D",
                    "project_intake_overview",
                    string.Empty,
                    $"Project Intake loaded with {degraded.Length} degraded source or sources.",
                    new
                    {
                        sources = degraded.Select(load => new { load.Source, load.DiagnosticCode }).ToArray(),
                        redaction = "No customer document or request payload retained."
                    },
                    AdminExperienceCommon.ClientIp(context),
                    context.TraceIdentifier,
                    context.RequestAborted);
            }
            catch
            {
                // The overview remains available even when audit persistence is degraded.
            }
        }

        var intakes = intakesLoad.Value;
        var projects = projectsLoad.Value;
        var resourceRequests = resourceLoad.Value;
        var capacity = capacityLoad.Value;
        var projectManagers = managerLoad.Value;
        var engineers = engineerLoad.Value;
        var accountExecutives = accountExecutiveLoad.Value;
        var solutionArchitects = solutionArchitectLoad.Value;

        return Results.Ok(new
        {
            status = degraded.Length == 0
                ? "project_intake_overview_loaded"
                : "project_intake_overview_partial",
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
            accountExecutives,
            solutionArchitects,
            sources = loads.Select(load => new
            {
                source = load.Source,
                status = load.Succeeded ? "healthy" : "degraded",
                diagnosticCode = load.DiagnosticCode
            }).ToArray(),
            warnings = degraded.Select(load => $"{load.Source} is temporarily unavailable ({load.DiagnosticCode}).").ToArray(),
            guardrails = new[]
            {
                "Workflow is production-shaped; integrations are enabled only after approval.",
                "Salesforce sync is intentionally out of scope.",
                "Outlook calendar sync is intentionally out of scope.",
                "Resource assignment approval will be added before production enforcement."
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> CreateIntakeRequestAsync(ProjectIntakeCreateRequest request, HttpContext context)
    {
        if ((request.ClientId is null && string.IsNullOrWhiteSpace(request.ClientName)) || string.IsNullOrWhiteSpace(request.RequestTitle))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Customer and request title are required."
            });
        }

        var authorized = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanManageIntake(actor));
        if (authorized.Failure is not null) return authorized.Failure;
        await using var connection = authorized.Connection!;
        var actor = authorized.Actor!;

        var resolvedClientName = request.ClientName?.Trim() ?? string.Empty;

        if (request.ClientId is not null)
        {
            await using var clientCommand = new NpgsqlCommand("""
                SELECT client_name
                FROM clients
                WHERE client_id = @client_id
                  AND is_active = TRUE;
                """, connection);

            clientCommand.Parameters.AddWithValue("client_id", request.ClientId.Value);

            var clientNameResult = await clientCommand.ExecuteScalarAsync();

            if (clientNameResult is null)
            {
                return Results.BadRequest(new
                {
                    status = "validation_failed",
                    message = "Selected customer was not found or is inactive."
                });
            }

            resolvedClientName = Convert.ToString(clientNameResult) ?? resolvedClientName;
        }

        var plannedEngineeringCost = request.PlannedEngineeringCost ?? 0;
        var plannedPmCost = request.PlannedPmCost ?? 0;
        var plannedTotalProjectCost = request.PlannedTotalProjectCost ?? plannedEngineeringCost + plannedPmCost;

        var requestNumber = $"INTAKE-{DateTime.UtcNow:yyyyMMddHHmmss}";

        const string sql = """
            INSERT INTO project_intake_requests (
                request_number,
                client_id,
                client_name,
                opportunity_reference,
                request_title,
                request_description,
                assigned_pm_user_id,
                /* 053I_INTAKE_AE_SA_INSERT_COLUMNS_START */
                account_executive_user_id,
                solution_architect_user_id,
                /* 053I_INTAKE_AE_SA_INSERT_COLUMNS_END */
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
                intake_source_notes,
                planned_engineering_cost,
                planned_pm_cost,
                planned_total_project_cost
            )
            VALUES (
                @request_number,
                @client_id,
                @client_name,
                @opportunity_reference,
                @request_title,
                @request_description,
                @assigned_pm_user_id,
                /* 053I_INTAKE_AE_SA_INSERT_VALUES_START */
                @account_executive_user_id,
                @solution_architect_user_id,
                /* 053I_INTAKE_AE_SA_INSERT_VALUES_END */
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
                @intake_source_notes,
                @planned_engineering_cost,
                @planned_pm_cost,
                @planned_total_project_cost
            )
            RETURNING project_intake_request_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("request_number", requestNumber);
        command.Parameters.AddWithValue("client_id", request.ClientId is null ? DBNull.Value : request.ClientId.Value);
        command.Parameters.AddWithValue("client_name", resolvedClientName);
        command.Parameters.AddWithValue("opportunity_reference", string.IsNullOrWhiteSpace(request.OpportunityReference) ? DBNull.Value : request.OpportunityReference.Trim());
        command.Parameters.AddWithValue("request_title", request.RequestTitle.Trim());
        command.Parameters.AddWithValue("request_description", string.IsNullOrWhiteSpace(request.RequestDescription) ? DBNull.Value : request.RequestDescription.Trim());
        command.Parameters.AddWithValue("assigned_pm_user_id", request.AssignedPmUserId is null ? DBNull.Value : request.AssignedPmUserId);
        /* 053I_INTAKE_AE_SA_PARAMETERS_START */
        command.Parameters.AddWithValue("account_executive_user_id", request.AccountExecutiveUserId is null ? DBNull.Value : request.AccountExecutiveUserId.Value);
        command.Parameters.AddWithValue("solution_architect_user_id", request.SolutionArchitectUserId is null ? DBNull.Value : request.SolutionArchitectUserId.Value);
        /* 053I_INTAKE_AE_SA_PARAMETERS_END */
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
        command.Parameters.AddWithValue("planned_engineering_cost", plannedEngineeringCost);
        command.Parameters.AddWithValue("planned_pm_cost", plannedPmCost);
        command.Parameters.AddWithValue("planned_total_project_cost", plannedTotalProjectCost);

        var id = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create intake request."));

        await InsertAuditLogAsync(connection, "project_intake_request_created", "project_intake_request", id, actor.ActualUserId);

        return Results.Ok(new
        {
            status = "created",
            requestNumber,
            projectIntakeRequestId = id,
            message = "Project intake request created."
        });
    }


    private static async Task<IResult> UploadIntakeDocumentAsync(Guid requestId, HttpContext context)
    {
        var authorized = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanManageIntake(actor));
        if (authorized.Failure is not null) return authorized.Failure;
        await using var connection = authorized.Connection!;
        var actor = authorized.Actor!;
        var request = context.Request;

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

        var pipeline = ProjectTime.Api.Ai.PulseAiDocumentPipelineOptions.FromEnvironment();
        var safeOriginalFileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(safeOriginalFileName).ToLowerInvariant();
        if (file.Length > pipeline.MaximumFileBytes)
        {
            return Results.BadRequest(new
            {
                status = "file_too_large",
                message = $"Project intake documents are limited to {pipeline.MaximumFileBytes / (1024 * 1024)} MB."
            });
        }
        if (!ProjectTime.Api.Ai.PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                status = "unsupported_file_type",
                message = "Upload an approved PDF, Office Open XML, text, CSV, JSON, XML, or HTML document."
            });
        }

        await using (var intakeCommand = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM project_intake_requests WHERE project_intake_request_id = @request_id);", connection))
        {
            intakeCommand.Parameters.AddWithValue("request_id", requestId);
            if ((bool?)await intakeCommand.ExecuteScalarAsync(context.RequestAborted) != true)
            {
                return Results.NotFound(new
                {
                    module = "024/027",
                    status = "intake_not_found",
                    message = "The selected intake no longer exists. Refresh and select an active intake before uploading."
                });
            }
        }

        var uploadRoot = ProjectTime.Api.Ai.ProjectPulseUploadStorage.ResolveRoot();

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
            "purchase_order" => "purchase_order",
            "architecture" => "architecture",
            _ => "other"
        };

        var engineeringVisible = !string.Equals(form["engineeringVisible"], "false", StringComparison.OrdinalIgnoreCase);
        var aiTimesheetContextEnabled =
            string.Equals(form["aiTimesheetContextEnabled"], "true", StringComparison.OrdinalIgnoreCase) ||
            documentCategory is "sow" or "gsd";

        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var requestFolder = Path.Combine(uploadRoot, "project-intake", requestId.ToString("N"));
        Directory.CreateDirectory(requestFolder);

        var storedPath = Path.Combine(requestFolder, storedFileName);

        await using (var stream = File.Create(storedPath))
        {
            await file.CopyToAsync(stream);
        }

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

        await InsertAuditLogAsync(connection, "project_intake_document_uploaded", "project_intake_request", requestId, actor.ActualUserId);

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

    private static async Task<IResult> SubmitSignedHandoffAsync(Guid requestId, HttpContext context)
    {
        var authorized = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanSubmitSignedHandoff(actor));
        if (authorized.Failure is not null) return authorized.Failure;

        await using var connection = authorized.Connection!;
        var actor = authorized.Actor!;
        var cancellationToken = context.RequestAborted;

        string requestNumber;
        string customerName;
        string requestTitle;
        string description;
        await using (var command = new NpgsqlCommand("""
            SELECT
                request_number,
                client_name,
                request_title,
                COALESCE(request_description, '')
            FROM project_intake_requests
            WHERE project_intake_request_id = @request_id;
            """, connection))
        {
            command.Parameters.AddWithValue("request_id", requestId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.NotFound(new
                {
                    module = "027",
                    status = "intake_not_found",
                    message = "The signed handoff intake was not found."
                });
            }

            requestNumber = reader.GetString(0);
            customerName = reader.GetString(1);
            requestTitle = reader.GetString(2);
            description = reader.GetString(3);
        }

        var documents = new List<SignedHandoffDocument>();
        await using (var command = new NpgsqlCommand("""
            SELECT
                project_intake_document_id,
                document_category,
                original_file_name,
                size_bytes
            FROM project_intake_documents
            WHERE project_intake_request_id = @request_id
              AND is_active = TRUE
              AND COALESCE(upload_source, '') <> 'celar_ai_chat_attachment'
            ORDER BY uploaded_at, original_file_name;
            """, connection))
        {
            command.Parameters.AddWithValue("request_id", requestId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3)));
            }
        }

        if (documents.Count == 0 || !documents.Any(document => document.Category == "sow"))
        {
            return Results.BadRequest(new
            {
                module = "027",
                status = "signed_sow_required",
                message = "Upload and classify the signed SOW before routing this package to the Project Team Coordinator."
            });
        }

        var recipients = await ProjectNotificationRepository.LoadUsersInRolesAsync(
            connection,
            ["PROJECT_TEAM_COORDINATOR"],
            "project_team_coordinator",
            cancellationToken);
        recipients = recipients
            .Where(recipient => !string.IsNullOrWhiteSpace(recipient.Email))
            .GroupBy(recipient => recipient.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First() with { RecipientType = "to" })
            .ToArray();
        if (recipients.Length == 0)
        {
            return Results.Json(new
            {
                module = "027",
                status = "ptc_recipient_missing",
                message = "The package is retained, but no active Project Team Coordinator has an email address. Assign an active PTC role and retry the handoff."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var publicOrigin = ProjectPulsePublicOriginCompatibility.TryResolveProxyOrConfiguredOrigin(
            context,
            out var resolvedOrigin,
            out _)
                ? resolvedOrigin.GetLeftPart(UriPartial.Authority)
                : $"{context.Request.Scheme}://{context.Request.Host}";
        var platformUrl = $"{publicOrigin}{context.Request.PathBase}/#project-intake";
        var documentLines = string.Join("\n", documents.Select(document =>
            $"- {document.FileName} ({document.Category.Replace('_', ' ')}, {Math.Ceiling(document.SizeBytes / 1024m):N0} KB): {publicOrigin}{context.Request.PathBase}/api/project-workspace/documents/{document.Id:D}/download"));
        var subject = $"Signed customer package ready — {customerName} / {requestTitle}";
        var textBody = $"""
            A signed customer package is ready for Project Team Coordinator review.

            Intake: {requestNumber}
            Customer: {customerName}
            Package: {requestTitle}
            Submitted by: {actor.DisplayName} ({actor.Email})

            Documents retained in Pulse:
            {documentLines}

            Sales context:
            {description}

            Review the package in Pulse: {platformUrl}

            Each document link requires ProjectPulse authentication, enforces the PTC/project access scope, and records governed download evidence. Raw customer files are not copied into automated email.
            """;
        var documentHtml = string.Join(string.Empty, documents.Select(document =>
        {
            var url = $"{publicOrigin}{context.Request.PathBase}/api/project-workspace/documents/{document.Id:D}/download";
            return $"<li><a href=\"{System.Net.WebUtility.HtmlEncode(url)}\">{System.Net.WebUtility.HtmlEncode(document.FileName)}</a> "
                + $"<span>({System.Net.WebUtility.HtmlEncode(document.Category.Replace('_', ' '))}, {Math.Ceiling(document.SizeBytes / 1024m):N0} KB)</span></li>";
        }));
        var htmlBody = $"""
            <div style="font-family:Arial,sans-serif;line-height:1.5;color:#172033">
              <h2 style="margin:0 0 12px">Signed customer package ready</h2>
              <p><strong>Intake:</strong> {System.Net.WebUtility.HtmlEncode(requestNumber)}<br />
              <strong>Customer:</strong> {System.Net.WebUtility.HtmlEncode(customerName)}<br />
              <strong>Package:</strong> {System.Net.WebUtility.HtmlEncode(requestTitle)}<br />
              <strong>Submitted by:</strong> {System.Net.WebUtility.HtmlEncode(actor.DisplayName)} ({System.Net.WebUtility.HtmlEncode(actor.Email)})</p>
              <h3>Secure documents</h3>
              <ul>{documentHtml}</ul>
              <p><a href="{System.Net.WebUtility.HtmlEncode(platformUrl)}">Open the intake workspace in ProjectPulse</a></p>
              <p style="font-size:12px;color:#526173">Links require ProjectPulse authentication, enforce the PTC/project access scope, and retain download evidence. Raw customer files are not copied into automated email.</p>
            </div>
            """;
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);

        Guid dispatchId;
        try
        {
            dispatchId = await ProjectNotificationRepository.UpsertDispatchAsync(
                connection,
                null,
                null,
                null,
                $"signed-handoff:{requestId:D}",
                "signed_customer_handoff",
                "informational",
                "027",
                "signed_handoff_ready",
                subject,
                textBody,
                htmlBody,
                readiness.RecipientBoundary,
                "queued",
                recipients,
                new
                {
                    projectIntakeRequestId = requestId,
                    requestNumber,
                    submittedByUserId = actor.ActualUserId,
                    submittedByEmail = actor.Email,
                    documentCount = documents.Count,
                    documentIds = documents.Select(document => document.Id).ToArray(),
                    documentDelivery = "secure_projectpulse_reference",
                    serverDerivedRecipients = true
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "027",
                "signed_handoff_notification_dispatch",
                exception,
                "The package was retained, but its PTC notification could not be queued.");
        }

        var delivery = await ProjectNotificationProcessingService.DeliverDispatchAsync(
            connection,
            dispatchId,
            actor.ActualUserId,
            "Module 027 signed customer package submitted.",
            context,
            cancellationToken);

        await using (var update = new NpgsqlCommand("""
            UPDATE project_intake_requests
            SET intake_status = CASE
                    WHEN intake_status IN ('closed', 'cancelled', 'converted') THEN intake_status
                    ELSE 'ptc_review'
                END,
                intake_source_notes = CONCAT_WS(E'\n', NULLIF(intake_source_notes, ''), @handoff_note),
                updated_at = NOW()
            WHERE project_intake_request_id = @request_id;
            """, connection))
        {
            update.Parameters.AddWithValue("request_id", requestId);
            update.Parameters.AddWithValue(
                "handoff_note",
                $"Module 027 dispatch {dispatchId:D}: {delivery.Status} via {delivery.Provider}.");
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditLogAsync(connection, "signed_sow_handoff_submitted", "project_intake_request", requestId, actor.ActualUserId);

        return Results.Json(new
        {
            module = "027",
            status = delivery.Status,
            sent = delivery.Sent,
            message = delivery.Sent
                ? $"The signed package was retained and Module 065 sent the PTC notification to {recipients.Length} recipient(s)."
                : $"The signed package was retained and its PTC notification is {delivery.Status}. {delivery.Message}",
            dispatchId,
            recipientCount = recipients.Length,
            documentCount = documents.Count,
            provider = delivery.Provider,
            recipientBoundary = delivery.RecipientBoundary,
            auditPath = $"/api/project-notifications/dispatches?dispatchId={dispatchId:D}",
            documentsIncludedAs = "secure_projectpulse_references"
        }, statusCode: delivery.Sent ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> CreateResourceRequestAsync(
        EngineeringResourceRequestCreateRequest request,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedFunction) || request.RequestedHours <= 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Requested function and requested hours greater than zero are required."
            });
        }

        var authorized = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanManageIntake(actor));
        if (authorized.Failure is not null) return authorized.Failure;
        await using var connection = authorized.Connection!;
        var actor = authorized.Actor!;

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

        await InsertAuditLogAsync(connection, "engineering_resource_request_created", "engineering_resource_request", id, actor.ActualUserId);

        return Results.Ok(new
        {
            status = "created",
            requestNumber,
            engineeringResourceRequestId = id,
            message = "Engineering resource request created."
        });
    }

    private static async Task<IResult> AssignResourceRequestAsync(
        Guid requestId,
        EngineeringResourceAssignmentRequest request,
        HttpContext context)
    {
        var authorized = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanManageIntake(actor));
        if (authorized.Failure is not null) return authorized.Failure;
        await using var connection = authorized.Connection!;
        var actor = authorized.Actor!;

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

        await InsertAuditLogAsync(connection, "engineering_resource_request_assigned", "engineering_resource_request", requestId, actor.ActualUserId);

        return Results.Ok(new
        {
            status = "assigned",
            requestId,
            assignedUserId = request.UserId,
            maxEngineersPerRequest = 15
        });
    }

    private interface ISourceLoad
    {
        string Source { get; }
        bool Succeeded { get; }
        string DiagnosticCode { get; }
    }

    private sealed record SourceLoad<T>(
        string Source,
        bool Succeeded,
        string DiagnosticCode,
        T Value) : ISourceLoad;

    private static async Task<SourceLoad<T>> LoadSourceAsync<T>(
        string source,
        Func<Task<T>> loader,
        T fallback)
    {
        try
        {
            return new SourceLoad<T>(source, true, string.Empty, await loader());
        }
        catch (PostgresException exception)
        {
            return new SourceLoad<T>(source, false, $"postgres_{exception.SqlState}", fallback);
        }
        catch (Exception exception)
        {
            return new SourceLoad<T>(source, false, exception.GetType().Name.ToLowerInvariant(), fallback);
        }
    }

    private static async Task<List<IntakeSummary>> LoadIntakeRequestsAsync(NpgsqlConnection connection)
    {
        var rows = new List<IntakeSummary>();

        const string sql = """
            SELECT
                pir.project_intake_request_id AS id,
                pir.client_id AS client_id,
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
                /* 053I_INTAKE_AE_SA_SELECT_FIELDS_START */
                ae.display_name AS account_executive_name,
                ae.email AS account_executive_email,
                sa.display_name AS solution_architect_name,
                sa.email AS solution_architect_email,
                /* 053I_INTAKE_AE_SA_SELECT_FIELDS_END */
                pir.created_at AS created_at,
                COALESCE(pir.intake_source, 'manual_entry') AS intake_source,
                pir.source_system AS source_system,
                pir.external_reference_id AS external_reference_id,
                pir.external_record_type AS external_record_type,
                pir.external_record_url AS external_record_url,
                COALESCE(pir.source_document_required, FALSE) AS source_document_required,
                COALESCE(pir.source_document_received, FALSE) AS source_document_received,
                COALESCE(docs.document_count, 0)::bigint AS document_count,
                COALESCE(pir.planned_engineering_cost, 0)::numeric AS planned_engineering_cost,
                COALESCE(pir.planned_pm_cost, 0)::numeric AS planned_pm_cost,
                COALESCE(pir.planned_total_project_cost, 0)::numeric AS planned_total_project_cost
            FROM project_intake_requests pir
            LEFT JOIN app_users pm ON pm.user_id = pir.assigned_pm_user_id
            LEFT JOIN app_users ae ON ae.user_id = pir.account_executive_user_id
            LEFT JOIN app_users sa ON sa.user_id = pir.solution_architect_user_id
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
                reader.IsDBNull(O("client_id")) ? null : reader.GetGuid(O("client_id")),
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
                /* 053I_INTAKE_AE_SA_SUMMARY_VALUES_START */
                S("account_executive_name"),
                S("account_executive_email"),
                S("solution_architect_name"),
                S("solution_architect_email"),
                /* 053I_INTAKE_AE_SA_SUMMARY_VALUES_END */
                ReadDateTimeOffset(reader, O("created_at")),
                reader.GetString(O("intake_source")),
                S("source_system"),
                S("external_reference_id"),
                S("external_record_type"),
                S("external_record_url"),
                !reader.IsDBNull(O("source_document_required")) && reader.GetBoolean(O("source_document_required")),
                !reader.IsDBNull(O("source_document_received")) && reader.GetBoolean(O("source_document_received")),
                reader.GetInt64(O("document_count")),
                reader.GetDecimal(O("planned_engineering_cost")),
                reader.GetDecimal(O("planned_pm_cost")),
                reader.GetDecimal(O("planned_total_project_cost"))));
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
                /* 053I_PROJECT_AE_SA_SELECT_FIELDS_START */
                ae.display_name AS account_executive_name,
                ae.email AS account_executive_email,
                sa.display_name AS solution_architect_name,
                sa.email AS solution_architect_email,
                /* 053I_PROJECT_AE_SA_SELECT_FIELDS_END */
                COUNT(DISTINCT pt.task_id)::bigint AS task_count,
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS assignment_count
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            LEFT JOIN app_users ae ON ae.user_id = p.account_executive_user_id
            LEFT JOIN app_users sa ON sa.user_id = p.solution_architect_user_id
            LEFT JOIN project_tasks pt ON pt.project_id = p.project_id AND pt.is_active = TRUE
            LEFT JOIN project_assignments pa ON pa.project_id = p.project_id
            GROUP BY p.project_id, p.project_code, p.project_name, c.client_name, p.status, p.start_date, p.end_date, p.billable, pm.display_name, ae.display_name, ae.email, sa.display_name, sa.email
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
                /* 053I_PROJECT_AE_SA_SUMMARY_VALUES_START */
                S("account_executive_name"),
                S("account_executive_email"),
                S("solution_architect_name"),
                S("solution_architect_email"),
                /* 053I_PROJECT_AE_SA_SUMMARY_VALUES_END */
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
               AND r.role_code IN ('ENGINEERING', 'ENGINEER')
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

    /* 053I_AE_SA_USER_OPTION_HELPER_START */
    private static async Task<List<UserOption>> LoadUsersByRoleOrProfileAsync(NpgsqlConnection connection, string[] roleCodes, string[] profileTerms)
    {
        var rows = new List<UserOption>();

        const string sql = """
            SELECT DISTINCT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email,
                COALESCE(NULLIF(u.job_title, ''), NULLIF(u.role_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), '')
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.is_active = TRUE
              AND COALESCE(u.login_enabled, TRUE) = TRUE
              AND (
                    r.role_code = ANY(@role_codes)
                 OR EXISTS (
                        SELECT 1
                        FROM unnest(@profile_terms) AS profile_term(term)
                        WHERE LOWER(
                            COALESCE(u.job_title, '') || ' ' ||
                            COALESCE(u.role_name, '') || ' ' ||
                            COALESCE(u.department_name, '') || ' ' ||
                            COALESCE(u.department, '') || ' ' ||
                            COALESCE(u.team_name, '')
                        ) LIKE '%' || LOWER(profile_term.term) || '%'
                    )
              )
            ORDER BY display_name, u.email;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("role_codes", roleCodes);
        command.Parameters.AddWithValue("profile_terms", profileTerms);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new UserOption(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }
    /* 053I_AE_SA_USER_OPTION_HELPER_END */

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

    private static async Task InsertAuditLogAsync(
        NpgsqlConnection connection,
        string action,
        string entityType,
        Guid entityId,
        Guid? actorUserId = null)
    {
        const string sql = """
            INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
            VALUES (@actor_user_id, @action, @entity_type, @entity_id);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actor_user_id", actorUserId is null ? DBNull.Value : actorUserId.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);

        await command.ExecuteNonQueryAsync();
    }

    private static bool CanViewIntake(ProjectNotificationActor actor) =>
        CanManageIntake(actor)
        || actor.Permissions.Contains("VIEW_PROJECT_INTAKE")
        || actor.Permissions.Contains("VIEW_PROJECT_INTAKE_AGING")
        || actor.Permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF");

    private static bool CanManageIntake(ProjectNotificationActor actor) =>
        actor.IsAdministrator
        || actor.IsCoordinator
        || actor.Roles.Contains("SALES")
        || actor.Roles.Contains("INSIDE_SALES")
        || actor.Roles.Contains("ACCOUNT_EXECUTIVE")
        || actor.Roles.Contains("ACCOUNT_EXECUTIVES")
        || actor.Roles.Contains("SOLUTION_ARCHITECT")
        || actor.Roles.Contains("SA")
        || actor.Roles.Contains("SAA")
        || actor.Roles.Contains("PROJECT_COORDINATOR")
        || actor.Roles.Contains("PROJECT_MANAGEMENT")
        || actor.Roles.Contains("PROJECT_MANAGER")
        || actor.Roles.Contains("PROJECT_MANAGEMENT_LEAD")
        || actor.Roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
        || actor.Permissions.Contains("MANAGE_PROJECT_INTAKE")
        || actor.Permissions.Contains("MANAGE_PROJECT_INTAKE_AGING")
        || actor.Permissions.Contains("MANAGE_PROJECT_DOCUMENTS");

    private static bool CanSubmitSignedHandoff(ProjectNotificationActor actor) =>
        actor.IsAdministrator
        || actor.IsCoordinator
        || actor.Roles.Overlaps([
            "SALES",
            "INSIDE_SALES",
            "ACCOUNT_EXECUTIVE",
            "ACCOUNT_EXECUTIVES",
            "SOLUTION_ARCHITECT",
            "SA",
            "SAA",
            "PROJECT_COORDINATOR"
        ])
        || actor.Permissions.Contains("MANAGE_PROJECT_INTAKE")
        || actor.Permissions.Contains("MANAGE_PROJECT_DOCUMENTS");


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

}

internal sealed record ProjectIntakeCreateRequest(
    Guid? ClientId,
    string ClientName,
    string? OpportunityReference,
    string RequestTitle,
    string? RequestDescription,
    Guid? AssignedPmUserId,
    /* 053I_INTAKE_AE_SA_RECORD_FIELDS_START */
    Guid? AccountExecutiveUserId,
    Guid? SolutionArchitectUserId,
    /* 053I_INTAKE_AE_SA_RECORD_FIELDS_END */
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
    string? IntakeSourceNotes,
    decimal? PlannedEngineeringCost,
    decimal? PlannedPmCost,
    decimal? PlannedTotalProjectCost);

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

internal sealed record SignedHandoffDocument(
    Guid Id,
    string Category,
    string FileName,
    long SizeBytes);

internal sealed record IntakeSummary(
    Guid Id,
    Guid? ClientId,
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
    /* 053I_INTAKE_AE_SA_SUMMARY_FIELDS_START */
    string? AccountExecutiveName,
    string? AccountExecutiveEmail,
    string? SolutionArchitectName,
    string? SolutionArchitectEmail,
    /* 053I_INTAKE_AE_SA_SUMMARY_FIELDS_END */
    DateTimeOffset CreatedAt,
    string IntakeSource,
    string? SourceSystem,
    string? ExternalReferenceId,
    string? ExternalRecordType,
    string? ExternalRecordUrl,
    bool SourceDocumentRequired,
    bool SourceDocumentReceived,
    long DocumentCount,
    decimal PlannedEngineeringCost,
    decimal PlannedPmCost,
    decimal PlannedTotalProjectCost);

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
    /* 053I_PROJECT_AE_SA_SUMMARY_FIELDS_START */
    string? AccountExecutiveName,
    string? AccountExecutiveEmail,
    string? SolutionArchitectName,
    string? SolutionArchitectEmail,
    /* 053I_PROJECT_AE_SA_SUMMARY_FIELDS_END */
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
