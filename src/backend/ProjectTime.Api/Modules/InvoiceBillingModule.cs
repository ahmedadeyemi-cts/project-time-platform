using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class InvoiceBillingModule
{
    private static readonly string[] InvoiceEligibleStatuses =
    [
        "pm_approved",
        "manager_approved",
        "project_approved",
        "project_validated",
        "accounting_ready",
        "reconciled",
        "locked"
    ];

    public static WebApplication MapInvoiceBillingEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/billing/candidates",
            (Func<HttpContext, Task<IResult>>)GetCandidatesAsync);

        app.MapGet(
            "/api/billing/projects/{projectId:guid}/candidates",
            (Func<Guid, HttpContext, Task<IResult>>)GetProjectCandidateAsync);

        app.MapGet(
            "/api/billing/projects/{projectId:guid}/invoices",
            (Func<Guid, HttpContext, Task<IResult>>)GetProjectInvoicesAsync);

        app.MapGet(
            "/api/billing/invoices/{invoiceId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)GetInvoiceAsync);

        app.MapPost(
            "/api/billing/projects/{projectId:guid}/invoices",
            (Func<Guid, InvoiceBillingCreateInvoiceRequest, HttpContext, Task<IResult>>)CreateInvoiceAsync);

        return app;
    }

    private static async Task<IResult> GetCandidatesAsync(HttpContext httpContext)
    {
        return await BuildCandidatesResultAsync(null, httpContext);
    }

    private static async Task<IResult> GetProjectCandidateAsync(Guid projectId, HttpContext httpContext)
    {
        return await BuildCandidatesResultAsync(projectId, httpContext);
    }

    private static async Task<IResult> BuildCandidatesResultAsync(Guid? projectId, HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);

        if (sessionUserId is null)
        {
            return SessionRequired();
        }

        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        var configError = ValidateConfig(config);
        if (configError is not null) return configError;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var access = await LoadAccessContextAsync(connection, sessionUserId.Value);

        if (!access.CanViewBilling)
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Invoice and billing data requires an active ProjectPulse role."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var connectorStatuses = await LoadConnectorStatusesAsync(connection);
        var projectRows = await LoadProjectRowsAsync(connection, access, projectId);
        var candidates = new List<InvoiceBillingCandidate>();

        foreach (var project in projectRows)
        {
            var assignedEngineers = await LoadAssignedEngineersAsync(connection, project.ProjectId);
            var lines = await LoadCandidateLinesAsync(connection, project.ProjectId);
            var nonLaborLines = await LoadNonLaborCandidateLinesAsync(connection, project.ProjectId);
            var invoiceHistory = await LoadInvoiceHistoryAsync(connection, project.ProjectId);
            var commercial = await SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync(
                connection,
                project.ProjectId);

            var projectBlockers = BuildProjectBlockers(project, lines, nonLaborLines);
            var approvedHours = lines.Sum(line => line.ApprovedHours);
            decimal? autoCalculatedAmount = null;

            if (lines.Count > 0 && lines.All(line => line.RateOptions.Count == 1))
            {
                autoCalculatedAmount = decimal.Round(
                    lines.Sum(line => line.ApprovedHours * line.RateOptions[0].UnitRate),
                    2,
                    MidpointRounding.AwayFromZero);
            }

            var rateResolutionStatus = lines.Count == 0
                ? "no_eligible_time"
                : lines.Any(line => line.RateOptions.Count == 0)
                    ? "missing_rate"
                    : lines.Any(line => line.RateOptions.Count > 1)
                        ? "selection_required"
                        : "resolved";

            candidates.Add(new InvoiceBillingCandidate(
                project.ProjectId,
                project.ClientId,
                project.CustomerName,
                project.ProjectCode,
                project.ProjectName,
                string.Empty,
                project.ContractType,
                project.Status,
                project.ProjectManagerName,
                project.ProjectCoordinatorName,
                assignedEngineers,
                project.CertiniaId,
                project.SellQuoteNumber,
                project.SalesforceId,
                project.PurchaseOrderRequired,
                project.PurchaseOrder,
                lines,
                nonLaborLines,
                lines.Count,
                nonLaborLines.Count,
                approvedHours,
                autoCalculatedAmount,
                rateResolutionStatus,
                projectBlockers,
                projectBlockers.Count == 0
                    && (lines.Any(line => line.RateOptions.Count > 0) || nonLaborLines.Count > 0),
                access.CanCreateInvoices,
                invoiceHistory,
                commercial));
        }

        if (projectId is not null && candidates.Count == 0)
        {
            return Results.Json(new
            {
                status = "not_found_or_not_authorized",
                message = "The requested project was not found or is outside the current user's billing scope."
            }, statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(new
        {
            status = "billing_candidates_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            approvedStatuses = InvoiceEligibleStatuses,
            canCreateInvoices = access.CanCreateInvoices,
            scope = access.ScopeLabel,
            connectorStatuses,
            count = candidates.Count,
            candidates
        });
    }

    private static async Task<IResult> GetProjectInvoicesAsync(Guid projectId, HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);
        if (sessionUserId is null) return SessionRequired();

        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        var configError = ValidateConfig(config);
        if (configError is not null) return configError;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var access = await LoadAccessContextAsync(connection, sessionUserId.Value);

        if (!access.CanViewBilling || !await CanAccessProjectAsync(connection, access, projectId))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "The requested project is outside the current user's billing scope."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var invoices = await LoadInvoiceHistoryAsync(connection, projectId);

        return Results.Ok(new
        {
            status = "billing_invoice_history_loaded",
            projectId,
            count = invoices.Count,
            invoices
        });
    }

    private static async Task<IResult> GetInvoiceAsync(Guid invoiceId, HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);
        if (sessionUserId is null) return SessionRequired();

        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        var configError = ValidateConfig(config);
        if (configError is not null) return configError;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var access = await LoadAccessContextAsync(connection, sessionUserId.Value);
        if (!access.CanViewBilling)
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Invoice and billing data requires an active ProjectPulse role."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var projectId = await LoadInvoiceProjectIdAsync(connection, invoiceId);

        if (projectId is null)
        {
            return Results.NotFound(new
            {
                status = "invoice_not_found",
                message = "The requested invoice was not found."
            });
        }

        if (!await CanAccessProjectAsync(connection, access, projectId.Value))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "The requested invoice is outside the current user's billing scope."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var invoice = await LoadInvoiceDetailAsync(connection, invoiceId);

        return invoice is null
            ? Results.NotFound(new
            {
                status = "invoice_not_found",
                message = "The requested invoice was not found."
            })
            : Results.Ok(new
            {
                status = "billing_invoice_loaded",
                invoice
            });
    }

    private static async Task<IResult> CreateInvoiceAsync(
        Guid projectId,
        InvoiceBillingCreateInvoiceRequest request,
        HttpContext httpContext)
    {
        var sessionUserId = GetSessionUserId(httpContext);
        if (sessionUserId is null) return SessionRequired();

        var invoiceType = Clean(request.InvoiceType).ToLowerInvariant();

        if (invoiceType is not ("partial" or "final"))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Invoice type must be partial or final."
            });
        }

        var requestedLines = (request.Lines ?? [])
            .Where(line => line.TimeEntryId != Guid.Empty && line.RateLineId != Guid.Empty)
            .ToList();
        var requestedReadinessReviewIds = (request.BillingReadinessReviewIds ?? [])
            .Where(reviewId => reviewId != Guid.Empty)
            .ToList();

        if (requestedLines.Count == 0 && requestedReadinessReviewIds.Count == 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Select at least one approved labor line or one governed ready non-labor package."
            });
        }

        if (requestedLines.Count + requestedReadinessReviewIds.Count > 250)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "A single invoice may contain no more than 250 selected source lines."
            });
        }

        if (requestedLines.Select(line => line.TimeEntryId).Distinct().Count() != requestedLines.Count)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Each time entry may appear only once in an invoice request."
            });
        }

        if (requestedReadinessReviewIds.Distinct().Count() != requestedReadinessReviewIds.Count)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Each governed non-labor package may appear only once in an invoice request."
            });
        }

        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        var configError = ValidateConfig(config);
        if (configError is not null) return configError;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            var access = await LoadAccessContextAsync(connection, sessionUserId.Value, transaction);

            if (!access.CanCreateInvoices)
            {
                await SafeRollbackAsync(transaction);
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "Invoice creation is restricted to authorized billing, accounting, project-management, coordinator, and administrator roles."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!await CanAccessProjectAsync(connection, access, projectId, transaction))
            {
                await SafeRollbackAsync(transaction);
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "The requested project is outside the current user's billing scope."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var project = await LoadProjectForInvoiceAsync(connection, transaction, projectId);

            if (project is null)
            {
                await SafeRollbackAsync(transaction);
                return Results.NotFound(new
                {
                    status = "project_not_found",
                    message = "The requested project was not found."
                });
            }

            var projectBlockers = BuildProjectStructuralBlockers(project);

            if (IsFixedPrice(project.ContractType) && requestedLines.Count > 0)
            {
                projectBlockers.Add("Fixed Price hourly time is utilization evidence only; invoice the governed milestone or expense package instead.");
            }

            if (projectBlockers.Count > 0)
            {
                await SafeRollbackAsync(transaction);
                return Results.Conflict(new
                {
                    status = "billing_blocked",
                    message = "The project has unresolved billing blockers.",
                    blockers = projectBlockers
                });
            }

            var selectedLines = new List<InvoiceBillingResolvedLine>();
            var selectedNonLaborLines = new List<InvoiceBillingResolvedNonLaborLine>();

            foreach (var requestedLine in requestedLines)
            {
                var line = await LoadResolvedInvoiceLineAsync(
                    connection,
                    transaction,
                    projectId,
                    requestedLine.TimeEntryId,
                    requestedLine.RateLineId);

                if (line is null)
                {
                    await SafeRollbackAsync(transaction);
                    return Results.Conflict(new
                    {
                        status = "line_not_invoice_eligible",
                        message = $"Time entry {requestedLine.TimeEntryId} is no longer eligible, was already invoiced, or the selected rate is not an effective stored rate for that entry."
                    });
                }

                selectedLines.Add(line);
            }

            foreach (var reviewId in requestedReadinessReviewIds)
            {
                var line = await LoadResolvedNonLaborLineAsync(
                    connection,
                    transaction,
                    projectId,
                    reviewId);

                if (line is null)
                {
                    await SafeRollbackAsync(transaction);
                    return Results.Conflict(new
                    {
                        status = "package_not_invoice_eligible",
                        message = $"Billing readiness package {reviewId} is no longer ready, lacks governed evidence, or is already invoiced."
                    });
                }

                selectedNonLaborLines.Add(line);
            }

            if (invoiceType == "final")
            {
                var remainingEligibleCount = IsFixedPrice(project.ContractType)
                    ? 0
                    : await CountEligibleTimeEntriesAsync(
                        connection,
                        transaction,
                        projectId);

                var remainingNonLaborCount = await CountEligibleNonLaborPackagesAsync(
                    connection,
                    transaction,
                    projectId);

                if (remainingEligibleCount != selectedLines.Count
                    || remainingNonLaborCount != selectedNonLaborLines.Count)
                {
                    await SafeRollbackAsync(transaction);
                    return Results.Conflict(new
                    {
                        status = "final_invoice_incomplete",
                        message = "A final invoice must include every currently eligible uninvoiced labor line and ready non-labor package for the project.",
                        remainingEligibleCount,
                        remainingNonLaborCount,
                        selectedCount = selectedLines.Count,
                        selectedNonLaborCount = selectedNonLaborLines.Count
                    });
                }
            }

            var identity = await AllocateInvoiceIdentityAsync(
                connection,
                transaction,
                projectId,
                sessionUserId.Value);

            var invoiceId = Guid.NewGuid();
            var billingPeriodStart = selectedLines.Select(line => line.WorkDate)
                .Concat(selectedNonLaborLines.Select(line => line.BillingPeriodStart))
                .Min();
            var billingPeriodEnd = selectedLines.Select(line => line.WorkDate)
                .Concat(selectedNonLaborLines.Select(line => line.BillingPeriodEnd))
                .Max();
            var subtotal = decimal.Round(
                selectedLines.Sum(line => line.LineAmount)
                    + selectedNonLaborLines.Sum(line => line.LineAmount),
                2,
                MidpointRounding.AwayFromZero);

            var immutableSnapshot = JsonSerializer.Serialize(new
            {
                source = "ProjectPulse Module 042",
                projectId,
                identity.InvoiceNumber,
                invoiceType,
                createdByUserId = sessionUserId.Value,
                createdAt = DateTimeOffset.UtcNow,
                selectedTimeEntryIds = selectedLines.Select(line => line.TimeEntryId).ToArray(),
                selectedRateLineIds = selectedLines.Select(line => line.RateLineId).ToArray(),
                selectedBillingReadinessReviewIds = selectedNonLaborLines
                    .Select(line => line.ReadinessReviewId)
                    .ToArray(),
                project = new
                {
                    project.ProjectCode,
                    project.ProjectName,
                    project.CustomerName,
                    project.ContractType,
                    project.ProjectManagerName,
                    project.ProjectCoordinatorName,
                    project.CertiniaId,
                    project.SalesforceId,
                    project.SellQuoteNumber
                },
                purchaseOrder = project.PurchaseOrder,
                subtotal
            });

            await InsertInvoiceHeaderAsync(
                connection,
                transaction,
                invoiceId,
                identity,
                project,
                invoiceType,
                billingPeriodStart,
                billingPeriodEnd,
                subtotal,
                Clean(request.Notes),
                sessionUserId.Value,
                immutableSnapshot);

            var lineNumber = 1;

            foreach (var line in selectedLines.OrderBy(line => line.WorkDate).ThenBy(line => line.ResourceName))
            {
                await InsertInvoiceLineAsync(
                    connection,
                    transaction,
                    invoiceId,
                    lineNumber,
                    line);

                lineNumber++;
            }

            foreach (var line in selectedNonLaborLines
                .OrderBy(line => line.BillingPeriodStart)
                .ThenBy(line => line.Description))
            {
                await InsertNonLaborInvoiceLineAsync(
                    connection,
                    transaction,
                    invoiceId,
                    lineNumber,
                    line);

                lineNumber++;
            }

            await InsertInvoiceEventAsync(
                connection,
                transaction,
                invoiceId,
                sessionUserId.Value,
                invoiceType,
                identity.InvoiceNumber,
                selectedLines.Count + selectedNonLaborLines.Count,
                subtotal);

            await transaction.CommitAsync();

            var invoice = await LoadInvoiceDetailAsync(connection, invoiceId);

            return Results.Created(
                $"/api/billing/invoices/{invoiceId}",
                new
                {
                    status = "billing_invoice_created",
                    message = $"{(invoiceType == "final" ? "Final" : "Partial")} invoice {identity.InvoiceNumber} was created from verified labor and governed package records.",
                    invoice
                });
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "40001")
        {
            await SafeRollbackAsync(transaction);

            return Results.Conflict(new
            {
                status = "concurrent_billing_conflict",
                message = "The invoice could not be created because another billing transaction changed the same project or time entry. Reload billing data and try again."
            });
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private static async Task<List<InvoiceBillingProjectRow>> LoadProjectRowsAsync(
        NpgsqlConnection connection,
        InvoiceBillingAccessContext access,
        Guid? projectId)
    {
        var rows = new List<InvoiceBillingProjectRow>();

        const string sql = """
            SELECT
                p.project_id,
                p.client_id,
                COALESCE(c.client_name, '') AS customer_name,
                COALESCE(p.project_code, '') AS project_code,
                COALESCE(p.project_name, '') AS project_name,
                COALESCE(p.contract_type, '') AS contract_type,
                COALESCE(p.status, '') AS project_status,
                COALESCE(pm.display_name, pm.email, '') AS project_manager_name,
                COALESCE(ptc.display_name, ptc.email, '') AS project_coordinator_name,
                COALESCE(p.certinia_id_number, '') AS certinia_id,
                COALESCE(p.sell_quote_number, '') AS sell_quote_number,
                COALESCE(p.salesforce_id_number, '') AS salesforce_id,
                COALESCE(profile.purchase_order_required, FALSE) AS purchase_order_required,
                po.project_purchase_order_id,
                po.po_number,
                po.authorized_amount,
                po.effective_start_date,
                po.effective_end_date,
                po.customer_reference
            FROM projects p
            LEFT JOIN clients c
                ON c.client_id = p.client_id
            LEFT JOIN app_users pm
                ON pm.user_id = p.project_manager_user_id
            LEFT JOIN app_users ptc
                ON ptc.user_id = p.project_coordinator_user_id
            LEFT JOIN project_billing_profiles profile
                ON profile.project_id = p.project_id
            LEFT JOIN LATERAL (
                SELECT
                    candidate.project_purchase_order_id,
                    candidate.po_number,
                    candidate.authorized_amount,
                    candidate.effective_start_date,
                    candidate.effective_end_date,
                    candidate.customer_reference
                FROM project_purchase_orders candidate
                WHERE candidate.project_id = p.project_id
                  AND candidate.is_primary = TRUE
                  AND candidate.po_status = 'active'
                  AND (
                      candidate.effective_start_date IS NULL
                      OR candidate.effective_start_date <= CURRENT_DATE
                  )
                  AND (
                      candidate.effective_end_date IS NULL
                      OR candidate.effective_end_date >= CURRENT_DATE
                  )
                ORDER BY candidate.updated_at DESC, candidate.created_at DESC
                LIMIT 1
            ) po ON TRUE
            WHERE
                (@project_id IS NULL OR p.project_id = @project_id)
                AND (
                    @is_broad_scope = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR p.project_coordinator_user_id = @user_id
                    OR EXISTS (
                        SELECT 1
                        FROM project_assignments assignment
                        WHERE assignment.project_id = p.project_id
                          AND assignment.user_id = @user_id
                    )
                )
            ORDER BY p.created_at DESC, p.project_code
            LIMIT 250;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("project_id", NpgsqlDbType.Uuid).Value =
            projectId is null ? DBNull.Value : projectId.Value;
        command.Parameters.AddWithValue("is_broad_scope", access.IsBroadScope);
        command.Parameters.AddWithValue("user_id", access.UserId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var purchaseOrder = reader.IsDBNull(13)
                ? null
                : new InvoiceBillingPurchaseOrder(
                    reader.GetGuid(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    ReadDateOnlyOrNull(reader, 16),
                    ReadDateOnlyOrNull(reader, 17),
                    reader.GetString(18));

            rows.Add(new InvoiceBillingProjectRow(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetBoolean(12),
                purchaseOrder));
        }

        return rows;
    }

    private static async Task<List<string>> LoadAssignedEngineersAsync(
        NpgsqlConnection connection,
        Guid projectId)
    {
        var rows = new List<string>();

        const string sql = """
            SELECT DISTINCT COALESCE(u.display_name, u.email, '')
            FROM project_assignments assignment
            JOIN app_users u
                ON u.user_id = assignment.user_id
            WHERE assignment.project_id = @project_id
              AND COALESCE(u.is_active, TRUE) = TRUE
            ORDER BY 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var value = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(value)) rows.Add(value);
        }

        return rows;
    }

    private static async Task<List<InvoiceBillingCandidateLine>> LoadCandidateLinesAsync(
        NpgsqlConnection connection,
        Guid projectId)
    {
        var rows = new List<InvoiceBillingCandidateLine>();

        const string sql = """
            SELECT
                te.time_entry_id,
                te.work_date,
                te.user_id,
                COALESCE(resource.display_name, resource.email, '') AS resource_name,
                COALESCE(resource.email, '') AS resource_email,
                te.task_id,
                COALESCE(task.task_code, '') AS task_code,
                COALESCE(task.task_name, '') AS task_name,
                COALESCE(te.description, '') AS description,
                te.hours,
                COALESCE(te.time_type, 'normal') AS time_type,
                te.status
            FROM time_entries te
            JOIN app_users resource
                ON resource.user_id = te.user_id
            LEFT JOIN project_tasks task
                ON task.task_id = te.task_id
            WHERE te.project_id = @project_id
              AND te.billable = TRUE
              AND te.hours > 0
              AND te.status = ANY(@approved_statuses)
              AND NOT EXISTS (
                  SELECT 1
                  FROM billing_invoice_lines invoiced
                  JOIN billing_invoices invoice
                    ON invoice.billing_invoice_id = invoiced.billing_invoice_id
                  WHERE invoiced.time_entry_id = te.time_entry_id
                    AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
              )
            ORDER BY te.work_date, resource.display_name, task.task_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("approved_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
            InvoiceEligibleStatuses;

        await using var reader = await command.ExecuteReaderAsync();
        var sourceRows = new List<InvoiceBillingCandidateLineSource>();

        while (await reader.ReadAsync())
        {
            sourceRows.Add(new InvoiceBillingCandidateLineSource(
                reader.GetGuid(0),
                ReadDateOnly(reader, 1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetString(11)));
        }

        await reader.CloseAsync();

        foreach (var source in sourceRows)
        {
            var rateOptions = await LoadRateOptionsAsync(connection, source.TimeEntryId);

            rows.Add(new InvoiceBillingCandidateLine(
                source.TimeEntryId,
                source.WorkDate,
                source.ResourceUserId,
                source.ResourceName,
                source.ResourceEmail,
                source.TaskId,
                source.TaskCode,
                source.TaskName,
                source.Description,
                source.ApprovedHours,
                source.TimeType,
                source.ApprovalStatus,
                rateOptions,
                rateOptions.Count == 1 ? rateOptions[0].RateLineId : null,
                rateOptions.Count == 0
                    ? "No effective stored hourly rate matches this approved time entry."
                    : rateOptions.Count > 1
                        ? "Select the correct stored rate before invoicing."
                        : string.Empty));
        }

        return rows;
    }

    private static async Task<List<InvoiceBillingNonLaborCandidateLine>> LoadNonLaborCandidateLinesAsync(
        NpgsqlConnection connection,
        Guid projectId)
    {
        var rows = new List<InvoiceBillingNonLaborCandidateLine>();

        await using var command = new NpgsqlCommand("""
            SELECT
                review.work_billing_readiness_review_id,
                review.billing_period_start,
                review.billing_period_end,
                review.package_type,
                review.evidence_source_type,
                review.evidence_description,
                review.evidence_amount,
                COALESCE(actor.display_name, actor.email, ''),
                review.updated_at
            FROM work_billing_readiness_reviews review
            LEFT JOIN app_users actor ON actor.user_id = review.reviewed_by_user_id
            WHERE review.project_id = @project_id
              AND review.review_status = 'ready'
              AND review.evidence_source_type IN ('expense', 'fixed_price_milestone')
              AND COALESCE(review.evidence_amount, 0) > 0
              AND review.evidence_description <> ''
              AND NOT EXISTS (
                  SELECT 1
                  FROM billing_invoice_lines line
                  JOIN billing_invoices invoice
                    ON invoice.billing_invoice_id = line.billing_invoice_id
                  WHERE line.billing_readiness_review_id = review.work_billing_readiness_review_id
                    AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
              )
            ORDER BY review.billing_period_start, review.updated_at;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new InvoiceBillingNonLaborCandidateLine(
                reader.GetGuid(0),
                ReadDateOnly(reader, 1),
                ReadDateOnly(reader, 2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetString(7),
                ReadDateTimeOffset(reader, 8)));
        }

        return rows;
    }

    private static async Task<List<InvoiceBillingRateOption>> LoadRateOptionsAsync(
        NpgsqlConnection connection,
        Guid timeEntryId,
        NpgsqlTransaction? transaction = null)
    {
        var rows = new List<InvoiceBillingRateOption>();

        const string sql = """
            SELECT
                line.rate_line_id,
                card.rate_card_id,
                card.rate_card_code,
                card.rate_card_name,
                line.sku_code,
                line.display_name,
                line.description,
                line.labor_category,
                line.time_type,
                line.unit_type,
                line.rate_amount,
                COALESCE(profile.default_rate_card_id = card.rate_card_id, FALSE) AS is_project_default,
                COALESCE(card.client_id = project.client_id, FALSE) AS is_customer_rate
            FROM time_entries entry
            JOIN projects project
                ON project.project_id = entry.project_id
            LEFT JOIN project_billing_profiles profile
                ON profile.project_id = project.project_id
            JOIN work_rate_cards card
                ON LOWER(card.status) IN ('active', 'published', 'approved')
               AND card.effective_start_date <= entry.work_date
               AND (
                    card.effective_end_date IS NULL
                    OR card.effective_end_date >= entry.work_date
               )
            JOIN work_rate_card_lines line
                ON line.rate_card_id = card.rate_card_id
               AND line.is_active = TRUE
               AND line.billable_default = TRUE
               AND LOWER(line.unit_type) = 'hour'
               AND LOWER(line.time_type) = LOWER(COALESCE(entry.time_type, 'normal'))
            WHERE entry.time_entry_id = @time_entry_id
              AND (
                    (
                        profile.default_rate_card_id IS NOT NULL
                        AND card.rate_card_id = profile.default_rate_card_id
                    )
                    OR (
                        profile.default_rate_card_id IS NULL
                        AND (
                            card.client_id = project.client_id
                            OR card.client_id IS NULL
                        )
                    )
              )
            ORDER BY
                COALESCE(profile.default_rate_card_id = card.rate_card_id, FALSE) DESC,
                COALESCE(card.client_id = project.client_id, FALSE) DESC,
                line.display_order,
                card.rate_card_name,
                line.display_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("time_entry_id", timeEntryId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new InvoiceBillingRateOption(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetDecimal(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12)));
        }

        return rows;
    }

    private static async Task<List<InvoiceBillingInvoiceSummary>> LoadInvoiceHistoryAsync(
        NpgsqlConnection connection,
        Guid projectId)
    {
        var rows = new List<InvoiceBillingInvoiceSummary>();

        const string sql = """
            SELECT
                invoice.billing_invoice_id,
                invoice.invoice_number,
                invoice.invoice_type,
                invoice.invoice_status,
                invoice.billing_period_start,
                invoice.billing_period_end,
                invoice.invoice_date,
                invoice.subtotal_amount,
                invoice.total_amount,
                invoice.created_at,
                invoice.finalized_at,
                COUNT(line.billing_invoice_line_id)::integer AS line_count
            FROM billing_invoices invoice
            LEFT JOIN billing_invoice_lines line
                ON line.billing_invoice_id = invoice.billing_invoice_id
            WHERE invoice.project_id = @project_id
            GROUP BY invoice.billing_invoice_id
            ORDER BY invoice.invoice_series_number DESC, invoice.invoice_installment_number DESC;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new InvoiceBillingInvoiceSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                ReadDateOnlyOrNull(reader, 4),
                ReadDateOnlyOrNull(reader, 5),
                ReadDateOnlyOrNull(reader, 6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                ReadDateTimeOffset(reader, 9),
                reader.IsDBNull(10) ? null : ReadDateTimeOffset(reader, 10),
                reader.GetInt32(11)));
        }

        return rows;
    }

    private static async Task<List<InvoiceBillingConnectorStatus>> LoadConnectorStatusesAsync(
        NpgsqlConnection connection)
    {
        var rows = new List<InvoiceBillingConnectorStatus>();

        const string sql = """
            SELECT
                system_code,
                display_name,
                environment_name,
                connection_status,
                inbound_enabled,
                outbound_enabled,
                last_connection_test_status,
                last_connection_test_at,
                last_successful_sync_at
            FROM external_integration_connections
            ORDER BY display_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new InvoiceBillingConnectorStatus(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : ReadDateTimeOffset(reader, 7),
                reader.IsDBNull(8) ? null : ReadDateTimeOffset(reader, 8)));
        }

        return rows;
    }

    private static List<string> BuildProjectBlockers(
        InvoiceBillingProjectRow project,
        IReadOnlyList<InvoiceBillingCandidateLine> lines,
        IReadOnlyList<InvoiceBillingNonLaborCandidateLine> nonLaborLines)
    {
        var blockers = BuildProjectStructuralBlockers(project);

        if (lines.Count == 0 && nonLaborLines.Count == 0)
            blockers.Add("No approved uninvoiced labor or governed ready non-labor package is currently eligible.");

        if (IsFixedPrice(project.ContractType)
            && nonLaborLines.Count == 0)
        {
            blockers.Add("Fixed Price invoice dollars require a governed ready milestone or expense package; hourly time remains utilization evidence only.");
        }

        return blockers;
    }

    private static List<string> BuildProjectStructuralBlockers(
        InvoiceBillingProjectRow project)
    {
        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(project.CustomerName))
            blockers.Add("Customer is not configured.");

        if (string.IsNullOrWhiteSpace(project.ProjectCode))
            blockers.Add("Project code is not configured.");

        if (string.IsNullOrWhiteSpace(project.ContractType))
            blockers.Add("Contract type is not configured.");

        if (string.IsNullOrWhiteSpace(project.ProjectManagerName))
            blockers.Add("Project Manager is not assigned.");

        if (project.PurchaseOrderRequired && project.PurchaseOrder is null)
            blockers.Add("An active primary purchase order is required before invoicing.");

        return blockers;
    }

    private static async Task<InvoiceBillingProjectRow?> LoadProjectForInvoiceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId)
    {
        const string sql = """
            SELECT
                p.project_id,
                p.client_id,
                COALESCE(c.client_name, '') AS customer_name,
                COALESCE(p.project_code, '') AS project_code,
                COALESCE(p.project_name, '') AS project_name,
                COALESCE(p.contract_type, '') AS contract_type,
                COALESCE(p.status, '') AS project_status,
                COALESCE(pm.display_name, pm.email, '') AS project_manager_name,
                COALESCE(ptc.display_name, ptc.email, '') AS project_coordinator_name,
                COALESCE(p.certinia_id_number, '') AS certinia_id,
                COALESCE(p.sell_quote_number, '') AS sell_quote_number,
                COALESCE(p.salesforce_id_number, '') AS salesforce_id,
                COALESCE(profile.purchase_order_required, FALSE) AS purchase_order_required,
                po.project_purchase_order_id,
                po.po_number,
                po.authorized_amount,
                po.effective_start_date,
                po.effective_end_date,
                po.customer_reference
            FROM projects p
            LEFT JOIN clients c
                ON c.client_id = p.client_id
            LEFT JOIN app_users pm
                ON pm.user_id = p.project_manager_user_id
            LEFT JOIN app_users ptc
                ON ptc.user_id = p.project_coordinator_user_id
            LEFT JOIN project_billing_profiles profile
                ON profile.project_id = p.project_id
            LEFT JOIN LATERAL (
                SELECT
                    candidate.project_purchase_order_id,
                    candidate.po_number,
                    candidate.authorized_amount,
                    candidate.effective_start_date,
                    candidate.effective_end_date,
                    candidate.customer_reference
                FROM project_purchase_orders candidate
                WHERE candidate.project_id = p.project_id
                  AND candidate.is_primary = TRUE
                  AND candidate.po_status = 'active'
                  AND (
                      candidate.effective_start_date IS NULL
                      OR candidate.effective_start_date <= CURRENT_DATE
                  )
                  AND (
                      candidate.effective_end_date IS NULL
                      OR candidate.effective_end_date >= CURRENT_DATE
                  )
                ORDER BY candidate.updated_at DESC, candidate.created_at DESC
                LIMIT 1
            ) po ON TRUE
            WHERE p.project_id = @project_id
            FOR UPDATE OF p;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return null;

        var purchaseOrder = reader.IsDBNull(13)
            ? null
            : new InvoiceBillingPurchaseOrder(
                reader.GetGuid(13),
                reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                ReadDateOnlyOrNull(reader, 16),
                ReadDateOnlyOrNull(reader, 17),
                reader.GetString(18));

        return new InvoiceBillingProjectRow(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetBoolean(12),
            purchaseOrder);
    }

    private static async Task<InvoiceBillingResolvedLine?> LoadResolvedInvoiceLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid timeEntryId,
        Guid rateLineId)
    {
        const string sql = """
            SELECT
                entry.time_entry_id,
                entry.work_date,
                entry.user_id,
                COALESCE(resource.display_name, resource.email, '') AS resource_name,
                COALESCE(resource.email, '') AS resource_email,
                entry.task_id,
                COALESCE(task.task_code, '') AS task_code,
                COALESCE(task.task_name, '') AS task_name,
                COALESCE(entry.description, '') AS description,
                entry.hours,
                COALESCE(entry.time_type, 'normal') AS time_type,
                entry.status,
                line.rate_line_id,
                card.rate_card_id,
                card.rate_card_code,
                card.rate_card_name,
                line.sku_code,
                line.display_name,
                line.description,
                line.labor_category,
                line.rate_amount
            FROM time_entries entry
            JOIN projects project
                ON project.project_id = entry.project_id
            JOIN app_users resource
                ON resource.user_id = entry.user_id
            LEFT JOIN project_tasks task
                ON task.task_id = entry.task_id
            LEFT JOIN project_billing_profiles profile
                ON profile.project_id = project.project_id
            JOIN work_rate_card_lines line
                ON line.rate_line_id = @rate_line_id
               AND line.is_active = TRUE
               AND line.billable_default = TRUE
               AND LOWER(line.unit_type) = 'hour'
               AND LOWER(line.time_type) = LOWER(COALESCE(entry.time_type, 'normal'))
            JOIN work_rate_cards card
                ON card.rate_card_id = line.rate_card_id
               AND LOWER(card.status) IN ('active', 'published', 'approved')
               AND card.effective_start_date <= entry.work_date
               AND (
                    card.effective_end_date IS NULL
                    OR card.effective_end_date >= entry.work_date
               )
            WHERE entry.time_entry_id = @time_entry_id
              AND entry.project_id = @project_id
              AND entry.billable = TRUE
              AND entry.hours > 0
              AND entry.status = ANY(@approved_statuses)
              AND COALESCE(line.rate_amount, 0) > 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM billing_invoice_lines invoiced
                  JOIN billing_invoices invoice
                    ON invoice.billing_invoice_id = invoiced.billing_invoice_id
                  WHERE invoiced.time_entry_id = entry.time_entry_id
                    AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
              )
              AND (
                    (
                        profile.default_rate_card_id IS NOT NULL
                        AND card.rate_card_id = profile.default_rate_card_id
                    )
                    OR (
                        profile.default_rate_card_id IS NULL
                        AND (
                            card.client_id = project.client_id
                            OR card.client_id IS NULL
                        )
                    )
              )
            FOR UPDATE OF entry;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("time_entry_id", timeEntryId);
        command.Parameters.AddWithValue("rate_line_id", rateLineId);
        command.Parameters.Add("approved_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
            InvoiceEligibleStatuses;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return null;

        var approvedHours = reader.GetDecimal(9);
        var unitRate = reader.GetDecimal(20);
        var amount = decimal.Round(
            approvedHours * unitRate,
            2,
            MidpointRounding.AwayFromZero);

        return new InvoiceBillingResolvedLine(
            reader.GetGuid(0),
            ReadDateOnly(reader, 1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            approvedHours,
            reader.GetString(10),
            reader.GetString(11),
            reader.GetGuid(12),
            reader.GetGuid(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetString(18),
            reader.GetString(19),
            unitRate,
            amount);
    }

    private static async Task<InvoiceBillingResolvedNonLaborLine?> LoadResolvedNonLaborLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid readinessReviewId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                review.work_billing_readiness_review_id,
                review.billing_period_start,
                review.billing_period_end,
                review.package_type,
                review.evidence_source_type,
                review.evidence_description,
                review.evidence_amount
            FROM work_billing_readiness_reviews review
            WHERE review.work_billing_readiness_review_id = @review_id
              AND review.project_id = @project_id
              AND review.review_status = 'ready'
              AND review.evidence_source_type IN ('expense', 'fixed_price_milestone')
              AND COALESCE(review.evidence_amount, 0) > 0
              AND review.evidence_description <> ''
              AND NOT EXISTS (
                  SELECT 1
                  FROM billing_invoice_lines line
                  JOIN billing_invoices invoice
                    ON invoice.billing_invoice_id = line.billing_invoice_id
                  WHERE line.billing_readiness_review_id = review.work_billing_readiness_review_id
                    AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
              )
            FOR UPDATE OF review;
            """, connection, transaction);
        command.Parameters.AddWithValue("review_id", readinessReviewId);
        command.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new InvoiceBillingResolvedNonLaborLine(
            reader.GetGuid(0),
            ReadDateOnly(reader, 1),
            ReadDateOnly(reader, 2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetDecimal(6));
    }

    private static async Task<int> CountEligibleNonLaborPackagesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)::integer
            FROM work_billing_readiness_reviews review
            WHERE review.project_id = @project_id
              AND review.review_status = 'ready'
              AND review.evidence_source_type IN ('expense', 'fixed_price_milestone')
              AND COALESCE(review.evidence_amount, 0) > 0
              AND review.evidence_description <> ''
              AND NOT EXISTS (
                  SELECT 1
                  FROM billing_invoice_lines line
                  JOIN billing_invoices invoice
                    ON invoice.billing_invoice_id = line.billing_invoice_id
                  WHERE line.billing_readiness_review_id = review.work_billing_readiness_review_id
                    AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
              );
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountEligibleTimeEntriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId)
    {
        const string sql = """
            SELECT COUNT(*)::integer
            FROM time_entries entry
            WHERE entry.project_id = @project_id
              AND entry.billable = TRUE
              AND entry.hours > 0
              AND entry.status = ANY(@approved_statuses)
              AND NOT EXISTS (
                  SELECT 1
                  FROM billing_invoice_lines invoiced
                  JOIN billing_invoices invoice
                    ON invoice.billing_invoice_id = invoiced.billing_invoice_id
                  WHERE invoiced.time_entry_id = entry.time_entry_id
                    AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("approved_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
            InvoiceEligibleStatuses;

        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<InvoiceBillingIdentity> AllocateInvoiceIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid actorUserId)
    {
        await using (var ensureCommand = new NpgsqlCommand("""
            INSERT INTO project_billing_profiles (
                project_id,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @project_id,
                @actor_user_id,
                @actor_user_id
            )
            ON CONFLICT (project_id) DO NOTHING;
            """, connection, transaction))
        {
            ensureCommand.Parameters.AddWithValue("project_id", projectId);
            ensureCommand.Parameters.AddWithValue("actor_user_id", actorUserId);
            await ensureCommand.ExecuteNonQueryAsync();
        }

        long? seriesNumber;

        await using (var lockCommand = new NpgsqlCommand("""
            SELECT invoice_series_number
            FROM project_billing_profiles
            WHERE project_id = @project_id
            FOR UPDATE;
            """, connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("project_id", projectId);
            var result = await lockCommand.ExecuteScalarAsync();
            seriesNumber = result is null or DBNull ? null : Convert.ToInt64(result);
        }

        if (seriesNumber is null)
        {
            await using var allocateCommand = new NpgsqlCommand("""
                UPDATE project_billing_profiles
                SET invoice_series_number = nextval('billing_invoice_series_seq'),
                    updated_by_user_id = @actor_user_id,
                    updated_at = NOW()
                WHERE project_id = @project_id
                  AND invoice_series_number IS NULL
                RETURNING invoice_series_number;
                """, connection, transaction);

            allocateCommand.Parameters.AddWithValue("project_id", projectId);
            allocateCommand.Parameters.AddWithValue("actor_user_id", actorUserId);

            seriesNumber = Convert.ToInt64(
                await allocateCommand.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Unable to allocate an invoice series."));
        }

        int installmentNumber;

        await using (var installmentCommand = new NpgsqlCommand("""
            SELECT COALESCE(MAX(invoice_installment_number), 0)::integer + 1
            FROM billing_invoices
            WHERE project_id = @project_id;
            """, connection, transaction))
        {
            installmentCommand.Parameters.AddWithValue("project_id", projectId);
            installmentNumber = Convert.ToInt32(await installmentCommand.ExecuteScalarAsync() ?? 1);
        }

        return new InvoiceBillingIdentity(
            seriesNumber.Value,
            installmentNumber,
            $"PHD-{seriesNumber.Value:000000}-{installmentNumber}");
    }

    private static async Task InsertInvoiceHeaderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId,
        InvoiceBillingIdentity identity,
        InvoiceBillingProjectRow project,
        string invoiceType,
        DateOnly billingPeriodStart,
        DateOnly billingPeriodEnd,
        decimal subtotal,
        string notes,
        Guid actorUserId,
        string immutableSnapshot)
    {
        const string sql = """
            INSERT INTO billing_invoices (
                billing_invoice_id,
                invoice_series_number,
                invoice_installment_number,
                invoice_number,
                project_id,
                client_id,
                invoice_type,
                invoice_status,
                billing_period_start,
                billing_period_end,
                invoice_date,
                customer_name_snapshot,
                project_code_snapshot,
                project_name_snapshot,
                contract_type_snapshot,
                project_manager_name_snapshot,
                project_coordinator_name_snapshot,
                purchase_order_id,
                purchase_order_number_snapshot,
                purchase_order_amount_snapshot,
                certinia_id_snapshot,
                salesforce_id_snapshot,
                sell_quote_snapshot,
                subtotal_amount,
                adjustment_amount,
                tax_amount,
                total_amount,
                invoice_notes,
                billing_instructions_snapshot,
                created_by_user_id,
                finalized_by_user_id,
                finalized_at,
                immutable_snapshot_json
            )
            SELECT
                @invoice_id,
                @series_number,
                @installment_number,
                @invoice_number,
                @project_id,
                @client_id,
                @invoice_type,
                'finalized',
                @billing_period_start,
                @billing_period_end,
                CURRENT_DATE,
                @customer_name,
                @project_code,
                @project_name,
                @contract_type,
                @project_manager_name,
                @project_coordinator_name,
                @purchase_order_id,
                @purchase_order_number,
                @purchase_order_amount,
                @certinia_id,
                @salesforce_id,
                @sell_quote,
                @subtotal,
                0,
                0,
                @subtotal,
                @notes,
                COALESCE(profile.billing_instructions, ''),
                @actor_user_id,
                @actor_user_id,
                NOW(),
                @immutable_snapshot::jsonb
            FROM (SELECT 1) source
            LEFT JOIN project_billing_profiles profile
                ON profile.project_id = @project_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("series_number", identity.SeriesNumber);
        command.Parameters.AddWithValue("installment_number", identity.InstallmentNumber);
        command.Parameters.AddWithValue("invoice_number", identity.InvoiceNumber);
        command.Parameters.AddWithValue("project_id", project.ProjectId);
        command.Parameters.Add("client_id", NpgsqlDbType.Uuid).Value =
            project.ClientId is null ? DBNull.Value : project.ClientId.Value;
        command.Parameters.AddWithValue("invoice_type", invoiceType);
        command.Parameters.AddWithValue("billing_period_start", billingPeriodStart);
        command.Parameters.AddWithValue("billing_period_end", billingPeriodEnd);
        command.Parameters.AddWithValue("customer_name", project.CustomerName);
        command.Parameters.AddWithValue("project_code", project.ProjectCode);
        command.Parameters.AddWithValue("project_name", project.ProjectName);
        command.Parameters.AddWithValue("contract_type", project.ContractType);
        command.Parameters.AddWithValue("project_manager_name", project.ProjectManagerName);
        command.Parameters.AddWithValue("project_coordinator_name", project.ProjectCoordinatorName);
        command.Parameters.Add("purchase_order_id", NpgsqlDbType.Uuid).Value =
            project.PurchaseOrder is null ? DBNull.Value : project.PurchaseOrder.PurchaseOrderId;
        command.Parameters.AddWithValue("purchase_order_number", project.PurchaseOrder?.PoNumber ?? string.Empty);
        command.Parameters.Add("purchase_order_amount", NpgsqlDbType.Numeric).Value =
            project.PurchaseOrder?.AuthorizedAmount is decimal purchaseOrderAmount
                ? purchaseOrderAmount
                : DBNull.Value;
        command.Parameters.AddWithValue("certinia_id", project.CertiniaId);
        command.Parameters.AddWithValue("salesforce_id", project.SalesforceId);
        command.Parameters.AddWithValue("sell_quote", project.SellQuoteNumber);
        command.Parameters.AddWithValue("subtotal", subtotal);
        command.Parameters.AddWithValue("notes", notes);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("immutable_snapshot", immutableSnapshot);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertInvoiceLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId,
        int lineNumber,
        InvoiceBillingResolvedLine line)
    {
        const string sql = """
            INSERT INTO billing_invoice_lines (
                billing_invoice_id,
                line_number,
                source_type,
                time_entry_id,
                task_id,
                resource_user_id,
                work_date,
                resource_name_snapshot,
                resource_email_snapshot,
                task_code_snapshot,
                task_name_snapshot,
                customer_facing_description,
                internal_description,
                time_type,
                labor_category,
                approved_hours,
                rate_card_id,
                rate_line_id,
                rate_code_snapshot,
                rate_description_snapshot,
                unit_rate,
                line_amount,
                manager_approval_snapshot,
                project_approval_snapshot,
                accounting_readiness_snapshot,
                source_snapshot_json
            )
            VALUES (
                @invoice_id,
                @line_number,
                'time_entry',
                @time_entry_id,
                @task_id,
                @resource_user_id,
                @work_date,
                @resource_name,
                @resource_email,
                @task_code,
                @task_name,
                @description,
                @description,
                @time_type,
                @labor_category,
                @approved_hours,
                @rate_card_id,
                @rate_line_id,
                @rate_code,
                @rate_description,
                @unit_rate,
                @line_amount,
                @approval_status,
                '',
                @accounting_readiness,
                @source_snapshot::jsonb
            );
            """;

        var sourceSnapshot = JsonSerializer.Serialize(new
        {
            line.TimeEntryId,
            line.WorkDate,
            line.ResourceUserId,
            line.TaskId,
            line.ApprovalStatus,
            line.RateCardId,
            line.RateLineId,
            capturedAt = DateTimeOffset.UtcNow
        });

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("line_number", lineNumber);
        command.Parameters.AddWithValue("time_entry_id", line.TimeEntryId);
        command.Parameters.Add("task_id", NpgsqlDbType.Uuid).Value =
            line.TaskId is null ? DBNull.Value : line.TaskId.Value;
        command.Parameters.AddWithValue("resource_user_id", line.ResourceUserId);
        command.Parameters.AddWithValue("work_date", line.WorkDate);
        command.Parameters.AddWithValue("resource_name", line.ResourceName);
        command.Parameters.AddWithValue("resource_email", line.ResourceEmail);
        command.Parameters.AddWithValue("task_code", line.TaskCode);
        command.Parameters.AddWithValue("task_name", line.TaskName);
        command.Parameters.AddWithValue("description", line.Description);
        command.Parameters.AddWithValue("time_type", line.TimeType);
        command.Parameters.AddWithValue("labor_category", line.LaborCategory);
        command.Parameters.AddWithValue("approved_hours", line.ApprovedHours);
        command.Parameters.AddWithValue("rate_card_id", line.RateCardId);
        command.Parameters.AddWithValue("rate_line_id", line.RateLineId);
        command.Parameters.AddWithValue("rate_code", line.RateCode);
        command.Parameters.AddWithValue("rate_description", line.RateDescription);
        command.Parameters.AddWithValue("unit_rate", line.UnitRate);
        command.Parameters.AddWithValue("line_amount", line.LineAmount);
        command.Parameters.AddWithValue("approval_status", line.ApprovalStatus);
        command.Parameters.AddWithValue(
            "accounting_readiness",
            line.ApprovalStatus is "accounting_ready" or "reconciled" or "locked"
                ? line.ApprovalStatus
                : "manager_approved");
        command.Parameters.AddWithValue("source_snapshot", sourceSnapshot);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertNonLaborInvoiceLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId,
        int lineNumber,
        InvoiceBillingResolvedNonLaborLine line)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO billing_invoice_lines (
                billing_invoice_id,
                line_number,
                source_type,
                billing_readiness_review_id,
                work_date,
                customer_facing_description,
                internal_description,
                time_type,
                labor_category,
                approved_hours,
                rate_code_snapshot,
                rate_description_snapshot,
                unit_rate,
                line_amount,
                manager_approval_snapshot,
                project_approval_snapshot,
                accounting_readiness_snapshot,
                source_snapshot_json
            )
            VALUES (
                @invoice_id,
                @line_number,
                @source_type,
                @review_id,
                @work_date,
                @description,
                @internal_description,
                'non_labor',
                @source_type,
                1,
                @rate_code,
                @rate_description,
                @amount,
                @amount,
                'not_applicable',
                'ready',
                'ready',
                @source_snapshot::jsonb
            );
            """, connection, transaction);

        var sourceSnapshot = JsonSerializer.Serialize(new
        {
            line.ReadinessReviewId,
            line.BillingPeriodStart,
            line.BillingPeriodEnd,
            line.PackageType,
            line.SourceType,
            line.Description,
            line.LineAmount,
            capturedAt = DateTimeOffset.UtcNow
        });
        var rateCode = line.SourceType == "fixed_price_milestone"
            ? "MILESTONE"
            : "EXPENSE";

        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("line_number", lineNumber);
        command.Parameters.AddWithValue("source_type", line.SourceType);
        command.Parameters.AddWithValue("review_id", line.ReadinessReviewId);
        command.Parameters.Add("work_date", NpgsqlDbType.Date).Value = line.BillingPeriodEnd;
        command.Parameters.AddWithValue("description", line.Description);
        command.Parameters.AddWithValue("internal_description", line.PackageType);
        command.Parameters.AddWithValue("rate_code", rateCode);
        command.Parameters.AddWithValue("rate_description", line.PackageType);
        command.Parameters.AddWithValue("amount", line.LineAmount);
        command.Parameters.Add("source_snapshot", NpgsqlDbType.Jsonb).Value = sourceSnapshot;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertInvoiceEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid invoiceId,
        Guid actorUserId,
        string invoiceType,
        string invoiceNumber,
        int lineCount,
        decimal totalAmount)
    {
        const string sql = """
            INSERT INTO billing_invoice_events (
                billing_invoice_id,
                event_type,
                prior_status,
                new_status,
                actor_user_id,
                event_reason,
                event_json
            )
            VALUES (
                @invoice_id,
                'invoice_created',
                '',
                'finalized',
                @actor_user_id,
                @event_reason,
                @event_json::jsonb
            );
            """;

        var reason = $"{invoiceType} invoice {invoiceNumber} created from {lineCount} approved time entries.";
        var eventJson = JsonSerializer.Serialize(new
        {
            invoiceNumber,
            invoiceType,
            lineCount,
            totalAmount
        });

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("invoice_id", invoiceId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("event_reason", reason);
        command.Parameters.AddWithValue("event_json", eventJson);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> LoadInvoiceProjectIdAsync(
        NpgsqlConnection connection,
        Guid invoiceId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT project_id
            FROM billing_invoices
            WHERE billing_invoice_id = @invoice_id;
            """, connection);

        command.Parameters.AddWithValue("invoice_id", invoiceId);
        var result = await command.ExecuteScalarAsync();

        return result is Guid value ? value : null;
    }

    private static async Task<InvoiceBillingInvoiceDetail?> LoadInvoiceDetailAsync(
        NpgsqlConnection connection,
        Guid invoiceId)
    {
        InvoiceBillingInvoiceHeader? header = null;

        await using (var headerCommand = new NpgsqlCommand("""
            SELECT
                billing_invoice_id,
                invoice_number,
                project_id,
                invoice_type,
                invoice_status,
                billing_period_start,
                billing_period_end,
                invoice_date,
                customer_name_snapshot,
                project_code_snapshot,
                project_name_snapshot,
                contract_type_snapshot,
                project_manager_name_snapshot,
                project_coordinator_name_snapshot,
                purchase_order_number_snapshot,
                purchase_order_amount_snapshot,
                certinia_id_snapshot,
                salesforce_id_snapshot,
                sell_quote_snapshot,
                subtotal_amount,
                adjustment_amount,
                tax_amount,
                total_amount,
                invoice_notes,
                created_at,
                finalized_at
            FROM billing_invoices
            WHERE billing_invoice_id = @invoice_id;
            """, connection))
        {
            headerCommand.Parameters.AddWithValue("invoice_id", invoiceId);
            await using var reader = await headerCommand.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                header = new InvoiceBillingInvoiceHeader(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    ReadDateOnlyOrNull(reader, 5),
                    ReadDateOnlyOrNull(reader, 6),
                    ReadDateOnlyOrNull(reader, 7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(18),
                    reader.GetDecimal(19),
                    reader.GetDecimal(20),
                    reader.GetDecimal(21),
                    reader.GetDecimal(22),
                    reader.GetString(23),
                    ReadDateTimeOffset(reader, 24),
                    reader.IsDBNull(25) ? null : ReadDateTimeOffset(reader, 25));
            }
        }

        if (header is null) return null;

        var lines = new List<InvoiceBillingInvoiceLineDetail>();

        await using (var lineCommand = new NpgsqlCommand("""
            SELECT
                billing_invoice_line_id,
                line_number,
                time_entry_id,
                work_date,
                resource_name_snapshot,
                resource_email_snapshot,
                task_code_snapshot,
                task_name_snapshot,
                customer_facing_description,
                time_type,
                labor_category,
                approved_hours,
                rate_code_snapshot,
                rate_description_snapshot,
                unit_rate,
                line_amount,
                manager_approval_snapshot,
                accounting_readiness_snapshot
            FROM billing_invoice_lines
            WHERE billing_invoice_id = @invoice_id
            ORDER BY line_number;
            """, connection))
        {
            lineCommand.Parameters.AddWithValue("invoice_id", invoiceId);
            await using var reader = await lineCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lines.Add(new InvoiceBillingInvoiceLineDetail(
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    ReadDateOnlyOrNull(reader, 3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetDecimal(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetDecimal(14),
                    reader.GetDecimal(15),
                    reader.GetString(16),
                    reader.GetString(17)));
            }
        }

        var events = new List<InvoiceBillingInvoiceEventDetail>();

        await using (var eventCommand = new NpgsqlCommand("""
            SELECT
                billing_invoice_event_id,
                event_type,
                prior_status,
                new_status,
                event_reason,
                created_at
            FROM billing_invoice_events
            WHERE billing_invoice_id = @invoice_id
            ORDER BY created_at;
            """, connection))
        {
            eventCommand.Parameters.AddWithValue("invoice_id", invoiceId);
            await using var reader = await eventCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                events.Add(new InvoiceBillingInvoiceEventDetail(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    ReadDateTimeOffset(reader, 5)));
            }
        }

        return new InvoiceBillingInvoiceDetail(header, lines, events);
    }

    private static async Task<InvoiceBillingAccessContext> LoadAccessContextAsync(
        NpgsqlConnection connection,
        Guid userId,
        NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT
                user_row.user_id,
                COALESCE(user_row.email, '') AS email,
                COALESCE(
                    array_agg(DISTINCT role.role_code)
                    FILTER (WHERE role.role_code IS NOT NULL),
                    ARRAY[]::text[]
                ) AS role_codes
            FROM app_users user_row
            LEFT JOIN app_user_role_assignments assignment
                ON assignment.user_id = user_row.user_id
               AND assignment.is_active = TRUE
            LEFT JOIN app_roles role
                ON role.app_role_id = assignment.app_role_id
               AND role.is_active = TRUE
            WHERE user_row.user_id = @user_id
              AND user_row.is_active = TRUE
            GROUP BY user_row.user_id, user_row.email;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return InvoiceBillingAccessContext.Empty(userId);
        }

        var roles = reader.GetFieldValue<string[]>(2)
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new InvoiceBillingAccessContext(
            reader.GetGuid(0),
            reader.GetString(1),
            roles);
    }

    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        InvoiceBillingAccessContext access,
        Guid projectId,
        NpgsqlTransaction? transaction = null)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM projects project
                WHERE project.project_id = @project_id
                  AND (
                      @is_broad_scope = TRUE
                      OR project.project_manager_user_id = @user_id
                      OR project.project_coordinator_user_id = @user_id
                      OR EXISTS (
                          SELECT 1
                          FROM project_assignments assignment
                          WHERE assignment.project_id = project.project_id
                            AND assignment.user_id = @user_id
                      )
                  )
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("is_broad_scope", access.IsBroadScope);
        command.Parameters.AddWithValue("user_id", access.UserId);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static Guid? GetSessionUserId(HttpContext httpContext)
    {
        return httpContext.Items.TryGetValue("ProjectPulseSessionUserId", out var value)
            && value is Guid userId
                ? userId
                : null;
    }

    private static IResult SessionRequired()
    {
        return Results.Json(new
        {
            status = "session_required",
            message = "A valid ProjectPulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static IResult? ValidateConfig(InvoiceBillingDatabaseConfig config)
    {
        if (config.Missing.Count == 0) return null;

        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }

    private static bool IsFixedPrice(string contractType)
    {
        var normalized = Clean(contractType)
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("fixed", StringComparison.Ordinal)
            && normalized.Contains("price", StringComparison.Ordinal);
    }

    private static string Clean(string? value)
    {
        return (value ?? string.Empty).Trim();
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

    private static DateOnly? ReadDateOnlyOrNull(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ReadDateOnly(reader, ordinal);
    }

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);

        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static async Task SafeRollbackAsync(NpgsqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // The original database exception is more useful than a rollback error.
        }
    }
}

internal sealed record InvoiceBillingCreateInvoiceRequest(
    string? InvoiceType,
    List<InvoiceBillingCreateInvoiceLineRequest>? Lines,
    List<Guid>? BillingReadinessReviewIds,
    string? Notes);

internal sealed record InvoiceBillingCreateInvoiceLineRequest(
    Guid TimeEntryId,
    Guid RateLineId);

internal sealed record InvoiceBillingAccessContext(
    Guid UserId,
    string Email,
    IReadOnlySet<string> RoleCodes)
{
    public static InvoiceBillingAccessContext Empty(Guid userId)
    {
        return new InvoiceBillingAccessContext(
            userId,
            string.Empty,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    public bool IsBroadScope => RoleCodes.Any(InvoiceBillingModuleRolePolicy.IsBroadReadRole);
    public bool CanViewBilling => RoleCodes.Count > 0;
    public bool CanCreateInvoices => RoleCodes.Any(InvoiceBillingModuleRolePolicy.IsInvoiceWriteRole);

    public string ScopeLabel => IsBroadScope
        ? "billing_operations_scope"
        : "assigned_or_managed_project_scope";
}

internal static class InvoiceBillingModuleRolePolicy
{
    private static readonly HashSet<string> BroadReadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING",
        "ACCOUNTING_BILLING",
        "BILLING",
        "FINANCE",
        "EXECUTIVE"
    };

    private static readonly HashSet<string> InvoiceWriteRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "ACCOUNTING",
        "ACCOUNTING_BILLING",
        "BILLING",
        "FINANCE",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD"
    };

    public static bool IsBroadReadRole(string roleCode) => BroadReadRoles.Contains(roleCode);
    public static bool IsInvoiceWriteRole(string roleCode) => InvoiceWriteRoles.Contains(roleCode);
}

internal sealed record InvoiceBillingProjectRow(
    Guid ProjectId,
    Guid? ClientId,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string ContractType,
    string Status,
    string ProjectManagerName,
    string ProjectCoordinatorName,
    string CertiniaId,
    string SellQuoteNumber,
    string SalesforceId,
    bool PurchaseOrderRequired,
    InvoiceBillingPurchaseOrder? PurchaseOrder);

internal sealed record InvoiceBillingPurchaseOrder(
    Guid PurchaseOrderId,
    string PoNumber,
    decimal? AuthorizedAmount,
    DateOnly? EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    string CustomerReference);

internal sealed record InvoiceBillingCandidate(
    Guid ProjectId,
    Guid? ClientId,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string WorkType,
    string ContractType,
    string Status,
    string ProjectManagerName,
    string ProjectCoordinatorName,
    IReadOnlyList<string> AssignedEngineers,
    string CertiniaId,
    string SellQuoteNumber,
    string SalesforceId,
    bool PurchaseOrderRequired,
    InvoiceBillingPurchaseOrder? PurchaseOrder,
    IReadOnlyList<InvoiceBillingCandidateLine> Lines,
    IReadOnlyList<InvoiceBillingNonLaborCandidateLine> NonLaborLines,
    int ApprovedLineCount,
    int ReadyNonLaborLineCount,
    decimal ApprovedHours,
    decimal? AutoCalculatedAmount,
    string RateResolutionStatus,
    IReadOnlyList<string> Blockers,
    bool CanCreateInvoice,
    bool CurrentUserCanCreateInvoices,
    IReadOnlyList<InvoiceBillingInvoiceSummary> InvoiceHistory,
    SellCommercialProjectSummary Commercial);

internal sealed record InvoiceBillingCandidateLineSource(
    Guid TimeEntryId,
    DateOnly WorkDate,
    Guid ResourceUserId,
    string ResourceName,
    string ResourceEmail,
    Guid? TaskId,
    string TaskCode,
    string TaskName,
    string Description,
    decimal ApprovedHours,
    string TimeType,
    string ApprovalStatus);

internal sealed record InvoiceBillingCandidateLine(
    Guid TimeEntryId,
    DateOnly WorkDate,
    Guid ResourceUserId,
    string ResourceName,
    string ResourceEmail,
    Guid? TaskId,
    string TaskCode,
    string TaskName,
    string Description,
    decimal ApprovedHours,
    string TimeType,
    string ApprovalStatus,
    IReadOnlyList<InvoiceBillingRateOption> RateOptions,
    Guid? SuggestedRateLineId,
    string RateBlocker);

internal sealed record InvoiceBillingNonLaborCandidateLine(
    Guid ReadinessReviewId,
    DateOnly BillingPeriodStart,
    DateOnly BillingPeriodEnd,
    string PackageType,
    string SourceType,
    string Description,
    decimal Amount,
    string ReviewedBy,
    DateTimeOffset UpdatedAt);

internal sealed record InvoiceBillingRateOption(
    Guid RateLineId,
    Guid RateCardId,
    string RateCardCode,
    string RateCardName,
    string SkuCode,
    string DisplayName,
    string Description,
    string LaborCategory,
    string TimeType,
    string UnitType,
    decimal UnitRate,
    bool IsProjectDefault,
    bool IsCustomerRate);

internal sealed record InvoiceBillingInvoiceSummary(
    Guid BillingInvoiceId,
    string InvoiceNumber,
    string InvoiceType,
    string InvoiceStatus,
    DateOnly? BillingPeriodStart,
    DateOnly? BillingPeriodEnd,
    DateOnly? InvoiceDate,
    decimal SubtotalAmount,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt,
    int LineCount);

internal sealed record InvoiceBillingConnectorStatus(
    string SystemCode,
    string DisplayName,
    string EnvironmentName,
    string ConnectionStatus,
    bool InboundEnabled,
    bool OutboundEnabled,
    string LastConnectionTestStatus,
    DateTimeOffset? LastConnectionTestAt,
    DateTimeOffset? LastSuccessfulSyncAt);

internal sealed record InvoiceBillingResolvedLine(
    Guid TimeEntryId,
    DateOnly WorkDate,
    Guid ResourceUserId,
    string ResourceName,
    string ResourceEmail,
    Guid? TaskId,
    string TaskCode,
    string TaskName,
    string Description,
    decimal ApprovedHours,
    string TimeType,
    string ApprovalStatus,
    Guid RateLineId,
    Guid RateCardId,
    string RateCardCode,
    string RateCardName,
    string RateCode,
    string RateDisplayName,
    string RateDescription,
    string LaborCategory,
    decimal UnitRate,
    decimal LineAmount);

internal sealed record InvoiceBillingResolvedNonLaborLine(
    Guid ReadinessReviewId,
    DateOnly BillingPeriodStart,
    DateOnly BillingPeriodEnd,
    string PackageType,
    string SourceType,
    string Description,
    decimal LineAmount);

internal sealed record InvoiceBillingIdentity(
    long SeriesNumber,
    int InstallmentNumber,
    string InvoiceNumber);

internal sealed record InvoiceBillingInvoiceHeader(
    Guid BillingInvoiceId,
    string InvoiceNumber,
    Guid ProjectId,
    string InvoiceType,
    string InvoiceStatus,
    DateOnly? BillingPeriodStart,
    DateOnly? BillingPeriodEnd,
    DateOnly? InvoiceDate,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string ContractType,
    string ProjectManagerName,
    string ProjectCoordinatorName,
    string PurchaseOrderNumber,
    decimal? PurchaseOrderAmount,
    string CertiniaId,
    string SalesforceId,
    string SellQuote,
    decimal SubtotalAmount,
    decimal AdjustmentAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt);

internal sealed record InvoiceBillingInvoiceLineDetail(
    Guid BillingInvoiceLineId,
    int LineNumber,
    Guid? TimeEntryId,
    DateOnly? WorkDate,
    string ResourceName,
    string ResourceEmail,
    string TaskCode,
    string TaskName,
    string Description,
    string TimeType,
    string LaborCategory,
    decimal ApprovedHours,
    string RateCode,
    string RateDescription,
    decimal UnitRate,
    decimal LineAmount,
    string ManagerApprovalSnapshot,
    string AccountingReadinessSnapshot);

internal sealed record InvoiceBillingInvoiceEventDetail(
    Guid BillingInvoiceEventId,
    string EventType,
    string PriorStatus,
    string NewStatus,
    string EventReason,
    DateTimeOffset CreatedAt);

internal sealed record InvoiceBillingInvoiceDetail(
    InvoiceBillingInvoiceHeader Header,
    IReadOnlyList<InvoiceBillingInvoiceLineDetail> Lines,
    IReadOnlyList<InvoiceBillingInvoiceEventDetail> Events);

internal sealed record InvoiceBillingDatabaseConfig(
    string ConnectionString,
    IReadOnlyList<string> Missing)
{
    public static InvoiceBillingDatabaseConfig FromEnvironment()
    {
        var direct = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
            Environment.GetEnvironmentVariable("ConnectionStrings__ProjectPulse"),
            Environment.GetEnvironmentVariable("ConnectionStrings__ProjectTime"));

        if (!string.IsNullOrWhiteSpace(direct))
        {
            return new InvoiceBillingDatabaseConfig(direct, []);
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(host)) missing.Add("PTP_DB_HOST");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("PTP_DB_NAME");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("PTP_DB_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("PTP_DB_PASSWORD");

        if (missing.Count > 0)
        {
            return new InvoiceBillingDatabaseConfig(string.Empty, missing);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host!,
            Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
            Database = database!,
            Username = username!,
            Password = password!,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 10
        };

        return new InvoiceBillingDatabaseConfig(builder.ConnectionString, []);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;
    }
}
