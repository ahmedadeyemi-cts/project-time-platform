using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class PendingApprovalWorkModule
{
    private static async Task<bool> CompleteOneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PendingApprovalAccess access,
        string stage,
        PendingApprovalItem item,
        string systemReason,
        bool hasScopedApprovalAudit,
        CancellationToken cancellationToken)
    {
        var expectedStatus = stage switch
        {
            "manager" => "submitted",
            "pm" => "manager_approved",
            _ => "pm_approved"
        };
        var nextStatus = stage switch
        {
            "manager" => "manager_approved",
            "pm" => "pm_approved",
            _ => "accounting_ready"
        };
        var approvalStage = stage switch
        {
            "manager" => "manager",
            "pm" => "project_manager",
            _ => "accounting"
        };
        var requiredStage = stage switch
        {
            "manager" => "MANAGER",
            "pm" => "PROJECT_MANAGER",
            _ => "PTC_FINAL"
        };
        var auditAction = stage switch
        {
            "manager" => "timesheet_day_manager_bulk_approved",
            "pm" => "timesheet_day_project_manager_bulk_approved",
            _ => "timesheet_day_ptc_final_bulk_approved"
        };

        var updateSql = stage switch
        {
            "manager" => """
                UPDATE timesheet_day_statuses
                SET status = 'manager_approved',
                    manager_user_id = @actor_user_id,
                    manager_decision_comment = @reason,
                    manager_approved_at = NOW(),
                    manager_declined_at = NULL,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'submitted'
                RETURNING timesheet_day_status_id;
                """,
            "pm" => """
                UPDATE timesheet_day_statuses
                SET status = 'pm_approved',
                    pm_approved_by_user_id = @actor_user_id,
                    pm_approved_at = NOW(),
                    pm_decision_comment = @reason,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'manager_approved'
                RETURNING timesheet_day_status_id;
                """,
            _ => """
                UPDATE timesheet_day_statuses
                SET status = 'accounting_ready',
                    accounting_ready_by_user_id = @actor_user_id,
                    accounting_ready_at = NOW(),
                    accounting_comment = @reason,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'pm_approved'
                RETURNING timesheet_day_status_id;
                """
        };

        await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            update.Parameters.AddWithValue("reason", systemReason);
            update.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            update.Parameters.AddWithValue("work_date", item.WorkDate);
            if (await update.ExecuteScalarAsync(cancellationToken) is not Guid)
            {
                return false;
            }
        }

        await using (var updateEntries = new NpgsqlCommand("""
            UPDATE time_entries
            SET status = @next_status,
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction))
        {
            updateEntries.Parameters.AddWithValue("next_status", nextStatus);
            updateEntries.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            updateEntries.Parameters.AddWithValue("work_date", item.WorkDate);
            await updateEntries.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var approvals = new NpgsqlCommand("""
            INSERT INTO approval_records (
                time_entry_id,
                approval_stage,
                approval_status,
                approver_user_id,
                decision_comment
            )
            SELECT
                time_entry_id,
                @approval_stage,
                'approved',
                @actor_user_id,
                @reason
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction))
        {
            approvals.Parameters.AddWithValue("approval_stage", approvalStage);
            approvals.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            approvals.Parameters.AddWithValue("reason", systemReason);
            approvals.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            approvals.Parameters.AddWithValue("work_date", item.WorkDate);
            await approvals.ExecuteNonQueryAsync(cancellationToken);
        }

        var oldValue = JsonSerializer.Serialize(new
        {
            status = expectedStatus,
            item.WorkDate,
            item.UserId,
            item.TotalHours
        });
        var newValue = JsonSerializer.Serialize(new
        {
            status = nextStatus,
            item.WorkDate,
            item.UserId,
            item.TotalHours,
            approvalMode = "bulk_no_user_comment",
            stage
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
                'timesheet_day',
                @timesheet_id,
                @old_value::jsonb,
                @new_value::jsonb,
                NULLIF(@ip_address, '')::inet,
                @user_agent
            );
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            audit.Parameters.AddWithValue("action", auditAction);
            audit.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            audit.Parameters.AddWithValue("old_value", oldValue);
            audit.Parameters.AddWithValue("new_value", newValue);
            audit.Parameters.AddWithValue("ip_address", access.IpAddress);
            audit.Parameters.AddWithValue("user_agent", access.UserAgent);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        if (hasScopedApprovalAudit)
        {
            var delegated = stage switch
            {
                "manager" => !access.IsManager,
                "pm" => !access.IsProjectManager,
                _ => !access.IsPtc
            };

            await using var scopedAudit = new NpgsqlCommand("""
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
                )
                VALUES (
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
            scopedAudit.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            scopedAudit.Parameters.AddWithValue("work_date", item.WorkDate);
            scopedAudit.Parameters.AddWithValue("required_stage", requiredStage);
            scopedAudit.Parameters.AddWithValue("original_role", requiredStage);
            scopedAudit.Parameters.AddWithValue("actor_user_id", access.ActualUserId);
            scopedAudit.Parameters.AddWithValue("acting_role_code", access.PrimaryRoleCode);
            scopedAudit.Parameters.AddWithValue("delegated_action", delegated);
            scopedAudit.Parameters.AddWithValue("reason", systemReason);
            scopedAudit.Parameters.AddWithValue("previous_status", expectedStatus);
            scopedAudit.Parameters.AddWithValue("new_status", nextStatus);
            scopedAudit.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
            {
                bulkApproval = true,
                userCommentRequired = false,
                source = "pending_approval_work",
                item.WeekStart,
                item.WeekEnd,
                item.TotalHours
            }));
            await scopedAudit.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }
}
