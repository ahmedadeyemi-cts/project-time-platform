using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

public static class CelarAiCapabilityTargets
{
    public const string CelarAi = "celar_ai";
    public const string Claude = ProjectPulseAiProviders.Claude;
    public const string OpenAi = ProjectPulseAiProviders.OpenAi;
    public const string Local = ProjectPulseAiProviders.Local;

    public static readonly string[] All = [CelarAi, Claude, OpenAi, Local];
    public static readonly string[] DefaultOrder = [CelarAi, Claude, OpenAi, Local];
}

public sealed record CelarAiCapabilityDefinition(
    string FeatureCode,
    string DisplayName,
    IReadOnlyList<string> ConsumerModules,
    string ExternalContextPolicy,
    string ContextClassification,
    string Description);

public static class CelarAiCapabilityCatalog
{
    public const string TimesheetCompatibility = ProjectPulseAiFeatures.TimesheetDescription;
    public const string TimesheetNonProject = "timesheet_non_project_description";
    public const string TimesheetProjectTask = "timesheet_project_task_description";
    public const string TimesheetServiceRequest = "timesheet_service_request_description";
    public const string SowGsdPlanning = ProjectPulseAiFeatures.SowGsdPlanning;
    public const string HelpAssistant = ProjectPulseAiFeatures.HelpAssistant;
    public const string CloseoutCommunication = ProjectPulseAiFeatures.CloseoutCommunication;
    public const string ProjectFlowHivePlan = ProjectPulseAiFeatures.ProjectFlowHivePlan;

    public static readonly IReadOnlyDictionary<string, CelarAiCapabilityDefinition> Definitions =
        new Dictionary<string, CelarAiCapabilityDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [TimesheetNonProject] = new(
                TimesheetNonProject,
                "Timesheet — Non-project time",
                ["001"],
                "sanitized_non_document_context_only",
                "internal_non_document",
                "Uses the employee note, category, date, and non-project row context. It does not assume project documents."),
            [TimesheetProjectTask] = new(
                TimesheetProjectTask,
                "Timesheet — Project tasks",
                ["001", "019"],
                "sanitized_generic_only",
                "restricted_project",
                "Uses authorized project/task evidence and private project documents. Raw project evidence remains private."),
            [TimesheetServiceRequest] = new(
                TimesheetServiceRequest,
                "Timesheet — Requests / Service Requests",
                ["001", "019"],
                "sanitized_generic_only",
                "restricted_request",
                "Uses authorized request metadata and related project, IQS, document, attachment, and governed email evidence."),
            [SowGsdPlanning] = new(
                SowGsdPlanning,
                "SOW / GSD planning",
                ["011", "025"],
                "sanitized_generic_only",
                "restricted_commercial_document",
                "Creates private, non-binding planning drafts with citations and required human review."),
            [ProjectFlowHivePlan] = new(
                ProjectFlowHivePlan,
                "Project FlowHive plan, schedule, and diagram",
                ["011", "066"],
                "sanitized_generic_only",
                "restricted_project_plan",
                "Creates a private WBS, dependencies, milestones, timeline, and diagram before deterministic scheduling and review."),
            [CloseoutCommunication] = new(
                CloseoutCommunication,
                "Closeout communication",
                ["011", "040", "055C"],
                "sanitized_generic_only",
                "restricted_project_closeout",
                "Creates unsent internal-review and customer-ready closeout drafts from authorized completion evidence."),
            [HelpAssistant] = new(
                HelpAssistant,
                "Celar AI Help, Search, and troubleshooting",
                ["011", "999"],
                "sanitized_generic_only",
                "permission_scoped_system_intelligence",
                "Uses source-controlled operating knowledge and authorized system tools before optional generic assistance.")
        };

    public static CelarAiCapabilityDefinition Resolve(string? feature)
    {
        var normalized = NormalizeFeature(feature);
        return Definitions.TryGetValue(normalized, out var definition)
            ? definition
            : Definitions[HelpAssistant];
    }

    public static string NormalizeFeature(string? feature)
    {
        var normalized = feature?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized == TimesheetCompatibility ? TimesheetNonProject : normalized;
    }

    public static string ResolveTimesheetFeature(
        string? rowType,
        string? rowLabel,
        string? taskCode,
        string? projectCode,
        string? projectName)
    {
        var row = $"{rowType} {rowLabel} {taskCode}".ToLowerInvariant();
        if (row.Contains("service request", StringComparison.Ordinal)
            || row.Contains("service_request", StringComparison.Ordinal)
            || row.Contains("request", StringComparison.Ordinal)
            || row.Contains("sr-", StringComparison.Ordinal))
        {
            return TimesheetServiceRequest;
        }

        if (!string.IsNullOrWhiteSpace(projectCode)
            || !string.IsNullOrWhiteSpace(projectName)
            || row.Contains("project", StringComparison.Ordinal))
        {
            return TimesheetProjectTask;
        }

        return TimesheetNonProject;
    }

    public static IReadOnlyList<string> ValidateTargets(IEnumerable<string>? values)
    {
        var targets = (values ?? [])
            .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToArray();
        if (targets.Length != 4)
            throw new ArgumentException("Select exactly four targets: primary, secondary, tertiary, and final fallback.");
        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
            throw new ArgumentException("A capability route cannot contain duplicate targets.");
        if (targets.Any(target => !CelarAiCapabilityTargets.All.Contains(target, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("The route contains an unsupported AI target.");
        if (!string.Equals(targets[^1], CelarAiCapabilityTargets.Local, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Governed local template must remain the final fallback.");
        return targets;
    }
}

public sealed record CelarAiCapabilityRouteSnapshot(
    string FeatureCode,
    string DisplayName,
    IReadOnlyList<string> ConsumerModules,
    string ExternalContextPolicy,
    string ContextClassification,
    IReadOnlyList<string> Targets,
    int Revision,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    bool Persisted)
{
    public object ToPublicResponse() => new
    {
        feature = FeatureCode,
        displayName = DisplayName,
        consumerModules = ConsumerModules,
        externalContextPolicy = ExternalContextPolicy,
        contextClassification = ContextClassification,
        primary = Targets.ElementAtOrDefault(0),
        secondary = Targets.ElementAtOrDefault(1),
        tertiary = Targets.ElementAtOrDefault(2),
        finalFallback = Targets.ElementAtOrDefault(3),
        targets = Targets,
        revision = Revision,
        updatedAt = UpdatedAt,
        updatedBy = UpdatedBy,
        persisted = Persisted,
        duplicateRequests = false,
        safetyRefusalFailover = false,
        privacyPolicyEditable = false,
        stateChanged = false
    };
}

public sealed record CelarAiPrivateModelProfile(
    string EnvironmentCode,
    bool Enabled,
    string Endpoint,
    string Model,
    string BearerToken,
    IReadOnlyList<string> PrivateHostAllowlist,
    bool RequirePrivateModelForDocuments,
    int Revision,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    string EndpointHostFingerprint,
    string TokenFingerprint,
    bool Persisted)
{
    public bool EndpointConfigured => !string.IsNullOrWhiteSpace(Endpoint);
    public bool ModelConfigured => !string.IsNullOrWhiteSpace(Model);
    public bool Configured => EndpointConfigured && ModelConfigured;
    public bool Ready => Enabled && Configured;

    public object ToPublicResponse(string endpointPolicyStatus = "not_tested") => new
    {
        environment = EnvironmentCode,
        enabled = Enabled,
        configured = Configured,
        ready = Ready,
        endpointConfigured = EndpointConfigured,
        endpointHostFingerprint = EndpointHostFingerprint,
        endpointReturned = false,
        model = ModelConfigured ? Model : "Not configured",
        bearerTokenConfigured = !string.IsNullOrWhiteSpace(BearerToken),
        bearerTokenFingerprint = TokenFingerprint,
        bearerTokenReturned = false,
        privateHostAllowlistCount = PrivateHostAllowlist.Count,
        requirePrivateModelForDocuments = RequirePrivateModelForDocuments,
        revision = Revision,
        updatedAt = UpdatedAt,
        updatedBy = UpdatedBy,
        persisted = Persisted,
        endpointPolicyStatus,
        confidentialContextEligible = Ready && endpointPolicyStatus is "private_endpoint_approved" or "not_tested",
        rawInternalDocumentsMayUsePublicProviders = false,
        stateChanged = false
    };
}

public sealed record CelarAiRouteUpdateRequest(
    IReadOnlyList<string>? Targets,
    int? ExpectedRevision);

public sealed record CelarAiPrivateModelSettingsRequest(
    bool? Enabled,
    string? Endpoint,
    string? Model,
    IReadOnlyList<string>? PrivateHostAllowlist,
    bool? RequirePrivateModelForDocuments,
    int? ExpectedRevision);

public sealed record CelarAiPrivateModelSecretRequest(
    string? BearerToken,
    int? ExpectedRevision);

public sealed record CelarAiCapabilityExecutionContext(
    string Feature,
    bool ContainsPrivateDocuments,
    bool ContainsCustomerIdentity,
    bool ContainsPeopleRecords,
    bool ContainsFinancialValues,
    bool AllowSanitizedExternalAssistance,
    IReadOnlyList<string> SensitiveTerms,
    string ConsumerModule,
    string CorrelationId);

public sealed class CelarAiConfigurationConflictException(string message) : InvalidOperationException(message);

public sealed class CelarAiCapabilityRoutingStore
{
    private readonly string? _connectionString;
    private readonly byte[]? _encryptionKey;
    private readonly ILogger<CelarAiCapabilityRoutingStore> _logger;

    public CelarAiCapabilityRoutingStore(ILogger<CelarAiCapabilityRoutingStore> logger)
    {
        _logger = logger;
        _connectionString = ConnectionString();
        _encryptionKey = EncryptionKey();
    }

    public bool DatabaseAvailable => !string.IsNullOrWhiteSpace(_connectionString);
    public bool SecretEncryptionAvailable => _encryptionKey is { Length: 32 };
    public string EnvironmentCode => Clean(Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT"), 80, "unspecified");

    public async Task<IReadOnlyList<CelarAiCapabilityRouteSnapshot>> LoadRoutesAsync(
        CancellationToken cancellationToken = default)
    {
        var stored = new Dictionary<string, StoredRoute>(StringComparer.OrdinalIgnoreCase);
        if (DatabaseAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await EnsureSchemaAsync(connection, cancellationToken);
                const string sql = """
                    SELECT feature_code, route_targets::text, external_context_policy,
                           revision, updated_at, updated_by
                    FROM ai_capability_routes;
                    """;
                await using var command = new NpgsqlCommand(sql, connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var feature = reader.GetString(0);
                    var targets = JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? [];
                    stored[feature] = new StoredRoute(
                        feature,
                        targets,
                        reader.GetString(2),
                        reader.GetInt32(3),
                        new DateTimeOffset(reader.GetDateTime(4).ToUniversalTime()),
                        reader.IsDBNull(5) ? null : reader.GetGuid(5));
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Module 064 could not load capability routes; defaults remain active.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        return CelarAiCapabilityCatalog.Definitions.Values
            .OrderBy(definition => definition.DisplayName)
            .Select(definition => stored.TryGetValue(definition.FeatureCode, out var route)
                ? new CelarAiCapabilityRouteSnapshot(
                    definition.FeatureCode,
                    definition.DisplayName,
                    definition.ConsumerModules,
                    route.ExternalContextPolicy,
                    definition.ContextClassification,
                    SafeTargets(route.Targets),
                    route.Revision,
                    route.UpdatedAt,
                    route.UpdatedBy,
                    true)
                : new CelarAiCapabilityRouteSnapshot(
                    definition.FeatureCode,
                    definition.DisplayName,
                    definition.ConsumerModules,
                    definition.ExternalContextPolicy,
                    definition.ContextClassification,
                    CelarAiCapabilityTargets.DefaultOrder,
                    0,
                    now,
                    null,
                    false))
            .ToArray();
    }

    public async Task<CelarAiCapabilityRouteSnapshot> LoadRouteAsync(
        string feature,
        CancellationToken cancellationToken = default)
    {
        var normalized = CelarAiCapabilityCatalog.NormalizeFeature(feature);
        return (await LoadRoutesAsync(cancellationToken))
            .FirstOrDefault(route => string.Equals(route.FeatureCode, normalized, StringComparison.OrdinalIgnoreCase))
            ?? (await LoadRoutesAsync(cancellationToken)).First(route => route.FeatureCode == CelarAiCapabilityCatalog.HelpAssistant);
    }

    public async Task<CelarAiCapabilityRouteSnapshot> SaveRouteAsync(
        string feature,
        IReadOnlyList<string> targets,
        int? expectedRevision,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseAvailable) throw new InvalidOperationException("Database configuration is unavailable.");
        var definition = CelarAiCapabilityCatalog.Resolve(feature);
        if (!string.Equals(definition.FeatureCode, CelarAiCapabilityCatalog.NormalizeFeature(feature), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The requested capability is not registered.");
        var validated = CelarAiCapabilityCatalog.ValidateTargets(targets);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await ReadRouteForUpdateAsync(connection, transaction, definition.FeatureCode, cancellationToken);
        var currentRevision = current?.Revision ?? 0;
        if (expectedRevision.HasValue && expectedRevision.Value != currentRevision)
            throw new CelarAiConfigurationConflictException("The capability route changed after it was loaded. Refresh and try again.");
        var nextRevision = currentRevision + 1;
        var now = DateTimeOffset.UtcNow;
        var targetsJson = JsonSerializer.Serialize(validated);

        const string upsert = """
            INSERT INTO ai_capability_routes
                (feature_code, route_targets, external_context_policy, revision, updated_at, updated_by)
            VALUES
                (@feature, @targets::jsonb, @policy, @revision, @updated_at, @updated_by)
            ON CONFLICT (feature_code) DO UPDATE SET
                route_targets = EXCLUDED.route_targets,
                external_context_policy = EXCLUDED.external_context_policy,
                revision = EXCLUDED.revision,
                updated_at = EXCLUDED.updated_at,
                updated_by = EXCLUDED.updated_by;
            """;
        await using (var command = new NpgsqlCommand(upsert, connection, transaction))
        {
            command.Parameters.AddWithValue("feature", definition.FeatureCode);
            command.Parameters.AddWithValue("targets", NpgsqlDbType.Jsonb, targetsJson);
            command.Parameters.AddWithValue("policy", definition.ExternalContextPolicy);
            command.Parameters.AddWithValue("revision", nextRevision);
            command.Parameters.AddWithValue("updated_at", now);
            command.Parameters.AddWithValue("updated_by", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string audit = """
            INSERT INTO ai_capability_route_audit
                (feature_code, previous_targets, new_targets, previous_external_context_policy,
                 new_external_context_policy, actor_user_id)
            VALUES
                (@feature, @previous::jsonb, @next::jsonb, @previous_policy, @next_policy, @actor);
            """;
        await using (var command = new NpgsqlCommand(audit, connection, transaction))
        {
            command.Parameters.AddWithValue("feature", definition.FeatureCode);
            command.Parameters.AddWithValue("previous", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(current?.Targets ?? CelarAiCapabilityTargets.DefaultOrder));
            command.Parameters.AddWithValue("next", NpgsqlDbType.Jsonb, targetsJson);
            command.Parameters.AddWithValue("previous_policy", (object?)current?.ExternalContextPolicy ?? DBNull.Value);
            command.Parameters.AddWithValue("next_policy", definition.ExternalContextPolicy);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return new CelarAiCapabilityRouteSnapshot(
            definition.FeatureCode,
            definition.DisplayName,
            definition.ConsumerModules,
            definition.ExternalContextPolicy,
            definition.ContextClassification,
            validated,
            nextRevision,
            now,
            actorUserId,
            true);
    }

    public Task<CelarAiCapabilityRouteSnapshot> ResetRouteAsync(
        string feature,
        int? expectedRevision,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        SaveRouteAsync(feature, CelarAiCapabilityTargets.DefaultOrder, expectedRevision, actorUserId, cancellationToken);

    public async Task<CelarAiPrivateModelProfile> LoadPrivateModelProfileAsync(
        CancellationToken cancellationToken = default)
    {
        if (DatabaseAvailable && SecretEncryptionAvailable)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await EnsureSchemaAsync(connection, cancellationToken);
                const string sql = """
                    SELECT enabled, endpoint_ciphertext, endpoint_nonce, endpoint_tag,
                           endpoint_host_fingerprint, model_name, auth_mode,
                           token_ciphertext, token_nonce, token_tag, token_fingerprint,
                           private_host_allowlist::text, require_private_model_for_documents,
                           revision, updated_at, updated_by
                    FROM ai_private_model_profiles
                    WHERE environment_code = @environment;
                    """;
                await using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("environment", EnvironmentCode);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var endpoint = reader.IsDBNull(1)
                        ? string.Empty
                        : Decrypt("celar_ai_private_endpoint", (byte[])reader[1], (byte[])reader[2], (byte[])reader[3]);
                    var token = reader.IsDBNull(7)
                        ? string.Empty
                        : Decrypt("celar_ai_private_token", (byte[])reader[7], (byte[])reader[8], (byte[])reader[9]);
                    var allowlist = JsonSerializer.Deserialize<string[]>(reader.GetString(11)) ?? [];
                    return new CelarAiPrivateModelProfile(
                        EnvironmentCode,
                        reader.GetBoolean(0),
                        endpoint,
                        reader.GetString(5),
                        token,
                        allowlist,
                        reader.GetBoolean(12),
                        reader.GetInt32(13),
                        new DateTimeOffset(reader.GetDateTime(14).ToUniversalTime()),
                        reader.IsDBNull(15) ? null : reader.GetGuid(15),
                        reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                        true);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Module 064 could not load the private Celar AI profile; environment fallback remains active.");
            }
        }

        return EnvironmentProfile();
    }

    public async Task<CelarAiPrivateModelProfile> SavePrivateModelSettingsAsync(
        CelarAiPrivateModelSettingsRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseAvailable) throw new InvalidOperationException("Database configuration is unavailable.");
        if (!SecretEncryptionAvailable)
            throw new InvalidOperationException("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY must be a base64-encoded 32-byte key.");
        var current = await LoadPrivateModelProfileAsync(cancellationToken);
        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != current.Revision)
            throw new CelarAiConfigurationConflictException("The private model profile changed after it was loaded. Refresh and try again.");

        var enabled = request.Enabled ?? current.Enabled;
        var endpoint = Clean(request.Endpoint, 1000, current.Endpoint);
        var model = Clean(request.Model, 240, current.Model);
        var allowlist = NormalizeAllowlist(request.PrivateHostAllowlist, current.PrivateHostAllowlist);
        var requirePrivate = request.RequirePrivateModelForDocuments ?? current.RequirePrivateModelForDocuments;
        if (enabled && (endpoint.Length == 0 || model.Length == 0))
            throw new ArgumentException("A private endpoint and model are required before enabling Celar AI private inference.");
        if (endpoint.Length > 0
            && !PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(endpoint, allowlist, out _, out var reason))
            throw new ArgumentException($"The private endpoint was rejected by policy ({reason}).");

        var next = current with
        {
            Enabled = enabled,
            Endpoint = endpoint,
            Model = model,
            PrivateHostAllowlist = allowlist,
            RequirePrivateModelForDocuments = requirePrivate,
            Revision = current.Revision + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actorUserId,
            EndpointHostFingerprint = HostFingerprint(endpoint),
            Persisted = true
        };
        await PersistPrivateProfileAsync(next, "settings_changed", actorUserId, cancellationToken);
        CelarAiPrivateModelRuntime.Apply(next);
        return next;
    }

    public async Task<CelarAiPrivateModelProfile> SavePrivateModelSecretAsync(
        CelarAiPrivateModelSecretRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseAvailable) throw new InvalidOperationException("Database configuration is unavailable.");
        if (!SecretEncryptionAvailable)
            throw new InvalidOperationException("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY must be a base64-encoded 32-byte key.");
        var token = request.BearerToken?.Trim() ?? string.Empty;
        if (token.Length is < 1 or > 8192 || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("The private bearer token is required, cannot contain whitespace, and must be 8192 characters or fewer.");
        var current = await LoadPrivateModelProfileAsync(cancellationToken);
        if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != current.Revision)
            throw new CelarAiConfigurationConflictException("The private model profile changed after it was loaded. Refresh and try again.");
        var next = current with
        {
            BearerToken = token,
            TokenFingerprint = Fingerprint(token),
            Revision = current.Revision + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = actorUserId,
            Persisted = true
        };
        await PersistPrivateProfileAsync(next, "secret_replaced", actorUserId, cancellationToken);
        CelarAiPrivateModelRuntime.Apply(next);
        return next;
    }

    private async Task PersistPrivateProfileAsync(
        CelarAiPrivateModelProfile profile,
        string action,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var endpoint = Encrypt("celar_ai_private_endpoint", profile.Endpoint);
        var token = Encrypt("celar_ai_private_token", profile.BearerToken);
        const string upsert = """
            INSERT INTO ai_private_model_profiles
                (environment_code, enabled, endpoint_ciphertext, endpoint_nonce, endpoint_tag,
                 endpoint_host_fingerprint, model_name, auth_mode, token_ciphertext, token_nonce,
                 token_tag, token_fingerprint, private_host_allowlist,
                 require_private_model_for_documents, revision, updated_at, updated_by)
            VALUES
                (@environment, @enabled, @endpoint_ciphertext, @endpoint_nonce, @endpoint_tag,
                 @endpoint_fingerprint, @model, 'bearer', @token_ciphertext, @token_nonce,
                 @token_tag, @token_fingerprint, @allowlist::jsonb,
                 @require_private, @revision, @updated_at, @updated_by)
            ON CONFLICT (environment_code) DO UPDATE SET
                enabled = EXCLUDED.enabled,
                endpoint_ciphertext = EXCLUDED.endpoint_ciphertext,
                endpoint_nonce = EXCLUDED.endpoint_nonce,
                endpoint_tag = EXCLUDED.endpoint_tag,
                endpoint_host_fingerprint = EXCLUDED.endpoint_host_fingerprint,
                model_name = EXCLUDED.model_name,
                auth_mode = EXCLUDED.auth_mode,
                token_ciphertext = EXCLUDED.token_ciphertext,
                token_nonce = EXCLUDED.token_nonce,
                token_tag = EXCLUDED.token_tag,
                token_fingerprint = EXCLUDED.token_fingerprint,
                private_host_allowlist = EXCLUDED.private_host_allowlist,
                require_private_model_for_documents = EXCLUDED.require_private_model_for_documents,
                revision = EXCLUDED.revision,
                updated_at = EXCLUDED.updated_at,
                updated_by = EXCLUDED.updated_by;
            """;
        await using (var command = new NpgsqlCommand(upsert, connection, transaction))
        {
            command.Parameters.AddWithValue("environment", profile.EnvironmentCode);
            command.Parameters.AddWithValue("enabled", profile.Enabled);
            AddBytes(command, "endpoint_ciphertext", endpoint.Ciphertext);
            AddBytes(command, "endpoint_nonce", endpoint.Nonce);
            AddBytes(command, "endpoint_tag", endpoint.Tag);
            command.Parameters.AddWithValue("endpoint_fingerprint", (object?)profile.EndpointHostFingerprint ?? DBNull.Value);
            command.Parameters.AddWithValue("model", profile.Model);
            AddBytes(command, "token_ciphertext", token.Ciphertext);
            AddBytes(command, "token_nonce", token.Nonce);
            AddBytes(command, "token_tag", token.Tag);
            command.Parameters.AddWithValue("token_fingerprint", (object?)profile.TokenFingerprint ?? DBNull.Value);
            command.Parameters.AddWithValue("allowlist", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(profile.PrivateHostAllowlist));
            command.Parameters.AddWithValue("require_private", profile.RequirePrivateModelForDocuments);
            command.Parameters.AddWithValue("revision", profile.Revision);
            command.Parameters.AddWithValue("updated_at", profile.UpdatedAt);
            command.Parameters.AddWithValue("updated_by", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string audit = """
            INSERT INTO ai_private_model_profile_audit
                (environment_code, action, revision, actor_user_id)
            VALUES (@environment, @action, @revision, @actor);
            """;
        await using (var command = new NpgsqlCommand(audit, connection, transaction))
        {
            command.Parameters.AddWithValue("environment", profile.EnvironmentCode);
            command.Parameters.AddWithValue("action", action);
            command.Parameters.AddWithValue("revision", profile.Revision);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<StoredRoute?> ReadRouteForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string feature,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT feature_code, route_targets::text, external_context_policy,
                   revision, updated_at, updated_by
            FROM ai_capability_routes
            WHERE feature_code = @feature
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("feature", feature);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new StoredRoute(
            reader.GetString(0),
            JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? [],
            reader.GetString(2),
            reader.GetInt32(3),
            new DateTimeOffset(reader.GetDateTime(4).ToUniversalTime()),
            reader.IsDBNull(5) ? null : reader.GetGuid(5));
    }

    private async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ai_capability_routes (
                feature_code TEXT PRIMARY KEY,
                route_targets JSONB NOT NULL,
                external_context_policy TEXT NOT NULL,
                revision INTEGER NOT NULL DEFAULT 1,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by UUID NULL
            );
            CREATE TABLE IF NOT EXISTS ai_capability_route_audit (
                audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                feature_code TEXT NOT NULL,
                previous_targets JSONB NULL,
                new_targets JSONB NOT NULL,
                previous_external_context_policy TEXT NULL,
                new_external_context_policy TEXT NOT NULL,
                actor_user_id UUID NULL,
                occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS ai_private_model_profiles (
                environment_code TEXT PRIMARY KEY,
                enabled BOOLEAN NOT NULL DEFAULT FALSE,
                endpoint_ciphertext BYTEA NULL,
                endpoint_nonce BYTEA NULL,
                endpoint_tag BYTEA NULL,
                endpoint_host_fingerprint TEXT NULL,
                model_name TEXT NOT NULL DEFAULT '',
                auth_mode TEXT NOT NULL DEFAULT 'bearer',
                token_ciphertext BYTEA NULL,
                token_nonce BYTEA NULL,
                token_tag BYTEA NULL,
                token_fingerprint TEXT NULL,
                private_host_allowlist JSONB NOT NULL DEFAULT '[]'::jsonb,
                require_private_model_for_documents BOOLEAN NOT NULL DEFAULT TRUE,
                revision INTEGER NOT NULL DEFAULT 1,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by UUID NULL
            );
            CREATE TABLE IF NOT EXISTS ai_private_model_profile_audit (
                audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                environment_code TEXT NOT NULL,
                action TEXT NOT NULL,
                revision INTEGER NOT NULL,
                actor_user_id UUID NULL,
                occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private CelarAiPrivateModelProfile EnvironmentProfile()
    {
        var endpoint = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT")?.Trim() ?? string.Empty;
        var model = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_MODEL")?.Trim() ?? string.Empty;
        var token = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")?.Trim() ?? string.Empty;
        var allowlist = NormalizeAllowlist(
            (Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST") ?? string.Empty)
                .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            PulseAiPrivateRuntimePolicy.PrivateHostSuffixDefaults);
        var enabled = bool.TryParse(Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED"), out var value) && value;
        return new CelarAiPrivateModelProfile(
            EnvironmentCode,
            enabled,
            endpoint,
            model,
            token,
            allowlist,
            true,
            0,
            DateTimeOffset.UtcNow,
            null,
            HostFingerprint(endpoint),
            Fingerprint(token),
            false);
    }

    private EncryptedValue Encrypt(string purpose, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return EncryptedValue.Empty;
        var plaintext = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(_encryptionKey!, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(purpose));
            return new EncryptedValue(ciphertext, nonce, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private string Decrypt(string purpose, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_encryptionKey!, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void AddBytes(NpgsqlCommand command, string name, byte[]? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Bytea);
        parameter.Value = value is { Length: > 0 } ? value : DBNull.Value;
    }

    private static IReadOnlyList<string> SafeTargets(IReadOnlyList<string> values)
    {
        try { return CelarAiCapabilityCatalog.ValidateTargets(values); }
        catch { return CelarAiCapabilityTargets.DefaultOrder; }
    }

    private static IReadOnlyList<string> NormalizeAllowlist(
        IEnumerable<string>? values,
        IReadOnlyList<string> fallback)
    {
        var result = (values ?? [])
            .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        return result.Length > 0 ? result : fallback;
    }

    private static string HostFingerprint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return string.Empty;
        return Fingerprint(uri.DnsSafeHost.ToLowerInvariant());
    }

    private static string Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
    }

    private static string Clean(string? value, int maximum, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string? ConnectionString() => new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION",
            "PROJECTPULSE_DB_CONNECTION",
            "PROJECTTIME_DB_CONNECTION"
        }
        .Select(Environment.GetEnvironmentVariable)
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static byte[]? EncryptionKey()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY");
            if (string.IsNullOrWhiteSpace(value)) return null;
            var key = Convert.FromBase64String(value.Trim());
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record StoredRoute(
        string Feature,
        IReadOnlyList<string> Targets,
        string ExternalContextPolicy,
        int Revision,
        DateTimeOffset UpdatedAt,
        Guid? UpdatedBy);

    private sealed record EncryptedValue(byte[]? Ciphertext, byte[]? Nonce, byte[]? Tag)
    {
        public static EncryptedValue Empty { get; } = new(null, null, null);
    }
}

public static class CelarAiPrivateModelRuntime
{
    private static readonly object Sync = new();
    private static CelarAiPrivateModelProfile? _profile;

    public static void Apply(CelarAiPrivateModelProfile profile)
    {
        lock (Sync) _profile = profile;
    }

    public static CelarAiPrivateModelProfile? Snapshot()
    {
        lock (Sync) return _profile;
    }

    public static PulseAiPrivateRagOptions Apply(PulseAiPrivateRagOptions options)
    {
        var profile = Snapshot();
        if (profile is null || !profile.Persisted) return options;
        return options with
        {
            Enabled = profile.Enabled,
            InferenceEndpoint = profile.Endpoint,
            InferenceModel = profile.Model,
            InferenceBearerToken = profile.BearerToken,
            RequirePrivateModelForDocumentAnswers = profile.RequirePrivateModelForDocuments,
            PrivateHostAllowlist = profile.PrivateHostAllowlist
        };
    }
}

public sealed class CelarAiCapabilityRoutingLoader(
    CelarAiCapabilityRoutingStore store,
    ILogger<CelarAiCapabilityRoutingLoader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await LoadAsync(stoppingToken);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            CelarAiPrivateModelRuntime.Apply(await store.LoadPrivateModelProfileAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 064 could not refresh the private Celar AI runtime profile.");
        }
    }
}

public sealed class CelarAiPrivateGenerationTarget
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CelarAiPrivateGenerationTarget> _logger;

    public CelarAiPrivateGenerationTarget(
        IHttpClientFactory httpClientFactory,
        ILogger<CelarAiPrivateGenerationTarget> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ProjectPulseAiProviderResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        CelarAiPrivateModelProfile profile,
        CancellationToken cancellationToken)
    {
        if (!profile.Enabled)
            return Unavailable("celar_ai_private_model_disabled");
        if (!profile.Configured)
            return Unavailable("celar_ai_private_model_not_configured");
        if (!PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                profile.Endpoint,
                profile.PrivateHostAllowlist,
                out var endpoint,
                out var reason)
            || endpoint is null)
        {
            return Unavailable($"celar_ai_private_endpoint_{reason}");
        }

        var payload = new
        {
            model = profile.Model,
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            temperature = request.Temperature,
            max_tokens = request.MaxOutputTokens
        };
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(profile.BearerToken))
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.BearerToken);
            message.Headers.Add("X-Celar-AI-Private-Boundary", "true");
            message.Headers.Add("X-Celar-AI-Feature", request.Feature);
            var client = _httpClientFactory.CreateClient("PulseAiPrivateInference");
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                ? values.FirstOrDefault()
                : null;
            if (!response.IsSuccessStatusCode)
                return new ProjectPulseAiProviderResult(
                    CelarAiCapabilityTargets.CelarAi,
                    ProjectPulseAiOutcomes.Unavailable,
                    null,
                    $"celar_ai_private_http_{(int)response.StatusCode}",
                    "The private Celar AI model is unavailable.",
                    requestId,
                    null,
                    (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = ReadContent(json.RootElement).Trim();
            if (content.Length == 0)
                return Unavailable("celar_ai_private_empty_response", requestId, (int)response.StatusCode);
            return new ProjectPulseAiProviderResult(
                CelarAiCapabilityTargets.CelarAi,
                ProjectPulseAiOutcomes.Success,
                content,
                null,
                null,
                requestId,
                null,
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI private generation failed without logging prompt, endpoint, token, or source content. Feature={Feature}",
                request.Feature);
            return Unavailable("celar_ai_private_transport_failure");
        }
    }

    public async Task<ProjectPulseAiProbeResult> ProbeAsync(
        CelarAiPrivateModelProfile profile,
        CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(
            new ProjectPulseAiGenerationRequest(
                CelarAiCapabilityCatalog.HelpAssistant,
                "Return only the requested word.",
                "Return OK.",
                16,
                0),
            profile,
            cancellationToken);
        return new ProjectPulseAiProbeResult(
            CelarAiCapabilityTargets.CelarAi,
            result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content),
            result.IsSuccess ? "generation_verified" : result.Code ?? "generation_probe_failed",
            result.IsSuccess ? "Celar AI private generation is verified." : "Celar AI private generation is unavailable.",
            result.HttpStatusCode,
            result.RequestId);
    }

    private static string ReadContent(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
                return content.ValueKind == JsonValueKind.String ? content.GetString() ?? string.Empty : content.GetRawText();
            if (choice.TryGetProperty("text", out var text)) return text.GetString() ?? string.Empty;
        }
        if (root.TryGetProperty("output_text", out var outputText)) return outputText.GetString() ?? string.Empty;
        if (root.TryGetProperty("content", out var directContent))
            return directContent.ValueKind == JsonValueKind.String ? directContent.GetString() ?? string.Empty : directContent.GetRawText();
        return string.Empty;
    }

    private static ProjectPulseAiProviderResult Unavailable(
        string code,
        string? requestId = null,
        int? status = null) => new(
            CelarAiCapabilityTargets.CelarAi,
            ProjectPulseAiOutcomes.Unavailable,
            null,
            code,
            "The private Celar AI model is unavailable.",
            requestId,
            null,
            status);
}

public sealed record CelarAiConsumerAssuranceSnapshot(
    string Feature,
    string Module,
    string EntryPoint,
    bool CentralRouterConnected,
    bool PrivateContextCompliant,
    bool DirectProviderFree,
    DateTimeOffset? LastExercisedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string LastTarget,
    string LastOutcome,
    string LastCorrelationId);

public sealed class CelarAiConsumerAssuranceRegistry
{
    private readonly ConcurrentDictionary<string, RuntimeState> _runtime = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Feature, string Module, string EntryPoint)[] Definitions =
    [
        (CelarAiCapabilityCatalog.TimesheetNonProject, "001", "ProjectPulseAiTimeEntrySuggestionService"),
        (CelarAiCapabilityCatalog.TimesheetProjectTask, "001/019", "ProjectPulseAiTimeEntrySuggestionService"),
        (CelarAiCapabilityCatalog.TimesheetServiceRequest, "001/019", "ProjectPulseAiTimeEntrySuggestionService"),
        (CelarAiCapabilityCatalog.SowGsdPlanning, "011/025", "CelarAiEnterprisePlatformService"),
        (CelarAiCapabilityCatalog.ProjectFlowHivePlan, "011/066", "CelarAiEnterprisePlatformService"),
        (CelarAiCapabilityCatalog.CloseoutCommunication, "011/040/055C", "CelarAiCapabilityRoutingModule"),
        (CelarAiCapabilityCatalog.HelpAssistant, "011/999", "CelarAiBrandModule")
    ];

    public void Record(string feature, string target, string outcome, string correlationId)
    {
        var now = DateTimeOffset.UtcNow;
        _runtime.AddOrUpdate(
            feature,
            _ => new RuntimeState(now, outcome == ProjectPulseAiOutcomes.Success ? now : null,
                outcome == ProjectPulseAiOutcomes.Success ? null : now, target, outcome, correlationId),
            (_, state) => state with
            {
                LastExercisedAt = now,
                LastSuccessAt = outcome == ProjectPulseAiOutcomes.Success ? now : state.LastSuccessAt,
                LastFailureAt = outcome == ProjectPulseAiOutcomes.Success ? state.LastFailureAt : now,
                LastTarget = target,
                LastOutcome = outcome,
                LastCorrelationId = correlationId
            });
    }

    public IReadOnlyList<CelarAiConsumerAssuranceSnapshot> Snapshots() => Definitions
        .Select(definition =>
        {
            _runtime.TryGetValue(definition.Feature, out var state);
            return new CelarAiConsumerAssuranceSnapshot(
                definition.Feature,
                definition.Module,
                definition.EntryPoint,
                CentralRouterConnected: true,
                PrivateContextCompliant: true,
                DirectProviderFree: true,
                state?.LastExercisedAt,
                state?.LastSuccessAt,
                state?.LastFailureAt,
                state?.LastTarget ?? "not_exercised",
                state?.LastOutcome ?? "not_exercised",
                state?.LastCorrelationId ?? string.Empty);
        })
        .ToArray();

    private sealed record RuntimeState(
        DateTimeOffset LastExercisedAt,
        DateTimeOffset? LastSuccessAt,
        DateTimeOffset? LastFailureAt,
        string LastTarget,
        string LastOutcome,
        string LastCorrelationId);
}

public sealed class CelarAiCapabilityRouter
{
    private readonly CelarAiCapabilityRoutingStore _store;
    private readonly CelarAiPrivateGenerationTarget _privateTarget;
    private readonly ProjectPulseAiConfiguration _configuration;
    private readonly ProjectPulseAiHealthRegistry _health;
    private readonly PulseAiEscalationSanitizer _sanitizer;
    private readonly IReadOnlyDictionary<string, IProjectPulseAiProvider> _providers;
    private readonly CelarAiConsumerAssuranceRegistry _assurance;
    private readonly ILogger<CelarAiCapabilityRouter> _logger;

    public CelarAiCapabilityRouter(
        CelarAiCapabilityRoutingStore store,
        CelarAiPrivateGenerationTarget privateTarget,
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthRegistry health,
        PulseAiEscalationSanitizer sanitizer,
        IEnumerable<IProjectPulseAiProvider> providers,
        CelarAiConsumerAssuranceRegistry assurance,
        ILogger<CelarAiCapabilityRouter> logger)
    {
        _store = store;
        _privateTarget = privateTarget;
        _configuration = configuration;
        _health = health;
        _sanitizer = sanitizer;
        _providers = providers.ToDictionary(provider => provider.Code, StringComparer.OrdinalIgnoreCase);
        _assurance = assurance;
        _logger = logger;
    }

    public Task<ProjectPulseAiRouteResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        CelarAiCapabilityExecutionContext execution,
        Func<string> localFallback,
        CancellationToken cancellationToken = default) =>
        GenerateInternalAsync(request, execution, localFallback, skipPrivateTarget: false, cancellationToken);

    public Task<ProjectPulseAiRouteResult> GenerateExternalAsync(
        ProjectPulseAiGenerationRequest request,
        CelarAiCapabilityExecutionContext execution,
        Func<string> localFallback,
        CancellationToken cancellationToken = default) =>
        GenerateInternalAsync(request, execution, localFallback, skipPrivateTarget: true, cancellationToken);

    private async Task<ProjectPulseAiRouteResult> GenerateInternalAsync(
        ProjectPulseAiGenerationRequest request,
        CelarAiCapabilityExecutionContext execution,
        Func<string> localFallback,
        bool skipPrivateTarget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localFallback);
        var feature = CelarAiCapabilityCatalog.NormalizeFeature(request.Feature);
        var route = await _store.LoadRouteAsync(feature, cancellationToken);
        var attempted = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var target in route.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (skipPrivateTarget && target == CelarAiCapabilityTargets.CelarAi)
            {
                skipped.Add(target);
                continue;
            }
            if (target == CelarAiCapabilityTargets.Local)
            {
                var content = localFallback();
                _assurance.Record(feature, target, ProjectPulseAiOutcomes.Success, execution.CorrelationId);
                return new ProjectPulseAiRouteResult(
                    content,
                    target,
                    ProjectPulseAiOutcomes.Success,
                    failed.Count > 0 || skipped.Count > 0
                        ? "Higher-priority targets were unavailable or not eligible. The governed local template was used."
                        : null,
                    attempted,
                    skipped,
                    null,
                    null);
            }

            if (target == CelarAiCapabilityTargets.CelarAi)
            {
                attempted.Add(target);
                var profile = await _store.LoadPrivateModelProfileAsync(cancellationToken);
                var result = await _privateTarget.GenerateAsync(request with { Feature = feature }, profile, cancellationToken);
                if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content))
                {
                    _assurance.Record(feature, target, result.Outcome, execution.CorrelationId);
                    return new ProjectPulseAiRouteResult(
                        result.Content,
                        target,
                        result.Outcome,
                        failed.Count > 0 || skipped.Count > 0 ? "Celar AI completed after another target was skipped." : null,
                        attempted,
                        skipped,
                        result.Usage,
                        result.RequestId);
                }
                failed.Add(target);
                continue;
            }

            if (!_providers.TryGetValue(target, out var provider))
            {
                skipped.Add(target);
                continue;
            }
            var externalRequest = PrepareExternalRequest(request, execution);
            if (externalRequest is null)
            {
                skipped.Add(target);
                continue;
            }
            _health.ApplyConfiguration(_configuration.Provider(target));
            if (!_health.CanAttempt(target, out _))
            {
                skipped.Add(target);
                continue;
            }

            attempted.Add(target);
            ProjectPulseAiProviderResult result;
            try
            {
                result = await provider.GenerateAsync(externalRequest, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Module 064 target {Target} failed without exposing prompt or secret content.", target);
                _health.RecordFailure(target, "provider_unhandled_failure", null);
                failed.Add(target);
                continue;
            }

            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content))
            {
                _health.RecordSuccess(target, result.Usage, result.RequestId, rateLimits: result.RateLimits);
                _assurance.Record(feature, target, result.Outcome, execution.CorrelationId);
                return new ProjectPulseAiRouteResult(
                    result.Content,
                    target,
                    result.Outcome,
                    failed.Count > 0 || skipped.Count > 0
                        ? $"{DisplayName(target)} completed after a higher-priority target was unavailable or ineligible."
                        : null,
                    attempted,
                    skipped,
                    result.Usage,
                    result.RequestId);
            }
            if (result.IsRefusal)
            {
                _health.RecordRefusal(target, result.Usage, result.RequestId, result.RateLimits);
                _assurance.Record(feature, target, result.Outcome, execution.CorrelationId);
                return new ProjectPulseAiRouteResult(
                    string.Empty,
                    target,
                    ProjectPulseAiOutcomes.Refusal,
                    $"{DisplayName(target)} declined this request under its safety controls. No later target was attempted.",
                    attempted,
                    skipped,
                    result.Usage,
                    result.RequestId);
            }
            _health.RecordFailure(target, result.Code ?? "provider_unavailable", result.RequestId);
            failed.Add(target);
        }

        var fallback = localFallback();
        _assurance.Record(feature, CelarAiCapabilityTargets.Local, ProjectPulseAiOutcomes.Success, execution.CorrelationId);
        return new ProjectPulseAiRouteResult(
            fallback,
            CelarAiCapabilityTargets.Local,
            ProjectPulseAiOutcomes.Success,
            "No configured or eligible AI target was available. The governed local template was used.",
            attempted,
            skipped,
            null,
            null);
    }

    private ProjectPulseAiGenerationRequest? PrepareExternalRequest(
        ProjectPulseAiGenerationRequest request,
        CelarAiCapabilityExecutionContext execution)
    {
        var restricted = execution.ContainsPrivateDocuments
            || execution.ContainsCustomerIdentity
            || execution.ContainsPeopleRecords
            || execution.ContainsFinancialValues;
        if (restricted && !execution.AllowSanitizedExternalAssistance) return null;
        var sanitized = _sanitizer.SanitizeForExecution(new PulseAiSanitizationRequest(
            Purpose: $"module064_{execution.Feature}",
            Content: request.UserPrompt,
            Classification: restricted ? "restricted_generic" : "internal_generic",
            SensitiveTerms: execution.SensitiveTerms.ToArray(),
            AcknowledgePreviewOnly: true));
        if (!sanitized.ExternalExecutionAuthorized) return null;
        return request with
        {
            Feature = CelarAiCapabilityCatalog.NormalizeFeature(request.Feature),
            SystemPrompt = $"""
                You are an optional generic reasoning target used by Celar AI through Module 064.
                The request has been sanitized. Do not request or invent customer names, project identifiers,
                employee identities, internal documents, financial values, credentials, hostnames, IP addresses,
                or proprietary system facts. Do not claim that work was completed, approved, sent, published,
                baselined, assigned, committed, or deployed. Return only the requested reviewable content.
                """,
            UserPrompt = sanitized.SanitizedCapsule
        };
    }

    private static string DisplayName(string target) => target switch
    {
        CelarAiCapabilityTargets.CelarAi => "Celar AI",
        CelarAiCapabilityTargets.Claude => "Claude",
        CelarAiCapabilityTargets.OpenAi => "OpenAI",
        _ => "Governed local template"
    };
}
