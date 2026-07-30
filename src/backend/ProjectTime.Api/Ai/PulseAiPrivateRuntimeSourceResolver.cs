using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateRuntimeSourceResolver
{
    private static readonly HashSet<string> BroadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "EXECUTIVE"
    };

    private readonly ILogger<PulseAiPrivateRuntimeSourceResolver> _logger;

    public PulseAiPrivateRuntimeSourceResolver(
        ILogger<PulseAiPrivateRuntimeSourceResolver> logger)
    {
        _logger = logger;
    }

    public async Task<PulseAiAuthorizedDocumentSource?> ResolveAsync(
        Guid effectiveUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (MissingDatabaseConfiguration().Count > 0) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var access = await LoadAccessAsync(connection, effectiveUserId, cancellationToken);
            if (!access.IsActive) return null;

            const string sql = """
                SELECT
                    d.project_intake_document_id,
                    d.project_id,
                    p.project_code,
                    p.project_name,
                    COALESCE(c.client_name, 'No customer'),
                    COALESCE(d.document_type, 'other'),
                    LOWER(COALESCE(d.document_category, d.document_type, 'other')),
                    d.original_file_name,
                    d.stored_file_name,
                    d.storage_path,
                    d.content_type,
                    COALESCE(d.size_bytes, 0)::bigint,
                    COALESCE(d.engineering_visible, FALSE),
                    COALESCE(d.ai_timesheet_context_enabled, FALSE),
                    COALESCE(d.extraction_status, 'not_started'),
                    (
                        NULLIF(BTRIM(COALESCE(d.ai_context_summary, '')), '') IS NOT NULL
                        AND LOWER(COALESCE(d.extraction_status, '')) IN ('completed','ready','indexed','processed')
                    ),
                    d.ai_context_last_processed_at,
                    d.uploaded_at,
                    COALESCE(d.upload_source, 'manual'),
                    COALESCE(d.pulse_ai_classification,
                        CASE
                            WHEN LOWER(COALESCE(d.document_category, d.document_type, 'other')) IN (
                                'sow','statement_of_work','gsd','global_solution_design',
                                'contract','rate','pricing'
                            ) THEN 'restricted_internal_document'
                            WHEN LOWER(COALESCE(d.document_category, d.document_type, 'other')) IN (
                                'architecture','design','order','quote','proposal'
                            ) THEN 'confidential_project_document'
                            ELSE 'internal_project_document'
                        END)
                FROM project_intake_documents d
                JOIN projects p ON p.project_id = d.project_id
                LEFT JOIN clients c ON c.client_id = p.client_id
                WHERE d.project_intake_document_id = @document_id
                  AND d.is_active = TRUE
                  AND d.project_id IS NOT NULL
                  AND COALESCE(d.engineering_visible, FALSE) = TRUE
                  AND (
                    @is_broad = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1 FROM project_assignments pa
                        WHERE pa.project_id = p.project_id
                          AND pa.user_id = @user_id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests err
                        WHERE err.project_id = p.project_id
                          AND (
                            err.fulfilled_by_user_id = @user_id
                            OR err.assigned_pm_user_id = @user_id
                            OR EXISTS (
                                SELECT 1
                                FROM engineering_resource_request_assignments erra
                                WHERE erra.engineering_resource_request_id = err.engineering_resource_request_id
                                  AND erra.user_id = @user_id
                            )
                          )
                    )
                  )
                LIMIT 1;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("document_id", documentId);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("user_id", effectiveUserId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return new PulseAiAuthorizedDocumentSource(
                DocumentId: reader.GetGuid(0),
                ProjectId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ProjectCode: reader.GetString(2),
                ProjectName: reader.GetString(3),
                CustomerName: reader.GetString(4),
                DocumentType: reader.GetString(5),
                DocumentCategory: reader.GetString(6),
                OriginalFileName: reader.GetString(7),
                StoredFileName: reader.GetString(8),
                StoragePath: reader.GetString(9),
                ContentType: reader.IsDBNull(10) ? null : reader.GetString(10),
                SizeBytes: reader.GetInt64(11),
                EngineeringVisible: reader.GetBoolean(12),
                AiTimesheetContextEnabled: reader.GetBoolean(13),
                ExtractionStatus: reader.GetString(14),
                ExistingContextSummaryReady: reader.GetBoolean(15),
                ContextLastProcessedAt: reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                UploadedAt: reader.GetFieldValue<DateTimeOffset>(17),
                UploadSource: reader.GetString(18),
                AccessScope: access.ScopeLabel,
                Classification: reader.GetString(19),
                RoleCodes: access.RoleCodes.OrderBy(value => value).ToArray());
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private source resolution failed. DocumentId={DocumentId} Diagnostic={Diagnostic}",
                documentId,
                Diagnostic(exception));
            return null;
        }
    }

    private static async Task<AccessContext> LoadAccessAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                u.user_id,
                COALESCE(u.is_active, FALSE),
                COALESCE(string_agg(DISTINCT r.role_code, ',' ORDER BY r.role_code), '')
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.user_id = @user_id
            GROUP BY u.user_id, u.is_active;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return AccessContext.Empty(userId);
        var roles = reader.GetString(2)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AccessContext(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            roles);
    }

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        var required = new[] { "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD" };
        return required
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();
    }

    private static string ConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("PTP_DB_HOST"),
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("PTP_DB_NAME"),
            Username = Environment.GetEnvironmentVariable("PTP_DB_USER"),
            Password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD"),
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 5,
            Timeout = 8,
            CommandTimeout = 20
        };
        return builder.ConnectionString;
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "private_source_resolution_failure"
    };

    private sealed record AccessContext(
        Guid UserId,
        bool IsActive,
        IReadOnlySet<string> RoleCodes)
    {
        public bool IsBroadScope => RoleCodes.Overlaps(BroadRoles);
        public string ScopeLabel => IsBroadScope
            ? "organization_document_scope"
            : RoleCodes.Overlaps(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PROJECT_MANAGER",
                "PROJECT_MANAGEMENT",
                "PROJECT_MANAGEMENT_LEAD",
                "PROJECT_MANAGEMENT_TEAM_LEAD",
                "PM_TEAM_LEAD"
            })
                ? "managed_and_assigned_project_scope"
                : "assigned_project_scope";
        public static AccessContext Empty(Guid userId) =>
            new(userId, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}
