from pathlib import Path

path = Path('src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs')
text = path.read_text()
start_marker = '    private static async Task<IResult> GenerateAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)\n'
end_marker = '    private static async Task<IResult> ConfirmAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)\n'
start = text.index(start_marker)
end = text.index(end_marker, start)
replacement = r'''    private static async Task<IResult> GenerateAsync(Guid engagementId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Module025SowGsd");

        Module025EngagementRow current;
        Module025AccessContext access;
        try
        {
            var writable = await LoadWritableStateAsync(engagementId, context, cancellationToken);
            if (writable.Error is not null) return writable.Error;
            await using (var snapshotConnection = writable.Connection!)
            {
                current = writable.Engagement!;
                access = writable.Access!;
                if (current.Status == "confirmed") return StateConflict("confirmed_record", "Reopen this confirmed SOW/GSD before generating a new scope.");
                if (current.ServiceOverview.Trim().Length < 20) return Results.BadRequest(new { status = "service_overview_required", message = "Enter a meaningful Service Overview before asking Celar AI to build the detailed P/D/I/V/R scope and level of effort." });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 generation state could not be loaded. EngagementId={EngagementId} Diagnostic={Diagnostic}", engagementId, exception.GetType().Name.ToLowerInvariant());
            return Results.Json(new { status = "module025_generation_state_unavailable", message = "The SOW/GSD generation state is temporarily unavailable. The saved draft was not changed.", stateChanged = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        CelarAiComposeResult composition;
        try
        {
            var enterprise = context.RequestServices.GetRequiredService<CelarAiEnterprisePlatformService>();
            composition = await enterprise.ComposeAsync(
                access.ActualUserId,
                access.EffectiveUserId,
                new CelarAiComposeRequest(
                    Mode: "sow_draft",
                    ProjectCode: current.EngagementNumber,
                    ProjectName: current.CustomerName,
                    StartDate: null,
                    RequestedOutcome: BuildGenerationPrompt(current),
                    DetailLevel: "comprehensive",
                    DiagramType: "flowchart",
                    AllowSanitizedExternalFallback: false,
                    ProjectId: null,
                    CapabilityCode: CelarAiCapabilityCatalog.SowGsdPlanning),
                context,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 detailed SOW/GSD generation failed without logging customer or Service Overview content. EngagementId={EngagementId} Diagnostic={Diagnostic}", engagementId, exception.GetType().Name.ToLowerInvariant());
            return Results.Json(new { status = "module025_ai_temporarily_unavailable", message = "The governed Celar AI route did not complete. The saved SOW/GSD draft was not changed.", stateChanged = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (composition.SowDraft is null)
        {
            return Results.UnprocessableEntity(new { status = "module025_ai_evidence_limited", message = "Celar AI did not return a reviewable SOW draft. No generic scope or fabricated level of effort was substituted.", composition.Status, composition.Warnings, composition.MissingEvidence, composition.Conflicts, composition.Confidence, composition.ConfidenceExplanation, composition.CorrelationId });
        }

        Dictionary<string, GeneratedPhase> generated;
        JsonElement sowSections;
        JsonElement aiMetadata;
        try
        {
            var sowDraft = JsonSerializer.SerializeToElement(composition.SowDraft);
            var workPackages = JsonArray(sowDraft, "WorkPackages").ToArray();
            if (workPackages.Length == 0)
            {
                return Results.UnprocessableEntity(new { status = "module025_ai_evidence_limited", message = "Celar AI returned no detailed work packages. No generic P/D/I/V/R scope was substituted.", composition.Status, composition.Warnings, composition.MissingEvidence, composition.Confidence, composition.CorrelationId });
            }

            generated = PhaseCodes.ToDictionary(code => code, code => new GeneratedPhase(code), StringComparer.OrdinalIgnoreCase);
            foreach (var package in workPackages)
            {
                var phaseCode = ClassifyPhase(JsonString(package, "Phase"), JsonString(package, "Name"), JsonString(package, "Description"));
                var phase = generated[phaseCode];
                phase.PackageCount += 1;
                phase.SuggestedHours += Math.Max(0m, JsonDecimal(package, "EstimatedHours") ?? 0m);
                AddDistinct(phase.Objectives, JsonString(package, "Description"));
                var packageName = JsonString(package, "Name");
                var packageDescription = JsonString(package, "Description");
                AddDistinct(phase.DetailedActivities, packageName.Length > 0 ? $"{packageName}: {packageDescription}" : packageDescription);
                AddDistinct(phase.TechnicalTasks, JsonStrings(package, "DetailedSteps"));
                AddDistinct(phase.Deliverables, JsonStrings(package, "Outputs"));
                AddDistinct(phase.CustomerResponsibilities, JsonStrings(package, "CustomerResponsibilities"));
                AddDistinct(phase.UsSignalResponsibilities, JsonStrings(package, "UsSignalResponsibilities"));
                AddDistinct(phase.Prerequisites, JsonStrings(package, "Prerequisites"));
                AddDistinct(phase.Dependencies, JsonStrings(package, "Predecessors"));
                if (JsonBoolean(package, "IsAssumption")) AddDistinct(phase.Assumptions, packageDescription);
                AddDistinct(phase.OpenQuestions, JsonStrings(package, "OpenQuestions"));
                AddDistinct(phase.AcceptanceCriteria, JsonStrings(package, "AcceptanceCriteria"));
                AddDistinct(phase.ValidationSteps, JsonStrings(package, "ValidationSteps"));
                AddDistinct(phase.Risks, JsonStrings(package, "Risks"));
                foreach (var citationId in JsonIntegers(package, "CitationIds")) phase.CitationIds.Add(citationId);
            }

            sowSections = JsonSerializer.SerializeToElement(new
            {
                executiveSummary = JsonString(sowDraft, "ExecutiveSummary"),
                objectives = JsonStrings(sowDraft, "Objectives"),
                inScope = JsonStrings(sowDraft, "InScope"),
                outOfScope = JsonStrings(sowDraft, "OutOfScope"),
                deliverables = JsonStrings(sowDraft, "Deliverables"),
                customerResponsibilities = JsonStrings(sowDraft, "CustomerResponsibilities"),
                usSignalResponsibilities = JsonStrings(sowDraft, "UsSignalResponsibilities"),
                assumptions = JsonStrings(sowDraft, "Assumptions"),
                dependencies = JsonStrings(sowDraft, "Dependencies"),
                acceptanceCriteria = JsonStrings(sowDraft, "AcceptanceCriteria"),
                timelineAndMilestones = JsonStrings(sowDraft, "TimelineAndMilestones"),
                risks = JsonStrings(sowDraft, "Risks"),
                openQuestions = JsonStrings(sowDraft, "OpenQuestions"),
                citationIds = JsonIntegers(sowDraft, "CitationIds"),
                reviewRequired = true,
                contractuallyBinding = false
            });
            aiMetadata = JsonSerializer.SerializeToElement(new
            {
                generatedAt = DateTimeOffset.UtcNow,
                composition.Status,
                composition.PrimaryExecutionPath,
                composition.SelectedTarget,
                composition.AttemptedTargets,
                composition.SkippedTargets,
                composition.Warnings,
                composition.MissingEvidence,
                composition.Conflicts,
                composition.CoverageScore,
                composition.Confidence,
                composition.ConfidenceExplanation,
                composition.CorrelationId,
                source = "service_overview_and_governed_celar_ai",
                humanReviewRequired = true,
                suggestedHoursPreservedSeparately = true
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 Celar AI output could not be materialized. EngagementId={EngagementId} CorrelationId={CorrelationId} Diagnostic={Diagnostic}", engagementId, composition.CorrelationId, exception.GetType().Name.ToLowerInvariant());
            return Results.Json(new { status = "module025_ai_output_unavailable", message = "Celar AI returned an output that could not be safely prepared for review. The saved SOW/GSD draft was not changed.", composition.CorrelationId, stateChanged = false }, statusCode: StatusCodes.Status502BadGateway);
        }

        try
        {
            var persistence = await LoadWritableStateAsync(engagementId, context, cancellationToken);
            if (persistence.Error is not null) return persistence.Error;
            await using var connection = persistence.Connection!;
            var latest = persistence.Engagement!;
            var latestAccess = persistence.Access!;

            if (latest.Revision != current.Revision)
            {
                return RevisionConflict(latest.Revision);
            }
            if (latest.Status == "confirmed")
            {
                return StateConflict("confirmed_record", "This SOW/GSD was confirmed while Celar AI was generating. Reopen it before generating a new scope.");
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var phaseCode in PhaseCodes)
            {
                var phase = generated[phaseCode];
                var objective = phase.Objectives.Count > 0 ? string.Join(" ", phase.Objectives) : $"No supported {PhaseLabel(phaseCode)} work package was returned. The Solution Architect must define and validate this phase before confirmation.";
                var rationale = phase.PackageCount > 0
                    ? $"Celar AI suggested {phase.SuggestedHours:0.##} hour(s) across {phase.PackageCount} detailed {PhaseLabel(phaseCode)} work package(s). The Solution Architect must validate the estimate against customer readiness, dependencies, access, technical constraints, and the confirmed execution approach before finalizing the GSD."
                    : "No evidence-supported effort was returned for this phase. The suggested effort remains 0 hours until the Solution Architect defines and validates the missing work.";
                await SaveGeneratedPhaseAsync(connection, transaction, engagementId, phase, objective, rationale, cancellationToken);
            }
            const string update = """
                UPDATE module025_sow_gsd_engagements
                SET sow_sections=@sow_sections::jsonb, ai_metadata=@ai_metadata::jsonb, status='review_ready', last_generated_at=NOW(), revision=revision+1
                WHERE engagement_id=@engagement_id AND revision=@expected_revision AND is_active=TRUE AND status NOT IN ('archived','confirmed')
                RETURNING revision;
                """;
            int revision;
            await using (var command = new NpgsqlCommand(update, connection, transaction))
            {
                command.Parameters.AddWithValue("engagement_id", engagementId);
                command.Parameters.AddWithValue("expected_revision", current.Revision);
                command.Parameters.AddWithValue("sow_sections", sowSections.GetRawText());
                command.Parameters.AddWithValue("ai_metadata", aiMetadata.GetRawText());
                var value = await command.ExecuteScalarAsync(cancellationToken);
                if (value is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return RevisionConflict(latest.Revision);
                }
                revision = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            await InsertEventAsync(connection, transaction, engagementId, latestAccess.ActualUserId, revision, "ai_generated", "Detailed P/D/I/V/R scope and suggested LOE generated for Solution Architect review.", new { composition.CorrelationId, composition.Confidence, missingPhaseCodes = generated.Values.Where(value => value.PackageCount == 0).Select(value => value.PhaseCode).ToArray() }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            Module025EngagementRow? saved = null;
            try
            {
                saved = await LoadEngagementAsync(connection, engagementId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Module 025 generated scope committed but readback failed. EngagementId={EngagementId} CorrelationId={CorrelationId} Diagnostic={Diagnostic}", engagementId, composition.CorrelationId, exception.GetType().Name.ToLowerInvariant());
            }

            return Results.Ok(new { status = "module025_detailed_scope_generated", revision, engagement = saved is null ? null : PublicEngagement(saved, latestAccess), warnings = composition.Warnings, missingEvidence = composition.MissingEvidence, conflicts = composition.Conflicts, confidence = composition.Confidence, confidenceExplanation = composition.ConfidenceExplanation, correlationId = composition.CorrelationId, message = saved is null ? "Detailed scope was generated and saved. Reload this SOW/GSD to view the latest revision." : "Detailed Plan, Design, Implement, Validate, and Release scope is ready for Solution Architect review. AI-suggested hours remain separate from editable final hours.", stateChanged = true });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Module 025 generated scope could not be persisted. EngagementId={EngagementId} CorrelationId={CorrelationId} Diagnostic={Diagnostic}", engagementId, composition.CorrelationId, exception.GetType().Name.ToLowerInvariant());
            return Results.Json(new { status = "module025_generation_persistence_unavailable", message = "Celar AI completed, but the generated scope could not be saved. The existing SOW/GSD draft was preserved. Retry generation.", composition.CorrelationId, stateChanged = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

'''
path.write_text(text[:start] + replacement + text[end:])
