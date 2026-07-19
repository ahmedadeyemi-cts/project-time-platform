import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');
const app = read('src', 'App.jsx');
const main = read('src', 'main.jsx');
const manager = read('src', 'ManagerApprovalPanel.jsx');
const reset = read('src', 'LocalAdminPasswordResetApprovalsPanel.jsx');
const mailbox = read('src', 'ApprovalMailbox.jsx');
const center = read('src', 'ApprovalCenter.jsx');
const backend = read('..', '..', 'backend', 'ProjectTime.Api', 'Modules', 'ApprovalCenterModule.cs');

const assertions = [];
function assert(name, condition, detail = '') {
  assertions.push({ name, condition, detail });
  console.log(`${name}=${condition ? 'PASSED' : 'FAILED'}${detail ? ` — ${detail}` : ''}`);
}

assert('MODULE_002_FLOATING_BANNER_REMOVED', !main.includes('ApprovalNotificationBanner'), 'main.jsx has no floating banner mount');
assert('MODULE_002_MAILBOX_IMPORTED', app.includes("import ApprovalMailbox from './ApprovalMailbox.jsx';"));
assert('MODULE_002_MAILBOX_MOUNTED_IN_TOPBAR', app.includes('<ApprovalMailbox />') && app.indexOf('<ApprovalMailbox />') < app.indexOf('<ProjectPulseGlobalSearch />'));
assert('MODULE_002_APPROVAL_CENTER_IMPORTED', app.includes("import ApprovalCenter from './ApprovalCenter.jsx';"));
assert('MODULE_002_ROUTE_ISOLATED', /activeRoute\s*===\s*'manager-approval'/.test(app) && app.includes('<ApprovalCenter />'));
assert('MODULE_002_EXACT_ROLE_CODES', ['SUPER_ADMINISTRATOR','ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','MANAGER','PROJECT_MANAGER','PROJECT_MANAGEMENT'].every((role) => app.includes(`'${role}'`) && backend.includes(`"${role}"`)));
assert('MODULE_002_GENERIC_PENDINGCOUNT_PARSER_REMOVED', !app.includes('/pendingcount$/'));
assert('MODULE_002_DRAFT_NEVER_ACTIONABLE', backend.includes("tds.status = 'submitted'") && !backend.includes("status IN ('draft', 'submitted'"));
assert('MODULE_002_PROJECT_MANAGER_PASSWORD_RESET_BLOCKED', backend.includes('PasswordResetRoles') && !/PasswordResetRoles[\s\S]{0,400}"PROJECT_MANAGER"/.test(backend));
assert('MODULE_002_REJECTION_REASON_REQUIRED', manager.includes('Specific reason') && backend.includes('A specific rejection reason is required'));
assert('MODULE_002_GLOBAL_SMTP_OUTBOX', backend.includes('TIME_ENTRY_REJECTION') && backend.includes('email_notification_outbox') && backend.includes('notification_outbox'));
assert('MODULE_002_ENGINEER_REJECTION_DETAIL', backend.includes('Rejected entries:') && backend.includes('Submitted description:'));
assert('MODULE_002_MAILBOX_RED_BADGE', mailbox.includes('approval-mailbox-badge') && mailbox.includes('actionableTotal'));
assert('MODULE_002_PASSWORD_RESET_CUSTOM_MODAL', reset.includes('approval-decision-modal') && !reset.includes('window.prompt'));
assert('MODULE_002_TIME_REJECTION_CUSTOM_MODAL', manager.includes('approval-decision-modal') && !manager.includes('window.prompt'));
assert('MODULE_002_CENTER_ROLE_TABS', center.includes('Time approvals') && center.includes('Password resets'));

const failed = assertions.filter((item) => !item.condition);
if (failed.length) {
  console.error('\nModule 002 Approval Center contract failed.');
  for (const item of failed) console.error(`- ${item.name}: ${item.detail}`);
  process.exit(1);
}
console.log('\nMODULE_002_APPROVAL_CENTER_CONTRACT=PASSED');
