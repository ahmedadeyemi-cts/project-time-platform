using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<IResult> Module001TimerTargetsAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;

        var access = await RequireModule001AccessAsync(context, connection, "TIME_VIEW", false);
        if (access.Error is not null) return access.Error;

        var actor = access.Actor!;
        var weekStart = Module001RequestedWeek(context);
        var weekEnd = weekStart.AddDays(6);
        var targets = new List<object>();
        var assignmentCount = 0;
        var nonProjectCount = 0;

        await using (var assignments = new NpgsqlCommand("""
            SELECT pa.project_assignment_id,
                   COALESCE(c.client_name, ''),
                   p.project_id,
                   p.project_code,
                   p.project_name,
                   pt.task_id,
                   pt.task_code,
                   pt.task_name
            FROM project_assignments pa
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt
              ON pt.task_id = pa.task_id
             AND pt.project_id = pa.project_id
            LEFT JOIN clients c ON c.client_id = p.client_id
            WHERE pa.user_id = @user_id
              AND pa.effective_start_date <= @week_end
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
              AND p.status IN ('active','on_hold')
              AND pt.is_active = TRUE
            ORDER BY p.project_code, pt.task_code;
            """, connection))
        {
            assignments.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            assignments.Parameters.AddWithValue("week_start", weekStart);
            assignments.Parameters.AddWithValue("week_end", weekEnd);

            await using var reader = await assignments.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var assignmentId = reader.GetGuid(0);
                var customerName = reader.GetString(1);
                var projectId = reader.GetGuid(2);
                var projectCode = reader.GetString(3);
                var projectName = reader.GetString(4);
                var taskId = reader.GetGuid(5);
                var taskCode = reader.GetString(6);
                var taskName = reader.GetString(7);
                var labelParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(customerName)) labelParts.Add(customerName);
                if (!string.IsNullOrWhiteSpace(projectCode)) labelParts.Add(projectCode);
                labelParts.Add(taskName);

                targets.Add(new
                {
                    targetType = "assignment",
                    targetId = assignmentId,
                    selectionValue = $"assignment:{assignmentId:D}",
                    selectionLabel = string.Join(" · ", labelParts),
                    groupLabel = "Assigned project work",
                    assignmentId,
                    customerName,
                    projectId,
                    projectCode,
                    projectName,
                    taskId,
                    taskCode,
                    taskName
                });
                assignmentCount++;
            }
        }

        await using (var categories = new NpgsqlCommand("""
            SELECT non_project_time_category_id,
                   category_code,
                   category_name
            FROM non_project_time_categories
            WHERE is_active = TRUE
            ORDER BY category_name;
            """, connection))
        {
            await using var reader = await categories.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var categoryId = reader.GetGuid(0);
                var categoryCode = reader.GetString(1);
                var categoryName = reader.GetString(2);

                targets.Add(new
                {
                    targetType = "category",
                    targetId = categoryId,
                    selectionValue = $"category:{categoryId:D}",
                    selectionLabel = categoryName,
                    groupLabel = "Authorized non-project activities",
                    nonProjectTimeCategoryId = categoryId,
                    categoryCode,
                    categoryName
                });
                nonProjectCount++;
            }
        }

        return Results.Ok(new
        {
            weekStart,
            weekEnd,
            count = targets.Count,
            assignmentCount,
            nonProjectCount,
            authoritativeSources = new[] { "project_assignments", "non_project_time_categories" },
            targets
        });
    }
}
