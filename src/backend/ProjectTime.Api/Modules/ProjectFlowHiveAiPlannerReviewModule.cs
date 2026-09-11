using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

internal static partial class ProjectFlowHiveAiPlannerOrchestrationModule
{
    private static void MapReviewEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/project-flowhive/projects/{projectId:guid}/ai-planner/runs/{runId:guid}/review",
            (Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)GetReviewAsync);
        endpoints.MapPost("/api/project-flowhive/projects/{projectId:guid}/ai-planner/runs/{runId:guid}/review-preview",
            (Guid projectId, Guid runId, ProjectFlowHivePlannerReviewRequest request, HttpContext context, CancellationToken token) =>
                ReviewAsync(projectId, runId, request, false, context, token));
        endpoints.MapPost("/api/project-flowhive/projects/{projectId:guid}/ai-planner/runs/{runId:guid}/apply-reviewed",
            (Guid projectId, Guid runId, ProjectFlowHivePlannerReviewRequest request, HttpContext context, CancellationToken token) =>
                ReviewAsync(projectId, runId, request, true, context, token));
    }

    private static bool Reviewable(PlannerRun run) => run.Terminal && run.GeneratedPlan is not null &&
        run.Schedule?.Valid == true && run.Validation?.Valid == true &&
        (run.Phase is "candidate_review_required" or "working_copy_changed") && run.SavedWorkingRowVersion is null;

    private static async Task<bool> ReviewSourceIsCurrentAsync(NpgsqlConnection connection, PlannerRun run,
        HttpContext context, CancellationToken token)
    {
        if (run.ActualUserId != run.EffectiveUserId) return false;
        var authority = await ProjectPlanningAccessResolver.ResolveForActorAsync(connection, run.ActualUserId, run.ProjectId, "066", token);
        if (!authority.CanEditPlanner) return false;
        var access = await context.RequestServices.GetRequiredService<PulseAiPrivateRagService>().LoadAccessAsync(run.EffectiveUserId, token);
        var source = await ProjectPlanningDocumentResolver.ReadCurrentAsync(connection, run.ProjectId, token);
        return access.IsActive && access.CanFlowHive && source.ReadyForGeneration &&
            source.SelectedDocuments.All(d => d.EngineeringVisible) &&
            ProjectFlowHiveExecutionPolicy.SelectionFingerprint(source) == run.SourceSelectionFingerprint &&
            ProjectFlowHiveExecutionPolicy.VersionFingerprint(source) == run.SourceVersionFingerprint;
    }

    private static async Task<(ProjectFlowHivePlanRequest Plan, Guid? RowVersion)> LoadReviewBaseAsync(
        NpgsqlConnection connection, PlannerRun run, bool forUpdate, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT working_payload::text,row_version FROM project_flowhive_working_copies WHERE project_id=@project" + (forUpdate ? " FOR UPDATE;" : ";"), connection);
        command.Parameters.AddWithValue("project", run.ProjectId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return (run.Plan!, null);
        var plan = Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(0));
        if (plan?.ProjectId != run.ProjectId) throw new InvalidOperationException("The saved working copy has invalid project identity.");
        return (plan, reader.GetGuid(1));
    }

    private static IResult ReviewConflict(string code, string message) => Results.Conflict(new { status = code, message, stateChanged = false });

    private static async Task<IResult> GetReviewAsync(Guid projectId, Guid runId, HttpContext context, CancellationToken token)
    {
        var opened = await OpenAsync(projectId, context, requireEdit: true, token);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var run = await LoadRunAsync(connection, projectId, runId, token);
        if (run is null || run.ActualUserId != opened.Access!.ActualUserId || run.EffectiveUserId != opened.Access.EffectiveUserId)
            return Results.NotFound(new { status = "flowhive_ai_planner_run_not_found" });
        if (!Reviewable(run)) return ReviewConflict("candidate_not_reviewable", "This operation has no unapplied candidate to review.");
        if (!await ReviewSourceIsCurrentAsync(connection, run, context, token))
            return ReviewConflict("flowhive_source_access_changed", "The candidate's source evidence is no longer current or authorized.");
        var current = await LoadReviewBaseAsync(connection, run, false, token);
        return Results.Ok(new { projectId, runId, contract = ProjectFlowHivePlannerReview.Contract,
            expectedWorkingRowVersion = current.RowVersion, currentPlan = current.Plan,
            candidatePlan = run.GeneratedPlan, candidateSchedule = run.Schedule,
            candidateValidation = run.Validation, workingCopyChangedSinceGeneration = current.RowVersion != run.ExpectedWorkingRowVersion,
            stateChanged = false });
    }

    private static async Task<IResult> ReviewAsync(Guid projectId, Guid runId, ProjectFlowHivePlannerReviewRequest request,
        bool apply, HttpContext context, CancellationToken token)
    {
        var note = request.ReviewNote?.Trim() ?? "";
        if (note.Length is < 10 or > 4000 || request.Decisions is null || request.Decisions.Count > 500 ||
            request.Decisions.Any(d => d is null || string.IsNullOrWhiteSpace(d.ExistingWbs)))
            return Validation("A review note of 10–4,000 characters and explicit decisions for existing work are required.");
        var opened = await OpenAsync(projectId, context, requireEdit: true, token);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s';", connection, transaction))
            await timeout.ExecuteNonQueryAsync(token);
        await using (var schema = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@id);", connection, transaction))
        {
            schema.Parameters.AddWithValue("id", ProjectFlowHivePlannerReview.Migration);
            if (await schema.ExecuteScalarAsync(token) is not true)
                return Results.Json(new { status = "flowhive_review_migration_required", message = "Reviewed regeneration is not enabled until its audit migration is verified.", stateChanged = false }, statusCode: 503);
        }
        await using (var guard = new NpgsqlCommand($"SELECT run_id FROM {RunTable} WHERE project_id=@project AND run_id=@run FOR UPDATE;", connection, transaction))
        {
            guard.Parameters.AddWithValue("project", projectId); guard.Parameters.AddWithValue("run", runId);
            if (await guard.ExecuteScalarAsync(token) is not Guid) return Results.NotFound();
        }
        var run = (await LoadRunAsync(connection, projectId, runId, token))!;
        if (run.ActualUserId != opened.Access!.ActualUserId || run.EffectiveUserId != opened.Access.EffectiveUserId)
            return Results.NotFound(new { status = "flowhive_ai_planner_run_not_found" });
        if (!await ReviewSourceIsCurrentAsync(connection, run, context, token))
            return ReviewConflict("flowhive_source_access_changed", "The candidate's source evidence is no longer current or authorized.");
        // Exactly-once replay: only the identical reviewed request may receive the
        // original receipt. No model invocation and no second working-copy save.
        if (apply)
        {
            await using var receipt = new NpgsqlCommand("SELECT preview_fingerprint,decisions::text,review_note,expected_row_version FROM project_flowhive_ai_plan_reviews WHERE run_id=@run;", connection, transaction);
            receipt.Parameters.AddWithValue("run", runId);
            await using var reader = await receipt.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var priorDecisions = Deserialize<ProjectFlowHiveExistingTaskDecision[]>(reader.GetString(1)) ?? [];
                var same = ProjectFlowHivePlannerReview.MatchesAppliedReview(reader.GetString(0), reader.GetString(2),
                    reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3), priorDecisions, request);
                if (!same) return ReviewConflict("review_already_applied", "This candidate was already applied with a different reviewed decision.");
                await reader.DisposeAsync();
                await transaction.CommitAsync(token);
                return RunResult(run);
            }
        }
        if (!Reviewable(run)) return ReviewConflict("candidate_not_reviewable", "This operation has no unapplied candidate to review.");
        var current = await LoadReviewBaseAsync(connection, run, true, token);
        if (current.RowVersion != request.ExpectedWorkingRowVersion)
            return ReviewConflict("working_copy_version_conflict", "The working copy changed. Reload the review and preview its merge again; no existing work was replaced.");
        ProjectFlowHivePlanRequest merged;
        try { merged = ProjectFlowHivePlannerReview.Merge(runId, current.Plan, run.GeneratedPlan!, request.Decisions); }
        catch (InvalidOperationException exception) { return Validation(exception.Message); }
        var validation = ProjectFlowHiveScheduleEngine.Validate(merged);
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(merged);
        var fingerprint = ProjectFlowHivePlannerReview.Fingerprint(runId, current.RowVersion, merged, request.Decisions, note);
        if (!apply)
        {
            await transaction.CommitAsync(token);
            return Results.Ok(new { projectId, runId, plan = merged, schedule, validation, previewFingerprint = fingerprint,
                expectedWorkingRowVersion = current.RowVersion,
                reviewSummary = new { existingTaskCount = current.Plan.Tasks?.Count(t => !t.IsSummary) ?? 0,
                    mappedTaskCount = request.Decisions.Count(d => d.CandidateWbs is not null),
                    retainedTaskCount = request.Decisions.Count(d => d.CandidateWbs is null),
                    preservedMilestoneCount = current.Plan.Milestones?.Count ?? 0,
                    previousPlannedHours = (current.Plan.Assignments ?? []).Sum(a => a.PlannedHours), plannedHours = schedule.PlannedHours },
                stateChanged = false });
        }
        if (request.PreviewFingerprint != fingerprint)
            return ReviewConflict("review_preview_required", "Preview this exact merge and note before applying it. The candidate or decisions do not match the reviewed preview.");
        // Recheck effective project permission and source authority after preview
        // calculation, immediately before any mutable working-copy write.
        if (!await ReviewSourceIsCurrentAsync(connection, run, context, token))
            return ReviewConflict("flowhive_source_access_changed", "Permission or evidence changed during review; the current working plan was not replaced.");
        await CommitReviewedCandidateAsync(connection, transaction, runId, projectId, opened.Access.ActualUserId,
            current.Plan, run.GeneratedPlan!, merged, request, note, fingerprint, validation, schedule, token);
        await transaction.CommitAsync(token);
        return RunResult((await LoadRunAsync(connection, projectId, runId, token))!);
    }

    // Kept as a single independently exercised transaction boundary: the updated
    // working copy, immutable before/after audit and saved run receipt all succeed
    // or all roll back. Human review is not a restart/extension of AI inference.
    private static async Task CommitReviewedCandidateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid runId, Guid projectId, Guid actor, ProjectFlowHivePlanRequest prior, ProjectFlowHivePlanRequest candidate,
        ProjectFlowHivePlanRequest merged, ProjectFlowHivePlannerReviewRequest request, string note, string fingerprint,
        ProjectFlowHivePlanValidationResult validation, ProjectFlowHiveScheduleResult schedule, CancellationToken token)
    {
        var saved = await SaveWorkingCopyAsync(connection, transaction, projectId, merged, actor,
            request.ExpectedWorkingRowVersion, validation, schedule, token);
        if (saved is null) throw new InvalidOperationException("The working copy changed before the reviewed save.");
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO project_flowhive_ai_plan_reviews(run_id,project_id,actor_user_id,expected_row_version,
                applied_row_version,applied_revision,preview_fingerprint,prior_plan,candidate_plan,applied_plan,decisions,review_note)
            VALUES(@run,@project,@actor,@expected,@row,@revision,@fingerprint,@prior::jsonb,@candidate::jsonb,@applied::jsonb,@decisions::jsonb,@note);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("run", runId); audit.Parameters.AddWithValue("project", projectId); audit.Parameters.AddWithValue("actor", actor);
            audit.Parameters.Add("expected", NpgsqlDbType.Uuid).Value = (object?)request.ExpectedWorkingRowVersion ?? DBNull.Value;
            audit.Parameters.AddWithValue("row", saved.RowVersion); audit.Parameters.AddWithValue("revision", saved.WorkingRevision);
            audit.Parameters.AddWithValue("fingerprint", fingerprint); audit.Parameters.AddWithValue("prior", JsonSerializer.Serialize(prior, Json));
            audit.Parameters.AddWithValue("candidate", JsonSerializer.Serialize(candidate, Json)); audit.Parameters.AddWithValue("applied", JsonSerializer.Serialize(merged, Json));
            audit.Parameters.AddWithValue("decisions", JsonSerializer.Serialize(request.Decisions, Json)); audit.Parameters.AddWithValue("note", note);
            await audit.ExecuteNonQueryAsync(token);
        }
        await using var complete = new NpgsqlCommand($"""
            UPDATE {RunTable} SET phase='working_draft_ready',status=@status,saved_working_row_version=@row,
                saved_working_revision=@revision,generated_plan=@plan::jsonb,schedule_payload=@schedule::jsonb,
                validation_payload=@validation::jsonb,progress_percent=100,blockers='[]'::jsonb,updated_at=NOW(),row_version=gen_random_uuid(),
                operation_logs=operation_logs || '["An authorized PM previewed and applied the milestone-preserving merge; immutable review evidence and the working-copy receipt were committed together."]'::jsonb
            WHERE run_id=@run AND project_id=@project AND actual_actor_user_id=@actor AND effective_actor_user_id=@actor
                AND phase IN ('candidate_review_required','working_copy_changed') AND completed_at IS NOT NULL
                AND saved_working_row_version IS NULL;
            """, connection, transaction);
        complete.Parameters.AddWithValue("run", runId); complete.Parameters.AddWithValue("project", projectId); complete.Parameters.AddWithValue("actor", actor);
        complete.Parameters.AddWithValue("status", FinalStatus(schedule)); complete.Parameters.AddWithValue("row", saved.RowVersion);
        complete.Parameters.AddWithValue("revision", saved.WorkingRevision); complete.Parameters.AddWithValue("plan", JsonSerializer.Serialize(merged, Json));
        complete.Parameters.AddWithValue("schedule", JsonSerializer.Serialize(schedule, Json)); complete.Parameters.AddWithValue("validation", JsonSerializer.Serialize(validation, Json));
        if (await complete.ExecuteNonQueryAsync(token) != 1) throw new InvalidOperationException("The candidate is no longer available for this reviewed save.");
    }
}
