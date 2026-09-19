import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = "fd38a04d2e0dc171bbf52c9066db7dc3d99e72b9";
const expected = [
  "scripts/ci/validate-module030-source-boundary.sh",
  ".github/workflows/module025-protected-uat-control.yml",
  ".github/workflows/projectpulse-deploy-test.yml",
  "scripts/release-test/run-module025-export-uat.py",
  "tests/module025-export-deploy.test.py",
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-export-ci.yml",
  "docs/releases/2026-09-19-module025-standard-download-formats.md",
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh",
  "scripts/release-test/run-module025-installed-sa-uat.py",
  "scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh",
  "scripts/release-test/validate-module025-governed-release.sh",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/backend/ProjectTime.Api/Assets/Templates/Module025StandardGsd.xlsx",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdContracts.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdDocumentExporter.cs",
  "src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs",
  "src/backend/ProjectTime.Api/Modules/Module025StandardGsdExporter.cs",
  "src/backend/ProjectTime.Api/Modules/Module025TaskEstimates.cs",
  "src/backend/ProjectTime.Api/ProjectTime.Api.csproj",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/sow-gsd-workspace.css",
  "src/frontend/project-time-web/src/module025/task-estimates.js",
  "tests/Module025ExportTests/Module025ExportTests.csproj",
  "tests/Module025ExportTests/Program.cs",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-standard-download-formats-scope.mjs",
  "tests/module025-task-editor.test.mjs",
  "tests/module025-task-estimates.test.mjs",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
const registrations = {
  "scripts/ci/validate-module030-source-boundary.sh": [["if [[ \"$HEAD_BRANCH\" == fix/module025-private-generation-recovery ]]; then", "if [[ \"$HEAD_BRANCH\" == fix/module025-standard-download-formats-20260919 ]]; then\n  node tests/module025-standard-download-formats-scope.mjs\n  exit 0\nelif [[ \"$HEAD_BRANCH\" == fix/module025-private-generation-recovery ]]; then"]],
  "scripts/ci/validate-celar-ai-enterprise-source-boundary.sh": [
    [
      "if [[ \"$HEAD_BRANCH\" == fix/module025-private-generation-recovery ]]; then",
      "if [[ \"$HEAD_BRANCH\" == fix/module025-standard-download-formats-20260919 ]]; then\n  node tests/module025-standard-download-formats-scope.mjs\n  exit 0\nelif [[ \"$HEAD_BRANCH\" == fix/module025-private-generation-recovery ]]; then"
    ]
  ],
  "scripts/release-test/validate-protected-test-controller-branches.sh": [
    [
      "if [[ \"$HEAD_BRANCH\" == fix/connectwise-sell-migration-replay ]]; then",
      "if [[ \"$HEAD_BRANCH\" == fix/module025-standard-download-formats-20260919 ]]; then\n  node tests/module025-standard-download-formats-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ \"$HEAD_BRANCH\" == fix/connectwise-sell-migration-replay ]]; then"
    ]
  ],
  "scripts/release-test/validate-module025-governed-release.sh": [
    [
      "if [[ \"$HEAD_BRANCH\" == fix/module025-private-generation-recovery ]]; then",
      "if [[ \"$HEAD_BRANCH\" == fix/module025-standard-download-formats-20260919 ]]; then\n  node tests/module025-standard-download-formats-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n  exit 0\nelif [[ \"$HEAD_BRANCH\" == fix/module025-private-generation-recovery ]]; then"
    ]
  ],
  ".github/workflows/flowhive-psa-release-control-ci.yml": [
    [
      "          if [[ \"$GITHUB_HEAD_REF\" == fix/module025-private-generation-recovery ]]; then",
      "          if [[ \"$GITHUB_HEAD_REF\" == fix/module025-standard-download-formats-20260919 ]]; then\n            node tests/module025-standard-download-formats-scope.mjs\n          elif [[ \"$GITHUB_HEAD_REF\" == fix/module025-private-generation-recovery ]]; then"
    ]
  ],
  "tests/flowhive-psa-admission.test.mjs": [
    ["    if (privateGenerationCorrection) {", "    if (module025StandardExports) {\n      protectedPaths.splice(protectedPaths.indexOf('.github/workflows/projectpulse-deploy-test.yml'), 1);\n      execFileSync('node', ['tests/module025-standard-download-formats-scope.mjs']);\n    }\n    if (privateGenerationCorrection) {"],
    [
      "const privateGenerationCorrection =",
      "const module025StandardExports = process.env.GITHUB_HEAD_REF === 'fix/module025-standard-download-formats-20260919';\nconst privateGenerationCorrection ="
    ],
    [
      "  || privateGenerationCorrection ||",
      "  || module025StandardExports || privateGenerationCorrection ||"
    ],
    [
      "const module025VerifierBase = privateGenerationCorrection ?",
      "const module025VerifierBase = module025StandardExports ? 'fd38a04d2e0dc171bbf52c9066db7dc3d99e72b9' : privateGenerationCorrection ?"
    ]
  ],
  "tests/validate-celar-ai-pr630-consolidated.mjs": [
    [
      "const module025RetainedRegisterMode =",
      "const module025StandardExportsMode = branchName === 'fix/module025-standard-download-formats-20260919';\nif (module025StandardExportsMode) await import('./module025-standard-download-formats-scope.mjs');\nconst module025RetainedRegisterMode ="
    ],
    [
      "const scopedCompatibilityMode = privateGenerationMode ||",
      "const scopedCompatibilityMode = module025StandardExportsMode || privateGenerationMode ||"
    ]
  ],
  "scripts/release-test/run-module025-installed-sa-uat.py": [
    [
      "        reviewed_phases = [phase_payload(phase) for phase in (current.get(\"phases\") or [])]",
      "        reviewed_phases = [phase_payload(phase) for phase in (current.get(\"phases\") or [])]\n        # Synthetic reviewer allocation only; no additional model request.\n        import uuid\n        for phase in reviewed_phases:\n            phase[\"tasks\"] = [{\"taskId\": str(uuid.uuid4()), \"description\": phase[\"objective\"],\n                               \"hours\": phase[\"finalHours\"], \"notes\": \"Synthetic UAT reviewed phase work package\"}]"
    ]
  ],
  "scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh": [
    [
      "    customerName:$customerName,",
      "    customerName:$customerName,\n    projectName:\"Protected UAT Module 025 scope\","
    ],
    [
      "  expectedRevision: .engagement.revision,",
      "  expectedRevision: .engagement.revision,\n  projectName: .engagement.projectName,"
    ],
    [
      "    acceptanceCriteria, validationSteps, risks, loeRationale\n  }]",
      "    acceptanceCriteria, validationSteps, risks, loeRationale,\n    tasks: [{taskId: (\"00000000-0000-4000-8000-00000000000\" + (.sortOrder | tostring)), description: .objective,\n             hours: ((.finalHours // 0) + (if .phaseCode == \"plan\" then 1 else 0 end)), notes: \"Synthetic UAT reviewed phase work package\"}]\n  }]"
    ]
  ]
};
expected.sort();
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
assert.equal(git('merge-base', base, 'HEAD'), base);
const verify = files => assert.deepEqual([...files].sort(), expected);
verify(git('diff', '--name-only', base).split(/\r?\n/));
for (const path of expected) assert.throws(() => verify(expected.filter(p => p !== path)));
assert.throws(() => verify([...expected, '.github/workflows/unapproved.yml']));
for (const [path, changes] of Object.entries(registrations)) {
  let allowed = original(path);
  for (const [before, after] of changes) allowed = allowed.replace(before, after);
  assert.equal(fs.readFileSync(path, 'utf8'), allowed, `Registration exceeded scope: ${path}`);
}
for (const name of expected.filter(p => p.endsWith('.yml') && !['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/module025-protected-uat-control.yml'].includes(p))) verifyReadOnlyWorkflow(fs.readFileSync(name,'utf8'), name);
for (const path of ['.github/workflows/projectpulse-deploy-production.yml', '.github/flowhive-psa-protected-test-candidate.json', 'scripts/release-test/flowhive-psa-admission.mjs', 'src/backend/ProjectTime.Api/Modules/Module025SowSellModule.cs'])
  assert.equal(fs.readFileSync(path, 'utf8'), original(path), `Protected behavior changed: ${path}`);
const project = 'src/backend/ProjectTime.Api/ProjectTime.Api.csproj';
const resource = '    <EmbeddedResource Include="Assets/Templates/Module025StandardGsd.xlsx" LogicalName="ProjectTime.Api.Assets.Templates.Module025StandardGsd.xlsx" />\n';
assert.equal(fs.readFileSync(project, 'utf8').replace(resource, ''), original(project));
assert.equal(git('diff', '--name-only', base, '--', 'database', 'deployment', 'src/backend/ProjectTime.Api/Ai'), '');
console.log('MODULE025_STANDARD_DOWNLOAD_FORMATS_SCOPE=PASS generation_engine=unchanged retained_versions=unchanged deployment_authority=unchanged');

assert.equal(createHash('sha256').update(fs.readFileSync('.github/workflows/projectpulse-deploy-test.yml')).digest('hex'), '276d23806215df92246a8b37eb7291f55f1dd1b7938ec38cdaff45570bebd2c3', 'Authorized export acceptance controller changed');

assert.equal(createHash('sha256').update(fs.readFileSync('.github/workflows/module025-protected-uat-control.yml')).digest('hex'), '50e0ab92d90f49f2c79cbeadbacadc4ecd5c03b47fe7268c92646d07b2ff2657', 'Authorized export acceptance controller changed');
