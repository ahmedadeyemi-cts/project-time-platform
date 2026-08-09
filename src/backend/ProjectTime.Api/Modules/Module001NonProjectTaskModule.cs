using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Allows Project Team Coordinators and administrators to create a governed
/// non-project activity that can immediately be used as a Move Time destination.
/// The existing database intentionally requires project_tasks to belong to a
/// project, so standalone tasks are persisted through non_project_time_categories.
/// </summary>
public static partial class Module001NonProjectTaskModule
{
    private static readonly HashSet<string> AuthorizedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
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

    public static WebApplication MapModule001NonProjectTaskEndpoints(this WebApplication app)
    {
        app.MapPost(
            "/api/timesheet/ptc/non-project-tasks",
            CreateOrUpdateNonProjectTaskAsync);
        return app;
    }

    private static async Task<IResult> CreateOrUpdateNonProjectTaskAsync(
        NonProjectTaskRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);

        var access = await LoadAccessAsync(connection, context, context.RequestAborted);
        if (access is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (access.IsViewAs)
        {
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Standalone task creation is disabled while using Administrator View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!access.RoleCodes.Any(AuthorizedRoles.Contains))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Only a Project Team Coordinator or Super Administrator may create a non-project task."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var taskCode = NormalizeCode(request.TaskCode);
        var taskName = Clean(request.TaskName);
        var taskDescription = Clean(request.TaskDescription);
        var classification = Clean(request.UtilizationClassification).ToLowerInvariant();
        var reason = Clean(request.Reason);
        var requiresApproval = request.RequiresApproval ?? true;

        if (taskCode.Length < 2 || taskCode.Length > 100)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Task code must contain 2 to 100 letters, numbers, periods, underscores, or hyphens."
            });
        }

        if (taskName.Length < 2 || taskName.Length > 255)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Task name must contain 2 to 255 characters."
            });
        }

        if (taskDescription.Length > 2000)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Task description may not exceed 2,000 characters."
            });
        }

        if (string.IsNullOrWhiteSpace(classification)) classification = "non_billable";
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
                message = "Enter a specific business reason for creating the standalone task."
            });
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            ExistingCategory? prior = null;
            await using (var priorCommand = new NpgsqlCommand("""
                SELECT
                    non_project_time_category_id,
                    category_code,
                    category_name,
                    COALESCE(category_description, ''),
                    utilization_classification,
                    requires_approval,
                    is_active,
                    display_order
                FROM non_project_time_categories
                WHERE UPPER(category_code) = UPPER(@category_code)
                FOR UPDATE;
                """, connection, transaction))
            {
                priorCommand.Parameters.AddWithValue("category_code", taskCode);
                await using var reader = await priorCommand.ExecuteReaderAsync(context.RequestAborted);
                if (await reader.ReadAsync(context.RequestAborted))
                {
                    prior = new ExistingCategory(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetBoolean(5),
                        reader.GetBoolean(6),
                        reader.GetInt32(7));
                }
            }

            var displayOrder = request.DisplayOrder is >= 0 and <= 10000
                ? request.DisplayOrder.Value
                : prior?.DisplayOrder ?? 100;
            var categoryId = prior?.CategoryId ?? Guid.NewGuid();
            var savedTaskCode = prior?.Code ?? taskCode;

            if (prior is null)
            {
                await using var insert = new NpgsqlCommand("""
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
                    )
                    VALUES (
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
                    )
                    RETURNING non_project_time_category_id;
                    """, connection, transaction);
                insert.Parameters.AddWithValue("category_id", categoryId);
                insert.Parameters.AddWithValue("category_code", taskCode);
                insert.Parameters.AddWithValue("category_name", taskName);
                insert.Parameters.AddWithValue("category_description", taskDescription);
                insert.Parameters.AddWithValue("classification", classification);
                insert.Parameters.AddWithValue("requires_approval", requiresApproval);
                insert.Parameters.AddWithValue("display_order", displayOrder);
                categoryId = (Guid)(await insert.ExecuteScalarAsync(context.RequestAborted) ?? categoryId);
            }
            else
            {
                await using var update = new NpgsqlCommand("""
                    UPDATE non_project_time_categories
                    SET category_name = @category_name,
                        category_description = @category_description,
                        utilization_classification = @classification,
                        requires_approval = @requires_approval,
                        is_active = TRUE,
                        display_order = @display_order,
                        updated_at = NOW()
                    WHERE non_project_time_category_id = @category_id
                    RETURNING non_project_time_category_id;
                    """, connection, transaction);
                update.Parameters.AddWithValue("category_id", categoryId);
                update.Parameters.AddWithValue("category_name", taskName);
                update.Parameters.AddWithValue("category_description", taskDescription);
                update.Parameters.AddWithValue("classification", classification);
                update.Parameters.AddWithValue("requires_approval", requiresApproval);
                update.Parameters.AddWithValue("display_order", displayOrder);
                categoryId = (Guid)(await update.ExecuteScalarAsync(context.RequestAborted) ?? categoryId);
            }

            var oldValue = prior is null
                ? "{}"
                : JsonSerializer.Serialize(new
                {
                    categoryId = prior.CategoryId,
                    categoryCode = prior.Code,
                    categoryName = prior.Name,
                    categoryDescription = prior.Description,
                    utilizationClassification = prior.Classification,
                    requiresApproval = prior.RequiresApproval,
                    isActive = prior.IsActive,
                    displayOrder = prior.DisplayOrder
                });
            var newValue = JsonSerializer.Serialize(new
            {
                categoryId,
                categoryCode = savedTaskCode,
                categoryName = taskName,
                categoryDescription = taskDescription,
                utilizationClassification = classification,
                requiresApproval,
                isActive = true,
                displayOrder,
                destinationType = "non_project",
                reason
            });

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO audit_logs (
                    actor_user_id,
                    action,
                    entity_type,
                    entity_id,
                    old_value,
                    new_value,
                    ip_address,
                    user_agent
                )
                VALUES (
                    @actor_user_id,
                    @action,
                    'non_project_time_category',
                    @category_id,
                    @old_value::jsonb,
                    @new_value::jsonb,
                    NULLIF(@ip_address, '')::inet,
                    @user_agent
                );
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
                audit.Parameters.AddWithValue(
                    "action",
                    prior is null
                        ? "ptc_non_project_task_created"
                        : "ptc_non_project_task_updated");
                audit.Parameters.AddWithValue("category_id", categoryId);
                audit.Parameters.AddWithValue("old_value", oldValue);
                audit.Parameters.AddWithValue("new_value", newValue);
                audit.Parameters.AddWithValue("ip_address", access.IpAddress);
                audit.Parameters.AddWithValue("user_agent", access.UserAgent);
                await audit.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                status = prior is null
                    ? "non_project_task_created"
                    : "non_project_task_updated",
                message = prior is null
                    ? "The non-project task was created and is available as a Move Time destination."
                    : "The non-project task was updated, reactivated, and is available as a Move Time destination.",
                nonProjectTimeCategoryId = categoryId,
                taskCode = savedTaskCode,
                taskName,
                utilizationClassification = classification,
                requiresApproval,
                destinationType = "non_project",
                projectId = (Guid?)null,
                auditReason = reason
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Problem(
                title: "Non-project task could not be created",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
