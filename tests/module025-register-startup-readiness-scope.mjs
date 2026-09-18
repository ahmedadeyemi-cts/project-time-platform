import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { verifyReadOnlyWorkflow } from './flowhive-psa-scope.mjs';
const base = 'ef0dd9cd7d64366d4115084646dea2dd9e412edd';
const expected = [
  ".github/workflows/flowhive-psa-release-control-ci.yml",
  ".github/workflows/module025-governed-protected-test-release-manual.yml",
  "scripts/release-test/validate-protected-test-controller-branches.sh",
  "src/frontend/project-time-web/src/EnterpriseExperienceController.jsx",
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx",
  "src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx",
  "tests/flowhive-psa-admission.test.mjs",
  "tests/module025-installed-browser-harness.mjs",
  "tests/module025-register-browser.test.py",
  "tests/module025-register-startup-readiness-scope.mjs",
  "tests/module025-sow-register-browser.py",
  "tests/validate-celar-ai-pr630-consolidated.mjs"
];
const registrations = {
  ".github/workflows/module025-governed-protected-test-release-manual.yml": "          if [[ \"$HEAD_BRANCH\" == 'fix/module025-register-startup-readiness-20260918' ]]; then\n            node tests/module025-register-startup-readiness-scope.mjs\n            node tests/validate-systemwide-image-build-controller.mjs\n            exit 0\n          fi\n",
  ".github/workflows/flowhive-psa-release-control-ci.yml": "          elif [[ \"$GITHUB_HEAD_REF\" == 'fix/module025-register-startup-readiness-20260918' ]]; then\n            node tests/module025-register-startup-readiness-scope.mjs\n",
  "scripts/release-test/validate-protected-test-controller-branches.sh": "elif [[ \"$HEAD_BRANCH\" == 'fix/module025-register-startup-readiness-20260918' ]]; then\n  node tests/module025-register-startup-readiness-scope.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\n"
};
const replacements = {
  "tests/flowhive-psa-admission.test.mjs": {
    "const module025RegisterReport =": "const module025RegisterStartup = process.env.GITHUB_HEAD_REF === 'fix/module025-register-startup-readiness-20260918';\nconst module025RegisterReport =",
    "  || module025RegisterReport ||": "  || module025RegisterStartup || module025RegisterReport ||",
    "const module025VerifierBase = module025RegisterReport ?": "const module025VerifierBase = module025RegisterStartup ? 'ef0dd9cd7d64366d4115084646dea2dd9e412edd' : module025RegisterReport ?"
  },
  "tests/validate-celar-ai-pr630-consolidated.mjs": {
    "const module025PhaseProviderRecoveryMode =": "const module025RegisterStartupMode = branchName === 'fix/module025-register-startup-readiness-20260918';\nif (module025RegisterStartupMode) (await import('./module025-register-startup-readiness-scope.mjs')).verifyModule025RegisterStartupScope();\nconst module025PhaseProviderRecoveryMode =",
    "const scopedCompatibilityMode = module025PhaseProviderRecoveryMode ||": "const scopedCompatibilityMode = module025RegisterStartupMode || module025PhaseProviderRecoveryMode ||"
  }
};
const workspaceChanges = {
  "src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx": {
    "export default function SowGsdWorkspace({ onOpenRegister }) {": "export default function SowGsdWorkspace({ onOpenRegister, onWorkspaceReady }) {",
    "  }, [loadBootstrap]);": "  }, [loadBootstrap]);\n\n  useEffect(() => {\n    onWorkspaceReady?.(Boolean(bootstrap));\n  }, [bootstrap, onWorkspaceReady]);"
  },
  "src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx": {
    "  const [registerEngagementId, setRegisterEngagementId] = useState('');": "  const [registerEngagementId, setRegisterEngagementId] = useState('');\n  const [workspaceReady, setWorkspaceReady] = useState(false);",
    "        <button type=\"button\" id=\"m025-register-tab\" role=\"tab\" aria-selected={view === 'register'} aria-controls=\"m025-register-panel\"": "        <button type=\"button\" id=\"m025-register-tab\" role=\"tab\" aria-selected={view === 'register'} aria-controls=\"m025-register-panel\"\n          disabled={!workspaceReady}",
    "        <SowGsdAuthoringWorkspace onOpenRegister={openRegister} />": "        <SowGsdAuthoringWorkspace onOpenRegister={openRegister} onWorkspaceReady={setWorkspaceReady} />"
  }
};
const headerAddition = "    // Module 025 headers contain document/report actions. They are part of the\n    // workspace, even when asynchronous startup makes their h1 appear first.\n    '[data-module025-sow-gsd-workspace=\"true\"]',\n    '[data-module025-sow-register=\"true\"]',\n";
const git = (...args) => execFileSync('git', args, {encoding:'utf8'}).trim();
const original = name => execFileSync('git', ['show', `${base}:${name}`], {encoding:'utf8'});
export function verifyModule025RegisterStartupScope() {
  assert.equal(git('merge-base', base, 'HEAD'), base);
  const verify = files => assert.deepEqual([...files].sort(), expected);
  verify(git('diff', '--name-only', base).split(/\r?\n/));
  for (const name of expected) assert.throws(() => verify(expected.filter(value => value !== name)));
  assert.throws(() => verify([...expected, '.github/workflows/projectpulse-deploy-test.yml']));
  for (const [name, registration] of Object.entries(registrations)) {
    const source = fs.readFileSync(name, 'utf8');
    assert.equal(source.replace(registration, ''), original(name));
    if (name.endsWith('.yml')) {
      verifyReadOnlyWorkflow(source, name);
      assert.throws(() => verifyReadOnlyWorkflow(source.replace('contents: read', 'contents: write'), name));
    }
  }
  for (const [name, changes] of Object.entries(replacements)) {
    let source = original(name);
    for (const [before, after] of Object.entries(changes)) source = source.replace(before, after);
    assert.equal(fs.readFileSync(name, 'utf8'), source);
  }
  const controller = 'src/frontend/project-time-web/src/EnterpriseExperienceController.jsx';
  assert.equal(fs.readFileSync(controller, 'utf8').replace(headerAddition, ''), original(controller));
  for (const name of [
    '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/run-module025-sow-gsd-protected-test-uat.sh'
  ]) assert.equal(fs.readFileSync(name, 'utf8'), original(name), `Release or acceptance authority changed: ${name}`);
  for (const [name, changes] of Object.entries(workspaceChanges)) {
    let source = original(name);
    for (const [before, after] of Object.entries(changes)) source = source.replace(before, after);
    assert.equal(fs.readFileSync(name, 'utf8'), source);
  }
  assert.deepEqual(git('diff', '--name-only', base, '--', 'deployment', 'database', 'src').split(/\r?\n/), [controller, ...Object.keys(workspaceChanges)].sort());
  console.log('MODULE025_REGISTER_STARTUP_SCOPE=PASS exact_files=12 action_headers=preserved authorization=unchanged deployment_authority=unchanged');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) verifyModule025RegisterStartupScope();
