#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path

CLARIFICATION_OLD = '''        if (plan.ClarificationsToRequest.Count > 0)
        {
            findings.Add(Review(
                "clarification_recommended",
                "The planner identified missing scope that could change the answer.",
                "Ask the listed clarification when authoritative resolution cannot be completed safely."));
        }'''

CLARIFICATION_NEW = '''        // Planner clarifications are pre-retrieval safeguards. Once the
        // required authoritative evidence has been retrieved, cited, and has
        // passed every blocker-class gate, that evidence has resolved the
        // missing scope and must not keep an otherwise verified answer in a
        // review-required state. Missing, stale, unauthorized, or incomplete
        // evidence still retains the clarification review.
        if (plan.ClarificationsToRequest.Count > 0
            && (successfulSources.Length == 0
                || findings.Any(finding => finding.Severity == "blocker")))
        {
            findings.Add(Review(
                "clarification_recommended",
                "The planner identified missing scope that could change the answer and authoritative evidence did not fully resolve it.",
                "Ask the listed clarification when authoritative resolution cannot be completed safely."));
        }'''

SUCCESS_OLD = '''        var successfulSources = result.Sources.Where(IsSuccessfulSource).ToArray();'''
SUCCESS_NEW = '''        // A provider HTTP 200 proves only that generation completed. It is
        // never promoted into an authoritative source receipt. Public-current
        // facts require an allowlisted retrieval-time official web source.
        var successfulSources = result.Sources
            .Where(source => IsAuthoritativeSource(source, plan))
            .ToArray();'''

CURRENT_OLD = '''        var currentPublicVerified = plan.QuestionClass != CelarAiAnswerQuestionClass.PublicCurrent
            || successfulSources.Any(source =>
                !source.Freshness.Contains("not_live", StringComparison.OrdinalIgnoreCase)
                && (source.Freshness.Contains("current", StringComparison.OrdinalIgnoreCase)
                    || source.Freshness.Contains("retrieved", StringComparison.OrdinalIgnoreCase)
                    || source.Freshness.Contains("live", StringComparison.OrdinalIgnoreCase)));'''
CURRENT_NEW = '''        var currentPublicVerified = plan.QuestionClass != CelarAiAnswerQuestionClass.PublicCurrent
            || successfulSources.Any(IsAuthoritativeCurrentPublicSource);'''

CURRENT_FINDING_OLD = '''        if (!currentPublicVerified)
        {
            findings.Add(Blocker(
                "current_public_fact_not_live_verified",
                "The question requests a changing public fact, but the response has no live or retrieval-time public evidence.",
                "Use the governed current-public-information route and cite retrieval-time sources."));
        }'''
CURRENT_FINDING_NEW = '''        if (!currentPublicVerified)
        {
            findings.Add(Blocker(
                "current_public_fact_not_live_verified",
                "The question requests a changing public fact, but the response has no allowlisted retrieval-time official evidence.",
                "Use the governed current-public-information route and cite retrieval-time official sources."));
        }
        var unsupportedMaterialClaims = plan.QuestionClass == CelarAiAnswerQuestionClass.PublicCurrent
            ? MaterialClaimsWithoutCitation(result.Answer, validCitationIds)
            : Array.Empty<string>();
        if (unsupportedMaterialClaims.Count > 0)
        {
            findings.Add(Blocker(
                "material_claim_citation_support_missing",
                $"{unsupportedMaterialClaims.Count} material public-current claim or claims do not map to a retrieved authoritative source citation.",
                "Attach an inline [source-id] citation to every material factual sentence or remove the unsupported claim."));
        }'''

CONFLICT_OLD = '''        if (result.Answer.Conflicts.Count > 0)
        {
            findings.Add(Review(
                "conflicting_evidence_requires_review",
                $"The answer contains {result.Answer.Conflicts.Count} unresolved evidence conflict or conflicts.",
                "Present the conflict explicitly and require an owning-module or human authority decision."));
        }'''
CONFLICT_NEW = '''        if (result.Answer.Conflicts.Count > 0)
        {
            findings.Add(Blocker(
                "conflicting_evidence_requires_review",
                $"The answer contains {result.Answer.Conflicts.Count} unresolved evidence conflict or conflicts.",
                "Block answer promotion, present the conflict explicitly, and require an authoritative or human resolution."));
        }'''

HELPER_ANCHOR = '''    private static bool IsSuccessfulSource(PulseAiSystemSourceEvidence source) =>'''
HELPERS = '''    private static bool IsAuthoritativeSource(
        PulseAiSystemSourceEvidence source,
        CelarAiUniversalAnswerPlan plan)
    {
        if (!IsSuccessfulSource(source)) return false;
        if (source.SourceType.Equals("governed_public_ai", StringComparison.OrdinalIgnoreCase)
            || source.SourceType.Equals("governed_private_ai", StringComparison.OrdinalIgnoreCase)
            || source.SourceType.Equals("provider_knowledge", StringComparison.OrdinalIgnoreCase)
            || source.SourceType.Equals("narrative_provider_response", StringComparison.OrdinalIgnoreCase)
            || source.Path.StartsWith("module064:public-general-knowledge", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return plan.QuestionClass != CelarAiAnswerQuestionClass.PublicCurrent
            || IsAuthoritativeCurrentPublicSource(source);
    }

    private static bool IsAuthoritativeCurrentPublicSource(PulseAiSystemSourceEvidence source)
    {
        if (!source.SourceType.Equals("authoritative_public_web", StringComparison.OrdinalIgnoreCase)
            || !source.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || !source.Freshness.Equals("live_retrieved_current", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return Uri.TryCreate(source.Path, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static IReadOnlyList<string> MaterialClaimsWithoutCitation(
        PulseAiSystemDetailedAnswer answer,
        IReadOnlyList<int> validCitationIds)
    {
        var claims = new[] { answer.DirectConclusion, answer.ExecutiveSummary }
            .Concat(answer.CurrentState)
            .Concat(answer.DetailedAnalysis)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (claims.Length == 0) return [];
        var tokens = validCitationIds.Select(id => $"[{id}]").ToArray();
        return claims
            .Where(claim => tokens.Length == 0
                || !tokens.Any(token => claim.Contains(token, StringComparison.Ordinal)))
            .ToArray();
    }

'''


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected exactly one anchor, found {count}')
    return source.replace(old, new, 1)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    source_path = Path(args.input)
    output_path = Path(args.output)
    generated = source_path.read_text(encoding='utf-8')
    generated = replace_once(generated, CLARIFICATION_OLD, CLARIFICATION_NEW, 'clarification')
    generated = replace_once(generated, SUCCESS_OLD, SUCCESS_NEW, 'authoritative source filter')
    generated = replace_once(generated, CURRENT_OLD, CURRENT_NEW, 'current public verification')
    generated = replace_once(generated, CURRENT_FINDING_OLD, CURRENT_FINDING_NEW, 'claim support gate')
    generated = replace_once(generated, CONFLICT_OLD, CONFLICT_NEW, 'source conflict blocker')
    generated = replace_once(generated, HELPER_ANCHOR, HELPERS + HELPER_ANCHOR, 'authoritative helper insertion')
    generated = replace_once(
        generated,
        '                or "tool_timeout_budget_exceeded");',
        '                or "tool_timeout_budget_exceeded"\n                or "conflicting_evidence_requires_review"\n                or "material_claim_citation_support_missing");',
        'fail-closed conclusion list')

    required_markers = [
        'IsAuthoritativeSource(source, plan)',
        'source.SourceType.Equals("governed_public_ai"',
        'source.SourceType.Equals("authoritative_public_web"',
        'material_claim_citation_support_missing',
        'findings.Add(Blocker(\n                "conflicting_evidence_requires_review"',
        'findings.Any(finding => finding.Severity == "blocker")'
    ]
    missing = [marker for marker in required_markers if marker not in generated]
    if missing:
        raise SystemExit(f'generated reliability source is missing markers: {missing}')

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(generated, encoding='utf-8')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
