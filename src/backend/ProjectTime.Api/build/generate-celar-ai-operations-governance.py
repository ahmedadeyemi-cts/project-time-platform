#!/usr/bin/env python3
"""Generate compiler copies of large Celar AI operations sources.

Canonical files remain readable. This deterministic transform removes the old
unscoped duplicate-search route, enforces effective-user read scope, separates
observe-only monitoring from machine creation, validates typed evidence, uses a
stable incident fingerprint, and delegates scheduled probes to the real probe
adapter. Every required anchor fails closed.
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def replace_once(source: str, before: str, after: str, label: str) -> str:
    count = source.count(before)
    if count != 1:
        raise SystemExit(f"CELAR_AI_OPERATIONS_GENERATOR_{label}=FAILED expected=1 actual={count}")
    return source.replace(before, after, 1)


def generate_module(source: str) -> str:
    source = replace_once(
        source,
        '''        group.MapGet("/defects/matches",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, string?, string?, string?, string?, CancellationToken, Task<IResult>>)FindMatchingDefectsAsync);
''',
        '',
        'REMOVE_UNSCOPED_MATCH_ROUTE',
    )
    source = replace_once(
        source,
        '''                defectNumber,
                access.Actual,
                CanViewAllDefects(access),''',
        '''                defectNumber,
                access.Effective,
                CanViewAllDefects(access),''',
        'EFFECTIVE_USER_DEFECT_READ',
    )
    source = source.replace(
        'automaticMonitoringEnabled = CelarAiOperationsPolicy.AutomaticMonitoringEnabled,',
        'featureFlags = CelarAiOperationalFeatureFlags.PublicState(),',
    )
    source = source.replace(
        'deploymentLevelMonitoringEnabled = CelarAiOperationsPolicy.AutomaticMonitoringEnabled,',
        '''deploymentLevelMonitoringEnabled = CelarAiOperationalFeatureFlags.MonitoringEnabled,
                deploymentLevelAutomaticDefectsEnabled = CelarAiOperationalFeatureFlags.AutomaticDefectsEnabled,''',
    )
    if 'group.MapGet("/defects/matches"' in source:
        raise SystemExit('CELAR_AI_OPERATIONS_GENERATOR_UNSCOPED_ROUTE_REMAINS=FAILED')
    if 'defectNumber,\n                access.Actual,' in source:
        raise SystemExit('CELAR_AI_OPERATIONS_GENERATOR_ACTUAL_READ_SCOPE_REMAINS=FAILED')
    return source


def generate_orchestration(source: str) -> str:
    source = replace_once(
        source,
        '''    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CelarAiCapabilityRoutingStore _routing;
    private readonly ILogger<CelarAiDefectOrchestrationService> _logger;''',
        '''    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CelarAiCapabilityRoutingStore _routing;
    private readonly CelarAiRealProbeService _realProbes;
    private readonly ILogger<CelarAiDefectOrchestrationService> _logger;''',
        'REAL_PROBE_FIELD',
    )
    source = replace_once(
        source,
        '''    public CelarAiDefectOrchestrationService(
        IHttpClientFactory httpClientFactory,
        CelarAiCapabilityRoutingStore routing,
        ILogger<CelarAiDefectOrchestrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _routing = routing;
        _logger = logger;
    }''',
        '''    public CelarAiDefectOrchestrationService(
        IHttpClientFactory httpClientFactory,
        CelarAiCapabilityRoutingStore routing,
        CelarAiRealProbeService realProbes,
        ILogger<CelarAiDefectOrchestrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _routing = routing;
        _realProbes = realProbes;
        _logger = logger;
    }''',
        'REAL_PROBE_CONSTRUCTOR',
    )
    source = replace_once(
        source,
        '        if (!CelarAiOperationsPolicy.AutomaticMonitoringEnabled) return;',
        '        if (!CelarAiOperationalFeatureFlags.MonitoringEnabled) return;',
        'OBSERVE_ONLY_MONITORING',
    )
    source = replace_once(
        source,
        '''        var document = request.EvidenceDocument is { ValueKind: JsonValueKind.Object } element
            ? JsonSerializer.Deserialize<object>(element.GetRawText(), Json) ?? new { }
            : new { };''',
        '''        var document = CelarAiTypedEvidencePolicy.Normalize(request.EvidenceDocument);''',
        'TYPED_EVIDENCE',
    )
    fingerprint_pattern = re.compile(
        r'''        var fingerprint = Fingerprint\(\n            policy\.Environment,\n            policy\.ComponentCode,\n            evidence\.ProbeCode,\n            evidence\.FailureCode,\n            CelarAiOperationsPolicy\.ReleaseSha\(\)\);'''
    )
    source, count = fingerprint_pattern.subn(
        '''        var fingerprint = Fingerprint(
            "v2",
            policy.Environment,
            policy.PolicyCode,
            policy.ComponentCode);''',
        source,
        count=1,
    )
    if count != 1:
        raise SystemExit(f'CELAR_AI_OPERATIONS_GENERATOR_STABLE_FINGERPRINT=FAILED actual={count}')

    probe_pattern = re.compile(
        r'''    private async Task<CelarAiProbeEvidence> ProbeForPolicyAsync\(\n        CelarAiMonitorPolicy policy,\n        CancellationToken cancellationToken\) => policy\.PolicyCode switch\n    \{.*?\n    \};''',
        re.S,
    )
    source, count = probe_pattern.subn(
        '''    private Task<CelarAiProbeEvidence> ProbeForPolicyAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken) =>
        _realProbes.RunAsync(policy, cancellationToken);''',
        source,
        count=1,
    )
    if count != 1:
        raise SystemExit(f'CELAR_AI_OPERATIONS_GENERATOR_REAL_PROBE_DELEGATION=FAILED actual={count}')

    source = source.replace(
        'automaticMonitoringEnabled = CelarAiOperationsPolicy.AutomaticMonitoringEnabled,',
        'featureFlags = CelarAiOperationalFeatureFlags.PublicState(),',
    )
    source = source.replace(
        '''        var lastFailure = await command.ExecuteScalarAsync(cancellationToken);
        return lastFailure is null or DBNull
            || observedAt - (DateTimeOffset)lastFailure >= TimeSpan.FromSeconds(policy.RecoveryStabilitySeconds);''',
        '''        var lastFailure = await command.ExecuteScalarAsync(cancellationToken);
        if (lastFailure is null or DBNull) return true;
        var lastFailureAt = lastFailure switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("The recovery timestamp could not be interpreted safely.")
        };
        return observedAt - lastFailureAt >= TimeSpan.FromSeconds(policy.RecoveryStabilitySeconds);''',
    )
    for marker in (
        'CelarAiTypedEvidencePolicy.Normalize',
        'CelarAiOperationalFeatureFlags.MonitoringEnabled',
        '"v2",\n            policy.Environment,\n            policy.PolicyCode,',
        '_realProbes.RunAsync',
    ):
        if marker not in source:
            raise SystemExit(f'CELAR_AI_OPERATIONS_GENERATOR_MARKER=FAILED marker={marker}')
    return source


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--mode', choices=('module', 'orchestration'), required=True)
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    source = Path(args.input).read_text(encoding='utf-8')
    generated = generate_module(source) if args.mode == 'module' else generate_orchestration(source)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(generated, encoding='utf-8')


if __name__ == '__main__':
    main()
