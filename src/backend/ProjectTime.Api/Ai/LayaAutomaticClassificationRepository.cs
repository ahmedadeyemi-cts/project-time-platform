using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Durable Module 064 classification queue. This repository is intentionally
/// separate from the document-processing queue: scan/extraction/index success
/// remains authoritative even when Laya is disabled or unavailable.
/// </summary>
public sealed class LayaAutomaticClassificationRepository
{
    public async Task<LayaClassificationJob?> EnqueueIfPermittedAsync(
        PulseAiPrivateRuntimeOptions options,
        Guid documentId,
        Guid? projectId,
        Guid versionId,
        string sourceSha256,
        CancellationToken cancellationToken = default)
    {
        if (options.DocumentServicePrincipalUserId is not Guid servicePrincipal
            || !LayaProcessedSourceReader.IsHash(sourceSha256))
            return null;

        // Migration 125 and the existing Module 064 settings table are applied
        // by the release migration job. Do not enter a transaction and then
        // continue after a missing-table error: PostgreSQL marks that
        // transaction aborted. On an older database, leave the document's
        // ordinary processing result authoritative and let the next release
        // retry admission after the additive schema is present.
        if (!await HasSchemaAsync(cancellationToken)) return null;

        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var policy = await ReadPolicyAsync(connection, transaction, cancellationToken);
            if (policy is null)
            {
                await UpdateDocumentStatusAsync(connection, transaction, documentId, "paused", 0,
                    string.Empty, "laya_schema_unavailable", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            if (!policy.Value.Enabled)
            {
                await UpdateDocumentStatusAsync(connection, transaction, documentId, "paused", policy.Value.Version,
                    LayaDecisionContract.Revision, "laya_policy_disabled", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            const string sql = """
                INSERT INTO pulse_ai_laya_classification_jobs(
                    project_intake_document_id, project_id, document_version_id,
                    source_sha256, policy_version, model_revision,
                    service_principal_user_id, job_status, maximum_attempts,
                    correlation_id, request_id)
                SELECT @document, @project, @version, @source, @policy, @model,
                       @service, 'queued', @maximum_attempts, @correlation, gen_random_uuid()
                WHERE EXISTS(
                    SELECT 1
                    FROM project_intake_documents d
                    JOIN pulse_ai_document_versions v
                      ON v.pulse_ai_document_version_id = d.pulse_ai_active_version_id
                     AND v.pulse_ai_document_version_id = @version
                     AND v.source_sha256 = @source
                    WHERE d.project_intake_document_id = @document
                      AND d.is_active = TRUE
                      AND d.pulse_ai_processing_status = 'ready')
                ON CONFLICT DO NOTHING
                RETURNING pulse_ai_laya_classification_job_id;
                """;
            Guid? jobId;
            await using (var command = new NpgsqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("document", documentId);
                command.Parameters.AddWithValue("project", projectId is null ? DBNull.Value : projectId.Value);
                command.Parameters.AddWithValue("version", versionId);
                command.Parameters.AddWithValue("source", sourceSha256);
                command.Parameters.AddWithValue("policy", policy.Value.Version);
                command.Parameters.AddWithValue("model", LayaDecisionContract.Revision);
                command.Parameters.AddWithValue("service", servicePrincipal);
                command.Parameters.AddWithValue("maximum_attempts", options.MaximumAttempts);
                command.Parameters.AddWithValue("correlation", $"laya-auto-{Guid.NewGuid():N}");
                var value = await command.ExecuteScalarAsync(cancellationToken);
                jobId = value is Guid queuedJobId ? queuedJobId : null;
            }

            if (jobId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            await UpdateDocumentStatusAsync(connection, transaction, documentId, "queued", policy.Value.Version,
                LayaDecisionContract.Revision, string.Empty, cancellationToken);
            await InsertEventAsync(connection, transaction, documentId, projectId, servicePrincipal,
                "laya_classification_queued", "requested", "laya-auto", string.Empty,
                new { policyVersion = policy.Value.Version, modelRevision = LayaDecisionContract.Revision,
                    sourceVersionId = versionId, rawDocumentTextLogged = false }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(jobId.Value, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<LayaClassificationJob?> ClaimNextAsync(
        PulseAiPrivateRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await CancelReplacedJobsAsync(connection, transaction, cancellationToken);
        await PauseDisabledJobsAsync(connection, transaction, cancellationToken);
        await RecoverExpiredLeasesAsync(connection, transaction, cancellationToken);
        const string selectSql = """
            SELECT pulse_ai_laya_classification_job_id
            FROM pulse_ai_laya_classification_jobs job
            JOIN project_intake_documents document
              ON document.project_intake_document_id=job.project_intake_document_id
             AND document.is_active=TRUE
             AND document.pulse_ai_active_version_id=job.document_version_id
            JOIN celar_laya_settings policy ON policy.singleton=TRUE AND policy.enabled=TRUE
            WHERE job.job_status IN ('queued','retry_wait')
              AND job.available_at <= NOW()
              AND job.attempt_count < job.maximum_attempts
              AND (job.lease_expires_at IS NULL OR job.lease_expires_at < NOW())
            ORDER BY job.requested_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1;
            """;
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        var value = await select.ExecuteScalarAsync(cancellationToken);
        if (value is not Guid jobId)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        const string updateSql = """
            UPDATE pulse_ai_laya_classification_jobs
            SET job_status='running', attempt_count=attempt_count+1,
                started_at=COALESCE(started_at,NOW()), lease_owner=@owner,
                lease_token=@token, lease_generation=lease_generation+1,
                lease_heartbeat_at=NOW(), lease_expires_at=NOW()+(@seconds * INTERVAL '1 second'),
                diagnostic_code='', diagnostic_message=''
            WHERE pulse_ai_laya_classification_job_id=@job_id;
            """;
        await using var update = new NpgsqlCommand(updateSql, connection, transaction);
        update.Parameters.AddWithValue("owner", options.WorkerIdentity);
        update.Parameters.AddWithValue("token", Guid.NewGuid());
        update.Parameters.AddWithValue("seconds", options.LeaseSeconds);
        update.Parameters.AddWithValue("job_id", jobId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(jobId, cancellationToken);
    }

    public async Task<bool> RenewLeaseAsync(LayaClassificationJob job, int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        if (job.LeaseToken is null) return false;
        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_laya_classification_jobs
            SET lease_heartbeat_at=NOW(), lease_expires_at=NOW()+(@seconds * INTERVAL '1 second')
            WHERE pulse_ai_laya_classification_job_id=@job_id AND lease_owner=@owner
              AND lease_token=@token AND lease_generation=@generation AND lease_expires_at > NOW()
              AND job_status='running';
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("seconds", leaseSeconds);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("owner", job.LeaseOwner);
        command.Parameters.AddWithValue("token", job.LeaseToken.Value);
        command.Parameters.AddWithValue("generation", job.LeaseGeneration);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task CompleteAsync(
        LayaClassificationJob job,
        string status,
        string diagnosticCode,
        string diagnosticMessage,
        object evidence,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var terminal = status is "succeeded" or "failed" or "cancelled";
        const string sql = """
            UPDATE pulse_ai_laya_classification_jobs
            SET job_status=@status,
                completed_at=CASE WHEN @terminal THEN NOW() ELSE completed_at END,
                available_at=CASE WHEN @status='retry_wait' THEN NOW()+INTERVAL '15 minutes' ELSE available_at END,
                lease_owner='', lease_token=NULL, lease_heartbeat_at=NULL, lease_expires_at=NULL,
                diagnostic_code=@code, diagnostic_message=@message, evidence_json=@evidence::jsonb
            WHERE pulse_ai_laya_classification_job_id=@job_id AND lease_owner=@owner
              AND lease_token=@token AND lease_generation=@generation AND lease_expires_at > NOW();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("terminal", terminal);
        command.Parameters.AddWithValue("code", diagnosticCode);
        command.Parameters.AddWithValue("message", diagnosticMessage);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidence));
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("owner", job.LeaseOwner);
        command.Parameters.AddWithValue("token", job.LeaseToken is null ? DBNull.Value : job.LeaseToken.Value);
        command.Parameters.AddWithValue("generation", job.LeaseGeneration);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The Laya classification worker lost its fenced lease.");
        await UpdateDocumentStatusAsync(connection, transaction, job.DocumentId, status switch
        {
            "succeeded" => "succeeded",
            "retry_wait" => "retry_wait",
            "paused" => "paused",
            "cancelled" => "cancelled",
            _ => "failed"
        }, job.PolicyVersion, job.ModelRevision, diagnosticCode, cancellationToken);
        var eventStatus = status switch
        {
            "retry_wait" => "partial",
            "paused" => "blocked",
            _ => status
        };
        await InsertEventAsync(connection, transaction, job.DocumentId, job.ProjectId,
            job.ServicePrincipalUserId, "laya_classification_completed", eventStatus, job.CorrelationId,
            diagnosticCode, evidence, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveDecisionAsync(
        LayaClassificationJob job,
        LayaProcessedSource processed,
        JsonObject answer,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var policy = await ReadPolicyAsync(connection, transaction, cancellationToken);
        if (policy is null || !policy.Value.Enabled || policy.Value.Version != job.PolicyVersion)
            throw new LayaDecisionFailure("laya_policy_changed", 409);
        if (!await LayaProcessedSourceReader.LockCurrentVersionAsync(connection, transaction, processed, cancellationToken))
            throw new LayaDecisionFailure("laya_source_changed", 409);

        answer["sourceVersionId"] = processed.VersionId!.Value.ToString();
        answer["sourceEvidenceContract"] = LayaProcessedSourceReader.ContractVersion;
        answer["automaticClassification"] = true;
        answer["reviewRequired"] = true;
        answer["automationApproved"] = false;
        answer["workflowActionsPerformed"] = 0;
        const string save = """
            INSERT INTO celar_laya_decisions(
                decision_id, document_id, source_sha256, request_id, created_by,
                policy_version, evidence)
            VALUES(@id,@document,@source,@request,@actor,@policy,@evidence::jsonb)
            ON CONFLICT(document_id, created_by, request_id) DO NOTHING;
            """;
        await using (var command = new NpgsqlCommand(save, connection, transaction))
        {
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("document", job.DocumentId);
            command.Parameters.AddWithValue("source", processed.SourceSha256);
            command.Parameters.AddWithValue("request", job.RequestId);
            command.Parameters.AddWithValue("actor", job.ServicePrincipalUserId);
            command.Parameters.AddWithValue("policy", job.PolicyVersion);
            command.Parameters.AddWithValue("evidence", answer.ToJsonString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await UpdateDocumentStatusAsync(connection, transaction, job.DocumentId, "succeeded",
            job.PolicyVersion, job.ModelRevision, string.Empty, cancellationToken);
        await UpdateJobSucceededAsync(connection, transaction, job, answer, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<LayaClassificationJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT pulse_ai_laya_classification_job_id, project_intake_document_id, project_id,
                   document_version_id, source_sha256, policy_version, model_revision,
                   service_principal_user_id, job_status, attempt_count, maximum_attempts,
                   lease_owner, lease_token, lease_generation, lease_expires_at,
                   correlation_id, request_id, diagnostic_code, diagnostic_message
            FROM pulse_ai_laya_classification_jobs
            WHERE pulse_ai_laya_classification_job_id=@job_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static async Task<(bool Enabled, long Version)?> ReadPolicyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT enabled, version FROM celar_laya_settings WHERE singleton=TRUE FOR UPDATE",
            connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetBoolean(0), reader.GetInt64(1)) : null;
    }

    private static async Task<bool> HasSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT to_regclass('public.celar_laya_settings') IS NOT NULL
               AND to_regclass('public.pulse_ai_laya_classification_jobs') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='project_intake_documents'
                     AND column_name='pulse_ai_laya_classification_status');
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task UpdateDocumentStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid documentId,
        string status, long policyVersion, string modelRevision, string diagnosticCode,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE project_intake_documents
            SET pulse_ai_laya_classification_status=@status,
                pulse_ai_laya_policy_version=@policy,
                pulse_ai_laya_model_revision=@model,
                pulse_ai_laya_error_code=@code,
                pulse_ai_laya_updated_at=NOW()
            WHERE project_intake_document_id=@document AND is_active=TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("policy", policyVersion);
        command.Parameters.AddWithValue("model", modelRevision);
        command.Parameters.AddWithValue("code", diagnosticCode);
        command.Parameters.AddWithValue("document", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateJobSucceededAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LayaClassificationJob job,
        JsonObject evidence, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE pulse_ai_laya_classification_jobs
            SET job_status='succeeded', completed_at=NOW(), lease_owner='', lease_token=NULL,
                lease_heartbeat_at=NULL, lease_expires_at=NULL, diagnostic_code='', diagnostic_message='',
                evidence_json=@evidence::jsonb
            WHERE pulse_ai_laya_classification_job_id=@job_id AND lease_owner=@owner
              AND lease_token=@token AND lease_generation=@generation AND lease_expires_at > NOW();
            """, connection, transaction);
        command.Parameters.AddWithValue("evidence", evidence.ToJsonString());
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("owner", job.LeaseOwner);
        command.Parameters.AddWithValue("token", job.LeaseToken!.Value);
        command.Parameters.AddWithValue("generation", job.LeaseGeneration);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The Laya classification worker lost its fenced lease.");
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid documentId, Guid? projectId,
        Guid actor, string eventCode, string eventStatus, string correlationId, string diagnosticCode,
        object evidence, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO pulse_ai_document_processing_events(
                project_intake_document_id, project_id, actual_user_id, effective_user_id,
                event_code, event_status, correlation_id, diagnostic_code, evidence_json)
            VALUES(@document,@project,@actor,@actor,@event,@status,@correlation,@code,@evidence::jsonb);
            """, connection, transaction);
        command.Parameters.AddWithValue("document", documentId);
        command.Parameters.AddWithValue("project", projectId is null ? DBNull.Value : projectId.Value);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("event", eventCode);
        command.Parameters.AddWithValue("status", eventStatus);
        command.Parameters.AddWithValue("correlation", correlationId);
        command.Parameters.AddWithValue("code", diagnosticCode);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidence));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecoverExpiredLeasesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE pulse_ai_laya_classification_jobs
            SET job_status=CASE WHEN attempt_count >= maximum_attempts THEN 'failed' ELSE 'retry_wait' END,
                completed_at=CASE WHEN attempt_count >= maximum_attempts THEN NOW() ELSE NULL END,
                available_at=NOW(), lease_owner='', lease_token=NULL, lease_heartbeat_at=NULL,
                lease_expires_at=NULL, diagnostic_code='expired_laya_worker_lease',
                diagnostic_message='The prior Laya worker lease expired; bounded recovery was applied.'
            WHERE job_status='running' AND lease_expires_at IS NOT NULL AND lease_expires_at < NOW();
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CancelReplacedJobsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH cancelled AS (
                UPDATE pulse_ai_laya_classification_jobs job
                   SET job_status='cancelled', completed_at=COALESCE(completed_at,NOW()),
                       lease_owner='', lease_token=NULL, lease_heartbeat_at=NULL, lease_expires_at=NULL,
                       diagnostic_code='laya_source_replaced',
                       diagnostic_message='The document was replaced or revoked before automatic classification ran.'
                 WHERE job.job_status IN ('queued','retry_wait')
                   AND NOT EXISTS (
                       SELECT 1 FROM project_intake_documents document
                       WHERE document.project_intake_document_id=job.project_intake_document_id
                         AND document.is_active=TRUE
                         AND document.pulse_ai_active_version_id=job.document_version_id)
                 RETURNING job.project_intake_document_id, job.project_id,
                           job.service_principal_user_id, job.correlation_id
            )
            INSERT INTO pulse_ai_document_processing_events(
                project_intake_document_id, project_id, actual_user_id, effective_user_id,
                event_code, event_status, correlation_id, diagnostic_code, evidence_json)
            SELECT project_intake_document_id, project_id, service_principal_user_id,
                   service_principal_user_id, 'laya_classification_cancelled', 'cancelled',
                   correlation_id, 'laya_source_replaced',
                   '{"sourceChanged":true,"rawDocumentTextLogged":false}'::jsonb
              FROM cancelled;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PauseDisabledJobsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH paused AS (
                UPDATE pulse_ai_laya_classification_jobs job
                   SET job_status='paused', completed_at=NULL,
                       lease_owner='', lease_token=NULL, lease_heartbeat_at=NULL, lease_expires_at=NULL,
                       diagnostic_code='laya_policy_disabled',
                       diagnostic_message='Module 064 automatic classification is paused while policy is disabled.'
                  FROM celar_laya_settings policy
                 WHERE policy.singleton=TRUE AND policy.enabled=FALSE
                   AND job.job_status IN ('queued','retry_wait')
                 RETURNING job.project_intake_document_id, job.project_id,
                           job.service_principal_user_id, job.correlation_id,
                           job.policy_version, job.model_revision
            ), documents AS (
                UPDATE project_intake_documents document
                   SET pulse_ai_laya_classification_status='paused',
                       pulse_ai_laya_error_code='laya_policy_disabled',
                       pulse_ai_laya_updated_at=NOW()
                  FROM paused
                 WHERE document.project_intake_document_id=paused.project_intake_document_id
                   AND document.is_active=TRUE
                RETURNING document.project_intake_document_id
            )
            INSERT INTO pulse_ai_document_processing_events(
                project_intake_document_id, project_id, actual_user_id, effective_user_id,
                event_code, event_status, correlation_id, diagnostic_code, evidence_json)
            SELECT paused.project_intake_document_id, paused.project_id,
                   paused.service_principal_user_id, paused.service_principal_user_id,
                   'laya_classification_paused', 'blocked', paused.correlation_id,
                   'laya_policy_disabled', jsonb_build_object(
                       'policyVersion', paused.policy_version,
                       'modelRevision', paused.model_revision,
                       'rawDocumentTextLogged', false)
              FROM paused;
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LayaClassificationJob Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4), reader.GetInt64(5),
        reader.GetString(6), reader.GetGuid(7), reader.GetString(8), reader.GetInt32(9),
        reader.GetInt32(10), reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetGuid(12),
        reader.GetInt64(13), reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
        reader.GetString(15), reader.GetGuid(16), reader.GetString(17), reader.GetString(18));
}

public sealed record LayaClassificationJob(
    Guid JobId,
    Guid DocumentId,
    Guid? ProjectId,
    Guid? VersionId,
    string SourceSha256,
    long PolicyVersion,
    string ModelRevision,
    Guid ServicePrincipalUserId,
    string Status,
    int AttemptCount,
    int MaximumAttempts,
    string LeaseOwner,
    Guid? LeaseToken,
    long LeaseGeneration,
    DateTimeOffset? LeaseExpiresAt,
    string CorrelationId,
    Guid RequestId,
    string DiagnosticCode,
    string DiagnosticMessage);
