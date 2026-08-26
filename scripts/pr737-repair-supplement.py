from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
manifest = ROOT / '.github/shared-project-document-planning-governed-release-files.txt'
lines = [line.strip() for line in manifest.read_text(encoding='utf-8').splitlines() if line.strip()]
required = {
    '.github/workflows/project-planning-identity-safe-admission-ci.yml',
    'database/migrations/097_project_planning_identity_safe_admission.sql',
    'database/rollback/097_project_planning_identity_safe_admission_rollback.sql',
    'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs',
    'tests/test-project-planning-identity-safe-admission-migration-097.sh',
    'tests/test-pulse-ai-runtime-job-query-shape.sh',
}
for path in required:
    if path not in lines:
        lines.append(path)
manifest.write_text('\n'.join(sorted(set(lines))) + '\n', encoding='utf-8')
print('GOVERNED_RELEASE_MANIFEST_UPDATED')
for path in sorted(required):
    print(f'GOVERNED {path}')
