using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// One source-backed planning engine for FlowHive and Project Forge. The private
/// project documents are resolved before this service is called. This service
/// enforces current-document citations, phase-native executable WBS assembly, and
/// schedule calculation without silently compressing effort or duration.
/// </summary>
internal static class ProjectPlanningAiOrchestrator
{
    internal const string Contract = "project-planning-ai-orchestrator-v2-20260906";
    private const string DurableRunTable = "project_flowhive_ai_planner_runs";
    private static readonly JsonSerializerOptions DurablePlannerJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<ProjectPlanningGenerationResult> GenerateAsync(
        CelarAiEnterprisePlatformService enterprise,
        Guid actualUserId,
        Guid effectiveUserId,
        ProjectFlowHivePlanRequest seed,
        ProjectPlanningDocumentResolution documents,
        string? requestedOutcome,
        string? detailLevel,
        string capabilityCode,
        bool allowSanitizedExternalFallback,
        HttpContext context,
        CancellationToken cancellationToken,
        FlowHiveSequentialExecution? sequential = null, Guid? observedRunId = null)
    {
        if (!documents.ReadyForGeneration)
        {
            return ProjectPlanningGenerationResult.NotReady(
                documents.Blockers,
                documents.Warnings,
                "The current project SOW and supporting documents are not ready for source-grounded plan generation.");
        }

        var outcome = Clean(
            requestedOutcome,
            4_000,
            "Create a complete source-backed project planning draft. Return distinct, project-specific executable tasks in Plan, Design, Implement, Validate, and Release. Assign each task to its own phase exactly once; never repeat every work package across all five phases. Include detailed steps, products, platforms, versions, licensing, quantities, tools, systems, interfaces, access, inputs, outputs, responsibilities, acceptance, validation, rollback, risks, assumptions, open questions, roles, effort, duration, predecessors, and citations. Never fabricate missing information; convert it into open questions.");
        outcome += "\nThe WBS exists to tell delivery engineers exactly what technical work must be performed from the authoritative SOW scope. Decompose every in-scope product, platform, version, migration, upgrade, configuration, integration, quantity, dependency, validation obligation, cutover requirement, and deliverable into concrete executable technical tasks. Name the actual technology and technical action whenever the SOW provides it. Generic placeholders such as implement solution components, conduct system testing, assess environment, design solution architecture, finalize implementation, or similar lifecycle boilerplate do not satisfy the contract and must not be returned as substitutes for scoped technical work. Project-management activities may supplement but never replace technical scope. Return at least one detailed child task in each of Plan, Design, Implement, Validate, and Release, with unique WBS references, at least two distinct execution steps, inputs, outputs, acceptance criteria, validation steps, required roles, positive effort/duration estimates, and current SOW evidence citations. Do not automatically create project milestones. The PM should review exceptions and business constraints, not reconstruct the technical plan. Phase semantics are mandatory: PLAN contains project logistics, access, scheduling, current-state discovery, inventory, prerequisites, readiness, and existing-environment assessment; DESIGN contains target architecture, logical design, sizing, compatibility, dependencies, configuration decisions, implementation sequencing, rollback design, and technical installation approach, but never the installation itself; IMPLEMENT contains installation, build, configuration, migration, upgrade, data movement, and execution of technical changes; VALIDATE contains every form of testing and verification, including unit, integration, functional, performance, failover, recovery, security, UAT, acceptance, and remediation/retest; RELEASE contains cutover/go-live, production transition, runbooks, as-built documentation, knowledge transfer, support handoff, hypercare, acceptance, and closeout. Installation/configuration must not be placed in Design. Testing must not be placed in Implement. Runbook development and cutover must be placed in Release.";

        // Project Forge is a review projection of the same governed planning graph,
        // not a reason to invoke the model a second time behind an HTTP gateway.
        // Reuse a completed current-version FlowHive planner run, or durably queue
        // one for the background worker and let the caller poll with HTTP 202.
        if (string.Equals(
                capabilityCode,
                CelarAiCapabilityCatalog.ProjectForgePlanEstimate,
                StringComparison.OrdinalIgnoreCase))
        {
            return await ReuseOrQueueDurableFlowHivePlanAsync(
                actualUserId,
                effectiveUserId,
                seed,
                documents,
                requestedOutcome?.Trim() ?? string.Empty,
                detailLevel,
                context,
                cancellationToken, observedRunId);
        }

        CelarAiComposeResult composition;
        try
        {
            composition = await enterprise.ComposeAsync(
                actualUserId,
                effectiveUserId,
                new CelarAiComposeRequest(
                    Mode: "project_plan",
                    ProjectCode: seed.ProjectCode,
                    ProjectName: seed.ProjectName,
                    StartDate: seed.ProjectStartDate,
                    RequestedOutcome: outcome,
                    DetailLevel: Clean(detailLevel, 80, "comprehensive"),
                    DiagramType: "flowchart",
                    AllowSanitizedExternalFallback: allowSanitizedExternalFallback,
                    ProjectId: seed.ProjectId,
                    CapabilityCode: capabilityCode) { FlowHiveExecution = sequential },
                context,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectPlanningAiOrchestrator")
                .LogWarning(exception, "Shared project-planning generation returned an evidence-limited result.");
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "The governed AI route was temporarily unavailable. No Planner or Forge draft was changed.",
                documents.Blockers,
                documents.Warnings.Concat([
                    "The existing project documents remain available and can be retried without another upload."
                ]).ToArray());
        }

        // Preserve the terminal route outcome before evaluating evidence coverage.
        // A refusal is not a missing-document problem and must never trigger retry.
        if (composition.Status == "celar_ai_solution_draft_refused")
        {
            var provider = composition.SelectedTarget switch
            {
                CelarAiCapabilityTargets.DeepSeek => "DeepSeek",
                CelarAiCapabilityTargets.CelarAi => "Celar AI",
                CelarAiCapabilityTargets.Claude => "Claude",
                CelarAiCapabilityTargets.OpenAi => "OpenAI",
                _ => "The selected AI provider"
            };
            var message = $"{provider} reported a safety refusal. Generation stopped without provider failover, automatic retry, or changes to the planning draft. Use this run's correlation ID to review the provider diagnostic.";
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_safety_refusal",
                message,
                composition,
                null,
                null,
                null,
                [],
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        var sourceGroundedFailSafeReady = IsSourceGroundedFailSafePlan(composition.FlowHivePlan);

        // A private provider deadline is an availability failure, not an
        // evidence-quality failure. Preserve the route diagnostics and let the
        // durable worker consume its existing bounded retry budget. Previously
        // this fell through to the evidence gate, which converted a transient
        // Celar AI deadline into terminal needs_attention before retry policy
        // could run.
        var retryableProviderDiagnostics = (composition.TargetDecisions ?? [])
            .Where(decision => decision.Outcome is "failed" or "unavailable")
            .Select(decision => decision.ReasonCode)
            .Where(IsRetryableProviderDiagnostic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (retryableProviderDiagnostics.Length > 0 && !sourceGroundedFailSafeReady)
        {
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_ai_temporarily_unavailable",
                "The private AI provider exceeded a bounded attempt deadline. No planning draft was changed; the durable worker may use its remaining retry budget.",
                composition,
                null,
                null,
                null,
                composition.MissingEvidence
                    .Concat(retryableProviderDiagnostics.Select(code => $"private_provider_{code}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        if (sequential is not null
            && sequential.State.Phases.Any(p => p.Status != "completed" || p.Plan is null)
            && !sourceGroundedFailSafeReady)
            return ProjectPlanningGenerationResult.Failed("project_planning_phases_incomplete",
                "Five validated phases are required. Generation stopped without changing the project plan; inspect the saved stage progress.",
                ["All five delivery phases must finish before WBS assembly."], documents.Warnings);

        var currentDocumentIds = documents.CurrentDocumentIds;
        var currentCitations = composition.Citations
            .Where(citation => currentDocumentIds.Contains(citation.DocumentId)
                && documents.SelectedDocuments.Any(document => document.DocumentId == citation.DocumentId
                    && document.ActiveSourceSha256.Length == 64
                    && string.Equals(document.ActiveSourceSha256, citation.SourceSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(document.ActiveDocumentVersion, citation.DocumentVersion, StringComparison.Ordinal)))
            .ToArray();
        var currentCitationIds = currentCitations
            .Select(citation => citation.CitationId)
            .ToHashSet();
        var currentSowId = documents.StatementOfWork!.DocumentId;
        var sowCitations = currentCitations
            .Where(citation => citation.DocumentId == currentSowId)
            .ToArray();

        var privatePlan = composition.FlowHivePlan;
        // A low-confidence model result may still be a useful review artifact.
        // Accept it only when the whole WBS is structurally complete; current
        // citation and SOW authority are still enforced immediately below.
        var reviewablePartialPlanReady =
            composition.Status == "celar_ai_solution_draft_partial"
            && PulseAiPrivateRagService.IsReviewableWholeWbs(privatePlan);
        var completedStatus = composition.Status == "celar_ai_solution_draft_completed"
            || sourceGroundedFailSafeReady
            || reviewablePartialPlanReady;
        var citedPlan = privatePlan is not null
            && privatePlan.Tasks.Count > 0
            && privatePlan.CitationIds.Count > 0
            && privatePlan.CitationIds.All(currentCitationIds.Contains)
            && privatePlan.Tasks.All(task => task.CitationIds.Count > 0
                && task.CitationIds.All(currentCitationIds.Contains));
        var grounded = completedStatus
            && citedPlan
            && sowCitations.Length > 0;

        if (!grounded)
        {
            var missing = composition.MissingEvidence
                .Concat(documents.Blockers)
                .Concat([
                    "Celar AI did not return a complete plan cited to the current active Work Register SOW and current authorized project evidence. No generic plan was substituted."
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_evidence_insufficient",
                "The source-evidence quality gate did not pass. No planning draft was changed.",
                composition,
                null,
                null,
                null,
                missing,
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        ProjectFlowHivePlanRequest generated;
        try
        {
            // Bind identity to the exact SOW version before deriving stable task IDs.
            // FlowHive must preserve native task phases, not multiply a scaffold.
            generated = string.Equals(capabilityCode, CelarAiCapabilityCatalog.ProjectFlowHivePlan, StringComparison.OrdinalIgnoreCase)
                ? ProjectFlowHiveExecutablePlanBuilder.BuildCandidate(seed with
                {
                    SowVersion = documents.StatementOfWork?.ActiveVersionId?.ToString("D"),
                    GsdVersion = documents.GeneralSolutionDesign?.ActiveVersionId?.ToString("D")
                }, privatePlan!, currentCitationIds)
                : ProjectFlowHiveDetailedPlanBuilder.Build(seed, privatePlan!);
        }
        catch (Exception exception)
        {
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_expansion_failed",
                "The AI result did not meet the executable five-phase work-breakdown contract. No planning draft was changed.",
                composition,
                null,
                null,
                null,
                composition.MissingEvidence
                    .Concat([exception.Message])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        generated = NormalizeAiPhaseSemantics(generated);

        var genericTechnicalTasks = generated.Tasks
            .Where(task => !task.IsSummary && IsGenericTechnicalPlaceholder(task.Name))
            .Select(task => $"{task.WbsNumber} {task.Name}")
            .ToArray();
        if (genericTechnicalTasks.Length > 0)
        {
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_technical_scope_insufficient",
                "The generated WBS contained generic lifecycle placeholders instead of the technical work required by the SOW. No working draft was changed.",
                composition,
                null,
                null,
                null,
                genericTechnicalTasks
                    .Select(task => $"Replace generic task with SOW-specific technical work: {task}")
                    .ToArray(),
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        var validation = ProjectFlowHiveScheduleEngine.Validate(generated);
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(generated);
        generated = ApplySchedule(generated, schedule, composition, documents);

        var warnings = composition.Warnings
            .Concat(sourceGroundedFailSafeReady
                ? ["AI providers did not complete the whole-WBS synthesis within their bounded deadlines. FlowHive created a complete source-cited review draft from the current private project evidence; PM and Engineering review is required before baseline approval."]
                : Array.Empty<string>())
            .Concat(reviewablePartialPlanReady
                ? ["The AI WBS passed the current-source citation and executable-work checks but remained below the configured confidence threshold. FlowHive saved it as a review-only working draft; PM and Engineering validation is required before baseline approval."]
                : Array.Empty<string>())
            .Concat(documents.Warnings)
            .Concat(schedule.Issues
                .Where(issue => issue.Code == "project_end_exceeded")
                .Select(issue => issue.Message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = schedule.ProjectTargetEndDate.HasValue
            && schedule.ProjectFinishDate.HasValue
            && schedule.ProjectFinishDate.Value > schedule.ProjectTargetEndDate.Value
                ? "completed_with_schedule_overrun"
                : "completed";

        return new ProjectPlanningGenerationResult(
            validation.Valid && schedule.Valid,
            status,
            validation.Valid && schedule.Valid
                ? "The current project documents produced a source-cited five-phase planning draft."
                : "The generated plan requires correction before it can be saved.",
            composition,
            generated,
            validation,
            schedule,
            composition.MissingEvidence,
            warnings);
    }

    internal static ProjectFlowHivePlanRequest NormalizeAiPhaseSemantics(ProjectFlowHivePlanRequest plan)
    {
        if (!string.Equals(plan.SourceKind, "celar_ai", StringComparison.OrdinalIgnoreCase) || plan.Tasks is null)
            return plan;

        static bool Match(string text, string pattern) =>
            System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        var tasks = plan.Tasks.Select(task =>
        {
            if (task.IsSummary) return task;
            var nameText = task.Name ?? string.Empty;
            var text = string.Join(" ", new[] { task.Name, task.Description }.Concat(task.DetailedSteps ?? []));
            var phase = task.Phase;
            if (Match(nameText, @"\b(cutover|go-live|golive|runbook|runbooks|as-built|as built|knowledge transfer|handoff|hand-off|handover|hypercare|production transition|train operations|training operations)\b"))
                phase = "Release";
            else if (Match(nameText, @"\b(test|testing|backups?|restores?|validate|validation|verify|verification|uat|failover|performance|recovery|security review|acceptance testing|retest|drill)\b")
                && !Match(nameText, @"\b(test plan|test design|validation method|acceptance criteria)\b"))
                phase = "Validate";
            else if (Match(nameText, @"\b(install|installation|deploy|deployment|configure|configuration|migrate|migration|upgrade|provision|provisioning|build)\b")
                && !Match(nameText, @"\b(installation approach|implementation approach|deployment approach|migration approach|upgrade approach|configuration design|build design)\b"))
                phase = "Implement";
            return string.Equals(phase, task.Phase, StringComparison.Ordinal) ? task : task with { Phase = phase };
        }).ToArray();

        var phaseOrder = new[] { "Plan", "Design", "Implement", "Validate", "Release" };
        var executable = phaseOrder.SelectMany(phase => tasks.Where(task => !task.IsSummary && string.Equals(task.Phase, phase, StringComparison.OrdinalIgnoreCase))).ToArray();
        var summaries = phaseOrder.Select((phase, index) => tasks.First(task => task.IsSummary && string.Equals(task.Phase, phase, StringComparison.OrdinalIgnoreCase)) with { WbsNumber = (index + 1).ToString(), ParentWbsNumber = null }).ToArray();
        var rebuilt = new List<ProjectFlowHivePlanTaskInput>();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var phase in phaseOrder.Select((name, index) => new { name, wbs = (index + 1).ToString() }))
        {
            rebuilt.Add(summaries.First(task => string.Equals(task.Phase, phase.name, StringComparison.OrdinalIgnoreCase)));
            var number = 0;
            foreach (var task in executable.Where(task => string.Equals(task.Phase, phase.name, StringComparison.OrdinalIgnoreCase)))
            {
                number++;
                var nextWbs = $"{phase.wbs}.{number}";
                aliases[task.WbsNumber] = nextWbs;
                rebuilt.Add(task with { WbsNumber = nextWbs, ParentWbsNumber = phase.wbs });
            }
        }
        var dependencies = (plan.Dependencies ?? []).Select(item => item with
        {
            PredecessorWbs = aliases.GetValueOrDefault(item.PredecessorWbs, item.PredecessorWbs),
            SuccessorWbs = aliases.GetValueOrDefault(item.SuccessorWbs, item.SuccessorWbs)
        }).Where(item => !string.Equals(item.PredecessorWbs, item.SuccessorWbs, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        var assignments = (plan.Assignments ?? []).Select(item => item with { TaskWbs = aliases.GetValueOrDefault(item.TaskWbs, item.TaskWbs) }).ToArray();
        return plan with { Tasks = rebuilt, Dependencies = dependencies, Assignments = assignments };
    }

    private static bool IsGenericTechnicalPlaceholder(string? name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return true;
        string[] placeholders =
        [
            "implement solution components",
            "conduct system testing",
            "execute unit and integration testing",
            "assess existing environment and constraints",
            "design solution architecture",
            "conduct design review and approval",
            "validate assumptions and exclusions",
            "set up staging and test environments",
            "manage defects and fixes",
            "conduct user acceptance testing",
            "develop training materials and documentation",
            "deliver training sessions",
            "finalize implementation and prepare for go-live",
            "execute go-live and hypercare",
            "complete handover to operations",
            "conduct project closure and post-mortem",
            "develop project plan and schedule",
            "define project governance and communication plan"
        ];
        return placeholders.Any(placeholder => normalized == placeholder);
    }

    internal static bool IsSourceGroundedFailSafePlan(PulseAiPrivateFlowHivePlan? plan)
    {
        if (plan is null || plan.Tasks.Count < 15 || plan.CitationIds.Count == 0)
            return false;

        var phases = new[] { "Plan", "Design", "Implement", "Validate", "Release" };
        return phases.All(phase =>
                plan.Tasks.Count(task => string.Equals(task.Phase, phase, StringComparison.Ordinal)) >= 3)
            && plan.Tasks.All(task =>
                task.CitationIds.Count > 0
                && task.EstimatedHours > 0m
                && task.EstimatedDurationDays > 0m
                && (task.DetailedSteps?.Count ?? 0) >= 2)
            && string.Equals(
                plan.ConfidenceExplanation?.Contains("deterministic private fallback", StringComparison.OrdinalIgnoreCase) == true
                    ? "fail_safe" : string.Empty,
                "fail_safe",
                StringComparison.Ordinal);
    }

    private static async Task<ProjectPlanningGenerationResult> ReuseOrQueueDurableFlowHivePlanAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        ProjectFlowHivePlanRequest seed,
        ProjectPlanningDocumentResolution documents,
        string outcome,
        string? detailLevel,
        HttpContext context,
        CancellationToken cancellationToken, Guid? observedRunId)
    {
        if (!seed.ProjectId.HasValue)
        {
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "Project Forge could not resolve the exact project identifier for the shared background planner.",
                ["The exact selected project UUID is required before Project Forge can reuse or queue the shared planner."],
                documents.Warnings);
        }

        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
        {
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "The durable shared planner store is temporarily unavailable.",
                config.Missing,
                documents.Warnings);
        }

        var projectId = seed.ProjectId.Value;
        var currentSowVersion = documents.StatementOfWork?.ActiveVersionId?.ToString("D") ?? string.Empty;
        var currentGsdVersion = documents.GeneralSolutionDesign?.ActiveVersionId?.ToString("D") ?? string.Empty;
        var correlationId = Clean(
            context.Response.Headers["X-ProjectPulse-Correlation-Id"].FirstOrDefault()
                ?? context.TraceIdentifier,
            180,
            Guid.NewGuid().ToString("N"));

        try
        {
            await using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var guard = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@project_id::text,734));",
                connection,
                transaction))
            {
                guard.Parameters.AddWithValue("project_id", projectId);
                await guard.ExecuteNonQueryAsync(cancellationToken);
            }

            var rows = new List<DurablePlannerRow>();
            await using (var command = new NpgsqlCommand($"""
                SELECT run_id,status,phase,progress_percent,
                       COALESCE(generated_plan::text,''),
                       COALESCE(schedule_payload::text,''),
                       COALESCE(validation_payload::text,''),
                       COALESCE(warnings::text,'[]'),
                       COALESCE(correlation_id,''),
                       completed_at
                  FROM {DurableRunTable}
                 WHERE project_id=@project_id
                   AND (@observed::uuid IS NULL OR run_id=@observed)
                   AND actual_actor_user_id=@actual
                   AND effective_actor_user_id=@effective
                   AND requested_outcome=@outcome AND detail_level=@detail
                   AND execution_contract=@execution_contract
                   AND (source_version_fingerprint=@source_versions OR status IN ('queued','processing','generating'))
                   AND (status IN ('queued','processing','generating','completed','completed_with_schedule_overrun') OR @observed::uuid IS NOT NULL)
                 ORDER BY created_at DESC
                 LIMIT 12
                 FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("project_id", projectId);
                command.Parameters.Add("observed", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)observedRunId ?? DBNull.Value;
                command.Parameters.AddWithValue("actual", actualUserId);
                command.Parameters.AddWithValue("effective", effectiveUserId);
                command.Parameters.AddWithValue("outcome", Clean(outcome, 4_000, string.Empty));
                command.Parameters.AddWithValue("detail", Clean(detailLevel, 80, "comprehensive"));
                command.Parameters.AddWithValue("execution_contract", ProjectFlowHiveExecutionPolicy.Contract);
                command.Parameters.AddWithValue("source_versions", ProjectFlowHiveExecutionPolicy.VersionFingerprint(documents));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new DurablePlannerRow(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        Convert.ToInt32(reader.GetValue(3)),
                        Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(4)),
                        Deserialize<ProjectFlowHiveScheduleResult>(reader.GetString(5)),
                        Deserialize<ProjectFlowHivePlanValidationResult>(reader.GetString(6)),
                        Deserialize<string[]>(reader.GetString(7)) ?? [],
                        reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
                }
            }

            foreach (var row in rows)
            {
                if (row.Status is not ("completed" or "completed_with_schedule_overrun")
                    || row.Plan is null
                    || row.Schedule is null
                    || row.Validation is null
                    || row.Plan.RevisionLabel != ProjectFlowHiveExecutablePlanBuilder.Contract
                    || row.Plan.ProjectStartDate != seed.ProjectStartDate
                    || row.Plan.ProjectEndDate != seed.ProjectEndDate
                    || !MatchesCurrentAuthority(row.Plan, projectId, currentSowVersion, currentGsdVersion))
                {
                    continue;
                }

                await transaction.CommitAsync(cancellationToken);
                return ReusedDurableResult(row, documents) with { Progress = await Progress(row.RunId) };
            }

            var active = rows.FirstOrDefault(row => row.Status is "queued" or "processing" or "generating");
            if (active is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ProjectPlanningGenerationResult.Failed(
                    "project_planning_ai_temporarily_unavailable",
                    $"The shared durable planner is still processing this project ({active.Status}/{active.Phase}, {active.ProgressPercent}%). Project Forge will reuse it when complete.",
                    [],
                    documents.Warnings.Concat([
                        "Project Forge did not start a second synchronous AI request while the shared planner was already running."
                    ]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) with { Progress = await Progress(active.RunId) };
            }

            if (observedRunId.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return ProjectPlanningGenerationResult.Failed("project_planning_stopped",
                    "The selected planner run stopped or its source documents changed. Review its status in FlowHive before starting another run.",
                    [], documents.Warnings) with { Progress = await Progress(observedRunId.Value) };
            }

            // Release the read transaction before the shared queue captures its own starting revision.
            await transaction.CommitAsync(cancellationToken);
            var runId = await ProjectFlowHiveAiPlannerOrchestrationModule.QueueForActorAsync(
                connection, projectId, actualUserId, effectiveUserId, seed,
                outcome, detailLevel, correlationId, cancellationToken);
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "Project Forge queued the shared durable planner in the background. Retry this request while the planner completes; no second synchronous AI request was started.",
                [],
                documents.Warnings.Concat([
                    $"Shared planner run {runId:D} is queued for background generation."
                ]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) with { Progress = await Progress(runId) };

            Task<object?> Progress(Guid id) => ProjectFlowHiveAiPlannerOrchestrationModule.ReadPhaseProgressForActorAsync(
                connection, projectId, id, actualUserId, effectiveUserId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectPlanningAiOrchestrator")
                .LogWarning(exception, "Project Forge could not reuse or queue the durable FlowHive planner.");
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "The shared durable planner is temporarily unavailable. No Project Forge draft was changed.",
                [],
                documents.Warnings.Concat([
                    "The current project documents remain available; retrying does not require another upload."
                ]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private static ProjectPlanningGenerationResult ReusedDurableResult(
        DurablePlannerRow row,
        ProjectPlanningDocumentResolution documents)
    {
        var plan = row.Plan!;
        var schedule = row.Schedule!;
        var validation = row.Validation!;
        var warnings = row.Warnings
            .Concat(documents.Warnings)
            .Concat([
                "Project Forge reused the completed durable FlowHive planning artifact for the same project and current document versions; no second model inference was executed in this HTTP request."
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var correlationId = !string.IsNullOrWhiteSpace(plan.CelarAiCorrelationId)
            ? plan.CelarAiCorrelationId!
            : row.CorrelationId;
        var provider = !string.IsNullOrWhiteSpace(plan.CelarAiProviderCode)
            ? plan.CelarAiProviderCode!
            : "shared_durable_flowhive_planner";
        var composition = new CelarAiComposeResult(
            Status: "celar_ai_solution_draft_completed",
            Mode: "project_plan",
            PrimaryExecutionPath: "shared_durable_flowhive_planner",
            ProjectId: plan.ProjectId,
            ProjectCode: plan.ProjectCode ?? string.Empty,
            ProjectName: plan.ProjectName ?? string.Empty,
            DetailedAnswer: null,
            FlowHivePlan: null,
            SowDraft: null,
            Timeline: [],
            Diagram: null,
            Citations: [],
            Warnings: warnings,
            MissingEvidence: [],
            Conflicts: [],
            CoverageScore: 1m,
            Confidence: plan.CelarAiConfidence ?? 1m,
            ConfidenceExplanation: "Project Forge reused the already source-grounded durable FlowHive plan for the exact current project-document authority.",
            ExternalAssistance: null,
            DataAsOf: row.CompletedAt ?? DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            SelectedTarget: provider,
            AttemptedTargets: [provider],
            SkippedTargets: [],
            TargetDecisions: []);

        return new ProjectPlanningGenerationResult(
            validation.Valid && schedule.Valid,
            row.Status,
            validation.Valid && schedule.Valid
                ? "Project Forge reused the completed source-cited five-phase FlowHive planning artifact."
                : "The shared FlowHive planning artifact requires correction before Project Forge can save it.",
            composition,
            plan,
            validation,
            schedule,
            [],
            warnings);
    }

    private static bool MatchesCurrentAuthority(
        ProjectFlowHivePlanRequest plan,
        Guid projectId,
        string currentSowVersion,
        string currentGsdVersion)
    {
        if (plan.ProjectId != projectId) return false;
        if (string.IsNullOrWhiteSpace(currentSowVersion)
            || !string.Equals(plan.SowVersion, currentSowVersion, StringComparison.OrdinalIgnoreCase))
            return false;
        if (currentGsdVersion.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(plan.GsdVersion)) return false;
        }
        else if (!string.Equals(plan.GsdVersion, currentGsdVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var executable = (plan.Tasks ?? [])
            .Where(task => !task.IsSummary && !task.IsMilestone)
            .ToArray();
        return executable.Length > 0
            && executable.All(task => (task.CitationIds?.Count ?? 0) > 0)
            && (plan.CelarAiCitationIds?.Count ?? 0) > 0;
    }

    private static T? Deserialize<T>(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        try { return JsonSerializer.Deserialize<T>(value, DurablePlannerJson); }
        catch { return default; }
    }

    private static ProjectFlowHivePlanRequest ApplySchedule(
        ProjectFlowHivePlanRequest generated,
        ProjectFlowHiveScheduleResult schedule,
        CelarAiComposeResult composition,
        ProjectPlanningDocumentResolution documents)
    {
        var scheduledByWbs = schedule.Tasks
            .GroupBy(task => task.WbsNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return generated with
        {
            Tasks = (generated.Tasks ?? []).Select(task =>
            {
                var wbs = task.WbsNumber?.Trim() ?? string.Empty;
                return scheduledByWbs.TryGetValue(wbs, out var scheduled)
                    ? task with
                    {
                        EstimatedStartDate = scheduled.StartDate,
                        EstimatedFinishDate = scheduled.EndDate
                    }
                    : task;
            }).ToArray(),
            Milestones = (generated.Milestones ?? []).Select(milestone =>
                scheduledByWbs.TryGetValue(milestone.PredecessorWbs, out var predecessor)
                    ? milestone with { TargetDate = predecessor.EndDate }
                    : milestone).ToArray(),
            SowVersion = documents.StatementOfWork?.ActiveVersionId?.ToString("D"),
            GsdVersion = documents.GeneralSolutionDesign?.ActiveVersionId?.ToString("D"),
            SourceKind = "celar_ai",
            CelarAiProviderCode = composition.SelectedTarget.Length > 0
                ? composition.SelectedTarget
                : composition.PrimaryExecutionPath,
            CelarAiCorrelationId = composition.CorrelationId,
            CelarAiConfidence = composition.Confidence
        };
    }

    private static string Clean(string? value, int maximum, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    internal static bool IsRetryableProviderDiagnostic(string? diagnostic)
    {
        var normalized = (diagnostic ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is
            "provider_deadline_exceeded"
            or "private_model_timeout"
            or "private_model_http_502"
            or "private_model_http_503"
            or "private_model_http_504"
            or "private_model_transport_failure"
            || normalized.StartsWith("private_module025_generation_deadline_exceeded", StringComparison.Ordinal)
            || normalized.StartsWith("private_module025_phase_deadline_exceeded", StringComparison.Ordinal);
    }

    private sealed record DurablePlannerRow(
        Guid RunId,
        string Status,
        string Phase,
        int ProgressPercent,
        ProjectFlowHivePlanRequest? Plan,
        ProjectFlowHiveScheduleResult? Schedule,
        ProjectFlowHivePlanValidationResult? Validation,
        IReadOnlyList<string> Warnings,
        string CorrelationId,
        DateTimeOffset? CompletedAt);
}

internal sealed record ProjectPlanningGenerationResult(
    bool Succeeded,
    string Status,
    string Message,
    CelarAiComposeResult? Composition,
    ProjectFlowHivePlanRequest? Plan,
    ProjectFlowHivePlanValidationResult? Validation,
    ProjectFlowHiveScheduleResult? Schedule,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Warnings, object? Progress = null)
{
    internal static ProjectPlanningGenerationResult NotReady(
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        string message) => new(
            false,
            "project_planning_documents_processing",
            message,
            null,
            null,
            null,
            null,
            blockers,
            warnings);

    internal static ProjectPlanningGenerationResult Failed(
        string status,
        string message,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> warnings) => new(
            false,
            status,
            message,
            null,
            null,
            null,
            null,
            missingEvidence,
            warnings);
}
