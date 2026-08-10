from __future__ import annotations

from pathlib import Path


def replace_once(path: str, old: str, new: str, label: str) -> None:
    file_path = Path(path)
    text = file_path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one {label} in {path}, found {count}.")
    file_path.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/backend/ProjectTime.Api/Ai/ProjectPulseAiReleaseRuntimePolicy.cs",
    r'|secretref://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,})$",',
    r'|secretref://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,}|github-environment://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,})$",',
    "versioned secret-reference regex suffix",
)

replace_once(
    ".github/workflows/celar-ai-oracle-test-runtime-deploy.yml",
    'TOKEN_REFERENCE="secretref://github-environment-test/celar-ai-oracle-runtime-token@$TARGET_RELEASE_COMMIT"',
    'TOKEN_REFERENCE="github-environment://test/celar-ai-oracle-runtime-token@$TARGET_RELEASE_COMMIT"',
    "Oracle token provenance assignment",
)

replace_once(
    "tests/CelarAiOracleExternalRuntimeTests/Program.cs",
    'const string TokenReference = "secretref://github-environment-test/celar-ai-oracle-runtime-token@1111111111111111111111111111111111111111";',
    'const string TokenReference = "github-environment://test/celar-ai-oracle-runtime-token@1111111111111111111111111111111111111111";',
    "Oracle behavioral test token provenance",
)

replace_once(
    "tests/CelarAiProductionHardeningTests/ReleaseRuntimeBehavior.cs",
    'Set("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE", "secretref://ci/celar-token@version-0001");',
    'Set("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE", "github-environment://ci/celar-token@version-0001");',
    "release-runtime GitHub Environment provenance test",
)

validator = Path("tests/validate-celar-ai-oracle-test-runtime.mjs")
validator_text = validator.read_text(encoding="utf-8")

read_marker = "const policy = read('src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs')\n"
if validator_text.count(read_marker) != 1:
    raise SystemExit("Could not locate the external HTTPS policy reader.")
validator_text = validator_text.replace(
    read_marker,
    read_marker
    + "const releasePolicy = read('src/backend/ProjectTime.Api/Ai/ProjectPulseAiReleaseRuntimePolicy.cs')\n",
    1,
)

policy_assertion_marker = (
    "]) requireText(policy, marker, 'external HTTPS policy')\n\n"
    "requireText(services, 'PulseAiExternalRuntimeReadiness', 'authenticated startup readiness client')\n"
)
if validator_text.count(policy_assertion_marker) != 1:
    raise SystemExit("Could not locate the external HTTPS policy assertion boundary.")
validator_text = validator_text.replace(
    policy_assertion_marker,
    "]) requireText(policy, marker, 'external HTTPS policy')\n\n"
    "requireText(releasePolicy, 'github-environment://', 'GitHub Environment token provenance scheme')\n"
    "requireText(services, 'PulseAiExternalRuntimeReadiness', 'authenticated startup readiness client')\n",
    1,
)

workflow_assertion_marker = "rejectText(workflow, 'curl -k', 'TLS verification bypass')\n"
if validator_text.count(workflow_assertion_marker) != 1:
    raise SystemExit("Could not locate the Oracle workflow rejection checks.")
validator_text = validator_text.replace(
    workflow_assertion_marker,
    "requireText(workflow, 'github-environment://test/celar-ai-oracle-runtime-token@', 'literal GitHub Environment token provenance')\n"
    "requireText(workflow, 'PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN=\"secretref:$TOKEN_SECRET_NAME\"', 'native Container Apps token binding')\n"
    + workflow_assertion_marker
    + "rejectText(workflow, 'TOKEN_REFERENCE=\"secretref://', 'Azure-reserved secretref metadata prefix')\n",
    1,
)
validator.write_text(validator_text, encoding="utf-8")

print(
    "Oracle Container Apps secret binding corrected: native secretref remains only on token values, "
    "while immutable GitHub Environment provenance uses a literal non-reserved URI."
)
