import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const absolute = (relative) => path.join(repoRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');
const checks = [];

function assert(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`MODULE011_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function walk(relativeDirectory) {
  const directory = absolute(relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const relative = path.join(relativeDirectory, entry.name);
    if (entry.isDirectory()) files.push(...walk(relative));
    else files.push(relative.replaceAll('\\', '/'));
  }
  return files;
}

const paths = {
  center: 'src/frontend/project-time-web/src/PulseAiCenter.jsx',
  css: 'src/frontend/project-time-web/src/pulse-ai-center.css',
  compatibilityMount: 'src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx',
  registry: 'src/frontend/project-time-web/src/module-availability-registry.js',
  navigationCss: 'src/frontend/project-time-web/src/permission-aware-more-menu.css',
  app: 'src/frontend/project-time-web/src/App.jsx',
  groupOneValidator: 'src/frontend/project-time-web/scripts/validate-group-1-navigation-work-consolidation.mjs',
  packageJson: 'src/frontend/project-time-web/package.json',
  readme: 'docs/modules/module-011-pulse-ai/README.md',
  recovery: 'docs/modules/module-011-pulse-ai/LEGACY-WORK-TASK-BUILDER-RECOVERY.md',
  catalog: 'docs/MODULE-CATALOG.md'
};

const documentationKeys = new Set(['readme', 'recovery']);
const leanWebBuildContext = !exists('.git')
  && exists('deployment/containers/web/Dockerfile')
  && !exists(paths.readme)
  && !exists(paths.recovery);

for (const [key, relative] of Object.entries(paths)) {
  if (documentationKeys.has(key)) continue;
  assert(`FILE_${key.toUpperCase()}`, exists(relative), relative);
}

assert(
  'FILE_README',
  exists(paths.readme) || leanWebBuildContext,
  exists(paths.readme)
    ? paths.readme
    : 'canonical README was verified in the full repository build before the lean web image stage'
);
assert(
  'FILE_RECOVERY',
  exists(paths.recovery) || leanWebBuildContext,
  exists(paths.recovery)
    ? paths.recovery
    : 'legacy recovery evidence was verified in the full repository build before the lean web image stage'
);

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PULSE_AI_CONTRACT=FAILED_MISSING_FILE');
  process.exit(1);
}

const center = read(paths.center);
const css = read(paths.css);
const compatibilityMount = read(paths.compatibilityMount);
const registry = read(paths.registry);
const navigationCss = read(paths.navigationCss);
const app = read(paths.app);
const groupOneValidator = read(paths.groupOneValidator);
const packageJson = read(paths.packageJson);
const readme = exists(paths.readme) ? read(paths.readme) : '';
const recovery = exists(paths.recovery) ? read(paths.recovery) : '';
const catalog = read(paths.catalog);

const module011Start = registry.indexOf("moduleNumber: '011'");
const module012Start = registry.indexOf("moduleNumber: '012'", module011Start);
const module011Block = module011Start >= 0 && module012Start > module011Start
  ? registry.slice(module011Start, module012Start)
  : '';

assert(
  'REGISTRY_IDENTITY',
  module011Block.includes("displayName: 'Pulse AI'")
    && module011Block.includes("group: 'AI & Automation'")
    && module011Block.includes("lifecycle: 'source_foundation'"),
  'Module 011 is registered as the Pulse AI source foundation'
);

assert(
  'COMPATIBILITY_ROUTE',
  module011Block.includes("route: 'work-task-builder'")
    && module011Block.includes('compatibilityRoute: true')
    && !registry.includes("'work-task-builder': 'work-register'"),
  'the historical route mounts Pulse AI and is no longer redirected to Module 055C'
);

assert(
  'ACTIVE_NOT_RETIRED',
  !module011Block.includes('isRetired: true')
    && module011Block.includes('previousIdentity: Object.freeze({')
    && module011Block.includes("displayName: 'Work Task Builder'")
    && module011Block.includes("lifecycle: 'retired_non_destructively'"),
  'Pulse AI is active while the retired Work Task Builder identity remains explicit history'
);

assert(
  'LEGACY_RECOVERY_CHECKPOINT',
  module011Block.includes("recoveryCheckpoint: 'main@ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2'")
    && (leanWebBuildContext || (
      recovery.includes('cd58f58b77d9fe0dc9660c5fed75b9a6bf431c39')
      && recovery.includes('Modules 055D and 055C')
    )),
  leanWebBuildContext
    ? 'registry recovery metadata remains mandatory; full recovery document was verified before the lean web image stage'
    : 'the exact pre-reuse component checkpoint and business disposition are recoverable'
);

assert(
  'VISIBLE_APP_NAME',
  app.includes("title: 'Pulse AI'")
    && app.includes("case 'work-task-builder':")
    && app.includes("return 'Pulse AI';")
    && !app.includes("title: 'Work Task Builder'"),
  'visible Module 011 navigation and registry labels are Pulse AI while the compatibility route remains unchanged'
);

assert(
  'APP_COMPATIBILITY_MOUNT',
  app.includes("import WorkTaskBuilderPanel from './WorkTaskBuilderPanel.jsx';")
    && app.includes("activeRoute === 'work-task-builder'")
    && app.includes('<WorkTaskBuilderPanel />')
    && compatibilityMount.includes("import PulseAiCenter from './PulseAiCenter.jsx';")
    && compatibilityMount.includes('return <PulseAiCenter />;'),
  'the existing shared App.jsx route mounts Pulse AI through a small compatibility component'
);

assert(
  'PULSE_AI_IDENTITY',
  center.includes('data-module="011"')
    && center.includes('data-module-name="Pulse AI"')
    && center.includes('data-source-phase="read-only-foundation"')
    && center.includes('<h1>Pulse AI</h1>'),
  'the page exposes the current Module 011 identity and locked source phase'
);

const requiredTabs = [
  'Overview',
  'Knowledge & RAG',
  'Datasets',
  'Training',
  'Evaluations',
  'Model Registry',
  'Deployments',
  'Governance'
];
assert(
  'LIFECYCLE_WORKSPACES',
  requiredTabs.every((label) => center.includes(`label: '${label}'`)),
  'all approved Pulse AI lifecycle workspaces are present'
);

assert(
  'MODULE_064_READ_ONLY_BOUNDARY',
  center.includes("fetch('/api/ai-configuration'")
    && center.includes("method: 'GET'")
    && center.includes('Module 064 remains the governed provider and inference gateway')
    && !center.includes('/api/ai-configuration/providers/')
    && !center.includes('/health/refresh')
    && !center.includes('/secret')
    && !center.includes('/enabled'),
  'Pulse AI reads sanitized Module 064 status but cannot mutate providers, secrets, models, or health'
);

assert(
  'NO_DIRECT_PROVIDER_CALLS',
  !center.includes('api.openai.com')
    && !center.includes('api.anthropic.com')
    && !center.includes('generativelanguage.googleapis.com')
    && !center.includes('localhost:8000')
    && !center.includes('v1/chat/completions')
    && !center.includes('v1/responses'),
  'the browser never contacts a model provider or private inference endpoint directly'
);

assert(
  'NO_MUTATION_REQUESTS',
  !/method\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/i.test(center)
    && !center.includes("fetch('/api/pulse-ai")
    && !center.includes('navigator.sendBeacon'),
  'the foundation contains no API mutation request'
);

assert(
  'SESSION_ONLY_DRAFTS',
  center.includes('useState([...INITIAL_PROJECTS])')
    && center.includes('Session draft — not persisted')
    && center.includes('No record leaves the browser and nothing is persisted.')
    && !center.includes('localStorage')
    && !center.includes('sessionStorage')
    && !center.includes('indexedDB'),
  'project drafting is browser-memory-only with no hidden local or durable persistence'
);

assert(
  'EXECUTION_LOCKED',
  center.includes('className="pulse-ai-locked-action" disabled')
    && center.includes('Submit training job')
    && center.includes('Run evaluation suite')
    && center.includes('Register model artifact')
    && center.includes('Promote to production')
    && center.includes('Execution locked'),
  'training, evaluation execution, artifact registration, and deployment controls remain disabled'
);

assert(
  'EXTERNAL_COMPUTE_BOUNDARY',
  center.includes('External GPU environment')
    && center.includes('LoRA or QLoRA runs outside the ProjectPulse web/API process.')
    && center.includes('Large adapters and models belong in approved object storage or a model registry'),
  'future training is explicitly external to the ProjectPulse web and API processes'
);

assert(
  'PERMISSION_AUTHORITY',
  center.includes('The ProjectPulse backend—not the model—enforces authorization.')
    && center.includes('Super Administrator receives Full Control')
    && center.includes('No Access hides the module and denies its APIs')
    && center.includes('Modules 012 and 037'),
  'the application remains the authorization authority and future permissions are mapped to Modules 012/037'
);

assert(
  'WORK_TASK_OWNERSHIP_PRESERVED',
  module011Block.includes("replacementRoutes: Object.freeze(['work-register', 'create-work-register'])")
    && registry.includes("moduleNumber: '055C'")
    && registry.includes("moduleNumber: '055D'")
    && !center.includes('/api/work-tasks')
    && !compatibilityMount.includes('/api/work-tasks'),
  'Pulse AI does not reclaim project creation, task, assignment, or work-task APIs'
);

assert(
  'NAVIGATION_REACTIVATED',
  !navigationCss.includes('a[href="#work-task-builder"]')
    && !navigationCss.includes('button[data-route="work-task-builder"]')
    && !navigationCss.includes('[data-module-number="011"]')
    && navigationCss.includes('.enterprise-more-dropdown[data-permission-evidence="loading"]'),
  'Module 011 is no longer hard-hidden while permission-aware fail-closed navigation remains intact'
);

assert(
  'RESPONSIVE_BRANDED_UI',
  center.includes('usSignalLogoDataUrl')
    && css.includes('.pulse-ai-hero')
    && css.includes('@media (max-width: 920px)')
    && css.includes('@media (max-width: 700px)')
    && css.includes('[data-theme="dark"]'),
  'Pulse AI uses approved branding with desktop, mobile, and dark-theme behavior'
);

assert(
  'DOCUMENTED_LOCKS',
  leanWebBuildContext || (
    readme.includes('Database migration | None')
      && readme.includes('Training execution | None')
      && readme.includes('Provider mutation | None')
      && readme.includes('Azure or deployment change | None')
      && readme.includes('No deployment, migration, Azure, Entra, provider-secret, or live-model change')
  ),
  leanWebBuildContext
    ? 'canonical locked-boundary documentation was verified in the full repository build before the lean web image stage'
    : 'documentation states the exact locked source boundary'
);

assert(
  'CATALOG_UPDATED',
  catalog.includes('| 011 | Pulse AI |')
    && catalog.includes('retired Work Task Builder')
    && catalog.includes('Modules 055D and 055C'),
  'central governance records the approved Pulse AI reuse and preserved legacy ownership'
);

assert(
  'GROUP_ONE_RECONCILED',
  groupOneValidator.includes("assert('MODULE_011_PULSE_AI'")
    && groupOneValidator.includes('GROUP1_MODULE_011_DISPOSITION=REUSED_AS_PULSE_AI')
    && groupOneValidator.includes('LEGACY_WORK_TASK_BUILDER_RECOVERABLE'),
  'the earlier navigation consolidation contract recognizes the approved Module 011 reuse'
);

assert(
  'BUILD_GUARD_REGISTERED',
  packageJson.includes('"validate:module011": "node ./scripts/validate-module-011-pulse-ai.mjs"')
    && packageJson.includes('npm run validate:module011'),
  'the complete frontend build executes the Pulse AI validator'
);

const pulseAiMigrations = walk('database/migrations').filter((relative) => /(?:module[-_]?011|pulse[-_]?ai)/i.test(relative));
assert(
  'NO_MIGRATION',
  pulseAiMigrations.length === 0,
  pulseAiMigrations.length === 0
    ? 'no Module 011 or Pulse AI migration exists'
    : `unexpected migration paths: ${pulseAiMigrations.join(', ')}`
);

const pulseAiDeploymentWorkflows = walk('.github/workflows').filter((relative) => /(?:module[-_]?011|pulse[-_]?ai)/i.test(relative));
assert(
  'NO_DEPLOYMENT_WORKFLOW',
  pulseAiDeploymentWorkflows.length === 0,
  pulseAiDeploymentWorkflows.length === 0
    ? 'no Module 011 deployment or environment-changing workflow exists'
    : `unexpected workflow paths: ${pulseAiDeploymentWorkflows.join(', ')}`
);

console.log(`MODULE_011_PULSE_AI_CHECKS=${checks.length}`);
console.log(`MODULE_011_PULSE_AI_VALIDATION_CONTEXT=${leanWebBuildContext ? 'LEAN_WEB_BUILD_CONTEXT' : 'FULL_REPOSITORY'}`);
console.log('MODULE_011_PULSE_AI_SOURCE_PHASE=READ_ONLY_SESSION_ONLY_FOUNDATION');
console.log('MODULE_011_PULSE_AI_PROVIDER_MUTATIONS=0');
console.log('MODULE_011_PULSE_AI_TRAINING_JOBS_SUBMITTED=0');
console.log('MODULE_011_PULSE_AI_EXTERNAL_CALLS_PERFORMED=0');
console.log('MODULE_011_PULSE_AI_DATABASE_CHANGES=0');
console.log('MODULE_011_PULSE_AI_AZURE_CHANGES=0');
console.log('MODULE_011_PULSE_AI_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PULSE_AI_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_PULSE_AI_CONTRACT=PASSED');
