using System.Data;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>Module 064 owns a separate Test-only recommendation capability.
/// No provider-order mutation, document-category mutation, approval or generation.
/// </summary>
public static class LayaDecisionModule
{
    private const string Root = "/api/ai-configuration/decisions/laya";
    private sealed record Policy(bool Enabled, long Version);

    public static IEndpointRouteBuilder MapLayaDecisionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        void Map(string path, string method, string action) => endpoints.MapMethods(
            Root + path, [method], (Func<HttpContext, Task<IResult>>)(c => HandleAsync(c, action)));
        Map("", "GET", "configuration");
        Map("", "PUT", "configure");
        Map("/health", "POST", "health");
        Map("/documents", "GET", "documents");
        Map("/documents/{documentId:guid}/processing-state", "GET", "processing-state");
        Map("/documents/{documentId:guid}/classifications", "POST", "classify");
        Map("/documents/{documentId:guid}/classifications", "GET", "history");
        Map("/documents/{documentId:guid}/classifications/{decisionId:guid}/review", "POST", "review");
        return endpoints;
    }

    private static Guid? Identity(HttpContext c, params string[] keys)
    {
        foreach (var key in keys)
            if (c.Items.TryGetValue(key, out var value)
                && Guid.TryParse(value?.ToString(), out var id) && id != Guid.Empty) return id;
        return null;
    }

    private static async Task<IResult> HandleAsync(HttpContext c, string action)
    {
        c.Response.Headers.CacheControl = "no-store";
        var ct = c.RequestAborted;
        var actual = Identity(c, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = Identity(c, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (actual is null || effective is null) return Fail("session_required", 401);
        if (actual != effective || c.Items.TryGetValue("ProjectPulseIsViewAs", out var view) && view is true)
            return Fail("decision_view_as_not_allowed", 403);
        try
        {
            var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
            if (release.IsCandidate) return Fail("decision_candidate_execution_blocked", 423);
            await using var db = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
            await db.OpenAsync(ct);
            if (!await AdministratorAsync(c, db, actual.Value, ct)) return Fail("administrator_required", 403);
            if (!HttpMethods.IsGet(c.Request.Method) && !AiProviderConfigurationModule.SameOrigin(c))
                return Fail("origin_rejected", 403);
            var policy = await ReadPolicyAsync(db, null, false, ct);
            var allowed = LayaDecisionTransport.DeploymentAllowed();
            if (action == "configuration") return Results.Ok(new
            {
                module = "064", capability = "document_classification", provider = "celar_ai",
                engine = "laya", enabled = policy.Enabled, version = policy.Version,
                deploymentAllowed = allowed, effectiveEnabled = allowed && policy.Enabled,
                mode = "human_review_only", configurationSource = "module064_database",
                modelRevision = LayaDecisionContract.Revision, labels = LayaDecisionContract.Labels,
                excerptPolicy = LayaDecisionContract.ExcerptPolicy,
                generativeProviderOrderChanged = false, externalFallbackAllowed = false,
                reviewRequired = true, productionAccuracyValidated = false
            });
            if (!allowed) return Fail("decision_deployment_not_allowed", 423);
            if (action == "configure")
            {
                var body = await BodyAsync(c, ["enabled", "version"]);
                var enabled = body["enabled"]!.GetValue<bool>();
                var version = body["version"]!.GetValue<long>();
                await using var tx = await db.BeginTransactionAsync(ct);
                await using var cmd = Command(db, tx, """
                    UPDATE celar_laya_settings SET enabled=@enabled, version=version+1,
                        changed_by=@actor, changed_at=now()
                    WHERE singleton=true AND version=@version RETURNING version
                    """, ("enabled", enabled), ("actor", actual.Value), ("version", version));
                var changed = await cmd.ExecuteScalarAsync(ct);
                if (changed is not long next) return Fail("decision_configuration_changed", 409);
                await ExecuteAsync(db, tx, """
                    INSERT INTO celar_laya_settings_audit(version,enabled,actor_id) VALUES(@version,@enabled,@actor)
                    """, ct, ("version", next), ("enabled", enabled), ("actor", actual.Value));
                await tx.CommitAsync(ct);
                return Results.Ok(new { enabled, version = next, providerOrderChanged = false });
            }
            if (action == "health")
            {
                await BodyAsync(c, []);
                var health = await LayaDecisionTransport.SendAsync(null, ct);
                if (health["status"]?.GetValue<string>() != "ready"
                    || health["runtime_connected"]?.GetValue<bool>() != true
                    || health["state_token_budget"]?.GetValue<int>() != 450)
                    return Fail("decision_runtime_not_ready", 503);
                return Results.Ok(new { status = "ready", runtimeConnected = true,
                    effectiveEnabled = policy.Enabled, modelRevision = LayaDecisionContract.Revision,
                    inferenceBusy = health["inference_busy"]?.GetValue<bool>() == true,
                    stateTokenBudget = 450, productionAccuracyValidated = false });
            }
            var pipeline = c.RequestServices.GetRequiredService<PulseAiPrivateDocumentPipelineService>();
            var evidenceReader = new LayaProcessedSourceReader(c.RequestServices.GetRequiredService<PulseAiPrivateRuntimeSourceResolver>());
            if (action == "documents")
            {
                var project = c.Request.Query["projectCode"].ToString().Trim();
                if (project.Length > 100) return Fail("invalid_project_filter", 400);
                var documents = await pipeline.ListInventoryAsync(effective.Value, project, "", "", 500, ct);
                var stages = await evidenceReader.StagesAsync(documents.Select(d => d.DocumentId).ToArray(), ct);
                return Results.Ok(new { limit = 500, documents = documents.Select(d => new
                {
                    documentId = d.DocumentId, fileName = d.OriginalFileName, projectCode = d.ProjectCode,
                    processingStage = stages.GetValueOrDefault(d.DocumentId, "unknown"),
                    previewAdmitted = false, evidenceSource = LayaProcessedSourceReader.ContractVersion
                }) });
            }
            if (action == "classify" && !policy.Enabled) return Fail("decision_capability_disabled", 409);
            var documentId = Guid.Parse(c.Request.RouteValues["documentId"]!.ToString()!);
            // Read the worker's actual scan/extraction receipt, never global preview flags.
            var processed = await evidenceReader.ReadAsync(effective.Value, documentId, ct);
            if (processed is null) return Fail("document_not_found_or_not_authorized", 404);
            if (action == "processing-state") return Results.Ok(processed.ToPublicEvidence());
            var source = processed.SourceSha256;
            if (action == "history")
                return Results.Ok(new { sourceSha256 = source, history = await HistoryAsync(db, documentId, ct) });
            if (!processed.Ready)
                return Results.Json(new
                {
                    status = "decision_document_admission_required", processing = processed.ToPublicEvidence(),
                    reviewRequired = true, externalFallbackAllowed = false, workflowActionsPerformed = 0
                }, statusCode: 422);
            if (action == "review")
            {
                var body = await BodyAsync(c, ["label"]);
                var label = body["label"]!.GetValue<string>();
                if (!LayaDecisionContract.Labels.Contains(label, StringComparer.Ordinal))
                    return Fail("decision_invalid_label", 400);
                var decisionId = Guid.Parse(c.Request.RouteValues["decisionId"]!.ToString()!);
                await using var tx = await db.BeginTransactionAsync(ct);
                if (!await LayaProcessedSourceReader.LockCurrentVersionAsync(db, tx, processed, ct))
                    return Fail("decision_source_changed", 409);
                await using var insert = Command(db, tx, """
                    INSERT INTO celar_laya_reviews(decision_id,reviewed_label,reviewed_by)
                    SELECT decision_id,@label,@actor FROM celar_laya_decisions
                    WHERE decision_id=@id AND document_id=@document AND source_sha256=@source
                    ON CONFLICT DO NOTHING RETURNING decision_id
                    """, ("id", decisionId), ("document", documentId), ("source", source),
                    ("label", label), ("actor", actual.Value));
                if (await insert.ExecuteScalarAsync(ct) is null)
                    return Fail("decision_already_reviewed_or_source_changed", 409);
                await tx.CommitAsync(ct);
                return Results.Ok(new { decisionId, reviewedLabel = label,
                    status = "review_recorded", documentCategoryChanged = false, workflowActionsPerformed = 0 });
            }
            if (action != "classify") return Fail("not_found", 404);
            if (!policy.Enabled) return Fail("decision_capability_disabled", 409);
            var classificationRequest = await BodyAsync(c, ["requestId"]);
            if (!Guid.TryParse(classificationRequest["requestId"]?.GetValue<string>(), out var requestId)
                || requestId == Guid.Empty) return Fail("decision_request_id_required", 400);
            var prior = await ExistingAsync(db, documentId, actual.Value, requestId, ct);
            if (prior is not null)
                return prior["sourceSha256"]?.GetValue<string>() == source
                    ? Results.Ok(prior) : Fail("decision_source_changed", 409);
            var excerpt = processed.Excerpt;
            var answer = LayaDecisionContract.Validate(await LayaDecisionTransport.SendAsync(excerpt, ct));
            // Do not disclose/persist a returned recommendation after revocation or replacement.
            var current = await evidenceReader.ReadAsync(effective.Value, documentId, ct);
            if (current is null) return Fail("document_not_found_or_not_authorized", 404);
            if (!LayaProcessedSourceReader.SameEvidence(processed, current))
                return Fail("decision_source_changed", 409);
            if (!LayaDecisionTransport.DeploymentAllowed()) return Fail("decision_deployment_not_allowed", 423);
            await using var decisionTx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var finalPolicy = await ReadPolicyAsync(db, decisionTx, true, ct);
            if (!await LayaProcessedSourceReader.LockCurrentVersionAsync(db, decisionTx, processed, ct))
                return Fail("decision_source_changed", 409);
            if (!finalPolicy.Enabled || finalPolicy.Version != policy.Version)
                return Fail("decision_configuration_changed", 409);
            var decision = Guid.NewGuid();
            answer["excerptPolicy"] = LayaDecisionContract.ExcerptPolicy;
            answer["excerptSha256"] = LayaDecisionContract.Sha256(excerpt);
            answer["excerptCharacters"] = excerpt.EnumerateRunes().Count();
            answer["sourceSectionIndex"] = processed.SectionIndex;
            answer["sourceVersionId"] = processed.VersionId!.Value.ToString();
            answer["sourceEvidenceContract"] = LayaProcessedSourceReader.ContractVersion;
            answer["analyzedWholeDocument"] = false;
            await using var save = Command(db, decisionTx, """
                INSERT INTO celar_laya_decisions(decision_id,document_id,source_sha256,request_id,created_by,policy_version,evidence)
                VALUES(@id,@document,@source,@request,@actor,@version,@evidence::jsonb)
                ON CONFLICT(document_id,created_by,request_id) DO NOTHING
                """, ("id", decision), ("document", documentId), ("source", source), ("request", requestId),
                ("actor", actual.Value), ("version", policy.Version), ("evidence", answer.ToJsonString()));
            await save.ExecuteNonQueryAsync(ct);
            await decisionTx.CommitAsync(ct);
            return Results.Ok(await ExistingAsync(db, documentId, actual.Value, requestId, ct));
        }
        catch (LayaDecisionFailure e) { return Fail(e.Code, e.Status); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Fail("request_cancelled", 499); }
        catch (PostgresException e) when (e.SqlState == "42P01") { return Fail("decision_schema_not_installed", 503); }
        catch (System.Text.Json.JsonException) { return Fail("decision_invalid_request", 400); }
        catch (InvalidDataException) { return Fail("decision_invalid_contract", 502); }
        catch (Exception e)
        {
            // Never log exception messages/objects, bodies, file paths or credentials.
            c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("LayaDecision")
                .LogWarning("Laya request failed: {Type}", e.GetType().Name);
            return Fail("decision_request_failed", 503);
        }
    }

    private static async Task<bool> AdministratorAsync(HttpContext c, NpgsqlConnection db, Guid actual, CancellationToken ct)
    {
        await using var active = Command(db, null, "SELECT EXISTS(SELECT 1 FROM app_users WHERE user_id=@id AND is_active=true)", ("id", actual));
        if (await active.ExecuteScalarAsync(ct) is not true) return false;
        if (ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(c, Array.Empty<string>())
            || await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(c, cancellationToken: ct)) return true;
        await using var roles = Command(db, null, """
            SELECT EXISTS(SELECT 1 FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id
            WHERE a.user_id=@id AND a.is_active=true AND r.is_active=true AND r.role_code='SYSTEM_ADMINISTRATOR')
            """, ("id", actual));
        return await roles.ExecuteScalarAsync(ct) is true;
    }

    private static async Task<JsonObject> BodyAsync(HttpContext c, string[] fields)
    {
        if (c.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
            throw new LayaDecisionFailure("decision_json_required", 415);
        if (c.Request.ContentLength is > 2048) throw new LayaDecisionFailure("decision_request_too_large", 413);
        var bytes = new byte[2049];
        var used = 0;
        while (used < bytes.Length)
        {
            var count = await c.Request.Body.ReadAsync(bytes.AsMemory(used), c.RequestAborted);
            if (count == 0) break;
            used += count;
        }
        if (used > 2048) throw new LayaDecisionFailure("decision_request_too_large", 413);
        using var parsed = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(0, used));
        if (parsed.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new LayaDecisionFailure("decision_invalid_request", 400);
        var properties = parsed.RootElement.EnumerateObject().ToArray();
        if (properties.Length != fields.Length || properties.Select(p => p.Name).Distinct().Count() != fields.Length
            || fields.Any(f => !properties.Any(p => p.Name == f)))
            throw new LayaDecisionFailure("decision_invalid_request", 400);
        foreach (var property in properties)
        {
            var valid = property.Name switch
            {
                "enabled" => property.Value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
                "version" => property.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                    && property.Value.TryGetInt64(out var version) && version > 0,
                _ => property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    && property.Value.GetString() is { Length: > 0 and <= 64 }
            };
            if (!valid) throw new LayaDecisionFailure("decision_invalid_request", 400);
        }
        return JsonNode.Parse(bytes.AsSpan(0, used))!.AsObject();
    }

    private static NpgsqlCommand Command(NpgsqlConnection db, NpgsqlTransaction? tx, string sql, params (string, object)[] args)
    {
        var command = new NpgsqlCommand(sql, db, tx) { CommandTimeout = 20 };
        foreach (var (key, value) in args) command.Parameters.AddWithValue(key, value);
        return command;
    }

    private static async Task ExecuteAsync(NpgsqlConnection db, NpgsqlTransaction? tx, string sql, CancellationToken ct, params (string, object)[] args)
    {
        await using var cmd = Command(db, tx, sql, args);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Policy> ReadPolicyAsync(NpgsqlConnection db, NpgsqlTransaction? tx, bool locked, CancellationToken ct)
    {
        await using var command = Command(db, tx, "SELECT enabled,version FROM celar_laya_settings WHERE singleton=true" + (locked ? " FOR UPDATE" : ""));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new LayaDecisionFailure("decision_schema_not_initialized");
        return new Policy(reader.GetBoolean(0), reader.GetInt64(1));
    }

    private static async Task<JsonObject?> ExistingAsync(NpgsqlConnection db, Guid doc, Guid actor, Guid request, CancellationToken ct)
    {
        await using var command = Command(db, null, """
            SELECT jsonb_build_object('decisionId',decision_id,'sourceSha256',source_sha256,
                'createdAt',created_at,'policyVersion',policy_version,'evidence',evidence)::text
            FROM celar_laya_decisions WHERE document_id=@doc AND created_by=@actor AND request_id=@request
            """, ("doc", doc), ("actor", actor), ("request", request));
        return await command.ExecuteScalarAsync(ct) is string json ? JsonNode.Parse(json) as JsonObject : null;
    }

    private static async Task<JsonArray> HistoryAsync(NpgsqlConnection db, Guid doc, CancellationToken ct)
    {
        await using var command = Command(db, null, """
            SELECT jsonb_build_object('decisionId',d.decision_id,'sourceSha256',d.source_sha256,
                'createdAt',d.created_at,'createdBy',d.created_by,'policyVersion',d.policy_version,
                'evidence',d.evidence,'review',CASE WHEN r.decision_id IS NULL THEN NULL ELSE
                jsonb_build_object('label',r.reviewed_label,'reviewedBy',r.reviewed_by,'reviewedAt',r.reviewed_at) END)::text
            FROM celar_laya_decisions d LEFT JOIN celar_laya_reviews r ON r.decision_id=d.decision_id
            WHERE d.document_id=@doc ORDER BY d.created_at DESC LIMIT 30
            """, ("doc", doc));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new JsonArray();
        while (await reader.ReadAsync(ct)) result.Add(JsonNode.Parse(reader.GetString(0)));
        return result;
    }

    private static IResult Fail(string code, int status) => Results.Json(new
    {
        status = code, reviewRequired = true, externalFallbackAllowed = false,
        workflowActionsPerformed = 0
    }, statusCode: status);
}
