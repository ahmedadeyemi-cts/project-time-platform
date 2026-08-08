from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def write(relative: str, content: str) -> None:
    (ROOT / relative).write_text(content, encoding="utf-8")


def replace_once(relative: str, old: str, new: str) -> None:
    text = read(relative)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{relative}: expected one replacement target, found {count}")
    write(relative, text.replace(old, new, 1))


def regex_replace_once(relative: str, pattern: str, replacement: str) -> None:
    text = read(relative)
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.DOTALL)
    if count != 1:
        raise SystemExit(f"{relative}: expected one regex replacement target, found {count}")
    write(relative, updated)


def insert_after_once(relative: str, marker: str, addition: str) -> None:
    text = read(relative)
    count = text.count(marker)
    if count != 1:
        raise SystemExit(f"{relative}: expected one insertion marker, found {count}")
    write(relative, text.replace(marker, marker + addition, 1))


def insert_before_once(relative: str, marker: str, addition: str) -> None:
    text = read(relative)
    count = text.count(marker)
    if count != 1:
        raise SystemExit(f"{relative}: expected one insertion marker, found {count}")
    write(relative, text.replace(marker, addition + marker, 1))


# Ask Celar AI: recognize clearly public officeholder questions before the
# conservative acronym/proper-name privacy guard. Internal people/project role
# questions continue to fail closed inside Celar AI.
replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs",
    'public const string ContractVersion = "celar-ai-system-knowledge-v4-20260807";',
    'public const string ContractVersion = "celar-ai-system-knowledge-v5-20260808";',
)
replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs",
    '''        if (LooksLikeNamedInternalSubject(raw)) return false;

        return Regex.IsMatch(normalized,
                @"^(?:(?:what\\s+(?:is|are|was|were)|why\\s+(?:is|are|does|do|did)|how\\s+(?:do|does|can|to)|define|explain|translate|calculate|write|give\\s+me)\\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && !LooksLikeInternalRecordQuestion(normalized);
    }

    private static bool LooksLikeNamedInternalSubject(string raw)
''',
    '''        // Clearly public country-officeholder questions are safe to route
        // through the isolated public-question boundary. This check runs before
        // the conservative acronym/proper-name guard so ordinary public terms
        // such as "US President" are not mistaken for an internal record code.
        if (LooksLikeClearlyPublicOfficeholderQuestion(normalized)) return true;

        if (LooksLikeNamedInternalSubject(raw)) return false;

        return Regex.IsMatch(normalized,
                @"^(?:(?:what\\s+(?:is|are|was|were)|why\\s+(?:is|are|does|do|did)|how\\s+(?:do|does|can|to)|define|explain|translate|calculate|write|give\\s+me)\\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && !LooksLikeInternalRecordQuestion(normalized);
    }

    private static bool LooksLikeClearlyPublicOfficeholderQuestion(string normalized) =>
        Regex.IsMatch(
            normalized,
            @"^who\\s+(?:is|are|was|were)\\s+(?:the\\s+)?(?:current\\s+)?(?:(?:(?:u\\.?\\s*s\\.?|united\\s+states)\\s+)?(?:president|vice\\s+president)|(?:president|vice\\s+president)\\s+of\\s+(?:the\\s+)?(?:u\\.?\\s*s\\.?|united\\s+states)|(?:president|prime\\s+minister|head\\s+of\\s+state|monarch|king|queen)\\s+of\\s+[a-z][a-z\\s.'’-]{2,})\\s*\\??$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool LooksLikeNamedInternalSubject(string raw)
''',
)

insert_after_once(
    "tests/CelarAiInternalDataTests/Program.cs",
    '''Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("What is the capital of France?"),
    "clearly public capital question is external eligible");
''',
    '''Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the US President?"),
    "public US officeholder question is external eligible");
Require(
    PulseAiSystemKnowledgeCatalog.Analyze("Who is the US President?").IntentCode == "general_knowledge",
    "public US officeholder question resolves to general knowledge");
Require(
    !PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the president of the United States?"),
    "spelled-out public officeholder question is external eligible");
Require(
    PulseAiSystemKnowledgeCatalog.IsPulseScopedQuestion("Who is the project manager for our project?"),
    "internal project-role question remains private");
''',
)

insert_after_once(
    "src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs",
    '        ("general_knowledge", "What is the capital of France?", "general_knowledge", "general_knowledge"),\n',
    '        ("public_officeholder", "Who is the US President?", "general_knowledge", "general_knowledge"),\n',
)

# FlowHive and Project Forge: if cited private evidence exists but the approved
# private model is unavailable, preserve a structured citation-grounded plan.
# The central router can then continue to Claude/OpenAI using only its fixed,
# identity-free generic planning capsule; raw evidence never leaves Celar AI.
replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    '''            else if (!options.RequirePrivateModelForDocumentAnswers)
            {
                answer = DeterministicEvidenceAnswer(
                    answerRunId,
                    query,
                    retrieval,
                    model,
                    directKnowledge,
                    flowHive);
            }
''',
    '''            else if (flowHive || !options.RequirePrivateModelForDocumentAnswers)
            {
                // Planning remains fail-closed on scope. When private inference
                // is unavailable, retain a cited private scaffold so the shared
                // router may continue with only its fixed identity-free generic
                // planning capsule. Raw SOW/GSD text and identities stay private.
                answer = DeterministicEvidenceAnswer(
                    answerRunId,
                    query,
                    retrieval,
                    model,
                    directKnowledge,
                    flowHive);
            }
''',
)

rich_plan = '''            var tasks = retrieval.Chunks.Take(8).Select((chunk, index) =>
            {
                var phase = DeterministicPlanningPhase(chunk, index);
                return new PulseAiPrivateFlowHiveTask(
                    Wbs: $"{index + 1}.0",
                    Name: chunk.SectionTitle.Length > 0
                        ? chunk.SectionTitle
                        : $"Review {chunk.DocumentCategory.ToUpperInvariant()} evidence",
                    Description: "Convert this cited scope evidence into one controlled delivery work package with explicit prerequisites, ordered execution, objective outputs, validation evidence, measurable acceptance criteria, and accountable human review.",
                    EstimatedDurationDays: phase == "Implement" ? 2m : 1m,
                    RequiredRoles: ["Project Manager", "Engineer"],
                    Predecessors: index == 0 ? [] : [$"{index}.0"],
                    CitationIds: [chunk.RankOrder],
                    IsAssumption: true,
                    Phase: phase,
                    DetailedSteps: DeterministicPlanningSteps(phase),
                    Inputs:
                    [
                        "Current authorized citation and its approved scope boundary.",
                        "Confirmed access, decisions, dependencies, change controls, and review criteria required for this work package."
                    ],
                    Outputs:
                    [
                        $"{phase} work-package deliverable or objective evidence record.",
                        "Updated decision, exception, dependency, risk, and follow-up record for unresolved items."
                    ],
                    AcceptanceCriteria:
                    [
                        "The Project Manager and Engineering reviewer confirm that the output is traceable to the cited scope and contains no unsupported commitment.",
                        "Every prerequisite, exception, failed validation, and open item has an accountable owner and review disposition."
                    ],
                    ValidationSteps:
                    [
                        "Compare the produced output with the cited scope, approved prerequisites, and measurable acceptance criteria.",
                        "Retain objective evidence, record exceptions without hiding them, and repeat affected checks after an authorized correction."
                    ],
                    CustomerResponsibilities:
                    [
                        "Provide the decisions, access, information, review responses, and acceptance participation required by the approved scope."
                    ],
                    UsSignalResponsibilities:
                    [
                        "Perform only the authorized delivery activity, preserve objective evidence, and escalate missing prerequisites or scope conflicts rather than assuming them."
                    ],
                    Prerequisites:
                    [
                        "The governing citation remains current, authorized, and applicable to this work package.",
                        "Required access, backups, approvals, dependencies, communications, and rollback controls are available before execution."
                    ],
                    Risks:
                    [
                        "A deterministic scaffold can omit technical nuance and therefore requires Engineering review before adoption.",
                        "Missing access, decisions, evidence, dependencies, or acceptance measures can delay work and must be escalated."
                    ],
                    OpenQuestions:
                    [
                        "Which source-backed technical details, owners, dependencies, or acceptance measures still require confirmation?",
                        "Which assumptions must become verified facts before this work package is scheduled or adopted?"
                    ],
                    EstimatedHours: phase == "Implement" ? 16m : 8m,
                    Priority: "normal");
            }).ToArray();
            var plan = new PulseAiPrivateFlowHivePlan(
                Objective: "Prepare a comprehensive, reviewable project-plan draft from current authorized evidence while preserving source citations, scope boundaries, deterministic scheduling, and required human approval.",
                Tasks: tasks,
                Milestones: DeterministicPlanningMilestones(retrieval.Chunks),
                Dependencies:
                [
                    "The deterministic FlowHive schedule establishes executable predecessor relationships after PM and Engineering review.",
                    "A task cannot advance while a cited prerequisite, required decision, access dependency, or acceptance condition remains unresolved."
                ],
                RequiredRoles: ["Project Manager", "Engineer"],
                Assumptions:
                [
                    "Durations and effort are planning placeholders until Engineering validates the cited scope and technical complexity.",
                    "Identity-free Claude/OpenAI guidance may improve generic delivery structure but cannot establish project scope, dates, completion, or customer commitments."
                ],
                Risks:
                [
                    "The approved private model was unavailable, so every generated detail requires PM and Engineering review.",
                    "Generic external planning guidance may be incomplete for the private technical environment and is never treated as source evidence."
                ],
                OutOfScopeItems:
                [
                    "Any activity, deliverable, technical detail, date, or commitment not supported by current authorized citations remains out of scope until approved through governed change control."
                ],
                OpenQuestions: retrieval.MissingEvidence.Count > 0
                    ? retrieval.MissingEvidence
                    : ["Which source-backed details, owners, dependencies, or acceptance measures still require PM and Engineering confirmation?"],
                Conflicts: retrieval.Conflicts,
                CitationIds: retrieval.Chunks.Select(chunk => chunk.RankOrder).ToArray(),
                Confidence: Math.Min(0.45m, retrieval.CoverageScore),
                ConfidenceExplanation: "The deterministic private fallback preserves citation-grounded scope and complete review fields. Identity-free external guidance remains supplementary and unverified, so confidence is capped until PM and Engineering validate the plan.");'''

regex_replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    r'''            var plan = new PulseAiPrivateFlowHivePlan\(
                Objective: "Prepare a reviewable project-plan draft from the authorized project documents\.".*?
                ConfidenceExplanation: "The deterministic fallback preserves citations but does not perform full private-model planning reasoning\."\);''',
    rich_plan,
)

replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    '["The approved private model was unavailable. This scaffold must not be treated as a complete project plan."],',
    '["The approved private model was unavailable. Celar AI preserved a citation-grounded scope scaffold while the shared router used only identity-free generic planning guidance. No raw SOW/GSD text, identity, date, environment detail, identifier, or commercial value left the private boundary; PM and Engineering review remains mandatory."],',
)

planning_helpers = '''    private static string DeterministicPlanningPhase(
        PulseAiPrivateRetrievedChunk chunk,
        int index)
    {
        var evidence = $"{chunk.SectionTitle} {chunk.CitationAnchor}".ToLowerInvariant();
        if (new[] { "release", "handoff", "transition", "knowledge transfer", "closeout" }
            .Any(value => evidence.Contains(value, StringComparison.Ordinal)))
            return "Release";
        if (new[] { "validate", "validation", "test", "testing", "verify", "acceptance", "uat", "remediation" }
            .Any(value => evidence.Contains(value, StringComparison.Ordinal)))
            return "Validate";
        if (new[] { "design", "architecture", "workshop", "technical requirement", "solution" }
            .Any(value => evidence.Contains(value, StringComparison.Ordinal)))
            return "Design";
        if (new[] { "plan", "planning", "discovery", "kickoff", "prerequisite", "readiness", "scope" }
            .Any(value => evidence.Contains(value, StringComparison.Ordinal)))
            return "Plan";
        if (new[] { "implement", "configuration", "deployment", "migration", "integration", "install", "upgrade" }
            .Any(value => evidence.Contains(value, StringComparison.Ordinal)))
            return "Implement";
        return new[] { "Plan", "Design", "Implement", "Validate", "Release" }[index % 5];
    }

    private static IReadOnlyList<string> DeterministicPlanningSteps(string phase) =>
        phase switch
        {
            "Plan" =>
            [
                "Review the current authorized citation, scope boundaries, exclusions, responsibilities, assumptions, dependencies, and acceptance requirements before creating executable work.",
                "Confirm accountable roles, required decisions, access, source artifacts, communications, change controls, scheduling constraints, and escalation paths without inventing unavailable facts.",
                "Translate the supported outcome into a bounded work package, identify missing evidence and conflicts, and assign every unresolved prerequisite or decision for human follow-up.",
                "Record the approved planning output, objective review evidence, exceptions, and authorization required before the work package advances to design."
            ],
            "Design" =>
            [
                "Translate the cited scope outcome into traceable functional, technical, security, operational, support, and acceptance requirements without expanding the approved boundary.",
                "Document the proposed approach, dependencies, interfaces, assumptions, constraints, implementation sequence, validation method, rollback criteria, and required human decisions.",
                "Review the design with the accountable Project Manager and Engineer, resolve or assign every conflict, and preserve the resulting decision and exception evidence.",
                "Approve the design package only after prerequisites, acceptance measures, implementation controls, and validation evidence requirements are complete enough for execution."
            ],
            "Implement" =>
            [
                "Verify the approved design, access, backups, prerequisites, maintenance controls, communications, monitoring, dependency readiness, and rollback capability before the first change.",
                "Perform the authorized implementation, configuration, migration, integration, installation, upgrade, or remediation activity in controlled stages traceable to the cited scope.",
                "Capture objective evidence for each stage, document deviations and failed actions, and stop or escalate when a prerequisite, safety control, or scope boundary is not satisfied.",
                "Record the implemented state, outstanding exceptions, follow-up actions, and readiness evidence required before formal validation begins."
            ],
            "Validate" =>
            [
                "Execute the approved technical, functional, security, operational, and regression checks that map directly to the cited acceptance requirements and implemented output.",
                "Record passed and failed checks with objective evidence, determine ownership for every defect or exception, and avoid claiming success when evidence remains incomplete.",
                "Apply only authorized corrections, repeat affected validation, and preserve before-and-after evidence plus any remaining risk, limitation, or deferred item.",
                "Prepare the acceptance evidence package for Project Manager, Engineering, and required stakeholder review before the work advances to release."
            ],
            _ =>
            [
                "Finalize the approved configuration record, operating procedures, support information, known limitations, open risks, and role-appropriate knowledge-transfer material.",
                "Confirm monitoring, support ownership, escalation paths, access, documentation, acceptance evidence, and operational readiness for the authorized transition.",
                "Complete the handoff review, assign every unresolved action, and preserve evidence that the receiving owner understands responsibilities and limitations.",
                "Close the work package only after deliverable status, acceptance evidence, exceptions, lessons learned, archival requirements, and required approvals are recorded."
            ]
        };

    private static IReadOnlyList<PulseAiPrivateFlowHiveMilestone> DeterministicPlanningMilestones(
        IReadOnlyList<PulseAiPrivateRetrievedChunk> chunks)
    {
        var supported = chunks.Take(5).ToArray();
        var phases = new[] { "Plan", "Design", "Implement", "Validate", "Release" };
        return phases.Select((phase, index) => new PulseAiPrivateFlowHiveMilestone(
            Name: $"{phase} review gate",
            Description: $"Confirm that the {phase.ToLowerInvariant()} work packages remain traceable to authorized evidence, contain complete review fields, and include no unsupported scope, date, or commitment before advancing.",
            ProposedTiming: $"After completion and review of the {phase} work packages.",
            AcceptanceEvidence:
            [
                "Objective work-package output and validation evidence are retained.",
                "Every exception, risk, dependency, assumption, and open item has an accountable review disposition."
            ],
            CitationIds: [supported[Math.Min(index, supported.Length - 1)].RankOrder],
            IsAssumption: true)).ToArray();
    }

'''
insert_before_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    "    private static PulseAiPrivateRagAnswer InsufficientEvidence(\n",
    planning_helpers,
)

# Regression contracts.
replace_once(
    "tests/validate-celar-ai-internal-data-intelligence.mjs",
    "requireText(catalog, 'celar-ai-system-knowledge-v4-20260807', 'system-knowledge contract version')",
    "requireText(catalog, 'celar-ai-system-knowledge-v5-20260808', 'system-knowledge contract version')",
)
insert_after_once(
    "tests/validate-celar-ai-internal-data-intelligence.mjs",
    "requireText(catalog, 'LooksLikeNamedInternalSubject(raw)', 'named-subject privacy guard')\n",
    "requireText(catalog, 'LooksLikeClearlyPublicOfficeholderQuestion(normalized)', 'public officeholder classification before acronym privacy guard')\n",
)

insert_before_once(
    "src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs",
    "const failed = assertions.filter((assertion) => !assertion.condition);\n",
    '''assertInvariant(
  'MODULE_066_CITED_SCAFFOLD_EXTERNAL_ENRICHMENT',
  privateRag.includes('else if (flowHive || !options.RequirePrivateModelForDocumentAnswers)') &&
    privateRag.includes('DeterministicPlanningSteps') &&
    privateRag.includes('DeterministicPlanningMilestones') &&
    privateRag.includes('identity-free generic planning guidance') &&
    privateRag.includes('No raw SOW/GSD text'),
  'private cited scope survives private-model unavailability while only identity-free generic guidance may use Claude/OpenAI'
);

''',
)

insert_after_once(
    "src/frontend/project-time-web/scripts/validate-module-033-project-forge.mjs",
    "requireText(externalReasoning, 'ProjectPulseAiFeatures.ProjectForgePlanEstimate', 'Module 064 Project Forge execution route');\n",
    '''for (const token of [
  'else if (flowHive || !options.RequirePrivateModelForDocumentAnswers)',
  'DeterministicPlanningSteps',
  'DeterministicPlanningMilestones',
  'identity-free generic planning guidance',
  'No raw SOW/GSD text'
]) requireText(privateRagService, token, 'Project Forge cited scaffold and external enrichment boundary');
''',
)

# Architecture decision record for future changes.
doc_path = ROOT / "docs/modules/module-011-pulse-ai/UNIFIED-CELAR-CONTEXT-ROUTING.md"
if doc_path.exists():
    raise SystemExit(f"{doc_path.relative_to(ROOT)} already exists")
doc_path.write_text(
    """# Unified Celar AI Context Routing

## Canonical target order

Every AI-enabled capability uses the same governed target order:

1. Celar AI
2. Claude
3. OpenAI
4. Governed local template

A target advances only when the prior target is unavailable, ineligible, or below the capability quality gate. A safety refusal remains terminal and is never bypassed by a later target.

## Capability-specific context adapters

The router order is shared, but each capability compiles a different governed context envelope.

- **Ask Celar AI — public question:** sends only the isolated public question to Claude/OpenAI after the public classifier and privacy gate pass.
- **Ask Celar AI — Pulse/internal question:** uses authorized product knowledge, live tools, internal-data resolvers, and private RAG. Private context is not sent to public providers.
- **Timesheet:** uses authorized project/SOW context privately when available. Claude/OpenAI may receive only approved identity-free fact labels and a separately sanitized Engineer note. With no SOW, the same de-identified fact route can still create a detailed reviewable description.
- **FlowHive and Project Forge:** build a private citation-grounded scope scaffold first. If private inference is unavailable, Celar AI preserves that scaffold as a partial review artifact, then Claude/OpenAI may provide only the fixed identity-free five-phase planning blueprint. The private system retains cited work packages and deterministic scheduling; no raw SOW/GSD text, customer identity, people data, dates, environment detail, identifiers, or commercial values leave the private boundary.
- **Governed local:** remains the final fallback and never invents private facts or completion evidence.

## Quality and promotion gates

Public answers must be classified as public, contain no protected context, pass output privacy validation, and return a usable direct answer. Internal answers require the owning source, permission scope, evidence, citations, confidence, and freshness appropriate to the capability. FlowHive and Project Forge remain review-only until Project Manager and Engineering validation; deterministic scheduling, adoption, assignment, publication, submission, or customer commitment always remains outside model control.

## Regression repaired on August 8, 2026

The public classifier previously recognized `What`, `Why`, and `How` question forms but not clearly public `Who` officeholder questions. It also treated the ordinary acronym `US` as a possible internal identifier. As a result, `Who is the US President?` was incorrectly routed to internal product knowledge and ended as an evidence-limited local answer. Clearly public country-officeholder questions are now recognized before the conservative named-subject/acronym privacy guard, while named employees, project roles, customer records, and other internal subjects continue to fail closed inside Celar AI.
""",
    encoding="utf-8",
)

allowed = {
    "src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs",
    "tests/CelarAiInternalDataTests/Program.cs",
    "src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    "tests/validate-celar-ai-internal-data-intelligence.mjs",
    "src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs",
    "src/frontend/project-time-web/scripts/validate-module-033-project-forge.mjs",
    "docs/modules/module-011-pulse-ai/UNIFIED-CELAR-CONTEXT-ROUTING.md",
    ".github/workflows/bootstrap-unified-celar-context-routing.yml",
    ".github/workflows/bootstrap-unified-celar-context-routing-pr.yml",
    "scripts/temporary/apply_unified_celar_context_routing.py",
}
changed = {
    line[3:]
    for line in subprocess.check_output(["git", "status", "--short"], cwd=ROOT, text=True).splitlines()
    if len(line) >= 4
}
unexpected = changed - allowed
if unexpected:
    raise SystemExit(f"Unexpected files after patch: {sorted(unexpected)}")

print("UNIFIED_CELAR_CONTEXT_ROUTING_PATCH=APPLIED")
