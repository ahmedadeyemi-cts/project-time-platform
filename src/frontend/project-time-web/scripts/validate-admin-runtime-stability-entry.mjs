import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const absolute = (relative) => path.join(repositoryRoot, relative);
const exists = (relative) => fs.existsSync(absolute(relative));
const read = (relative) => fs.readFileSync(absolute(relative), 'utf8');

const backendEvidence = [
  'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs',
  'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityReadContinuityCompatibility.cs',
  'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs'
];

if (backendEvidence.every(exists)) {
  await import('./validate-admin-runtime-stability.mjs');
  console.log('ADMIN_RUNTIME_STABILITY_CONTEXT=FULL_REPOSITORY');
  process.exit(0);
}

const frontendEvidence = {
  main: 'src/frontend/project-time-web/src/main.jsx',
  runtime: 'src/frontend/project-time-web/src/runtime-data-compatibility.js',
  timer: 'src/frontend/project-time-web/src/module001/TimesheetEnhancementPortal.jsx',
  audit: 'src/frontend/project-time-web/src/AuditHistoryPanel.jsx',
  owner: 'src/frontend/project-time-web/src/AdminRuntimeStabilityPortal.jsx',
  theme: 'src/frontend/project-time-web/src/admin-experience-theme.js',
  ownership: 'src/frontend/project-time-web/src/react-dom-ownership-prelude.js',
  microsoft: 'src/frontend/project-time-web/src/microsoft-integration-compatibility.js',
  microsoftCss: 'src/frontend/project-time-web/src/microsoft-integration-portal.css',
  mailUi: 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx'
};

for (const relative of Object.values(frontendEvidence)) {
  if (!exists(relative)) throw new Error(`Minimal web build context is missing ${relative}.`);
}

const main = read(frontendEvidence.main);
const runtime = read(frontendEvidence.runtime);
const timer = read(frontendEvidence.timer);
const audit = read(frontendEvidence.audit);
const owner = read(frontendEvidence.owner);
const theme = read(frontendEvidence.theme);
const ownership = read(frontendEvidence.ownership);
const microsoft = read(frontendEvidence.microsoft);
const microsoftCss = read(frontendEvidence.microsoftCss);
const mailUi = read(frontendEvidence.mailUi);

const checks = [
  ['stable Module 008 owner', main.includes('<AdminRuntimeStabilityPortal />') && owner.includes('<AuditHistoryPanel stableRouteOwner />') && audit.includes('__projectPulseModule008StableOwnerInstalled')],
  ['direct role-policy transport', runtime.includes('projectpulse-role-policy-direct-fetch-v3') && runtime.includes('role_policy_contract_mismatch')],
  ['Module 001 route scope', timer.includes('function isTimesheetRoute()') && timer.includes("'X-ProjectPulse-Module-Number': '001'")],
  ['safe theme control', !theme.includes('MutationObserver') && !theme.includes('window.location.reload') && !theme.includes('node.remove()')],
  ['View-As ownership boundary', ownership.includes('__projectPulseGlobalViewAsTopbarMountInstalled = true') && !/insertBefore|appendChild|removeChild|MutationObserver/.test(ownership)],
  ['Module 010 responsive preview', microsoftCss.includes('.route-azure-admin .azure-admin-heading-actions') && microsoftCss.includes('.route-azure-admin .azure-selection-toolbar') && microsoftCss.includes('overflow-x: auto')],
  ['strict services activation', microsoft.includes('applyPayload?.runtimeActivated !== true') && microsoft.includes('module_065_services_profile_not_active')],
  ['Module 065 readiness panel', main.includes('<MicrosoftMailTransportReadinessPanel />') && mailUi.includes('/api/microsoft-integration/mail-runtime/test') && mailUi.includes('No live message is sent.')]
];

for (const [name, passed] of checks) {
  console.log(`ADMIN_RUNTIME_STABILITY_MINIMAL_${String(name).toUpperCase().replaceAll(/[^A-Z0-9]+/g, '_')}=${passed ? 'PASSED' : 'FAILED'}`);
  if (!passed) throw new Error(`Minimal web build context failed ${name}.`);
}

console.log('ADMIN_RUNTIME_STABILITY_CONTEXT=MINIMAL_WEB_BUILD');
console.log('ADMIN_RUNTIME_STABILITY_CONTRACT=PASSED');
