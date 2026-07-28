using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Connects current, non-deleted Module 005 expense uploads to the governed
/// Work-to-Cash readiness contract. PMs, PTCs, Accounting, and Super
/// Administrators acknowledge authoritative upload totals; the browser cannot
/// submit its own billing amount. Deleted or superseded expense evidence is
/// blocked before Module 042 loads billing candidates.
/// </summary>
public static partial class Module005ProjectExpenseUploadModule
{
    public static WebApplication MapModule005ProjectExpenseBillingAcknowledgementEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/project-expenses/projects/{projectId:guid}/billing-context",
            (Func<Guid, HttpContext, Task<IResult>>)GetExpenseBillingContextAsync);
        app.MapPost(
            "/api/project-expenses/projects/{projectId:guid}/billing-acknowledgement",
            (Func<Guid, ExpenseBillingAcknowledgementRequest, HttpContext, Task<IResult>>)AcknowledgeExpenseBillingAsync);
        return app;
    }

    public static WebApplication UseProjectExpenseBillingReadinessContinuity(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/api/billing/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await using var connection = await OpenConnectionAsync();
                    await BlockStaleExpenseReadinessAsync(connection, null, context.RequestAborted);
                }
                catch (Exception exception)
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ProjectExpenseBillingReadinessContinuity")
                        .LogWarning(
                            "Stale project-expense billing readiness reconciliation was unavailable ({ExceptionType}).",
                            exception.GetType().Name);
                }
            }

            await next();
        });
        return app;
    }

    private static async Task<IResult> GetExpenseBillingContextAsync(Guid projectId, HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (!HasRole(actor, SelfRoles) && !HasRole(actor, BillingRoles))
            return AccessDenied("The current role cannot view project expense billing context.");

        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var project = projects.FirstOrDefault(item => item.ProjectId == projectId);
        if (project is null) return AccessDenied("The selected project is outside the current role scope.");

        await BlockStaleExpenseReadinessAsync(connection, null, context.RequestAborted);
        var snapshot = await LoadCurrentExpenseSnapshotAsync(connection, null, projectId, context.RequestAborted);
        var acknowledgement = await LoadExpenseAcknowledgementAsync(connection, null, projectId, context.RequestAborted);
        var treatment = BillingTreatment(project.ContractType);
        var expectedAmount = treatment == "pass_through_invoice"
            ? snapshot.ReimbursableAmount
            : snapshot.TotalAmount;
        var expectedPackage = PackageType(treatment);
        var expectedStatus = treatment == "pass_through_invoice" ? "ready" : "draft";
        var acknowledgementCurrent = snapshot.CurrentUploadCount > 0
            && acknowledgement is not null
            && acknowledgement.PackageType.Equals(expectedPackage, StringComparison.OrdinalIgnoreCase)
            && acknowledgement.ReviewStatus.Equals(expectedStatus, StringComparison.OrdinalIgnoreCase)
            && acknowledgement.EvidenceAmount == expectedAmount
            && acknowledgement.UpdatedAt >= snapshot.LatestUploadAt;

        return Results.Ok(new
        {
            status = "project_expense_billing_context_loaded",
            module = "005",
            project = new
            {
                project.ProjectId,
                project.ClientId,
                project.CustomerName,
                project.ProjectCode,
                project.ProjectName,
                project.ContractType,
                billingTreatment = treatment
            },
            actor = new
            {
                actor.ActualUserId,
                actor.EffectiveUserId,
                actor.RoleCodes,
                actor.IsViewAs,
                canAcknowledgeForBilling = !actor.IsViewAs && HasRole(actor, BillingRoles)
            },
            snapshot.CurrentUploadCount,
            trackedExpenseTotal = snapshot.TotalAmount,
            invoiceEligibleExpenseTotal = treatment == "pass_through_invoice" ? snapshot.ReimbursableAmount : 0m,
            fixedPriceIncludedCostTotal = treatment == "included_fixed_price" ? snapshot.TotalAmount : 0m,
            snapshot.PeriodStart,
            snapshot.PeriodEnd,
            snapshot.LatestUploadAt,
            uploads = snapshot.Uploads,
            requiresAcknowledgement = snapshot.CurrentUploadCount > 0 && !acknowledgementCurrent,
            acknowledgementCurrent,
            acknowledgement = acknowledgement is null ? null : new
            {
                acknowledgement.ReviewId,
                acknowledgement.PackageType,
                acknowledgement.ReviewStatus,
                acknowledgement.EvidenceSourceType,
                acknowledgement.EvidenceDescription,
                acknowledgement.EvidenceAmount,
                acknowledgement.ReviewedBy,
                acknowledgement.UpdatedAt
            },
            deletedUploadsExcluded = true,
            staleReadinessBlocked = true
        });
    }

    private static async Task<IResult> AcknowledgeExpenseBillingAsync(
        Guid projectId,
        ExpenseBillingAcknowledgementRequest request,
        HttpContext context)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 500)
        {
            return Results.BadRequest(new
            {
                status = "reason_required",
                message = "Enter a specific acknowledgement reason of at least five characters."
            });
        }

        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs) return ViewAsReadOnly();
        if (!HasRole(actor, BillingRoles))
        {
            return AccessDenied(
                "Expense billing acknowledgement is restricted to the assigned PM, Project Team Coordinator, Accounting, or Super Administrator.");
        }

        var projects = await LoadAccessibleProjectsAsync(connection, actor, true);
        var project = projects.FirstOrDefault(item => item.ProjectId == projectId);
        if (project is null) return AccessDenied("The selected project is outside the current role scope.");

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await using (var advisory = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@key, 44));",
                connection,
                transaction))
            {
                advisory.Parameters.AddWithValue("key", $"project-expense-billing:{projectId}");
                await advisory.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await BlockStaleExpenseReadinessAsync(connection, transaction, context.RequestAborted);
            var snapshot = await LoadCurrentExpenseSnapshotAsync(
                connection,
                transaction,
                projectId,
                context.RequestAborted);
            if (snapshot.CurrentUploadCount == 0)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "no_current_project_expenses",
                    message = "No current, non-deleted expense upload is available to acknowledge."
                });
            }

            var treatment = BillingTreatment(project.ContractType);
            var packageType = PackageType(treatment);
            var reviewStatus = treatment == "pass_through_invoice" ? "ready" : "draft";
            var evidenceAmount = treatment == "pass_through_invoice"
                ? snapshot.ReimbursableAmount
                : snapshot.TotalAmount;
            if (evidenceAmount <= 0)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "expense_amount_not_eligible",
                    message = treatment == "pass_through_invoice"
                        ? "The current upload contains no positive reimbursable amount for pass-through invoicing."
                        : "The current upload contains no positive tracked expense amount."
                });
            }

            var periodStart = snapshot.PeriodStart ?? DateOnly.FromDateTime(snapshot.LatestUploadAt.UtcDateTime);
            var periodEnd = snapshot.PeriodEnd ?? periodStart;
            var description = $"Module 005 current expense uploads ({snapshot.CurrentUploadCount}) for {project.ProjectCode}; "
                + $"authoritative amount {evidenceAmount:0.00}; upload IDs {string.Join(',', snapshot.Uploads.Select(upload => upload.UploadId))}.";
            var checklist = JsonSerializer.Serialize(new Dictionary<string, bool>
            {
                ["timeApproved"] = true,
                ["certifyReviewed"] = true,
                ["customerMapped"] = true,
                ["exceptionsCleared"] = true,
                ["billingTreatment"] = true,
                ["evidenceReady"] = true,
                ["customerNotesReady"] = true,
                ["accountingReady"] = true
            });
            var reviewId = Guid.NewGuid();

            await using (var command = new NpgsqlCommand("""
                INSERT INTO work_billing_readiness_reviews (
                    work_billing_readiness_review_id,
                    project_id,
                    billing_period_start,
                    billing_period_end,
                    package_type,
                    review_status,
                    checklist_json,
                    notes,
                    evidence_source_type,
                    evidence_description,
                    evidence_amount,
                    reviewed_by_user_id,
                    created_at,
                    updated_at
                )
                VALUES (
                    @review_id,
                    @project_id,
                    @period_start,
                    @period_end,
                    @package_type,
                    @review_status,
                    @checklist::jsonb,
                    @notes,
                    'expense',
                    @evidence_description,
                    @evidence_amount,
                    @actor_user_id,
                    NOW(),
                    NOW()
                )
                ON CONFLICT (project_id, billing_period_start, billing_period_end, package_type)
                DO UPDATE SET
                    review_status = EXCLUDED.review_status,
                    checklist_json = EXCLUDED.checklist_json,
                    notes = EXCLUDED.notes,
                    evidence_source_type = EXCLUDED.evidence_source_type,
                    evidence_description = EXCLUDED.evidence_description,
                    evidence_amount = EXCLUDED.evidence_amount,
                    reviewed_by_user_id = EXCLUDED.reviewed_by_user_id,
                    updated_at = NOW()
                RETURNING work_billing_readiness_review_id;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("review_id", reviewId);
                command.Parameters.AddWithValue("project_id", projectId);
                command.Parameters.Add("period_start", NpgsqlDbType.Date).Value = periodStart;
                command.Parameters.Add("period_end", NpgsqlDbType.Date).Value = periodEnd;
                command.Parameters.AddWithValue("package_type", packageType);
                command.Parameters.AddWithValue("review_status", reviewStatus);
                command.Parameters.AddWithValue("checklist", checklist);
                command.Parameters.AddWithValue("notes", reason);
                command.Parameters.AddWithValue("evidence_description", description);
                command.Parameters.AddWithValue("evidence_amount", evidenceAmount);
                command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                reviewId = (Guid)(await command.ExecuteScalarAsync(context.RequestAborted) ?? reviewId);
            }

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO work_lifecycle_audit_events (
                    work_lifecycle_audit_event_id,
                    project_id,
                    process_area,
                    event_type,
                    prior_state,
                    new_state,
                    summary,
                    reason,
                    actor_user_id,
                    related_entity_type,
                    related_entity_id,
                    event_json,
                    created_at
                )
                VALUES (
                    gen_random_uuid(),
                    @project_id,
                    'billing_readiness',
                    @event_type,
                    'expense_uploaded',
                    @new_state,
                    @summary,
                    @reason,
                    @actor_user_id,
                    'billing_readiness_review',
                    @review_id,
                    @event_json::jsonb,
                    NOW()
                );
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("project_id", projectId);
                audit.Parameters.AddWithValue(
                    "event_type",
                    treatment == "pass_through_invoice"
                        ? "project_expense_ready_for_invoice"
                        : "project_expense_acknowledged_as_included_cost");
                audit.Parameters.AddWithValue("new_state", reviewStatus);
                audit.Parameters.AddWithValue(
                    "summary",
                    treatment == "pass_through_invoice"
                        ? "Current project expense uploads were acknowledged for pass-through invoice review."
                        : "Current project expense uploads were acknowledged as included project cost and will not become a separate invoice line.");
                audit.Parameters.AddWithValue("reason", reason);
                audit.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                audit.Parameters.AddWithValue("review_id", reviewId);
                audit.Parameters.AddWithValue("event_json", JsonSerializer.Serialize(new
                {
                    treatment,
                    packageType,
                    reviewStatus,
                    evidenceAmount,
                    currentUploadCount = snapshot.CurrentUploadCount,
                    uploadIds = snapshot.Uploads.Select(upload => upload.UploadId).ToArray(),
                    deletedUploadsExcluded = true,
                    invoiceLineEligible = treatment == "pass_through_invoice"
                }));
                await audit.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = treatment == "pass_through_invoice"
                    ? "project_expense_ready_for_invoice"
                    : "project_expense_acknowledged_as_included_cost",
                message = treatment == "pass_through_invoice"
                    ? "The authoritative current expense total is ready for Module 042 invoice review."
                    : "The authoritative current expense total is tracked as included project cost and will not be added as a separate invoice charge.",
                projectId,
                reviewId,
                billingTreatment = treatment,
                packageType,
                reviewStatus,
                evidenceAmount,
                currentUploadCount = snapshot.CurrentUploadCount,
                invoiceLineEligible = treatment == "pass_through_invoice",
                auditRecorded = true
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task BlockStaleExpenseReadinessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE work_billing_readiness_reviews review
            SET review_status = 'blocked',
                notes = CASE
                    WHEN review.notes = '' THEN 'Automatically blocked because the acknowledged Module 005 expense evidence is no longer current.'
                    ELSE review.notes || E'\nAutomatically blocked because the acknowledged Module 005 expense evidence is no longer current.'
                END,
                updated_at = NOW()
            WHERE review.evidence_source_type = 'expense'
              AND review.review_status = 'ready'
              AND review.package_type LIKE 'expense-%'
              AND (
                    NOT EXISTS (
                        SELECT 1
                        FROM project_expense_uploads upload
                        WHERE upload.project_id = review.project_id
                          AND upload.is_current = TRUE
                          AND upload.deleted_at IS NULL
                          AND COALESCE(upload.period_start, upload.uploaded_at::date) <= review.billing_period_end
                          AND COALESCE(upload.period_end, upload.uploaded_at::date) >= review.billing_period_start
                    )
                    OR review.updated_at < COALESCE((
                        SELECT MAX(upload.uploaded_at)
                        FROM project_expense_uploads upload
                        WHERE upload.project_id = review.project_id
                          AND upload.is_current = TRUE
                          AND upload.deleted_at IS NULL
                          AND COALESCE(upload.period_start, upload.uploaded_at::date) <= review.billing_period_end
                          AND COALESCE(upload.period_end, upload.uploaded_at::date) >= review.billing_period_start
                    ), review.updated_at)
                    OR (
                        review.package_type = 'expense-only-pass-through'
                        AND COALESCE(review.evidence_amount, 0) <> COALESCE((
                            SELECT SUM(upload.reimbursable_amount)
                            FROM project_expense_uploads upload
                            WHERE upload.project_id = review.project_id
                              AND upload.is_current = TRUE
                              AND upload.deleted_at IS NULL
                              AND COALESCE(upload.period_start, upload.uploaded_at::date) <= review.billing_period_end
                              AND COALESCE(upload.period_end, upload.uploaded_at::date) >= review.billing_period_start
                        ), 0)
                    )
              );
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ExpenseBillingSnapshot> LoadCurrentExpenseSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var uploads = new List<ExpenseBillingUpload>();
        await using var command = new NpgsqlCommand("""
            SELECT
                upload.project_expense_upload_id,
                upload.expense_owner_user_id,
                COALESCE(owner_user.display_name, owner_user.email, ''),
                upload.period_start,
                upload.period_end,
                upload.total_amount,
                upload.reimbursable_amount,
                upload.billing_treatment,
                upload.version_number,
                upload.uploaded_at
            FROM project_expense_uploads upload
            JOIN app_users owner_user ON owner_user.user_id = upload.expense_owner_user_id
            WHERE upload.project_id = @project_id
              AND upload.is_current = TRUE
              AND upload.deleted_at IS NULL
            ORDER BY upload.uploaded_at DESC, upload.version_number DESC;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            uploads.Add(new ExpenseBillingUpload(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetFieldValue<DateTimeOffset>(9)));
        }

        return new ExpenseBillingSnapshot(
            uploads.Count,
            uploads.Sum(upload => upload.TotalAmount),
            uploads.Sum(upload => upload.ReimbursableAmount),
            uploads.Where(upload => upload.PeriodStart.HasValue).Select(upload => upload.PeriodStart!.Value).DefaultIfEmpty().Min(),
            uploads.Where(upload => upload.PeriodEnd.HasValue).Select(upload => upload.PeriodEnd!.Value).DefaultIfEmpty().Max(),
            uploads.Count == 0 ? DateTimeOffset.MinValue : uploads.Max(upload => upload.UploadedAt),
            uploads);
    }

    private static async Task<ExpenseBillingAcknowledgement?> LoadExpenseAcknowledgementAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                review.work_billing_readiness_review_id,
                review.package_type,
                review.review_status,
                review.evidence_source_type,
                review.evidence_description,
                review.evidence_amount,
                COALESCE(actor.display_name, actor.email, ''),
                review.updated_at
            FROM work_billing_readiness_reviews review
            LEFT JOIN app_users actor ON actor.user_id = review.reviewed_by_user_id
            WHERE review.project_id = @project_id
              AND review.evidence_source_type = 'expense'
              AND review.package_type LIKE 'expense-%'
            ORDER BY review.updated_at DESC
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ExpenseBillingAcknowledgement(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static string PackageType(string treatment) => treatment switch
    {
        "pass_through_invoice" => "expense-only-pass-through",
        "included_fixed_price" => "expense-included-fixed-price",
        _ => "expense-internal-nonbillable"
    };

    public sealed record ExpenseBillingAcknowledgementRequest(string? Reason);

    private sealed record ExpenseBillingSnapshot(
        int CurrentUploadCount,
        decimal TotalAmount,
        decimal ReimbursableAmount,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        DateTimeOffset LatestUploadAt,
        IReadOnlyList<ExpenseBillingUpload> Uploads);

    private sealed record ExpenseBillingUpload(
        Guid UploadId,
        Guid ExpenseOwnerUserId,
        string ExpenseOwnerName,
        DateOnly? PeriodStart,
        DateOnly? PeriodEnd,
        decimal TotalAmount,
        decimal ReimbursableAmount,
        string BillingTreatment,
        int VersionNumber,
        DateTimeOffset UploadedAt);

    private sealed record ExpenseBillingAcknowledgement(
        Guid ReviewId,
        string PackageType,
        string ReviewStatus,
        string EvidenceSourceType,
        string EvidenceDescription,
        decimal EvidenceAmount,
        string ReviewedBy,
        DateTimeOffset UpdatedAt);
}
