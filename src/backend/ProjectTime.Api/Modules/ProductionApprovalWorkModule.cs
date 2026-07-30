using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Production approval-work contract.
///
/// Approval units are deliberately stage-specific:
/// - Manager: one submitted employee day.
/// - Project Manager: one managed project on one employee day.
/// - PTC final: one employee day after every required project approval, or one
///   manager-approved non-project-only day that does not require PM review.
///
/// This prevents a PM from approving another PM's project entries or any
/// non-project entry merely because both appear on the same day.
/// </summary>
public static class ProductionApprovalWorkModule
{
    private const string ContractVersion = "approval-work-production-v2-2026-07-30";
    private const int DefaultPageSize = 200;
    private const int MaximumPageSize = 500;
    private const int MaximumSelectedItems = 2000;

    private static readonly HashSet<string> OrganizationApprovalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
    };

    private static readonly HashSet<string> ManagerApprovalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANAGER",
        "PEOPLE_MANAGER",
        "ENGINEERING_LEAD",
        "ENGINEERING_TEAM_LEAD"
    };

    private static readonly HashSet<string> ProjectApprovalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    private static readonly HashSet<string> ProtectedNonProjectCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADMINISTRATIVE",
        "BEREAVEMENT",
        "COMP_TIME",
        "HOLIDAY",
        "JURY_DUTY",
        "LTD",
        "PEER_SUPPORT",
        "PERSONAL_HOLIDAY",
        "FMLA_APPROVED",
        "STD",
        "SICK_LEAVE",
        "UNPAID_TIME_OFF",
        "TRAINING",
        "VACATION",
        "VOLUNTEER_TIME"
    };

    private static readonly HashSet<string> AllowedClassifications = new(StringComparer.OrdinalIgnoreCase)
    {
        "administrative",
        "leave",
        "non_billable",
        "paid_time_off",
        "training",
        "unpaid_time_off"
    };

    public static WebApplication MapProductionApprovalWorkEndpoints(this WebApplication app)
    {
        app.MapGet("/api/approval-work/v2/pending", GetPendingAsync);
        app.MapPost("/api/approval-work/v2/bulk-complete", BulkCompleteAsync);
        app.MapPost("/api/timesheet/ptc/non-project-activities", CreateNonProjectActivityAsync);
        return app;
    }

    public static async Task<IResult> GetPendingAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);

        var access = await LoadAccessAsync(connection, context, context.RequestAborted);
        if (access is null) return SessionRequired();
        if (!access.CanViewAnyApprovalWork)
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "No approval work is assigned to the current role."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var stageText = context.Request.Query["stage"].FirstOrDefault();
        var stage = string.IsNullOrWhiteSpace(stageText) ? null : NormalizeStage(stageText);
        if (!string.IsNullOrWhiteSpace(stageText) && stage is null)
        {
            return Results.BadRequest(new
            {
                status = "invalid_stage",
                message = "Approval stage must be manager, pm, or ptc."
            });
        }

        DateOnly? requestedWeek = null;
        var weekText = context.Request.Query["weekStart"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(weekText))
        {
            if (!DateOnly.TryParse(weekText, out var parsedWeek) || parsedWeek.DayOfWeek != DayOfWeek.Sunday)
            {
                return Results.BadRequest(new
                {
                    status = "invalid_week_start",
                    message = "WeekStart must be a Sunday in YYYY-MM-DD format."
                });
            }
            requestedWeek = parsedWeek;
        }

        var page = PositiveInt(context.Request.Query["page"].FirstOrDefault(), 1);
        var pageSize = Math.Clamp(
            PositiveInt(context.Request.Query["pageSize"].FirstOrDefault(), DefaultPageSize),
            1,
            MaximumPageSize);
        var search = Clean(context.Request.Query["search"].FirstOrDefault()).ToLowerInvariant();

        // Complete stage/week aggregates are calculated from the full authorized
        // set. Detail rows are then filtered and paged without silently changing
        // the authoritative totals.
        var allItems = await LoadCandidatesAsync(
            connection,
            transaction: null,
            access,
            stage: null,
            weekStart: null,
            context.RequestAborted);

        var stageCounts = new
        {
            manager = allItems.Count(item => item.Stage == "manager"),
            pm = allItems.Count(item => item.Stage == "pm"),
            ptc = allItems.Count(item => item.Stage == "ptc")
        };

        var pendingWeeks = allItems
            .GroupBy(item => new
            {
                item.Stage,
                item.StageLabel,
                item.WeekStart,
                item.WeekEnd
            })
            .Select(group => new
            {
                group.Key.Stage,
                group.Key.StageLabel,
                group.Key.WeekStart,
                group.Key.WeekEnd,
                count = group.Count(),
                totalHours = decimal.Round(group.Sum(item => item.TotalHours), 2),
                oldestPendingAt = group.Min(item => item.PendingAt),
                newestPendingAt = group.Max(item => item.PendingAt),
                projectApprovalGroups = group.Count(item => item.ApprovalUnitType == "project_scope"),
                nonProjectOnlyDays = group.Count(item => item.NonProjectOnly)
            })
            .OrderBy(group => group.WeekStart)
            .ThenBy(group => StageOrder(group.Stage))
            .ToArray();

        IEnumerable<ApprovalWorkItem> filtered = allItems;
        if (stage is not null) filtered = filtered.Where(item => item.Stage == stage);
        if (requestedWeek.HasValue)
        {
            filtered = filtered.Where(item => item.WeekStart == requestedWeek.Value);
        }
        if (search.Length > 0)
        {
            filtered = filtered.Where(item => ItemMatchesSearch(item, search));
        }

        var filteredItems = filtered.ToArray();
        var offset = (page - 1) * pageSize;
        var pageItems = filteredItems.Skip(offset).Take(pageSize).ToArray();

        return Results.Ok(new
        {
            status = "pending_approval_work_loaded",
            apiContractVersion = ContractVersion,
            refreshedAtUtc = DateTimeOffset.UtcNow,
            aggregationComplete = true,
            totalPending = allItems.Count,
            filteredCount = filteredItems.Length,
            page,
            pageSize,
            returnedCount = pageItems.Length,
            hasMore = offset + pageItems.Length < filteredItems.Length,
            nextPage = offset + pageItems.Length < filteredItems.Length ? page + 1 : (int?)null,
            stageCounts,
            access = AccessPayload(access),
            pendingWeeks,
            items = pageItems
        });
    }

    public static async Task<IResult> BulkCompleteAsync(
        BulkCompleteRequest request,
        HttpContext context)
    {
        var stage = NormalizeStage(request.Stage);
        if (stage is null)
        {
            return Results.BadRequest(new
            {
                status = "invalid_stage",
                message = "Approval stage must be manager, pm, or ptc."
            });
        }

        if (!request.WeekStart.HasValue || request.WeekStart.Value.DayOfWeek != DayOfWeek.Sunday)
        {
            return Results.BadRequest(new
            {
                status = "week_required",
                message = "Select a valid Sunday week start before completing approval work."
            });
        }

        if (request.Items is { Count: 0 })
        {
            return Results.BadRequest(new
            {
                status = "empty_selection",
                message = "No approval items were selected. An empty selection can never approve an entire week."
            });
        }

        if (request.Items is { Count: > MaximumSelectedItems })
        {
            return Results.Json(new
            {
                status = "selection_too_large",
                message = $"Select no more than {MaximumSelectedItems} approval units at one time, or use Approve entire week."
            }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var mode = NormalizeMode(request.Mode, request.Items);
        if (mode is null)
        {
            return Results.BadRequest(new
            {
                status = "invalid_bulk_mode",
                message = "Bulk mode must be selected or week."
            });
        }

        if (mode == "selected" && request.Items is null)
        {
            return Results.BadRequest(new
            {
                status = "selection_required",
                message = "Selected mode requires at least one explicit approval item."
            });
        }

        if (mode == "week" && request.Items is not null)
        {
            return Results.BadRequest(new
            {
                status = "ambiguous_bulk_request",
                message = "Approve-entire-week mode must not include an item selection."
            });
        }

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var access = await LoadAccessAsync(connection, context, context.RequestAborted);
        if (access is null) return SessionRequired();
        if (access.IsViewAs)
        {
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Approval decisions are disabled while using Administrator View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        }
        if (!CanCompleteStage(access, stage))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = $"The current role cannot complete the {StageLabel(stage)} stage."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        var batchId = Guid.NewGuid();
        try
        {
            var candidates = await LoadCandidatesAsync(
                connection,
                transaction,
                access,
                stage,
                request.WeekStart,
                context.RequestAborted);

            var authorizedCandidateCount = candidates.Count;
            if (mode == "selected")
            {
                var selected = request.Items!
                    .Select(SelectionKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                candidates = candidates
                    .Where(item => selected.Contains(ItemKey(item)))
                    .ToList();

                if (candidates.Count == 0)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Json(new
                    {
                        status = "selection_no_longer_actionable",
                        message = "None of the selected approval items remain pending within your authorized scope."
                    }, statusCode: StatusCodes.Status409Conflict);
                }
            }

            if (candidates.Count == 0)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Ok(new
                {
                    status = "nothing_to_complete",
                    apiContractVersion = ContractVersion,
                    batchId,
                    stage,
                    mode,
                    weekStart = request.WeekStart,
                    completedCount = 0,
                    skippedCount = 0,
                    commentRequired = false,
                    message = "No pending approval work remains for the selected stage and week."
                });
            }

            var hasStageEvents = await TableExistsAsync(
                connection,
                transaction,
                "scoped_approval_stage_events",
                context.RequestAborted);
            var hasImmutablePolicyEvents = await TableExistsAsync(
                connection,
                transaction,
                "scoped_role_policy_audit_events",
                context.RequestAborted);

            var systemReason = BuildSystemApprovalReason(
                access,
                stage,
                request.WeekStart.Value,
                batchId,
                mode);
            var completed = 0;
            var skipped = 0;

            foreach (var item in candidates)
            {
                var changed = await CompleteItemAsync(
                    connection,
                    transaction,
                    access,
                    stage,
                    item,
                    batchId,
                    systemReason,
                    hasStageEvents,
                    context.RequestAborted);
                if (changed) completed++;
                else skipped++;
            }

            if (hasImmutablePolicyEvents)
            {
                await RecordImmutableBatchAsync(
                    connection,
                    transaction,
                    access,
                    batchId,
                    stage,
                    mode,
                    request.WeekStart.Value,
                    candidates.Count,
                    completed,
                    skipped,
                    systemReason,
                    context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = completed > 0 ? "approval_work_bulk_completed" : "nothing_to_complete",
                apiContractVersion = ContractVersion,
                batchId,
                stage,
                stageLabel = StageLabel(stage),
                mode,
                weekStart = request.WeekStart,
                weekEnd = request.WeekStart.Value.AddDays(6),
                authorizedCandidateCount,
                requestedCount = candidates.Count,
                completedCount = completed,
                skippedCount = skipped,
                commentRequired = false,
                immutableEvidenceRecorded = hasStageEvents || hasImmutablePolicyEvents,
                message = completed > 0
                    ? $"Completed {completed} {StageLabel(stage).ToLowerInvariant()} approval unit(s) for the selected week."
                    : "No pending approval items remained when the request was processed."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            LogFailure("bulk approval", context, exception);
            return SafeFailure(
                context,
                "Bulk approval could not be completed",
                "The approval request was not completed. Refresh the queue and try again.");
        }
    }

    public static async Task<IResult> CreateNonProjectActivityAsync(
        NonProjectActivityRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var access = await LoadAccessAsync(connection, context, context.RequestAborted);
        if (access is null) return SessionRequired();
        if (access.IsViewAs)
        {
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Non-project activity creation is disabled while using Administrator View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        }
        if (!access.RoleCodes.Any(OrganizationApprovalRoles.Contains))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Only a Project Team Coordinator or administrator may create a non-project activity."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var code = NormalizeCode(request.TaskCode);
        var name = Clean(request.TaskName);
        var description = Clean(request.TaskDescription);
        var classification = Clean(request.UtilizationClassification).ToLowerInvariant();
        var reason = Clean(request.Reason);
        var requiresApproval = request.RequiresApproval ?? true;

        if (code.Length is < 2 or > 100)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Activity code must contain 2 to 100 letters, numbers, periods, underscores, or hyphens."
            });
        }
        if (ProtectedNonProjectCodes.Contains(code))
        {
            return Results.Json(new
            {
                status = "protected_activity_code",
                message = "That code belongs to a system-managed non-project activity and cannot be replaced or reactivated here."
            }, statusCode: StatusCodes.Status409Conflict);
        }
        if (name.Length is < 2 or > 255)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Activity name must contain 2 to 255 characters."
            });
        }
        if (description.Length > 2000)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Activity description may not exceed 2,000 characters."
            });
        }
        if (classification.Length == 0) classification = "non_billable";
        if (!AllowedClassifications.Contains(classification))
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Select a supported non-project utilization classification."
            });
        }
        if (reason.Length < 5)
        {
            return Results.BadRequest(new
            {
                status = "reason_required",
                message = "Enter a specific business reason for creating the non-project activity."
            });
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            // Serialize same-code creation attempts so two coordinators cannot
            // create competing activities in a race.
            await using (var advisory = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext(UPPER(@category_code)));",
                connection,
                transaction))
            {
                advisory.Parameters.AddWithValue("category_code", code);
                await advisory.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await using (var duplicate = new NpgsqlCommand("""
                SELECT non_project_time_category_id, is_active
                FROM non_project_time_categories
                WHERE UPPER(category_code) = UPPER(@category_code)
                LIMIT 1;
                """, connection, transaction))
            {
                duplicate.Parameters.AddWithValue("category_code", code);
                await using var reader = await duplicate.ExecuteReaderAsync(context.RequestAborted);
                if (await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Json(new
                    {
                        status = "activity_code_exists",
                        existingActivityId = reader.GetGuid(0),
                        existingActivityActive = reader.GetBoolean(1),
                        message = "That activity code already exists. Select the existing activity or enter a new code; creation never overwrites an existing category."
                    }, statusCode: StatusCodes.Status409Conflict);
                }
            }

            var activityId = Guid.NewGuid();
            var displayOrder = request.DisplayOrder is >= 0 and <= 10000
                ? request.DisplayOrder.Value
                : 500;
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO non_project_time_categories (
                    non_project_time_category_id,
                    category_code,
                    category_name,
                    category_description,
                    utilization_classification,
                    requires_approval,
                    is_active,
                    display_order,
                    created_at,
                    updated_at
                ) VALUES (
                    @category_id,
                    @category_code,
                    @category_name,
                    @category_description,
                    @classification,
                    @requires_approval,
                    TRUE,
                    @display_order,
                    NOW(),
                    NOW()
                );
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("category_id", activityId);
                insert.Parameters.AddWithValue("category_code", code);
                insert.Parameters.AddWithValue("category_name", name);
                insert.Parameters.AddWithValue("category_description", description);
                insert.Parameters.AddWithValue("classification", classification);
                insert.Parameters.AddWithValue("requires_approval", requiresApproval);
                insert.Parameters.AddWithValue("display_order", displayOrder);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }

            if (await TableExistsAsync(
                    connection,
                    transaction,
                    "scoped_role_policy_audit_events",
                    context.RequestAborted))
            {
                await using var immutableAudit = new NpgsqlCommand("""
                    INSERT INTO scoped_role_policy_audit_events (
                        event_code,
                        actor_user_id,
                        actor_email,
                        reason,
                        previous_state,
                        new_state,
                        event_metadata
                    ) VALUES (
                        'NON_PROJECT_ACTIVITY_CREATED',
                        @actor_user_id,
                        @actor_email,
                        @reason,
                        '{}'::jsonb,
                        @new_state::jsonb,
                        @metadata::jsonb
                    );
                    """, connection, transaction);
                immutableAudit.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
                immutableAudit.Parameters.AddWithValue("actor_email", access.Email);
                immutableAudit.Parameters.AddWithValue("reason", reason);
                immutableAudit.Parameters.AddWithValue("new_state", JsonSerializer.Serialize(new
                {
                    nonProjectTimeCategoryId = activityId,
                    taskCode = code,
                    taskName = name,
                    taskDescription = description,
                    utilizationClassification = classification,
                    requiresApproval,
                    isActive = true,
                    displayOrder
                }));
                immutableAudit.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
                {
                    apiContractVersion = ContractVersion,
                    destinationType = "non_project",
                    protectedSystemActivity = false,
                    createOnly = true
                }));
                await immutableAudit.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await InsertPlatformAuditAsync(
                connection,
                transaction,
                access.ActualUserId,
                "ptc_non_project_activity_created",
                "non_project_time_category",
                activityId,
                context.RequestAborted);

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "non_project_activity_created",
                apiContractVersion = ContractVersion,
                message = "The non-project activity was created and is immediately available as a Move Time destination.",
                nonProjectTimeCategoryId = activityId,
                taskCode = code,
                taskName = name,
                utilizationClassification = classification,
                requiresApproval,
                destinationType = "non_project",
                selectionValue = $"category:{activityId:D}",
                projectId = (Guid?)null,
                immutableEvidenceRecorded = true
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Json(new
            {
                status = "activity_code_exists",
                message = "That activity code was created by another request. Refresh the destination list and select it, or enter a new code."
            }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            LogFailure("non-project activity creation", context, exception);
            return SafeFailure(
                context,
                "Non-project activity could not be created",
                "The activity was not created. Refresh the Move Time workspace and try again.");
        }
    }

    private static async Task<List<ApprovalWorkItem>> LoadCandidatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApprovalAccess access,
        string? stage,
        DateOnly? weekStart,
        CancellationToken cancellationToken)
    {
        var items = new List<ApprovalWorkItem>();
        var weekEnd = weekStart?.AddDays(6);
        await using var command = new NpgsqlCommand("""
            WITH pending_days AS (
                SELECT
                    tds.timesheet_id,
                    tds.user_id,
                    tds.work_date,
                    tds.status,
                    COALESCE(
                        CASE
                            WHEN tds.status = 'submitted' THEN tds.submitted_at
                            WHEN tds.status = 'manager_approved'
                                THEN NULLIF(to_jsonb(tds)->>'manager_approved_at', '')::timestamptz
                            WHEN tds.status = 'pm_approved'
                                THEN NULLIF(to_jsonb(tds)->>'pm_approved_at', '')::timestamptz
                            ELSE NULL
                        END,
                        tds.updated_at,
                        tds.created_at
                    ) AS pending_at
                FROM timesheet_day_statuses tds
                WHERE tds.status IN ('submitted', 'manager_approved', 'pm_approved')
                  AND (
                        @week_start IS NULL
                        OR tds.work_date BETWEEN @week_start AND @week_end
                  )
            ),
            manager_items AS (
                SELECT
                    pending.timesheet_id,
                    pending.user_id,
                    timesheet.week_start_date,
                    timesheet.week_end_date,
                    pending.work_date,
                    'manager'::text AS stage,
                    'Manager review'::text AS stage_label,
                    pending.status,
                    pending.pending_at,
                    COALESCE(NULLIF(submitter.display_name, ''), submitter.email, 'Unknown resource') AS resource_name,
                    COALESCE(submitter.email, '') AS resource_email,
                    COALESCE(SUM(entry.hours), 0)::numeric AS total_hours,
                    COUNT(entry.time_entry_id)::bigint AS entry_count,
                    NULL::uuid AS project_id,
                    COALESCE(
                        STRING_AGG(DISTINCT NULLIF(project.project_code, ''), ', ')
                            FILTER (WHERE NULLIF(project.project_code, '') IS NOT NULL),
                        ''
                    ) AS project_codes,
                    COALESCE(
                        STRING_AGG(DISTINCT NULLIF(project.project_name, ''), ', ')
                            FILTER (WHERE NULLIF(project.project_name, '') IS NOT NULL),
                        ''
                    ) AS project_names,
                    'day'::text AS scope_key,
                    CASE
                        WHEN COUNT(entry.time_entry_id) > 0 AND COUNT(entry.project_id) = 0
                            THEN 'non_project_day'
                        ELSE 'timesheet_day'
                    END::text AS approval_unit_type,
                    (COUNT(entry.time_entry_id) > 0 AND COUNT(entry.project_id) = 0) AS non_project_only,
                    (COUNT(*) FILTER (
                        WHERE entry.time_entry_id IS NOT NULL AND entry.project_id IS NULL
                    ) > 0) AS contains_non_project_time
                FROM pending_days pending
                JOIN timesheets timesheet ON timesheet.timesheet_id = pending.timesheet_id
                JOIN app_users submitter ON submitter.user_id = pending.user_id
                LEFT JOIN time_entries entry
                  ON entry.timesheet_id = pending.timesheet_id
                 AND entry.work_date = pending.work_date
                LEFT JOIN projects project ON project.project_id = entry.project_id
                WHERE pending.status = 'submitted'
                  AND pending.user_id <> @effective_user_id
                  AND @can_manager_approve
                  AND (
                        @organization_scope
                        OR (
                            @is_manager
                            AND lower(COALESCE(submitter.manager_email, '')) = lower(@actor_email)
                        )
                  )
                GROUP BY
                    pending.timesheet_id,
                    pending.user_id,
                    timesheet.week_start_date,
                    timesheet.week_end_date,
                    pending.work_date,
                    pending.status,
                    pending.pending_at,
                    submitter.display_name,
                    submitter.email
            ),
            pm_items AS (
                SELECT
                    pending.timesheet_id,
                    pending.user_id,
                    timesheet.week_start_date,
                    timesheet.week_end_date,
                    pending.work_date,
                    'pm'::text AS stage,
                    'PM review'::text AS stage_label,
                    pending.status,
                    pending.pending_at,
                    COALESCE(NULLIF(submitter.display_name, ''), submitter.email, 'Unknown resource') AS resource_name,
                    COALESCE(submitter.email, '') AS resource_email,
                    COALESCE(SUM(entry.hours), 0)::numeric AS total_hours,
                    COUNT(entry.time_entry_id)::bigint AS entry_count,
                    project.project_id,
                    COALESCE(project.project_code, '') AS project_codes,
                    COALESCE(project.project_name, '') AS project_names,
                    ('project:' || project.project_id::text)::text AS scope_key,
                    'project_scope'::text AS approval_unit_type,
                    FALSE AS non_project_only,
                    FALSE AS contains_non_project_time
                FROM pending_days pending
                JOIN timesheets timesheet ON timesheet.timesheet_id = pending.timesheet_id
                JOIN app_users submitter ON submitter.user_id = pending.user_id
                JOIN time_entries entry
                  ON entry.timesheet_id = pending.timesheet_id
                 AND entry.work_date = pending.work_date
                 AND entry.project_id IS NOT NULL
                JOIN projects project ON project.project_id = entry.project_id
                WHERE pending.status = 'manager_approved'
                  AND pending.user_id <> @effective_user_id
                  AND @can_project_approve
                  AND entry.status = 'manager_approved'
                  AND (
                        @organization_scope
                        OR (
                            @is_project_manager
                            AND project.project_manager_user_id = @effective_user_id
                        )
                  )
                  AND NOT EXISTS (
                        SELECT 1
                        FROM approval_records approval
                        WHERE approval.time_entry_id = entry.time_entry_id
                          AND approval.approval_stage = 'project_manager'
                          AND approval.approval_status = 'approved'
                  )
                GROUP BY
                    pending.timesheet_id,
                    pending.user_id,
                    timesheet.week_start_date,
                    timesheet.week_end_date,
                    pending.work_date,
                    pending.status,
                    pending.pending_at,
                    submitter.display_name,
                    submitter.email,
                    project.project_id,
                    project.project_code,
                    project.project_name
            ),
            ptc_items AS (
                SELECT
                    pending.timesheet_id,
                    pending.user_id,
                    timesheet.week_start_date,
                    timesheet.week_end_date,
                    pending.work_date,
                    'ptc'::text AS stage,
                    'PTC final review'::text AS stage_label,
                    pending.status,
                    pending.pending_at,
                    COALESCE(NULLIF(submitter.display_name, ''), submitter.email, 'Unknown resource') AS resource_name,
                    COALESCE(submitter.email, '') AS resource_email,
                    COALESCE(SUM(entry.hours), 0)::numeric AS total_hours,
                    COUNT(entry.time_entry_id)::bigint AS entry_count,
                    NULL::uuid AS project_id,
                    COALESCE(
                        STRING_AGG(DISTINCT NULLIF(project.project_code, ''), ', ')
                            FILTER (WHERE NULLIF(project.project_code, '') IS NOT NULL),
                        ''
                    ) AS project_codes,
                    COALESCE(
                        STRING_AGG(DISTINCT NULLIF(project.project_name, ''), ', ')
                            FILTER (WHERE NULLIF(project.project_name, '') IS NOT NULL),
                        ''
                    ) AS project_names,
                    'day'::text AS scope_key,
                    CASE
                        WHEN COUNT(entry.time_entry_id) > 0 AND COUNT(entry.project_id) = 0
                            THEN 'non_project_day'
                        ELSE 'timesheet_day'
                    END::text AS approval_unit_type,
                    (COUNT(entry.time_entry_id) > 0 AND COUNT(entry.project_id) = 0) AS non_project_only,
                    (COUNT(*) FILTER (
                        WHERE entry.time_entry_id IS NOT NULL AND entry.project_id IS NULL
                    ) > 0) AS contains_non_project_time
                FROM pending_days pending
                JOIN timesheets timesheet ON timesheet.timesheet_id = pending.timesheet_id
                JOIN app_users submitter ON submitter.user_id = pending.user_id
                LEFT JOIN time_entries entry
                  ON entry.timesheet_id = pending.timesheet_id
                 AND entry.work_date = pending.work_date
                LEFT JOIN projects project ON project.project_id = entry.project_id
                WHERE pending.user_id <> @effective_user_id
                  AND @can_ptc_final_approve
                  AND @organization_scope
                  AND (
                        pending.status = 'pm_approved'
                        OR (
                            pending.status = 'manager_approved'
                            AND NOT EXISTS (
                                SELECT 1
                                FROM time_entries project_entry
                                WHERE project_entry.timesheet_id = pending.timesheet_id
                                  AND project_entry.work_date = pending.work_date
                                  AND project_entry.project_id IS NOT NULL
                            )
                        )
                  )
                GROUP BY
                    pending.timesheet_id,
                    pending.user_id,
                    timesheet.week_start_date,
                    timesheet.week_end_date,
                    pending.work_date,
                    pending.status,
                    pending.pending_at,
                    submitter.display_name,
                    submitter.email
            ),
            candidates AS (
                SELECT * FROM manager_items
                UNION ALL
                SELECT * FROM pm_items
                UNION ALL
                SELECT * FROM ptc_items
            )
            SELECT
                timesheet_id,
                user_id,
                week_start_date,
                week_end_date,
                work_date,
                stage,
                stage_label,
                status,
                pending_at,
                resource_name,
                resource_email,
                total_hours,
                entry_count,
                project_id,
                project_codes,
                project_names,
                scope_key,
                approval_unit_type,
                non_project_only,
                contains_non_project_time
            FROM candidates
            WHERE (@stage_filter = '' OR stage = @stage_filter)
            ORDER BY
                week_start_date,
                work_date,
                CASE stage WHEN 'manager' THEN 0 WHEN 'pm' THEN 1 ELSE 2 END,
                resource_name,
                project_codes;
            """, connection, transaction);

        command.Parameters.AddWithValue("stage_filter", stage ?? string.Empty);
        command.Parameters.Add("week_start", NpgsqlDbType.Date).Value =
            weekStart.HasValue ? weekStart.Value : DBNull.Value;
        command.Parameters.Add("week_end", NpgsqlDbType.Date).Value =
            weekEnd.HasValue ? weekEnd.Value : DBNull.Value;
        command.Parameters.AddWithValue("effective_user_id", access.EffectiveUserId);
        command.Parameters.AddWithValue("actor_email", access.Email);
        command.Parameters.AddWithValue("organization_scope", access.OrganizationScope);
        command.Parameters.AddWithValue("is_manager", access.IsManager);
        command.Parameters.AddWithValue("is_project_manager", access.IsProjectManager);
        command.Parameters.AddWithValue("can_manager_approve", access.CanManagerApprove);
        command.Parameters.AddWithValue("can_project_approve", access.CanProjectApprove);
        command.Parameters.AddWithValue("can_ptc_final_approve", access.CanPtcFinalApprove);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ApprovalWorkItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetDecimal(11),
                reader.GetInt64(12),
                reader.IsDBNull(13) ? null : reader.GetGuid(13),
                reader.GetString(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetBoolean(18),
                reader.GetBoolean(19)));
        }

        return items;
    }

    private static async Task<bool> CompleteItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        string stage,
        ApprovalWorkItem item,
        Guid batchId,
        string systemReason,
        bool hasStageEvents,
        CancellationToken cancellationToken) => stage switch
        {
            "manager" => await CompleteManagerItemAsync(
                connection, transaction, access, item, batchId, systemReason, hasStageEvents, cancellationToken),
            "pm" => await CompleteProjectManagerItemAsync(
                connection, transaction, access, item, batchId, systemReason, hasStageEvents, cancellationToken),
            "ptc" => await CompletePtcItemAsync(
                connection, transaction, access, item, batchId, systemReason, hasStageEvents, cancellationToken),
            _ => false
        };

    private static async Task<bool> CompleteManagerItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        ApprovalWorkItem item,
        Guid batchId,
        string reason,
        bool hasStageEvents,
        CancellationToken cancellationToken)
    {
        var currentStatus = await LockDayStatusAsync(
            connection, transaction, item.TimesheetId, item.WorkDate, cancellationToken);
        if (currentStatus != "submitted") return false;

        await using (var updateDay = new NpgsqlCommand("""
            UPDATE timesheet_day_statuses
            SET status = 'manager_approved',
                manager_user_id = @actor_user_id,
                manager_decision_comment = @reason,
                manager_approved_at = NOW(),
                manager_declined_at = NULL,
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
              AND status = 'submitted';
            """, connection, transaction))
        {
            updateDay.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            updateDay.Parameters.AddWithValue("reason", reason);
            updateDay.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            updateDay.Parameters.AddWithValue("work_date", item.WorkDate);
            if (await updateDay.ExecuteNonQueryAsync(cancellationToken) == 0) return false;
        }

        await using (var updateEntries = new NpgsqlCommand("""
            UPDATE time_entries
            SET status = 'manager_approved', updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
              AND status = 'submitted';
            """, connection, transaction))
        {
            updateEntries.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            updateEntries.Parameters.AddWithValue("work_date", item.WorkDate);
            await updateEntries.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertApprovalRecordsForDayAsync(
            connection,
            transaction,
            item.TimesheetId,
            item.WorkDate,
            "manager",
            access.ActualUserId,
            reason,
            cancellationToken);
        await RecordItemEvidenceAsync(
            connection,
            transaction,
            access,
            item,
            batchId,
            "MANAGER",
            "submitted",
            "manager_approved",
            "timesheet_day_manager_bulk_approved",
            reason,
            hasStageEvents,
            partialProjectApproval: false,
            cancellationToken);
        return true;
    }

    private static async Task<bool> CompleteProjectManagerItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        ApprovalWorkItem item,
        Guid batchId,
        string reason,
        bool hasStageEvents,
        CancellationToken cancellationToken)
    {
        if (!item.ProjectId.HasValue) return false;
        var currentStatus = await LockDayStatusAsync(
            connection, transaction, item.TimesheetId, item.WorkDate, cancellationToken);
        if (currentStatus != "manager_approved") return false;

        var entryIds = new List<Guid>();
        await using (var selectEntries = new NpgsqlCommand("""
            SELECT entry.time_entry_id
            FROM time_entries entry
            JOIN projects project ON project.project_id = entry.project_id
            WHERE entry.timesheet_id = @timesheet_id
              AND entry.work_date = @work_date
              AND entry.project_id = @project_id
              AND entry.status = 'manager_approved'
              AND (
                    @organization_scope
                    OR project.project_manager_user_id = @effective_user_id
              )
              AND NOT EXISTS (
                    SELECT 1
                    FROM approval_records approval
                    WHERE approval.time_entry_id = entry.time_entry_id
                      AND approval.approval_stage = 'project_manager'
                      AND approval.approval_status = 'approved'
              )
            ORDER BY entry.time_entry_id
            FOR UPDATE OF entry;
            """, connection, transaction))
        {
            selectEntries.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            selectEntries.Parameters.AddWithValue("work_date", item.WorkDate);
            selectEntries.Parameters.AddWithValue("project_id", item.ProjectId.Value);
            selectEntries.Parameters.AddWithValue("organization_scope", access.OrganizationScope);
            selectEntries.Parameters.AddWithValue("effective_user_id", access.EffectiveUserId);
            await using var reader = await selectEntries.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) entryIds.Add(reader.GetGuid(0));
        }
        if (entryIds.Count == 0) return false;

        await using (var approvals = new NpgsqlCommand("""
            INSERT INTO approval_records (
                time_entry_id,
                approval_stage,
                approval_status,
                approver_user_id,
                decision_comment
            )
            SELECT
                entry.time_entry_id,
                'project_manager',
                'approved',
                @actor_user_id,
                @reason
            FROM time_entries entry
            WHERE entry.time_entry_id = ANY(@entry_ids)
              AND NOT EXISTS (
                    SELECT 1
                    FROM approval_records existing
                    WHERE existing.time_entry_id = entry.time_entry_id
                      AND existing.approval_stage = 'project_manager'
                      AND existing.approval_status = 'approved'
              );
            """, connection, transaction))
        {
            approvals.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            approvals.Parameters.AddWithValue("reason", reason);
            approvals.Parameters.AddWithValue("entry_ids", entryIds.ToArray());
            await approvals.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateEntries = new NpgsqlCommand("""
            UPDATE time_entries
            SET status = 'pm_approved', updated_at = NOW()
            WHERE time_entry_id = ANY(@entry_ids)
              AND status = 'manager_approved';
            """, connection, transaction))
        {
            updateEntries.Parameters.AddWithValue("entry_ids", entryIds.ToArray());
            await updateEntries.ExecuteNonQueryAsync(cancellationToken);
        }

        bool projectApprovalsRemain;
        await using (var outstanding = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM time_entries entry
                WHERE entry.timesheet_id = @timesheet_id
                  AND entry.work_date = @work_date
                  AND entry.project_id IS NOT NULL
                  AND NOT EXISTS (
                        SELECT 1
                        FROM approval_records approval
                        WHERE approval.time_entry_id = entry.time_entry_id
                          AND approval.approval_stage = 'project_manager'
                          AND approval.approval_status = 'approved'
                  )
            );
            """, connection, transaction))
        {
            outstanding.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            outstanding.Parameters.AddWithValue("work_date", item.WorkDate);
            projectApprovalsRemain = Convert.ToBoolean(
                await outstanding.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        if (!projectApprovalsRemain)
        {
            await using var advanceDay = new NpgsqlCommand("""
                UPDATE timesheet_day_statuses
                SET status = 'pm_approved',
                    pm_approved_by_user_id = @actor_user_id,
                    pm_approved_at = NOW(),
                    pm_decision_comment = @reason,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'manager_approved';
                """, connection, transaction);
            advanceDay.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            advanceDay.Parameters.AddWithValue("reason", reason);
            advanceDay.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            advanceDay.Parameters.AddWithValue("work_date", item.WorkDate);
            await advanceDay.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordItemEvidenceAsync(
            connection,
            transaction,
            access,
            item,
            batchId,
            "PROJECT_MANAGER",
            "manager_approved",
            projectApprovalsRemain ? "manager_approved" : "pm_approved",
            "timesheet_project_scope_bulk_approved",
            reason,
            hasStageEvents,
            partialProjectApproval: projectApprovalsRemain,
            cancellationToken);
        return true;
    }

    private static async Task<bool> CompletePtcItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        ApprovalWorkItem item,
        Guid batchId,
        string reason,
        bool hasStageEvents,
        CancellationToken cancellationToken)
    {
        var currentStatus = await LockDayStatusAsync(
            connection, transaction, item.TimesheetId, item.WorkDate, cancellationToken);
        if (currentStatus is not ("pm_approved" or "manager_approved")) return false;
        if (currentStatus == "manager_approved"
            && !await IsNonProjectOnlyDayAsync(
                connection, transaction, item.TimesheetId, item.WorkDate, cancellationToken))
        {
            return false;
        }

        await using (var updateDay = new NpgsqlCommand("""
            UPDATE timesheet_day_statuses
            SET status = 'accounting_ready',
                accounting_ready_by_user_id = @actor_user_id,
                accounting_ready_at = NOW(),
                accounting_comment = @reason,
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
              AND status = @expected_status;
            """, connection, transaction))
        {
            updateDay.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            updateDay.Parameters.AddWithValue("reason", reason);
            updateDay.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            updateDay.Parameters.AddWithValue("work_date", item.WorkDate);
            updateDay.Parameters.AddWithValue("expected_status", currentStatus);
            if (await updateDay.ExecuteNonQueryAsync(cancellationToken) == 0) return false;
        }

        await using (var updateEntries = new NpgsqlCommand("""
            UPDATE time_entries
            SET status = 'accounting_ready', updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction))
        {
            updateEntries.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            updateEntries.Parameters.AddWithValue("work_date", item.WorkDate);
            await updateEntries.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertApprovalRecordsForDayAsync(
            connection,
            transaction,
            item.TimesheetId,
            item.WorkDate,
            "accounting",
            access.ActualUserId,
            reason,
            cancellationToken);
        await RecordItemEvidenceAsync(
            connection,
            transaction,
            access,
            item,
            batchId,
            "PTC_FINAL",
            currentStatus,
            "accounting_ready",
            "timesheet_day_ptc_final_bulk_approved",
            reason,
            hasStageEvents,
            partialProjectApproval: false,
            cancellationToken);
        return true;
    }

    private static async Task InsertApprovalRecordsForDayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timesheetId,
        DateOnly workDate,
        string approvalStage,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO approval_records (
                time_entry_id,
                approval_stage,
                approval_status,
                approver_user_id,
                decision_comment
            )
            SELECT
                entry.time_entry_id,
                @approval_stage,
                'approved',
                @actor_user_id,
                @reason
            FROM time_entries entry
            WHERE entry.timesheet_id = @timesheet_id
              AND entry.work_date = @work_date
              AND NOT EXISTS (
                    SELECT 1
                    FROM approval_records existing
                    WHERE existing.time_entry_id = entry.time_entry_id
                      AND existing.approval_stage = @approval_stage
                      AND existing.approval_status = 'approved'
              );
            """, connection, transaction);
        command.Parameters.AddWithValue("approval_stage", approvalStage);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordItemEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        ApprovalWorkItem item,
        Guid batchId,
        string requiredStage,
        string previousStatus,
        string newStatus,
        string auditAction,
        string reason,
        bool hasStageEvents,
        bool partialProjectApproval,
        CancellationToken cancellationToken)
    {
        await InsertPlatformAuditAsync(
            connection,
            transaction,
            access.ActualUserId,
            auditAction,
            "timesheet_day",
            item.TimesheetId,
            cancellationToken);

        if (!hasStageEvents) return;
        var delegated = requiredStage switch
        {
            "MANAGER" => !access.IsManager,
            "PROJECT_MANAGER" => !access.IsProjectManager,
            _ => !access.IsPtc
        };
        await using var command = new NpgsqlCommand("""
            INSERT INTO scoped_approval_stage_events (
                timesheet_id,
                work_date,
                required_stage,
                original_responsible_role,
                original_responsible_user_id,
                acting_user_id,
                acting_role_code,
                delegated_action,
                reason,
                previous_status,
                new_status,
                audit_metadata
            ) VALUES (
                @timesheet_id,
                @work_date,
                @required_stage,
                @original_role,
                NULL,
                @actor_user_id,
                @acting_role_code,
                @delegated_action,
                @reason,
                @previous_status,
                @new_status,
                @metadata::jsonb
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
        command.Parameters.AddWithValue("work_date", item.WorkDate);
        command.Parameters.AddWithValue("required_stage", requiredStage);
        command.Parameters.AddWithValue("original_role", requiredStage);
        command.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
        command.Parameters.AddWithValue("acting_role_code", access.PrimaryRoleCode);
        command.Parameters.AddWithValue("delegated_action", delegated);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("previous_status", previousStatus);
        command.Parameters.AddWithValue("new_status", newStatus);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
        {
            apiContractVersion = ContractVersion,
            batchId,
            bulkApproval = true,
            userCommentRequired = false,
            item.Stage,
            item.ScopeKey,
            item.ApprovalUnitType,
            item.ProjectId,
            item.ProjectCodes,
            item.ProjectNames,
            item.NonProjectOnly,
            item.ContainsNonProjectTime,
            partialProjectApproval,
            item.WeekStart,
            item.WeekEnd,
            item.TotalHours,
            immutableEvidence = true
        }));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordImmutableBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        Guid batchId,
        string stage,
        string mode,
        DateOnly weekStart,
        int requested,
        int completed,
        int skipped,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO scoped_role_policy_audit_events (
                event_code,
                actor_user_id,
                actor_email,
                reason,
                previous_state,
                new_state,
                event_metadata
            ) VALUES (
                'APPROVAL_BULK_COMPLETED',
                @actor_user_id,
                @actor_email,
                @reason,
                @previous_state::jsonb,
                @new_state::jsonb,
                @metadata::jsonb
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
        command.Parameters.AddWithValue("actor_email", access.Email);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("previous_state", JsonSerializer.Serialize(new
        {
            stage,
            weekStart,
            requestedCount = requested
        }));
        command.Parameters.AddWithValue("new_state", JsonSerializer.Serialize(new
        {
            completedCount = completed,
            skippedCount = skipped,
            commentRequired = false
        }));
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
        {
            apiContractVersion = ContractVersion,
            batchId,
            mode,
            access.EffectiveUserId,
            access.PrimaryRoleCode,
            access.ScopeLabel,
            immutableEvidence = true
        }));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPlatformAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        string action,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
            VALUES (@actor_user_id, @action, @entity_type, @entity_id);
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> LockDayStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timesheetId,
        DateOnly workDate,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT status
            FROM timesheet_day_statuses
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static async Task<bool> IsNonProjectOnlyDayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timesheetId,
        DateOnly workDate,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) > 0
                AND COUNT(*) FILTER (WHERE project_id IS NOT NULL) = 0
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction);
        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<ApprovalAccess?> LoadAccessAsync(
        NpgsqlConnection connection,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actualUserId = ContextGuid(context, "ProjectPulseActualUserId")
            ?? ContextGuid(context, "ProjectPulseSessionUserId")
            ?? ContextGuid(context, "ProjectPulseEffectiveUserId");
        if (!actualUserId.HasValue) return null;
        var effectiveUserId = ContextGuid(context, "ProjectPulseEffectiveUserId")
            ?? actualUserId.Value;
        var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context)
            || effectiveUserId != actualUserId.Value;

        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(user_row.email, ''),
                COALESCE(NULLIF(user_row.display_name, ''), user_row.email, 'ProjectPulse user'),
                COALESCE(
                    ARRAY_AGG(DISTINCT UPPER(role_row.role_code))
                        FILTER (WHERE role_row.role_code IS NOT NULL),
                    ARRAY[]::text[]
                )
            FROM app_users user_row
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id = user_row.user_id
             AND assignment.is_active = TRUE
            LEFT JOIN app_roles role_row
              ON role_row.app_role_id = assignment.app_role_id
             AND role_row.is_active = TRUE
            WHERE user_row.user_id = @user_id
              AND user_row.is_active = TRUE
            GROUP BY user_row.user_id, user_row.email, user_row.display_name;
            """, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var email = reader.GetString(0);
        var displayName = reader.GetString(1);
        var roleCodes = reader.GetFieldValue<string[]>(2)
            .Select(role => role.Trim().ToUpperInvariant())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var roles = roleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var organizationScope = roles.Any(OrganizationApprovalRoles.Contains);
        var isManager = roles.Any(ManagerApprovalRoles.Contains);
        var isProjectManager = roles.Any(ProjectApprovalRoles.Contains);
        var isPtc = roles.Contains("PROJECT_TEAM_COORDINATOR");
        var canManagerApprove = organizationScope || isManager;
        var canProjectApprove = organizationScope || isProjectManager;
        var canPtcFinalApprove = organizationScope;
        var scopeLabel = organizationScope
            ? "All organization approvals"
            : isManager && isProjectManager
                ? "Direct reports and managed projects"
                : isManager
                    ? "My direct reports"
                    : isProjectManager
                        ? "My managed projects"
                        : "No approval scope";
        var primaryRoleCode = roleCodes.FirstOrDefault(role => role == "SUPER_ADMINISTRATOR")
            ?? roleCodes.FirstOrDefault(role => role == "ADMINISTRATOR")
            ?? roleCodes.FirstOrDefault(role => role == "PROJECT_TEAM_COORDINATOR")
            ?? roleCodes.FirstOrDefault(ProjectApprovalRoles.Contains)
            ?? roleCodes.FirstOrDefault(ManagerApprovalRoles.Contains)
            ?? roleCodes.FirstOrDefault()
            ?? "UNKNOWN";

        return new ApprovalAccess(
            actualUserId.Value,
            effectiveUserId,
            email,
            displayName,
            roleCodes,
            primaryRoleCode,
            isViewAs,
            organizationScope,
            isManager,
            isProjectManager,
            isPtc,
            canManagerApprove,
            canProjectApprove,
            canPtcFinalApprove,
            scopeLabel);
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.' || @table_name) IS NOT NULL;",
            connection,
            transaction);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static object AccessPayload(ApprovalAccess access) => new
    {
        access.EffectiveUserId,
        access.DisplayName,
        roleCodes = access.RoleCodes,
        access.ScopeLabel,
        access.CanManagerApprove,
        access.CanProjectApprove,
        access.CanPtcFinalApprove,
        access.IsViewAs,
        pmApprovalGranularity = "project_scope",
        nonProjectApprovalRoute = "manager_then_ptc"
    };

    private static bool ItemMatchesSearch(ApprovalWorkItem item, string search) =>
        new[]
        {
            item.ResourceName,
            item.ResourceEmail,
            item.WorkDate.ToString("yyyy-MM-dd"),
            item.ProjectCodes,
            item.ProjectNames,
            item.StageLabel,
            item.NonProjectOnly ? "non-project" : string.Empty
        }.Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static string ItemKey(ApprovalWorkItem item) =>
        $"{item.TimesheetId:D}|{item.WorkDate:yyyy-MM-dd}|{item.Stage}|{item.ProjectId?.ToString("D") ?? "day"}";

    private static string SelectionKey(BulkApprovalSelection item)
    {
        var stage = NormalizeStage(item.Stage) ?? string.Empty;
        return $"{item.TimesheetId:D}|{item.WorkDate:yyyy-MM-dd}|{stage}|{item.ProjectId?.ToString("D") ?? "day"}";
    }

    private static string? NormalizeMode(string? value, List<BulkApprovalSelection>? items)
    {
        var normalized = Clean(value).ToLowerInvariant();
        if (normalized.Length == 0)
        {
            // Backward-compatible inference remains safe because an empty array
            // is rejected before this method is called.
            return items is null ? "week" : "selected";
        }
        return normalized switch
        {
            "selected" or "selection" => "selected",
            "week" or "all" or "entire_week" => "week",
            _ => null
        };
    }

    private static string? NormalizeStage(string? value) => Clean(value).ToLowerInvariant() switch
    {
        "manager" or "manager_review" => "manager",
        "pm" or "project_manager" or "project-manager" => "pm",
        "ptc" or "ptc_final" or "ptc-final" => "ptc",
        _ => null
    };

    private static string StageLabel(string stage) => stage switch
    {
        "manager" => "Manager review",
        "pm" => "PM review",
        "ptc" => "PTC final review",
        _ => "Approval review"
    };

    private static int StageOrder(string stage) => stage switch
    {
        "manager" => 0,
        "pm" => 1,
        "ptc" => 2,
        _ => 9
    };

    private static bool CanCompleteStage(ApprovalAccess access, string stage) => stage switch
    {
        "manager" => access.CanManagerApprove,
        "pm" => access.CanProjectApprove,
        "ptc" => access.CanPtcFinalApprove,
        _ => false
    };

    private static string BuildSystemApprovalReason(
        ApprovalAccess access,
        string stage,
        DateOnly weekStart,
        Guid batchId,
        string mode) =>
        $"{StageLabel(stage)} {mode} approval completed by {access.DisplayName} for week {weekStart:yyyy-MM-dd}; batch {batchId:D}. No user-entered approval comment was required.";

    private static string NormalizeCode(string? value)
    {
        var normalized = Regex.Replace(
            Clean(value).ToUpperInvariant().Replace(' ', '_'),
            "[^A-Z0-9._-]+",
            "_");
        return normalized.Trim('_', '-', '.');
    }

    private static int PositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static Guid? ContextGuid(HttpContext context, string key)
    {
        if (!context.Items.TryGetValue(key, out var value)) return null;
        if (value is Guid guid) return guid;
        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid ProjectPulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult SafeFailure(HttpContext context, string title, string message) =>
        Results.Json(new
        {
            status = "operation_failed",
            title,
            message,
            traceId = context.TraceIdentifier
        }, statusCode: StatusCodes.Status500InternalServerError);

    private static void LogFailure(string operation, HttpContext context, Exception exception) =>
        Console.Error.WriteLine(
            $"ProjectPulse {operation} failed. traceId={context.TraceIdentifier} exception={exception}");

    private static string ConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        throw new InvalidOperationException("ProjectPulse database connection is not configured.");
    }

    public sealed record BulkCompleteRequest(
        string? Mode,
        string? Stage,
        DateOnly? WeekStart,
        List<BulkApprovalSelection>? Items,
        string? RequestId);

    public sealed record BulkApprovalSelection(
        Guid TimesheetId,
        DateOnly WorkDate,
        string? Stage,
        Guid? ProjectId,
        string? ScopeKey);

    public sealed record NonProjectActivityRequest(
        string? TaskCode,
        string? TaskName,
        string? TaskDescription,
        string? UtilizationClassification,
        bool? RequiresApproval,
        int? DisplayOrder,
        string? Reason);

    private sealed record ApprovalAccess(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string Email,
        string DisplayName,
        string[] RoleCodes,
        string PrimaryRoleCode,
        bool IsViewAs,
        bool OrganizationScope,
        bool IsManager,
        bool IsProjectManager,
        bool IsPtc,
        bool CanManagerApprove,
        bool CanProjectApprove,
        bool CanPtcFinalApprove,
        string ScopeLabel)
    {
        public bool CanViewAnyApprovalWork =>
            CanManagerApprove || CanProjectApprove || CanPtcFinalApprove;
    }

    private sealed record ApprovalWorkItem(
        Guid TimesheetId,
        Guid UserId,
        DateOnly WeekStart,
        DateOnly WeekEnd,
        DateOnly WorkDate,
        string Stage,
        string StageLabel,
        string Status,
        DateTimeOffset PendingAt,
        string ResourceName,
        string ResourceEmail,
        decimal TotalHours,
        long EntryCount,
        Guid? ProjectId,
        string ProjectCodes,
        string ProjectNames,
        string ScopeKey,
        string ApprovalUnitType,
        bool NonProjectOnly,
        bool ContainsNonProjectTime);
}