using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseNotificationRecipientResolver
{
    private static readonly string[] PtcRoles =
    [
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_COORDINATOR",
        "PTC"
    ];

    private static readonly string[] AdministratorRoles =
    [
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR"
    ];

    private static readonly string[] ManagerRoles =
    [
        "MANAGER",
        "PEOPLE_MANAGER",
        "ENGINEERING_MANAGER",
        "ENGINEERING_LEAD",
        "ENGINEERING_TEAM_LEAD"
    ];

    private static readonly string[] BillingRoles =
    [
        "ACCOUNTING",
        "ACCOUNTING_BILLING",
        "BILLING",
        "FINANCE"
    ];

    private static readonly string[] SecurityRoles =
    [
        "SECURITY",
        "SECURITY_OPERATIONS",
        "SOC",
        "SUPER_ADMINISTRATOR"
    ];

    internal static async Task<EnterpriseNotificationRecipientResolution> ResolveAsync(
        NpgsqlConnection connection,
        EnterpriseNotificationPolicyRow policy,
        EnterpriseNotificationEventRow notificationEvent,
        CancellationToken cancellationToken)
    {
        var recipients = new List<ProjectNotificationUser>();
        var evidence = new List<string>();

        // Signed producer contracts may provide user IDs and, only where an
        // approved upstream system has no ProjectPulse identity, explicit email
        // recipients. The signed-ingestion endpoint validates the producer before
        // these fields can reach recipient resolution.
        await AddPayloadUsersAsync(
            connection,
            recipients,
            notificationEvent.Payload,
            "recipientUserIds",
            "to",
            "signed_event_payload.recipientUserIds",
            cancellationToken);
        await AddPayloadUsersAsync(
            connection,
            recipients,
            notificationEvent.Payload,
            "ccUserIds",
            "cc",
            "signed_event_payload.ccUserIds",
            cancellationToken);
        if (notificationEvent.IngestionSource == "signed_api")
        {
            AddPayloadEmails(
                recipients,
                notificationEvent.Payload,
                "recipientEmails",
                "to",
                "signed_event_payload.recipientEmails");
            AddPayloadEmails(
                recipients,
                notificationEvent.Payload,
                "ccEmails",
                "cc",
                "signed_event_payload.ccEmails");
        }

        var strategy = policy.RecipientStrategy.Trim().ToLowerInvariant();
        switch (strategy)
        {
            case "timesheet_engineer":
            case "subject_user":
            case "expense_owner":
            case "report_requester":
            case "defect_assignee":
            case "defect_reporter":
                await AddSubjectUserAsync(
                    connection,
                    recipients,
                    notificationEvent.SubjectUserId,
                    "to",
                    $"{strategy}.subject_user_id",
                    cancellationToken);
                break;

            case "timesheet_manager":
                await AddManagerForUserAsync(
                    connection,
                    recipients,
                    notificationEvent.SubjectUserId,
                    "to",
                    "app_users.manager_email",
                    cancellationToken);
                if (recipients.Count == 0)
                    await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "ptc_fallback", cancellationToken);
                break;

            case "timesheet_project_managers":
                await AddTimesheetProjectManagersAsync(
                    connection,
                    recipients,
                    notificationEvent,
                    "to",
                    cancellationToken);
                if (recipients.Count == 0)
                    await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "ptc_fallback", cancellationToken);
                break;

            case "ptc_role_group":
                await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "active_ptc_role", cancellationToken);
                break;

            case "timesheet_current_approvers":
                await AddCurrentTimesheetApproversAsync(connection, recipients, notificationEvent, cancellationToken);
                break;

            case "project_manager":
                await AddProjectRolesAsync(connection, recipients, notificationEvent.ProjectId, true, false, false, false, "to", cancellationToken);
                break;

            case "project_team":
                await AddProjectRolesAsync(connection, recipients, notificationEvent.ProjectId, true, true, true, true, "to", cancellationToken);
                await AddProjectEngineersAsync(connection, recipients, notificationEvent.ProjectId, "to", cancellationToken);
                break;

            case "manager_and_ptc":
                await AddSubjectUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "compliance.subject_user", cancellationToken);
                await AddManagerForUserAsync(connection, recipients, notificationEvent.SubjectUserId, "cc", "compliance.manager", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, PtcRoles, "cc", "compliance.ptc", cancellationToken);
                break;

            case "report_requester_and_admin":
                await AddSubjectUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "report.requester", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, PtcRoles, "cc", "report.ptc", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, AdministratorRoles, "cc", "report.administrator", cancellationToken);
                break;

            case "billing_project_team":
                await AddRoleGroupAsync(connection, recipients, BillingRoles, "to", "billing_role_group", cancellationToken);
                await AddProjectRolesAsync(connection, recipients, notificationEvent.ProjectId, true, true, false, false, "cc", cancellationToken);
                break;

            case "entra_expiration_recipients":
                if (recipients.Count == 0)
                    await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "module_065_expiration_recipient", cancellationToken);
                break;

            case "qualification_owner_and_manager":
                await AddSubjectUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "qualification.owner", cancellationToken);
                await AddManagerForUserAsync(connection, recipients, notificationEvent.SubjectUserId, "cc", "qualification.manager", cancellationToken);
                break;

            case "oncall_assignee":
                await AddSubjectUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "oncall.assignee", cancellationToken);
                await AddManagerForUserAsync(connection, recipients, notificationEvent.SubjectUserId, "cc", "oncall.manager", cancellationToken);
                break;

            case "oncall_manager_and_ptc":
                await AddManagerForUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "oncall.manager", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "oncall.ptc", cancellationToken);
                await AddSubjectUserAsync(connection, recipients, notificationEvent.SubjectUserId, "cc", "oncall.assignee", cancellationToken);
                break;

            case "defect_assignee_and_managers":
                await AddSubjectUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "defect.assignee", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, ManagerRoles, "cc", "defect.manager_role_group", cancellationToken);
                break;

            case "operations_stakeholders":
            case "integration_stakeholders":
                await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", $"{strategy}.ptc", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, AdministratorRoles, "to", $"{strategy}.administrator", cancellationToken);
                break;

            case "security_stakeholders":
                await AddRoleGroupAsync(connection, recipients, SecurityRoles, "to", "security.role_group", cancellationToken);
                await AddRoleGroupAsync(connection, recipients, AdministratorRoles, "cc", "security.administrator", cancellationToken);
                break;

            default:
                // Payload-based resolution above is the only fallback. Unknown
                // strategies never broaden to all users.
                evidence.Add($"unknown_strategy:{policy.RecipientStrategy}");
                break;
        }

        if (strategy == "expense_owner")
            await AddProjectRolesAsync(connection, recipients, notificationEvent.ProjectId, true, false, false, false, "cc", cancellationToken);

        var normalized = recipients
            .Where(recipient => !string.IsNullOrWhiteSpace(recipient.Email))
            .GroupBy(
                recipient => recipient.Email.Trim().ToLowerInvariant(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var rows = group.ToArray();
                var preferred = rows.FirstOrDefault(row => row.RecipientType == "to") ?? rows[0];
                return preferred with
                {
                    Email = preferred.Email.Trim().ToLowerInvariant(),
                    RecipientType = rows.Any(row => row.RecipientType == "to") ? "to" : "cc",
                    DerivationSource = string.Join(
                        ";",
                        rows.Select(row => row.DerivationSource)
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase))
                };
            })
            .OrderBy(row => row.RecipientType == "to" ? 0 : 1)
            .ThenBy(row => row.DisplayName)
            .ThenBy(row => row.Email)
            .ToArray();

        evidence.AddRange(normalized.Select(recipient =>
            $"{recipient.RecipientType}:{recipient.Role}:{recipient.DerivationSource}"));

        if (normalized.Length == 0)
        {
            return new(
                Array.Empty<ProjectNotificationUser>(),
                "suppressed",
                "NO_AUTHORIZED_RECIPIENTS",
                "No active, authorized recipient could be derived from ProjectPulse data or the signed producer contract.",
                evidence.ToArray());
        }

        return new(
            normalized,
            "resolved",
            string.Empty,
            $"Derived {normalized.Length} unique recipient(s) server-side.",
            evidence.ToArray());
    }

    private static async Task AddCurrentTimesheetApproversAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        EnterpriseNotificationEventRow notificationEvent,
        CancellationToken cancellationToken)
    {
        var status = PayloadString(notificationEvent.Payload, "status").ToLowerInvariant();
        switch (status)
        {
            case "submitted":
                await AddManagerForUserAsync(connection, recipients, notificationEvent.SubjectUserId, "to", "timesheet.current_manager", cancellationToken);
                break;
            case "manager_approved":
                await AddTimesheetProjectManagersAsync(connection, recipients, notificationEvent, "to", cancellationToken);
                break;
            case "pm_approved":
                await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "timesheet.current_ptc", cancellationToken);
                break;
        }

        if (recipients.Count == 0)
            await AddRoleGroupAsync(connection, recipients, PtcRoles, "to", "timesheet.approver_fallback", cancellationToken);
    }

    private static async Task AddTimesheetProjectManagersAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        EnterpriseNotificationEventRow notificationEvent,
        string recipientType,
        CancellationToken cancellationToken)
    {
        if (!notificationEvent.EntityId.HasValue) return;
        var workDate = PayloadDate(notificationEvent.Payload, "workDate");
        if (!workDate.HasValue) return;

        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT
                manager.user_id,
                COALESCE(NULLIF(manager.display_name, ''), manager.email),
                lower(manager.email)
            FROM time_entries entry
            JOIN projects project ON project.project_id = entry.project_id
            JOIN app_users manager
              ON manager.user_id = project.project_manager_user_id
             AND manager.is_active = TRUE
            WHERE entry.timesheet_id = @timesheet_id
              AND entry.work_date = @work_date;
            """, connection);
        command.Parameters.AddWithValue("timesheet_id", notificationEvent.EntityId.Value);
        command.Parameters.AddWithValue("work_date", workDate.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                "PROJECT_MANAGER",
                "time_entries.projects.project_manager_user_id",
                recipientType));
        }
    }

    private static async Task AddSubjectUserAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        Guid? userId,
        string recipientType,
        string derivationSource,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return;
        var user = await LoadUserAsync(connection, userId.Value, derivationSource, recipientType, cancellationToken);
        if (user is not null) recipients.Add(user);
    }

    private static async Task AddManagerForUserAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        Guid? userId,
        string recipientType,
        string derivationSource,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return;
        await using var command = new NpgsqlCommand("""
            SELECT
                manager.user_id,
                COALESCE(NULLIF(manager.display_name, ''), manager.email),
                lower(manager.email)
            FROM app_users subject
            JOIN app_users manager
              ON lower(manager.email) = lower(subject.manager_email)
             AND manager.is_active = TRUE
            WHERE subject.user_id = @user_id
              AND subject.is_active = TRUE
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return;
        recipients.Add(new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            "MANAGER",
            derivationSource,
            recipientType));
    }

    private static async Task AddProjectRolesAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        Guid? projectId,
        bool includePm,
        bool includePtc,
        bool includeSa,
        bool includeAe,
        string recipientType,
        CancellationToken cancellationToken)
    {
        if (!projectId.HasValue) return;
        await using var command = new NpgsqlCommand("""
            SELECT
                role_code,
                app_user.user_id,
                COALESCE(NULLIF(app_user.display_name, ''), app_user.email),
                lower(app_user.email),
                derivation_source
            FROM (
                SELECT 'PROJECT_MANAGER'::text AS role_code,
                       project.project_manager_user_id AS user_id,
                       'projects.project_manager_user_id'::text AS derivation_source,
                       @include_pm AS include_role
                FROM projects project WHERE project.project_id = @project_id
                UNION ALL
                SELECT 'PROJECT_TEAM_COORDINATOR',
                       project.project_coordinator_user_id,
                       'projects.project_coordinator_user_id',
                       @include_ptc
                FROM projects project WHERE project.project_id = @project_id
                UNION ALL
                SELECT 'SOLUTION_ARCHITECT',
                       project.solution_architect_user_id,
                       'projects.solution_architect_user_id',
                       @include_sa
                FROM projects project WHERE project.project_id = @project_id
                UNION ALL
                SELECT 'ACCOUNT_EXECUTIVE',
                       project.account_executive_user_id,
                       'projects.account_executive_user_id',
                       @include_ae
                FROM projects project WHERE project.project_id = @project_id
            ) role_source
            JOIN app_users app_user
              ON app_user.user_id = role_source.user_id
             AND app_user.is_active = TRUE
            WHERE role_source.include_role = TRUE;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId.Value);
        command.Parameters.AddWithValue("include_pm", includePm);
        command.Parameters.AddWithValue("include_ptc", includePtc);
        command.Parameters.AddWithValue("include_sa", includeSa);
        command.Parameters.AddWithValue("include_ae", includeAe);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(new(
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(0),
                reader.GetString(4),
                recipientType));
        }
    }

    private static async Task AddProjectEngineersAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        Guid? projectId,
        string recipientType,
        CancellationToken cancellationToken)
    {
        if (!projectId.HasValue) return;
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT
                    app_user.user_id,
                    COALESCE(NULLIF(app_user.display_name, ''), app_user.email),
                    lower(app_user.email)
                FROM project_assignments assignment
                JOIN app_users app_user
                  ON app_user.user_id = assignment.user_id
                 AND app_user.is_active = TRUE
                WHERE assignment.project_id = @project_id
                  AND assignment.effective_start_date <= CURRENT_DATE
                  AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE);
                """, connection);
            command.Parameters.AddWithValue("project_id", projectId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                recipients.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    "ENGINEER",
                    "project_assignments.user_id",
                    recipientType));
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            // A missing optional assignment source never broadens recipient scope.
        }
    }

    private static async Task AddRoleGroupAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        string[] roleCodes,
        string recipientType,
        string derivationSource,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT DISTINCT
                app_user.user_id,
                COALESCE(NULLIF(app_user.display_name, ''), app_user.email),
                lower(app_user.email),
                upper(role.role_code)
            FROM app_users app_user
            JOIN app_user_role_assignments assignment
              ON assignment.user_id = app_user.user_id
             AND assignment.is_active = TRUE
            JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id
             AND role.is_active = TRUE
            WHERE app_user.is_active = TRUE
              AND upper(role.role_code) = ANY(@role_codes);
            """, connection);
        command.Parameters.Add(new NpgsqlParameter(
            "role_codes",
            NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = roleCodes.Select(value => value.ToUpperInvariant()).ToArray()
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                derivationSource,
                recipientType));
        }
    }

    private static async Task AddPayloadUsersAsync(
        NpgsqlConnection connection,
        List<ProjectNotificationUser> recipients,
        JsonElement payload,
        string propertyName,
        string recipientType,
        string derivationSource,
        CancellationToken cancellationToken)
    {
        foreach (var userId in PayloadGuidArray(payload, propertyName))
        {
            var user = await LoadUserAsync(
                connection,
                userId,
                derivationSource,
                recipientType,
                cancellationToken);
            if (user is not null) recipients.Add(user);
        }
    }

    private static void AddPayloadEmails(
        List<ProjectNotificationUser> recipients,
        JsonElement payload,
        string propertyName,
        string recipientType,
        string derivationSource)
    {
        foreach (var email in PayloadStringArray(payload, propertyName))
        {
            var normalized = email.Trim().ToLowerInvariant();
            if (!LooksLikeEmail(normalized)) continue;
            recipients.Add(new(
                null,
                normalized,
                normalized,
                "SIGNED_EVENT_RECIPIENT",
                derivationSource,
                recipientType));
        }
    }

    private static async Task<ProjectNotificationUser?> LoadUserAsync(
        NpgsqlConnection connection,
        Guid userId,
        string derivationSource,
        string recipientType,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                app_user.user_id,
                COALESCE(NULLIF(app_user.display_name, ''), app_user.email),
                lower(app_user.email),
                COALESCE((
                    SELECT upper(role.role_code)
                    FROM app_user_role_assignments assignment
                    JOIN app_roles role ON role.app_role_id = assignment.app_role_id
                    WHERE assignment.user_id = app_user.user_id
                      AND assignment.is_active = TRUE
                      AND role.is_active = TRUE
                    ORDER BY role.role_code
                    LIMIT 1
                ), 'USER')
            FROM app_users app_user
            WHERE app_user.user_id = @user_id
              AND app_user.is_active = TRUE
              AND COALESCE(BTRIM(app_user.email), '') <> '';
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            derivationSource,
            recipientType);
    }

    internal static string PayloadString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    internal static Guid? PayloadGuid(JsonElement payload, string propertyName) =>
        Guid.TryParse(PayloadString(payload, propertyName), out var value) ? value : null;

    internal static DateOnly? PayloadDate(JsonElement payload, string propertyName) =>
        DateOnly.TryParse(PayloadString(payload, propertyName), out var value) ? value : null;

    private static Guid[] PayloadGuidArray(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array) return Array.Empty<Guid>();
        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
            .Select(text => Guid.TryParse(text, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
    }

    private static string[] PayloadStringArray(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 3 && value.IndexOf('.', at) > at + 1;
    }
}