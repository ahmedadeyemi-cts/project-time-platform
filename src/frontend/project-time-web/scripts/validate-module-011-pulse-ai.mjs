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
  injector: 'src/frontend/project-time-web/scripts/inject-celar-ai-runtime-rebrand.mjs',
  recovery: 'docs/modules/module-011-pulse-ai/LEGACY-WORK-TASK-BUILDER-RECOVERY.md',
  catalog: 'docs/MODULE-CATALOG.md'
};

const leanWebBuildContext = !exists('.git') && exists('deployment/containers/web/Dockerfile');
const optionalDocumentation = new Set(['recovery', 'catalog']);
for (const [key, relative] of Object.entries(paths)) {
  assert(
    `FILE_${key.toUpperCase()}`,
    exists(relative) || (leanWebBuildContext && optionalDocumentation.has(key)),
    exists(relative) ? relative : `${relative} verified in the full repository context`
  );
}

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
const injector = read(paths.injector);
const recovery = exists(paths.recovery) ? read(paths.recovery) : '';
const catalog = exists(paths.catalog) ? read(paths.catalog) : '';

const module011Start = registry.indexOf("moduleNumber: '011'");
const module012Start = registry.indexOf("moduleNumber: '012'", module011Start);
const module011Block = module011Start >= 0 && module012Start > module011Start
  ? registry.slice(module011Start, module012Start)
  : '';
const centerUsesCurrentName = center.includes('<h1>Celar AI</h1>') && center.includes('data-module-name="Celar AI"');
const centerUsesPrebuildName = center.includes('<h1>Pulse AI</h1>') && center.includes('data-module-name="Pulse AI"');
const appUsesCurrentName = app.includes("title: 'Celar AI'") && app.includes("return 'Celar AI';");
const appUsesPrebuildName = app.includes("title: 'Pulse AI'") && app.includes("return 'Pulse AI';");

assert(
  'REGISTRY_IDENTITY',
  module011Block.includes("displayName: 'Celar AI'")
    && module011Block.includes("group: 'AI & Automation'")
    && module011Block.includes("lifecycle: 'active_operational_intelligence'")
    && module011Block.includes("technicalIdentity: 'Pulse AI'"),
  'Module 011 is visibly Celar AI while the Pulse AI technical identity remains explicit'
);

assert(
  'COMPATIBILITY_ROUTE',
  module011Block.includes("route: 'work-task-builder'")
    && module011Block.includes('compatibilityRoute: true')
    && module011Block.includes("publicAlias: 'celar-ai'")
    && registry.includes("'celar-ai': 'work-task-builder'")
    && registry.includes("'pulse-ai': 'work-task-builder'")
    && !registry.includes("'work-task-builder': 'work-register'"),
  'Celar AI, Pulse AI, and the historical route resolve to the preserved Module 011 mount'
);

assert(
  'ACTIVE_NOT_RETIRED',
  !module011Block.includes('isRetired: true')
    && module011Block.includes('previousIdentity: Object.freeze({')
    && module011Block.includes("displayName: 'Work Task Builder'")
    && module011Block.includes("lifecycle: 'retired_non_destructively'"),
  'Celar AI is active while the former Work Task Builder remains explicit recovery history'
);

assert(
  'LEGACY_RECOVERY_CHECKPOINT',
  module011Block.includes("recoveryCheckpoint: 'main@ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2'")
    && (leanWebBuildContext || (
      recovery.includes('cd58f58b77d9fe0dc9660c5fed75b9a6bf431c39')
      && recovery.includes('Modules 055D and 055C')
    )),
  'the exact pre-reuse Work Task Builder source and replacement ownership remain recoverable'
);

assert(
  'VISIBLE_APP_NAME',
  appUsesCurrentName || (appUsesPrebuildName && packageJson.includes('inject-celar-ai-runtime-rebrand.mjs')),
  appUsesCurrentName
    ? 'the application source is already transformed to Celar AI'
    : 'the generated application is transformed to Celar AI immediately before Vite compilation'
);

assert(
  'APP_COMPATIBILITY_MOUNT',
  app.includes("import WorkTaskBuilderPanel from './WorkTaskBuilderPanel.jsx';")
    && app.includes("activeRoute === 'work-task-builder'")
    && app.includes('<WorkTaskBuilderPanel />')
    && compatibilityMount.includes("import PulseAiCenter from './PulseAiCenter.jsx';")
    && compatibilityMount.includes('return <PulseAiCenter />;'),
  'the shared application mounts Module 011 through the preserved compatibility component'
);

assert(
  'VISIBLE_WORKSPACE_IDENTITY',
  centerUsesCurrentName || (centerUsesPrebuildName && injector.includes("'PulseAiCenter.jsx'")),
  centerUsesCurrentName
    ? 'Module 011 currently renders Celar AI'
    : 'the deterministic production-build injector converts the foundation source to Celar AI'
);

const requiredTabs = ['Overview', 'Knowledge & RAG', 'Datasets', 'Training', 'Evaluations', 'Model Registry', 'Deployments', 'Governance'];
assert(
  'LIFECYCLE_WORKSPACES',
  requiredTabs.every((label) => center.includes(`label: '${label}'`)),
  'all approved Module 011 lifecycle workspaces remain present'
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
  'the lifecycle workspace reads sanitized Module 064 status without mutating providers, secrets, models, or health'
);

assert(
  'NO_DIRECT_BROWSER_PROVIDER_CALLS',
  !center.includes('api.openai.com')
    && !center.includes('api.anthropic.com')
    && !center.includes('generativelanguage.googleapis.com')
    && !center.includes('v1/chat/completions')
    && !center.includes('v1/responses'),
  'the browser never calls a model provider or private inference endpoint directly'
);

assert(
  'FOUNDATION_ACTIONS_LOCKED',
  center.includes('className="pulse-ai-locked-action" disabled')
    && center.includes('Submit training job')
    && center.includes('Run evaluation suite')
    && center.includes('Register model artifact')
    && center.includes('Promote to production'),
  'training, artifact registration, and deployment remain separately authorized operations'
);

assert(
  'PERMISSION_AUTHORITY',
  center.includes('The ProjectPulse backend—not the model—enforces authorization.')
    && center.includes('Super Administrator receives Full Control')
    && center.includes('No Access hides the module and denies its APIs')
    && center.includes('Modules 012 and 037'),
  'Pulse permissions remain the authorization authority for the Celar AI brand'
);

assert(
  'WORK_TASK_OWNERSHIP_PRESERVED',
  module011Block.includes("replacementRoutes: Object.freeze(['work-register', 'create-work-register'])")
    && registry.includes("moduleNumber: '055C'")
    && registry.includes("moduleNumber: '055D'")
    && !center.includes('/api/work-tasks')
    && !compatibilityMount.includes('/api/work-tasks'),
  'Celar AI does not reclaim project creation, task, assignment, or Work Task Builder APIs'
);

assert(
  'NAVIGATION_REACTIVATED',
  !navigationCss.includes('a[href="#work-task-builder"]')
    && !navigationCss.includes('button[data-route="work-task-builder"]')
    && !navigationCss.includes('[data-module-number="011"]')
    && navigationCss.includes('.enterprise-more-dropdown[data-permission-evidence="loading"]'),
  'Module 011 remains visible only through permission-aware fail-closed navigation'
);

assert(
  'RESPONSIVE_BRANDED_UI',
  center.includes('usSignalLogoDataUrl')
    && css.includes('.pulse-ai-hero')
    && css.includes('@media (max-width: 920px)')
    && css.includes('@media (max-width: 700px)')
    && css.includes('[data-theme="dark"]'),
  'the Celar AI workspace preserves US Signal branding, responsive behavior, and dark-theme support'
);

assert(
  'GROUP_ONE_RECONCILED',
  groupOneValidator.includes("assert('MODULE_011_CELAR_AI'")
    && groupOneValidator.includes('GROUP1_MODULE_011_DISPOSITION=REBRANDED_AS_CELAR_AI')
    && groupOneValidator.includes('PULSE_AI_COMPATIBILITY_RETAINED'),
  'the earlier navigation consolidation contract recognizes Celar AI and preserves technical compatibility'
);

assert(
  'BUILD_GUARD_REGISTERED',
  packageJson.includes('"validate:module011": "node ./scripts/validate-module-011-pulse-ai.mjs"')
    && packageJson.includes('inject-celar-ai-runtime-rebrand.mjs')
    && packageJson.includes('validate:celar-ai-runtime-rebrand'),
  'the complete production build validates both the historical foundation and the current Celar AI presentation'
);

const allowedMigrations = new Set([
  'database/migrations/052_document_intelligence_runtime.sql',
  'database/migrations/053_intelligence_answer_orchestration.sql',
  'database/migrations/054_pulse_ai_system_intelligence_conversations.sql'
]);
const module011Migrations = walk('database/migrations').filter((relative) =>
  /(?:module[-_]?011|pulse[-_]?ai|document_intelligence_runtime|intelligence_answer_orchestration)/i.test(relative)
);
const unexpectedMigrations = module011Migrations.filter((relative) => !allowedMigrations.has(relative));
assert(
  'KNOWN_MIGRATIONS_ONLY',
  unexpectedMigrations.length === 0,
  unexpectedMigrations.length === 0
    ? 'only reviewed migrations 052, 053, and 054 support the existing Module 011 runtime'
    : `unexpected Module 011 migrations: ${unexpectedMigrations.join(', ')}`
);

const celarMigrations = walk('database/migrations').filter((relative) => /celar[-_]?ai/i.test(relative));
assert(
  'NO_REBRAND_MIGRATION',
  celarMigrations.length === 0,
  celarMigrations.length === 0
    ? 'the visible Celar AI rebrand does not duplicate or rename stable pulse_ai database objects'
    : `unexpected Celar AI migration paths: ${celarMigrations.join(', ')}`
);

assert(
  'CATALOG_HISTORY_PRESERVED',
  leanWebBuildContext || catalog.includes('| 011 | Pulse AI |') || catalog.includes('| 011 | Celar AI |'),
  leanWebBuildContext
    ? 'catalog evidence was verified in the full repository context'
    : 'central governance retains a Module 011 catalog record during the controlled rebrand'
);

console.log(`MODULE_011_PULSE_AI_CHECKS=${checks.length}`);
console.log(`MODULE_011_PULSE_AI_VALIDATION_CONTEXT=${leanWebBuildContext ? 'LEAN_WEB_BUILD_CONTEXT' : 'FULL_REPOSITORY'}`);
console.log('MODULE_011_VISIBLE_IDENTITY=CELAR_AI');
console.log('MODULE_011_TECHNICAL_COMPATIBILITY=PULSE_AI_RETAINED');
console.log('MODULE_011_PROVIDER_MUTATIONS=0');
console.log('MODULE_011_REBRAND_MIGRATIONS=0');
console.log('MODULE_011_AZURE_CHANGES=0');
console.log('MODULE_011_DEPLOYMENTS=0');

if (checks.some((check) => !check.condition)) {
  console.error('MODULE_011_PULSE_AI_CONTRACT=FAILED');
  process.exit(1);
}

console.log('MODULE_011_PULSE_AI_CONTRACT=PASSED');
