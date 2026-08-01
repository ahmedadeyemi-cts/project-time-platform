using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    public static WebApplication MapCombinedModulePublicReadinessEndpoint(this WebApplication app)
    {
        // Execute every current and compatibility approval request through one
        // hardened contract before historical endpoint handlers can be selected.
        // The hardening layer explicitly executes IResult responses, retires old
        // approval writes, and fails closed when immutable evidence is unavailable.
        app.UseProductionApprovalWorkflowHardening();
        app.UseProductionApprovalWorkCompatibility();
        app.MapProductionApprovalWorkEndpoints();

        // This combined-module composition root is already registered exactly once
        // before app.Run(). Register the additive Module 001/002 operational routes
        // here so the generated Program source does not need another cross-cutting edit.
        app.MapPendingApprovalWorkEndpoints();
        app.MapModule001NonProjectTaskEndpoints();
        app.MapGet("/health/combined-modules", CombinedPublicReadinessAsync);
        app.MapGet("/api/public/combined-modules/readiness", CombinedPublicReadinessAsync);
        return app;
    }

    private static async Task<IResult> CombinedPublicReadinessAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                WITH canonical_roles(role_code) AS (
                    VALUES
                      ('ENGINEERING'),('PROJECT_MANAGEMENT'),('ENGINEERING_LEAD'),('PROJECT_MANAGEMENT_LEAD'),
                      ('MANAGER'),('SALES'),('INSIDE_SALES'),('SOLUTION_ARCHITECT'),('EXECUTIVE'),
                      ('PROJECT_TEAM_COORDINATOR'),('ACCOUNTING'),('SUPER_ADMINISTRATOR')
                ), eligible_aliases(role_code) AS (
                    VALUES
                      ('ENGINEERING'),('ENGINEER'),
                      ('ENGINEERING_LEAD'),('ENGINEERING_TEAM_LEAD'),
                      ('PROJECT_MANAGEMENT'),('PROJECT_MANAGER'),
                      ('PROJECT_MANAGEMENT_LEAD'),('PROJECT_MANAGEMENT_TEAM_LEAD'),('PM_TEAM_LEAD')
                )
                SELECT
                    (SELECT COUNT(*) FROM app_roles r
                     JOIN canonical_roles c ON c.role_code=UPPER(r.role_code)
                     WHERE r.is_active=TRUE),
                    (SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active=TRUE),
                    (SELECT COUNT(*) FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED'),
                    (SELECT COUNT(*) FROM scoped_role_policy_effective_grants),
                    (SELECT COUNT(DISTINCT u.user_id)
                     FROM app_users u
                     JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
                     JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                     JOIN eligible_aliases e ON e.role_code=UPPER(r.role_code)
                     WHERE u.is_active=TRUE),
                    (SELECT COUNT(DISTINCT u.user_id)
                     FROM app_users u
                     JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
                     JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                     WHERE u.is_active=TRUE
                       AND UPPER(r.role_code) IN ('PROJECT_TEAM_COORDINATOR','SUPER_ADMINISTRATOR','ADMINISTRATOR')),
                    (SELECT COUNT(*) FROM schema_migrations
                     WHERE migration_id IN (
                       '040_scoped_role_policy_versions',
                       '043_ptc_time_steward_permissions')),
                    (SELECT COUNT(*) FROM schema_migrations
                     WHERE migration_id IN (
                       '044_project_expense_upload_certify_connection',
                       '044a_project_expense_self_certify_permission')),
                    (SELECT COUNT(*) FROM pg_tables
                     WHERE schemaname='public'
                       AND tablename IN (
                         'project_expense_uploads',
                         'project_expense_lines',
                         'project_expense_events',
                         'project_expense_mail_outbox',
                         'certify_connection_profiles',
                         'certify_expense_import_runs')),
                    (SELECT COUNT(*) FROM non_project_time_categories WHERE is_active=TRUE),
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger trigger_row
                        WHERE trigger_row.tgrelid = to_regclass('public.scoped_approval_stage_events')
                          AND trigger_row.tgname = 'trg_projectpulse040_approval_audit_immutable'
                          AND trigger_row.tgenabled <> 'D'
                          AND NOT trigger_row.tgisinternal
                    ),
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger trigger_row
                        WHERE trigger_row.tgrelid = to_regclass('public.scoped_role_policy_audit_events')
                          AND trigger_row.tgname = 'trg_projectpulse040_policy_audit_immutable'
                          AND trigger_row.tgenabled <> 'D'
                          AND NOT trigger_row.tgisinternal
                    );
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            var roleCount = Convert.ToInt32(reader.GetInt64(0));
            var moduleCount = Convert.ToInt32(reader.GetInt64(1));
            var publishedPolicyCount = Convert.ToInt32(reader.GetInt64(2));
            var effectiveGrantCount = Convert.ToInt32(reader.GetInt64(3));
            var eligibleUserCount = Convert.ToInt32(reader.GetInt64(4));
            var operatorCount = Convert.ToInt32(reader.GetInt64(5));
            var foundationalMigrationCount = Convert.ToInt32(reader.GetInt64(6));
            var expenseMigrationCount = Convert.ToInt32(reader.GetInt64(7));
            var expenseTableCount = Convert.ToInt32(reader.GetInt64(8));
            var nonProjectCategoryCount = Convert.ToInt32(reader.GetInt64(9));
            var immutableStageEvidenceReady = reader.GetBoolean(10);
            var immutableBatchEvidenceReady = reader.GetBoolean(11);

            var roleContractReady = roleCount == 12;
            var moduleContractReady = moduleCount == 70;
            var publishedPolicyReady = publishedPolicyCount == 1;
            var grantContractReady = effectiveGrantCount > 0;
            var eligibleUserContractReady = eligibleUserCount > 0;
            var operatorContractReady = operatorCount > 0;
            var foundationalMigrationsReady = foundationalMigrationCount == 2;
            var expenseMigrationsReady = expenseMigrationCount == 2;
            var expenseTablesReady = expenseTableCount == 6;
            var nonProjectCategoriesReady = nonProjectCategoryCount > 0;
            var immutableApprovalAuditReady = immutableStageEvidenceReady
                && immutableBatchEvidenceReady;
            var ready = roleContractReady
                && moduleContractReady
                && publishedPolicyReady
                && grantContractReady
                && eligibleUserContractReady
                && operatorContractReady
                && foundationalMigrationsReady
                && expenseMigrationsReady
                && expenseTablesReady
                && nonProjectCategoriesReady
                && immutableApprovalAuditReady;

            return Results.Json(new
            {
                status = ready ? "combined_module_runtime_ready" : "combined_module_runtime_incomplete",
                contractVersion = "combined-modules-001-005-012-037-038-public-v1",
                approvalWorkContractVersion = "approval-work-production-v2-2026-07-30",
                roleContractReady,
                moduleContractReady,
                publishedPolicyReady,
                grantContractReady,
                eligibleUserContractReady,
                operatorContractReady,
                foundationalMigrationsReady,
                expenseMigrationsReady,
                expenseTablesReady,
                nonProjectCategoriesReady,
                immutableStageEvidenceReady,
                immutableBatchEvidenceReady,
                immutableApprovalAuditReady,
                productionApprovalRoutingReady = immutableApprovalAuditReady,
                legacyApprovalWriteRoutesRetired = true,
                projectScopedPmApproval = true,
                nonProjectRoute = "manager_then_ptc",
                modules = new[] { "001", "005", "012", "037", "038" },
                operationalCountsReturned = false
            }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        }
        catch
        {
            return Results.Json(new
            {
                status = "combined_module_runtime_unavailable",
                contractVersion = "combined-modules-001-005-012-037-038-public-v1",
                approvalWorkContractVersion = "approval-work-production-v2-2026-07-30",
                message = "The combined runtime could not complete its readiness check.",
                operationalCountsReturned = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
