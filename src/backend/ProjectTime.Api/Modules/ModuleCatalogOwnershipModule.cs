using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Durable module-owner metadata for the Module Management table.
/// Ownership is descriptive accountability metadata only; it never grants role,
/// module, record, team, department, or organization access.
/// </summary>
public static class ModuleCatalogOwnershipModule
{
    private const string MigrationFile = "090_module_management_table_and_ownership.sql";
    private const string OwnerEligibilityPolicy = "developer_super_administrator_only";
    private static readonly string[] DeveloperOwnerRoleCodes =
    [
        "SUPER_ADMINISTRATOR",
        "SUPERADMINISTRATOR",
        "GLOBAL_ADMINISTRATOR",
        "GLOBALADMINISTRATOR"
    ];

    public sealed record ModuleOwnerUpdateRequest(
        Guid? OwnerUserId,
        string? OwnerEmail,
        int ExpectedRevision);

    public static WebApplication MapModuleCatalogOwnershipEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/module-catalog/owners",
            (Func<HttpContext, Task<IResult>>)GetOwnersAsync);
        app.MapPut(
            "/api/module-catalog/{moduleNumber}/owner",
            (HttpContext context, string moduleNumber, ModuleOwnerUpdateRequest request) =>
                UpdateOwnerAsync(context, moduleNumber, request));
        return app;
    }

    private static async Task<IResult> GetOwnersAsync(HttpContext context)
    {
        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        if (!actualUserId.HasValue)
            return Results.Json(new { status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized);

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);

            var administrator = await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                context,
                connection,
                cancellationToken: context.RequestAborted);
            actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
                context,
                "ProjectPulseActualUserId",
                "ProjectPulseSessionUserId") ?? actualUserId;
            var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context);
            var canManage = administrator
                && !isViewAs
                && actualUserId.HasValue
                && await IsDeveloperModuleOwnerAsync(
                    connection,
                    transaction: null,
                    actualUserId.Value,
                    context.RequestAborted);

            var owners = new List<object>();
            await using (var command = new NpgsqlCommand("""
                SELECT
                    module.module_code,
                    module.owner_user_id,
                    COALESCE(NULLIF(app_user.display_name, ''), NULLIF(app_user.email, ''), 'Unassigned') AS display_name,
                    COALESCE(NULLIF(external_identity.preferred_email, ''), NULLIF(app_user.email, ''), '') AS preferred_email,
                    COALESCE(module.owner_revision_number, 0),
                    module.owner_updated_at
                FROM scoped_role_policy_modules module
                LEFT JOIN app_users app_user
                  ON app_user.user_id = module.owner_user_id
                LEFT JOIN LATERAL (
                    SELECT COALESCE(NULLIF(link.email, ''), NULLIF(link.user_principal_name, '')) AS preferred_email
                    FROM auth_external_identity_links link
                    WHERE link.user_id = app_user.user_id
                      AND link.is_active = TRUE
                    ORDER BY
                      CASE WHEN lower(COALESCE(link.email, link.user_principal_name, '')) LIKE '%@ussignal.com' THEN 0 ELSE 1 END,
                      link.updated_at DESC NULLS LAST,
                      link.created_at DESC
                    LIMIT 1
                ) external_identity ON TRUE
                WHERE module.is_active = TRUE
                ORDER BY module.module_code;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
            {
                while (await reader.ReadAsync(context.RequestAborted))
                {
                    owners.Add(new
                    {
                        moduleNumber = reader.GetString(0),
                        ownerUserId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
                        displayName = reader.GetString(2),
                        email = canManage ? reader.GetString(3) : string.Empty,
                        revision = reader.GetInt32(4),
                        updatedAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5)
                    });
                }
            }

            var ownerCandidates = new List<object>();
            if (canManage)
            {
                await using var candidateCommand = new NpgsqlCommand("""
                    SELECT
                        app_user.user_id,
                        COALESCE(NULLIF(app_user.display_name, ''), NULLIF(app_user.email, ''), 'Unnamed user') AS display_name,
                        COALESCE(NULLIF(external_identity.preferred_email, ''), NULLIF(app_user.email, ''), '') AS preferred_email
                    FROM app_users app_user
                    LEFT JOIN LATERAL (
                        SELECT COALESCE(NULLIF(link.email, ''), NULLIF(link.user_principal_name, '')) AS preferred_email
                        FROM auth_external_identity_links link
                        WHERE link.user_id = app_user.user_id
                          AND link.is_active = TRUE
                        ORDER BY
                          CASE WHEN lower(COALESCE(link.email, link.user_principal_name, '')) LIKE '%@ussignal.com' THEN 0 ELSE 1 END,
                          link.updated_at DESC NULLS LAST,
                          link.created_at DESC
                        LIMIT 1
                    ) external_identity ON TRUE
                    WHERE app_user.is_active = TRUE
                      AND EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments owner_assignment
                        JOIN app_roles owner_role
                          ON owner_role.app_role_id = owner_assignment.app_role_id
                         AND owner_role.is_active = TRUE
                        WHERE owner_assignment.user_id = app_user.user_id
                          AND owner_assignment.is_active = TRUE
                          AND trim(both '_' from regexp_replace(
                                upper(btrim(COALESCE(owner_role.role_code, ''))),
                                '[^A-Z0-9]+',
                                '_',
                                'g')) = ANY(@developer_owner_role_codes)
                      )
                    ORDER BY display_name, preferred_email
                    LIMIT 1000;
                    """, connection);
                AddDeveloperOwnerRoleCodes(candidateCommand);
                await using var candidateReader = await candidateCommand.ExecuteReaderAsync(context.RequestAborted);
                while (await candidateReader.ReadAsync(context.RequestAborted))
                {
                    ownerCandidates.Add(new
                    {
                        userId = candidateReader.GetGuid(0),
                        displayName = candidateReader.GetString(1),
                        email = candidateReader.GetString(2)
                    });
                }
            }

            return Results.Ok(new
            {
                owners,
                ownerCandidates,
                access = new
                {
                    canManage,
                    isViewAs,
                    authoritySource = canManage
                        ? "actual_session_super_administrator"
                        : "authenticated_read_only"
                },
                policy = new
                {
                    ownershipDoesNotGrantAccess = true,
                    ownerEligibility = OwnerEligibilityPolicy,
                    migration = MigrationFile
                }
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UndefinedColumn
            || exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return DependencyUnavailable();
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ModuleCatalogOwnershipModule")
                .LogWarning("Module ownership could not be loaded ({ExceptionType}).", exception.GetType().Name);
            return DependencyUnavailable();
        }
    }

    private static async Task<IResult> UpdateOwnerAsync(
        HttpContext context,
        string moduleNumber,
        ModuleOwnerUpdateRequest request)
    {
        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        if (!actualUserId.HasValue)
            return Results.Json(new { status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized);
        if (ProjectPulseActualSessionAuthority.IsViewAs(context))
            return Results.Json(new { status = "view_as_read_only", message = "Exit View-As before changing module ownership." }, statusCode: StatusCodes.Status403Forbidden);

        var normalizedModule = (moduleNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedModule) || normalizedModule.Length > 16)
            return Results.BadRequest(new { status = "invalid_module", message = "A valid module number is required." });
        if (!request.OwnerUserId.HasValue && string.IsNullOrWhiteSpace(request.OwnerEmail))
            return Results.BadRequest(new { status = "owner_required", message = "Select an active module owner." });
        if (request.ExpectedRevision < 0)
            return Results.BadRequest(new { status = "invalid_revision", message = "The owner revision is invalid." });

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            var administrator = await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                context,
                connection,
                cancellationToken: context.RequestAborted);
            actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
                context,
                "ProjectPulseActualUserId",
                "ProjectPulseSessionUserId") ?? actualUserId;
            var developerOwner = administrator
                && actualUserId.HasValue
                && await IsDeveloperModuleOwnerAsync(
                    connection,
                    transaction: null,
                    actualUserId.Value,
                    context.RequestAborted);
            if (!developerOwner)
                return Results.Json(new
                {
                    status = "forbidden",
                    message = "Only an actual Super Administrator session can change module ownership. The session must belong to an active developer Super Administrator."
                }, statusCode: StatusCodes.Status403Forbidden);

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);

            Guid? previousOwnerId = null;
            var previousRevision = 0;
            await using (var currentCommand = new NpgsqlCommand("""
                SELECT owner_user_id, COALESCE(owner_revision_number, 0)
                FROM scoped_role_policy_modules
                WHERE upper(module_code) = @module_number
                  AND is_active = TRUE
                FOR UPDATE;
                """, connection, transaction))
            {
                currentCommand.Parameters.AddWithValue("module_number", normalizedModule);
                await using var reader = await currentCommand.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.NotFound(new { status = "module_not_found", message = $"Module {normalizedModule} is not registered." });
                }
                previousOwnerId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
                previousRevision = reader.GetInt32(1);
            }

            if (previousRevision != request.ExpectedRevision)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Json(new
                {
                    status = "owner_revision_conflict",
                    message = "Module ownership changed after this page was loaded. Refresh and try again.",
                    currentRevision = previousRevision
                }, statusCode: StatusCodes.Status409Conflict);
            }

            Guid ownerUserId;
            string ownerDisplayName;
            string ownerEmail;
            await using (var ownerCommand = new NpgsqlCommand("""
                SELECT
                    app_user.user_id,
                    COALESCE(NULLIF(app_user.display_name, ''), NULLIF(app_user.email, ''), 'Unnamed user') AS display_name,
                    COALESCE(NULLIF(external_identity.preferred_email, ''), NULLIF(app_user.email, ''), '') AS preferred_email
                FROM app_users app_user
                LEFT JOIN LATERAL (
                    SELECT COALESCE(NULLIF(link.email, ''), NULLIF(link.user_principal_name, '')) AS preferred_email
                    FROM auth_external_identity_links link
                    WHERE link.user_id = app_user.user_id
                      AND link.is_active = TRUE
                    ORDER BY
                      CASE WHEN lower(COALESCE(link.email, link.user_principal_name, '')) LIKE '%@ussignal.com' THEN 0 ELSE 1 END,
                      link.updated_at DESC NULLS LAST,
                      link.created_at DESC
                    LIMIT 1
                ) external_identity ON TRUE
                WHERE app_user.is_active = TRUE
                  AND EXISTS (
                    SELECT 1
                    FROM app_user_role_assignments owner_assignment
                    JOIN app_roles owner_role
                      ON owner_role.app_role_id = owner_assignment.app_role_id
                     AND owner_role.is_active = TRUE
                    WHERE owner_assignment.user_id = app_user.user_id
                      AND owner_assignment.is_active = TRUE
                      AND trim(both '_' from regexp_replace(
                            upper(btrim(COALESCE(owner_role.role_code, ''))),
                            '[^A-Z0-9]+',
                            '_',
                            'g')) = ANY(@developer_owner_role_codes)
                  )
                  AND (
                    (@owner_user_id IS NOT NULL AND app_user.user_id = @owner_user_id)
                    OR (@owner_email <> '' AND (
                      lower(app_user.email) = lower(@owner_email)
                      OR lower(COALESCE(external_identity.preferred_email, '')) = lower(@owner_email)
                    ))
                  )
                ORDER BY CASE WHEN @owner_user_id IS NOT NULL AND app_user.user_id = @owner_user_id THEN 0 ELSE 1 END
                LIMIT 1;
                """, connection, transaction))
            {
                ownerCommand.Parameters.Add("owner_user_id", NpgsqlDbType.Uuid).Value =
                    request.OwnerUserId.HasValue ? request.OwnerUserId.Value : DBNull.Value;
                ownerCommand.Parameters.AddWithValue("owner_email", (request.OwnerEmail ?? string.Empty).Trim());
                AddDeveloperOwnerRoleCodes(ownerCommand);
                await using var reader = await ownerCommand.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.BadRequest(new
                    {
                        status = "owner_not_found",
                        message = "The selected owner must be an active developer Super Administrator."
                    });
                }
                ownerUserId = reader.GetGuid(0);
                ownerDisplayName = reader.GetString(1);
                ownerEmail = reader.GetString(2);
            }

            var nextRevision = previousRevision + 1;
            var updatedAt = DateTimeOffset.UtcNow;
            await using (var updateCommand = new NpgsqlCommand("""
                UPDATE scoped_role_policy_modules
                SET owner_user_id = @owner_user_id,
                    owner_revision_number = @next_revision,
                    owner_updated_at = @updated_at,
                    owner_updated_by_user_id = @actor_user_id
                WHERE upper(module_code) = @module_number
                  AND is_active = TRUE;
                """, connection, transaction))
            {
                updateCommand.Parameters.AddWithValue("owner_user_id", ownerUserId);
                updateCommand.Parameters.AddWithValue("next_revision", nextRevision);
                updateCommand.Parameters.AddWithValue("updated_at", updatedAt);
                updateCommand.Parameters.AddWithValue("actor_user_id", actualUserId.Value);
                updateCommand.Parameters.AddWithValue("module_number", normalizedModule);
                await updateCommand.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await using (var auditCommand = new NpgsqlCommand("""
                INSERT INTO scoped_role_policy_audit_events (
                    policy_version_id,
                    event_code,
                    actor_user_id,
                    actor_email,
                    reason,
                    previous_state,
                    new_state,
                    event_metadata
                )
                VALUES (
                    NULL,
                    'MODULE_OWNER_CHANGED',
                    @actor_user_id,
                    @actor_email,
                    'Super Administrator changed module ownership from Module Management.',
                    jsonb_build_object(
                        'moduleNumber', @module_number,
                        'ownerUserId', @previous_owner_user_id,
                        'revision', @previous_revision
                    ),
                    jsonb_build_object(
                        'moduleNumber', @module_number,
                        'ownerUserId', @owner_user_id,
                        'ownerEmail', @owner_email,
                        'revision', @next_revision
                    ),
                    jsonb_build_object(
                        'immutableAudit', TRUE,
                        'ownershipDoesNotGrantAccess', TRUE
                    )
                );
                """, connection, transaction))
            {
                auditCommand.Parameters.AddWithValue("actor_user_id", actualUserId.Value);
                auditCommand.Parameters.AddWithValue("actor_email", ProjectPulseActualSessionAuthority.ReadActualEmail(context));
                auditCommand.Parameters.AddWithValue("module_number", normalizedModule);
                auditCommand.Parameters.Add("previous_owner_user_id", NpgsqlDbType.Uuid).Value = previousOwnerId.HasValue ? previousOwnerId.Value : DBNull.Value;
                auditCommand.Parameters.AddWithValue("previous_revision", previousRevision);
                auditCommand.Parameters.AddWithValue("owner_user_id", ownerUserId);
                auditCommand.Parameters.AddWithValue("owner_email", ownerEmail);
                auditCommand.Parameters.AddWithValue("next_revision", nextRevision);
                await auditCommand.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "owner_updated",
                message = $"Module {normalizedModule} owner updated to {ownerDisplayName}.",
                owner = new
                {
                    moduleNumber = normalizedModule,
                    ownerUserId,
                    displayName = ownerDisplayName,
                    email = ownerEmail,
                    revision = nextRevision,
                    updatedAt
                },
                policy = new
                {
                    ownershipDoesNotGrantAccess = true,
                    ownerEligibility = OwnerEligibilityPolicy
                }
            });
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UndefinedColumn
            || exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return DependencyUnavailable();
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ModuleCatalogOwnershipModule")
                .LogWarning("Module ownership could not be updated ({ExceptionType}).", exception.GetType().Name);
            return Results.Json(new { status = "owner_update_failed", message = "Module ownership could not be updated. No access permissions were changed." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<bool> IsDeveloperModuleOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) return false;

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_users app_user
                JOIN app_user_role_assignments assignment
                  ON assignment.user_id = app_user.user_id
                 AND assignment.is_active = TRUE
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                WHERE app_user.user_id = @user_id
                  AND app_user.is_active = TRUE
                  AND trim(both '_' from regexp_replace(
                        upper(btrim(COALESCE(role.role_code, ''))),
                        '[^A-Z0-9]+',
                        '_',
                        'g')) = ANY(@developer_owner_role_codes)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        AddDeveloperOwnerRoleCodes(command);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static void AddDeveloperOwnerRoleCodes(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue(
            "developer_owner_role_codes",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            DeveloperOwnerRoleCodes);
    }

    private static string? BuildConnectionString()
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
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private static IResult DependencyUnavailable() =>
        Results.Json(new
        {
            status = "module_ownership_unavailable",
            migration = MigrationFile,
            message = "Module ownership storage is not available. No module owner or access permission was changed."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
}
