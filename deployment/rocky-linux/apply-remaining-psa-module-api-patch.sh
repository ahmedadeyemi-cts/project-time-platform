#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.1"', api)

endpoints = r'''
app.MapGet("/api/project-intake/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var requests = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT request_number, client_name, request_title, intake_status, priority, target_start_date, target_completion_date, estimated_hours
        FROM project_intake_requests
        ORDER BY created_at DESC;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            requests.Add(new
            {
                requestNumber = reader.GetString(0),
                clientName = reader.GetString(1),
                title = reader.GetString(2),
                status = reader.GetString(3),
                priority = reader.GetString(4),
                targetStartDate = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5),
                targetCompletionDate = reader.IsDBNull(6) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(6),
                estimatedHours = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7)
            });
        }
    }

    var templates = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT template_code, template_name, service_line, default_phase_count, default_task_count
        FROM project_templates
        WHERE is_active = TRUE
        ORDER BY template_name;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            templates.Add(new
            {
                templateCode = reader.GetString(0),
                templateName = reader.GetString(1),
                serviceLine = reader.IsDBNull(2) ? null : reader.GetString(2),
                defaultPhaseCount = reader.GetInt32(3),
                defaultTaskCount = reader.GetInt32(4)
            });
        }
    }

    return Results.Ok(new { count = requests.Count, requests, templates });
});

app.MapGet("/api/project-management/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var milestones = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT p.project_code, pm.milestone_name, pm.milestone_status, pm.due_date, pm.display_order
        FROM project_milestones pm
        INNER JOIN projects p ON p.project_id = pm.project_id
        ORDER BY p.project_code, pm.display_order, pm.due_date;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            milestones.Add(new
            {
                projectCode = reader.GetString(0),
                name = reader.GetString(1),
                status = reader.GetString(2),
                dueDate = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
                displayOrder = reader.GetInt32(4)
            });
        }
    }

    var risks = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT p.project_code, pr.risk_title, pr.probability, pr.impact, pr.risk_status, pr.mitigation_plan
        FROM project_risks pr
        INNER JOIN projects p ON p.project_id = pr.project_id
        ORDER BY p.project_code, pr.created_at DESC;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            risks.Add(new
            {
                projectCode = reader.GetString(0),
                title = reader.GetString(1),
                probability = reader.GetString(2),
                impact = reader.GetString(3),
                status = reader.GetString(4),
                mitigationPlan = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    return Results.Ok(new { milestoneCount = milestones.Count, riskCount = risks.Count, milestones, risks });
});

app.MapGet("/api/resource-scheduling/capacity", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(28);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var rows = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT u.display_name, u.email, rcp.week_start_date, rcp.available_hours, rcp.assigned_hours, rcp.planned_utilization_percent, rcp.capacity_status
        FROM resource_capacity_plans rcp
        INNER JOIN app_users u ON u.user_id = rcp.user_id
        WHERE rcp.week_start_date BETWEEN @start AND @end
        ORDER BY rcp.week_start_date, u.display_name;
        """, connection);
    command.Parameters.AddWithValue("start", start);
    command.Parameters.AddWithValue("end", end);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            resourceName = reader.GetString(0),
            resourceEmail = reader.GetString(1),
            weekStart = reader.GetFieldValue<DateOnly>(2),
            availableHours = reader.GetDecimal(3),
            assignedHours = reader.GetDecimal(4),
            plannedUtilizationPercent = reader.GetDecimal(5),
            status = reader.GetString(6)
        });
    }

    return Results.Ok(new { weekStart = start, weekEnd = end, count = rows.Count, capacity = rows });
});

app.MapGet("/api/expenses/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var reports = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT er.report_number, er.report_title, er.report_status, er.report_total, u.display_name, p.project_code
        FROM expense_reports er
        INNER JOIN app_users u ON u.user_id = er.user_id
        LEFT JOIN projects p ON p.project_id = er.project_id
        ORDER BY er.created_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        reports.Add(new
        {
            reportNumber = reader.GetString(0),
            title = reader.GetString(1),
            status = reader.GetString(2),
            total = reader.GetDecimal(3),
            resourceName = reader.GetString(4),
            projectCode = reader.IsDBNull(5) ? null : reader.GetString(5)
        });
    }

    return Results.Ok(new { count = reports.Count, reports });
});

app.MapGet("/api/invoicing/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var invoices = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT ci.invoice_number, ci.invoice_status, ci.billing_period_start, ci.billing_period_end, ci.labor_amount, ci.expense_amount, ci.invoice_total, p.project_code, p.project_name
        FROM client_invoices ci
        LEFT JOIN projects p ON p.project_id = ci.project_id
        ORDER BY ci.generated_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        invoices.Add(new
        {
            invoiceNumber = reader.GetString(0),
            status = reader.GetString(1),
            billingPeriodStart = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            billingPeriodEnd = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
            laborAmount = reader.GetDecimal(4),
            expenseAmount = reader.GetDecimal(5),
            invoiceTotal = reader.GetDecimal(6),
            projectCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            projectName = reader.IsDBNull(8) ? null : reader.GetString(8)
        });
    }

    return Results.Ok(new { count = invoices.Count, invoices });
});

app.MapGet("/api/reporting/executive-dashboard", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var metrics = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT snapshot_date, metric_name, metric_value, metric_unit, metric_context::text
        FROM reporting_snapshots
        WHERE snapshot_type = 'executive_dashboard'
        ORDER BY metric_name;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        metrics.Add(new
        {
            snapshotDate = reader.GetFieldValue<DateOnly>(0),
            metricName = reader.GetString(1),
            metricValue = reader.GetDecimal(2),
            metricUnit = reader.IsDBNull(3) ? null : reader.GetString(3),
            context = reader.IsDBNull(4) ? null : reader.GetString(4)
        });
    }

    return Results.Ok(new { count = metrics.Count, metrics });
});

'''

if 'app.MapGet("/api/project-intake/summary"' not in api:
    api = api.replace('\napp.Run();', '\n' + endpoints + 'app.Run();', 1)

api_file.write_text(api)
PY

echo "==> Remaining PSA module API patch applied"
echo "==> Expected API version after redeploy: 0.5.1"
