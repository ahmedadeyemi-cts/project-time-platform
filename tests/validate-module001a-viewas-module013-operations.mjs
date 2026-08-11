import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const requireText = (content, marker, label) => {
  if (!content.includes(marker)) throw new Error(`${label}: missing ${marker}`);
};
const rejectText = (content, marker, label) => {
  if (content.includes(marker)) throw new Error(`${label}: forbidden ${marker}`);
};

const app = read('src/frontend/project-time-web/src/App.jsx');
const closeout = read('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx');
const guide = read('src/frontend/project-time-web/src/PageContextGuide.jsx');
const approval = read('src/frontend/project-time-web/src/approval-access-navigation-compatibility.js');
const operationsUi = read('src/frontend/project-time-web/src/ServiceControlCenter.jsx');
const operationsApi = read('src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs');
const operationsContracts = read('src/backend/ProjectTime.Api/Modules/PlatformOperationsContracts.cs');
const remediationApi = read('src/backend/ProjectTime.Api/Modules/SystemDiagnosticRemediationModule.cs');

requireText(app, "activeRoute === 'engineer-task-closeout' ? (", 'Module 001A direct route');
rejectText(app, "activeRoute === 'engineer-task-closeout' && canUseEngineerTaskCloseout", 'client-only Module 001A route gate');
requireText(app, "module001a_owns_route_data", 'Module 001A route-owned data boundary');
requireText(app, '[selectedWeekStart, authSession?.sessionToken, activeRoute]', 'Module 001A route refresh dependency');
requireText(closeout, 'No tasks are available for closeout', 'enterprise closeout empty state');
requireText(closeout, 'No action is required.', 'enterprise no-action message');
requireText(guide, 'view_as_documented_contract', 'View-As page context boundary');
requireText(app, '!localViewAsIsActive() ? <AiProviderReadinessController authSession={authSession} /> : null', 'View-As provider-monitoring boundary');
requireText(approval, 'view_as_no_approval_authority', 'View-As approval no-authority contract');
requireText(operationsApi, 'BuildVersionInventoryAsync', 'Module 013 version inventory');
requireText(operationsApi, 'SHOW server_version;', 'PostgreSQL server version evidence');
requireText(operationsContracts, 'secretValuesReturned = false', 'Module 013 security contract');
requireText(operationsUi, 'Version inventory', 'Module 013 version inventory UI');
requireText(operationsUi, 'Open controlled restart workspace', 'Module 013 controlled restart entry');
requireText(operationsUi, '/api/system-diagnostics/operations-adapter-readiness', 'Module 998 restart readiness');
requireText(remediationApi, 'restart_service', 'governed restart implementation');
requireText(remediationApi, 'requested_by <> @actor', 'restart separation of duties');
rejectText(operationsUi, 'Process.Start', 'arbitrary process execution');
rejectText(operationsUi, '/restart?service=', 'arbitrary restart target');

console.log('MODULE_001A_VIEW_AS_ROUTE=PASS');
console.log('MODULE_001A_ENTERPRISE_EMPTY_STATE=PASS');
console.log('MODULE_001A_PRIVILEGED_BACKGROUND_ISOLATION=PASS');
console.log('MODULE_013_VERSION_INVENTORY=PASS');
console.log('MODULE_013_GOVERNED_RESTART_ENTRY=PASS');
console.log('PRODUCTION_MUTATIONS=0');
