using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Shared ProjectPulse-native edit/save persistence for Modules 064–070 and 073–074.
/// This module stores governed application documents only. It does not activate Entra,
/// Key Vault, AI-provider secrets, SMTP delivery, or any external system.
/// </summary>
public static class Module064074NativeAdministration
{
    private const string MigrationFile = "032_projectpulse_native_administration_documents.sql";
    private const int MaximumRequestBytes = 512 * 1024;
    private const int MaximumRecords = 1000;

    private static readonly IReadOnlyDictionary<string, ModuleDefinition> Definitions =
        new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal)
        {
            ["064"] = new(
                "064",
                "AI Provider Configuration",
                "configuration",
                "configuration",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR" },
                new[]
                {
                    Field("providerMode", "Provider mode", "select", true, new[] { "priority_failover", "claude_only", "openai_only", "local_only" }),
                    Field("claudeModel", "Claude model", "text"),
                    Field("openAiModel", "OpenAI model", "text"),
                    Field("localModel", "Local model", "text"),
                    Field("healthPollMinutes", "Health poll interval (minutes)", "number", min: 1, max: 1440),
                    Field("notes", "Governance notes", "textarea")
                },
                () => ConfigurationDocument(new JsonObject
                {
                    ["providerMode"] = "priority_failover",
                    ["claudeModel"] = "",
                    ["openAiModel"] = "",
                    ["localModel"] = "",
                    ["healthPollMinutes"] = 5,
                    ["notes"] = ""
                })),
            ["065"] = new(
                "065",
                "Entra Secret Administration Metadata",
                "configuration",
                "configuration",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR" },
                new[]
                {
                    Field("applicationId", "Application ID", "text"),
                    Field("tenantId", "Tenant ID", "text"),
                    Field("ownerTeam", "Owner team", "text"),
                    Field("secretLabel", "Secret label", "text"),
                    Field("secretPresent", "Secret presence recorded", "checkbox"),
                    Field("secretFingerprint", "Secret fingerprint (non-secret)", "text"),
                    Field("rotationWindowDays", "Rotation window (days)", "number", min: 1, max: 730),
                    Field("warningDays", "Warning threshold (days)", "number", min: 1, max: 365),
                    Field("criticalDays", "Critical threshold (days)", "number", min: 0, max: 365),
                    Field("nextReviewDate", "Next review date", "date"),
                    Field("notes", "Governance notes", "textarea")
                },
                () => ConfigurationDocument(new JsonObject
                {
                    ["applicationId"] = "",
                    ["tenantId"] = "",
                    ["ownerTeam"] = "",
                    ["secretLabel"] = "",
                    ["secretPresent"] = false,
                    ["secretFingerprint"] = "",
                    ["rotationWindowDays"] = 90,
                    ["warningDays"] = 30,
                    ["criticalDays"] = 14,
                    ["nextReviewDate"] = "",
                    ["notes"] = ""
                })),
            ["066"] = new(
                "066",
                "Project FlowHive Plans",
                "collection",
                "plans",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT" },
                new[]
                {
                    Field("planId", "Plan ID", "text"),
                    Field("planName", "Plan name", "text", true),
                    Field("projectCode", "Project code", "text"),
                    Field("status", "Status", "select", true, new[] { "draft", "active", "on_hold", "complete", "archived" }),
                    Field("baselineLabel", "Baseline label", "text"),
                    Field("startDate", "Start date", "date"),
                    Field("targetDate", "Target date", "date"),
                    Field("ownerUserId", "Plan owner", "identity"),
                    Field("wbs", "WBS JSON", "textarea"),
                    Field("dependencies", "Dependency JSON", "textarea"),
                    Field("collaborationNotes", "Collaboration notes", "textarea")
                },
                () => CollectionDocument("plans")),
            ["067"] = new(
                "067",
                "Global Mail Configuration",
                "configuration",
                "configuration",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR" },
                new[]
                {
                    Field("providerTarget", "Provider target", "select", true, new[] { "locked", "microsoft_graph", "smtp_relay" }),
                    Field("senderName", "Sender name", "text"),
                    Field("senderAddress", "Sender address", "email"),
                    Field("replyToAddress", "Reply-to address", "email"),
                    Field("recipientBoundary", "Recipient boundary", "select", true, new[] { "locked", "test_only", "production_governed" }),
                    Field("notes", "Governance notes", "textarea")
                },
                () => ConfigurationDocument(new JsonObject
                {
                    ["providerTarget"] = "locked",
                    ["senderName"] = "",
                    ["senderAddress"] = "",
                    ["replyToAddress"] = "",
                    ["recipientBoundary"] = "locked",
                    ["notes"] = ""
                })),
            ["068"] = new(
                "068",
                "System Architecture Records",
                "collection",
                "components",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR" },
                new[]
                {
                    Field("componentId", "Component ID", "text"),
                    Field("name", "Component name", "text", true),
                    Field("category", "Category", "select", true, new[] { "application", "data", "authentication", "integration", "environment" }),
                    Field("environment", "Environment", "text"),
                    Field("ownerUserId", "Owner", "identity"),
                    Field("dataClassification", "Data classification", "select", false, new[] { "public", "internal", "confidential", "restricted" }),
                    Field("status", "Status", "select", true, new[] { "planned", "active", "degraded", "retired" }),
                    Field("notes", "Architecture notes", "textarea")
                },
                () => CollectionDocument("components")),
            ["069"] = new(
                "069",
                "Qualifications and Certifications",
                "collection",
                "qualifications",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD" },
                new[]
                {
                    Field("qualificationId", "Qualification ID", "text"),
                    Field("userId", "Engineer", "identity", true),
                    Field("name", "Qualification or certification", "text", true),
                    Field("category", "Category", "text"),
                    Field("level", "Level", "text"),
                    Field("issuedOn", "Issued on", "date"),
                    Field("expiresOn", "Expires on", "date"),
                    Field("status", "Status", "select", true, new[] { "active", "expiring", "expired", "planned" }),
                    Field("notes", "Notes", "textarea")
                },
                () => CollectionDocument("qualifications")),
            ["070"] = new(
                "070",
                "Capacity Forecast Scenarios",
                "collection",
                "scenarios",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "MANAGER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT" },
                new[]
                {
                    Field("scenarioId", "Scenario ID", "text"),
                    Field("scenarioName", "Scenario name", "text", true),
                    Field("startDate", "Start date", "date", true),
                    Field("horizonWeeks", "Horizon (weeks)", "number", true, min: 4, max: 52),
                    Field("practice", "Practice", "text"),
                    Field("engineerUserId", "Engineer", "identity"),
                    Field("supplementalDemandHours", "Supplemental demand hours", "number", min: 0, max: 100000),
                    Field("probabilityPercent", "Probability percent", "number", min: 0, max: 100),
                    Field("notes", "Scenario notes", "textarea")
                },
                () => CollectionDocument("scenarios")),
            ["073"] = new(
                "073",
                "Sales Coverage Alignments",
                "collection",
                "alignments",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SOLUTION_ARCHITECT", "PROJECT_TEAM_COORDINATOR" },
                new[]
                {
                    Field("alignmentId", "Alignment ID", "text"),
                    Field("territory", "Territory", "text", true),
                    Field("team", "Team", "text", true),
                    Field("primaryUserId", "Primary owner", "identity", true),
                    Field("backupUserId", "Backup owner", "identity"),
                    Field("effectiveFrom", "Effective from", "date", true),
                    Field("effectiveTo", "Effective to", "date"),
                    Field("status", "Status", "select", true, new[] { "active", "planned", "inactive" }),
                    Field("notes", "Coverage notes", "textarea")
                },
                () => CollectionDocument("alignments")),
            ["074"] = new(
                "074",
                "OEM and Vendor Directory",
                "collection",
                "vendors",
                new[] { "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SOLUTION_ARCHITECT", "PROJECT_TEAM_COORDINATOR" },
                new[]
                {
                    Field("vendorId", "Vendor ID", "text"),
                    Field("name", "Vendor name", "text", true),
                    Field("vendorType", "Vendor type", "text"),
                    Field("status", "Status", "select", true, new[] { "active", "inactive", "prospective" }),
                    Field("website", "Website", "url"),
                    Field("supportUrl", "Support URL", "url"),
                    Field("supportEmail", "Support email", "email"),
                    Field("notes", "Vendor notes", "textarea")
                },
                () => CollectionDocument("vendors"))
        };

    private static readonly HashSet<string> ForbiddenSecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "secretvalue",
        "password",
        "token",
        "accesstoken",
        "refreshtoken",
        "apikey",
        "clientsecret",
        "credential",
        "privatekey",
        "connectionstring",
        "smtpcredential",
        "providersecret"
    };

    public static WebApplication MapModule064074NativeAdministrationEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/native-administration/{moduleNumber}/schema",
            (Func<string, HttpContext, Task<IResult>>)GetSchemaAsync);
        app.MapGet(
            "/api/native-administration/{moduleNumber}/document",
            (Func<string, HttpContext, Task<IResult>>)GetDocumentAsync);
        app.MapPut(
            "/api/native-administration/{moduleNumber}/document",
            (Func<string, HttpContext, Task<IResult>>)SaveDocumentAsync);
        app.MapGet(
            "/api/native-administration/{moduleNumber}/history",
            (Func<string, HttpContext, Task<IResult>>)GetHistoryAsync);
        app.MapPost(
            "/api/native-administration/{moduleNumber}/history/{revisionId:guid}/restore",
            (Func<string, Guid, HttpContext, Task<IResult>>)RestoreRevisionAsync);

        return app;
    }

    private static async Task<IResult> GetSchemaAsync(string moduleNumber, HttpContext context)
    {
        if (!Definitions.TryGetValue(moduleNumber, out var definition))
        {
            return ModuleNotSupported(moduleNumber);
        }

        var access = await ResolveAccessAsync(definition, context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        var identities = definition.Fields.Any(field => field.Type == "identity")
            ? await ReadIdentityOptionsAsync(context)
            : new JsonArray();

        return Results.Ok(new
        {
            module = definition.ModuleNumber,
            moduleName = definition.Title,
            mode = definition.Mode,
            collectionKey = definition.DocumentKey,
            fields = definition.Fields.Select(field => new
            {
                name = field.Name,
                label = field.Label,
                type = field.Type,
                required = field.Required,
                options = field.Options,
                min = field.Min,
                max = field.Max,
                placeholder = field.Placeholder,
                help = field.Help
            }),
            identityOptions = identities,
            access = AccessResponse(access.Context!, context),
            persistence = PersistenceStatus()
        });
    }

    private static async Task<IResult> GetDocumentAsync(string moduleNumber, HttpContext context)
    {
        if (!Definitions.TryGetValue(moduleNumber, out var definition))
        {
            return ModuleNotSupported(moduleNumber);
        }

        var access = await ResolveAccessAsync(definition, context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable(moduleNumber);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    document_json::text,
                    revision_number,
                    updated_by,
                    updated_at
                FROM projectpulse_native_admin_documents
                WHERE module_number = @module_number
                  AND document_key = @document_key;
                """, connection);
            command.Parameters.AddWithValue("module_number", definition.ModuleNumber);
            command.Parameters.AddWithValue("document_key", definition.DocumentKey);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.Ok(new
                {
                    module = definition.ModuleNumber,
                    status = "native_document_default",
                    revision = 0L,
                    document = definition.DefaultDocument(),
                    updatedBy = (Guid?)null,
                    updatedAt = (DateTimeOffset?)null,
                    access = AccessResponse(access.Context!, context),
                    persistence = PersistenceStatus()
                });
            }

            var document = JsonNode.Parse(reader.GetString(0)) as JsonObject
                ?? definition.DefaultDocument();

            return Results.Ok(new
            {
                module = definition.ModuleNumber,
                status = "native_document_loaded",
                revision = reader.GetInt64(1),
                document,
                updatedBy = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime(),
                access = AccessResponse(access.Context!, context),
                persistence = PersistenceStatus()
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, moduleNumber, "load native administration document");
            return DependencyUnavailable(moduleNumber);
        }
    }

    private static async Task<IResult> SaveDocumentAsync(string moduleNumber, HttpContext context)
    {
        if (!Definitions.TryGetValue(moduleNumber, out var definition))
        {
            return ModuleNotSupported(moduleNumber);
        }

        var access = await ResolveAccessAsync(definition, context, requireManage: true);
        if (access.Failure is not null) return access.Failure;

        if (context.Request.ContentLength is > MaximumRequestBytes)
        {
            return Results.BadRequest(new
            {
                module = moduleNumber,
                status = "document_too_large",
                maximumBytes = MaximumRequestBytes
            });
        }

        JsonNode? payload;
        try
        {
            payload = await JsonNode.ParseAsync(context.Request.Body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new
            {
                module = moduleNumber,
                status = "invalid_json",
                message = "A valid JSON request body is required."
            });
        }

        var payloadObject = payload as JsonObject;
        var document = payloadObject?["document"] as JsonObject ?? payloadObject;
        if (document is null)
        {
            return Results.BadRequest(new
            {
                module = moduleNumber,
                status = "document_required"
            });
        }

        long? expectedRevision = null;
        if (payloadObject?["expectedRevision"] is JsonValue revisionValue
            && revisionValue.TryGetValue<long>(out var revision))
        {
            expectedRevision = revision;
        }

        var validation = ValidateDocument(definition, document);
        if (validation is not null) return validation;

        return await PersistDocumentAsync(
            definition,
            document,
            access.Context!.ActualUserId,
            expectedRevision,
            "save",
            null,
            context);
    }

    private static async Task<IResult> GetHistoryAsync(string moduleNumber, HttpContext context)
    {
        if (!Definitions.TryGetValue(moduleNumber, out var definition))
        {
            return ModuleNotSupported(moduleNumber);
        }

        var access = await ResolveAccessAsync(definition, context, requireManage: false);
        if (access.Failure is not null) return access.Failure;

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable(moduleNumber);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    revision_id,
                    revision_number,
                    saved_by,
                    saved_at,
                    change_reason
                FROM projectpulse_native_admin_document_revisions
                WHERE module_number = @module_number
                  AND document_key = @document_key
                ORDER BY revision_number DESC
                LIMIT 100;
                """, connection);
            command.Parameters.AddWithValue("module_number", definition.ModuleNumber);
            command.Parameters.AddWithValue("document_key", definition.DocumentKey);

            var history = new JsonArray();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                history.Add(new JsonObject
                {
                    ["revisionId"] = reader.GetGuid(0).ToString(),
                    ["revision"] = reader.GetInt64(1),
                    ["savedBy"] = reader.IsDBNull(2) ? null : reader.GetGuid(2).ToString(),
                    ["savedAt"] = reader.GetFieldValue<DateTimeOffset>(3)
                        .ToUniversalTime()
                        .ToString("O", CultureInfo.InvariantCulture),
                    ["reason"] = reader.GetString(4)
                });
            }

            return Results.Ok(new
            {
                module = definition.ModuleNumber,
                status = "native_history_loaded",
                canRestore = access.Context!.CanManage && !IsViewAs(context),
                history
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, moduleNumber, "load native administration history");
            return DependencyUnavailable(moduleNumber);
        }
    }

    private static async Task<IResult> RestoreRevisionAsync(
        string moduleNumber,
        Guid revisionId,
        HttpContext context)
    {
        if (!Definitions.TryGetValue(moduleNumber, out var definition))
        {
            return ModuleNotSupported(moduleNumber);
        }

        var access = await ResolveAccessAsync(definition, context, requireManage: true);
        if (access.Failure is not null) return access.Failure;

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable(moduleNumber);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT document_json::text
                FROM projectpulse_native_admin_document_revisions
                WHERE revision_id = @revision_id
                  AND module_number = @module_number
                  AND document_key = @document_key;
                """, connection);
            command.Parameters.AddWithValue("revision_id", revisionId);
            command.Parameters.AddWithValue("module_number", definition.ModuleNumber);
            command.Parameters.AddWithValue("document_key", definition.DocumentKey);

            var raw = await command.ExecuteScalarAsync() as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Results.NotFound(new
                {
                    module = moduleNumber,
                    status = "revision_not_found"
                });
            }

            var document = JsonNode.Parse(raw) as JsonObject;
            if (document is null)
            {
                return Results.Json(new
                {
                    module = moduleNumber,
                    status = "revision_invalid"
                }, statusCode: StatusCodes.Status409Conflict);
            }

            var validation = ValidateDocument(definition, document);
            if (validation is not null) return validation;

            return await PersistDocumentAsync(
                definition,
                document,
                access.Context!.ActualUserId,
                null,
                "restore",
                revisionId,
                context);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, moduleNumber, "restore native administration revision");
            return DependencyUnavailable(moduleNumber);
        }
    }

    private static async Task<IResult> PersistDocumentAsync(
        ModuleDefinition definition,
        JsonObject document,
        Guid actorUserId,
        long? expectedRevision,
        string changeReason,
        Guid? restoredFrom,
        HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable(definition.ModuleNumber);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            long currentRevision = 0;
            await using (var select = new NpgsqlCommand("""
                SELECT revision_number
                FROM projectpulse_native_admin_documents
                WHERE module_number = @module_number
                  AND document_key = @document_key
                FOR UPDATE;
                """, connection, transaction))
            {
                select.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                select.Parameters.AddWithValue("document_key", definition.DocumentKey);
                var value = await select.ExecuteScalarAsync();
                if (value is long storedRevision) currentRevision = storedRevision;
            }

            if (expectedRevision is not null && expectedRevision.Value != currentRevision)
            {
                return Results.Json(new
                {
                    module = definition.ModuleNumber,
                    status = "revision_conflict",
                    expectedRevision,
                    currentRevision,
                    message = "The document changed after it was loaded. Refresh before saving."
                }, statusCode: StatusCodes.Status409Conflict);
            }

            var nextRevision = currentRevision + 1;
            var documentJson = document.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            });

            await using (var upsert = new NpgsqlCommand("""
                INSERT INTO projectpulse_native_admin_documents
                (
                    module_number,
                    document_key,
                    document_json,
                    revision_number,
                    updated_by,
                    updated_at
                )
                VALUES
                (
                    @module_number,
                    @document_key,
                    CAST(@document_json AS jsonb),
                    @revision_number,
                    @updated_by,
                    now()
                )
                ON CONFLICT (module_number, document_key)
                DO UPDATE SET
                    document_json = EXCLUDED.document_json,
                    revision_number = EXCLUDED.revision_number,
                    updated_by = EXCLUDED.updated_by,
                    updated_at = now();
                """, connection, transaction))
            {
                upsert.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                upsert.Parameters.AddWithValue("document_key", definition.DocumentKey);
                upsert.Parameters.AddWithValue("document_json", documentJson);
                upsert.Parameters.AddWithValue("revision_number", nextRevision);
                upsert.Parameters.AddWithValue("updated_by", actorUserId);
                await upsert.ExecuteNonQueryAsync();
            }

            var revisionId = Guid.NewGuid();
            await using (var history = new NpgsqlCommand("""
                INSERT INTO projectpulse_native_admin_document_revisions
                (
                    revision_id,
                    module_number,
                    document_key,
                    revision_number,
                    document_json,
                    saved_by,
                    saved_at,
                    change_reason,
                    restored_from_revision_id
                )
                VALUES
                (
                    @revision_id,
                    @module_number,
                    @document_key,
                    @revision_number,
                    CAST(@document_json AS jsonb),
                    @saved_by,
                    now(),
                    @change_reason,
                    @restored_from
                );
                """, connection, transaction))
            {
                history.Parameters.AddWithValue("revision_id", revisionId);
                history.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                history.Parameters.AddWithValue("document_key", definition.DocumentKey);
                history.Parameters.AddWithValue("revision_number", nextRevision);
                history.Parameters.AddWithValue("document_json", documentJson);
                history.Parameters.AddWithValue("saved_by", actorUserId);
                history.Parameters.AddWithValue("change_reason", changeReason);
                history.Parameters.AddWithValue("restored_from", (object?)restoredFrom ?? DBNull.Value);
                await history.ExecuteNonQueryAsync();
            }

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO projectpulse_module_audit_events
                (
                    event_id,
                    module_number,
                    entity_type,
                    entity_id,
                    action_code,
                    actor_user_id,
                    evidence_json
                )
                VALUES
                (
                    @event_id,
                    @module_number,
                    'native_administration_document',
                    @entity_id,
                    @action_code,
                    @actor_user_id,
                    CAST(@evidence_json AS jsonb)
                );
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("event_id", Guid.NewGuid());
                audit.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                audit.Parameters.AddWithValue("entity_id", definition.DocumentKey);
                audit.Parameters.AddWithValue("action_code", changeReason);
                audit.Parameters.AddWithValue("actor_user_id", actorUserId);
                audit.Parameters.AddWithValue(
                    "evidence_json",
                    new JsonObject
                    {
                        ["revision"] = nextRevision,
                        ["revisionId"] = revisionId.ToString(),
                        ["restoredFrom"] = restoredFrom?.ToString(),
                        ["documentKey"] = definition.DocumentKey
                    }.ToJsonString());
                await audit.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            return Results.Ok(new
            {
                module = definition.ModuleNumber,
                status = changeReason == "restore" ? "native_document_restored" : "native_document_saved",
                revision = nextRevision,
                revisionId,
                restoredFrom,
                document,
                persistence = PersistenceStatus()
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, definition.ModuleNumber, "save native administration document");
            return DependencyUnavailable(definition.ModuleNumber);
        }
    }

    private static IResult? ValidateDocument(ModuleDefinition definition, JsonObject document)
    {
        var forbiddenPath = FindForbiddenSecretPath(document, "$", definition.ModuleNumber);
        if (forbiddenPath is not null)
        {
            return Results.BadRequest(new
            {
                module = definition.ModuleNumber,
                status = "secret_value_field_rejected",
                path = forbiddenPath,
                message = "Usable secrets, passwords, tokens, credentials, and connection strings cannot be stored in this module."
            });
        }

        if (definition.Mode == "configuration")
        {
            if (document[definition.DocumentKey] is not JsonObject configuration)
            {
                return InvalidDocument(definition.ModuleNumber, definition.DocumentKey, "A configuration object is required.");
            }

            return ValidateRecord(definition, configuration, definition.DocumentKey);
        }

        if (document[definition.DocumentKey] is not JsonArray records)
        {
            return InvalidDocument(definition.ModuleNumber, definition.DocumentKey, "A records array is required.");
        }

        if (records.Count > MaximumRecords)
        {
            return Results.BadRequest(new
            {
                module = definition.ModuleNumber,
                status = "record_limit_exceeded",
                maximumRecords = MaximumRecords
            });
        }

        for (var index = 0; index < records.Count; index++)
        {
            if (records[index] is not JsonObject record)
            {
                return InvalidDocument(definition.ModuleNumber, $"{definition.DocumentKey}[{index}]", "Every record must be an object.");
            }

            var failure = ValidateRecord(definition, record, $"{definition.DocumentKey}[{index}]");
            if (failure is not null) return failure;
        }

        return null;
    }

    private static IResult? ValidateRecord(ModuleDefinition definition, JsonObject record, string path)
    {
        var allowed = definition.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in record)
        {
            if (!allowed.Contains(property.Key))
            {
                return InvalidDocument(definition.ModuleNumber, $"{path}.{property.Key}", "The field is not part of the governed module schema.");
            }
        }

        foreach (var field in definition.Fields)
        {
            record.TryGetPropertyValue(field.Name, out var value);
            var failure = ValidateField(definition.ModuleNumber, field, value, $"{path}.{field.Name}");
            if (failure is not null) return failure;
        }

        return null;
    }

    private static IResult? ValidateField(string moduleNumber, FieldDefinition field, JsonNode? value, string path)
    {
        var text = NodeText(value)?.Trim() ?? string.Empty;
        if (field.Required && string.IsNullOrWhiteSpace(text) && field.Type != "checkbox")
        {
            return InvalidDocument(moduleNumber, path, $"{field.Label} is required.");
        }

        if (value is null || (field.Type != "checkbox" && string.IsNullOrWhiteSpace(text)))
        {
            return null;
        }

        switch (field.Type)
        {
            case "checkbox":
                if (value is not JsonValue checkbox || !checkbox.TryGetValue<bool>(out _))
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must be true or false.");
                }
                break;
            case "number":
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must be numeric.");
                }
                if (field.Min is not null && number < field.Min.Value)
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must be at least {field.Min.Value}.");
                }
                if (field.Max is not null && number > field.Max.Value)
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must not exceed {field.Max.Value}.");
                }
                break;
            case "date":
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must use YYYY-MM-DD.");
                }
                break;
            case "identity":
                if (!Guid.TryParse(text, out _))
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must reference a valid ProjectPulse user ID.");
                }
                break;
            case "email":
                if (!text.Contains('@', StringComparison.Ordinal) || text.Length > 320)
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must be a valid email address.");
                }
                break;
            case "url":
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
                    || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must be an HTTPS URL.");
                }
                break;
            case "select":
                if (field.Options is not null && !field.Options.Contains(text, StringComparer.Ordinal))
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} contains an unsupported value.");
                }
                break;
            case "textarea":
                if (text.Length > 20000)
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must not exceed 20,000 characters.");
                }
                break;
            default:
                if (text.Length > 4000)
                {
                    return InvalidDocument(moduleNumber, path, $"{field.Label} must not exceed 4,000 characters.");
                }
                break;
        }

        return null;
    }

    private static string? FindForbiddenSecretPath(JsonNode? node, string path, string moduleNumber)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var normalized = new string(property.Key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                var allowedMetadata = moduleNumber == "065"
                    && normalized is "secretlabel" or "secretpresent" or "secretfingerprint" or "secretowner" or "secretexpireson" or "secretstatus";
                if (!allowedMetadata && ForbiddenSecretPropertyNames.Contains(normalized))
                {
                    return $"{path}.{property.Key}";
                }

                var nested = FindForbiddenSecretPath(property.Value, $"{path}.{property.Key}", moduleNumber);
                if (nested is not null) return nested;
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var nested = FindForbiddenSecretPath(array[index], $"{path}[{index}]", moduleNumber);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(
        ModuleDefinition definition,
        HttpContext context,
        bool requireManage)
    {
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (actualUserId is null || effectiveUserId is null)
        {
            return new(null, Results.Json(new
            {
                module = definition.ModuleNumber,
                status = "session_required"
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, DependencyUnavailable(definition.ModuleNumber));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT upper(COALESCE(r.role_code, ''))
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) roles.Add(reader.GetString(0));

            var canManage = definition.ManageRoles.Any(roles.Contains);
            if (requireManage && IsViewAs(context))
            {
                return new(null, Results.Json(new
                {
                    module = definition.ModuleNumber,
                    status = "actual_session_required",
                    message = "Exit Administrator View-As preview before saving changes."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            if (requireManage && !canManage)
            {
                return new(null, Results.Json(new
                {
                    module = definition.ModuleNumber,
                    status = "native_administration_permission_required",
                    allowedRoles = definition.ManageRoles,
                    message = "Your actual ProjectPulse session does not have management authority for this module."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new AccessContext(actualUserId.Value, effectiveUserId.Value, roles, canManage), null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, definition.ModuleNumber, "authorize native administration access");
            return new(null, DependencyUnavailable(definition.ModuleNumber));
        }
    }

    private static async Task<JsonArray> ReadIdentityOptionsAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return new JsonArray();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    user_id,
                    COALESCE(NULLIF(display_name, ''), email) AS display_name,
                    email,
                    COALESCE(NULLIF(job_title, ''), '') AS job_title,
                    COALESCE(NULLIF(team_name, ''), NULLIF(department_name, ''), NULLIF(department, ''), '') AS team_name
                FROM app_users
                WHERE is_active = TRUE
                  AND COALESCE(login_enabled, TRUE) = TRUE
                ORDER BY display_name, email
                LIMIT 2000;
                """, connection);

            var identities = new JsonArray();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                identities.Add(new JsonObject
                {
                    ["userId"] = reader.GetGuid(0).ToString(),
                    ["displayName"] = reader.GetString(1),
                    ["email"] = reader.GetString(2),
                    ["jobTitle"] = reader.GetString(3),
                    ["teamName"] = reader.GetString(4)
                });
            }

            return identities;
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "shared", "load native administration identity options");
            return new JsonArray();
        }
    }

    private static object AccessResponse(AccessContext access, HttpContext context) => new
    {
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        roles = access.Roles.OrderBy(value => value),
        canView = true,
        canManage = access.CanManage,
        isViewAs = IsViewAs(context),
        authoritySource = "actual ProjectPulse session"
    };

    private static object PersistenceStatus() => new
    {
        mode = "projectpulse_postgresql",
        migration = MigrationFile,
        migrationApplied = false,
        externalSystemActivation = false,
        secretValuesAccepted = false,
        audit = "projectpulse_module_audit_events"
    };

    private static IResult InvalidDocument(string moduleNumber, string path, string message) =>
        Results.BadRequest(new
        {
            module = moduleNumber,
            status = "invalid_native_document",
            path,
            message
        });

    private static IResult ModuleNotSupported(string moduleNumber) =>
        Results.NotFound(new
        {
            module = moduleNumber,
            status = "native_administration_module_not_supported"
        });

    private static IResult DependencyUnavailable(string moduleNumber) =>
        Results.Json(new
        {
            module = moduleNumber,
            status = "native_persistence_unavailable",
            message = "ProjectPulse native administration storage is unavailable. Migration 032 may be pending."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
            && value is bool isViewAs
            && isViewAs)
        {
            return true;
        }

        var actual = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        return actual is not null && effective is not null && actual.Value != effective.Value;
    }

    private static string? NodeText(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text;
            if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
            if (value.TryGetValue<decimal>(out var number)) return number.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        }
        return node.ToJsonString();
    }

    private static FieldDefinition Field(
        string name,
        string label,
        string type,
        bool required = false,
        IReadOnlyList<string>? options = null,
        decimal? min = null,
        decimal? max = null,
        string? placeholder = null,
        string? help = null) =>
        new(name, label, type, required, options, min, max, placeholder, help);

    private static JsonObject ConfigurationDocument(JsonObject configuration) => new()
    {
        ["configuration"] = configuration
    };

    private static JsonObject CollectionDocument(string key) => new()
    {
        [key] = new JsonArray()
    };

    private static void LogFailure(HttpContext context, Exception exception, string moduleNumber, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Module064074NativeAdministration")
            .LogWarning(exception, "Module {ModuleNumber} could not {Operation}.", moduleNumber, operation);
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

    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        IReadOnlySet<string> Roles,
        bool CanManage);
    private sealed record ModuleDefinition(
        string ModuleNumber,
        string Title,
        string Mode,
        string DocumentKey,
        IReadOnlyList<string> ManageRoles,
        IReadOnlyList<FieldDefinition> Fields,
        Func<JsonObject> DefaultDocument);
    private sealed record FieldDefinition(
        string Name,
        string Label,
        string Type,
        bool Required,
        IReadOnlyList<string>? Options,
        decimal? Min,
        decimal? Max,
        string? Placeholder,
        string? Help);
}
