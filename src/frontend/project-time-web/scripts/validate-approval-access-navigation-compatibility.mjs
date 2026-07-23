import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.cwd(), '../../..');

async function text(path) {
  return readFile(resolve(root, path), 'utf8');
}

function requireText(source, values, label) {
  for (const value of values) {
    if (!source.includes(value)) {
      throw new Error(`${label} is missing required contract: ${value}`);
    }
  }
}

const paths = {
  main: 'src/frontend/project-time-web/src/main.jsx',
  compatibility: 'src/frontend/project-time-web/src/approval-access-navigation-compatibility.js',
  approvalCenter: 'src/frontend/project-time-web/src/ApprovalCenter.jsx',
  mailbox: 'src/frontend/project-time-web/src/ApprovalMailbox.jsx',
  backend: 'src/backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs',
  packageJson: 'src/frontend/project-time-web/package.json'
};

const [main, compatibility, approvalCenter, mailbox, backend, packageJson] = await Promise.all(
  Object.values(paths).map(text)
);

requireText(main, [
  "import App from './App.jsx';",
  "import './approval-access-navigation-compatibility.js';",
  '<ApprovalMailbox />'
], 'Application compatibility integration');

if (main.indexOf("import './approval-access-navigation-compatibility.js';") < main.indexOf("import App from './App.jsx';")) {
  throw new Error('The compatibility bridge must load after App installs the authenticated fetch pipeline.');
}

requireText(compatibility, [
  "const APPROVAL_COUNT_PATH = '/api/manager/approval-count';",
  "const APPROVAL_ACCESS_PATH = '/api/approval-center/access';",
  "method !== 'GET'",
  "url.pathname !== APPROVAL_COUNT_PATH",
  "cache: 'no-store'",
  "headers.set('Cache-Control', 'no-cache')",
  "url.searchParams.set('approval_contract', marker)",
  "property(payload, 'access', 'Access', 'approvalAccess', 'ApprovalAccess')",
  "property(source, 'roleCodes', 'RoleCodes', 'roles', 'Roles')",
  "property(source, 'canViewTimeApprovals', 'CanViewTimeApprovals')",
  "property(source, 'canViewPasswordResetApprovals', 'CanViewPasswordResetApprovals')",
  "accessResponse.status === 401 || accessResponse.status === 403",
  "return responseWithJson(accessResponse, accessPayload, accessResponse.status)",
  "access ??= normalizeApprovalAccess(summaryPayload)",
  "const MODULES_ROUTE = 'modules';",
  "if (route === MODULES_ROUTE) return;",
  "cleanText(heading.textContent) !== 'Modules'",
  "navigationLabelForRoute(route)",
  "window.addEventListener('hashchange', schedule)"
], 'Approval access and workspace title compatibility');

for (const forbidden of [
  "roleCodes.includes('MANAGER')",
  "roleCodes.includes('PROJECT_TEAM_COORDINATOR')",
  "canViewTimeApprovals: true",
  "canViewPasswordResetApprovals: true"
]) {
  if (compatibility.includes(forbidden)) {
    throw new Error(`The frontend compatibility bridge must not grant access locally: ${forbidden}`);
  }
}

requireText(approvalCenter, [
  "'/api/manager/approval-count'",
  'data?.access ?? {}',
  'Approval Center access is not available for'
], 'Approval Center consumer');

requireText(mailbox, [
  "'/api/manager/approval-count'",
  'if (!summary?.access) return null;',
  'Approval Inbox'
], 'Approval mailbox consumer');

requireText(backend, [
  '"SUPER_ADMINISTRATOR"',
  '"ADMINISTRATOR"',
  '"PROJECT_TEAM_COORDINATOR"',
  '"MANAGER"',
  '"PROJECT_MANAGER"',
  '"PROJECT_MANAGEMENT"',
  'app.MapGet("/api/approval-center/access"',
  'app.MapGet("/api/manager/approval-count"',
  'access = ToAccessPayload(access)'
], 'Backend-authoritative approval access');

requireText(packageJson, [
  'validate:approval-access-compatibility',
  'node ./scripts/validate-approval-access-navigation-compatibility.mjs',
  'npm run validate:approval-access-compatibility'
], 'Production build wiring');

console.log('Backend-authoritative approval access recovery, cache bypass, and Modules title restoration contracts passed.');
