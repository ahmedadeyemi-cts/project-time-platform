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
    r'@"^(?:https://[a-z0-9-]+\\.vault\\.azure\\.net/secrets/[A-Za-z0-9-]+/[A-Za-z0-9-]{16,}|secretref://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,})$",',
    r'@"^(?:https://[a-z0-9-]+\\.vault\\.azure\\.net/secrets/[A-Za-z0-9-]+/[A-Za-z0-9-]{16,}|secretref://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,}|github-environment://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,})$",',
    "versioned secret-reference regex",
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
policy_marker = "  'TryGetPinnedAddress',\n"
if validator_text.count(policy_marker) != 1:
    raise SystemExit("Could not locate the external HTTPS policy marker list.")
validator_text = validator_text.replace(
    policy_marker,
    policy_marker + "  'github-environment://',\n",
    1,
)

workflow_marker = "  'PROJECTPULSE_TEST_CELAR_AI_ORACLE_RUNTIME_TOKEN',\n"
if validator_text.count(workflow_marker) != 1:
    raise SystemExit("Could not locate the Oracle workflow marker list.")
validator_text = validator_text.replace(
    workflow_marker,
    workflow_marker
    + "  'github-environment://test/celar-ai-oracle-runtime-token@',\n"
    + "  'PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN=\"secretref:$TOKEN_SECRET_NAME\"',\n",
    1,
)

reject_marker = "rejectText(workflow, 'curl -k', 'TLS verification bypass')\n"
if validator_text.count(reject_marker) != 1:
    raise SystemExit("Could not locate the Oracle workflow rejection checks.")
validator_text = validator_text.replace(
    reject_marker,
    reject_marker
    + "rejectText(workflow, 'TOKEN_REFERENCE=\"secretref://', 'Azure-reserved secretref metadata prefix')\n",
    1,
)
validator.write_text(validator_text, encoding="utf-8")

print(
    "Oracle Container Apps secret binding corrected: native secretref remains only on token values, "
    "while immutable GitHub Environment provenance uses a literal non-reserved URI."
)
