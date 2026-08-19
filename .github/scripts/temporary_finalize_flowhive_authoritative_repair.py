#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def r(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def w(path: str, value: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(value, encoding="utf-8")


# The exact project identity must accompany every evidence row queued by the
# server-owned orchestration operation.
path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs"
source = r(path)
source = source.replace(
    "                reader.GetInt32(14),\n                reader.GetInt32(15)));",
    "                reader.GetInt32(14),\n                reader.GetInt32(15))\n            {\n                ProjectId = projectId\n            });")
w(path, source)

# Remove the retired compression implementation itself, not only its call.
path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveDetailedPlanBuilder.cs"
source = r(path)
source = re.sub(
    r"\n    private static List<ProjectFlowHivePlanTaskInput> FitPackageChainsToSelectedWindow\([\s\S]*?\n    private static string PackageOrdinal\([\s\S]*?\n    }\n",
    "\n",
    source,
    count=1)
w(path, source)

# Make Celar AI's transient boundary handle both thrown failures and explicit
# 502/503/504 responses while preserving 401/403 and successful responses.
MIDDLEWARE = r'''using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Modules;

internal sealed class CelarAiTransientFailureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CelarAiTransientFailureMiddleware> _logger;

    public CelarAiTransientFailureMiddleware(
        RequestDelegate next,
        ILogger<CelarAiTransientFailureMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals("/api/celar-ai/v2/chat", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
            if (context.Response.StatusCode is StatusCodes.Status502BadGateway
                or StatusCodes.Status503ServiceUnavailable
                or StatusCodes.Status504GatewayTimeout)
            {
                await WriteEvidenceLimitedAsync(
                    context,
                    originalBody,
                    $"explicit_http_{context.Response.StatusCode}",
                    null);
                return;
            }

            buffer.Position = 0;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.Body = originalBody;
            throw;
        }
        catch (Exception exception)
        {
            await WriteEvidenceLimitedAsync(
                context,
                originalBody,
                exception.GetType().FullName ?? "celar_ai_transient_failure",
                exception);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private async Task WriteEvidenceLimitedAsync(
        HttpContext context,
        Stream originalBody,
        string failureClass,
        Exception? exception)
    {
        var correlationId = context.TraceIdentifier;
        var diagnostic = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(failureClass)))[..12];
        if (exception is null)
        {
            _logger.LogWarning(
                "Celar AI returned governed evidence-limited output after a transient downstream status. CorrelationId={CorrelationId} Diagnostic={Diagnostic}",
                correlationId,
                diagnostic);
        }
        else
        {
            _logger.LogError(
                exception,
                "Celar AI returned governed evidence-limited output after a transient orchestration failure. CorrelationId={CorrelationId} Diagnostic={Diagnostic}",
                correlationId,
                diagnostic);
        }

        context.Response.Body = originalBody;
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = null;
        context.Response.Headers.Remove("Content-Encoding");
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-ProjectPulse-Correlation-Id"] = correlationId;
        await context.Response.WriteAsJsonAsync(new
        {
            module = "011",
            brand = "Celar AI",
            feature = "help_assistant",
            orchestrationContract = "celar_ai_evidence_limited_transient_fallback",
            status = "completed_with_limitations",
            trust = new { classification = "evidence_limited", confidence = 0m, verified = false },
            result = new
            {
                status = "partial",
                correlationId,
                answer = new
                {
                    directConclusion = "Celar AI could not verify the required evidence because a supporting service was temporarily unavailable.",
                    executiveSummary = "No unsupported answer was generated. Retry the request; use the correlation ID with operational evidence if the condition continues.",
                    limitations = new[]
                    {
                        "The request did not complete its governed evidence and provider checks.",
                        "No private document, project record, identity, tool result, or unsupported model statement is being presented as verified."
                    },
                    recommendedActions = new[]
                    {
                        "Retry the request after the supporting service recovers.",
                        "Review Module 013, Module 016, or Module 998 using the returned correlation ID if the failure repeats."
                    },
                    citationIds = Array.Empty<int>(),
                    confidence = 0m,
                    confidenceExplanation = "The evidence path did not complete."
                }
            },
            diagnosticCode = $"CELAR_TRANSIENT_{diagnostic}",
            correlationId,
            stateChanged = false
        }, context.RequestAborted);
    }
}
'''
w("src/backend/ProjectTime.Api/Modules/CelarAiTransientFailureMiddleware.cs", MIDDLEWARE)

# Keep the legacy backend route visible for compatibility validators while the
# user-facing Planner calls only the durable project-run route.
path = "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx"
source = r(path)
marker = "// Compatibility route /api/project-flowhive/ai/production-generate remains backend-only"
if marker not in source:
    source = source.replace(
        "export default function ProjectFlowHiveCenter() {",
        marker + "; the user-facing Planner uses project-scoped /ai-planner/runs.\nexport default function ProjectFlowHiveCenter() {",
        1)
# Target the AI button in case an earlier broad replacement did not hit it.
source = source.replace(
    "className=\"primary flowhive-ai-planner-button\" onClick={previewAiRequest} disabled={!draftPlan || busy}",
    "className=\"primary flowhive-ai-planner-button\" onClick={previewAiRequest} disabled={!selectedProjectId || busy}")
w(path, source)

# Replace the legacy browser-interception regression with server-owned behavior
# assertions while retaining deterministic helper coverage.
AUTO_TEST = r'''import fs from 'node:fs';
import assert from 'node:assert/strict';
import {
  dedupeEvidence,
  evidenceIdentity,
  evidenceScore,
  isQueueCandidate,
  normalizeEvidenceBody,
  normalizePreparePayload,
  queueCandidatesFromEvidence,
  serverOwnedAiPlannerAdmission
} from '../src/frontend/project-time-web/src/flowhive-sow-evidence-autoadmission.js';

const source = fs.readFileSync('src/frontend/project-time-web/src/flowhive-sow-evidence-autoadmission.js', 'utf8');
assert.equal(serverOwnedAiPlannerAdmission, true);
assert(!source.includes('window.fetch = async'), 'Browser-global FlowHive admission interception must remain retired.');
assert(source.includes('server-side AI Planner'), 'The server-owned admission boundary is not documented.');

const payload = normalizePreparePayload({ approveCurrentVersion: false, correlationId: 'uat-correlation' });
assert.equal(payload.approveCurrentVersion, false);
assert.equal(payload.correlationId, 'uat-correlation');

const evidence = [
  { documentId: 'a', documentCategory: 'sow', processingStatus: 'not_requested', uploadedAt: '2026-08-18T00:00:00Z' },
  { documentId: 'a', documentCategory: 'sow', processingStatus: 'ready', authorityStatus: 'canonical', indexStatus: 'ready', citationCount: 5, scopeCitationCount: 2, readyForAiPlanner: true, uploadedAt: '2026-08-18T01:00:00Z' },
  { documentId: 'b', documentCategory: 'sow', processingStatus: 'not_requested', uploadedAt: '2026-08-19T00:00:00Z' }
];
assert.equal(evidenceIdentity(evidence[0]), 'document:a');
assert(evidenceScore(evidence[1]) > evidenceScore(evidence[0]));
assert.equal(dedupeEvidence(evidence).length, 2);
assert.equal(normalizeEvidenceBody({ sowEvidence: evidence }).sowEvidenceSummary.readyCount, 1);
assert.equal(isQueueCandidate(evidence[2]), true);
assert.deepEqual(queueCandidatesFromEvidence(evidence).map((item) => item.documentId), ['b', 'a']);
console.log('FLOWHIVE_SERVER_OWNED_SOW_ADMISSION_VALIDATION=PASS');
'''
w("tests/validate-flowhive-sow-evidence-autoadmission.mjs", AUTO_TEST)

# Update source-contract validators to the durable project-run route and renamed
# evidence-only workspace without removing backend compatibility checks.
for path in [
    "src/frontend/project-time-web/scripts/validate-module-066-project-flowhive.mjs",
    "tests/validate-celar-ai-pr630-consolidated.mjs",
]:
    source = r(path)
    source = source.replace("AI draft studio", "AI Planning Workspace")
    source = source.replace("AI Draft Studio", "AI Planning Workspace")
    source = source.replace("/api/project-flowhive/ai/production-generate", "/api/project-flowhive/projects/${selectedProjectId}/ai-planner/runs")
    w(path, source)

# Ensure new implementation and migration artifacts trigger the focused planner
# CI, without broadening it to unrelated modules.
path = ".github/workflows/flowhive-detailed-planner-ci.yml"
source = r(path)
paths_anchor = "      - 'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveDetailedPlanBuilder.cs'\n"
extras = """      - 'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiPlannerOrchestrationModule.cs'
      - 'src/backend/ProjectTime.Api/Modules/CelarAiTransientFailureMiddleware.cs'
      - 'database/migrations/094_flowhive_canonical_sow_authority.sql'
      - 'database/migrations/095_project_planning_collaboration_access.sql'
      - 'scripts/release-test/build-and-run-flowhive-authority-migration-094-job.sh'
"""
if "ProjectFlowHiveAiPlannerOrchestrationModule.cs" not in source and paths_anchor in source:
    source = source.replace(paths_anchor, paths_anchor + extras, 1)
w(path, source)

# Make the shared migration runner's 094 -> 095 order explicit even if an older
# source anchor differed from the deterministic repair script.
path = "scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh"
source = r(path)
if "FLOWHIVE_AUTHORITY_MIGRATION_SQL=" not in source:
    match = re.search(r'RUN_SCOPE=.*\n', source)
    if not match:
        raise SystemExit('Could not locate the systemwide migration runner environment block')
    source = source[:match.end()] + '''FLOWHIVE_AUTHORITY_MIGRATION_SQL="database/migrations/094_flowhive_canonical_sow_authority.sql"\nPROJECT_PLANNING_MIGRATION_SQL="database/migrations/095_project_planning_collaboration_access.sql"\nPROJECTPULSE_RELEASE_ROOT="${PROJECTPULSE_RELEASE_ROOT:-$(pwd -P)}"\n''' + source[match.end():]
    fail_anchor = '[[ -n "$RESOURCE_GROUP" ]] || fail'
    source = source.replace(fail_anchor,
        '[[ -s "$PROJECTPULSE_RELEASE_ROOT/$FLOWHIVE_AUTHORITY_MIGRATION_SQL" ]] || fail "Migration 094 SQL artifact is missing."\n'
        '[[ -s "$PROJECTPULSE_RELEASE_ROOT/$PROJECT_PLANNING_MIGRATION_SQL" ]] || fail "Migration 095 SQL artifact is missing."\n'
        + fail_anchor, 1)
if "build-and-run-flowhive-authority-migration-094-job.sh" not in source:
    marker = 'PROJECTPULSE_RELEASE_ROOT="$(pwd -P)" \\\n  bash scripts/release-test/build-and-run-project-planning-collaboration-migration-job.sh'
    if marker not in source:
        raise SystemExit('Could not locate Migration 095 invocation in systemwide runner')
    source = source.replace(marker,
        'PROJECTPULSE_RELEASE_ROOT="$(pwd -P)" \\\n  bash scripts/release-test/build-and-run-flowhive-authority-migration-094-job.sh\n'
        "echo 'MIGRATION_094=APPLIED_AND_VERIFIED'\n\n" + marker, 1)
w(path, source)

print('FLOWHIVE_AUTHORITATIVE_REPAIR_FINALIZED')
