using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseNotificationSourceScanner
{
    private static readonly HashSet<int> QualificationReminderDays = [90, 60, 30, 14, 7, 1, 0];

    internal static async Task<EnterpriseNotificationSourceObservation[]> ScanAsync(
        NpgsqlConnection connection,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var observations = new List<EnterpriseNotificationSourceObservation>();
        observations.Add(await ScanTimesheetsAsync(connection, correlationId, cancellationToken));
        observations.Add(await ScanProjectExpensesAsync(connection, correlationId, cancellationToken));
        observations.Add(await ScanQualificationsAsync(connection, correlationId, cancellationToken));
        return observations.ToArray();
    }

    private static async Task<EnterpriseNotificationSourceObservation> ScanTimesheetsAsync(
        NpgsqlConnection connection,
        string correlationId,
        CancellationToken cancellationToken)
    {
        const string sourceCode = "timesheet_day_statuses";
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!await TableExistsAsync(connection, sourceCode, cancellationToken))
                return EnterpriseNotificationSourceObservation.Unavailable(
                    sourceCode,
                    "001/002",
                    "SOURCE_TABLE_NOT_AVAILABLE",
                    "The day-level timesheet lifecycle table is unavailable.");

            var rows = new List<TimesheetSourceRow>();
            await using (var command = new NpgsqlCommand("""
                SELECT
                    status.timesheet_id,
                    status.user_id,
                    status.work_date,
                    status.status,
                    status.submitted_at,
                    status.updated_at,
                    row_to_json(status)::text,
                    COALESCE(app_user.display_name, app_user.email, 'Engineer'),
                    COALESCE(app_user.email, ''),
                    COALESCE(SUM(entry.hours), 0)
                FROM timesheet_day_statuses status
                JOIN app_users app_user ON app_user.user_id = status.user_id
                LEFT JOIN time_entries entry
                  ON entry.timesheet_id = status.timesheet_id
                 AND entry.work_date = status.work_date
                WHERE status.work_date >= CURRENT_DATE - 180
                  AND status.status IN (
                      'submitted',
                      'manager_approved',
                      'manager_declined',
                      'pm_approved',
                      'pm_declined',
                      'accounting_ready',
                      'reconciled',
                      'locked'
                  )
                GROUP BY
                    status.timesheet_day_status_id,
                    status.timesheet_id,
                    status.user_id,
                    status.work_date,
                    status.status,
                    status.submitted_at,
                    status.updated_at,
                    app_user.display_name,
                    app_user.email
                ORDER BY status.updated_at;
                """, connection))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetFieldValue<DateOnly>(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                        reader.GetFieldValue<DateTimeOffset>(5),
                        ParseJson(reader.GetString(6)),
                        reader.GetString(7),
                        reader.GetString(8),
                        reader.GetDecimal(9)));
                }
            }

            var created = 0;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                created += await CreateTimesheetEventsAsync(
                    connection,
                    row,
                    correlationId,
                    cancellationToken);
            }

            var observation = EnterpriseNotificationSourceObservation.Healthy(
                sourceCode,
                "001/002",
                rows.Count,
                created,
                "Timesheet submission, staged approval, rejection, completion, and three-day overdue states were evaluated.");
            await EnterpriseNotificationRepository.UpsertCheckpointAsync(
                connection,
                observation,
                startedAt,
                cancellationToken);
            return observation;
        }
        catch (Exception exception)
        {
            var observation = EnterpriseNotificationSourceObservation.Failed(
                sourceCode,
                "001/002",
                EnterpriseNotificationRepository.Diagnostic(exception),
                "The timesheet notification source could not be evaluated. No recipient scope was broadened.");
            await TryCheckpointAsync(connection, observation, startedAt, cancellationToken);
            return observation;
        }
    }

    private static async Task<int> CreateTimesheetEventsAsync(
        NpgsqlConnection connection,
        TimesheetSourceRow row,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var stageTimestamp = StageTimestamp(row);
        var reviewerUserId = StageReviewerUserId(row);
        var reviewerName = reviewerUserId.HasValue
            ? await LoadUserDisplayNameAsync(connection, reviewerUserId.Value, cancellationToken)
            : string.Empty;
        var decisionComment = StageDecisionComment(row);
        var payloadBase = new Dictionary<string, object?>
        {
            ["timesheetId"] = row.TimesheetId,
            ["userId"] = row.UserId,
            ["engineerName"] = row.EngineerName,
            ["engineerEmail"] = row.EngineerEmail,
            ["workDate"] = row.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["status"] = row.Status,
            ["totalHours"] = row.TotalHours.ToString("0.00", CultureInfo.InvariantCulture),
            ["submittedAt"] = row.SubmittedAt?.ToString("O") ?? string.Empty,
            ["stageTimestamp"] = stageTimestamp.ToString("O"),
            ["reviewerUserId"] = reviewerUserId,
            ["reviewerName"] = reviewerName,
            ["decisionComment"] = decisionComment,
            ["deepLink"] = "#manager-approval",
            ["correlationId"] = correlationId
        };

        switch (row.Status)
        {
            case "submitted":
                created += await InsertAsync(
                    connection,
                    "TIME_SUBMISSION_CONFIRMATION",
                    "001",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:submitted-confirmation",
                    $"enterprise:time:submitted-confirmation:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    row.SubmittedAt ?? row.UpdatedAt,
                    payloadBase,
                    correlationId,
                    cancellationToken);
                created += await InsertAsync(
                    connection,
                    "TIME_MANAGER_APPROVAL_REQUEST",
                    "002",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:manager-request",
                    $"enterprise:time:manager-request:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    row.SubmittedAt ?? row.UpdatedAt,
                    payloadBase,
                    correlationId,
                    cancellationToken);
                break;

            case "manager_approved":
                created += await InsertAsync(
                    connection,
                    "TIME_PM_APPROVAL_REQUEST",
                    "002",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:pm-request",
                    $"enterprise:time:pm-request:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    stageTimestamp,
                    payloadBase,
                    correlationId,
                    cancellationToken);
                break;

            case "pm_approved":
                created += await InsertAsync(
                    connection,
                    "TIME_PTC_FINAL_APPROVAL_REQUEST",
                    "002",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:ptc-final-request",
                    $"enterprise:time:ptc-final-request:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    stageTimestamp,
                    payloadBase,
                    correlationId,
                    cancellationToken);
                break;

            case "manager_declined":
            case "pm_declined":
                created += await InsertAsync(
                    connection,
                    "TIME_REJECTED",
                    "002",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:{row.Status}:{stageTimestamp.ToUnixTimeSeconds()}",
                    $"enterprise:time:rejected:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:{row.Status}:{stageTimestamp.ToUnixTimeSeconds()}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    stageTimestamp,
                    payloadBase,
                    correlationId,
                    cancellationToken);
                break;

            case "accounting_ready":
            case "reconciled":
            case "locked":
                created += await InsertAsync(
                    connection,
                    "TIME_FULLY_APPROVED",
                    "002",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:fully-approved",
                    $"enterprise:time:fully-approved:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    stageTimestamp,
                    payloadBase,
                    correlationId,
                    cancellationToken);
                break;
        }

        if (row.Status is "submitted" or "manager_approved" or "pm_approved")
        {
            var ageDays = Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - stageTimestamp).TotalDays));
            if (ageDays >= 3)
            {
                var reminderBucket = ageDays / 3;
                var overduePayload = new Dictionary<string, object?>(payloadBase)
                {
                    ["ageDays"] = ageDays,
                    ["reminderBucket"] = reminderBucket
                };
                created += await InsertAsync(
                    connection,
                    "TIME_APPROVAL_OVERDUE_3_DAYS",
                    "002",
                    $"timesheet:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:{row.Status}:overdue:{reminderBucket}",
                    $"enterprise:time:overdue:{row.TimesheetId:N}:{row.WorkDate:yyyyMMdd}:{row.Status}:{reminderBucket}",
                    "timesheet",
                    row.TimesheetId,
                    null,
                    row.UserId,
                    DateTimeOffset.UtcNow,
                    overduePayload,
                    correlationId,
                    cancellationToken);
            }
        }

        return created;
    }

    private static async Task<EnterpriseNotificationSourceObservation> ScanProjectExpensesAsync(
        NpgsqlConnection connection,
        string correlationId,
        CancellationToken cancellationToken)
    {
        const string sourceCode = "project_expense_uploads";
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!await TableExistsAsync(connection, sourceCode, cancellationToken))
                return EnterpriseNotificationSourceObservation.Unavailable(
                    sourceCode,
                    "005",
                    "SOURCE_TABLE_NOT_AVAILABLE",
                    "Module 005 expense-upload storage is unavailable.");

            var rows = new List<ExpenseSourceRow>();
            await using (var command = new NpgsqlCommand("""
                SELECT
                    upload.project_expense_upload_id,
                    upload.project_id,
                    upload.expense_owner_user_id,
                    upload.uploaded_by_user_id,
                    upload.project_code,
                    upload.project_name,
                    upload.line_count,
                    upload.total_amount,
                    upload.reimbursable_amount,
                    upload.currency,
                    upload.uploaded_at,
                    upload.notification_status,
                    COALESCE(owner.display_name, owner.email, 'Expense owner')
                FROM project_expense_uploads upload
                JOIN app_users owner ON owner.user_id = upload.expense_owner_user_id
                WHERE upload.uploaded_at >= NOW() - INTERVAL '180 days'
                  AND upload.deleted_at IS NULL
                  AND upload.is_current = TRUE
                ORDER BY upload.uploaded_at;
                """, connection))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetGuid(2),
                        reader.GetGuid(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetInt32(6),
                        reader.GetDecimal(7),
                        reader.GetDecimal(8),
                        reader.GetString(9),
                        reader.GetFieldValue<DateTimeOffset>(10),
                        reader.GetString(11),
                        reader.GetString(12)));
                }
            }

            var created = 0;
            foreach (var row in rows)
            {
                var payload = new Dictionary<string, object?>
                {
                    ["uploadId"] = row.UploadId,
                    ["projectId"] = row.ProjectId,
                    ["expenseOwnerUserId"] = row.OwnerUserId,
                    ["uploadedByUserId"] = row.UploadedByUserId,
                    ["recipientName"] = row.OwnerName,
                    ["projectCode"] = row.ProjectCode,
                    ["projectName"] = row.ProjectName,
                    ["lineCount"] = row.LineCount,
                    ["totalAmount"] = $"{row.TotalAmount:0.00} {row.Currency}",
                    ["reimbursableAmount"] = $"{row.ReimbursableAmount:0.00} {row.Currency}",
                    ["notificationStatus"] = row.NotificationStatus,
                    ["deepLink"] = "#project-allocation-info",
                    ["correlationId"] = correlationId
                };
                created += await InsertAsync(
                    connection,
                    "EXPENSE_UPLOAD_CONFIRMATION",
                    "005",
                    $"expense-upload:{row.UploadId:N}:owner-confirmation",
                    $"enterprise:expense:upload-confirmation:{row.UploadId:N}",
                    "project_expense_upload",
                    row.UploadId,
                    row.ProjectId,
                    row.OwnerUserId,
                    row.UploadedAt,
                    payload,
                    correlationId,
                    cancellationToken);
                created += await InsertAsync(
                    connection,
                    "EXPENSE_PM_REVIEW_REQUEST",
                    "005",
                    $"expense-upload:{row.UploadId:N}:pm-review",
                    $"enterprise:expense:pm-review:{row.UploadId:N}",
                    "project_expense_upload",
                    row.UploadId,
                    row.ProjectId,
                    row.OwnerUserId,
                    row.UploadedAt,
                    payload,
                    correlationId,
                    cancellationToken);
            }

            var observation = EnterpriseNotificationSourceObservation.Healthy(
                sourceCode,
                "005",
                rows.Count,
                created,
                "Current project-expense uploads were evaluated for owner confirmation and Project Management review.");
            await EnterpriseNotificationRepository.UpsertCheckpointAsync(connection, observation, startedAt, cancellationToken);
            return observation;
        }
        catch (Exception exception)
        {
            var observation = EnterpriseNotificationSourceObservation.Failed(
                sourceCode,
                "005",
                EnterpriseNotificationRepository.Diagnostic(exception),
                "The Module 005 expense notification source could not be evaluated.");
            await TryCheckpointAsync(connection, observation, startedAt, cancellationToken);
            return observation;
        }
    }

    private static async Task<EnterpriseNotificationSourceObservation> ScanQualificationsAsync(
        NpgsqlConnection connection,
        string correlationId,
        CancellationToken cancellationToken)
    {
        const string sourceCode = "resource_qualifications";
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!await TableExistsAsync(connection, sourceCode, cancellationToken))
                return EnterpriseNotificationSourceObservation.Unavailable(
                    sourceCode,
                    "069",
                    "SOURCE_TABLE_NOT_AVAILABLE",
                    "Module 069 qualification storage is unavailable.");

            var rows = new List<QualificationSourceRow>();
            await using (var command = new NpgsqlCommand("""
                SELECT
                    qualification.resource_qualification_id,
                    qualification.user_id,
                    qualification.qualification_category,
                    qualification.qualification_name,
                    COALESCE(qualification.competency, ''),
                    qualification.effective_end_date,
                    COALESCE(app_user.display_name, app_user.email, 'Resource'),
                    COALESCE(app_user.email, '')
                FROM resource_qualifications qualification
                JOIN app_users app_user ON app_user.user_id = qualification.user_id
                WHERE qualification.effective_end_date IS NOT NULL
                  AND qualification.effective_end_date <= CURRENT_DATE + 90
                ORDER BY qualification.effective_end_date, qualification.qualification_name;
                """, connection))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetFieldValue<DateOnly>(5),
                        reader.GetString(6),
                        reader.GetString(7)));
                }
            }

            var created = 0;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var chicago = ChicagoNow();
            var weeklyExpiredWindow = chicago.DayOfWeek == DayOfWeek.Monday
                && chicago.TimeOfDay >= TimeSpan.FromHours(8);
            var weekKey = ISOWeek.GetYear(chicago.DateTime).ToString(CultureInfo.InvariantCulture)
                + "-W"
                + ISOWeek.GetWeekOfYear(chicago.DateTime).ToString("00", CultureInfo.InvariantCulture);

            foreach (var row in rows)
            {
                var daysRemaining = row.EffectiveEndDate.DayNumber - today.DayNumber;
                var payload = new Dictionary<string, object?>
                {
                    ["qualificationId"] = row.QualificationId,
                    ["qualificationCategory"] = row.Category,
                    ["qualificationName"] = row.Name,
                    ["competency"] = row.Competency,
                    ["expirationDate"] = row.EffectiveEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["daysRemaining"] = daysRemaining,
                    ["recipientName"] = row.UserName,
                    ["deepLink"] = "#qualifications-certifications",
                    ["correlationId"] = correlationId
                };

                if (daysRemaining >= 0 && QualificationReminderDays.Contains(daysRemaining))
                {
                    created += await InsertAsync(
                        connection,
                        "QUALIFICATION_EXPIRING",
                        "069",
                        $"qualification:{row.QualificationId:N}:expires:{daysRemaining}:{row.EffectiveEndDate:yyyyMMdd}",
                        $"enterprise:qualification:expiring:{row.QualificationId:N}:{daysRemaining}:{row.EffectiveEndDate:yyyyMMdd}",
                        "resource_qualification",
                        row.QualificationId,
                        null,
                        row.UserId,
                        DateTimeOffset.UtcNow,
                        payload,
                        correlationId,
                        cancellationToken);
                }
                else if (daysRemaining < 0 && weeklyExpiredWindow)
                {
                    created += await InsertAsync(
                        connection,
                        "QUALIFICATION_EXPIRED_WEEKLY",
                        "069",
                        $"qualification:{row.QualificationId:N}:expired:{weekKey}",
                        $"enterprise:qualification:expired:{row.QualificationId:N}:{weekKey}",
                        "resource_qualification",
                        row.QualificationId,
                        null,
                        row.UserId,
                        DateTimeOffset.UtcNow,
                        payload,
                        correlationId,
                        cancellationToken);
                }
            }

            var observation = EnterpriseNotificationSourceObservation.Healthy(
                sourceCode,
                "069",
                rows.Count,
                created,
                "Qualification expirations were evaluated at 90/60/30/14/7/1/0 days and in the weekly expired window.");
            await EnterpriseNotificationRepository.UpsertCheckpointAsync(connection, observation, startedAt, cancellationToken);
            return observation;
        }
        catch (Exception exception)
        {
            var observation = EnterpriseNotificationSourceObservation.Failed(
                sourceCode,
                "069",
                EnterpriseNotificationRepository.Diagnostic(exception),
                "The Module 069 qualification-expiration source could not be evaluated.");
            await TryCheckpointAsync(connection, observation, startedAt, cancellationToken);
            return observation;
        }
    }

    private static async Task<int> InsertAsync(
        NpgsqlConnection connection,
        string policyCode,
        string sourceModule,
        string sourceEventId,
        string idempotencyKey,
        string entityType,
        Guid? entityId,
        Guid? projectId,
        Guid? subjectUserId,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, object?> payload,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToElement(payload);
        var result = await EnterpriseNotificationRepository.InsertEventAsync(
            connection,
            policyCode,
            sourceModule,
            sourceEventId,
            idempotencyKey,
            entityType,
            entityId,
            projectId,
            subjectUserId,
            occurredAt,
            DateTimeOffset.UtcNow,
            json,
            "authoritative_scanner",
            null,
            correlationId,
            cancellationToken);
        return result.Created ? 1 : 0;
    }

    private static DateTimeOffset StageTimestamp(TimesheetSourceRow row)
    {
        var candidates = row.Status switch
        {
            "manager_approved" => ["manager_approved_at"],
            "manager_declined" => ["manager_declined_at"],
            "pm_approved" => ["pm_approved_at"],
            "pm_declined" => ["pm_declined_at"],
            "accounting_ready" => ["accounting_ready_at"],
            "reconciled" => ["reconciled_at"],
            "locked" => ["locked_at"],
            _ => ["submitted_at"]
        };
        foreach (var candidate in candidates)
        {
            var value = JsonString(row.RawStatus, candidate);
            if (DateTimeOffset.TryParse(value, out var timestamp)) return timestamp;
        }
        return row.SubmittedAt ?? row.UpdatedAt;
    }

    private static Guid? StageReviewerUserId(TimesheetSourceRow row)
    {
        var candidate = row.Status switch
        {
            "manager_approved" or "manager_declined" => "manager_user_id",
            "pm_approved" or "pm_declined" => "pm_approved_by_user_id",
            "accounting_ready" => "accounting_ready_by_user_id",
            _ => string.Empty
        };
        return Guid.TryParse(JsonString(row.RawStatus, candidate), out var id) ? id : null;
    }

    private static string StageDecisionComment(TimesheetSourceRow row) => row.Status switch
    {
        "manager_approved" or "manager_declined" => JsonString(row.RawStatus, "manager_decision_comment"),
        "pm_approved" or "pm_declined" => JsonString(row.RawStatus, "pm_decision_comment"),
        "accounting_ready" => JsonString(row.RawStatus, "accounting_comment"),
        _ => string.Empty
    };

    private static async Task<string> LoadUserDisplayNameAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(NULLIF(display_name, ''), email, 'Reviewer')
            FROM app_users
            WHERE user_id = @user_id;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass(@table_name) IS NOT NULL;",
            connection);
        command.Parameters.AddWithValue("table_name", $"public.{table}");
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task TryCheckpointAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationSourceObservation observation,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnterpriseNotificationRepository.UpsertCheckpointAsync(
                connection,
                observation,
                startedAt,
                cancellationToken);
        }
        catch
        {
            // The original source failure remains the authoritative diagnostic.
        }
    }

    private static DateTimeOffset ChicagoNow()
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static string JsonString(JsonElement row, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)
            || row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
    }

    private sealed record TimesheetSourceRow(
        Guid TimesheetId,
        Guid UserId,
        DateOnly WorkDate,
        string Status,
        DateTimeOffset? SubmittedAt,
        DateTimeOffset UpdatedAt,
        JsonElement RawStatus,
        string EngineerName,
        string EngineerEmail,
        decimal TotalHours);

    private sealed record ExpenseSourceRow(
        Guid UploadId,
        Guid ProjectId,
        Guid OwnerUserId,
        Guid UploadedByUserId,
        string ProjectCode,
        string ProjectName,
        int LineCount,
        decimal TotalAmount,
        decimal ReimbursableAmount,
        string Currency,
        DateTimeOffset UploadedAt,
        string NotificationStatus,
        string OwnerName);

    private sealed record QualificationSourceRow(
        Guid QualificationId,
        Guid UserId,
        string Category,
        string Name,
        string Competency,
        DateOnly EffectiveEndDate,
        string UserName,
        string UserEmail);
}