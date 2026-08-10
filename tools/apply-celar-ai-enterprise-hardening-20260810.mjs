import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();

function absolute(relativePath) {
  return path.join(root, relativePath);
}

function read(relativePath) {
  return fs.readFileSync(absolute(relativePath), 'utf8');
}

function write(relativePath, content) {
  const file = absolute(relativePath);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content.replace(/\r\n/g, '\n'), 'utf8');
}

function replaceOnce(relativePath, search, replacement, label) {
  const current = read(relativePath);
  const first = current.indexOf(search);
  if (first < 0) throw new Error(`${label}: source pattern was not found in ${relativePath}`);
  if (current.indexOf(search, first + search.length) >= 0) {
    throw new Error(`${label}: source pattern was ambiguous in ${relativePath}`);
  }
  write(relativePath, current.slice(0, first) + replacement + current.slice(first + search.length));
}

function replaceRegexOnce(relativePath, expression, replacement, label) {
  const current = read(relativePath);
  const matches = [...current.matchAll(expression)];
  if (matches.length !== 1) {
    throw new Error(`${label}: expected one match in ${relativePath}, found ${matches.length}`);
  }
  write(relativePath, current.replace(expression, replacement));
}

const oracleWorkflow = '.github/workflows/celar-ai-oracle-test-runtime-deploy.yml';
const oldOracleVerification = `          AUTH=(-H "Authorization: Bearer $UAT_SESSION" -H "X-ProjectPulse-Session: $UAT_SESSION" -H 'X-ProjectPulse-Module-Number: 011' -H 'Cache-Control: no-cache')
          AVAILABLE=false
          for _attempt in $(seq 1 18); do
            PROBE="$(curl -fsS --max-time 600 "\${AUTH[@]}" -H 'Content-Type: application/json' -H "Origin: $BASE" -d '{}' "$BASE/api/ai-configuration/private-model/test" || true)"
            if jq -e '.status == "private_model_available" and .configured == true and .available == true' <<<"$PROBE" >/dev/null 2>&1; then AVAILABLE=true; break; fi
            sleep 10
          done
          [[ "$AVAILABLE" == true ]]
          PIPELINE="$(curl -fsS --max-time 90 "\${AUTH[@]}" "$BASE/api/celar-ai/v1/documents/pipeline/readiness")"
          jq -e '.readiness.malwareScanAttested == true and .readiness.malwareScannerMode == "celar_https_gateway" and .readiness.ocrEndpointConfigured == true and .readiness.privateEmbeddingEndpointConfigured == true and .readiness.privateVectorIndexConfigured == true' <<<"$PIPELINE" >/dev/null
          RUNTIME="$(curl -fsS --max-time 90 "\${AUTH[@]}" "$BASE/api/celar-ai/v1/documents/runtime/readiness")"
          jq -e '.readiness.clamAvConfigured == true and .readiness.malwareScannerEndpointPrivate == true and .readiness.ocrConfigured == true and .readiness.ocrEndpointPrivate == true and .readiness.embeddingConfigured == true and .readiness.embeddingEndpointPrivate == true' <<<"$RUNTIME" >/dev/null`;
const newOracleVerification = `          AUTH_064=(-H "Authorization: Bearer $UAT_SESSION" -H "X-ProjectPulse-Session: $UAT_SESSION" -H 'X-ProjectPulse-Module-Number: 064' -H 'Cache-Control: no-cache')
          AUTH_011=(-H "Authorization: Bearer $UAT_SESSION" -H "X-ProjectPulse-Session: $UAT_SESSION" -H 'X-ProjectPulse-Module-Number: 011' -H 'Cache-Control: no-cache')
          AVAILABLE=false
          PROBE_FILE="$RUNNER_TEMP/pulse-private-model-probe.json"
          PROBE_SAFE="$RUNNER_TEMP/pulse-private-model-probe-safe.json"
          for _attempt in $(seq 1 18); do
            PROBE_STATUS="$(curl -sS --max-time 600 -o "$PROBE_FILE" -w '%{http_code}' "\${AUTH_064[@]}" -H 'Content-Type: application/json' -H "Origin: $BASE" -d '{}' "$BASE/api/ai-configuration/private-model/test" || true)"
            if [[ "$PROBE_STATUS" == 200 ]] && jq -e '.status == "private_model_available" and .configured == true and .available == true' "$PROBE_FILE" >/dev/null 2>&1; then
              AVAILABLE=true
              break
            fi
            if [[ -s "$PROBE_FILE" ]] && jq -e . "$PROBE_FILE" >/dev/null 2>&1; then
              jq --arg httpStatus "$PROBE_STATUS" '{httpStatus:$httpStatus,status:(.status // null),configured:(.configured // null),available:(.available // null),diagnosticCode:(.diagnosticCode // .diagnostic // null),endpointReturned:false,tokenReturned:false}' "$PROBE_FILE" > "$PROBE_SAFE"
            else
              jq -n --arg httpStatus "\${PROBE_STATUS:-transport_error}" '{httpStatus:$httpStatus,status:"invalid_or_empty_response",configured:null,available:false,diagnosticCode:null,endpointReturned:false,tokenReturned:false}' > "$PROBE_SAFE"
            fi
            sleep 10
          done
          if [[ "$AVAILABLE" != true ]]; then
            echo 'Pulse Module 064 private-model verification did not pass. Sanitized diagnostic follows.' >&2
            [[ -s "$PROBE_SAFE" ]] && jq . "$PROBE_SAFE" >&2
            exit 1
          fi
          PIPELINE="$(curl -fsS --max-time 90 "\${AUTH_011[@]}" "$BASE/api/celar-ai/v1/documents/pipeline/readiness")"
          jq -e '.readiness.malwareScanAttested == true and .readiness.malwareScannerMode == "celar_https_gateway" and .readiness.ocrEndpointConfigured == true and .readiness.privateEmbeddingEndpointConfigured == true and .readiness.privateVectorIndexConfigured == true' <<<"$PIPELINE" >/dev/null
          RUNTIME="$(curl -fsS --max-time 90 "\${AUTH_011[@]}" "$BASE/api/celar-ai/v1/documents/runtime/readiness")"
          jq -e '.readiness.clamAvConfigured == true and .readiness.malwareScannerEndpointPrivate == true and .readiness.ocrConfigured == true and .readiness.ocrEndpointPrivate == true and .readiness.embeddingConfigured == true and .readiness.embeddingEndpointPrivate == true' <<<"$RUNTIME" >/dev/null`;
replaceOnce(oracleWorkflow, oldOracleVerification, newOracleVerification, 'Oracle Module 064 authorization correction');

const knowledgeCatalog = 'src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs';
replaceOnce(
  knowledgeCatalog,
  `        if (CelarAiInternalDataService.IsSupportedQuestion(question)) return true;\n\n        if (ContainsAny(normalized,`,
  `        if (CelarAiInternalDataService.IsSupportedQuestion(question)) return true;\n\n        // Public organization facts use an explicit allowlist and remain eligible\n        // only when no Pulse, customer, employee, project, document, or financial\n        // context is present. This check runs before the proper-name fail-closed\n        // rule so approved public entities are not mistaken for internal records.\n        if (CelarAiPublicEntityRegistry.IsGovernedPublicQuestion(question)) return false;\n\n        if (ContainsAny(normalized,`,
  'Public-entity scope classification');
replaceOnce(
  knowledgeCatalog,
  `        if (LooksLikeClearlyPublicOfficeholderQuestion(normalized)) return true;\n\n        if (LooksLikeNamedInternalSubject(raw)) return false;`,
  `        if (LooksLikeClearlyPublicOfficeholderQuestion(normalized)) return true;\n\n        if (CelarAiPublicEntityRegistry.IsGovernedPublicQuestion(question)) return true;\n\n        if (LooksLikeNamedInternalSubject(raw)) return false;`,
  'Public-entity explicit-public classification');

const routingFile = 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs';
replaceOnce(
  routingFile,
  `            || opening.StartsWith("check your organisation's", StringComparison.OrdinalIgnoreCase);\n    }\n}\n\npublic sealed class CelarAiCapabilityRouter`,
  `            || opening.StartsWith("check your organisation's", StringComparison.OrdinalIgnoreCase);\n    }\n\n    private static readonly string[] PublicAnswerLowConfidenceSignals =\n    [\n        "i am not sure", "i'm not sure", "i cannot verify", "i can't verify",\n        "i do not know", "i don't know", "insufficient information",\n        "insufficient evidence", "unable to confirm", "cannot confirm",\n        "may or may not", "might be", "possibly", "it appears that"\n    ];\n\n    public static bool TryRejectPublicAnswer(string? content, out string decisionCode)\n    {\n        if (LooksLikeNonAnswer(content))\n        {\n            decisionCode = "public_general_question_semantic_non_answer";\n            return true;\n        }\n\n        var normalized = Regex.Replace(content ?? string.Empty, @"\\s+", " ").Trim();\n        var wordCount = Regex.Matches(normalized, @"\\S+").Count;\n        if (wordCount < 7 || PublicAnswerLowConfidenceSignals.Any(signal =>\n                normalized.Contains(signal, StringComparison.OrdinalIgnoreCase)))\n        {\n            decisionCode = "public_general_question_low_confidence_answer";\n            return true;\n        }\n\n        decisionCode = "public_general_question_answer_quality_passed";\n        return false;\n    }\n}\n\npublic sealed class CelarAiCapabilityRouter`,
  'Public answer confidence gate');
replaceOnce(
  routingFile,
  `                if (privateResult.IsSuccess && !string.IsNullOrWhiteSpace(privateResult.Content))\n                {\n                    RecordAlreadyExecutedPrivateAttempt(`,
  `                if (privateResult.IsSuccess && !string.IsNullOrWhiteSpace(privateResult.Content))\n                {\n                    if (execution.PublicGeneralQuestion\n                        && CelarAiExternalAnswerQuality.TryRejectPublicAnswer(\n                            privateResult.Content,\n                            out var privatePublicQualityCode))\n                    {\n                        // Transport and privacy succeeded, but the answer did not\n                        // meet the enterprise public-answer confidence floor. Keep\n                        // the provider healthy and continue to Claude/OpenAI in the\n                        // persisted route instead of displaying a weak answer.\n                        _health.RecordSuccess(\n                            CelarAiCapabilityTargets.CelarAi,\n                            privateResult.Usage,\n                            privateResult.RequestId,\n                            privatePublicQualityCode,\n                            privateResult.RateLimits);\n                        _assurance.Record(\n                            feature,\n                            CelarAiCapabilityTargets.CelarAi,\n                            ProjectPulseAiOutcomes.Unavailable,\n                            execution.CorrelationId);\n                        failed.Add(target);\n                        decisions.Add(new(target, "failed", privatePublicQualityCode));\n                        continue;\n                    }\n\n                    RecordAlreadyExecutedPrivateAttempt(`,
  'Private public-answer confidence escalation');
replaceOnce(
  routingFile,
  `                if (execution.PublicGeneralQuestion\n                    && CelarAiExternalAnswerQuality.LooksLikeNonAnswer(result.Content))\n                {\n                    const string nonAnswerCode = "public_general_question_semantic_non_answer";`,
  `                if (execution.PublicGeneralQuestion\n                    && CelarAiExternalAnswerQuality.TryRejectPublicAnswer(\n                        result.Content,\n                        out var publicAnswerQualityCode))\n                {`,
  'External public-answer confidence escalation');
let routingContent = read(routingFile);
const nonAnswerCodeCount = (routingContent.match(/nonAnswerCode/g) ?? []).length;
if (nonAnswerCodeCount !== 3) {
  throw new Error(`External public-answer code replacement expected 3 remaining uses, found ${nonAnswerCodeCount}`);
}
routingContent = routingContent.replaceAll('nonAnswerCode', 'publicAnswerQualityCode');
write(routingFile, routingContent);

const mainFile = 'src/frontend/project-time-web/src/main.jsx';
replaceOnce(
  mainFile,
  `import './enterprise-contrast-guard.css';\n`,
  `import './enterprise-contrast-guard.css';\nimport './enterprise-theme-completion.css';\n`,
  'Global enterprise theme completion import');

const routingPanel = 'src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx';
replaceOnce(
  routingPanel,
  `<small>The endpoint must use a private IP, loopback, or approved private DNS suffix. The saved value is never returned.</small>`,
  `<small>\n              {deploymentManaged\n                ? 'This protected endpoint is deployment-managed. Its raw URL and credential remain hidden; the configured state and fingerprint above are authoritative.'\n                : 'The endpoint must use a private IP, loopback, or approved private DNS suffix. The saved value is never returned.'}\n            </small>`,
  'Private endpoint protected-status explanation');

write('src/backend/ProjectTime.Api/Ai/CelarAiPublicEntityRegistry.cs', `using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Explicit public-entity policy for isolated public fact questions. An entity
/// name is eligible only when it is approved here or in the deployment-owned
/// allowlist and the question contains no enterprise-record context.
/// </summary>
public static class CelarAiPublicEntityRegistry
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly string[] DefaultApprovedEntities =
    [
        "US Signal",
        "OneNeck IT Solutions",
        "Microsoft",
        "OpenAI",
        "Anthropic",
        "Oracle",
        "Amazon Web Services",
        "Google Cloud",
        "Cisco",
        "Broadcom",
        "VMware",
        "Five9",
        "Salesforce"
    ];

    private static readonly Regex PublicFactCue = new(
        @"\\b(?:who\\s+(?:is|are|was|were)\\s+(?:the\\s+)?(?:current\\s+)?(?:ceo|chief\\s+executive\\s+officer|president|founder|owner|chair(?:man|woman|person)?)\\s+(?:of|for)|where\\s+(?:is|are)\\s+(?:the\\s+)?(?:headquarters|headquarter|hq|main\\s+office)\\s+(?:of|for)|when\\s+was\\s+.+\\s+founded|what\\s+(?:is|are|does|do)\\s+.+\\s+(?:do|make|provide|sell|offer)|what\\s+(?:company\\s+)?(?:owns|acquired)\\s+|what\\s+is\\s+the\\s+(?:website|headquarters|parent\\s+company|ownership|industry)\\s+(?:of|for))\\b",
        Options,
        RegexTimeout);

    private static readonly Regex EnterpriseContextCue = new(
        @"\\b(?:pulse|celar|module|work\\s+register|flowhive|project\\s+forge|our|my|assigned|customer|client|employee|engineer|manager|timesheet|time\\s+entry|invoice|billing|expense|contract|statement\\s+of\\s+work|sow|global\\s+solution\\s+design|gsd|iqs|task|ticket|case|account\\s+id|user\\s+id|private|confidential|proprietary|internal\\s+system|internal\\s+record)\\b",
        Options,
        RegexTimeout);

    public static bool IsGovernedPublicQuestion(string? question) =>
        TryGetApprovedEntity(question, out _);

    public static bool TryGetApprovedEntity(string? question, out string entity)
    {
        entity = string.Empty;
        var value = question?.Trim() ?? string.Empty;
        if (value.Length is < 4 or > 800
            || value.Any(character => char.IsControl(character) && character is not '\\r' and not '\\n')
            || EnterpriseContextCue.IsMatch(value)
            || !PublicFactCue.IsMatch(value))
        {
            return false;
        }

        entity = ApprovedEntities()
            .OrderByDescending(candidate => candidate.Length)
            .FirstOrDefault(candidate => Regex.IsMatch(
                value,
                $@"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(candidate)}(?![\\p{{L}}\\p{{N}}])",
                Options,
                RegexTimeout)) ?? string.Empty;
        return entity.Length > 0;
    }

    public static IReadOnlyList<string> ApprovedEntities()
    {
        var configured = (Environment.GetEnvironmentVariable(
                "PROJECTPULSE_CELAR_AI_PUBLIC_ENTITY_ALLOWLIST") ?? string.Empty)
            .Split([',', ';', '\\n', '\\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.Length is >= 2 and <= 160)
            .Take(200);
        return DefaultApprovedEntities
            .Concat(configured)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
`);

write('src/frontend/project-time-web/src/enterprise-theme-completion.css', `/* Final semantic light/dark contract for shared Pulse surfaces.
   Loaded last so legacy component literals cannot create unreadable islands. */
:root {
  --pulse-theme-canvas: var(--uss-app-canvas, #eef4f8);
  --pulse-theme-surface: var(--uss-app-surface, #ffffff);
  --pulse-theme-raised: var(--uss-app-surface-raised, #ffffff);
  --pulse-theme-subtle: var(--uss-app-surface-subtle, #f4f7fb);
  --pulse-theme-control: var(--uss-app-control, #ffffff);
  --pulse-theme-border: var(--uss-app-border, #ced9e3);
  --pulse-theme-border-strong: var(--uss-app-border-strong, #8798aa);
  --pulse-theme-text: var(--uss-app-text, #172033);
  --pulse-theme-muted: var(--uss-app-muted, #52627a);
  --pulse-theme-link: var(--uss-app-link, #005ea8);
}

:root[data-theme='dark'],
body[data-theme='dark'] {
  --pulse-theme-canvas: #07111f;
  --pulse-theme-surface: #111f33;
  --pulse-theme-raised: #14243a;
  --pulse-theme-subtle: #18283d;
  --pulse-theme-control: #111f33;
  --pulse-theme-border: #33475e;
  --pulse-theme-border-strong: #71839c;
  --pulse-theme-text: #f4f8ff;
  --pulse-theme-muted: #b8c4d6;
  --pulse-theme-link: #75c2ff;
}

:where(
  .group7-provider-readiness,
  .ai-provider-center__notice,
  .ai-provider-center__automatic-health,
  .ai-provider-center__execution-policy,
  .ai-provider-center__state,
  .ai-provider-center__section,
  .ai-provider-center__locked,
  .ai-provider-center__summary article,
  .ai-provider-center__provider,
  .ai-provider-center__routes article,
  .celar-ai-routing__private-model,
  .celar-ai-routing__production-readiness,
  .celar-ai-routing__knowledge-fabric,
  .celar-ai-routing__route-card,
  .celar-ai-routing__consumer-table,
  .celar-ai-provider-bridge,
  .celar-ai-provider-bridge article,
  .enterprise-status-card,
  .enterprise-warning
) {
  border-color: var(--pulse-theme-border) !important;
  background-color: var(--pulse-theme-surface) !important;
  color: var(--pulse-theme-text) !important;
}

:where(
  .group7-provider-readiness__provider,
  .celar-ai-routing__private-summary article,
  .celar-ai-routing__knowledge-grid article,
  .celar-ai-routing__architecture article,
  .celar-ai-provider-bridge__summary article,
  .celar-ai-provider-bridge__architecture article,
  .celar-ai-provider-bridge__route-grid article
) {
  border-color: var(--pulse-theme-border) !important;
  background-color: var(--pulse-theme-raised) !important;
  color: var(--pulse-theme-text) !important;
}

:where(
  .group7-provider-readiness,
  .ai-provider-center,
  .celar-ai-routing,
  .celar-ai-provider-bridge,
  .projectpulse-module-standard,
  .uss-enterprise-module-page
) :is(h1, h2, h3, h4, strong, dt, dd, label, legend) {
  color: var(--pulse-theme-text) !important;
}

:where(
  .group7-provider-readiness,
  .ai-provider-center,
  .celar-ai-routing,
  .celar-ai-provider-bridge,
  .projectpulse-module-standard,
  .uss-enterprise-module-page
) :is(p, small, span, .muted, .subtitle, .helper-text, .supporting-text) {
  color: var(--pulse-theme-muted);
}

:where(
  .group7-provider-readiness,
  .ai-provider-center,
  .celar-ai-routing,
  .celar-ai-provider-bridge,
  .projectpulse-module-standard,
  .uss-enterprise-module-page
) :is(input, select, textarea) {
  border-color: var(--pulse-theme-border-strong) !important;
  background: var(--pulse-theme-control) !important;
  color: var(--pulse-theme-text) !important;
  opacity: 1 !important;
}

:where(
  .group7-provider-readiness,
  .ai-provider-center,
  .celar-ai-routing,
  .celar-ai-provider-bridge,
  .projectpulse-module-standard,
  .uss-enterprise-module-page
) :is(input, textarea)::placeholder {
  color: var(--pulse-theme-muted) !important;
  opacity: 1 !important;
}

:root[data-theme='dark'] :where(
  .ai-provider-center__notice,
  .ai-provider-center__automatic-health,
  .ai-provider-center__execution-policy,
  .ai-provider-center__state,
  .ai-provider-center__locked
),
body[data-theme='dark'] :where(
  .ai-provider-center__notice,
  .ai-provider-center__automatic-health,
  .ai-provider-center__execution-policy,
  .ai-provider-center__state,
  .ai-provider-center__locked
) {
  background-image: none !important;
  background-color: var(--pulse-theme-raised) !important;
  color: var(--pulse-theme-text) !important;
}

:root[data-theme='dark'] :where(
  .group7-provider-readiness__provider,
  .ai-provider-center__provider,
  .celar-ai-routing__route-card,
  .celar-ai-routing__consumer-table [role='row']
),
body[data-theme='dark'] :where(
  .group7-provider-readiness__provider,
  .ai-provider-center__provider,
  .celar-ai-routing__route-card,
  .celar-ai-routing__consumer-table [role='row']
) {
  background: var(--pulse-theme-raised) !important;
  color: var(--pulse-theme-text) !important;
}

:root[data-theme='dark'] :where(
  .group7-provider-readiness__provider dt,
  .group7-provider-readiness__provider dd,
  .group7-provider-readiness__provider > div strong,
  .group7-provider-readiness__provider > div span,
  .ai-provider-center__secret-form small,
  .ai-provider-center__model-form small,
  .ai-provider-center__enable-control small
),
body[data-theme='dark'] :where(
  .group7-provider-readiness__provider dt,
  .group7-provider-readiness__provider dd,
  .group7-provider-readiness__provider > div strong,
  .group7-provider-readiness__provider > div span,
  .ai-provider-center__secret-form small,
  .ai-provider-center__model-form small,
  .ai-provider-center__enable-control small
) {
  color: var(--pulse-theme-text) !important;
}

:root[data-theme='dark'] .ai-provider-center__status--inactive,
body[data-theme='dark'] .ai-provider-center__status--inactive {
  background: #27364a !important;
  color: #e5edf8 !important;
}

:root[data-theme='dark'] .ai-provider-center__status--checking,
body[data-theme='dark'] .ai-provider-center__status--checking {
  background: #173d67 !important;
  color: #d8ecff !important;
}

:root[data-theme='dark'] .ai-provider-center__status--healthy,
body[data-theme='dark'] .ai-provider-center__status--healthy {
  background: #183f31 !important;
  color: #d9f8e7 !important;
}

:root[data-theme='dark'] .ai-provider-center__status--degraded,
body[data-theme='dark'] .ai-provider-center__status--degraded {
  background: #4b3513 !important;
  color: #ffedc4 !important;
}

.enterprise-top-bar .pulse-header-theme-switcher,
:root[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher,
body[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher {
  opacity: 1 !important;
  visibility: visible !important;
  filter: none !important;
  border-color: rgba(0, 75, 141, 0.28) !important;
  background: #f4f9fe !important;
}

.enterprise-top-bar .pulse-header-theme-switcher button,
:root[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher button,
body[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher button {
  color: #31546f !important;
  opacity: 1 !important;
  -webkit-text-fill-color: currentColor;
}

.enterprise-top-bar .pulse-header-theme-switcher button.active,
.enterprise-top-bar .pulse-header-theme-switcher button[aria-pressed='true'],
:root[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher button.active,
:root[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher button[aria-pressed='true'],
body[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher button.active,
body[data-theme='dark'] .enterprise-top-bar .pulse-header-theme-switcher button[aria-pressed='true'] {
  background: #004b8d !important;
  color: #ffffff !important;
}

:where(.projectpulse-module-standard, .uss-enterprise-module-page) a {
  color: var(--pulse-theme-link);
}

:where(.projectpulse-module-standard, .uss-enterprise-module-page)
  :is(button, input, select, textarea, a, summary):focus-visible {
  outline: 3px solid var(--uss-app-focus, #70d7ff) !important;
  outline-offset: 2px;
}
`);

write('src/frontend/project-time-web/scripts/validate-celar-ai-enterprise-hardening.mjs', `import fs from 'node:fs';

function text(path) {
  return fs.readFileSync(path, 'utf8');
}

function requireContains(content, marker, label) {
  if (!content.includes(marker)) throw new Error(\`Missing \${label}: \${marker}\`);
}

const workflow = text('.github/workflows/celar-ai-oracle-test-runtime-deploy.yml');
requireContains(workflow, "X-ProjectPulse-Module-Number: 064", 'Module 064 activation authorization');
requireContains(workflow, 'pulse-private-model-probe-safe.json', 'sanitized private-model activation diagnostic');
requireContains(workflow, 'AUTH_011', 'separate document-runtime authorization boundary');

const registry = text('src/backend/ProjectTime.Api/Ai/CelarAiPublicEntityRegistry.cs');
requireContains(registry, 'US Signal', 'approved public entity');
requireContains(registry, 'PROJECTPULSE_CELAR_AI_PUBLIC_ENTITY_ALLOWLIST', 'deployment-managed public entity allowlist');
requireContains(registry, 'EnterpriseContextCue', 'private-context fail-closed boundary');

const knowledge = text('src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs');
if ((knowledge.match(/CelarAiPublicEntityRegistry\.IsGovernedPublicQuestion/g) ?? []).length !== 2) {
  throw new Error('Public entity classification must be applied at both scope and explicit-public boundaries.');
}

const router = text('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs');
requireContains(router, 'public_general_question_low_confidence_answer', 'low-confidence public answer code');
if ((router.match(/TryRejectPublicAnswer/g) ?? []).length < 3) {
  throw new Error('Public answer quality must gate private, Claude, and OpenAI routing.');
}
requireContains(router, 'continue to Claude/OpenAI', 'private low-confidence escalation explanation');

const main = text('src/frontend/project-time-web/src/main.jsx');
requireContains(main, "import './enterprise-theme-completion.css';", 'global theme completion import');
const theme = text('src/frontend/project-time-web/src/enterprise-theme-completion.css');
for (const marker of ['group7-provider-readiness', 'ai-provider-center__provider', 'celar-ai-routing__route-card', "data-theme='dark'", 'pulse-header-theme-switcher']) {
  requireContains(theme, marker, 'theme completion selector');
}

const panel = text('src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx');
requireContains(panel, 'This protected endpoint is deployment-managed', 'protected endpoint explanation');

console.log('CELAR_AI_ENTERPRISE_HARDENING_VALIDATION=PASS');
`);

write('docs/modules/module-011-pulse-ai/CELAR-AI-ENTERPRISE-QUESTION-ROUTING.md', `# Celar AI enterprise question routing and privacy boundary

Celar AI uses one private-first routing authority for Module 011, Module 064, and every AI-enabled consumer. The default target order remains:

1. private Celar AI and governed Pulse tools;
2. Claude when the question is eligible and the prior answer is unavailable or below the public-answer confidence floor;
3. OpenAI when Claude is unavailable or below that floor; and
4. the governed local answer.

A safety refusal stops routing. It is never bypassed by a later provider.

## Public organization facts

Company-specific public questions are eligible only when the organization is present in the deployment-owned public entity allowlist and the question contains no Pulse, customer, employee, project, document, financial, account, ticket, or internal-system context. The built-in registry includes US Signal and commonly referenced public technology providers. Additional names use:

\`PROJECTPULSE_CELAR_AI_PUBLIC_ENTITY_ALLOWLIST\`

This is a public-entity exception, not a general proper-name exception. Ambiguous names remain private and fail closed.

## External data-loss prevention

Claude and OpenAI receive the original question only through the isolated public-question path. Every other external route receives a closed backend-owned purpose capsule. Raw SOW/GSD text, attachments, retrieved chunks, customer or employee identities, project records, financial values, credentials, internal hosts, and identifiers remain inside Pulse.

Provider output is checked again before display. A response that reintroduces protected terms, credentials, identifiers, or unsafe named entities is rejected. Public answers that are semantic non-answers, extremely short, or contain governed low-confidence signals are not promoted; routing continues to the next approved provider.

## Runtime activation

The protected Test activation controller uses Module 064 authorization for the private-model probe and Module 011 authorization for the document-runtime readiness endpoints. Failure evidence contains only allowlisted status and diagnostic fields; endpoint URLs and credentials are never returned.

## Theme contract

The final shared stylesheet is loaded after legacy module styles. It enforces semantic surfaces, controls, borders, text, muted text, status chips, focus indicators, and visible Light/Dark controls across Module 064, Celar AI, shared enterprise panels, and standard routed modules.
`);

console.log('CELAR_AI_ENTERPRISE_HARDENING_PATCH=APPLIED');
