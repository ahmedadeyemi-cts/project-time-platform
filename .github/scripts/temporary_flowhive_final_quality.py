#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path.cwd()


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, value: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(value, encoding="utf-8")


# Planning contracts: retain deterministic milestones alongside the WBS.
path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHivePlanningContracts.cs"
source = read(path)
if "ProjectFlowHivePlanMilestoneInput" not in source:
    source = source.replace(
        "    IReadOnlyList<int>? CelarAiCitationIds = null);",
        "    IReadOnlyList<int>? CelarAiCitationIds = null,\n    IReadOnlyList<ProjectFlowHivePlanMilestoneInput>? Milestones = null);",
        1,
    )
    anchor = "public sealed record ProjectFlowHiveDependencyInput("
    milestone = '''public sealed record ProjectFlowHivePlanMilestoneInput(
    Guid ClientMilestoneId,
    string Name,
    string Description,
    string PredecessorWbs,
    DateOnly? TargetDate,
    IReadOnlyList<string> AcceptanceEvidence,
    IReadOnlyList<int> CitationIds,
    bool IsAssumption);

'''
    if anchor not in source:
        raise SystemExit("Planner milestone contract anchor was not found")
    source = source.replace(anchor, milestone + anchor, 1)
write(path, source)

# Deterministic builder: every source work package receives a Release acceptance
# milestone, and every executable task receives a role-based planning resource.
path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveDetailedPlanBuilder.cs"
source = read(path)
if "BuildMilestones(workPackages" not in source:
    source = source.replace(
        "        var dependencies = BuildDependencies(workPackages, generatedWbs);\n        var notes = BuildPlanNotes(privatePlan, workPackages.Length);",
        "        var dependencies = BuildDependencies(workPackages, generatedWbs);\n"
        "        var milestones = BuildMilestones(workPackages, generatedWbs, privatePlan.Milestones);\n"
        "        var assignments = BuildRoleAssignments(generated);\n"
        "        var notes = BuildPlanNotes(privatePlan, workPackages.Length);",
        1,
    )
    source = source.replace(
        "            Dependencies = dependencies,\n            Assignments = [],",
        "            Dependencies = dependencies,\n            Assignments = assignments,\n            Milestones = milestones,",
        1,
    )

if "private static IReadOnlyList<ProjectFlowHivePlanMilestoneInput> BuildMilestones" not in source:
    anchor = "    private static IReadOnlyList<ProjectFlowHiveDependencyInput> BuildDependencies("
    helpers = '''    private static IReadOnlyList<ProjectFlowHivePlanMilestoneInput> BuildMilestones(
        IReadOnlyList<CanonicalWorkPackage> workPackages,
        IReadOnlyDictionary<(string PackageKey, string Phase), string> generatedWbs,
        IReadOnlyList<PulseAiPrivateFlowHiveMilestone> sourceMilestones)
    {
        var milestones = workPackages.Select(package =>
            new ProjectFlowHivePlanMilestoneInput(
                Guid.NewGuid(),
                Limit($"{package.Name} accepted and released", 300, "Work package accepted and released"),
                Limit(
                    $"Complete the cited release, handoff, and acceptance gate for {package.Description}",
                    2_000,
                    "Complete the cited release and acceptance gate."),
                generatedWbs[(package.Key, "Release")],
                null,
                package.AcceptanceCriteria
                    .Concat(package.ValidationSteps)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .DefaultIfEmpty("Retain approved acceptance and release evidence for the cited work package.")
                    .ToArray(),
                package.CitationIds,
                package.IsAssumption)).ToList();

        foreach (var milestone in sourceMilestones.Take(100))
        {
            var citations = milestone.CitationIds.Distinct().OrderBy(value => value).ToArray();
            if (citations.Length == 0) continue;
            var related = workPackages.FirstOrDefault(package => package.CitationIds.Intersect(citations).Any());
            var predecessor = related is null
                ? generatedWbs[(workPackages[^1].Key, "Release")]
                : generatedWbs[(related.Key, "Release")];
            milestones.Add(new ProjectFlowHivePlanMilestoneInput(
                Guid.NewGuid(),
                Limit(milestone.Name, 300, "Cited project milestone"),
                Limit(milestone.Description, 2_000, "Complete the cited project milestone."),
                predecessor,
                null,
                milestone.AcceptanceEvidence
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .DefaultIfEmpty("Retain objective acceptance evidence for the cited milestone.")
                    .ToArray(),
                citations,
                milestone.IsAssumption));
        }

        return milestones
            .GroupBy(item => $"{item.PredecessorWbs}|{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(250)
            .ToArray();
    }

    private static IReadOnlyList<ProjectFlowHivePlanAssignmentInput> BuildRoleAssignments(
        IReadOnlyList<ProjectFlowHivePlanTaskInput> tasks)
    {
        return tasks
            .Where(task => !task.IsSummary && !task.IsMilestone)
            .Select(task => new
            {
                Task = task,
                Role = (task.RequiredRoles ?? [])
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?? "Required delivery role — PM review required"
            })
            .Select(item => new ProjectFlowHivePlanAssignmentInput(
                item.Task.WbsNumber,
                null,
                item.Role,
                100m,
                Math.Max(0.25m, item.Task.RemainingEffortHours)))
            .Take(5_000)
            .ToArray();
    }

'''
    if anchor not in source:
        raise SystemExit("Detailed builder dependency helper anchor was not found")
    source = source.replace(anchor, helpers + anchor, 1)
write(path, source)

# Populate milestone dates from the deterministic schedule before persisting the
# working draft, while preserving no automatic immutable version/baseline.
path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs"
source = read(path)
if "Milestones = (generated.Milestones ?? [])" not in source:
    old = '''            generated = generated with
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
                SourceKind = "celar_ai",'''
    new = '''            generated = generated with
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
                SourceKind = "celar_ai",'''
    if old not in source:
        raise SystemExit("Orchestrator schedule projection anchor was not found")
    source = source.replace(old, new, 1)
write(path, source)

# Planner seed and evidence-only workspace UI.
path = "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx"
source = read(path)
if "milestones: []" not in source:
    source = source.replace(
        "    assignments: planAssignments,\n    gsdVersion:",
        "    assignments: planAssignments,\n    milestones: [],\n    gsdVersion:",
        1,
    )

if "flowhive-ai-operation-progress" not in source:
    # Insert immediately before the existing AI evidence/readiness panel content.
    anchor = "            <ol><li>The exact stored Module 064 order is followed for this capability.</li>"
    if anchor not in source:
        # The prior redesign may have reformatted the list; use the closing copy div.
        copy_start = source.find("{activeView === 'ai' ? (")
        insertion_at = source.find("</div>", copy_start)
        if copy_start < 0 or insertion_at < 0:
            raise SystemExit("AI Planning Workspace layout anchor was not found")
        insertion_at += len("</div>")
    else:
        insertion_at = source.find("</ol>", source.find(anchor)) + len("</ol>")
    panel = '''
            {aiPreview ? <section className="flowhive-ai-operation-progress" aria-label="AI Planner operation progress">
              <header><div><span>Operation phase</span><strong>{labelFrom(aiPreview.phase || aiPreview.status)}</strong></div><div><span>Progress</span><strong>{Number(aiPreview.progressPercent || 0)}%</strong></div><div><span>Run</span><strong>{aiPreview.runId ? String(aiPreview.runId).slice(0, 8) : 'Not started'}</strong></div></header>
              <progress max="100" value={Number(aiPreview.progressPercent || 0)}>{Number(aiPreview.progressPercent || 0)}%</progress>
              <div className="flowhive-ai-evidence-grid">
                <article><h4>Authority and evidence</h4><p>{aiPreview.planningEvidence?.sourceGrounded ? 'Current authoritative SOW citations are grounded.' : 'FlowHive is resolving private SOW/GSD evidence.'}</p><small>Private processing: {aiPreview.planningEvidence?.automaticPrivateProcessing ? 'Automatic' : 'Pending'}</small></article>
                <article><h4>Schedule</h4><p>{aiPreview.scheduleAssessment?.exceedsRequestedFinish ? `Calculated finish ${aiPreview.scheduleAssessment.calculatedFinishDate} exceeds the requested finish.` : 'The requested and calculated delivery window is under review.'}</p><small>Estimates compressed: {aiPreview.scheduleAssessment?.estimatesCompressed ? 'Yes' : 'No'}</small></article>
                <article><h4>Working draft</h4><p>{aiPreview.workingDraft?.persisted ? 'The editable Planner working draft is saved.' : 'No Planner mutation has occurred yet.'}</p><small>Immutable version: {aiPreview.workingDraft?.immutableVersionCreated ? 'Created' : 'Not created'} · Baseline: {aiPreview.workingDraft?.baselineCreated ? 'Created' : 'Not created'}</small></article>
              </div>
              {(aiPreview.blockers || []).length ? <div><h4>Missing information / blockers</h4><ul>{aiPreview.blockers.map((item) => <li key={item}>{item}</li>)}</ul></div> : null}
              {(aiPreview.warnings || []).length ? <div><h4>Warnings and open questions</h4><ul>{aiPreview.warnings.map((item) => <li key={item}>{item}</li>)}</ul></div> : null}
              {(aiPreview.generationLogs || []).length ? <details><summary>Generation logs</summary><ol>{aiPreview.generationLogs.map((item, index) => <li key={`${index}-${item}`}>{item}</li>)}</ol></details> : null}
              {(aiPreview.scheduleAssessment?.criticalPath || []).length ? <details><summary>Critical path</summary><ol>{aiPreview.scheduleAssessment.criticalPath.map((item) => <li key={item.wbsNumber}><strong>{item.wbsNumber} · {item.name}</strong><span>{formatDate(item.startDate)} – {formatDate(item.endDate)}</span></li>)}</ol></details> : null}
            </section> : <EmptyState>Run AI Planner from Planner to begin automatic SOW/GSD processing and generation.</EmptyState>}
'''
    source = source[:insertion_at] + panel + source[insertion_at:]

if "flowhive-milestone-list" not in source:
    planner_anchor = "              <div className=\"flowhive-table-heading\"><div><h3>AI Planner work breakdown</h3>"
    milestone_panel = '''              {(draftPlan.milestones || []).length ? <section className="flowhive-milestone-list"><header><div><h3>Project milestones</h3><p>Source-backed release and acceptance gates. Target dates are calculated from predecessor tasks.</p></div><strong>{draftPlan.milestones.length}</strong></header><div>{draftPlan.milestones.map((milestone) => <article key={milestone.clientMilestoneId}><div><span>{milestone.predecessorWbs}</span><h4>{milestone.name}</h4></div><p>{milestone.description}</p><small>{formatDate(milestone.targetDate)} · {(milestone.citationIds || []).length} citation(s)</small></article>)}</div></section> : null}
'''
    if planner_anchor in source:
        source = source.replace(planner_anchor, milestone_panel + planner_anchor, 1)
write(path, source)

# Lightweight CSS for the evidence-only operation and milestones; theme tokens
# preserve Light/Dark compatibility without hard-coded brand colors.
path = "src/frontend/project-time-web/src/project-flowhive-center.css"
source = read(path)
if ".flowhive-ai-operation-progress" not in source:
    source += '''

.flowhive-ai-operation-progress,.flowhive-milestone-list{display:grid;gap:1rem;padding:1rem;border:1px solid var(--pulse-border,rgba(100,116,139,.28));border-radius:1rem;background:var(--pulse-surface,#fff)}
.flowhive-ai-operation-progress>header,.flowhive-milestone-list>header{display:flex;flex-wrap:wrap;gap:1rem;align-items:center;justify-content:space-between}
.flowhive-ai-operation-progress>header div{display:grid;gap:.2rem;min-width:8rem}.flowhive-ai-operation-progress progress{width:100%;height:.75rem}
.flowhive-ai-evidence-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(14rem,1fr));gap:.75rem}.flowhive-ai-evidence-grid article,.flowhive-milestone-list article{display:grid;gap:.45rem;padding:.85rem;border:1px solid var(--pulse-border,rgba(100,116,139,.22));border-radius:.8rem;background:var(--pulse-surface-raised,rgba(148,163,184,.08))}
.flowhive-ai-operation-progress details[open]{display:grid;gap:.65rem}.flowhive-ai-operation-progress li{margin:.35rem 0}.flowhive-ai-operation-progress li span{display:block;font-size:.82rem;opacity:.75}
.flowhive-milestone-list>div{display:grid;grid-template-columns:repeat(auto-fit,minmax(18rem,1fr));gap:.75rem}.flowhive-milestone-list article>div{display:flex;gap:.6rem;align-items:baseline}.flowhive-milestone-list h3,.flowhive-milestone-list h4,.flowhive-ai-operation-progress h4{margin:0}
@media (prefers-color-scheme:dark){.flowhive-ai-operation-progress,.flowhive-milestone-list{background:var(--pulse-surface,#111827)}}
'''
write(path, source)

# Extend executable regression coverage for milestones and planning resources.
path = "tests/FlowHiveDetailedPlannerTests/Program.cs"
source = read(path)
if "work_package_release_milestones_created" not in source:
    anchor = 'Assert(generated.Notes?.Contains("Generated executable tasks: 10", StringComparison.Ordinal) == true,\n    "plan_notes_explain_detailed_task_count");'
    addition = anchor + '''
Assert((generated.Milestones?.Count ?? 0) >= 2, "work_package_release_milestones_created");
Assert(generated.Milestones!.All(item => item.CitationIds.Count > 0), "milestone_citations_preserved");
Assert(generated.Milestones!.All(item => item.PredecessorWbs.StartsWith("5.", StringComparison.Ordinal)), "milestones_follow_release_tasks");
Assert((generated.Assignments?.Count ?? 0) == executable.Length, "role_resources_populated_for_every_executable_task");
Assert(generated.Assignments!.All(item => !string.IsNullOrWhiteSpace(item.ResourceDisplayName)), "role_resource_names_populated");'''
    if anchor not in source:
        raise SystemExit("Planner milestone regression anchor was not found")
    source = source.replace(anchor, addition, 1)
write(path, source)

print("FLOWHIVE_FINAL_PRODUCTION_QUALITY_PATCH_APPLIED")
