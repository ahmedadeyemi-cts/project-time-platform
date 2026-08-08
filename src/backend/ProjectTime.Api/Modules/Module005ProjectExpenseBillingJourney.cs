using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Read-only enterprise billing-journey projection spanning Modules 055C, 005,
/// 039, 042, 040, and 030. The projection does not create or revise financial
/// evidence. It reports the current governed state and combines existing
/// append-only audit sources for non-technical users.
/// </summary>
public static partial class Module005ProjectExpenseUploadModule
{
    private const string BillingJourneyContractVersion = "2026-08-08.1";

    private static readonly string[] BillingJourneyApprovedTimeStatuses =
    [
        "pm_approved",
        "manager_approved",
        "project_approved",
        "project_validated",
        "accounting_ready",
        "reconciled",
        "locked"
    ];

    private static async Task<bool> TryHandleBillingJourneyRequestAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;

        var path = context.Request.Path.Value ?? string.Empty;
        IResult? result = null;

        if (path.Equals("/api/billing-journey/analytics", StringComparison.OrdinalIgnoreCase))
        {
            result = await GetBillingJourneyAnalyticsAsync(context);
        }
        else
        {
            const string prefix = "/api/billing-journey/projects/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(path[prefix.Length..].Trim('/'), out var projectId))
            {
                result = await GetBillingJourneyProjectAsync(projectId, context);
            }
        }

        if (result is null) return false;

        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["X-ProjectPulse-Billing-Journey"] = BillingJourneyContractVersion;
        await result.ExecuteAsync(context);
        return true;
    }

    private static async Task<IResult> GetBillingJourneyProjectAsync(
        Guid projectId,
        HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (!HasRole(actor, SelfRoles) && !HasRole(actor, BillingRoles))
            return AccessDenied("The current role cannot view the governed billing journey.");

        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var project = projects.FirstOrDefault(item => item.ProjectId == projectId);
        if (project is null)
            return AccessDenied("The selected project is outside the current role scope.");

        if (!actor.IsViewAs)
        {
            await BlockStaleExpenseReadinessAsync(
                connection,
                null,
                context.RequestAborted);
        }

        var expenseSnapshot = await LoadCurrentExpenseSnapshotAsync(
            connection,
            null,
            projectId,
            context.RequestAborted);
        var acknowledgement = await LoadExpenseAcknowledgementAsync(
            connection,
            null,
            projectId,
            context.RequestAborted);
        var metrics = await LoadBillingJourneyProjectMetricsAsync(
            connection,
            projectId,
            expenseSnapshot.CurrentUploadCount,
            context.RequestAborted);
        var activity = await LoadBillingJourneyActivityAsync(
            connection,
            projectId,
            context.RequestAborted);

        var treatment = BillingTreatment(project.ContractType);
        var expectedPackage = PackageType(treatment);
        var expectedExpenseStatus = treatment == "pass_through_invoice" ? "ready" : "draft";
        var expectedExpenseAmount = treatment == "pass_through_invoice"
            ? expenseSnapshot.ReimbursableAmount
            : expenseSnapshot.TotalAmount;
        var acknowledgementCurrent = expenseSnapshot.CurrentUploadCount > 0
            && acknowledgement is not null
            && acknowledgement.PackageType.Equals(expectedPackage, StringComparison.OrdinalIgnoreCase)
            && acknowledgement.ReviewStatus.Equals(expectedExpenseStatus, StringComparison.OrdinalIgnoreCase)
            && acknowledgement.EvidenceAmount == expectedExpenseAmount
            && acknowledgement.UpdatedAt >= expenseSnapshot.LatestUploadAt;

        var projectBlockers = new List<string>();
        if (string.IsNullOrWhiteSpace(project.CustomerName)) projectBlockers.Add("Customer is not linked.");
        if (string.IsNullOrWhiteSpace(project.ProjectCode)) projectBlockers.Add("Project code is missing.");
        if (string.IsNullOrWhiteSpace(project.ProjectName)) projectBlockers.Add("Project name is missing.");
        if (string.IsNullOrWhiteSpace(project.ContractType)) projectBlockers.Add("Contract type is missing.");
        if (metrics.PurchaseOrderRequired && !metrics.PurchaseOrderReady)
            projectBlockers.Add("A primary active purchase order is required.");

        var expenseState = expenseSnapshot.CurrentUploadCount == 0
            ? "not_required"
            : metrics.UnacceptedExpenseUploadCount > 0 || !acknowledgementCurrent
                ? "attention"
                : "complete";
        var readinessState = metrics.BlockedReadinessPackageCount > 0
            ? "attention"
            : metrics.ReadyReadinessPackageCount > 0
                ? "complete"
                : "pending";
        var invoiceState = metrics.FinalInvoiceCount > 0
            ? "complete"
            : metrics.PartialInvoiceCount > 0
                ? "in_progress"
                : "pending";
        var closeoutState = metrics.CloseoutStatus.Equals("closed", StringComparison.OrdinalIgnoreCase)
            ? "complete"
            : metrics.FinalInvoiceCount > 0
                ? "attention"
                : "locked";

        var blockers = new List<string>(projectBlockers);
        if (expenseSnapshot.CurrentUploadCount > 0 && metrics.UnacceptedExpenseUploadCount > 0)
            blockers.Add($"{metrics.UnacceptedExpenseUploadCount} current expense version(s) still require Project Manager acceptance.");
        if (expenseSnapshot.CurrentUploadCount > 0 && !acknowledgementCurrent)
            blockers.Add("Current Module 005 expense evidence requires a billing-treatment acknowledgement.");
        if (metrics.BlockedReadinessPackageCount > 0)
            blockers.Add($"{metrics.BlockedReadinessPackageCount} billing-readiness package(s) are blocked.");
        if (metrics.ReadyReadinessPackageCount == 0)
            blockers.Add("No billing-readiness package is currently marked ready.");
        if (metrics.PendingBillableTimeCount > 0)
            blockers.Add($"{metrics.PendingBillableTimeCount} billable time entr{(metrics.PendingBillableTimeCount == 1 ? "y requires" : "ies require")} approval or disposition.");
        if (metrics.FinalInvoiceCount == 0 && metrics.PartialInvoiceCount > 0
            && metrics.RemainingEligibleSourceCount == 0)
            blockers.Add("The prior partial invoice cycle is complete; prepare the next readiness period or final billing package.");

        var recommendedRoute = "reporting";
        var recommendedModule = "030";
        var recommendedAction = "Review invoice analytics";

        if (projectBlockers.Count > 0)
        {
            recommendedRoute = "work-register";
            recommendedModule = "055C";
            recommendedAction = "Complete project billing setup";
        }
        else if (expenseState == "attention")
        {
            recommendedRoute = "project-allocation-info";
            recommendedModule = "005";
            recommendedAction = "Resolve expense evidence";
        }
        else if (readinessState != "complete")
        {
            recommendedRoute = "billing-readiness";
            recommendedModule = "039";
            recommendedAction = "Complete billing readiness";
        }
        else if (metrics.FinalInvoiceCount == 0
            && (metrics.InvoiceCount == 0 || metrics.RemainingEligibleSourceCount > 0))
        {
            recommendedRoute = "invoice-billing-center";
            recommendedModule = "042";
            recommendedAction = metrics.PartialInvoiceCount > 0
                ? "Continue partial or final billing"
                : "Create the first invoice";
        }
        else if (metrics.FinalInvoiceCount == 0 && metrics.PartialInvoiceCount > 0)
        {
            recommendedRoute = "billing-readiness";
            recommendedModule = "039";
            recommendedAction = "Prepare the next or final billing period";
        }
        else if (!metrics.CloseoutStatus.Equals("closed", StringComparison.OrdinalIgnoreCase))
        {
            recommendedRoute = "project-closeout";
            recommendedModule = "040";
            recommendedAction = "Complete project closeout";
        }

        var stages = new object[]
        {
            new
            {
                key = "project",
                module = "055C",
                route = "work-register",
                label = "Project record",
                state = projectBlockers.Count == 0 ? "complete" : "attention",
                detail = projectBlockers.Count == 0
                    ? "Customer, project, contract, ownership, and purchase-order context are available."
                    : string.Join(" ", projectBlockers)
            },
            new
            {
                key = "expenses",
                module = "005",
                route = "project-allocation-info",
                label = "Expenses",
                state = expenseState,
                detail = expenseSnapshot.CurrentUploadCount == 0
                    ? "No current expense upload requires billing treatment."
                    : acknowledgementCurrent
                        ? $"{expenseSnapshot.CurrentUploadCount} current expense upload(s) are accepted and acknowledged."
                        : "Current expense versions require acceptance or billing-treatment acknowledgement."
            },
            new
            {
                key = "readiness",
                module = "039",
                route = "billing-readiness",
                label = "Billing readiness",
                state = readinessState,
                detail = $"{metrics.ReadyReadinessPackageCount} ready, {metrics.BlockedReadinessPackageCount} blocked, {metrics.DraftReadinessPackageCount} draft package(s)."
            },
            new
            {
                key = "invoice",
                module = "042",
                route = "invoice-billing-center",
                label = "Invoice",
                state = invoiceState,
                detail = $"{metrics.PartialInvoiceCount} partial and {metrics.FinalInvoiceCount} final non-void invoice(s); {metrics.RemainingEligibleSourceCount} eligible source(s) remain."
            },
            new
            {
                key = "closeout",
                module = "040",
                route = "project-closeout",
                label = "Closeout",
                state = closeoutState,
                detail = metrics.FinalInvoiceCount == 0
                    ? "Final billing or an approved no-further-billing disposition is required before closeout."
                    : $"Closeout status: {metrics.CloseoutStatus.Replace('_', ' ')}."
            },
            new
            {
                key = "analytics",
                module = "030",
                route = "reporting",
                label = "Analytics",
                state = metrics.InvoiceCount > 0 ? "available" : "pending",
                detail = metrics.InvoiceCount > 0
                    ? "Partial and final invoice reporting is available in the Analytics Center."
                    : "Invoice reporting becomes populated after the first immutable invoice is created."
            }
        };

        return Results.Ok(new
        {
            status = "billing_journey_loaded",
            contractVersion = BillingJourneyContractVersion,
            modules = new[] { "055C", "005", "039", "042", "040", "030" },
            project = new
            {
                project.ProjectId,
                project.CustomerName,
                project.ProjectCode,
                project.ProjectName,
                project.ContractType,
                project.Status,
                project.ProjectManagerUserId,
                project.ProjectManagerName,
                billingTreatment = treatment
            },
            actor = new
            {
                actor.ActualUserId,
                actor.EffectiveUserId,
                actor.RoleCodes,
                actor.IsViewAs,
                readOnly = actor.IsViewAs
            },
            billingMode = metrics.FinalInvoiceCount > 0
                ? "final_complete"
                : metrics.PartialInvoiceCount > 0
                    ? "partial_cycle"
                    : "not_started",
            stages,
            summary = new
            {
                currentExpenseUploadCount = expenseSnapshot.CurrentUploadCount,
                trackedExpenseAmount = expenseSnapshot.TotalAmount,
                invoiceEligibleExpenseAmount = treatment == "pass_through_invoice"
                    ? expenseSnapshot.ReimbursableAmount
                    : 0m,
                metrics.UnacceptedExpenseUploadCount,
                expenseAcknowledgementCurrent = acknowledgementCurrent,
                metrics.ReadyReadinessPackageCount,
                metrics.BlockedReadinessPackageCount,
                metrics.DraftReadinessPackageCount,
                metrics.EligibleUninvoicedTimeCount,
                metrics.PendingBillableTimeCount,
                metrics.ReadyUninvoicedNonLaborCount,
                remainingEligibleSourceCount = metrics.RemainingEligibleSourceCount,
                metrics.InvoiceCount,
                metrics.PartialInvoiceCount,
                metrics.FinalInvoiceCount,
                metrics.InvoicedAmount,
                metrics.LatestInvoiceNumber,
                metrics.LatestInvoiceType,
                metrics.LatestInvoiceStatus,
                metrics.LastInvoiceAt,
                metrics.CloseoutStatus,
                metrics.BillingDisposition,
                metrics.ClosedAt
            },
            recommended = new
            {
                module = recommendedModule,
                route = recommendedRoute,
                action = recommendedAction
            },
            blockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            activity,
            immutability = new
            {
                projectExpenseVersions = "Accepted expense versions cannot be deleted or replaced.",
                invoiceSnapshots = "Invoice headers, lines, source references, and totals are persisted as immutable invoice evidence.",
                lifecycleAudit = "Work-to-Cash and expense audit events are append-only and cannot be updated or deleted.",
                sourceRecords = "Invoice creation does not rewrite approved time or expense source evidence."
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetBillingJourneyAnalyticsAsync(HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (!HasRole(actor, SelfRoles) && !HasRole(actor, BillingRoles))
            return AccessDenied("The current role cannot view invoice analytics.");

        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var requestedProjectId = BillingJourneyGuidQuery(context, "projectId");
        if (requestedProjectId.HasValue && !projectMap.ContainsKey(requestedProjectId.Value))
            return AccessDenied("The selected analytics project is outside the current role scope.");

        var scopedProjectIds = requestedProjectId.HasValue
            ? new[] { requestedProjectId.Value }
            : projectMap.Keys.ToArray();
        if (scopedProjectIds.Length == 0)
        {
            return Results.Ok(new
            {
                status = "billing_invoice_analytics_loaded",
                contractVersion = BillingJourneyContractVersion,
                rows = Array.Empty<object>(),
                summary = new
                {
                    invoiceCount = 0,
                    partialInvoiceCount = 0,
                    partialInvoiceAmount = 0m,
                    finalInvoiceCount = 0,
                    finalInvoiceAmount = 0m,
                    totalNonVoidAmount = 0m
                },
                generatedAt = DateTimeOffset.UtcNow
            });
        }

        var invoiceType = BillingJourneyTextQuery(context, "invoiceType", 40).ToLowerInvariant();
        if (invoiceType is not "partial" and not "final") invoiceType = string.Empty;
        var invoiceStatus = BillingJourneyTextQuery(context, "status", 80).ToLowerInvariant();
        var search = BillingJourneyTextQuery(context, "search", 300);
        var dateFrom = BillingJourneyDateQuery(context, "dateFrom");
        var dateTo = BillingJourneyDateQuery(context, "dateTo");
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
            (dateFrom, dateTo) = (dateTo, dateFrom);

        var rows = await LoadBillingInvoiceAnalyticsRowsAsync(
            connection,
            scopedProjectIds,
            invoiceType,
            invoiceStatus,
            search,
            dateFrom,
            dateTo,
            context.RequestAborted);

        var nonVoid = rows.Where(row => !row.InvoiceStatus.Equals("void", StringComparison.OrdinalIgnoreCase)).ToArray();
        var partial = nonVoid.Where(row => row.InvoiceType.Equals("partial", StringComparison.OrdinalIgnoreCase)).ToArray();
        var final = nonVoid.Where(row => row.InvoiceType.Equals("final", StringComparison.OrdinalIgnoreCase)).ToArray();

        return Results.Ok(new
        {
            status = "billing_invoice_analytics_loaded",
            contractVersion = BillingJourneyContractVersion,
            report = new
            {
                code = "billing_invoice_lifecycle",
                name = "Partial & Final Invoice Lifecycle",
                category = "Financial",
                modules = new[] { "005", "039", "040", "042", "055C", "030" },
                description = "Immutable partial and final invoice evidence with labor, expense, milestone, status, period, ownership, and source totals."
            },
            effectiveFilters = new
            {
                projectId = requestedProjectId,
                invoiceType,
                status = invoiceStatus,
                search,
                dateFrom,
                dateTo
            },
            options = new
            {
                invoiceTypes = new[] { "partial", "final" },
                statuses = rows.Select(row => row.InvoiceStatus)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToArray(),
                projects = projects.Select(project => new
                {
                    project.ProjectId,
                    label = $"{project.ProjectCode} · {project.ProjectName}",
                    project.CustomerName
                }).OrderBy(item => item.CustomerName).ThenBy(item => item.label).ToArray()
            },
            summary = new
            {
                invoiceCount = rows.Length,
                nonVoidInvoiceCount = nonVoid.Length,
                partialInvoiceCount = partial.Length,
                partialInvoiceAmount = partial.Sum(row => row.TotalAmount),
                finalInvoiceCount = final.Length,
                finalInvoiceAmount = final.Sum(row => row.TotalAmount),
                voidInvoiceCount = rows.Count(row => row.InvoiceStatus.Equals("void", StringComparison.OrdinalIgnoreCase)),
                totalNonVoidAmount = nonVoid.Sum(row => row.TotalAmount),
                laborAmount = nonVoid.Sum(row => row.LaborAmount),
                expenseAmount = nonVoid.Sum(row => row.ExpenseAmount),
                milestoneAmount = nonVoid.Sum(row => row.MilestoneAmount),
                projectCount = nonVoid.Select(row => row.ProjectId).Distinct().Count(),
                customerCount = nonVoid.Select(row => row.CustomerName).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            },
            rows,
            scope = new
            {
                accessibleProjectCount = projects.Length,
                selectedProjectCount = scopedProjectIds.Length,
                actor.ActualUserId,
                actor.EffectiveUserId,
                actor.IsViewAs
            },
            immutability = new
            {
                invoiceSnapshotsRequired = true,
                sourceLinesVersioned = true,
                auditEventsAppendOnly = true
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<BillingJourneyProjectMetrics> LoadBillingJourneyProjectMetricsAsync(
        NpgsqlConnection connection,
        Guid projectId,
        int currentExpenseUploadCount,
        CancellationToken cancellationToken)
    {
        var metrics = new BillingJourneyProjectMetrics();

        await using (var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (WHERE lower(COALESCE(invoice_status, '')) <> 'void'),
                COUNT(*) FILTER (WHERE invoice_type = 'partial' AND lower(COALESCE(invoice_status, '')) <> 'void'),
                COUNT(*) FILTER (WHERE invoice_type = 'final' AND lower(COALESCE(invoice_status, '')) <> 'void'),
                COALESCE(SUM(total_amount) FILTER (WHERE lower(COALESCE(invoice_status, '')) <> 'void'), 0),
                MAX(created_at) FILTER (WHERE lower(COALESCE(invoice_status, '')) <> 'void')
            FROM billing_invoices
            WHERE project_id = @project_id;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.InvoiceCount = reader.GetInt64(0);
                metrics.PartialInvoiceCount = reader.GetInt64(1);
                metrics.FinalInvoiceCount = reader.GetInt64(2);
                metrics.InvoicedAmount = reader.GetDecimal(3);
                metrics.LastInvoiceAt = reader.IsDBNull(4)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(4);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT invoice_number, invoice_type, invoice_status
            FROM billing_invoices
            WHERE project_id = @project_id
              AND lower(COALESCE(invoice_status, '')) <> 'void'
            ORDER BY created_at DESC, invoice_installment_number DESC
            LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.LatestInvoiceNumber = reader.GetString(0);
                metrics.LatestInvoiceType = reader.GetString(1);
                metrics.LatestInvoiceStatus = reader.GetString(2);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (WHERE review_status = 'ready'),
                COUNT(*) FILTER (WHERE review_status = 'blocked'),
                COUNT(*) FILTER (WHERE review_status = 'draft'),
                MAX(updated_at)
            FROM work_billing_readiness_reviews
            WHERE project_id = @project_id;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.ReadyReadinessPackageCount = reader.GetInt64(0);
                metrics.BlockedReadinessPackageCount = reader.GetInt64(1);
                metrics.DraftReadinessPackageCount = reader.GetInt64(2);
                metrics.LastReadinessAt = reader.IsDBNull(3)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(3);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (
                    WHERE entry.status = ANY(@approved_statuses)
                      AND NOT EXISTS (
                          SELECT 1
                          FROM billing_invoice_lines line
                          JOIN billing_invoices invoice
                            ON invoice.billing_invoice_id = line.billing_invoice_id
                          WHERE line.time_entry_id = entry.time_entry_id
                            AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                      )
                ),
                COUNT(*) FILTER (
                    WHERE NOT (entry.status = ANY(@approved_statuses))
                      AND NOT EXISTS (
                          SELECT 1
                          FROM billing_invoice_lines line
                          JOIN billing_invoices invoice
                            ON invoice.billing_invoice_id = line.billing_invoice_id
                          WHERE line.time_entry_id = entry.time_entry_id
                            AND lower(COALESCE(invoice.invoice_status, '')) <> 'void'
                      )
                )
            FROM time_entries entry
            WHERE entry.project_id = @project_id
              AND entry.billable = TRUE
              AND entry.hours > 0;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.Add(new NpgsqlParameter(
                "approved_statuses",
                NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = BillingJourneyApprovedTimeStatuses
            });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.EligibleUninvoicedTimeCount = reader.GetInt64(0);
                metrics.PendingBillableTimeCount = reader.GetInt64(1);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*)
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
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            metrics.ReadyUninvoicedNonLaborCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        await using (var command = new NpgsqlCommand("""
            SELECT
                COALESCE(profile.purchase_order_required, FALSE),
                EXISTS (
                    SELECT 1
                    FROM project_purchase_orders purchase_order
                    WHERE purchase_order.project_id = project.project_id
                      AND purchase_order.is_primary = TRUE
                      AND purchase_order.po_status = 'active'
                      AND (purchase_order.effective_start_date IS NULL OR purchase_order.effective_start_date <= CURRENT_DATE)
                      AND (purchase_order.effective_end_date IS NULL OR purchase_order.effective_end_date >= CURRENT_DATE)
                )
            FROM projects project
            LEFT JOIN project_billing_profiles profile ON profile.project_id = project.project_id
            WHERE project.project_id = @project_id;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.PurchaseOrderRequired = reader.GetBoolean(0);
                metrics.PurchaseOrderReady = !metrics.PurchaseOrderRequired || reader.GetBoolean(1);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT closeout_status, billing_disposition, closed_at
            FROM work_closeout_records
            WHERE project_id = @project_id;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                metrics.CloseoutStatus = reader.GetString(0);
                metrics.BillingDisposition = reader.GetString(1);
                metrics.ClosedAt = reader.IsDBNull(2)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(2);
            }
        }

        metrics.UnacceptedExpenseUploadCount = currentExpenseUploadCount;
        if (currentExpenseUploadCount > 0
            && await BillingJourneyTableExistsAsync(
                connection,
                "project_expense_upload_acceptances",
                cancellationToken))
        {
            await using var command = new NpgsqlCommand("""
                SELECT COUNT(*)
                FROM project_expense_uploads upload
                WHERE upload.project_id = @project_id
                  AND upload.is_current = TRUE
                  AND upload.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM project_expense_upload_acceptances acceptance
                      WHERE acceptance.project_expense_upload_id = upload.project_expense_upload_id
                  );
                """, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            metrics.UnacceptedExpenseUploadCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        return metrics;
    }

    private static async Task<BillingJourneyActivityRow[]> LoadBillingJourneyActivityAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var rows = new List<BillingJourneyActivityRow>();

        await using (var command = new NpgsqlCommand("""
            SELECT
                event.work_lifecycle_audit_event_id,
                event.process_area,
                event.event_type,
                event.summary,
                event.reason,
                COALESCE(actor.display_name, actor.email, 'System'),
                event.related_entity_type,
                event.related_entity_id,
                event.event_json::text,
                event.created_at
            FROM work_lifecycle_audit_events event
            LEFT JOIN app_users actor ON actor.user_id = event.actor_user_id
            WHERE event.project_id = @project_id
            ORDER BY event.created_at DESC
            LIMIT 300;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new BillingJourneyActivityRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetGuid(7),
                    BillingJourneyJson(reader.GetString(8)),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    true));
            }
        }

        if (await BillingJourneyTableExistsAsync(connection, "project_expense_events", cancellationToken))
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                    event.project_expense_event_id,
                    event.event_code,
                    event.reason,
                    COALESCE(actor.display_name, actor.email, 'System'),
                    event.project_expense_upload_id,
                    event.event_metadata::text,
                    event.created_at
                FROM project_expense_events event
                LEFT JOIN app_users actor ON actor.user_id = event.actor_user_id
                WHERE event.project_id = @project_id
                ORDER BY event.created_at DESC
                LIMIT 300;
                """, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var action = reader.GetString(1);
                rows.Add(new BillingJourneyActivityRow(
                    reader.GetGuid(0),
                    "expense",
                    action,
                    BillingJourneyExpenseEventSummary(action),
                    reader.GetString(2),
                    reader.GetString(3),
                    "project_expense_upload",
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    BillingJourneyJson(reader.GetString(5)),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    true));
            }
        }

        return rows
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.EventId)
            .Take(500)
            .ToArray();
    }

    private static async Task<BillingJourneyInvoiceReportRow[]> LoadBillingInvoiceAnalyticsRowsAsync(
        NpgsqlConnection connection,
        Guid[] projectIds,
        string invoiceType,
        string invoiceStatus,
        string search,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var rows = new List<BillingJourneyInvoiceReportRow>();
        await using var command = new NpgsqlCommand("""
            SELECT
                invoice.billing_invoice_id,
                invoice.project_id,
                invoice.invoice_number,
                invoice.invoice_type,
                invoice.invoice_status,
                invoice.billing_period_start,
                invoice.billing_period_end,
                invoice.invoice_date,
                invoice.customer_name_snapshot,
                invoice.project_code_snapshot,
                invoice.project_name_snapshot,
                invoice.project_manager_name_snapshot,
                invoice.purchase_order_number_snapshot,
                invoice.subtotal_amount,
                invoice.adjustment_amount,
                invoice.tax_amount,
                invoice.total_amount,
                invoice.invoice_notes,
                invoice.created_at,
                invoice.finalized_at,
                COALESCE(created_by.display_name, created_by.email, 'System'),
                COALESCE(finalized_by.display_name, finalized_by.email, ''),
                COUNT(line.billing_invoice_line_id),
                COALESCE(SUM(line.line_amount) FILTER (WHERE line.source_type = 'time_entry'), 0),
                COALESCE(SUM(line.line_amount) FILTER (WHERE line.source_type = 'expense'), 0),
                COALESCE(SUM(line.line_amount) FILTER (WHERE line.source_type = 'fixed_price_milestone'), 0),
                COALESCE(SUM(line.line_amount) FILTER (WHERE line.source_type NOT IN ('time_entry', 'expense', 'fixed_price_milestone')), 0),
                invoice.immutable_snapshot_json <> '{}'::jsonb
            FROM billing_invoices invoice
            LEFT JOIN billing_invoice_lines line
              ON line.billing_invoice_id = invoice.billing_invoice_id
            LEFT JOIN app_users created_by
              ON created_by.user_id = invoice.created_by_user_id
            LEFT JOIN app_users finalized_by
              ON finalized_by.user_id = invoice.finalized_by_user_id
            WHERE invoice.project_id = ANY(@project_ids)
              AND (@invoice_type IS NULL OR lower(invoice.invoice_type) = @invoice_type)
              AND (@invoice_status IS NULL OR lower(invoice.invoice_status) = @invoice_status)
              AND (@date_from IS NULL OR COALESCE(invoice.invoice_date, invoice.created_at::date) >= @date_from)
              AND (@date_to IS NULL OR COALESCE(invoice.invoice_date, invoice.created_at::date) <= @date_to)
              AND (
                  @search IS NULL
                  OR invoice.invoice_number ILIKE @search_pattern
                  OR invoice.customer_name_snapshot ILIKE @search_pattern
                  OR invoice.project_code_snapshot ILIKE @search_pattern
                  OR invoice.project_name_snapshot ILIKE @search_pattern
                  OR invoice.project_manager_name_snapshot ILIKE @search_pattern
                  OR invoice.purchase_order_number_snapshot ILIKE @search_pattern
              )
            GROUP BY
                invoice.billing_invoice_id,
                created_by.display_name,
                created_by.email,
                finalized_by.display_name,
                finalized_by.email
            ORDER BY invoice.created_at DESC, invoice.invoice_installment_number DESC
            LIMIT 5000;
            """, connection);
        command.Parameters.Add(new NpgsqlParameter(
            "project_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = projectIds
        });
        command.Parameters.Add(new NpgsqlParameter("invoice_type", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(invoiceType) ? DBNull.Value : invoiceType
        });
        command.Parameters.Add(new NpgsqlParameter("invoice_status", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(invoiceStatus) ? DBNull.Value : invoiceStatus
        });
        command.Parameters.Add(new NpgsqlParameter("date_from", NpgsqlDbType.Date)
        {
            Value = dateFrom.HasValue ? dateFrom.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("date_to", NpgsqlDbType.Date)
        {
            Value = dateTo.HasValue ? dateTo.Value : DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Text)
        {
            Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search
        });
        command.Parameters.AddWithValue(
            "search_pattern",
            string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{search}%");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BillingJourneyInvoiceReportRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetDecimal(13),
                reader.GetDecimal(14),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                reader.GetString(17),
                reader.GetFieldValue<DateTimeOffset>(18),
                reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
                reader.GetString(20),
                reader.GetString(21),
                reader.GetInt64(22),
                reader.GetDecimal(23),
                reader.GetDecimal(24),
                reader.GetDecimal(25),
                reader.GetDecimal(26),
                reader.GetBoolean(27)));
        }

        return rows.ToArray();
    }

    private static async Task<bool> BillingJourneyTableExistsAsync(
        NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass(@qualified_name) IS NOT NULL;",
            connection);
        command.Parameters.AddWithValue("qualified_name", $"public.{table}");
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string BillingJourneyTextQuery(
        HttpContext context,
        string key,
        int maximum)
    {
        var value = context.Request.Query[key].ToString().Replace('\0', ' ').Trim();
        return value.Length <= maximum ? value : value[..maximum];
    }

    private static Guid? BillingJourneyGuidQuery(HttpContext context, string key) =>
        Guid.TryParse(context.Request.Query[key], out var value) ? value : null;

    private static DateOnly? BillingJourneyDateQuery(HttpContext context, string key) =>
        DateOnly.TryParse(context.Request.Query[key], out var value) ? value : null;

    private static JsonElement BillingJourneyJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static string BillingJourneyExpenseEventSummary(string eventCode) =>
        eventCode switch
        {
            "UPLOAD_CREATED" => "A project expense version was uploaded.",
            "CERTIFY_IMPORTED" => "A project expense version was imported from Certify.",
            "UPLOAD_SUPERSEDED" => "A project expense version was superseded by a newer immutable version.",
            "UPLOAD_DELETED" => "A project expense version was deleted before acceptance; its audit evidence remains immutable.",
            "PM_EXPENSE_VERSION_ACCEPTED" => "The assigned Project Manager accepted this exact expense version and locked it.",
            "NOTIFICATION_QUEUED" => "An expense notification was queued.",
            "NOTIFICATION_SENT" => "An expense notification was sent.",
            "NOTIFICATION_FAILED" => "An expense notification attempt failed.",
            _ => eventCode.Replace('_', ' ').ToLowerInvariant()
        };

    private sealed class BillingJourneyProjectMetrics
    {
        public long InvoiceCount { get; set; }
        public long PartialInvoiceCount { get; set; }
        public long FinalInvoiceCount { get; set; }
        public decimal InvoicedAmount { get; set; }
        public DateTimeOffset? LastInvoiceAt { get; set; }
        public string LatestInvoiceNumber { get; set; } = string.Empty;
        public string LatestInvoiceType { get; set; } = string.Empty;
        public string LatestInvoiceStatus { get; set; } = string.Empty;
        public long ReadyReadinessPackageCount { get; set; }
        public long BlockedReadinessPackageCount { get; set; }
        public long DraftReadinessPackageCount { get; set; }
        public DateTimeOffset? LastReadinessAt { get; set; }
        public long EligibleUninvoicedTimeCount { get; set; }
        public long PendingBillableTimeCount { get; set; }
        public long ReadyUninvoicedNonLaborCount { get; set; }
        public long UnacceptedExpenseUploadCount { get; set; }
        public bool PurchaseOrderRequired { get; set; }
        public bool PurchaseOrderReady { get; set; } = true;
        public string CloseoutStatus { get; set; } = "not_started";
        public string BillingDisposition { get; set; } = string.Empty;
        public DateTimeOffset? ClosedAt { get; set; }
        public long RemainingEligibleSourceCount =>
            EligibleUninvoicedTimeCount + ReadyUninvoicedNonLaborCount;
    }

    private sealed record BillingJourneyActivityRow(
        Guid EventId,
        string ProcessArea,
        string Action,
        string Summary,
        string Reason,
        string Actor,
        string RelatedEntityType,
        Guid? RelatedEntityId,
        JsonElement Details,
        DateTimeOffset OccurredAt,
        bool Immutable);

    private sealed record BillingJourneyInvoiceReportRow(
        Guid BillingInvoiceId,
        Guid ProjectId,
        string InvoiceNumber,
        string InvoiceType,
        string InvoiceStatus,
        DateOnly? BillingPeriodStart,
        DateOnly? BillingPeriodEnd,
        DateOnly? InvoiceDate,
        string CustomerName,
        string ProjectCode,
        string ProjectName,
        string ProjectManagerName,
        string PurchaseOrderNumber,
        decimal SubtotalAmount,
        decimal AdjustmentAmount,
        decimal TaxAmount,
        decimal TotalAmount,
        string InvoiceNotes,
        DateTimeOffset CreatedAt,
        DateTimeOffset? FinalizedAt,
        string CreatedBy,
        string FinalizedBy,
        long LineCount,
        decimal LaborAmount,
        decimal ExpenseAmount,
        decimal MilestoneAmount,
        decimal OtherAmount,
        bool ImmutableSnapshotAvailable);
}
