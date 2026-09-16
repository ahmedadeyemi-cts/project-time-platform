using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

internal sealed class Module025GenerationSourceChangedException : Exception;

// Uses the existing private, permission-scoped event store. No phase checkpoint
// becomes the saved draft until the existing atomic final publication succeeds.
internal sealed class Module025GenerationJournal(
    string connectionString, Guid engagementId, Guid actorId, int revision,
    Guid generationId, string sourceHash)
{
    internal async Task<(Dictionary<string, CelarAiComposeResult> Checkpoints, Dictionary<string, int> Attempts)> LoadAsync(CancellationToken token)
    {
        var checkpoints = new Dictionary<string, CelarAiComposeResult>(StringComparer.Ordinal);
        var attempts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("""
            SELECT evidence_json::text FROM module025_sow_gsd_events
            WHERE engagement_id=@engagement_id AND engagement_revision=@revision
              AND event_type='ai_generation_progress'
              AND evidence_json->>'sourceHash'=@source_hash
              AND evidence_json->>'contractVersion'=@contract_version
            ORDER BY event_id;
            """, connection) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("source_hash", sourceHash);
        command.Parameters.AddWithValue("contract_version", Module025GenerationEngine.ContractVersion);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            using var document = JsonDocument.Parse(reader.GetString(0));
            var root = document.RootElement;
            var progress = root.GetProperty("progress").Deserialize<Module025GenerationProgress>();
            if (progress is null || !Module025GenerationEngine.Phases.Contains(progress.Phase)) continue;
            if (progress.Stage == "phase_completed" && progress.Result is not null)
                checkpoints[progress.Phase] = progress.Result;
            if (root.GetProperty("generationId").GetGuid() == generationId && progress.Stage == "provider_started")
                attempts[progress.Phase] = Math.Max(attempts.GetValueOrDefault(progress.Phase), progress.Attempt);
        }
        return (checkpoints, attempts);
    }

    internal async Task PersistAsync(Module025GenerationProgress progress, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        // Abort on edits or archival between phases/attempts. A role change is
        // additionally rechecked by the permission-aware composer on each phase.
        await using (var guard = new NpgsqlCommand("""
            SELECT 1 FROM module025_sow_gsd_engagements
            WHERE engagement_id=@engagement_id AND revision=@revision
              AND is_active=TRUE AND status NOT IN ('archived','confirmed') FOR SHARE;
            """, connection, transaction) { CommandTimeout = 10 })
        {
            guard.Parameters.AddWithValue("engagement_id", engagementId);
            guard.Parameters.AddWithValue("revision", revision);
            if (await guard.ExecuteScalarAsync(token) is null)
                throw new Module025GenerationSourceChangedException();
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO module025_sow_gsd_events
              (engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
            VALUES(@engagement_id,'ai_generation_progress',@actor_id,@revision,
              'Detailed SOW phase generation progress.',@evidence::jsonb);
            """, connection, transaction) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("actor_id", actorId);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new
        {
            generationId, sourceHash, contractVersion = Module025GenerationEngine.ContractVersion,
            progress, recordedAt = DateTimeOffset.UtcNow
        }));
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }
}
