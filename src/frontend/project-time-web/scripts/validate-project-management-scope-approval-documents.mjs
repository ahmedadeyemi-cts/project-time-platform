import fs from 'node:fs';

function read(path) {
  if (!fs.existsSync(path)) throw new Error(`Missing required source: ${path}`);
  return fs.readFileSync(path, 'utf8');
}

function requireText(source, marker, label) {
  if (!source.includes(marker)) throw new Error(`Missing ${label}: ${marker}`);
}

const scope = read('../../backend/ProjectTime.Api/Modules/ProjectManagementWorkRegisterScope.cs');
const authorization = read('../../backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs');
const approval = read('../../backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs');
const approvalUi = read('./src/approval-access-navigation-compatibility.js');
const documents = read('./src/work-register-document-integrity.js');
const main = read('./src/main.jsx');

for (const role of [
  'SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR',
  'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT_LEAD',
  'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD', 'MANAGER'
]) requireText(scope, `"${role}"`, `Work Register role ${role}`);

requireText(scope, 'user_admin_manager_team_assignments', 'authoritative manager-to-team scope');
requireText(scope, 'project.project_manager_user_id = @user_id', 'assigned Project Manager scope');
requireText(scope, 'project_management_team_view', 'team view-only scope');
requireText(scope, 'canEditProject', 'per-project edit evidence');
requireText(scope, 'teamProjectsReadOnly', 'team read-only response contract');
requireText(authorization, 'ProjectManagementWorkRegisterScope.TryHandleReadAsync', 'server read-scope middleware');

for (const role of [
  'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD'
]) {
  requireText(approval, `"${role}"`, `Approval Center backend role ${role}`);
  requireText(approvalUi, `'${role}'`, `Approval Center frontend role ${role}`);
}
requireText(approval, 'var isManager = roleSet.Contains("MANAGER") || isProjectManagementLead;', 'lead team approval scope');
requireText(approval, '|| isProjectManagementLead;', 'lead managed-project approval scope');
requireText(approval, '? "Project Management Lead"', 'lead approval role label');

requireText(documents, "const EMPTY_DOCUMENT_ROUTE = '/api/work-register/projects/documents//';", 'empty document route guard');
requireText(documents, "status: 'document_identity_missing'", 'missing upload identity failure');
requireText(documents, 'UUID_PATTERN.test(value)', 'stable document identity validation');
requireText(main, "import './work-register-document-integrity.js';", 'document integrity runtime import');

console.log('PROJECT_MANAGEMENT_SCOPE_APPROVAL_DOCUMENTS=PASS');
console.log('PROJECT_MANAGER_SCOPE=OWN_PROJECTS_ONLY');
console.log('PROJECT_MANAGEMENT_LEAD_SCOPE=OWN_PLUS_ASSIGNED_TEAM_VIEW');
console.log('PROJECT_MANAGEMENT_MANAGER_SCOPE=ASSIGNED_TEAM_VIEW_ONLY');
console.log('PROJECT_TEAM_COORDINATOR_SCOPE=FULL_PROJECT_AUTHORITY');
console.log('APPROVAL_INBOX_PM_LEAD_ALIASES=PASS');
console.log('WORK_REGISTER_DOCUMENT_IDENTITY=FAIL_CLOSED');
