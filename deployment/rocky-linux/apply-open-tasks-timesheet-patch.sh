#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = re.sub(r'version = "0\.4\.[0-9]+"', 'version = "0.4.3"', api)
api = re.sub(r'version = "0\.3\.[0-9]+"', 'version = "0.4.3"', api)

endpoint = r'''
app.MapGet("/api/assignments/open-tasks", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var tasks = await LoadOpenAssignedProjectTasksAsync(connection, userId, start, end);

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = end,
        count = tasks.Count,
        tasks
    });
});

'''

if 'app.MapGet("/api/assignments/open-tasks"' not in api:
    api = api.replace('\napp.Run();', '\n' + endpoint + 'app.Run();', 1)

helper = r'''
static async Task<List<object>> LoadOpenAssignedProjectTasksAsync(NpgsqlConnection connection, Guid userId, DateOnly weekStart, DateOnly weekEnd)
{
    var tasks = new List<object>();

    const string sql = """
        SELECT DISTINCT
            pa.project_assignment_id,
            p.project_id,
            p.project_code,
            p.project_name,
            c.client_name,
            c.client_code,
            pt.task_id,
            pt.task_code,
            pt.task_name,
            pt.task_description,
            COALESCE(pa.allocation_percent, 0) AS allocation_percent,
            pa.effective_start_date,
            pa.effective_end_date,
            p.project_manager_user_id,
            pm.display_name AS project_manager_name
        FROM project_assignments pa
        INNER JOIN projects p ON p.project_id = pa.project_id
        INNER JOIN project_tasks pt ON pt.task_id = pa.task_id
        LEFT JOIN clients c ON c.client_id = p.client_id
        LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
        WHERE pa.user_id = @user_id
          AND p.status = 'active'
          AND pt.is_active = TRUE
          AND pa.effective_start_date <= @week_end
          AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
        ORDER BY p.project_code, pt.task_code;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start", weekStart);
    command.Parameters.AddWithValue("week_end", weekEnd);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        tasks.Add(new
        {
            assignmentId = reader.GetGuid(0),
            projectId = reader.GetGuid(1),
            projectCode = reader.GetString(2),
            projectName = reader.GetString(3),
            clientName = reader.IsDBNull(4) ? null : reader.GetString(4),
            clientCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            taskId = reader.GetGuid(6),
            taskCode = reader.GetString(7),
            taskName = reader.GetString(8),
            taskDescription = reader.IsDBNull(9) ? null : reader.GetString(9),
            allocationPercent = reader.GetDecimal(10),
            effectiveStartDate = reader.GetFieldValue<DateOnly>(11),
            effectiveEndDate = reader.IsDBNull(12) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(12),
            projectManagerUserId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13),
            projectManagerName = reader.IsDBNull(14) ? null : reader.GetString(14)
        });
    }

    return tasks;
}

'''

if 'static async Task<List<object>> LoadOpenAssignedProjectTasksAsync' not in api:
    api = api.replace('static async Task<Guid> GetOrCreateDevelopmentUserIdAsync', helper + 'static async Task<Guid> GetOrCreateDevelopmentUserIdAsync', 1)

api_file.write_text(api)

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

if 'const [openTasks, setOpenTasks]' not in app:
    app = app.replace("  const [activitySource, setActivitySource] = useState('nonProject');", "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });")

app = app.replace(
"""          fetchJson('/api/utilization/targets')
        ]);
""",
"""          fetchJson('/api/utilization/targets'),
          fetchJson(`/api/assignments/open-tasks?weekStart=${selectedWeekStart}`)
        ]);
""")

app = app.replace(
"""        const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult] = await Promise.all([
""",
"""        const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult, openTasksResult] = await Promise.all([
""")

app = app.replace(
"""          setUtilizationTargets({ loading: false, data: targetsResult, error: null });
""",
"""          setUtilizationTargets({ loading: false, data: targetsResult, error: null });
          setOpenTasks({ loading: false, data: openTasksResult, error: null });
""")

app = app.replace(
"""          setUtilizationTargets((current) => ({ ...current, loading: false, error: message }));
""",
"""          setUtilizationTargets((current) => ({ ...current, loading: false, error: message }));
          setOpenTasks((current) => ({ ...current, loading: false, error: message }));
""")

if 'function taskToRow(task)' not in app:
    app = app.replace(
"""function categoryToRow(category) {
""",
"""function taskToRow(task) {
  return {
    id: `project-task-${task.projectId}-${task.taskId}`,
    type: 'projectTask',
    state: 'Draft',
    activity: task.taskName,
    projectDescription: `${task.projectCode} • ${task.projectName}`,
    projectId: task.projectId,
    taskId: task.taskId,
    taskCode: task.taskCode,
    clientName: task.clientName,
    projectManagerName: task.projectManagerName
  };
}

function categoryToRow(category) {
""")

if 'function addTask(task)' not in app:
    app = app.replace(
"""  function addCategory(category) {
    if (!isAnyDayEditable) return;

    const row = categoryToRow(category);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');
  }

""",
"""  function addCategory(category) {
    if (!isAnyDayEditable) return;

    const row = categoryToRow(category);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');
  }

  function addTask(task) {
    if (!isAnyDayEditable) return;

    const row = taskToRow(task);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');
  }

""")

app = app.replace(
"""    savedEntries.forEach((entry) => {
      if (entry.rowType !== 'nonProject' || !entry.categoryCode) return;

      const rowId = `non-project-${entry.categoryCode}`;
""",
"""    savedEntries.forEach((entry) => {
      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) {
        const matchingTask = (openTasks.data?.tasks ?? []).find((task) => task.projectId === entry.projectId && task.taskId === entry.taskId);
        if (matchingTask) {
          rowMap.set(`task-${entry.projectId}-${entry.taskId}`, taskToRow(matchingTask));
        }
      }

      if (entry.rowType !== 'nonProject' || !entry.categoryCode) return;

      const rowId = `non-project-${entry.categoryCode}`;
""")

app = app.replace("  const categories = timesheet.data?.nonProjectCategories ?? [];", "  const categories = timesheet.data?.nonProjectCategories ?? [];\n  const assignedOpenTasks = openTasks.data?.tasks ?? [];")

app = app.replace("<span>{activitySource === 'nonProject' ? categories.length : 0}</span>", "<span>{activitySource === 'nonProject' ? categories.length : activitySource === 'openTasks' ? assignedOpenTasks.length : 0}</span>")

old_block = r'''              {activitySource === 'nonProject' ? (
                <div className="activity-group activity-results">
                  <h4>Non-project time</h4>
                  {categories.map((category) => {
                    const alreadyAdded = activeRows.some((row) => row.categoryCode === category.code);
                    return (
                      <button
                        className="activity-card"
                        type="button"
                        key={category.code}
                        disabled={alreadyAdded || !isAnyDayEditable}
                        onClick={() => addCategory(category)}
                      >
                        <strong>{category.name}</strong>
                        <span>{category.description ?? category.utilizationBucket}</span>
                        <small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div className="empty-activity-state">
                  <strong>{selectedActivitySource.emptyTitle}</strong>
                  <span>{selectedActivitySource.emptyDescription}</span>
                </div>
              )}'''
new_block = r'''              {activitySource === 'nonProject' ? (
                <div className="activity-group activity-results">
                  <h4>Non-project time</h4>
                  {categories.map((category) => {
                    const alreadyAdded = activeRows.some((row) => row.categoryCode === category.code);
                    return (
                      <button
                        className="activity-card"
                        type="button"
                        key={category.code}
                        disabled={alreadyAdded || !isAnyDayEditable}
                        onClick={() => addCategory(category)}
                      >
                        <strong>{category.name}</strong>
                        <span>{category.description ?? category.utilizationBucket}</span>
                        <small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>
                      </button>
                    );
                  })}
                </div>
              ) : activitySource === 'openTasks' ? (
                <div className="activity-group activity-results">
                  <h4>Open tasks</h4>
                  {openTasks.loading ? <span className="muted">Loading assigned tasks...</span> : null}
                  {openTasks.error ? <span className="error-text">{openTasks.error}</span> : null}
                  {!openTasks.loading && !openTasks.error && assignedOpenTasks.length === 0 ? (
                    <div className="empty-activity-state">
                      <strong>{selectedActivitySource.emptyTitle}</strong>
                      <span>{selectedActivitySource.emptyDescription}</span>
                    </div>
                  ) : null}
                  {assignedOpenTasks.map((task) => {
                    const alreadyAdded = activeRows.some((row) => row.projectId === task.projectId && row.taskId === task.taskId);
                    return (
                      <button
                        className="activity-card"
                        type="button"
                        key={`${task.projectId}-${task.taskId}`}
                        disabled={alreadyAdded || !isAnyDayEditable}
                        onClick={() => addTask(task)}
                      >
                        <strong>{task.taskName}</strong>
                        <span>{task.projectCode} • {task.projectName}</span>
                        <small>{task.projectManagerName ? `PM: ${task.projectManagerName}` : 'Project task'}</small>
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div className="empty-activity-state">
                  <strong>{selectedActivitySource.emptyTitle}</strong>
                  <span>{selectedActivitySource.emptyDescription}</span>
                </div>
              )}'''

app = app.replace(old_block, new_block)

app_file.write_text(app)
PY

echo "==> Open Tasks timesheet patch applied"
echo "==> Expected API version after redeploy: 0.4.3"
