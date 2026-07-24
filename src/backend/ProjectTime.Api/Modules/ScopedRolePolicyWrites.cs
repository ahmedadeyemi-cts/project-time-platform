using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<IResult> ValidateDraftAsync(
        PolicyPublishRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actor = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actor.Error is not null) return actor.Error;

        var validation = await ValidateChangesAsync(connection, request, null);
        return Results.Ok(validation);
    }

    private static async Task<IResult> PublishAsync(
        PolicyPublishRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "reason_required",
                message = "A reason is required to publish a policy version."
            });
        }

        var validation = await ValidateChangesAsync(connection, request, null);
        if (!validation.Valid)
        {
            return Results.BadRequest(validation);
        }

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var current = await LoadPublishedVersionAsync(connection, transaction);
            if (current is null)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new
                {
                    status = "published_policy_missing",
                    message = "No published scoped policy exists."
                });
            }

            if (request.BaseVersionNumber is not null
                && request.BaseVersionNumber.Value != current.VersionNumber)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new
                {
                    status = "policy_version_conflict",
                    expectedVersion = request.BaseVersionNumber,
                    actualVersion = current.VersionNumber,
                    message = "The policy changed after this draft was loaded. Refresh and review the latest version."
                });
            }

            var nextVersion = await NextVersionNumberAsync(connection, transaction);
            var newPolicyId = Guid.NewGuid();
            await InsertVersionAsync(
                connection,
                transaction,
                newPolicyId,
                nextVersion,
                $"Scoped RBAC policy v{nextVersion}",
                "DRAFT",
                current.SourceName,
                current.SourceSha256,
                request.Reason.Trim(),
                actor.ActualUserId,
                null);

            await using (var clone = new NpgsqlCommand("""
                INSERT INTO scoped_role_policy_grants (
                    policy_version_id, role_code, module_code, action_code,
                    scope_code, grant_effect, conditions, delegated_authority,
                    reason_required, audit_required, source_designation,
                    source_notes, is_active
                )
                SELECT
                    @new_policy_id, role_code, module_code, action_code,
                    scope_code, grant_effect, conditions, delegated_authority,
                    reason_required, audit_required, source_designation,
                    source_notes, is_active
                FROM scoped_role_policy_grants
                WHERE policy_version_id = @current_policy_id;
                """, connection, transaction))
            {
                clone.Parameters.AddWithValue("new_policy_id", newPolicyId);
                clone.Parameters.AddWithValue("current_policy_id", current.PolicyVersionId);
                await clone.ExecuteNonQueryAsync();
            }

            await ApplyChangesAsync(connection, transaction, newPolicyId, request.Changes ?? new());
            var postCloneValidation = await ValidatePolicyVersionAsync(connection, transaction, newPolicyId);
            if (!postCloneValidation.Valid)
            {
                await transaction.RollbackAsync();
                return Results.BadRequest(postCloneValidation);
            }

            await using (var retire = new NpgsqlCommand("""
                UPDATE scoped_role_policy_versions
                SET policy_status = 'RETIRED', retired_at = NOW()
                WHERE policy_version_id = @current_policy_id;
                """, connection, transaction))
            {
                retire.Parameters.AddWithValue("current_policy_id", current.PolicyVersionId);
                await retire.ExecuteNonQueryAsync();
            }

            await using (var publish = new NpgsqlCommand("""
                UPDATE scoped_role_policy_versions
                SET policy_status = 'PUBLISHED',
                    published_by_user_id = @actor_user_id,
                    published_at = NOW()
                WHERE policy_version_id = @new_policy_id;
                """, connection, transaction))
            {
                publish.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                publish.Parameters.AddWithValue("new_policy_id", newPolicyId);
                await publish.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(
                connection,
                transaction,
                newPolicyId,
                "POLICY_VERSION_PUBLISHED",
                actor,
                request.Reason.Trim(),
                JsonSerializer.SerializeToElement(new
                {
                    current.PolicyVersionId,
                    current.VersionNumber
                }),
                JsonSerializer.SerializeToElement(new
                {
                    policyVersionId = newPolicyId,
                    versionNumber = nextVersion,
                    changedRoleModulePairs = request.Changes?.Count ?? 0
                }));

            await transaction.CommitAsync();
            return Results.Ok(new
            {
                status = "policy_published",
                policyVersionId = newPolicyId,
                versionNumber = nextVersion,
                message = "A new immutable scoped RBAC policy version was published."
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Results.Problem(
                title: "Scoped policy publish failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> RestoreAsync(
        Guid policyVersionId,
        PolicyRestoreRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actorResult = await RequireOwnSessionSuperAdministratorAsync(context, connection);
        if (actorResult.Error is not null) return actorResult.Error;
        var actor = actorResult.Actor!;

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "reason_required",
                message = "A reason is required to restore a policy version."
            });
        }

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await AdvisoryLockAsync(connection, transaction);
            var source = await LoadVersionAsync(connection, transaction, policyVersionId);
            var current = await LoadPublishedVersionAsync(connection, transaction);
            if (source is null || current is null)
            {
                await transaction.RollbackAsync();
                return Results.NotFound(new
                {
                    status = "policy_version_not_found",
                    message = "The selected policy version could not be restored."
                });
            }

            var nextVersion = await NextVersionNumberAsync(connection, transaction);
            var newPolicyId = Guid.NewGuid();
            await InsertVersionAsync(
                connection,
                transaction,
                newPolicyId,
                nextVersion,
                $"Restored policy v{source.VersionNumber} as v{nextVersion}",
                "DRAFT",
                source.SourceName,
                source.SourceSha256,
                request.Reason.Trim(),
                actor.ActualUserId,
                source.PolicyVersionId);

            await using (var clone = new NpgsqlCommand("""
                INSERT INTO scoped_role_policy_grants (
                    policy_version_id, role_code, module_code, action_code,
                    scope_code, grant_effect, conditions, delegated_authority,
                    reason_required, audit_required, source_designation,
                    source_notes, is_active
                )
                SELECT
                    @new_policy_id, role_code, module_code, action_code,
                    scope_code, grant_effect, conditions, delegated_authority,
                    reason_required, audit_required, source_designation,
                    source_notes, is_active
                FROM scoped_role_policy_grants
                WHERE policy_version_id = @source_policy_id;
                """, connection, transaction))
            {
                clone.Parameters.AddWithValue("new_policy_id", newPolicyId);
                clone.Parameters.AddWithValue("source_policy_id", source.PolicyVersionId);
                await clone.ExecuteNonQueryAsync();
            }

            var validation = await ValidatePolicyVersionAsync(connection, transaction, newPolicyId);
            if (!validation.Valid)
            {
                await transaction.RollbackAsync();
                return Results.BadRequest(validation);
            }

            await using (var retire = new NpgsqlCommand("""
                UPDATE scoped_role_policy_versions
                SET policy_status = 'RETIRED', retired_at = NOW()
                WHERE policy_version_id = @current_policy_id;
                """, connection, transaction))
            {
                retire.Parameters.AddWithValue("current_policy_id", current.PolicyVersionId);
                await retire.ExecuteNonQueryAsync();
            }

            await using (var publish = new NpgsqlCommand("""
                UPDATE scoped_role_policy_versions
                SET policy_status = 'PUBLISHED',
                    published_by_user_id = @actor_user_id,
                    published_at = NOW()
                WHERE policy_version_id = @new_policy_id;
                """, connection, transaction))
            {
                publish.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                publish.Parameters.AddWithValue("new_policy_id", newPolicyId);
                await publish.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(
                connection,
                transaction,
                newPolicyId,
                "POLICY_VERSION_RESTORED",
                actor,
                request.Reason.Trim(),
                JsonSerializer.SerializeToElement(new
                {
                    current.PolicyVersionId,
                    current.VersionNumber
                }),
                JsonSerializer.SerializeToElement(new
                {
                    sourcePolicyVersionId = source.PolicyVersionId,
                    source.VersionNumber,
                    restoredPolicyVersionId = newPolicyId,
                    restoredVersionNumber = nextVersion
                }));

            await transaction.CommitAsync();
            return Results.Ok(new
            {
                status = "policy_restored",
                sourcePolicyVersionId = source.PolicyVersionId,
                sourceVersionNumber = source.VersionNumber,
                policyVersionId = newPolicyId,
                versionNumber = nextVersion,
                message = "The selected policy was restored as a new immutable version."
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Results.Problem(
                title: "Scoped policy restore failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<PolicyValidationResult> ValidateChangesAsync(
        NpgsqlConnection connection,
        PolicyPublishRequest request,
        NpgsqlTransaction? transaction)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (request.Changes is null || request.Changes.Count == 0)
        {
            errors.Add("At least one role/module policy change is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors.Add("A reason is required.");
        }

        var validActions = await LoadCodeSetAsync(
            connection,
            transaction,
            "SELECT action_code FROM scoped_role_policy_actions WHERE is_active = TRUE;");
        var validScopes = await LoadCodeSetAsync(
            connection,
            transaction,
            "SELECT scope_code FROM scoped_role_policy_scopes WHERE is_active = TRUE;");
        var validModules = await LoadCodeSetAsync(
            connection,
            transaction,
            "SELECT module_code FROM scoped_role_policy_modules WHERE is_active = TRUE;");

        foreach (var change in request.Changes ?? new())
        {
            var canonicalRole = CanonicalRole(change.RoleCode);
            if (!CanonicalRoleOrder.Contains(canonicalRole, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Unknown canonical role: {change.RoleCode}.");
            }
            if (!validModules.Contains(change.ModuleCode))
            {
                errors.Add($"Unknown module: {change.ModuleCode}.");
            }
            if (change.Grants is null || change.Grants.Count == 0)
            {
                warnings.Add($"{canonicalRole}/{change.ModuleCode} will fall back to legacy authorization because no scoped grants were supplied.");
                continue;
            }

            foreach (var grant in change.Grants)
            {
                if (!validActions.Contains(grant.ActionCode))
                    errors.Add($"Unknown action: {grant.ActionCode}.");
                if (!validScopes.Contains(grant.ScopeCode))
                    errors.Add($"Unknown scope: {grant.ScopeCode}.");
                if (!new[] { "GRANT", "DENY" }.Contains(
                        grant.Effect?.ToUpperInvariant(),
                        StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Invalid effect for {grant.ActionCode}: {grant.Effect}.");
                if (ScopedRolePolicyRules.NonBypassableActions.Contains(grant.ActionCode)
                    && string.Equals(grant.Effect, "GRANT", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{grant.ActionCode} is non-bypassable and cannot be granted by Full Control.");
                if (change.ModuleCode == "003"
                    && grant.ActionCode != "MODULE_VIEW"
                    && grant.ActionCode != "UTILIZATION_VIEW"
                    && !string.Equals(grant.Effect, "DENY", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Module 003 is read-only and cannot receive write grants.");
                if (change.ModuleCode == "037"
                    && !new[] { "MODULE_VIEW", "MATRIX_VIEW", "MATRIX_EXPORT", "ACCESS_EXPLAIN" }
                        .Contains(grant.ActionCode, StringComparer.OrdinalIgnoreCase)
                    && !string.Equals(grant.Effect, "DENY", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Module 037 is read-only and cannot receive policy write actions.");
                if (change.ModuleCode == "012"
                    && ScopedRolePolicyRules.IsWriteAction(grant.ActionCode)
                    && !string.Equals(canonicalRole, "SUPER_ADMINISTRATOR", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(grant.Effect, "GRANT", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Only Super Administrator may receive Module 012 policy write actions.");
            }
        }

        var activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection, transaction);
        if (activeSuperAdministrators < 1)
        {
            errors.Add("The final active Super Administrator must not be removed.");
        }

        return new PolicyValidationResult(
            errors.Count == 0,
            errors,
            warnings,
            activeSuperAdministrators);
    }

    private static async Task<PolicyValidationResult> ValidatePolicyVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid policyVersionId)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var activeSuperAdministrators = await CountActiveSuperAdministratorsAsync(connection, transaction);
        if (activeSuperAdministrators < 1)
            errors.Add("The final active Super Administrator must not be removed.");

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM scoped_role_policy_grants
            WHERE policy_version_id = @policy_version_id
              AND role_code = 'SUPER_ADMINISTRATOR'
              AND module_code = '012'
              AND action_code IN ('POLICY_PUBLISH','POLICY_RESTORE')
              AND grant_effect = 'GRANT'
              AND is_active = TRUE;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("policy_version_id", policyVersionId);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
            if (count < 2)
                errors.Add("Super Administrator must retain Module 012 publish and restore authority.");
        }

        await using (var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM scoped_role_policy_grants grant_row
            JOIN scoped_role_policy_actions action_row
              ON action_row.action_code = grant_row.action_code
            WHERE grant_row.policy_version_id = @policy_version_id
              AND grant_row.grant_effect = 'GRANT'
              AND action_row.is_non_bypassable = TRUE;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("policy_version_id", policyVersionId);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
            if (count > 0)
                errors.Add("Non-bypassable safety actions cannot be granted.");
        }

        return new PolicyValidationResult(
            errors.Count == 0,
            errors,
            warnings,
            activeSuperAdministrators);
    }

    private static async Task ApplyChangesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid policyVersionId,
        List<PolicyModuleChange> changes)
    {
        foreach (var change in changes)
        {
            var roleCode = CanonicalRole(change.RoleCode);
            await using (var delete = new NpgsqlCommand("""
                DELETE FROM scoped_role_policy_grants
                WHERE policy_version_id = @policy_version_id
                  AND role_code = @role_code
                  AND module_code = @module_code;
                """, connection, transaction))
            {
                delete.Parameters.AddWithValue("policy_version_id", policyVersionId);
                delete.Parameters.AddWithValue("role_code", roleCode);
                delete.Parameters.AddWithValue("module_code", change.ModuleCode.Trim());
                await delete.ExecuteNonQueryAsync();
            }

            foreach (var grant in change.Grants ?? new())
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO scoped_role_policy_grants (
                        policy_version_id, role_code, module_code, action_code,
                        scope_code, grant_effect, conditions,
                        delegated_authority, reason_required, audit_required,
                        source_designation, source_notes, is_active
                    )
                    VALUES (
                        @policy_version_id, @role_code, @module_code, @action_code,
                        @scope_code, @grant_effect, @conditions::jsonb,
                        @delegated_authority, @reason_required, @audit_required,
                        'Module 012 policy administration', @source_notes, @is_active
                    );
                    """, connection, transaction);
                insert.Parameters.AddWithValue("policy_version_id", policyVersionId);
                insert.Parameters.AddWithValue("role_code", roleCode);
                insert.Parameters.AddWithValue("module_code", change.ModuleCode.Trim());
                insert.Parameters.AddWithValue("action_code", grant.ActionCode.Trim().ToUpperInvariant());
                insert.Parameters.AddWithValue("scope_code", grant.ScopeCode.Trim().ToUpperInvariant());
                insert.Parameters.AddWithValue("grant_effect", grant.Effect.Trim().ToUpperInvariant());
                insert.Parameters.AddWithValue("conditions", grant.Conditions?.GetRawText() ?? "{}");
                insert.Parameters.AddWithValue("delegated_authority", grant.DelegatedAuthority);
                insert.Parameters.AddWithValue("reason_required", grant.ReasonRequired);
                insert.Parameters.AddWithValue("audit_required", grant.AuditRequired);
                insert.Parameters.AddWithValue("source_notes", change.Notes?.Trim() ?? string.Empty);
                insert.Parameters.AddWithValue("is_active", grant.IsActive);
                await insert.ExecuteNonQueryAsync();
            }
        }
    }
}
