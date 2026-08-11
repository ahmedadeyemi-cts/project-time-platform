using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 010 Azure / Entra Directory Users import repair.
/// Preview remains on the existing endpoint; this unique import endpoint persists selected users.
/// </summary>
public static class AzureDirectoryImportModule
{
    private const string ModuleNumber = "010";

    private static readonly HashSet<string> AcceptedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_AZURE_AD",
        "MANAGE_AZURE_SYNC"
    };

    public static void MapEndpoints(WebApplication app)
    {
        app.MapPost(
            "/api/microsoft-integration/directory-users/import-selected",
            (Func<HttpContext, Task<IResult>>)ImportSelectedUsersAsync);
    }

    private static async Task<IResult> ImportSelectedUsersAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context)) return Results.Json(new
        {
            module = ModuleNumber,
            status = "view_as_read_only",
            message = "Exit Administrator View-As before importing Entra users."
        }, statusCode: StatusCodes.Status403Forbidden);

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid selected-user import payload is required.");
        }

        using (document)
        {
            var root = document.RootElement;
            var rawCandidates = ExtractCandidates(root);
            var selectedIdentifiers = ExtractSelectedIdentifiers(root);
            var requestedRoleCode = First(
                JsonStringAny(root, "defaultRoleCode", "default_role_code", "roleCode", "role_code"),
                "ENGINEERING").ToUpperInvariant();

            if (rawCandidates.Count == 0)
            {
                return Results.BadRequest(new
                {
                    module = ModuleNumber,
                    status = "no_selected_users_received",
                    imported = 0,
                    skipped = 0,
                    duplicate = 0,
                    failed = 0,
                    transactionCommitted = false,
                    message = "The import request did not include selected user records. Preview users must be included with compatible Entra identifiers."
                });
            }

            await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
            await connection.OpenAsync(context.RequestAborted);

            var userColumns = await ReadColumnsAsync(connection, "app_users", context.RequestAborted);
            if (!userColumns.Contains("user_id") || !userColumns.Contains("email"))
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "app_users_schema_unavailable",
                    message = "The Pulse user directory schema is unavailable for import."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var assignmentColumns = await ReadColumnsAsync(connection, "app_user_role_assignments", context.RequestAborted);
            var sourceDefaults = await ReadImportDefaultsAsync(connection, context.RequestAborted);
            var defaultRoleCode = requestedRoleCode == "ENGINEERING" && !string.IsNullOrWhiteSpace(sourceDefaults.DefaultRoleCode)
                ? sourceDefaults.DefaultRoleCode.ToUpperInvariant()
                : requestedRoleCode;
            var roleId = await ResolveRoleIdAsync(connection, defaultRoleCode, context.RequestAborted);
            var auditAvailable = await TableExistsAsync(connection, "microsoft_integration_audit_events", context.RequestAborted);

            var outcomes = new List<ImportOutcome>();
            var requestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            for (var index = 0; index < rawCandidates.Count; index++)
            {
                var candidate = NormalizeCandidate(rawCandidates[index], sourceDefaults.SourceProvider);
                var savepoint = $"module010_import_{index}";
                await ExecuteControlAsync(connection, transaction, $"SAVEPOINT {savepoint};", context.RequestAborted);

                try
                {
                    if (string.IsNullOrWhiteSpace(candidate.Email))
                    {
                        outcomes.Add(Failed(candidate, "missing_email", "not_attempted"));
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (!candidate.AccountEnabled)
                    {
                        outcomes.Add(Skipped(candidate, "account_disabled"));
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (selectedIdentifiers.Count > 0 && !CandidateWasSelected(candidate, selectedIdentifiers))
                    {
                        outcomes.Add(Skipped(candidate, "not_selected"));
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (!requestKeys.Add(candidate.Key))
                    {
                        outcomes.Add(Skipped(candidate, "duplicate_in_request"));
                        await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    var existingUserId = await FindExistingUserAsync(
                        connection,
                        transaction,
                        userColumns,
                        candidate,
                        context.RequestAborted);

                    Guid userId;
                    string status;
                    string resultCode;
                    if (existingUserId is not null)
                    {
                        userId = existingUserId.Value;
                        await UpdateUserAsync(connection, transaction, userColumns, userId, candidate, context.RequestAborted);
                        status = "duplicate";
                        resultCode = "existing_user_upserted";
                    }
                    else
                    {
                        userId = Guid.NewGuid();
                        await InsertUserAsync(
                            connection,
                            transaction,
                            userColumns,
                            userId,
                            candidate,
                            defaultRoleCode,
                            context.RequestAborted);
                        status = "imported";
                        resultCode = "user_inserted";
                    }

                    var roleAssignment = await EnsureRoleAssignmentAsync(
                        connection,
                        transaction,
                        assignmentColumns,
                        userId,
                        roleId,
                        access.Context.UserId,
                        defaultRoleCode,
                        context.RequestAborted);

                    outcomes.Add(new(
                        candidate.Key,
                        candidate.Email,
                        candidate.DisplayName,
                        status,
                        resultCode,
                        userId,
                        roleAssignment));
                    await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                }
                catch
                {
                    await ExecuteControlAsync(connection, transaction, $"ROLLBACK TO SAVEPOINT {savepoint};", context.RequestAborted);
                    outcomes.Add(Failed(candidate, "database_write_failed", "not_completed"));
                    await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                }
            }

            var imported = outcomes.Count(item => item.Status == "imported");
            var duplicate = outcomes.Count(item => item.Status == "duplicate");
            var skipped = outcomes.Count(item => item.Status == "skipped");
            var failed = outcomes.Count(item => item.Status == "failed");

            if (auditAvailable)
            {
                await InsertAuditAsync(
                    connection,
                    transaction,
                    access.Context,
                    sourceDefaults.TenantKey,
                    failed == 0 ? "success" : "completed_with_failures",
                    context.TraceIdentifier,
                    new { imported, duplicate, skipped, failed, defaultRoleCode },
                    context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = failed == 0 ? "selected_users_imported" : "selected_users_imported_with_failures",
                imported,
                skipped,
                duplicate,
                failed,
                defaultRoleCode,
                roleAssignment = roleId is null
                    ? "role_not_found_users_imported_without_assignment"
                    : "explicit_role_assignment_processed",
                transactionCommitted = true,
                visibility = new
                {
                    userAdministration = true,
                    activeUserSelectors = true,
                    identityProfileModule062 = true
                },
                results = outcomes,
                message = $"Import committed: {imported} new, {duplicate} existing/upserted, {skipped} skipped, and {failed} failed user(s)."
            });
        }
    }

    private static async Task<AccessResult> AuthorizeAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Entra directory import authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(r.role_code, ''), COALESCE(p.permission_code, '')
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0))) roles.Add(reader.GetString(0));
                if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1))) permissions.Add(reader.GetString(1));
            }

            var administrator = roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR");
            if (!administrator && !permissions.Any(AcceptedPermissions.Contains))
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "azure_directory_import_access_required",
                    message = "Administrator or delegated Azure/Entra synchronization access is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new(userId.Value, ActualEmail(context), connectionString), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Entra directory import authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static List<JsonElement> ExtractCandidates(JsonElement root)
    {
        foreach (var name in new[] { "selectedUsers", "users", "previewUsers", "candidates", "availableUsers" })
        {
            if (TryProperty(root, name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => item.Clone())
                    .ToList();
            }
        }
        return new();
    }

    private static HashSet<string> ExtractSelectedIdentifiers(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "emails", "selectedEmails", "userIds", "selectedUserIds", "entraObjectIds", "selectedEntraObjectIds" })
        {
            if (!TryProperty(root, name, out var property) || property.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in property.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim().ToLowerInvariant());
            }
        }
        return result;
    }

    private static ImportCandidate NormalizeCandidate(JsonElement element, string sourceProvider)
    {
        var email = First(JsonStringAny(element, "email", "mail", "userPrincipalName", "upn")).ToLowerInvariant();
        var entraObjectId = First(JsonStringAny(element, "entraObjectId", "entra_object_id", "id", "userId", "user_id"));
        var key = First(JsonStringAny(element, "previewKey", "preview_key"), entraObjectId, email).ToLowerInvariant();
        return new(
            key,
            email,
            First(JsonStringAny(element, "displayName", "display_name", "name"), email),
            entraObjectId,
            First(JsonStringAny(element, "sourceProvider", "source_provider"), sourceProvider, "ENTRA_ID_TEST"),
            First(JsonStringAny(element, "jobTitle", "job_title")),
            First(JsonStringAny(element, "departmentName", "department_name", "department")),
            First(JsonStringAny(element, "officeLocation", "office_location", "location")),
            First(JsonStringAny(element, "managerEmail", "manager_email")),
            JsonBoolAny(element, "accountEnabled", "account_enabled", "enabled") ?? true);
    }

    private static bool CandidateWasSelected(ImportCandidate candidate, HashSet<string> selected) =>
        selected.Contains(candidate.Key)
        || selected.Contains(candidate.Email)
        || (!string.IsNullOrWhiteSpace(candidate.EntraObjectId)
            && selected.Contains(candidate.EntraObjectId.ToLowerInvariant()));

    private static async Task<ImportDefaults> ReadImportDefaultsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT to_jsonb(s)::text FROM azure_entra_settings s LIMIT 1;", connection);
            var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(raw)) return new("onenecklab", "ENTRA_ID_TEST", "ENGINEERING");
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var source = First(JsonStringAny(root, "sourceProvider", "source_provider"), "ENTRA_ID_TEST");
            return new(
                source.Contains("TEST", StringComparison.OrdinalIgnoreCase) ? "onenecklab" : "ussignal",
                source,
                First(JsonStringAny(root, "defaultRoleCode", "default_role_code"), "ENGINEERING"));
        }
        catch
        {
            return new("onenecklab", "ENTRA_ID_TEST", "ENGINEERING");
        }
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table_name;
            """, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.' || @table_name) IS NOT NULL;", connection);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<Guid?> FindExistingUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        ImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        var predicates = new List<string>();
        if (columns.Contains("email")) predicates.Add("LOWER(email) = LOWER(@email)");
        if (columns.Contains("entra_object_id") && !string.IsNullOrWhiteSpace(candidate.EntraObjectId))
            predicates.Add("entra_object_id = @entra_object_id");
        if (predicates.Count == 0) return null;

        await using var command = new NpgsqlCommand(
            $"SELECT user_id FROM app_users WHERE {string.Join(" OR ", predicates)} LIMIT 1;",
            connection,
            transaction);
        command.Parameters.AddWithValue("email", candidate.Email);
        if (predicates.Any(value => value.Contains("entra_object_id", StringComparison.Ordinal)))
            command.Parameters.AddWithValue("entra_object_id", candidate.EntraObjectId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private static async Task InsertUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        ImportCandidate candidate,
        string defaultRoleCode,
        CancellationToken cancellationToken)
    {
        var values = UserValues(columns, candidate, defaultRoleCode, includeCreatedAt: true);
        values["user_id"] = userId;
        await ExecuteInsertAsync(connection, transaction, "app_users", columns, values, cancellationToken);
    }

    private static async Task UpdateUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        ImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        var values = UserValues(columns, candidate, string.Empty, includeCreatedAt: false);
        values.Remove("email");
        await ExecuteUpdateAsync(connection, transaction, "app_users", "user_id", userId, columns, values, cancellationToken);
    }

    private static Dictionary<string, object> UserValues(
        HashSet<string> columns,
        ImportCandidate candidate,
        string defaultRoleCode,
        bool includeCreatedAt)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = candidate.Email,
            ["display_name"] = candidate.DisplayName,
            ["source_provider"] = candidate.SourceProvider,
            ["is_active"] = true,
            ["login_enabled"] = true,
            ["is_login_enabled"] = true,
            ["last_directory_sync_at"] = DateTimeOffset.UtcNow,
            ["updated_at"] = DateTimeOffset.UtcNow
        };
        if (includeCreatedAt) values["created_at"] = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(candidate.EntraObjectId)) values["entra_object_id"] = candidate.EntraObjectId;
        if (!string.IsNullOrWhiteSpace(candidate.JobTitle)) values["job_title"] = candidate.JobTitle;
        if (!string.IsNullOrWhiteSpace(candidate.Department))
        {
            values["department_name"] = candidate.Department;
            values["department"] = candidate.Department;
        }
        if (!string.IsNullOrWhiteSpace(candidate.OfficeLocation)) values["office_location"] = candidate.OfficeLocation;
        if (!string.IsNullOrWhiteSpace(candidate.ManagerEmail)) values["manager_email"] = candidate.ManagerEmail;
        if (!string.IsNullOrWhiteSpace(defaultRoleCode)) values["role_name"] = defaultRoleCode;
        return values.Where(item => columns.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Guid?> ResolveRoleIdAsync(
        NpgsqlConnection connection,
        string roleCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT app_role_id
                FROM app_roles
                WHERE UPPER(role_code) = UPPER(@role_code)
                  AND is_active = TRUE
                LIMIT 1;
                """, connection);
            command.Parameters.AddWithValue("role_code", roleCode);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is Guid id ? id : Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> EnsureRoleAssignmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        Guid? roleId,
        Guid actorUserId,
        string roleCode,
        CancellationToken cancellationToken)
    {
        if (roleId is null) return $"role_not_found:{roleCode}";
        if (!columns.Contains("user_id") || !columns.Contains("app_role_id")) return "assignment_table_unavailable";

        await using (var exists = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_user_role_assignments assignment
                WHERE assignment.user_id = @user_id
                  AND assignment.app_role_id = @role_id
                  AND COALESCE(NULLIF(to_jsonb(assignment)->>'is_active', '')::boolean, TRUE) = TRUE
            );
            """, connection, transaction))
        {
            exists.Parameters.AddWithValue("user_id", userId);
            exists.Parameters.AddWithValue("role_id", roleId.Value);
            if (Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken)))
                return $"already_assigned:{roleCode}";
        }

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_user_role_assignment_id"] = Guid.NewGuid(),
            ["user_role_assignment_id"] = Guid.NewGuid(),
            ["user_id"] = userId,
            ["app_role_id"] = roleId.Value,
            ["is_active"] = true,
            ["assigned_by_user_id"] = actorUserId,
            ["created_by_user_id"] = actorUserId,
            ["assigned_at"] = DateTimeOffset.UtcNow,
            ["created_at"] = DateTimeOffset.UtcNow,
            ["updated_at"] = DateTimeOffset.UtcNow
        };
        await ExecuteInsertAsync(connection, transaction, "app_user_role_assignments", columns, values, cancellationToken);
        return $"assigned:{roleCode}";
    }

    private static async Task ExecuteInsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        HashSet<string> columns,
        Dictionary<string, object> values,
        CancellationToken cancellationToken)
    {
        var included = values.Where(item => columns.Contains(item.Key)).ToList();
        if (included.Count == 0) throw new InvalidOperationException("no_insertable_columns");
        var names = included.Select(item => QuoteIdentifier(item.Key)).ToArray();
        var parameters = included.Select((_, index) => $"@p{index}").ToArray();
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {QuoteIdentifier(table)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", parameters)});",
            connection,
            transaction);
        for (var index = 0; index < included.Count; index++)
            command.Parameters.AddWithValue($"p{index}", included[index].Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string keyColumn,
        Guid keyValue,
        HashSet<string> columns,
        Dictionary<string, object> values,
        CancellationToken cancellationToken)
    {
        var included = values.Where(item => columns.Contains(item.Key)
            && !item.Key.Equals(keyColumn, StringComparison.OrdinalIgnoreCase)).ToList();
        if (included.Count == 0) return;
        var assignments = included.Select((item, index) => $"{QuoteIdentifier(item.Key)} = @p{index}").ToArray();
        await using var command = new NpgsqlCommand(
            $"UPDATE {QuoteIdentifier(table)} SET {string.Join(", ", assignments)} WHERE {QuoteIdentifier(keyColumn)} = @key;",
            connection,
            transaction);
        for (var index = 0; index < included.Count; index++)
            command.Parameters.AddWithValue($"p{index}", included[index].Value);
        command.Parameters.AddWithValue("key", keyValue);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AccessContext access,
        string tenantKey,
        string outcome,
        string correlationId,
        object metadata,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO microsoft_integration_audit_events (
                actor_user_id,
                actor_email,
                action_code,
                tenant_key,
                outcome_code,
                correlation_id,
                event_metadata
            )
            VALUES (
                @actor,
                @email,
                'DIRECTORY_USERS_IMPORTED',
                @tenant_key,
                @outcome,
                @correlation,
                CAST(@metadata AS jsonb)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("actor", access.UserId);
        command.Parameters.AddWithValue("email", access.Email);
        command.Parameters.AddWithValue("tenant_key", tenantKey);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("correlation", correlationId ?? string.Empty);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ImportOutcome Failed(ImportCandidate candidate, string code, string roleAssignment) =>
        new(candidate.Key, candidate.Email, candidate.DisplayName, "failed", code, null, roleAssignment);

    private static ImportOutcome Skipped(ImportCandidate candidate, string code) =>
        new(candidate.Key, candidate.Email, candidate.DisplayName, "skipped", code, null, "not_attempted");

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || identifier.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new InvalidOperationException("unsafe_identifier");
        return $"\"{identifier}\"";
    }

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string ActualEmail(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualEmail", "ProjectPulseSessionEmail" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString()!.Trim().ToLowerInvariant();
        }
        return "unknown";
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static string BuildConnectionString()
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
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return string.Empty;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string JsonStringAny(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!.Trim();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return value.ToString().Trim();
        }
        return string.Empty;
    }

    private static bool? JsonBoolAny(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record AccessResult(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(Guid UserId, string Email, string ConnectionString);
    private sealed record ImportDefaults(string TenantKey, string SourceProvider, string DefaultRoleCode);
    private sealed record ImportCandidate(
        string Key,
        string Email,
        string DisplayName,
        string EntraObjectId,
        string SourceProvider,
        string JobTitle,
        string Department,
        string OfficeLocation,
        string ManagerEmail,
        bool AccountEnabled);
    private sealed record ImportOutcome(
        string PreviewKey,
        string Email,
        string DisplayName,
        string Status,
        string ResultCode,
        Guid? UserId,
        string RoleAssignment);
}
