import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const root = path.resolve(import.meta.dirname, '..', '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const requireText = (source, needle, label) => {
  if (!source.includes(needle)) throw new Error(`Missing ${label}: ${needle}`);
};
const forbidText = (source, needle, label) => {
  if (source.includes(needle)) throw new Error(`Forbidden ${label}: ${needle}`);
};

const viewAs = read('src/frontend/project-time-web/src/view-as-storage-compatibility.js');
const drawer = read('src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx');
const modulesAuthority = read('src/frontend/project-time-web/src/module-directory-authority.js');
const modulesPortal = read('src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx');
const workspaces = read('src/frontend/project-time-web/src/WorkspaceNavigationPortal.jsx');
const workRegister = read('src/frontend/project-time-web/src/WorkRegisterCenter.jsx');
const integrity = read('src/frontend/project-time-web/src/work-register-document-integrity.js');
const pageGuide = read('src/frontend/project-time-web/src/PageContextGuide.jsx');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const workRegisterScope = read('src/backend/ProjectTime.Api/Modules/ProjectManagementWorkRegisterScope.cs');
const deploy = read('.github/workflows/projectpulse-deploy-test.yml');

requireText(viewAs, 'VIEW_AS_EFFECTIVE_REQUEST_BRIDGE_V1', 'View-As request bridge contract');
requireText(viewAs, "headers.set('X-ProjectPulse-View-As-User', viewAs.userId)", 'fetch effective-user header');
requireText(viewAs, "this.setRequestHeader('X-ProjectPulse-View-As-User', viewAs.userId)", 'XHR effective-user header');
requireText(viewAs, "status: 'view_as_read_only'", 'View-As write block');
requireText(viewAs, '__projectPulseAdministratorFetch', 'administrator-only View-As discovery bypass');
requireText(drawer, 'window.__projectPulseAdministratorFetch || window.__projectPulseOriginalFetch || window.fetch.bind(window)', 'drawer administrator discovery fetch');

requireText(modulesAuthority, 'SHARED_WORKSPACE_MODULE_AUTHORITY_V1', 'shared More/Modules authority contract');
requireText(workspaces, 'publishWorkspaceAuthorization(authority.authorized, authority.state)', 'workspace authority publisher');
requireText(modulesPortal, 'projectpulse:workspace-authorization-updated', 'Modules shared authority refresh');

requireText(workRegister, "const viewAsUserId = String(viewAs?.userId || '').trim();", 'canonical Work Register View-As header');
forbidText(workRegister, "headers['X-ProjectPulse-View-As-User'] = viewAsUser;", 'raw View-As JSON header');
requireText(integrity, 'workRegisterDocumentId', '055C compatibility document ID');
requireText(program, 'documentId = workRegisterDocumentId', 'canonical upload response document ID');
requireText(program, 'WORK_REGISTER_DOCUMENT_CONTINUITY_V1', 'document continuity contract');
requireText(program, 'LoadRowsByProjectAsync("work_register_project_documents"', '055D project-document detail source');
requireText(program, 'LoadRowsByProjectAsync("work_register_documents"', '055C project-document detail source');
requireText(program, 'work_register_project_document_id = @document_id', '055D download fallback');
requireText(program, 'workRegisterProjectDocumentRows.Concat(workRegisterDocumentRows)', 'combined 055C/055D overview count');
requireText(workRegisterScope, 'TryReadDocumentIdFromPath', 'document download scope parser');
requireText(workRegisterScope, 'ReadDocumentProjectIdAsync', 'document-to-project scope resolution');

requireText(pageGuide, "const API_INVENTORY_MODULES = new Set(['011', '064', '068', '078', '998']);", 'diagnostic-only API inventory routes');
requireText(pageGuide, 'canRequestLiveApiInventory(module.moduleNumber)', 'API inventory capability gate');
forbidText(pageGuide, "if (activeViewAsUser()) {\n      setApiEvidence({ status: 'view_as_documented_contract', apis: [] });\n      return () => { active = false; };\n    }\n    setApiEvidence({ status: 'loading'", 'unconditional API inventory load after View-As check');

requireText(deploy, 'Project Manager Work Register document continuity', 'Protected-Test 055C document UAT');
requireText(deploy, 'PRO-E875C783', 'known protected-Test document project');
requireText(deploy, 'WORK_REGISTER_DOCUMENT_CONTINUITY_V1', 'served bundle continuity marker');

const temporaryArtifacts = [
  '.github/pr698-payload',
  '.github/workflows/pr698-finalize.yml',
  '.github/workflows/pr698-finalize-v2.yml',
  '.github/workflows/pr698-finalize-v3.yml',
  '.github/workflows/pr698-finalize-v4.yml',
  '.github/workflows/pr698-trigger-finalize-v4.yml',
  '.github/workflows/pr698-workspace-navigation-diagnostic.yml'
];
for (const relative of temporaryArtifacts) {
  if (fs.existsSync(path.join(root, relative))) {
    throw new Error(`Temporary PR #698 publication artifact remains: ${relative}`);
  }
}

console.log('VIEW_AS_MODULES_055C_DOCUMENT_CONTINUITY=PASSED');
