using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

internal static partial class ProjectFlowHiveAiPlannerOrchestrationModule
{
    internal static async Task<object?> ReadPhaseProgressForActorAsync(NpgsqlConnection connection,
        Guid projectId, Guid runId, Guid actual, Guid effective, CancellationToken token)
    {
        var run = await LoadRunAsync(connection, projectId, runId, token);
        if (run is null || run.ActualUserId != actual || run.EffectiveUserId != effective) return null;
        return new { run.RunId, run.CreatedAt, run.CompletedAt, run.DeadlineAt, run.Terminal, run.Status,
            phases = (run.PhaseCheckpoint ?? FlowHiveSequentialState.Empty(run.SourceVersionFingerprint)).Progress(run.Terminal, run.CompletedAt) };
    }

    private static async Task PersistPhaseCheckpointAsync(NpgsqlConnection connection, PlannerRun run,
        FlowHiveSequentialState state, CancellationToken token)
    {
        var payload = JsonSerializer.Serialize(state, Json);
        if (payload.Length > 800000) throw new InvalidOperationException("flowhive_phase_checkpoint_too_large");
        await using var transaction = await connection.BeginTransactionAsync(token);
        var access = await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, run.ActualUserId, run.ProjectId, "066", token);
        var current = await ProjectPlanningDocumentResolver.ReadCurrentAsync(connection, run.ProjectId, token);
        if (run.ActualUserId != run.EffectiveUserId || !access.CanEditPlanner
            || !await AutomaticRunAllowedAsync(connection, run.RunId, run.ActualUserId, run.ProjectId, token)
            || !await ProjectFlowHiveLifecycle.LockActiveAsync(connection, transaction, run.ProjectId, token)
            || !current.ReadyForGeneration
            || ProjectFlowHiveExecutionPolicy.VersionFingerprint(current) != state.SourceFingerprint
            || ProjectFlowHiveExecutionPolicy.SelectionFingerprint(current) != run.SourceSelectionFingerprint)
        {
            await transaction.RollbackAsync(token);
            await StopRunAsync(connection, run.RunId, "phase_authority_changed",
                "The project, permission or source documents changed. Completed phase checkpoints were preserved; no plan was applied.", token);
            throw new OperationCanceledException("FlowHive phase authority changed.", token);
        }
        var completed = state.Phases.Count(p => p.Status == "completed");
        var active = state.Phases.FirstOrDefault(p => p.Status != "completed");
        await using var command = new NpgsqlCommand($"""
            UPDATE {RunTable}
               SET phase_checkpoint=@checkpoint::jsonb, phase=@phase, progress_percent=@progress, updated_at=NOW()
             WHERE run_id=@run AND status IN ('queued','processing','generating')
               AND deadline_at>clock_timestamp() AND source_version_fingerprint=@sources
               AND execution_contract=@contract;
            """, connection, transaction);
        command.Parameters.AddWithValue("checkpoint", payload);
        command.Parameters.AddWithValue("phase", active is null ? "assemble_wbs" : "generate_" + active.Phase.ToLowerInvariant());
        command.Parameters.AddWithValue("progress", 20 + completed * 14);
        command.Parameters.AddWithValue("run", run.RunId);
        command.Parameters.AddWithValue("sources", state.SourceFingerprint);
        command.Parameters.AddWithValue("contract", ProjectFlowHiveExecutionPolicy.Contract);
        if (await command.ExecuteNonQueryAsync(token) != 1)
            throw new OperationCanceledException("The FlowHive run stopped before its phase checkpoint could commit.", token);
        await transaction.CommitAsync(token);
    }
}
