using System.Diagnostics;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class CelarAiCapabilityRoutingModule
{
    private static readonly HashSet<string> AdditionalModuleAdministratorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATOR"
    };

    public static IEndpointRouteBuilder MapCelarAiCapabilityRoutingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/ai-configuration/routes",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)GetRoutesAsync);
        endpoints.MapPut(
            "/api/ai-configuration/routes/{featureCode}",
            (Func<string, CelarAiRouteUpdateRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)SaveRouteAsync);
        endpoints.MapPost(
            "/api/ai-configuration/routes/{featureCode}/reset",
            (Func<string, CelarAiRouteUpdateRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)ResetRouteAsync);
        endpoints.MapGet(
            "/api/ai-configuration/consumers",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CelarAiConsumerAssuranceRegistry, CancellationToken, Task<IResult>>)GetConsumersAsync);
        endpoints.MapGet(
            "/api/ai-configuration/knowledge-fabric",
            (Func<HttpContext, CelarAiKnowledgeFabricService, CancellationToken, Task<IResult>>)GetKnowledgeFabricAsync);

        endpoints.MapGet(
            "/api/ai-configuration/private-model",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, PulseAiPrivateDocumentRuntimeService, ProjectPulseAiConfiguration, ProjectPulseAiHealthRegistry, CancellationToken, Task<IResult>>)GetPrivateModelAsync);
        endpoints.MapPut(
            "/api/ai-configuration/private-model/settings",
            (Func<CelarAiPrivateModelSettingsRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)SavePrivateModelSettingsAsync);
        endpoints.MapPut(
            "/api/ai-configuration/private-model/secret",
            (Func<CelarAiPrivateModelSecretRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)SavePrivateModelSecretAsync);
        endpoints.MapPost(
            "/api/ai-configuration/private-model/test",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CelarAiPrivateGenerationTarget, ProjectPulseAiHealthRegistry, CancellationToken, Task<IResult>>)TestPrivateModelAsync);
        endpoints.MapPost(
            "/api/ai-configuration/sanitized-external-fallback/production-test",
            (Func<HttpContext, CelarAiCapabilityRouter, CancellationToken, Task<IResult>>)TestSanitizedExternalFallbackAsync);
        endpoints.MapPost(
            ProjectPulseAiCandidateRequestFence.VerificationPath,
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CelarAiPrivateGenerationTarget, CelarAiCapabilityRouter, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)VerifyReleaseCandidateAsync);
        endpoints.MapPost(
            "/api/ai-configuration/encryption-key/rotate",
            (Func<ProjectPulseAiEncryptionRotationRequest, HttpContext, ProjectPulseAiEncryptionRotationService, CancellationToken, Task<IResult>>)RotateEncryptionKeyAsync);

        endpoints.MapPost(
            "/api/project-flowhive/ai/generate",
            (Func<CelarAiComposeRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)GenerateFlowHiveAsync);
        endpoints.MapPost(
            "/api/sow-gsd-planning/ai/generate",
            (Func<CelarAiComposeRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)GenerateSowAsync);
        endpoints.MapPost(
            "/api/project-closeout/ai/communication",
            (Func<CelarAiCloseoutCommunicationRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiCapabilityRouter, CancellationToken, Task<IResult>>)GenerateCloseoutCommunicationAsync);

        return endpoints;
    }

    /// <summary>
    /// Performs every release-candidate assertion in one request and one
    /// process. It does not update health evidence, routes, profiles, sessions,
    /// documents, audit rows, conversations, queues, or files.
    /// </summary>
    private static async Task<IResult> VerifyReleaseCandidateAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CelarAiPrivateGenerationTarget privateTarget,
        CelarAiCapabilityRouter router,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (!release.IsCandidate)
            return Results.Json(new
            {
                status = "release_candidate_phase_required",
                message = "The combined verification operation exists only in the candidate phase.",
                releasePhase = release.PhaseCode,
                stateChanged = false
            }, statusCode: StatusCodes.Status409Conflict);

        var authorization = await AuthorizeAdministratorAsync(
            context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;

        var database = await InspectCandidateDatabaseAsync(cancellationToken);
        var profile = await store.LoadPrivateModelProfileAsync(cancellationToken);
        var endpointPolicy = await PrivateEndpointPolicyAsync(profile, cancellationToken);
        var routes = await store.LoadRoutesAsync(cancellationToken);
        var routesReady = routes.Count == CelarAiCapabilityCatalog.Definitions.Count
            && routes.All(route => route.DeploymentManaged
                && route.Targets.SequenceEqual(release.RouteOrder, StringComparer.OrdinalIgnoreCase));
        var runtimeReadiness = await runtime.GetReadinessAsync(cancellationToken);
        var storageReadiness = ProjectPulseUploadStorage.InspectProductionReadiness();

        var privateStopwatch = Stopwatch.StartNew();
        var privateProbe = database.ExactSowReady
            ? await privateTarget.ProbeExactAsync(
                profile,
                database.SampleChunkText,
                cancellationToken)
            : CelarAiPrivateProbeAttestation.Failed("exact_sow_not_ready");
        privateStopwatch.Stop();
        var externalProbe = await router.ProbeSanitizedExternalFallbackAsync(
            CorrelationId(context), cancellationToken, recordHealthEvidence: false);

        var roleIdentityMatches = database.ConfiguredRoleFingerprint.Length > 0
            && string.Equals(database.ConfiguredRoleFingerprint, database.ActiveRoleFingerprint, StringComparison.Ordinal);
        var ready = database.ReadOnlyIdentity
            && roleIdentityMatches
            && database.ExactSowReady
            && routesReady
            && store.SecretEncryptionAvailable
            && profile.Ready
            && endpointPolicy == "private_endpoint_dns_verified"
            && privateProbe.Ready
            && externalProbe.Ready
            && runtimeReadiness.Status == "private_document_runtime_ready";

        var blockers = new List<string>();
        if (!database.ReadOnlyIdentity)
            blockers.Add("The candidate database identity has mutation or elevated privileges.");
        if (!roleIdentityMatches)
            blockers.Add("The active database role does not match the release credential role fingerprint.");
        if (!store.SecretEncryptionAvailable)
            blockers.Add("A stable base64-encoded 32-byte PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY is required.");
        if (!database.ExactSowConfigured)
            blockers.Add("An exact release SOW document ID and source SHA-256 are required.");
        else if (!database.ExactSowReady)
            blockers.Add("The exact configured SOW is not active, authorized, canonical, and ready.");
        if (!routesReady)
            blockers.Add("All eight capabilities must use the deployment-managed release route order.");
        if (!profile.Ready || endpointPolicy != "private_endpoint_dns_verified")
            blockers.Add("The deployment-managed private Celar AI profile is not ready on private DNS.");
        if (!privateProbe.Ready)
            blockers.Add("The exact-SOW private Celar AI response or reported model did not match the release attestation contract.");
        if (!externalProbe.Ready)
            blockers.Add("The immediate identity-free Claude/OpenAI fallback probe failed.");
        blockers.AddRange(runtimeReadiness.Blockers);
        blockers.AddRange(runtimeReadiness.MissingConfiguration);

        return Results.Json(new
        {
            module = "064",
            status = ready
                ? "release_candidate_verified_read_only"
                : "release_candidate_verification_failed",
            ready,
            release = new
            {
                phase = release.PhaseCode,
                sourceCommit = release.SourceCommit,
                embeddedSourceCommit = release.EmbeddedSourceCommit,
                controlCommit = release.ControlCommit,
                expectedConfigurationSha256 = release.ExpectedConfigurationDigest,
                computedConfigurationSha256 = release.ComputedConfigurationDigest,
                configurationDigestMatches = string.Equals(
                    release.ExpectedConfigurationDigest,
                    release.ComputedConfigurationDigest,
                    StringComparison.Ordinal),
                revision = release.Revision,
                replicaId = Clean(
                    Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME")
                    ?? Environment.GetEnvironmentVariable("HOSTNAME"), 180, "unknown")
            },
            database = new
            {
                readOnlyIdentity = database.ReadOnlyIdentity,
                elevatedRole = database.ElevatedRole,
                schemaCreateAllowed = database.SchemaCreateAllowed,
                databaseCreateAllowed = database.DatabaseCreateAllowed,
                temporaryTableAllowed = database.TemporaryTableAllowed,
                defaultTransactionReadOnly = database.DefaultTransactionReadOnly,
                mutableTableCount = database.MutableTableCount,
                mutableSequenceCount = database.MutableSequenceCount,
                databaseFingerprint = database.DatabaseFingerprint,
                configuredRoleFingerprint = database.ConfiguredRoleFingerprint,
                activeRoleFingerprint = database.ActiveRoleFingerprint,
                roleIdentityMatches,
                exactSowConfigured = database.ExactSowConfigured,
                exactSowReady = database.ExactSowReady,
                exactDocumentReady = database.ExactDocumentReady,
                exactVersionReady = database.ExactVersionReady,
                exactProjectReady = database.ExactProjectReady,
                exactSourceShaReady = database.ExactSourceShaReady,
                exactIndexReady = database.ExactIndexReady,
                exactChunkSetReady = database.ExactChunkSetReady,
                exactChunkCount = database.ExactChunkCount,
                documentIdentityReturned = false
            },
            storage = new
            {
                ready = runtimeReadiness.UploadStorageProductionReady,
                verificationMode = storageReadiness.VerificationMode,
                readOnlyCanaryVerified = storageReadiness.ReadOnlyAttestationValid,
                writeDeleteProbeVerified = storageReadiness.WriteDeleteProbeVerified,
                rootFingerprint = runtimeReadiness.UploadRootFingerprint,
                pathReturned = false,
                stateChanged = false
            },
            providers = new
            {
                privateCelarAi = new
                {
                    available = privateProbe.Ready,
                    exactResponseMatched = privateProbe.ExactResponseMatched,
                    exactModelMatched = privateProbe.ExactModelMatched,
                    diagnosticCode = privateProbe.DiagnosticCode,
                    requestId = privateProbe.RequestId,
                    latencyMilliseconds = privateStopwatch.ElapsedMilliseconds,
                    responseContentReturned = false,
                    evidencePersisted = false
                },
                sanitizedExternalFallback = new
                {
                    ready = externalProbe.Ready,
                    identityFreeFixedCapsule = true,
                    responseContentReturned = false,
                    evidencePersisted = false,
                    targets = externalProbe.Targets.Select(target => new
                    {
                        provider = target.Provider,
                        available = target.Available,
                        privacyValidated = target.PrivacyValidated,
                        diagnosticCode = target.DiagnosticCode,
                        requestId = target.RequestId
                    }).ToArray()
                }
            },
            module064 = new
            {
                allCentralRoutesReady = routesReady,
                privateRuntimeReady = runtimeReadiness.Status == "private_document_runtime_ready",
                readyAuthorizedSowCount = runtimeReadiness.ReadySowDocumentCount,
                migrations052053061071Applied = runtimeReadiness.ProductionMigrationsApplied
            },
            guarantees = new
            {
                sessionLastSeenUpdated = false,
                databaseWrites = 0,
                fileWrites = 0,
                routeOrProfileWrites = 0,
                sharedProbeEvidenceWrites = 0,
                stateChanged = false
            },
            blockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            generatedAt = DateTimeOffset.UtcNow
        }, statusCode: ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<CandidateDatabaseEvidence> InspectCandidateDatabaseAsync(
        CancellationToken cancellationToken)
    {
        var documentConfigured = Guid.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_AI_RELEASE_SOW_DOCUMENT_ID"),
            out var documentId) && documentId != Guid.Empty;
        var versionConfigured = Guid.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_AI_RELEASE_SOW_VERSION_ID"),
            out var versionId) && versionId != Guid.Empty;
        var projectConfigured = Guid.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_AI_RELEASE_SOW_PROJECT_ID"),
            out var projectId) && projectId != Guid.Empty;
        var sourceSha = Environment.GetEnvironmentVariable("PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256")
            ?.Trim().ToLowerInvariant() ?? string.Empty;
        documentConfigured = documentConfigured
            && versionConfigured
            && projectConfigured
            && sourceSha.Length == 64
            && sourceSha.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
        ProjectPulseAiDatabaseConnectionEvidence resolver;
        try { resolver = ProjectPulseAiDatabaseConnection.ResolveEvidence(); }
        catch { return CandidateDatabaseEvidence.Unavailable(documentConfigured); }
        var connectionString = resolver.ConnectionString;
        if (connectionString is null)
            return CandidateDatabaseEvidence.Unavailable(documentConfigured);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            const string privilegeSql = """
                SELECT
                    current_user::text,
                    COALESCE(role.rolsuper OR role.rolcreatedb OR role.rolcreaterole
                        OR role.rolreplication OR role.rolbypassrls, TRUE),
                    EXISTS (
                        SELECT 1
                        FROM information_schema.schemata schema_info
                        WHERE schema_info.schema_name NOT IN ('pg_catalog', 'information_schema')
                          AND schema_info.schema_name NOT LIKE 'pg_toast%'
                          AND schema_info.schema_name NOT LIKE 'pg_temp_%'
                          AND has_schema_privilege(current_user, schema_info.schema_name, 'CREATE')
                    ),
                    has_database_privilege(current_user, current_database(), 'CREATE'),
                    has_database_privilege(current_user, current_database(), 'TEMPORARY'),
                    current_setting('default_transaction_read_only') = 'on',
                    (
                        SELECT COUNT(*)::int
                        FROM information_schema.tables table_info
                        WHERE table_info.table_schema NOT IN ('pg_catalog', 'information_schema')
                          AND table_info.table_schema NOT LIKE 'pg_toast%'
                          AND table_info.table_schema NOT LIKE 'pg_temp_%'
                          AND table_info.table_type = 'BASE TABLE'
                          AND (
                            has_table_privilege(current_user, table_info.table_schema || '.' || table_info.table_name, 'INSERT')
                            OR has_table_privilege(current_user, table_info.table_schema || '.' || table_info.table_name, 'UPDATE')
                            OR has_table_privilege(current_user, table_info.table_schema || '.' || table_info.table_name, 'DELETE')
                            OR has_table_privilege(current_user, table_info.table_schema || '.' || table_info.table_name, 'TRUNCATE')
                          )
                    ),
                    (
                        SELECT COUNT(*)::int
                        FROM information_schema.sequences sequence_info
                        WHERE sequence_info.sequence_schema NOT IN ('pg_catalog', 'information_schema')
                          AND sequence_info.sequence_schema NOT LIKE 'pg_toast%'
                          AND sequence_info.sequence_schema NOT LIKE 'pg_temp_%'
                          AND (
                            has_sequence_privilege(current_user, sequence_info.sequence_schema || '.' || sequence_info.sequence_name, 'USAGE')
                            OR has_sequence_privilege(current_user, sequence_info.sequence_schema || '.' || sequence_info.sequence_name, 'UPDATE')
                          )
                    )
                FROM pg_roles role
                WHERE role.rolname = current_user;
                """;
            await using var privilegeCommand = new NpgsqlCommand(privilegeSql, connection);
            await using var privilegeReader = await privilegeCommand.ExecuteReaderAsync(cancellationToken);
            if (!await privilegeReader.ReadAsync(cancellationToken))
                return CandidateDatabaseEvidence.Unavailable(documentConfigured);
            var activeRole = privilegeReader.GetString(0);
            var elevated = privilegeReader.GetBoolean(1);
            var schemaCreate = privilegeReader.GetBoolean(2);
            var databaseCreate = privilegeReader.GetBoolean(3);
            var temporaryTable = privilegeReader.GetBoolean(4);
            var defaultTransactionReadOnly = privilegeReader.GetBoolean(5);
            var mutableTableCount = privilegeReader.GetInt32(6);
            var mutableSequenceCount = privilegeReader.GetInt32(7);
            await privilegeReader.CloseAsync();

            var exactDocumentReady = false;
            var exactVersionReady = false;
            var exactProjectReady = false;
            var exactSourceShaReady = false;
            var exactIndexReady = false;
            var exactChunkSetReady = false;
            var exactChunkCount = 0;
            var sampleChunkText = string.Empty;
            var requireEmbedding = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT"));
            if (documentConfigured)
            {
                const string sowSql = """
                    SELECT
                        document.is_active
                          AND document.engineering_visible
                          AND document.ai_timesheet_context_enabled
                          AND document.pulse_ai_processing_status = 'ready'
                          AND LOWER(COALESCE(document.document_category, document.document_type, '')) IN
                              ('sow','statement_of_work','gsd','global_solution_design'),
                        document.pulse_ai_active_version_id = @version_id
                          AND version.pulse_ai_document_version_id = @version_id
                          AND version.project_intake_document_id = @document_id
                          AND version.authority_status IN ('approved','canonical'),
                        COALESCE(document.project_id = @project_id AND version.project_id = @project_id, FALSE),
                        LOWER(version.source_sha256) = @source_sha256,
                        CASE WHEN @require_embedding
                          THEN version.index_status IN ('embedding_ready','ready')
                          ELSE version.index_status IN ('lexical_ready','embedding_ready','ready')
                        END,
                        version.chunk_count,
                        COUNT(chunk.chunk_id)::int,
                        COALESCE((ARRAY_AGG(chunk.chunk_text ORDER BY chunk.chunk_index)
                            FILTER (WHERE chunk.chunk_id IS NOT NULL))[1], '')
                    FROM project_intake_documents document
                    JOIN pulse_ai_document_versions version
                      ON version.pulse_ai_document_version_id = document.pulse_ai_active_version_id
                    LEFT JOIN pulse_ai_document_chunks chunk
                      ON chunk.pulse_ai_document_version_id = version.pulse_ai_document_version_id
                     AND chunk.project_intake_document_id = document.project_intake_document_id
                     AND chunk.project_id = @project_id
                     AND LOWER(chunk.source_sha256) = @source_sha256
                     AND chunk.is_active = TRUE
                     AND CASE WHEN @require_embedding
                       THEN chunk.index_status IN ('embedding_ready','ready')
                         AND chunk.embedding_status = 'ready'
                       ELSE chunk.index_status IN ('lexical_ready','embedding_ready','ready')
                     END
                    WHERE document.project_intake_document_id = @document_id
                    GROUP BY document.project_intake_document_id, version.pulse_ai_document_version_id;
                    """;
                await using var sowCommand = new NpgsqlCommand(sowSql, connection);
                sowCommand.Parameters.AddWithValue("document_id", documentId);
                sowCommand.Parameters.AddWithValue("version_id", versionId);
                sowCommand.Parameters.AddWithValue("project_id", projectId);
                sowCommand.Parameters.AddWithValue("source_sha256", sourceSha);
                sowCommand.Parameters.AddWithValue("require_embedding", requireEmbedding);
                await using var sowReader = await sowCommand.ExecuteReaderAsync(cancellationToken);
                if (await sowReader.ReadAsync(cancellationToken))
                {
                    exactDocumentReady = sowReader.GetBoolean(0);
                    exactVersionReady = sowReader.GetBoolean(1);
                    exactProjectReady = sowReader.GetBoolean(2);
                    exactSourceShaReady = sowReader.GetBoolean(3);
                    exactIndexReady = sowReader.GetBoolean(4);
                    var declaredChunkCount = sowReader.GetInt32(5);
                    exactChunkCount = sowReader.GetInt32(6);
                    exactChunkSetReady = declaredChunkCount > 0 && exactChunkCount == declaredChunkCount;
                    sampleChunkText = sowReader.GetString(7);
                }
            }

            var exactSowReady = documentConfigured
                && exactDocumentReady
                && exactVersionReady
                && exactProjectReady
                && exactSourceShaReady
                && exactIndexReady
                && exactChunkSetReady
                && sampleChunkText.Length > 0;

            return new CandidateDatabaseEvidence(
                ReadOnlyIdentity: !elevated
                    && !schemaCreate
                    && !databaseCreate
                    && !temporaryTable
                    && defaultTransactionReadOnly
                    && mutableTableCount == 0
                    && mutableSequenceCount == 0,
                ElevatedRole: elevated,
                SchemaCreateAllowed: schemaCreate,
                DatabaseCreateAllowed: databaseCreate,
                TemporaryTableAllowed: temporaryTable,
                DefaultTransactionReadOnly: defaultTransactionReadOnly,
                MutableTableCount: mutableTableCount,
                MutableSequenceCount: mutableSequenceCount,
                ExactSowConfigured: documentConfigured,
                ExactSowReady: exactSowReady,
                ExactDocumentReady: exactDocumentReady,
                ExactVersionReady: exactVersionReady,
                ExactProjectReady: exactProjectReady,
                ExactSourceShaReady: exactSourceShaReady,
                ExactIndexReady: exactIndexReady,
                ExactChunkSetReady: exactChunkSetReady,
                ExactChunkCount: exactChunkCount,
                SampleChunkText: sampleChunkText,
                DatabaseFingerprint: resolver.DatabaseFingerprint,
                ConfiguredRoleFingerprint: resolver.ConfiguredRoleFingerprint,
                ActiveRoleFingerprint: ProjectPulseAiDatabaseConnection.FingerprintRole(activeRole));
        }
        catch
        {
            return CandidateDatabaseEvidence.Unavailable(documentConfigured);
        }
    }

    private static async Task<IResult> GetRoutesAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var routes = await store.LoadRoutesAsync(cancellationToken);
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        return Results.Ok(new
        {
            module = "064",
            status = "celar_ai_capability_routes_loaded",
            contractVersion = "celar-ai-capability-routing-v1",
            defaultOrder = CelarAiCapabilityTargets.DefaultOrder,
            availableTargets = new object[]
            {
                new { code = CelarAiCapabilityTargets.CelarAi, displayName = "Celar AI", kind = "private_orchestrator", publicProvider = false },
                new { code = CelarAiCapabilityTargets.Claude, displayName = "Claude", kind = "sanitized_external", publicProvider = true },
                new { code = CelarAiCapabilityTargets.OpenAi, displayName = "OpenAI", kind = "sanitized_external", publicProvider = true },
                new { code = CelarAiCapabilityTargets.Local, displayName = "Governed local template", kind = "deterministic_fallback", publicProvider = false }
            },
            routes = routes.Select(route => route.ToPublicResponse()).ToArray(),
            controls = new
            {
                localFallbackRequired = true,
                duplicateTargetsAllowed = false,
                safetyRefusalFailover = false,
                privacyPolicyEditable = false,
                rawPrivateContextEligibleForPublicProviders = false,
                viewAsMutationAllowed = false,
                configurationAuthority = release.ConfigurationAuthority,
                releasePhase = release.PhaseCode,
                deploymentManaged = release.IsReleaseScoped,
                readOnly = release.IsReleaseScoped,
                configurationSourceCommit = release.ConfigurationSourceCommit,
                catalogCapabilityCount = routes.Count
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> SaveRouteAsync(
        string featureCode,
        CelarAiRouteUpdateRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        if (ReleaseMutationBlocked() is { } blocked) return blocked;
        var actor = ActualSessionUserId(context)!.Value;
        try
        {
            var route = await store.SaveRouteAsync(
                featureCode,
                request.Targets ?? [],
                request.ExpectedRevision,
                actor,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_capability_route_saved",
                route = route.ToPublicResponse(),
                message = $"{route.DisplayName} now uses {string.Join(" → ", route.Targets.Select(DisplayTarget))}.",
                secretValuesReturned = false,
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_route", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not save the {Feature} capability route.", featureCode);
            return Results.Json(
                new { status = "route_save_unavailable", message = "The capability route could not be saved." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ResetRouteAsync(
        string featureCode,
        CelarAiRouteUpdateRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        if (ReleaseMutationBlocked() is { } blocked) return blocked;
        try
        {
            var route = await store.ResetRouteAsync(
                featureCode,
                request.ExpectedRevision,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_capability_route_reset",
                route = route.ToPublicResponse(),
                message = $"{route.DisplayName} was reset to Celar AI → Claude → OpenAI → Governed local template.",
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_route", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not reset the {Feature} capability route.", featureCode);
            return Results.Json(
                new { status = "route_reset_unavailable", message = "The capability route could not be reset." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetConsumersAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CelarAiConsumerAssuranceRegistry assurance,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var routes = (await store.LoadRoutesAsync(cancellationToken)).ToDictionary(route => route.FeatureCode, StringComparer.OrdinalIgnoreCase);
        return Results.Ok(new
        {
            module = "064",
            status = "celar_ai_consumer_assurance_loaded",
            consumers = assurance.Snapshots().Select(item => new
            {
                feature = item.Feature,
                module = item.Module,
                entryPoint = item.EntryPoint,
                route = routes.TryGetValue(item.Feature, out var route) ? route.Targets : CelarAiCapabilityTargets.DefaultOrder,
                item.CentralRouterConnected,
                item.PrivateContextCompliant,
                item.DirectProviderFree,
                item.LastExercisedAt,
                item.LastSuccessAt,
                item.LastFailureAt,
                item.LastTarget,
                item.LastOutcome,
                item.LastCorrelationId
            }),
            buildPolicy = new
            {
                directClaudeOrOpenAiClientsAllowedInConsumers = false,
                providerKeysReadableByConsumers = false,
                module064BoundaryRequired = true,
                privateEvidenceExternalized = false
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> GetKnowledgeFabricAsync(
        HttpContext context,
        CelarAiKnowledgeFabricService knowledgeFabric,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var snapshot = await knowledgeFabric.GetSnapshotAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "064",
            status = snapshot.Status,
            contractVersion = CelarAiKnowledgeFabricService.ContractVersion,
            knowledgeFabric = snapshot,
            privacyBoundary = new
            {
                endpointValuesReturned = false,
                secretValuesReturned = false,
                rawDocumentsReturned = false,
                promptsReturned = false,
                embeddingVectorsReturned = false
            },
            stateChanged = false
        });
    }

    private static async Task<IResult> GetPrivateModelAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        PulseAiPrivateDocumentRuntimeService runtime,
        ProjectPulseAiConfiguration providerConfiguration,
        ProjectPulseAiHealthRegistry health,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var profile = await store.LoadPrivateModelProfileAsync(cancellationToken);
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        var policy = await PrivateEndpointPolicyAsync(profile, cancellationToken);
        var runtimeReadiness = await runtime.GetReadinessAsync(cancellationToken);
        var routes = await store.LoadRoutesAsync(cancellationToken);
        var requiredFeatures = CelarAiCapabilityCatalog.Definitions.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredRoutes = routes.Where(route => requiredFeatures.Contains(route.FeatureCode)).ToArray();
        var routesReady = requiredRoutes.Length == requiredFeatures.Count
            && requiredRoutes.All(route =>
                (route.Persisted || route.DeploymentManaged)
                && route.Targets.Count == CelarAiCapabilityTargets.DefaultOrder.Length
                && route.Targets.SequenceEqual(
                    release.IsReleaseScoped ? release.RouteOrder : CelarAiCapabilityTargets.DefaultOrder,
                    StringComparer.OrdinalIgnoreCase));

        // A single policy flag cannot silently create a half-enabled public
        // failover path. When either external-fallback control is requested,
        // production readiness requires both controls plus live, fresh evidence
        // for both approved sanitized providers in the configured route order.
        var sanitizedExternalExecutionEnabled = RuntimeFlag(
            "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION");
        var enterpriseSanitizedExternalFallbackEnabled = RuntimeFlag(
            "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED");
        var sanitizedExternalFallbackRequired = sanitizedExternalExecutionEnabled
            || enterpriseSanitizedExternalFallbackEnabled;
        var sanitizedExternalFallbackEnabled = sanitizedExternalExecutionEnabled
            && enterpriseSanitizedExternalFallbackEnabled;

        health.ApplyConfiguration(providerConfiguration.Claude);
        health.ApplyConfiguration(providerConfiguration.OpenAi);
        var healthSnapshots = health.Snapshots();
        var externalHealthFreshnessSeconds = Math.Max(
            providerConfiguration.HealthIntervalSeconds * 2,
            60);
        var externalHealthFreshAfter = DateTimeOffset.UtcNow.AddSeconds(-externalHealthFreshnessSeconds);
        var claudeHealth = healthSnapshots.FirstOrDefault(item =>
            string.Equals(item.Provider, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase));
        var openAiHealth = healthSnapshots.FirstOrDefault(item =>
            string.Equals(item.Provider, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase));
        var claudeProductionReady = ExternalProviderProductionReady(
            providerConfiguration.Claude,
            claudeHealth,
            externalHealthFreshAfter);
        var openAiProductionReady = ExternalProviderProductionReady(
            providerConfiguration.OpenAi,
            openAiHealth,
            externalHealthFreshAfter);

        var blockers = new List<string>();
        if (!store.DatabaseAvailable) blockers.Add("The Pulse database connection is unavailable to Module 064.");
        if (!store.SecretEncryptionAvailable) blockers.Add("A stable base64-encoded 32-byte PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY is required.");
        if (!profile.Persisted && !profile.DeploymentManaged)
            blockers.Add("The private Celar AI profile is neither persisted nor deployment-managed for this environment.");
        if (!profile.Enabled) blockers.Add("The private Celar AI target is disabled.");
        if (!profile.EndpointConfigured) blockers.Add("The private OpenAI-compatible endpoint is not configured.");
        if (!profile.ModelConfigured) blockers.Add("The private model or deployment name is not configured.");
        if (!profile.AuthenticationConfigured) blockers.Add("Bearer authentication is not configured for the private Celar AI target.");
        if (!profile.RequirePrivateModelForDocuments) blockers.Add("Private inference is not required for document-grounded answers.");
        if (policy != "private_endpoint_dns_verified") blockers.Add($"The private inference endpoint did not pass HTTPS, allowlist, and private-DNS verification ({policy}).");
        if (!routesReady) blockers.Add("All eight central AI capability routes must use the exact governed deployment order with governed local template last.");
        if (sanitizedExternalFallbackRequired && !sanitizedExternalFallbackEnabled)
            blockers.Add("Sanitized external fallback requires both PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION and PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED.");
        if (sanitizedExternalFallbackRequired && !providerConfiguration.Claude.Enabled)
            blockers.Add("Claude must be enabled when sanitized external fallback is required.");
        if (sanitizedExternalFallbackRequired && !providerConfiguration.Claude.Configured)
            blockers.Add("Claude must have a write-only API credential when sanitized external fallback is required.");
        if (sanitizedExternalFallbackRequired
            && !ProviderModelApproved(providerConfiguration.Claude))
            blockers.Add("Claude must have an exact approved model when sanitized external fallback is required.");
        if (sanitizedExternalFallbackRequired
            && providerConfiguration.Claude.Enabled
            && providerConfiguration.Claude.Configured
            && !claudeProductionReady)
            blockers.Add("Claude needs a fresh successful health probe before sanitized external fallback is production ready.");
        if (sanitizedExternalFallbackRequired && !providerConfiguration.OpenAi.Enabled)
            blockers.Add("OpenAI must be enabled when sanitized external fallback is required.");
        if (sanitizedExternalFallbackRequired && !providerConfiguration.OpenAi.Configured)
            blockers.Add("OpenAI must have a write-only API credential when sanitized external fallback is required.");
        if (sanitizedExternalFallbackRequired
            && !ProviderModelApproved(providerConfiguration.OpenAi))
            blockers.Add("OpenAI must have an exact approved model when sanitized external fallback is required.");
        if (sanitizedExternalFallbackRequired
            && providerConfiguration.OpenAi.Enabled
            && providerConfiguration.OpenAi.Configured
            && !openAiProductionReady)
            blockers.Add("OpenAI needs a fresh successful health probe before sanitized external fallback is production ready.");
        blockers.AddRange(runtimeReadiness.Blockers);
        blockers.AddRange(runtimeReadiness.MissingConfiguration);
        var configurationReady = blockers.Count == 0;
        var privateTargetHealth = health.Snapshots().FirstOrDefault(item =>
            string.Equals(item.Provider, CelarAiCapabilityTargets.CelarAi, StringComparison.OrdinalIgnoreCase));
        var persistedProbe = release.IsCandidate
            ? null
            : await store.LoadPrivateProbeEvidenceAsync(profile.Revision, cancellationToken);
        var releaseProbeFreshAfter = DateTimeOffset.UtcNow.AddMinutes(-15);
        var privateTargetVerifiedAt = release.IsCandidate
            ? privateTargetHealth?.LastProbeSuccessAt
            : persistedProbe?.TestedAt;
        var privateTargetVerificationFresh = release.IsCandidate
            ? privateTargetHealth is { ProbeStatus: "available", LastProbeSuccessAt: { } probeSuccessAt }
                && probeSuccessAt >= releaseProbeFreshAfter
            : persistedProbe is { Available: true, Fresh: true };
        if (!privateTargetVerificationFresh)
            blockers.Add(release.IsCandidate
                ? "The private Celar AI target needs a successful candidate-local probe for this exact release revision within the last 15 minutes."
                : "The private Celar AI target needs shared successful probe evidence for this exact profile revision within the last 15 minutes.");
        var productionReady = blockers.Count == 0;
        return Results.Ok(new
        {
            module = "064",
            status = productionReady
                ? "celar_ai_private_platform_production_ready"
                : configurationReady
                    ? "celar_ai_private_platform_requires_runtime_verification"
                    : profile.Configured
                    ? "celar_ai_private_platform_requires_configuration"
                    : "celar_ai_private_model_not_configured",
            profile = profile.ToPublicResponse(policy),
            secureStore = new
            {
                databaseAvailable = store.DatabaseAvailable,
                encryptionAvailable = store.SecretEncryptionAvailable,
                endpointWriteOnly = true,
                tokenWriteOnly = true,
                endpointReturned = false,
                tokenReturned = false
            },
            productionReadiness = new
            {
                ready = productionReady,
                configurationReady,
                privateModelReady = profile.Ready
                    && policy == "private_endpoint_dns_verified"
                    && privateTargetVerificationFresh,
                privateTargetAvailability = new
                {
                    verified = privateTargetVerificationFresh,
                    status = privateTargetVerificationFresh ? "available" : "not_verified",
                    probeStatus = release.IsCandidate
                        ? privateTargetHealth?.ProbeStatus ?? "not_checked"
                        : persistedProbe is null ? "not_checked" : persistedProbe.Available ? "available" : "degraded",
                    verifiedAt = privateTargetVerifiedAt,
                    freshnessMinutes = 15,
                    evidenceScope = release.IsCandidate ? "release_revision_local_memory" : "database_shared_profile_revision",
                    profileRevision = profile.Revision,
                    lastFailureCode = persistedProbe is { Available: false }
                        ? persistedProbe.DiagnosticCode
                        : privateTargetHealth?.LastFailureCode
                            ?? privateTargetHealth?.LastProbeFailureCode
                            ?? string.Empty
                },
                privateDocumentRuntimeReady = runtimeReadiness.Status == "private_document_runtime_ready",
                allCentralCapabilityRoutesReady = routesReady,
                requiredTimesheetRoutesPersisted = routesReady,
                configurationAuthority = release.ConfigurationAuthority,
                releasePhase = release.PhaseCode,
                deploymentManaged = release.IsReleaseScoped,
                readOnly = release.IsReleaseScoped,
                configurationSourceCommit = release.ConfigurationSourceCommit,
                sanitizedExternalFallback = new
                {
                    required = sanitizedExternalFallbackRequired,
                    enabled = sanitizedExternalFallbackEnabled,
                    productionReady = !sanitizedExternalFallbackRequired
                        || sanitizedExternalFallbackEnabled && claudeProductionReady && openAiProductionReady,
                    privacyBoundary = "purpose_built_deidentified_capsules_only",
                    privateDocumentContentAllowed = false,
                    customerIdentityAllowed = false,
                    healthFreshnessSeconds = externalHealthFreshnessSeconds,
                    providers = new object[]
                    {
                        ExternalProviderReadinessResponse(
                            providerConfiguration.Claude,
                            claudeHealth,
                            claudeProductionReady),
                        ExternalProviderReadinessResponse(
                            providerConfiguration.OpenAi,
                            openAiHealth,
                            openAiProductionReady)
                    }
                },
                migrations = new
                {
                    migration052Applied = runtimeReadiness.MigrationApplied,
                    migration053Applied = runtimeReadiness.RagMigrationApplied,
                    migration061Applied = runtimeReadiness.RoutingMigrationApplied,
                    migration071Applied = runtimeReadiness.HardeningMigrationApplied,
                    allRequiredApplied = runtimeReadiness.ProductionMigrationsApplied
                },
                storage = new
                {
                    sharedPersistentWritable = runtimeReadiness.UploadStorageProductionReady,
                    rootFingerprint = runtimeReadiness.UploadRootFingerprint,
                    pathReturned = false
                },
                processing = new
                {
                    workerEnabled = runtimeReadiness.WorkerEnabled,
                    automaticQueueEnabled = runtimeReadiness.AutomaticDocumentQueueEnabled,
                    candidateExecutionBlocked = release.IsCandidate,
                    servicePrincipalConfigured = runtimeReadiness.DocumentServicePrincipalConfigured,
                    servicePrincipalActive = runtimeReadiness.DocumentServicePrincipalActive,
                    servicePrincipalQueuePermissionGranted = runtimeReadiness.DocumentServicePrincipalQueuePermissionGranted,
                    servicePrincipalAuthorized = runtimeReadiness.DocumentServicePrincipalAuthorized,
                    servicePrincipalDiagnosticCode = runtimeReadiness.DocumentServicePrincipalDiagnosticCode,
                    malwareScannerPrivate = runtimeReadiness.MalwareScannerEndpointPrivate,
                    ocrConfigured = runtimeReadiness.OcrConfigured,
                    ocrEndpointPrivate = runtimeReadiness.OcrEndpointPrivate,
                    embeddingConfigured = runtimeReadiness.EmbeddingConfigured,
                    embeddingEndpointPrivate = runtimeReadiness.EmbeddingEndpointPrivate,
                    lexicalOnlyCompletionApproved = runtimeReadiness.LexicalOnlyCompletionApproved
                },
                documents = new
                {
                    readyDocumentCount = runtimeReadiness.ReadyDocumentCount,
                    readySowDocumentCount = runtimeReadiness.ReadySowDocumentCount,
                    pendingSowDocumentCount = runtimeReadiness.PendingSowDocumentCount,
                    awaitingOcrJobCount = runtimeReadiness.AwaitingOcrJobCount,
                    failedJobCount = runtimeReadiness.FailedJobCount,
                    atLeastOneAuthorizedSowReady = runtimeReadiness.ReadySowDocumentCount > 0
                },
                blockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            },
            requiredRuntime = new
            {
                openAiCompatiblePrivateEndpoint = true,
                privateOrAllowlistedHost = true,
                modelNameRequired = true,
                supportedAuthenticationMethod = "bearer",
                bearerTokenRequired = true,
                dnsMustResolveOnlyToPrivateAddresses = true,
                httpsRequired = true
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> SavePrivateModelSettingsAsync(
        CelarAiPrivateModelSettingsRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        if (ReleaseMutationBlocked() is { } blocked) return blocked;
        try
        {
            var profile = await store.SavePrivateModelSettingsAsync(
                request,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_private_model_settings_saved",
                profile = profile.ToPublicResponse(await PrivateEndpointPolicyAsync(profile, cancellationToken)),
                message = "The private Celar AI settings were saved. Endpoint and token values are not returned.",
                endpointReturned = false,
                tokenReturned = false,
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_private_model_settings", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not save private Celar AI settings.");
            return Results.Json(
                new { status = "private_model_settings_unavailable", message = "The private model settings could not be saved." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> SavePrivateModelSecretAsync(
        CelarAiPrivateModelSecretRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        if (ReleaseMutationBlocked() is { } blocked) return blocked;
        try
        {
            var profile = await store.SavePrivateModelSecretAsync(
                request,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_private_model_secret_saved",
                profile = profile.ToPublicResponse(await PrivateEndpointPolicyAsync(profile, cancellationToken)),
                message = "The private Celar AI bearer token was encrypted and saved. It cannot be viewed after saving.",
                endpointReturned = false,
                tokenReturned = false,
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_private_model_secret", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not save the private Celar AI bearer token.");
            return Results.Json(
                new { status = "private_model_secret_unavailable", message = "The private model token could not be saved securely." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> TestPrivateModelAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CelarAiPrivateGenerationTarget target,
        ProjectPulseAiHealthRegistry health,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        var profile = await store.LoadPrivateModelProfileAsync(cancellationToken);
        if (!profile.Configured)
            return Results.BadRequest(new
            {
                status = "private_model_not_configured",
                message = "Save a private endpoint and model before testing Celar AI."
            });
        var stopwatch = Stopwatch.StartNew();
        var result = await target.ProbeAsync(profile, cancellationToken);
        health.RecordProbe(result);
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        var testedAt = DateTimeOffset.UtcNow;
        var expiresAt = testedAt.AddMinutes(15);
        if (!release.IsCandidate)
        {
            var persistedProbe = await store.SavePrivateProbeEvidenceAsync(
                profile,
                result,
                TimeSpan.FromMinutes(15),
                cancellationToken);
            testedAt = persistedProbe.TestedAt;
            expiresAt = persistedProbe.ExpiresAt;
        }
        stopwatch.Stop();
        return Results.Json(new
        {
            status = result.Available ? "private_model_available" : "private_model_unavailable",
            configured = profile.Configured,
            available = result.Available,
            model = profile.Model,
            latencyMilliseconds = stopwatch.ElapsedMilliseconds,
            diagnosticCode = result.Code,
            requestId = result.RequestId,
            endpointReturned = false,
            tokenReturned = false,
            testedAt,
            expiresAt,
            evidenceScope = release.IsCandidate ? "release_revision_local_memory" : "database_shared_profile_revision",
            deploymentManaged = release.IsReleaseScoped,
            databaseEvidenceWritten = !release.IsCandidate,
            stateChanged = false
        }, statusCode: result.Available ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> TestSanitizedExternalFallbackAsync(
        HttpContext context,
        CelarAiCapabilityRouter router,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(
            context,
            requireSameOrigin: true,
            cancellationToken);
        if (authorization is not null) return authorization;

        var result = await router.ProbeSanitizedExternalFallbackAsync(
            CorrelationId(context),
            cancellationToken);
        return Results.Json(new
        {
            module = "064",
            status = result.Status,
            ready = result.Ready,
            providerOrder = new[]
            {
                CelarAiCapabilityTargets.Claude,
                CelarAiCapabilityTargets.OpenAi
            },
            policy = new
            {
                sanitizedExternalExecutionEnabled = result.SanitizedExternalExecutionEnabled,
                enterpriseSanitizedExternalFallbackEnabled = result.EnterpriseSanitizedExternalFallbackEnabled,
                fixedServerAuthoredCapsule = true,
                callerContentAccepted = false,
                projectOrTaskContextRead = false,
                customerOrPeopleContextRead = false,
                privateDocumentContextRead = false,
                providerContentReturned = false,
                sharedRouteChanged = false,
                stateChanged = false
            },
            targets = result.Targets.Select(target => new
            {
                provider = target.Provider,
                status = target.Status,
                diagnosticCode = target.DiagnosticCode,
                requestId = target.RequestId
            }).ToArray(),
            generatedAt = result.GeneratedAt
        }, statusCode: result.Ready
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> RotateEncryptionKeyAsync(
        ProjectPulseAiEncryptionRotationRequest request,
        HttpContext context,
        ProjectPulseAiEncryptionRotationService rotation,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        if (ReleaseMutationBlocked() is { } blocked) return blocked;
        try
        {
            var result = await rotation.RotateAsync(
                request,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "ai_encryption_key_rotation_completed",
                previousKeyId = result.PreviousKeyId,
                activeKeyId = result.ActiveKeyId,
                publicProviderSecretsRotated = result.PublicProviderSecretsRotated,
                privateProfileRotated = result.PrivateProfileRotated,
                rotatedAt = result.RotatedAt,
                actorUserId = result.ActorUserId,
                secretValuesReturned = false,
                keyMaterialReturned = false,
                stateChanged = true
            });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_encryption_key_rotation", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 encryption-key rotation failed without exposing key material.");
            return Results.Json(
                new
                {
                    status = "encryption_key_rotation_unavailable",
                    message = "The atomic encryption-key rotation did not complete. No key material was returned."
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GenerateFlowHiveAsync(
        CelarAiComposeRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiEnterprisePlatformService platform,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var result = await platform.ComposeAsync(
            identity.Value.Actual,
            identity.Value.Effective,
            request with { Mode = string.IsNullOrWhiteSpace(request.Mode) ? "project_plan" : request.Mode },
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "066",
            feature = CelarAiCapabilityCatalog.ProjectFlowHivePlan,
            status = result.Status,
            result = result.ToPublicResponse(),
            reviewRequired = true,
            scheduleEngineValidationRequired = true,
            planSaved = false,
            planBaselined = false,
            customerDateCommitted = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GenerateSowAsync(
        CelarAiComposeRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiEnterprisePlatformService platform,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var result = await platform.ComposeAsync(
            identity.Value.Actual,
            identity.Value.Effective,
            request with { Mode = "sow_draft" },
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "025",
            feature = CelarAiCapabilityCatalog.SowGsdPlanning,
            status = result.Status,
            result = result.ToPublicResponse(),
            reviewRequired = true,
            contractuallyBinding = false,
            sowPublished = false,
            approvedSowOverwritten = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GenerateCloseoutCommunicationAsync(
        CelarAiCloseoutCommunicationRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiCapabilityRouter router,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var correlationId = CorrelationId(context);
        var projectCode = Clean(request.ProjectCode, 120);
        var projectName = Clean(request.ProjectName, 300);
        var prompt = $"""
            Prepare a review-only {Clean(request.Audience, 80, "internal")} project closeout communication.
            Project code: {projectCode}
            Project name: {projectName}
            Completion summary: {Clean(request.CompletionSummary, 6000)}
            Acceptance evidence: {Clean(request.AcceptanceEvidence, 6000)}
            Outstanding items: {Clean(request.OutstandingItems, 6000)}
            Operational handoff: {Clean(request.HandoffSummary, 6000)}
            Requested tone: {Clean(request.RequestedTone, 80, "professional and factual")}

            Produce a comprehensive communication with a subject, concise executive opening, verified completion,
            acceptance evidence, deliverables and handoff, outstanding items, owners, risks, next actions, and an explicit
            review/approval boundary. Separate completed facts from missing evidence and assumptions. Do not invent
            customer acceptance, billing completion, deliverable completion, dates, recipients, owners, or commitments.
            Return an unsent draft only. The owning closeout and notification modules retain final authority.
            """;
        var routed = await router.GenerateAsync(
            new ProjectPulseAiGenerationRequest(
                CelarAiCapabilityCatalog.CloseoutCommunication,
                "Create comprehensive, structured, factual professional-services closeout communication drafts. Lead concisely, preserve all verified evidence and open items, and never send a message or claim approval without evidence.",
                prompt,
                1800,
                0.15),
            new CelarAiCapabilityExecutionContext(
                CelarAiCapabilityCatalog.CloseoutCommunication,
                ContainsPrivateDocuments: true,
                ContainsCustomerIdentity: projectCode.Length > 0 || projectName.Length > 0,
                ContainsPeopleRecords: false,
                ContainsFinancialValues: false,
                // The fixed closeout capsule is backend-managed; the request's
                // legacy fallback checkbox cannot authorize or disable it.
                AllowSanitizedExternalAssistance: false,
                SensitiveTerms: [projectCode, projectName, "US Signal", "Pulse"],
                ConsumerModule: "040/055C",
                CorrelationId: correlationId,
                IdentityTerms: [projectCode, projectName],
                ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.CloseoutCommunication),
            () => BuildCloseoutFallback(request),
            cancellationToken);
        var externalSelected = routed.Provider is CelarAiCapabilityTargets.Claude
            or CelarAiCapabilityTargets.OpenAi;
        var refused = routed.Outcome == ProjectPulseAiOutcomes.Refusal;
        var externalBoundaryWarning = externalSelected
            ? "The selected public target received only a fixed backend-owned identity-free closeout structure and tone capsule. Its generic guidance is separate from the review draft and establishes no project, customer, acceptance, completion, recipient, date, owner, or commitment fact."
            : string.Empty;
        return Results.Ok(new
        {
            module = "040/055C",
            feature = CelarAiCapabilityCatalog.CloseoutCommunication,
            status = refused
                ? "closeout_draft_refused"
                : externalSelected
                    ? "closeout_draft_completed_with_generic_structure_assistance"
                : "closeout_draft_completed",
            draft = refused
                ? string.Empty
                : externalSelected
                    ? BuildCloseoutFallback(request)
                    : routed.Content,
            externalAssistance = externalSelected && !refused ? routed.Content : string.Empty,
            selectedTarget = routed.Provider,
            attemptedTargets = routed.AttemptedProviders,
            skippedTargets = routed.SkippedProviders,
            targetDecisions = routed.TargetDecisions ?? [],
            warning = string.Join(" ", new[] { routed.Warning, externalBoundaryWarning }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            correlationId,
            reviewRequired = true,
            emailSent = false,
            projectClosed = false,
            billingChanged = false,
            stateChanged = false
        });
    }

    private static string BuildCloseoutFallback(CelarAiCloseoutCommunicationRequest request)
    {
        var project = Clean(request.ProjectName, 300, Clean(request.ProjectCode, 120, "Project"));
        var completion = Clean(request.CompletionSummary, 6000, "No verified completion summary was supplied.");
        var acceptance = Clean(request.AcceptanceEvidence, 6000, "No acceptance evidence was supplied.");
        var outstanding = Clean(request.OutstandingItems, 6000, "No outstanding-item evidence was supplied; confirm this in the authoritative project record.");
        var handoff = Clean(request.HandoffSummary, 6000, "No operational handoff summary was supplied.");
        return $"""
            Subject: Project closeout review — {project}

            This review-only draft summarizes the currently supplied closeout evidence for {project}. It is not a closure,
            acceptance, billing, delivery, recipient, or customer-commitment record.

            Verified completion summary
            {completion}

            Acceptance evidence
            {acceptance}

            Operational handoff
            {handoff}

            Outstanding items, owners, and risks
            {outstanding}

            Next actions and review boundary
            Confirm deliverable completion, acceptance, dates, owners, risks, billing status, recipients, and commitments
            in the authoritative Pulse records. Obtain the required PM, Engineering, and closeout approvals before sending
            this communication or changing project state.
            """.Trim();
    }

    private static async Task<string> PrivateEndpointPolicyAsync(
        CelarAiPrivateModelProfile profile,
        CancellationToken cancellationToken)
    {
        if (!profile.EndpointConfigured) return "private_endpoint_not_configured";
        var resolution = await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
            profile.Endpoint,
            profile.PrivateHostAllowlist,
            requireHttps: true,
            allowLoopback: false,
            cancellationToken: cancellationToken);
        return resolution.Approved
            ? "private_endpoint_dns_verified"
            : $"private_endpoint_rejected_{resolution.Reason}";
    }

    private static bool ExternalProviderProductionReady(
        ProjectPulseAiProviderConfiguration configuration,
        ProjectPulseAiProviderHealthSnapshot? health,
        DateTimeOffset freshAfter) =>
        configuration.Enabled
        && configuration.Configured
        && ProviderModelApproved(configuration)
        && health is not null
        && health.Enabled
        && health.Configured
        && string.Equals(health.Status, "available", StringComparison.OrdinalIgnoreCase)
        && string.Equals(health.ProbeStatus, "available", StringComparison.OrdinalIgnoreCase)
        && health.LastProbeSuccessAt is { } verifiedAt
        && verifiedAt >= freshAfter;

    private static object ExternalProviderReadinessResponse(
        ProjectPulseAiProviderConfiguration configuration,
        ProjectPulseAiProviderHealthSnapshot? health,
        bool productionReady) => new
    {
        code = configuration.Code,
        enabled = configuration.Enabled,
        configured = configuration.Configured,
        modelConfigured = !string.IsNullOrWhiteSpace(configuration.Model),
        modelApproved = ProviderModelApproved(configuration),
        available = productionReady,
        status = health?.Status ?? "not_registered",
        probeStatus = health?.ProbeStatus ?? "not_registered",
        verifiedAt = health?.LastProbeSuccessAt,
        diagnosticCode = health?.LastProbeFailureCode
            ?? health?.LastFailureCode
            ?? string.Empty,
        credentialReturned = false
    };

    private static bool ProviderModelApproved(ProjectPulseAiProviderConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.Model)
        && configuration.ApprovedModels.Contains(
            configuration.Model,
            StringComparer.OrdinalIgnoreCase);

    private static bool RuntimeFlag(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var enabled) && enabled;

    private static async Task<IResult?> AuthorizeAdministratorAsync(
        HttpContext context,
        bool requireSameOrigin,
        CancellationToken cancellationToken)
    {
        var actual = ActualSessionUserId(context);
        if (actual is null) return SessionRequired();
        var effective = EffectiveSessionUserId(context) ?? actual;
        var isViewAs = effective != actual
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is bool active && active);
        if (isViewAs)
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Module 064 configuration cannot be changed or inspected through Administrator View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        if (requireSameOrigin && !SameOrigin(context))
            return Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
        if (ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(
                context,
                Array.Empty<string>()))
            return null;
        var connectionString = ConnectionString();
        if (connectionString is null)
            return Results.Json(new { status = "configuration_unavailable", message = "Administrator authorization could not be verified." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            if (await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                    context,
                    cancellationToken: cancellationToken))
                return null;

            // Preserve the pre-existing Module 064 SYSTEM_ADMINISTRATOR grant
            // without promoting that role to permanent platform-wide control.
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT COALESCE(string_agg(DISTINCT r.role_code, ','), '')
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                WHERE u.user_id = @user_id AND u.is_active = TRUE;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("user_id", actual.Value);
            var roles = ((await command.ExecuteScalarAsync(cancellationToken))?.ToString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Any(AdditionalModuleAdministratorRoles.Contains)) return null;
        }
        catch (Exception exception)
        {
            Log(context).LogWarning(exception, "Module 064 could not verify administrator authority.");
            return Results.Json(new { status = "authorization_unavailable", message = "Administrator authorization could not be verified." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        return Results.Json(new { status = "access_denied", message = "AI Provider Configuration Center is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = EffectiveSessionUserId(context);
        if (effective is null) return null;
        return (ActualSessionUserId(context) ?? effective.Value, effective.Value);
    }

    private static Guid? ActualSessionUserId(HttpContext context) =>
        UserId(context, "ProjectPulseActualUserId")
        ?? UserId(context, "ProjectPulseSessionUserId");

    private static Guid? EffectiveSessionUserId(HttpContext context) =>
        UserId(context, "ProjectPulseEffectiveUserId")
        ?? UserId(context, "ProjectPulseSessionUserId");

    private static Guid? UserId(HttpContext context, string key)
    {
        if (!context.Items.TryGetValue(key, out var value)) return null;
        if (value is Guid id) return id;
        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static bool SameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http")) return false;
        if (string.Equals(context.Request.Headers["Sec-Fetch-Site"].ToString(), "same-origin", StringComparison.OrdinalIgnoreCase)) return true;
        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var publicHost = !string.IsNullOrWhiteSpace(forwardedHost)
            ? HostString.FromUriComponent(forwardedHost)
            : context.Request.Host;
        return string.Equals(uri.Host, publicHost.Host, StringComparison.OrdinalIgnoreCase)
            && (publicHost.Port is null || uri.Port == publicHost.Port);
    }

    private static string DisplayTarget(string target) => target switch
    {
        CelarAiCapabilityTargets.CelarAi => "Celar AI",
        CelarAiCapabilityTargets.Claude => "Claude",
        CelarAiCapabilityTargets.OpenAi => "OpenAI",
        _ => "Governed local template"
    };

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()[..Math.Min(value.ToString().Length, 160)]
            : context.TraceIdentifier;

    private static ILogger Log(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CelarAiCapabilityRoutingModule");

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid Pulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(string permission) => Results.Json(new
    {
        status = "forbidden",
        requiredPermission = permission,
        message = "The current effective user is not authorized for this Celar AI operation."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult? ReleaseMutationBlocked()
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
        if (!release.IsReleaseScoped) return null;
        return Results.Json(new
        {
            module = "064",
            status = "deployment_managed_configuration_read_only",
            message = "Candidate and active release revisions use immutable deployment-managed AI configuration. Change the protected release manifest and deploy a new revision instead of mutating shared configuration.",
            configurationAuthority = "deployment_managed_release",
            configurationSourceCommit = release.ConfigurationSourceCommit,
            stateChanged = false
        }, statusCode: StatusCodes.Status423Locked);
    }

    private sealed record CandidateDatabaseEvidence(
        bool ReadOnlyIdentity,
        bool ElevatedRole,
        bool SchemaCreateAllowed,
        bool DatabaseCreateAllowed,
        bool TemporaryTableAllowed,
        bool DefaultTransactionReadOnly,
        int MutableTableCount,
        int MutableSequenceCount,
        bool ExactSowConfigured,
        bool ExactSowReady,
        bool ExactDocumentReady,
        bool ExactVersionReady,
        bool ExactProjectReady,
        bool ExactSourceShaReady,
        bool ExactIndexReady,
        bool ExactChunkSetReady,
        int ExactChunkCount,
        string SampleChunkText,
        string DatabaseFingerprint,
        string ConfiguredRoleFingerprint,
        string ActiveRoleFingerprint)
    {
        public static CandidateDatabaseEvidence Unavailable(bool exactSowConfigured) => new(
            ReadOnlyIdentity: false,
            ElevatedRole: true,
            SchemaCreateAllowed: true,
            DatabaseCreateAllowed: true,
            TemporaryTableAllowed: true,
            DefaultTransactionReadOnly: false,
            MutableTableCount: -1,
            MutableSequenceCount: -1,
            ExactSowConfigured: exactSowConfigured,
            ExactSowReady: false,
            ExactDocumentReady: false,
            ExactVersionReady: false,
            ExactProjectReady: false,
            ExactSourceShaReady: false,
            ExactIndexReady: false,
            ExactChunkSetReady: false,
            ExactChunkCount: 0,
            SampleChunkText: string.Empty,
            DatabaseFingerprint: string.Empty,
            ConfiguredRoleFingerprint: string.Empty,
            ActiveRoleFingerprint: string.Empty);
    }

    private static string? ConnectionString() => ProjectPulseAiDatabaseConnection.Resolve();
}

public sealed record CelarAiCloseoutCommunicationRequest(
    string? ProjectCode,
    string? ProjectName,
    string? Audience,
    string? CompletionSummary,
    string? AcceptanceEvidence,
    string? OutstandingItems,
    string? HandoffSummary,
    string? RequestedTone,
    bool AllowSanitizedExternalFallback = false);
